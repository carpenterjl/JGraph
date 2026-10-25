using System.Runtime.InteropServices;
using System.Text;

namespace JGraph.Runtime.Native;

/// <summary>
/// mxArrays read into <see cref="RuntimeValue"/>s and made from them (compiler and SDK plan, stage
/// K10): the conversion at the call boundary of jgrt.dll's mclFeval. An mxArray's memory is native
/// and its caller's; it is read through jgrt's <c>jgrtStorage</c>, which answers the storage as it
/// is (either complex layout), and made through jgrt's own mx functions, so it is freed with
/// mxDestroyArray like any other. What the engine keeps and C cannot hold (an object, a string, a
/// function handle, a table) goes out as a reference to its <see cref="RuntimeValue"/>, as R2025b's
/// runtime hands such values to C (class 48, 1-by-1, its class name; record-k10.ps1, k10_feval).
/// </summary>
/// <remarks>
/// What R2025b passes and JGraph cannot hold is refused with a message, not changed: a complex
/// integer array, a complex sparse array, an int64 or uint64 past 2^53, two fields of one name.
/// A sparse logical array comes in as JGraph's sparse double, as the engine's own sparse(true) does.
/// </remarks>
internal static unsafe class NativeArrays
{
    private const int CellClass = 1;
    private const int StructClass = 2;
    private const int LogicalClass = 3;
    private const int CharClass = 4;
    private const int DoubleClass = 6;
    private const int ReferenceClass = 48;

    private static readonly string[] NumericClasses = [.. RuntimeNumeric.Classes];

    private static delegate* unmanaged<nint, Storage*, int> _storage;
    private static delegate* unmanaged<nuint, nuint*, int, int, nint> _createNumericArray;
    private static delegate* unmanaged<nuint, nuint*, nint> _createCharArray;
    private static delegate* unmanaged<nuint, nuint*, nint> _createCellArray;
    private static delegate* unmanaged<nuint, nuint*, int, byte**, nint> _createStructArray;
    private static delegate* unmanaged<nuint, nuint, nuint, int, nint> _createSparse;
    private static delegate* unmanaged<nint, nuint, nint, void> _setCell;
    private static delegate* unmanaged<nint, nuint, int, nint, void> _setFieldByNumber;
    private static delegate* unmanaged<nint, void> _destroyArray;
    private static delegate* unmanaged<nint, byte*, nint> _createReference;
    private static delegate* unmanaged<nint, int, char*, nuint, int> _callHandler;

    /// <summary>jgrt's <c>JgrtStorage</c>, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Storage
    {
        public int ClassId;
        public int Flags;
        public ulong NDims;
        public nuint* Dims;
        public void* Data;
        public void* Imag;
        public nuint* Ir;
        public nuint* Jc;
        public ulong Nzmax;
        public int NFields;
        public int Reserved;
        public byte** FieldNames;
        public nint Handle;
        public byte* ClassName;
    }

    /// <summary>Finds jgrt's functions in <paramref name="module"/>.</summary>
    internal static void Bind(nint module)
    {
        _storage = (delegate* unmanaged<nint, Storage*, int>)NativeLibrary.GetExport(module, "jgrtStorage");
        _createNumericArray = (delegate* unmanaged<nuint, nuint*, int, int, nint>)NativeLibrary.GetExport(module, "mxCreateNumericArray");
        _createCharArray = (delegate* unmanaged<nuint, nuint*, nint>)NativeLibrary.GetExport(module, "mxCreateCharArray");
        _createCellArray = (delegate* unmanaged<nuint, nuint*, nint>)NativeLibrary.GetExport(module, "mxCreateCellArray");
        _createStructArray = (delegate* unmanaged<nuint, nuint*, int, byte**, nint>)NativeLibrary.GetExport(module, "mxCreateStructArray");
        _createSparse = (delegate* unmanaged<nuint, nuint, nuint, int, nint>)NativeLibrary.GetExport(module, "mxCreateSparse");
        _setCell = (delegate* unmanaged<nint, nuint, nint, void>)NativeLibrary.GetExport(module, "mxSetCell");
        _setFieldByNumber = (delegate* unmanaged<nint, nuint, int, nint, void>)NativeLibrary.GetExport(module, "mxSetFieldByNumber");
        _destroyArray = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(module, "mxDestroyArray");
        _createReference = (delegate* unmanaged<nint, byte*, nint>)NativeLibrary.GetExport(module, "jgrtCreateReference");
        _callHandler = (delegate* unmanaged<nint, int, char*, nuint, int>)NativeLibrary.GetExport(module, "jgrtCallHandler");
    }

