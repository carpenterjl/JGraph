using System.Globalization;
using System.Runtime.InteropServices;
using JGraph.Deploy;

namespace JGraph.Runtime.Com;

/// <summary>
/// MathWorks' value classes for a COM client (compiler and SDK plan, stage K17d): <c>MWStruct</c>
/// and its <c>MWField</c>s, <c>MWComplex</c>, <c>MWSparse</c> and <c>MWArg</c>, served by JGraph's COM
/// utility library (<c>JGraphComUtil.*</c>, decision KO10) and made by a component for the struct,
/// complex and sparse values it answers. Each holds what the client gave it as the COM marshaller
/// read it, or what the engine answered converted for the client, and each has its own
/// <c>MWFlags</c>; the conversion of what they hold is <see cref="ExcelConversion"/>'s, by the rules
/// stage K17d measured (k17_com_extra: x.arg.*, x.struct.*, x.complex.*, x.sparse.*, x.field.*).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal abstract class ExcelValueObject : ComServed
{
    protected ExcelValueObject(string className, nint module)
        : base(module, ExcelComGuids.UtilityVersionedProgId(className))
    {
        Flags = new ExcelFlags(module, ProgId);
        Adopt(Flags);
    }

    /// <summary>The object's <c>MWFlags</c>: its own until a client puts another flags object.</summary>
    public ExcelFlags Flags { get; private set; }

    /// <summary>The value of a VARIANT argument as the marshaller reads it; a served object stays itself, a missing value is null.</summary>
    protected static object? Given(ComArguments arguments, int index)
    {
        object? value = arguments.Value(index);
        if (value is not null && Marshal.IsComObject(value))
        {
            nint punk = Marshal.GetIUnknownForObject(value);
            try
            {
                if (ComObjects.Of(Native.NativeComCalls.Unwrap(punk)) is { } ours)
                {
                    return ours;
                }
            }
            finally
            {
                Marshal.Release(punk);
            }
        }

        return value;
    }

    /// <summary>A value kept in this object: a served object becomes this one's, so its wrapper lives as long as this one is served.</summary>
    protected object? Keep(object? value)
    {
        if (value is ComServed served && served.Owner is null)
        {
            Adopt(served);
        }

        return value;
    }

    /// <summary>The <c>MWFlags</c> member, get and put, shared by every value class.</summary>
    protected object? FlagsMember(ComInvokeKind kind, ComArguments arguments)
    {
        if (kind == ComInvokeKind.PropertyGet)
        {
            return Flags;
        }

        ExcelFlags given = Flags.Given<ExcelFlags>(arguments, "MWFlags");
        if (!ReferenceEquals(given, Flags))
        {
            Flags = given;
            Adopt(given);
        }

        return null;
    }

    /// <summary>Copies the flags of <paramref name="other"/> into this object's own, for a clone.</summary>
    protected void CopyFlags(ExcelValueObject other) => Flags.CopyFrom(other.Flags);
}

/// <summary>One field of an <see cref="ExcelStruct"/>, or a standalone <c>MWField</c>: a name, a value and flags.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelField : ExcelValueObject
{
    public ExcelField(nint module, string name = "")
        : base("MWField", module)
    {
        Name = name;
    }

    public override Guid InterfaceId => ExcelComGuids.FieldInterface;

    /// <summary>The field's name; '' for one made by a client (x.field.name_default).</summary>
    public string Name { get; }

    /// <summary>What the field holds, as given or converted; null when unset, which crosses as [] (x.struct.field_unset).</summary>
    public object? Value { get; set; }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "Value":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return Value;
                }

                Value = Keep(Given(arguments, 0));
                return null;
            case "Name":
                return Name;
            case "MWFlags":
                return FlagsMember(kind, arguments);
            case "Clone":
            {
                var copy = new ExcelField(Module, Name) { Value = Value };
                copy.CopyFlags(this);
                return copy;
            }

            default:
                throw NoMember(member);
        }
    }
}

