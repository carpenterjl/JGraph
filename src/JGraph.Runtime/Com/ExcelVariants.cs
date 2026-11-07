using System.Runtime.InteropServices;

namespace JGraph.Runtime.Com;

/// <summary>A VARIANT as COM lays it out: the type word, then eight bytes of value at offset 8.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct Variant
{
    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public nint Pointer;
    [FieldOffset(8)] public int I4;
    [FieldOffset(8)] public long I8;
    [FieldOffset(8)] public short I2;
    [FieldOffset(8)] public double R8;
}

/// <summary>The arguments of one member call, as jgrt hands them over: a pointer per declared parameter.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed unsafe class ComArguments(Variant** argv, int count)
{
    /// <summary>How many parameters were passed.</summary>
    public int Count { get; } = count;

    /// <summary>The <paramref name="index"/>th argument, or null past the end.</summary>
    public Variant* At(int index) => index >= 0 && index < Count && argv != null ? argv[index] : null;
}

/// <summary>
/// VARIANTs read into <see cref="RuntimeValue"/>s and written from them at an Excel add-in's call
/// boundary (compiler and SDK plan, stage K17c), by the rules stage K17a measured R2025b's add-in
/// following (<c>recordings/k17_com.out.txt</c>: in.*, out.*, pass.*): every scalar type to its
/// MATLAB class, a BSTR a char row, a DATE a datenum, a SAFEARRAY a matrix of its element class or,
/// for Variants, a matrix when the elements are numbers of one class, a logical array when they
/// are booleans, a char matrix when each is one character and a cell otherwise; and back, a value's
/// class to the VARIANT type it came from, a matrix to a SAFEARRAY, a cell to one of Variants, an
/// empty value and NaN (under the default flags) to nothing. The flags' effect, Range objects and
/// MathWorks' value classes (MWStruct, MWComplex, MWSparse, MWArg) are stage K17d's; what they
/// would carry is refused here with a message.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe class ExcelVariants
{
    public const ushort VtEmpty = 0;
    public const ushort VtNull = 1;
    public const ushort VtI2 = 2;
    public const ushort VtI4 = 3;
    public const ushort VtR4 = 4;
    public const ushort VtR8 = 5;
    public const ushort VtCy = 6;
    public const ushort VtDate = 7;
    public const ushort VtBstr = 8;
    public const ushort VtDispatch = 9;
    public const ushort VtError = 10;
    public const ushort VtBool = 11;
    public const ushort VtVariant = 12;
    public const ushort VtUnknown = 13;
    public const ushort VtDecimal = 14;
    public const ushort VtI1 = 16;
    public const ushort VtUI1 = 17;
    public const ushort VtUI2 = 18;
    public const ushort VtUI4 = 19;
    public const ushort VtI8 = 20;
    public const ushort VtUI8 = 21;
    public const ushort VtInt = 22;
    public const ushort VtUInt = 23;
    public const ushort VtArray = 0x2000;
    public const ushort VtByRef = 0x4000;

    /// <summary>DISP_E_PARAMNOTFOUND: a missing optional argument, which a VBA caller leaves out and a .NET one passes as Type.Missing.</summary>
    public const int ParamNotFound = unchecked((int)0x80020004);

    /// <summary>The days from MATLAB's datenum epoch to the OLE date's (30 December 1899), the default DateBias.</summary>
    public const int DateBias = 693960;

    /// <summary>The VARIANT a byref chain of VARIANTs ends at.</summary>
    public static Variant* Deref(Variant* v)
    {
        while (v != null && v->Vt == (VtByRef | VtVariant) && v->Pointer != 0)
        {
            v = (Variant*)v->Pointer;
        }

        return v;
    }

    /// <summary>
    /// Whether an argument was not given: VT_EMPTY, or the VT_ERROR a missing optional argument
    /// arrives as. R2025b takes a trailing one as not passed and refuses one in the middle (k17_com:
    /// opt.emptylast, opt.middle, both.missingvarargin).
    /// </summary>
    public static bool IsMissing(Variant* v)
    {
        v = Deref(v);
        if (v == null)
        {
            return true;
        }

        ushort vt = (ushort)(v->Vt & ~VtByRef);
        if (vt == VtEmpty)
        {
            return true;
        }

        if (vt == VtError)
        {
            int code = (v->Vt & VtByRef) != 0 ? *(int*)v->Pointer : v->I4;
            return code == ParamNotFound;
        }

        return false;
    }

    /// <summary>Reads an argument as the engine's value, by the measured rules.</summary>
    /// <exception cref="ComCallException">The value cannot cross yet (an object, a Range) or at all.</exception>
    public static RuntimeValue Read(Variant* v, ComServed owner)
    {
        v = Deref(v);
        if (v == null)
        {
            return RuntimeValue.Empty();
        }

        ushort vt = (ushort)(v->Vt & ~VtByRef);
        bool byRef = (v->Vt & VtByRef) != 0;
        switch (vt)
        {
            case VtEmpty:
                return RuntimeValue.Empty();
            case VtNull:
                // in.dbnull: a DBNull is [].
                return RuntimeValue.Empty();
            case VtError:
                // in.err_na: an Excel error cell is its int32 code.
                return RuntimeNumeric.Int32([byRef ? *(int*)v->Pointer : v->I4], 1, 1);
            case VtDispatch:
            case VtUnknown:
            {
                nint pointer = byRef ? *(nint*)v->Pointer : v->Pointer;
                if (pointer == 0)
                {
                    return RuntimeValue.Empty();
                }

                if (ComObjects.Of(Native.NativeComCalls.Unwrap(pointer)) is { } ours)
                {
                    string kind = ours is ExcelComponent ? "n add-in" : " " + ours.GetType().Name.Replace("Excel", "MW", StringComparison.Ordinal);
                    throw new ComCallException(ComCallException.Fail, owner.ProgId, $"A{kind} object cannot be passed to a function.");
                }

                throw new ComCallException(ComCallException.Fail, owner.ProgId,
                    "An object (an Excel Range, or another COM object) cannot be passed to a function yet: pass its Value2 (compiler and SDK plan, stage K17d).");
            }
        }

        object? value = Marshal.GetObjectForNativeVariant((IntPtr)v);
        return value is Array array ? FromArray(array, topLevel: true, owner) : FromScalar(value, owner);
    }

    private static RuntimeValue FromScalar(object? value, ComServed owner) => value switch
    {
        null => RuntimeValue.Empty(),
        DBNull => RuntimeValue.Empty(),
        System.Reflection.Missing => RuntimeNumeric.Int32([ParamNotFound], 1, 1),
        double d => RuntimeValue.Scalar(d),
        float f => RuntimeNumeric.Single([f], 1, 1),
        int i => RuntimeNumeric.Int32([i], 1, 1),
        short s => RuntimeNumeric.Int16([s], 1, 1),
        sbyte b => RuntimeNumeric.Int8([b], 1, 1),
        byte b => RuntimeNumeric.UInt8([b], 1, 1),
        ushort u => RuntimeNumeric.UInt16([u], 1, 1),
        uint u => RuntimeNumeric.UInt32([u], 1, 1),
        long l => RuntimeNumeric.Int64([l], 1, 1),
        ulong l => RuntimeNumeric.UInt64([l], 1, 1),
        bool t => RuntimeValue.Scalar(t),
        char c => RuntimeNumeric.UInt16([c], 1, 1),
        string text => text.Length == 0 ? new RuntimeChar("", [0, 0]) : RuntimeChar.Row(text),
        DateTime date => RuntimeValue.Scalar(date.ToOADate() + DateBias),
        decimal m => RuntimeValue.Scalar((double)m),
        _ => throw new ComCallException(ComCallException.Fail, owner.ProgId, $"A {value.GetType().Name} cannot be passed to a function."),
    };

    /// <summary>
    /// A SAFEARRAY: a one-dimensional one is a row (in.arr_r8), others keep their shape; an array
    /// of Variants is classified by its elements at the top level and is a cell when nested
    /// (in.arr_var_numbers, in.arr_jagged).
    /// </summary>
    private static RuntimeValue FromArray(Array array, bool topLevel, ComServed owner)
    {
        int rank = array.Rank;
        int[] dims = rank == 1 ? [1, array.GetLength(0)] : [.. Enumerable.Range(0, rank).Select(array.GetLength)];
        int count = 1;
        foreach (int d in dims)
        {
            count *= d;
        }

        var lower = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            lower[d] = array.GetLowerBound(d);
        }

        // The elements in MATLAB's column-major order: the first subscript fastest.
        var elements = new object?[count];
        var subs = new int[rank];
        for (int k = 0; k < count; k++)
        {
            int rest = k;
            for (int d = 0; d < rank; d++)
            {
                int extent = rank == 1 ? dims[1] : dims[d];
                subs[d] = rest % extent + lower[d];
                rest /= extent;
            }

            elements[k] = array.GetValue(subs);
        }

        Type element = array.GetType().GetElementType()!;
        if (element == typeof(double) || element == typeof(float) || element == typeof(int) || element == typeof(short) || element == typeof(sbyte)
            || element == typeof(byte) || element == typeof(ushort) || element == typeof(uint) || element == typeof(long) || element == typeof(ulong)
            || element == typeof(decimal) || element == typeof(DateTime) || element == typeof(char))
        {
            return NumericOf(elements, ClassOf(element), dims);
        }

        if (element == typeof(bool))
        {
            return new RuntimeLogical([.. elements.Select(static e => (bool)e!)], dims);
        }

        // Variants (and BSTRs): each element on its own, then the shape they make together.
        var values = new RuntimeValue?[count];
        var classes = new HashSet<string>(StringComparer.Ordinal);
        bool allSingleChars = true;
        bool allLogical = true;
        for (int k = 0; k < count; k++)
        {
            object? e = elements[k];
            if (e is null || e is DBNull)
            {
                values[k] = null;
                continue;
            }

            RuntimeValue converted = e is Array inner ? FromArray(inner, topLevel: false, owner) : FromScalar(e, owner);
            values[k] = converted;
            if (converted is RuntimeNumeric { Count: 1 } n && n.Dimensions.Count == 2)
            {
                classes.Add(n.ClassName);
            }
            else
            {
                classes.Add("*");
            }

            allLogical &= converted is RuntimeLogical { Count: 1 };
            allSingleChars &= converted is RuntimeChar { Count: 1 };
        }

        int given = values.Count(static v => v is not null);
        if (topLevel && (given == 0 || (classes.Count == 1 && !classes.Contains("*"))))
        {
            // in.arr_var_numbers, in.arr_var_with_empty: numbers of one class, an empty element 0.
            string cls = given == 0 ? "double" : classes.First();
            return Numeric([.. values.Select(static v => v is RuntimeNumeric n ? n.Real[0] : 0.0)], cls, dims);
        }

        if (topLevel && given == count && allLogical)
        {
            return new RuntimeLogical([.. values.Select(static v => ((RuntimeLogical)v!).Elements[0])], dims);
        }

        if (topLevel && given == count && allSingleChars)
        {
            // in.arr_str_2d: one character each makes a char matrix.
            return new RuntimeChar(string.Concat(values.Select(static v => ((RuntimeChar)v!).Characters)), dims);
        }

        return new RuntimeCell([.. values.Select(static v => v ?? RuntimeValue.Empty())], dims);
    }

    private static string ClassOf(Type element) =>
        element == typeof(float) ? "single"
        : element == typeof(int) ? "int32"
        : element == typeof(short) ? "int16"
        : element == typeof(sbyte) ? "int8"
        : element == typeof(byte) ? "uint8"
        : element == typeof(ushort) || element == typeof(char) ? "uint16"
        : element == typeof(uint) ? "uint32"
        : element == typeof(long) ? "int64"
        : element == typeof(ulong) ? "uint64"
        : "double";

    private static RuntimeNumeric NumericOf(IEnumerable<object?> elements, string cls, int[] dims) =>
        Numeric([.. elements.Select(static e => e switch
        {
            null or DBNull => 0.0,
            DateTime date => date.ToOADate() + DateBias,
            decimal m => (double)m,
            char c => c,
            IConvertible c => c.ToDouble(System.Globalization.CultureInfo.InvariantCulture),
            _ => 0.0,
        })], cls, dims);

    private static RuntimeNumeric Numeric(double[] values, string cls, int[] dims) => new(cls, values, null, dims);

    // ---- Out ---------------------------------------------------------------------------------

    /// <summary>Writes a member's answer into <paramref name="target"/>: a value, a served object, text, a number or nothing.</summary>
    public static void WriteResult(object? answer, Variant* target, ComServed owner)
    {
        if (target == null)
        {
            return;
        }

        VariantClear(target);
        switch (answer)
        {
            case null:
                return;
            case ComServed served:
                target->Pointer = ComObjects.Wrap(served);
                target->Vt = VtDispatch;
                return;
            case RuntimeValue value:
                Write(value, target, owner);
                return;
            default:
                Marshal.GetNativeVariantForObject(answer, (IntPtr)target);
                return;
        }
    }

    /// <summary>Writes <paramref name="value"/> into <paramref name="target"/>, cleared first.</summary>
    public static void Write(RuntimeValue value, Variant* target, ComServed owner)
    {
        object? made = ToDotNet(value, owner);
        VariantClear(target);
        if (made is not null)
        {
            Marshal.GetNativeVariantForObject(made, (IntPtr)target);
        }
    }

    /// <summary>
    /// Writes an output into the slot a client gave for it: through a byref VARIANT (what VBA and
    /// a .NET [ref] pass), into a byref scalar by conversion, and nowhere for a slot passed by
    /// value (k17_com: err.slotnotbyref answers nothing and no error).
    /// </summary>
    public static void WriteSlot(Variant* slot, RuntimeValue value, ComServed owner)
    {
        if (slot == null || (slot->Vt & VtByRef) == 0)
        {
            return;
        }

        if ((slot->Vt & ~VtByRef) == VtVariant)
        {
            Write(value, (Variant*)slot->Pointer, owner);
            return;
        }

        Variant made;
        made.Vt = VtEmpty;
        made.I8 = 0;
        Write(value, &made, owner);
        ushort wanted = (ushort)(slot->Vt & ~VtByRef);
        try
        {
            if (made.Vt != wanted)
            {
                int hr = VariantChangeType(&made, &made, 0, wanted);
                if (hr < 0)
                {
                    throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId,
                        $"An output of class {value.ClassName} cannot be stored in the variable given for it.");
                }
            }

            switch (wanted)
            {
                case VtR8: *(double*)slot->Pointer = made.R8; break;
                case VtI4: case VtInt: case VtUI4: case VtUInt: case VtError: *(int*)slot->Pointer = made.I4; break;
                case VtI2: case VtUI2: case VtBool: *(short*)slot->Pointer = made.I2; break;
                case VtI8: case VtUI8: case VtDate: case VtCy: *(long*)slot->Pointer = made.I8; break;
                case VtBstr: case VtDispatch: case VtUnknown:
                    *(nint*)slot->Pointer = made.Pointer;
                    made.Vt = VtEmpty;
                    break;
                default:
                    throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId, "An output cannot be stored in the variable given for it.");
            }
        }
        finally
        {
            VariantClear(&made);
        }
    }

    /// <summary>The .NET object the COM marshaller turns into the VARIANT R2025b's add-in answers; null for VT_EMPTY.</summary>
    /// <exception cref="ComCallException">The value's class is stage K17d's (struct, complex, sparse) or cannot cross.</exception>
    private static object? ToDotNet(RuntimeValue value, ComServed owner)
    {
        int[] dims = [.. value.Dimensions];
        switch (value)
        {
            case RuntimeNumeric numeric:
            {
                if (numeric.Imaginary is not null)
                {
                    throw Pending(owner, "A complex value", "an MWComplex object");
                }

                if (numeric.Count == 0)
                {
                    // out.empty, out.emptyrow: an empty value is nothing.
                    return null;
                }

                bool anyNaN = numeric.ClassName is "double" or "single" && numeric.Real.Any(double.IsNaN);
                if (numeric.Count == 1 && IsScalar(dims))
                {
                    // out.nan: NaN is nothing under the default ReplaceMatlabNaN; out.doubleneg0: -0 is 0.
                    return anyNaN ? null : Scalar(numeric.ClassName, numeric.Real[0]);
                }

                if (anyNaN)
                {
                    // out.nanmat: a matrix with NaN becomes Variants, nothing where the NaN was.
                    return Fill(typeof(object), dims, k => double.IsNaN(numeric.Real[k]) ? null : Scalar(numeric.ClassName, numeric.Real[k]));
                }

                Type element = ElementType(numeric.ClassName);
                return Fill(element, dims, k => Scalar(numeric.ClassName, numeric.Real[k]));
            }

            case RuntimeLogical logical:
                if (logical.Count == 0)
                {
                    return null;
                }

                return logical.Count == 1 && IsScalar(dims) ? logical.Elements[0] : Fill(typeof(bool), dims, k => logical.Elements[k]);

            case RuntimeChar text:
            {
                if (text.Count == 0)
                {
                    // out.charempty: '' is nothing.
                    return null;
                }

                if (dims.Length == 2 && dims[0] == 1)
                {
                    return text.ToString();
                }

                // out.charmat: a char matrix is one string per character, in its shape.
                string all = text.Characters;
                return Fill(typeof(string), dims, k => all[k].ToString());
            }

            case RuntimeString strings:
                // R2025b answers a type mismatch for every string; JGraph answers the text (ADR 0316).
                if (strings.Count == 1 && IsScalar(dims))
                {
                    return strings.Elements[0] ?? "";
                }

                return strings.Count == 0 ? null : Fill(typeof(string), dims, k => strings.Elements[k] ?? "");

            case RuntimeCell cell:
                // out.cell, out.cellempty: a cell is Variants in its shape, an empty one an empty array, not nothing.
                return Fill(typeof(object), dims, k => ToDotNet(cell.Elements[k], owner));

            case RuntimeStruct:
                throw Pending(owner, "A struct", "an MWStruct object");
            case RuntimeSparse:
                throw Pending(owner, "A sparse matrix", "an MWSparse object");
            default:
                throw new ComCallException(ComCallException.Fail, owner.ProgId,
                    $"A {value.ClassName} value cannot be returned to a COM client; return a number, text, a logical or a cell of them.");
        }
    }

    private static ComCallException Pending(ComServed owner, string what, string becomes) =>
        new(ComCallException.Fail, owner.ProgId, $"{what} cannot be returned to a COM client yet: it becomes {becomes} in stage K17d of the compiler and SDK plan.");

    private static bool IsScalar(int[] dims) => dims.All(static d => d == 1);

    private static Type ElementType(string cls) => cls switch
    {
        "single" => typeof(float),
        "int8" => typeof(sbyte),
        "uint8" => typeof(byte),
        "int16" => typeof(short),
        "uint16" => typeof(ushort),
        "int32" => typeof(int),
        "uint32" => typeof(uint),
        "int64" => typeof(long),
        "uint64" => typeof(ulong),
        _ => typeof(double),
    };

    /// <summary>A scalar in the .NET type its class marshals to (out.single, out.int8, out.uint32: the VARIANT type of the class).</summary>
    private static object Scalar(string cls, double x) => cls switch
    {
        "single" => (object)(float)x,
        "int8" => (object)(sbyte)x,
        "uint8" => (object)(byte)x,
        "int16" => (object)(short)x,
        "uint16" => (object)(ushort)x,
        "int32" => (object)(int)x,
        "uint32" => (object)(uint)x,
        "int64" => (object)(long)x,
        "uint64" => (object)(ulong)x,
        _ => (object)(x == 0 ? 0.0 : x),
    };

    /// <summary>
    /// An array of <paramref name="element"/> in MATLAB's shape, element k (column-major) from
    /// <paramref name="at"/>, with every lower bound 1: the SAFEARRAYs R2025b's add-in answers are
    /// one-based, as VBA's are (k17_com: out.doublerow [1..1,1..3], out.cellempty [1..0,1..0]).
    /// </summary>
    private static Array Fill(Type element, int[] dims, Func<int, object?> at)
    {
        int[] shape = dims.Length >= 2 ? dims : [1, dims.Length == 1 ? dims[0] : 0];
        var lower = new int[shape.Length];
        Array.Fill(lower, 1);
        Array made = Array.CreateInstance(element, shape, lower);
        int count = 1;
        foreach (int d in shape)
        {
            count *= d;
        }

        var subs = new int[shape.Length];
        for (int k = 0; k < count; k++)
        {
            int rest = k;
            for (int d = 0; d < shape.Length; d++)
            {
                subs[d] = rest % shape[d] + 1;
                rest /= shape[d];
            }

            made.SetValue(at(k), subs);
        }

        return made;
    }

    [DllImport("oleaut32.dll")]
    public static extern int VariantClear(Variant* variant);

    [DllImport("oleaut32.dll")]
    private static extern int VariantChangeType(Variant* destination, Variant* source, ushort flags, ushort vt);
}