    /// <summary>Hands <paramref name="text"/> to a C handler, or to standard output (1) or error (2) when there is none.</summary>
    internal static void CallHandler(nint handler, int stream, string text)
    {
        fixed (char* chars = text)
        {
            _callHandler(handler, stream, chars, (nuint)text.Length);
        }
    }

    /// <summary>Destroys an array jgrt made.</summary>
    internal static void Destroy(nint array)
    {
        if (array != 0)
        {
            _destroyArray(array);
        }
    }

    /// <summary>The value <paramref name="array"/> holds; a NULL cell element or field is <c>[]</c>.</summary>
    /// <exception cref="ArgumentException">The array holds what JGraph cannot hold.</exception>
    internal static RuntimeValue Read(nint array)
    {
        if (array == 0)
        {
            return RuntimeValue.Empty();
        }

        Storage s;
        _storage(array, &s);
        int[] dims = new int[(int)s.NDims];
        for (int k = 0; k < dims.Length; k++)
        {
            dims[k] = checked((int)s.Dims[k]);
        }

        int count = 1;
        foreach (int d in dims)
        {
            count = checked(count * d);
        }

        bool complex = (s.Flags & 1) != 0;
        bool separate = (s.Flags & 2) != 0;
        bool sparse = (s.Flags & 4) != 0;
        switch (s.ClassId)
        {
            case ReferenceClass:
                return GCHandle.FromIntPtr(s.Handle).Target as RuntimeValue
                    ?? throw new ArgumentException("An mxArray refers to a value that no longer exists.");

            case CellClass:
            {
                var elements = new RuntimeValue[count];
                nint* slots = (nint*)s.Data;
                for (int i = 0; i < count; i++)
                {
                    elements[i] = Read(slots == null ? 0 : slots[i]);
                }

                return new RuntimeCell(elements, dims);
            }

            case StructClass:
            {
                var names = new string[s.NFields];
                for (int f = 0; f < names.Length; f++)
                {
                    names[f] = Marshal.PtrToStringUTF8((nint)s.FieldNames[f]) ?? string.Empty;
                }

                if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                {
                    throw new ArgumentException(
                        $"A struct passed in has the field name '{names.GroupBy(static n => n).First(static g => g.Count() > 1).Key}' twice; "
                        + "JGraph's structs, like MATLAB's, hold each name once.");
                }

                nint* slots = (nint*)s.Data;
                var elements = new IReadOnlyDictionary<string, RuntimeValue>[count];
                for (int i = 0; i < count; i++)
                {
                    var fields = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
                    for (int f = 0; f < names.Length; f++)
                    {
                        fields[names[f]] = Read(slots == null ? 0 : slots[(i * names.Length) + f]);
                    }

                    elements[i] = fields;
                }

                return new RuntimeStruct(names, elements, dims);
            }

            case CharClass:
                return new RuntimeChar(count == 0 || s.Data == null ? string.Empty : new string((char*)s.Data, 0, count), dims);

            case LogicalClass when sparse:
                return Sparse(s, dims, logical: true);

            case LogicalClass:
            {
                var bits = new bool[count];
                byte* data = (byte*)s.Data;
                for (int i = 0; i < count; i++)
                {
                    bits[i] = data != null && data[i] != 0;
                }

                return new RuntimeLogical(bits, dims);
            }

            case >= DoubleClass and <= DoubleClass + 9:
            {
                string className = RuntimeNumeric.Classes[s.ClassId - DoubleClass];
                if (complex && className is not ("double" or "single"))
                {
                    throw new ArgumentException($"JGraph keeps no complex {className} array: pass it as a complex double or single one.");
                }

                if (sparse)
                {
                    return complex
                        ? throw new ArgumentException("JGraph has no complex sparse arrays: pass the real and imaginary parts apart.")
                        : Sparse(s, dims, logical: false);
                }

                var real = new double[count];
                double[]? imaginary = complex ? new double[count] : null;
                for (int i = 0; i < count && s.Data != null; i++)
                {
                    if (!complex)
                    {
                        real[i] = Element(s.ClassId, s.Data, i);
                    }
                    else if (separate)
                    {
                        real[i] = Element(s.ClassId, s.Data, i);
                        imaginary![i] = s.Imag == null ? 0 : Element(s.ClassId, s.Imag, i);
                    }
                    else
                    {
                        real[i] = Element(s.ClassId, s.Data, 2 * i);
                        imaginary![i] = Element(s.ClassId, s.Data, (2 * i) + 1);
                    }
                }

                return new RuntimeNumeric(className, real, imaginary, dims);
            }

            default:
                throw new ArgumentException($"An mxArray of class {s.ClassId} cannot be passed to a component.");
        }
    }