/// <summary>
/// <c>MWStruct</c>: a struct array of the client's or the engine's making. <c>Initialize(dims, names)</c>
/// sets the size (a scalar n is 1-by-n, a fraction truncated, a negative refused, a zero refused with
/// field names) and adds field names (duplicates dropped, a second call adding to the first:
/// x.struct.init_twice); <c>Item(i..., name)</c> is the field <c>name</c> of the element the indices name
/// (none for a 1-by-1, one linear index, or one per dimension), the same <see cref="ExcelField"/> each
/// time, and DISP_E_BADINDEX with no text for anything else (x.struct.item_*); <c>Dims</c> and
/// <c>FieldNames</c> are one-based arrays, the names nothing when there are none.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelStruct : ExcelValueObject
{
    private readonly List<string> _names = [];
    private readonly Dictionary<(int Element, string Name), ExcelField> _fields = [];
    private int[] _dims = [1, 1];

    public ExcelStruct(nint module)
        : base("MWStruct", module)
    {
    }

    public override Guid InterfaceId => ExcelComGuids.StructInterface;

    /// <summary>The size, at least two dimensions.</summary>
    public IReadOnlyList<int> Dims => _dims;

    /// <summary>The field names, in order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>How many elements the size holds.</summary>
    public int Count => _dims.Aggregate(1, static (a, b) => a * b);

    /// <summary>The <c>MWStruct</c> the engine answers for <paramref name="record"/>, each field's value converted by <paramref name="convert"/>.</summary>
    public static ExcelStruct Of(RuntimeStruct record, ExcelFlags flags, ComServed owner, Func<RuntimeValue, object?> convert)
    {
        _ = flags;
        _ = owner;
        var made = new ExcelStruct(ExcelComUtilityLibrary.Module);
        made._dims = [.. record.Dimensions];
        made._names.AddRange(record.FieldNames);
        for (int e = 0; e < record.Elements.Count; e++)
        {
            foreach (string name in record.FieldNames)
            {
                made.Field(e, name).Value = made.Keep(convert(record.Elements[e][name]));
            }
        }

        return made;
    }

    /// <summary>The struct as the engine's value, each field (null when unset) converted by <paramref name="convert"/>.</summary>
    public RuntimeStruct ToValue(Func<ExcelField?, RuntimeValue> convert)
    {
        var elements = new List<IReadOnlyDictionary<string, RuntimeValue>>();
        for (int e = 0; e < Count; e++)
        {
            var fields = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
            foreach (string name in _names)
            {
                fields[name] = convert(_fields.GetValueOrDefault((e, name)));
            }

            elements.Add(fields);
        }

        return new RuntimeStruct(_names, elements, _dims);
    }

    private ExcelField Field(int element, string name)
    {
        if (!_fields.TryGetValue((element, name), out ExcelField? field))
        {
            field = new ExcelField(Module, name);
            Adopt(field);
            _fields[(element, name)] = field;
        }

        return field;
    }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "Item":
                return Item(arguments);
            case "Initialize":
                Initialize(Given(arguments, 0), Given(arguments, 1));
                return null;
            case "NumberOfFields":
                return _names.Count;
            case "NumberOfDims":
                return _dims.Length;
            case "Dims":
                return ExcelConversion.Fill1(typeof(int), _dims.Length, k => _dims[k]);
            case "FieldNames":
                return _names.Count == 0 ? null : ExcelConversion.Fill1(typeof(string), _names.Count, k => _names[k]);
            case "MWFlags":
                return FlagsMember(kind, arguments);
            case "Clone":
            {
                var copy = new ExcelStruct(Module) { _dims = [.. _dims] };
                copy._names.AddRange(_names);
                foreach (((int element, string name), ExcelField field) in _fields)
                {
                    copy.Field(element, name).Value = field.Value;
                    copy.Field(element, name).Flags.CopyFrom(field.Flags);
                }

                copy.CopyFlags(this);
                return copy;
            }

            default:
                throw NoMember(member);
        }
    }

    /// <summary>Item(i0, ..., name): the indices first and the field's name last (x.struct.item_index_first); anything else DISP_E_BADINDEX, an index that is no number DISP_E_TYPEMISMATCH, both with no text.</summary>
    private ExcelField Item(ComArguments arguments)
    {
        var given = new List<object?>();
        for (int k = 0; k < arguments.Count; k++)
        {
            object? value = arguments.Value(k);
            if (value is null)
            {
                break;
            }

            given.Add(value);
        }

        // The last argument must be text (else DISP_E_BADINDEX), the ones before it numbers (else
        // DISP_E_TYPEMISMATCH: x.struct.item_text_index), the text a field (else DISP_E_BADINDEX).
        if (given.Count == 0 || given[^1] is not string name)
        {
            throw ComCallException.Silent(ComCallException.BadIndex);
        }

        var indices = new List<int>();
        foreach (object? index in given.Take(given.Count - 1))
        {
            if (index is string || index is not IConvertible c)
            {
                throw ComCallException.Silent(ComCallException.TypeMismatch);
            }

            indices.Add((int)Math.Floor(c.ToDouble(CultureInfo.InvariantCulture)));
        }

        if (!_names.Contains(name))
        {
            throw ComCallException.Silent(ComCallException.BadIndex);
        }

        int element;
        if (indices.Count == 0)
        {
            if (Count != 1)
            {
                throw ComCallException.Silent(ComCallException.BadIndex);
            }

            element = 0;
        }
        else if (indices.Count == 1)
        {
            element = indices[0] - 1;
        }
        else if (indices.Count == _dims.Length)
        {
            element = 0;
            int stride = 1;
            for (int d = 0; d < _dims.Length; d++)
            {
                if (indices[d] < 1 || indices[d] > _dims[d])
                {
                    throw ComCallException.Silent(ComCallException.BadIndex);
                }

                element += (indices[d] - 1) * stride;
                stride *= _dims[d];
            }
        }
        else
        {
            throw ComCallException.Silent(ComCallException.BadIndex);
        }

        if (element < 0 || element >= Count)
        {
            throw ComCallException.Silent(ComCallException.BadIndex);
        }

        return Field(element, name);
    }

    private void Initialize(object? dims, object? names)
    {
        if (dims is not null)
        {
            double[] numbers = dims switch
            {
                string => throw ComCallException.Silent(ComCallException.TypeMismatch),
                Array array => [.. ExcelConversion.ColumnMajor(array).Select(e => e is IConvertible c and not string ? c.ToDouble(CultureInfo.InvariantCulture) : throw ComCallException.Silent(ComCallException.TypeMismatch))],
                IConvertible c => [1, c.ToDouble(CultureInfo.InvariantCulture)],
                _ => throw ComCallException.Silent(ComCallException.TypeMismatch),
            };
            if (numbers.Length == 1)
            {
                numbers = [1, numbers[0]];
            }

            int[] made = [.. numbers.Select(static n => (int)Math.Floor(n))];
            if (made.Any(static n => n < 0))
            {
                throw Fail("Error initializing array, invalid dimensions");
            }

            _dims = made;
        }

        if (names is not null)
        {
            string[] given = names switch
            {
                string s => [s],
                Array array => [.. ExcelConversion.ColumnMajor(array).Select(e => e as string ?? throw Fail("Error initializing array, a field name must be text", ComCallException.InvalidArgument))],
                _ => throw Fail("Error initializing array, the field names must be text", ComCallException.InvalidArgument),
            };
            if (Count == 0 && given.Length > 0)
            {
                throw Fail("Error initializing array, cannot initialize a 0 length struct array with field names");
            }

            foreach (string name in given)
            {
                if (!_names.Contains(name))
                {
                    _names.Add(name);
                }
            }
        }
    }
}