    /// <summary>A new array holding <paramref name="value"/>, the caller's to destroy.</summary>
    internal static nint Make(RuntimeValue value)
    {
        nuint[] dims = [.. value.Dimensions.Select(static d => (nuint)d)];
        fixed (nuint* shape = dims)
        {
            switch (value)
            {
                case RuntimeNumeric numeric:
                {
                    int classId = DoubleClass + Array.IndexOf(NumericClasses, numeric.ClassName);
                    nint array = _createNumericArray((nuint)dims.Length, shape, classId, numeric.IsComplex ? 1 : 0);
                    Storage s;
                    _storage(array, &s);
                    IReadOnlyList<double> real = numeric.Real;
                    IReadOnlyList<double>? imaginary = numeric.Imaginary;
                    for (int i = 0; i < numeric.Count; i++)
                    {
                        if (imaginary is null)
                        {
                            Store(classId, s.Data, i, real[i]);
                        }
                        else
                        {
                            Store(classId, s.Data, 2 * i, real[i]);
                            Store(classId, s.Data, (2 * i) + 1, imaginary[i]);
                        }
                    }

                    return array;
                }

                case RuntimeLogical logical:
                {
                    nint array = _createNumericArray((nuint)dims.Length, shape, LogicalClass, 0);
                    Storage s;
                    _storage(array, &s);
                    for (int i = 0; i < logical.Count; i++)
                    {
                        ((byte*)s.Data)[i] = logical.Elements[i] ? (byte)1 : (byte)0;
                    }

                    return array;
                }

                case RuntimeChar text:
                {
                    nint array = _createCharArray((nuint)dims.Length, shape);
                    Storage s;
                    _storage(array, &s);
                    string characters = text.Characters;
                    for (int i = 0; i < characters.Length; i++)
                    {
                        ((char*)s.Data)[i] = characters[i];
                    }

                    return array;
                }

                case RuntimeCell cell:
                {
                    nint array = _createCellArray((nuint)dims.Length, shape);
                    for (int i = 0; i < cell.Count; i++)
                    {
                        _setCell(array, (nuint)i, Make(cell.Elements[i]));
                    }

                    return array;
                }

                case RuntimeStruct record:
                    return Struct(record, dims.Length, shape);

                case RuntimeSparse sparse:
                {
                    int stored = sparse.Values.Count;
                    nint array = _createSparse((nuint)sparse.Dimensions[0], (nuint)sparse.Dimensions[1], (nuint)Math.Max(stored, 1), 0);
                    Storage s;
                    _storage(array, &s);
                    for (int c = 0; c < sparse.ColumnStarts.Count; c++)
                    {
                        s.Jc[c] = (nuint)sparse.ColumnStarts[c];
                    }

                    for (int k = 0; k < stored; k++)
                    {
                        s.Ir[k] = (nuint)sparse.RowIndices[k];
                        ((double*)s.Data)[k] = sparse.Values[k];
                    }

                    return array;
                }

                default:
                    return Reference(value);
            }
        }
    }

    /// <summary>A reference to <paramref name="value"/>, which C holds by the handle it carries.</summary>
    internal static nint Reference(RuntimeValue value)
    {
        nint handle = GCHandle.ToIntPtr(GCHandle.Alloc(value));
        byte[] name = Encoding.UTF8.GetBytes(value.ClassName + "\0");
        fixed (byte* className = name)
        {
            return _createReference(handle, className);
        }
    }

    private static nint Struct(RuntimeStruct record, int ndims, nuint* shape)
    {
        IReadOnlyList<string> names = record.FieldNames;
        byte[][] bytes = [.. names.Select(static n => Encoding.UTF8.GetBytes(n + "\0"))];
        var pinned = new GCHandle[bytes.Length];
        var pointers = new byte*[bytes.Length + 1];
        try
        {
            for (int f = 0; f < bytes.Length; f++)
            {
                pinned[f] = GCHandle.Alloc(bytes[f], GCHandleType.Pinned);
                pointers[f] = (byte*)pinned[f].AddrOfPinnedObject();
            }

            nint array;
            fixed (byte** list = pointers)
            {
                array = _createStructArray((nuint)ndims, shape, names.Count, list);
            }

            for (int i = 0; i < record.Count; i++)
            {
                for (int f = 0; f < names.Count; f++)
                {
                    _setFieldByNumber(array, (nuint)i, f, Make(record.Elements[i][names[f]]));
                }
            }

            return array;
        }
        finally
        {
            foreach (GCHandle handle in pinned)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
    }

    private static RuntimeSparse Sparse(Storage s, int[] dims, bool logical)
    {
        int rows = dims[0], columns = dims[1];
        var starts = new int[columns + 1];
        for (int c = 0; c <= columns; c++)
        {
            starts[c] = checked((int)s.Jc[c]);
        }

        int stored = starts[columns];
        var indices = new int[stored];
        var values = new double[stored];
        for (int k = 0; k < stored; k++)
        {
            indices[k] = checked((int)s.Ir[k]);
            values[k] = logical ? (((byte*)s.Data)[k] != 0 ? 1 : 0) : ((double*)s.Data)[k];
        }

        return new RuntimeSparse(rows, columns, indices, starts, values);
    }

    private static double Element(int classId, void* data, int i) => classId switch
    {
        6 => ((double*)data)[i],
        7 => ((float*)data)[i],
        8 => ((sbyte*)data)[i],
        9 => ((byte*)data)[i],
        10 => ((short*)data)[i],
        11 => ((ushort*)data)[i],
        12 => ((int*)data)[i],
        13 => ((uint*)data)[i],
        14 => Exact(((long*)data)[i], "int64"),
        _ => Exact(((ulong*)data)[i], "uint64"),
    };

    // JGraph keeps a 64-bit integer as a double, exact to 2^53 (the interop plan's
    // JGraph:interop:int64Precision): one past that is refused, not rounded.
    private static double Exact(long x, string className) => x is >= -(1L << 53) and <= 1L << 53
        ? x
        : throw new ArgumentException($"{x} cannot be held exactly: JGraph keeps an {className} as a double, exact to 2^53.");

    private static double Exact(ulong x, string className) => x <= 1UL << 53
        ? x
        : throw new ArgumentException($"{x} cannot be held exactly: JGraph keeps a {className} as a double, exact to 2^53.");

    private static void Store(int classId, void* data, int i, double x)
    {
        switch (classId)
        {
            case 6: ((double*)data)[i] = x; break;
            case 7: ((float*)data)[i] = (float)x; break;
            case 8: ((sbyte*)data)[i] = (sbyte)x; break;
            case 9: ((byte*)data)[i] = (byte)x; break;
            case 10: ((short*)data)[i] = (short)x; break;
            case 11: ((ushort*)data)[i] = (ushort)x; break;
            case 12: ((int*)data)[i] = (int)x; break;
            case 13: ((uint*)data)[i] = (uint)x; break;
            case 14: ((long*)data)[i] = (long)x; break;
            default: ((ulong*)data)[i] = (ulong)x; break;
        }
    }
}