/// <summary><c>MWComplex</c>: a real and an imaginary part, each as given or converted, and flags that are kept but, as R2025b's, not applied to the parts (x.complex.flags_coerce).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelComplex : ExcelValueObject
{
    public ExcelComplex(nint module)
        : base("MWComplex", module)
    {
    }

    public override Guid InterfaceId => ExcelComGuids.ComplexInterface;

    public object? Real { get; set; }

    public object? Imag { get; set; }

    /// <summary>The <c>MWComplex</c> the engine answers for a complex value: its parts as the matching real arrays (one-based) or scalars, raw (x.complex.out_asdate).</summary>
    public static ExcelComplex Of(RuntimeNumeric numeric, ExcelFlags flags, ComServed owner)
    {
        _ = flags;
        _ = owner;
        int[] dims = [.. numeric.Dimensions];
        bool scalar = numeric.Count == 1 && dims.All(static d => d == 1);
        var made = new ExcelComplex(ExcelComUtilityLibrary.Module);
        made.Real = scalar ? ExcelConversion.Scalar(numeric.ClassName, numeric.Real[0]) : Part(numeric.ClassName, numeric.Real, dims);
        made.Imag = scalar ? ExcelConversion.Scalar(numeric.ClassName, numeric.Imaginary![0]) : Part(numeric.ClassName, numeric.Imaginary!, dims);
        return made;
    }

    private static Array Part(string cls, IReadOnlyList<double> values, int[] dims) =>
        ExcelConversion.Fill(cls == "single" ? typeof(float) : typeof(double), dims, k => ExcelConversion.Scalar(cls, values[k]));

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "Real":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return Real;
                }

                Real = Keep(Given(arguments, 0));
                return null;
            case "Imag":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return Imag;
                }

                Imag = Keep(Given(arguments, 0));
                return null;
            case "MWFlags":
                return FlagsMember(kind, arguments);
            case "Clone":
            {
                var copy = new ExcelComplex(Module) { Real = Real, Imag = Imag };
                copy.CopyFlags(this);
                return copy;
            }

            default:
                throw NoMember(member);
        }
    }
}

/// <summary><c>MWSparse</c>: <c>sparse(RowIndex, ColumnIndex, Array, NumRows, NumColumns)</c> as its five properties, the indices one-based.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelSparse : ExcelValueObject
{
    public ExcelSparse(nint module)
        : base("MWSparse", module)
    {
    }

    public override Guid InterfaceId => ExcelComGuids.SparseInterface;

    public object? Array { get; set; }

    public int? NumRows { get; set; }

    public int? NumColumns { get; set; }

    public object? RowIndex { get; set; }

    public object? ColumnIndex { get; set; }

    /// <summary>The <c>MWSparse</c> the engine answers for a sparse matrix: the stored values column by column as a one-based Double() (Boolean() for a logical one), their one-based row and column indices as Long() arrays (out.sparse, out.sparselogical).</summary>
    public static ExcelSparse Of(RuntimeSparse sparse, bool isLogical, ExcelFlags flags, ComServed owner)
    {
        _ = flags;
        _ = owner;
        int stored = sparse.Values.Count;
        var rows = new int[stored];
        var columns = new int[stored];
        for (int c = 0; c < sparse.Dimensions[1]; c++)
        {
            for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
            {
                rows[k] = sparse.RowIndices[k] + 1;
                columns[k] = c + 1;
            }
        }

        return new ExcelSparse(ExcelComUtilityLibrary.Module)
        {
            NumRows = sparse.Dimensions[0],
            NumColumns = sparse.Dimensions[1],
            RowIndex = ExcelConversion.Fill1(typeof(int), stored, k => rows[k]),
            ColumnIndex = ExcelConversion.Fill1(typeof(int), stored, k => columns[k]),
            Array = isLogical
                ? ExcelConversion.Fill1(typeof(bool), stored, k => sparse.Values[k] != 0)
                : ExcelConversion.Fill1(typeof(double), stored, k => sparse.Values[k]),
        };
    }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        bool get = kind == ComInvokeKind.PropertyGet;
        switch (member)
        {
            case "Array":
                if (get) return Array;
                Array = Keep(Given(arguments, 0));
                return null;
            case "NumRows":
                if (get) return NumRows ?? 0;
                NumRows = Flag.Int(arguments, this, member);
                return null;
            case "NumColumns":
                if (get) return NumColumns ?? 0;
                NumColumns = Flag.Int(arguments, this, member);
                return null;
            case "RowIndex":
                if (get) return RowIndex;
                RowIndex = Keep(Given(arguments, 0));
                return null;
            case "ColumnIndex":
                if (get) return ColumnIndex;
                ColumnIndex = Keep(Given(arguments, 0));
                return null;
            case "MWFlags":
                return FlagsMember(kind, arguments);
            case "Clone":
            {
                var copy = new ExcelSparse(Module) { Array = Array, NumRows = NumRows, NumColumns = NumColumns, RowIndex = RowIndex, ColumnIndex = ColumnIndex };
                copy.CopyFlags(this);
                return copy;
            }

            default:
                throw NoMember(member);
        }
    }
}

/// <summary><c>MWArg</c>: a value with flags of its own, whose data conversion flags convert it where the component's would (x.arg.*).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelArg : ExcelValueObject
{
    public ExcelArg(nint module)
        : base("MWArg", module)
    {
    }

    public override Guid InterfaceId => ExcelComGuids.ArgInterface;

    public object? Value { get; set; }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "Value":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return Value;
                }

                Value = Keep(Given(arguments, 0));
                return null;
            case "MWFlags":
                return FlagsMember(kind, arguments);
            case "Clone":
            {
                var copy = new ExcelArg(Module) { Value = Value };
                copy.CopyFlags(this);
                return copy;
            }

            default:
                throw NoMember(member);
        }
    }
}
