using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace JGraph.Runtime.Com;

/// <summary>
/// The conversions between a COM client's values and the engine's at an Excel add-in's call
/// boundary (compiler and SDK plan, stage K17d), by the rules stages K17a and K17d measured R2025b's
/// add-in following (<c>recordings/k17_com.out.txt</c> and <c>k17_com_extra.out.txt</c>), under the
/// flags of the add-in's <c>MWFlags</c> (<see cref="ExcelFlags"/>):
/// <list type="bullet">
/// <item>In: every scalar type to its MATLAB class, a BSTR a char row, a DATE a datenum through
/// <c>DateBias</c> (or its text under <c>InputDateFormat</c> 1), a SAFEARRAY a matrix of its element
/// class, an array of Variants a matrix when its elements are numbers of one class (under the
/// Matrix format), a char matrix when each is one character, else a cell; the Cell format making
/// cells of everything and <c>InputArrayIndFlag</c> nesting their elements; <c>CoerceNumericToType</c>
/// over numbers; a Range read through its <c>Value2</c>, blanks 0 or NaN by <c>ReplaceMissing</c>;
/// MathWorks' value classes (<see cref="ExcelStruct"/>, <see cref="ExcelComplex"/>, <see cref="ExcelSparse"/>,
/// <see cref="ExcelArg"/>, <see cref="ExcelField"/>) to what they hold.</item>
/// <item>Out: a value's class to its VARIANT type, matrices as one-based SAFEARRAYs, cells as arrays
/// of Variants, NaN by <c>ReplaceMatlabNaN</c>, a datenum a DATE under <c>OutputAsDate</c>, the Cell
/// format making arrays of Variants to the depth <c>OutputArrayIndFlag</c> says, a struct an
/// <c>MWStruct</c>, a complex value an <c>MWComplex</c>, a sparse one an <c>MWSparse</c>; and an output
/// into a Range written through <c>Resize</c> and <c>Value2</c> under <c>AutoResizeOutput</c> and
/// <c>TransposeOutput</c>.</item>
/// </list>
/// Where R2025b's conversion is demonstrably wrong (a NaN coerced to an integer answering the type's
/// minimum, a single answering -FLT_MAX for NaN, a date array that cannot be read back) JGraph
/// answers the true value and records the divergence (ADR 0317).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class ExcelConversion
{
    /// <summary>mwArrayFormatAsIs.</summary>
    public const int FormatAsIs = 0;

    /// <summary>mwArrayFormatMatrix, the default.</summary>
    public const int FormatMatrix = 1;

    /// <summary>mwArrayFormatCell.</summary>
    public const int FormatCell = 2;

    /// <summary>Excel's #N/A, the error value an output NaN becomes in a Range under ReplaceMatlabNaN 2.</summary>
    public const int ExcelErrorNA = unchecked((int)0x800A07FA);

    private const double OleDateMin = -657434.0;
    private const double OleDateMax = 2958465.99999999;

    // ---- In ------------------------------------------------------------------------------------

    /// <summary>The conversion's setting for one argument: where the array format and the data conversion come from, and what the value came through.</summary>
    private readonly record struct Context(ExcelArrayFormatFlags Format, ExcelDataConversionFlags Data, ComServed Owner, bool FromRange, int Depth)
    {
        public int InputFormat => Format.InputArrayFormat;

        public int Indirection => Format.InputArrayIndFlag;

        public Context Deeper(bool fromRange) => this with { FromRange = FromRange || fromRange, Depth = Depth + 1 };
    }

    /// <summary>Reads one argument of a call as the engine's value under <paramref name="flags"/>; null for one that is nothing (an unset MWComplex or MWSparse), which the caller takes as a missing argument.</summary>
    /// <exception cref="ComCallException">The value cannot cross.</exception>
    public static unsafe RuntimeValue? ReadArgument(Variant* v, ExcelFlags flags, ComServed owner) =>
        FromObject(ExcelVariants.Read(v), new Context(flags.ArrayFormat, flags.DataConversion, owner, FromRange: false, Depth: 0));

    /// <summary>
    /// Reads a <c>varargin</c> argument: converted as any argument, a cell is spread into the
    /// arguments it holds and anything else is one argument (k17_com: varin.vararray, varin.array,
    /// varin.packed; k17_com_extra: x.InputArrayFormat.2.varin_r8).
    /// </summary>
    public static unsafe IReadOnlyList<RuntimeValue> ReadVarargin(Variant* v, ExcelFlags flags, ComServed owner)
    {
        RuntimeValue? value = FromObject(ExcelVariants.Read(v), new Context(flags.ArrayFormat, flags.DataConversion, owner, FromRange: false, Depth: 0));
        return value switch
        {
            null => [],
            { Count: 0 } => [],
            RuntimeCell cell => [.. cell.Elements],
            _ => [value],
        };
    }

    /// <summary>A .NET object (as the COM marshaller reads a VARIANT) as the engine's value; null for one that is nothing at all (an unset MWComplex or MWSparse, a missing argument).</summary>
    public static RuntimeValue? FromClient(object? value, ExcelFlags flags, ComServed owner) =>
        FromObject(value, new Context(flags.ArrayFormat, flags.DataConversion, owner, FromRange: false, Depth: 0));

    private static RuntimeValue? FromObject(object? value, Context ctx)
    {
        switch (value)
        {
            case null:
                // A VT_EMPTY argument is nothing; inside an array the caller handles it.
                return null;
            case DBNull:
                return RuntimeValue.Empty();
            case Missing:
                return RuntimeNumeric.Int32([ExcelVariants.ParamNotFound], 1, 1);
            case ErrorWrapper error:
                return Shape(Coerce(RuntimeNumeric.Int32([error.ErrorCode], 1, 1), ctx.Data, value), ctx);
            case Array array:
                return FromArray(array, ctx);
            case ComServed served:
                return FromServed(served, ctx);
        }

        if (Marshal.IsComObject(value))
        {
            nint punk = Marshal.GetIUnknownForObject(value);
            try
            {
                if (ComObjects.Of(Native.NativeComCalls.Unwrap(punk)) is { } ours)
                {
                    return FromServed(ours, ctx);
                }
            }
            finally
            {
                Marshal.Release(punk);
            }

            object? held = RangeValue(value, ctx.Owner);
            if (held is null)
            {
                // A blank cell is 0, or NaN under ReplaceMissing 1 (in.range_blank, x.ReplaceMissing.1.range_blank_one).
                return Shape(RuntimeValue.Scalar(ctx.Data.ReplaceMissing == 1 ? double.NaN : 0.0), ctx);
            }

            return FromObject(held, ctx.Deeper(fromRange: true) with { Depth = ctx.Depth });
        }

        return Shape(FromScalar(value, ctx), ctx);
    }

    /// <summary>A scalar VARIANT's value, the date and coercion flags applied; not yet shaped by the array format.</summary>
    private static RuntimeValue FromScalar(object value, Context ctx)
    {
        switch (value)
        {
            case DateTime date:
                if (ctx.Data.InputDateFormat == 1)
                {
                    // mwDateFormatString: the date's text as oleaut32 writes it (3/15/2024 10:30:00 AM).
                    return RuntimeChar.Row(ExcelVariants.TextOf(date) ?? date.ToString(CultureInfo.CurrentCulture));
                }

                return Coerce(RuntimeValue.Scalar(date.ToOADate() + ctx.Data.DateBias), ctx.Data, value);
            case string text:
                return text.Length == 0 ? new RuntimeChar("", [0, 0]) : RuntimeChar.Row(text);
            case bool t:
                return Coerce(RuntimeValue.Scalar(t), ctx.Data, value);
            case double d:
                return Coerce(RuntimeValue.Scalar(d), ctx.Data, value);
            case float f:
                return Coerce(RuntimeNumeric.Single([f], 1, 1), ctx.Data, value);
            case int i:
                return Coerce(RuntimeNumeric.Int32([i], 1, 1), ctx.Data, value);
            case short s:
                return Coerce(RuntimeNumeric.Int16([s], 1, 1), ctx.Data, value);
            case sbyte b:
                return Coerce(RuntimeNumeric.Int8([b], 1, 1), ctx.Data, value);
            case byte b:
                return Coerce(RuntimeNumeric.UInt8([b], 1, 1), ctx.Data, value);
            case ushort u:
                return Coerce(RuntimeNumeric.UInt16([u], 1, 1), ctx.Data, value);
            case uint u:
                return Coerce(RuntimeNumeric.UInt32([u], 1, 1), ctx.Data, value);
            case long l:
                return Coerce(Int64Of(l, ctx.Owner), ctx.Data, value);
            case ulong l:
                return Coerce(UInt64Of(l, ctx.Owner), ctx.Data, value);
            case char c:
                return Coerce(RuntimeNumeric.UInt16([c], 1, 1), ctx.Data, value);
            case decimal m:
                return Coerce(RuntimeValue.Scalar((double)m), ctx.Data, value);
            default:
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"A {value.GetType().Name} cannot be passed to a function.");
        }
    }

    private static RuntimeNumeric Int64Of(long value, ComServed owner)
    {
        try
        {
            return RuntimeNumeric.Int64([value], 1, 1);
        }
        catch (ArgumentException)
        {
            throw new ComCallException(ComCallException.Fail, owner.ProgId, $"The int64 value {value} is beyond 2^53 and cannot be held exactly; pass it as text or as a double.");
        }
    }

    private static RuntimeNumeric UInt64Of(ulong value, ComServed owner)
    {
        try
        {
            return RuntimeNumeric.UInt64([value], 1, 1);
        }
        catch (ArgumentException)
        {
            throw new ComCallException(ComCallException.Fail, owner.ProgId, $"The uint64 value {value} is beyond 2^53 and cannot be held exactly; pass it as text or as a double.");
        }
    }

    /// <summary>The Cell format's shaping of a scalar: a 1-by-1 cell (k17_com: flag.InputArrayFormat.2.scalar), whatever InputArrayIndFlag says.</summary>
    private static RuntimeValue Shape(RuntimeValue scalar, Context ctx) =>
        ctx.InputFormat == FormatCell && ctx.Depth == 0 ? new RuntimeCell([scalar], [1, 1]) : scalar;

    /// <summary>
    /// A SAFEARRAY: a typed one a matrix of its element class (a one-dimensional one a row), an
    /// array of Variants classified by its elements; the array format and the indirection flag
    /// applied as measured (k17_com_extra: x.InputArrayFormat.*, x.InputArrayIndFlag.*).
    /// </summary>
    private static RuntimeValue FromArray(Array array, Context ctx)
    {
        int rank = array.Rank;
        int[] dims = rank == 1 ? [1, array.GetLength(0)] : [.. Enumerable.Range(0, rank).Select(array.GetLength)];
        object?[] elements = ColumnMajor(array);
        Type element = array.GetType().GetElementType()!;
        bool typed = element != typeof(object) && element != typeof(string);
        if (typed)
        {
            return FromTypedArray(elements, element, dims, ctx);
        }

        // Variants (and BSTRs): each element on its own, then the shape they make together.
        var values = new RuntimeValue?[elements.Length];
        var classes = new HashSet<string>(StringComparer.Ordinal);
        bool allSingleChars = true;
        bool allLogical = true;
        bool anyMissing = false;
        Context inner = ctx.Deeper(fromRange: false);
        // The elements are classified before any coercion: a client's array of numbers of one class is a
        // matrix left as it is (x.Coerce.8.var_numbers), a cell's elements are coerced one by one (x.Coerce.8.var_mixed).
        // (A coercion to text applies before, x.Coerce.4.var_numbers: a cell of texts.)
        Context plain = inner with { Data = inner.Data.CoerceNumericToType is 0 or 4 ? inner.Data : Uncoerced(inner.Data) };
        for (int k = 0; k < elements.Length; k++)
        {
            object? e = elements[k];
            if (e is null || e is DBNull)
            {
                // An empty element, a DBNull too (in.arr_var_with_dbnull: 0 in a matrix, [] in a cell).
                values[k] = null;
                anyMissing = true;
                continue;
            }

            RuntimeValue? converted = e is Array nested ? FromArray(nested, inner) : FromObject(e, plain);
            if (converted is null)
            {
                values[k] = null;
                anyMissing = true;
                continue;
            }

            values[k] = converted;
            if (converted is RuntimeNumeric { Count: 1 } n && n.Dimensions.Count == 2 && n.Imaginary is null)
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
        // The Matrix format collapses a client's own array of Variants at the top and at the depth the
        // indirection flag names (x.InputArrayIndFlag.1.fmt1.jagged), a Range's cells at that depth alone
        // (x.InputArrayIndFlag.1.fmt1.range, x.range.in_packed).
        bool collapse = ctx.InputFormat == FormatMatrix && (ctx.FromRange ? ctx.Depth == ctx.Indirection : ctx.Depth == 0 || ctx.Depth == ctx.Indirection);
        if (collapse && (given == 0 || (classes.Count == 1 && !classes.Contains("*"))))
        {
            // Numbers of one class; an empty element 0, or NaN for a Range's blank under ReplaceMissing 1 (x.ReplaceMissing.1.range_*).
            string cls = given == 0 ? "double" : classes.First();
            double blank = ctx.FromRange && ctx.Data.ReplaceMissing == 1 && cls is "double" or "single" ? double.NaN : 0.0;
            var numbers = new RuntimeNumeric(cls, [.. values.Select(v => v is RuntimeNumeric n ? n.Real[0] : blank)], null, dims);
            // A Range's numbers are coerced like a typed array; a client's own array of Variants is not (x.Coerce.8.range, x.Coerce.8.var_numbers).
            return ctx.FromRange ? CoerceArray(numbers, ctx.Data) : numbers;
        }

        if (collapse && allLogical && given > 0)
        {
            // Booleans, an empty element false (x.ReplaceMissing.*.range_bool_blank, x.ReplaceMissing.0.var_bools_empty).
            return new RuntimeLogical([.. values.Select(static v => v is RuntimeLogical l && l.Elements[0])], dims);
        }

        if (collapse && allSingleChars && given == elements.Length)
        {
            // in.arr_str_2d: one character each makes a char matrix.
            return new RuntimeChar(string.Concat(values.Select(static v => ((RuntimeChar)v!).Characters)), dims);
        }

        _ = anyMissing;
        var cellElements = new RuntimeValue[values.Length];
        for (int k = 0; k < values.Length; k++)
        {
            RuntimeValue item = values[k] ?? RuntimeValue.Empty();
            if (elements[k] is not Array && elements[k] is not null && elements[k] is not DBNull && !Marshal.IsComObject(elements[k]!) && values[k] is not null && ctx.Data.CoerceNumericToType != 4)
            {
                item = Coerce(item, ctx.Data, elements[k]!);
            }

            cellElements[k] = Nest(item, ctx, elementOfVariants: true);
        }

        return new RuntimeCell(cellElements, dims);
    }

    /// <summary>The Matrix format with no indirection, for the parts of an MWComplex.</summary>
    private static readonly ExcelArrayFormatFlags PlainFormat = new(0, "") { InputArrayFormat = FormatMatrix };

    /// <summary>The same data conversion with CoerceNumericToType off.</summary>
    private static ExcelDataConversionFlags Uncoerced(ExcelDataConversionFlags data)
    {
        var copy = new ExcelDataConversionFlags(0, "");
        copy.CopyFrom(data);
        copy.CoerceNumericToType = 0;
        return copy;
    }

    private static RuntimeValue FromTypedArray(object?[] elements, Type element, int[] dims, Context ctx)
    {
        RuntimeValue matrix;
        if (element == typeof(bool))
        {
            matrix = CoerceArray(new RuntimeLogical([.. elements.Select(static e => (bool)e!)], dims), ctx.Data);
        }
        else if (element == typeof(DateTime) && ctx.Data.InputDateFormat == 1)
        {
            // Dates as text are a cell of them (x.InputDateFormat.1.date_arr).
            matrix = new RuntimeCell([.. elements.Select(e => (RuntimeValue)RuntimeChar.Row(ExcelVariants.TextOf(e!) ?? ((DateTime)e!).ToString(CultureInfo.CurrentCulture)))], dims);
        }
        else
        {
            string cls = ClassOf(element);
            double[] numbers = [.. elements.Select(e => e switch
            {
                null or DBNull => 0.0,
                DateTime date => date.ToOADate() + ctx.Data.DateBias,
                decimal m => (double)m,
                char c => c,
                IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
                _ => 0.0,
            })];
            if (cls is "int64" or "uint64" && numbers.Any(static n => Math.Abs(n) > 9007199254740992.0))
            {
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"An {cls} array holds a value beyond 2^53, which cannot be held exactly; pass it as doubles or text.");
            }

            matrix = CoerceArray(new RuntimeNumeric(cls, numbers, null, dims), ctx.Data);
        }

        // The Cell format splits a typed array into a cell of its elements when its depth is within the
        // indirection; each element a 1-by-1 cell when the flag is set (x.InputArrayIndFlag.*.fmt2.r8_1d).
        if (ctx.InputFormat == FormatCell && ctx.Depth <= ctx.Indirection && matrix is not RuntimeCell)
        {
            int wraps = Math.Min(Math.Max(ctx.Indirection - ctx.Depth, 0), 1);
            RuntimeValue[] cells = [.. Enumerable.Range(0, matrix.Count).Select(k => Wrap(ElementOf(matrix, k), wraps))];
            return new RuntimeCell(cells, dims);
        }

        return matrix;
    }

    /// <summary>An element of an array of Variants, nested as the indirection flag says under the Cell format.</summary>
    private static RuntimeValue Nest(RuntimeValue value, Context ctx, bool elementOfVariants)
    {
        if (ctx.InputFormat != FormatCell || !elementOfVariants || value is RuntimeCell)
        {
            return value;
        }

        // A scalar element at depth d + 1 is wrapped (ind - d) times (x.InputArrayIndFlag.2.fmt2.var_1d: twice);
        // a Range's cells once at most, like a typed array's elements (x.InputArrayIndFlag.2.fmt2.range).
        int wraps = Math.Max(ctx.Indirection - ctx.Depth, 0);
        return Wrap(value, ctx.FromRange ? Math.Min(wraps, 1) : wraps);
    }

    private static RuntimeValue Wrap(RuntimeValue value, int times)
    {
        for (int k = 0; k < times; k++)
        {
            value = new RuntimeCell([value], [1, 1]);
        }

        return value;
    }

    /// <summary>Element k (column-major) of a matrix as a 1-by-1 value of its class.</summary>
    private static RuntimeValue ElementOf(RuntimeValue matrix, int k) => matrix switch
    {
        RuntimeNumeric n => new RuntimeNumeric(n.ClassName, [n.Real[k]], n.Imaginary is null ? null : [n.Imaginary[k]], [1, 1]),
        RuntimeLogical l => RuntimeValue.Scalar(l.Elements[k]),
        RuntimeChar c => new RuntimeChar(c.Characters[k].ToString(), [1, 1]),
        RuntimeCell cell => cell.Elements[k],
        _ => matrix,
    };

    /// <summary>The elements of a .NET array in MATLAB's column-major order, whatever its lower bounds.</summary>
    internal static object?[] ColumnMajor(Array array)
    {
        int rank = array.Rank;
        int count = array.Length;
        var elements = new object?[count];
        var subs = new int[rank];
        var lower = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            lower[d] = array.GetLowerBound(d);
        }

        for (int k = 0; k < count; k++)
        {
            int rest = k;
            for (int d = 0; d < rank; d++)
            {
                int extent = array.GetLength(d);
                subs[d] = rest % extent + lower[d];
                rest /= extent;
            }

            elements[k] = array.GetValue(subs);
        }

        return elements;
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

    /// <summary>
    /// What a Range (or any other object a client passes) holds: its <c>Value2</c>, else its default
    /// property, which is how Excel's Application answers "Microsoft Excel" (x.range.application);
    /// an object with neither is refused (x.range.worksheet).
    /// </summary>
    private static object? RangeValue(object comObject, ComServed owner)
    {
        Type type = comObject.GetType();
        try
        {
            return type.InvokeMember("Value2", BindingFlags.GetProperty, null, comObject, null, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMemberException or COMException)
        {
        }

        try
        {
            return type.InvokeMember("[DISPID=0]", BindingFlags.GetProperty, null, comObject, null, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMemberException or COMException)
        {
            throw new ComCallException(ComCallException.Fail, owner.ProgId,
                "An object with no Value2 and no default value (a Worksheet, a Workbook, another COM object) cannot be passed to a function; pass a Range or its Value2.");
        }
    }

    /// <summary>One of MathWorks' value classes (or a flags object, which cannot cross) as the engine's value.</summary>
    private static RuntimeValue? FromServed(ComServed served, Context ctx)
    {
        switch (served)
        {
            case ExcelArg arg:
                // Its own data conversion flags, the caller's array format (x.arg.component_fmt2, x.arg.component_coerce, x.arg.cell_var_fmt2).
                return arg.Value is null ? RuntimeValue.Empty() : FromObject(arg.Value, ctx with { Data = arg.Flags.DataConversion }) ?? RuntimeValue.Empty();
            case ExcelField field:
                return field.Value is null ? RuntimeValue.Empty() : FromObject(field.Value, ctx with { Data = field.Flags.DataConversion }) ?? RuntimeValue.Empty();
            case ExcelStruct record:
                return record.ToValue(value => value is null ? RuntimeValue.Empty() : FromObject(value.Value, ctx with { Data = value.Flags.DataConversion }) ?? RuntimeValue.Empty());
            case ExcelComplex complex:
                return FromComplex(complex, ctx);
            case ExcelSparse sparse:
                return FromSparse(sparse, ctx);
            case ExcelComponent:
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, "An add-in object cannot be passed to a function.");
            default:
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId,
                    $"A {served.GetType().Name.Replace("Excel", "MW", StringComparison.Ordinal)} object cannot be passed to a function.");
        }
    }

    /// <summary>
    /// An MWComplex: its parts as numeric arrays under the caller's data conversion (x.complex.in_component_coerce;
    /// its own flags are kept but not applied, as R2025b's are, x.complex.flags_coerce), a part left unset
    /// zeros, both unset nothing (x.complex.unset), then the caller's array format over the complex value
    /// (x.complex.in_cellformat). Integer parts become double: JGraph keeps no complex integer (ADR 0266).
    /// </summary>
    private static RuntimeValue? FromComplex(ExcelComplex complex, Context ctx)
    {
        if (complex.Real is null && complex.Imag is null)
        {
            return null;
        }

        Context parts = ctx with { Format = PlainFormat, Depth = 0 };
        RuntimeNumeric? re = Numeric(complex.Real is null ? null : FromObject(complex.Real, parts), "real", ctx);
        RuntimeNumeric? im = Numeric(complex.Imag is null ? null : FromObject(complex.Imag, parts), "imaginary", ctx);
        RuntimeNumeric given = re ?? im!;
        int[] dims = [.. given.Dimensions];
        re ??= new RuntimeNumeric(given.ClassName, new double[given.Count], null, dims);
        im ??= new RuntimeNumeric(given.ClassName, new double[given.Count], null, dims);
        if (re.Count != im.Count)
        {
            throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId,
                $"An MWComplex's Real and Imag parts must be the same size; they have {re.Count} and {im.Count} elements.");
        }

        string cls = re.ClassName == "single" && im.ClassName == "single" ? "single" : "double";
        var value = new RuntimeNumeric(cls, [.. re.Real], [.. im.Real], [.. re.Dimensions]);
        if (ctx.InputFormat == FormatCell)
        {
            RuntimeValue[] cells = [.. Enumerable.Range(0, value.Count).Select(k => ElementOf(value, k))];
            return new RuntimeCell(cells, [.. value.Dimensions]);
        }

        return value;
    }

    private static RuntimeNumeric? Numeric(RuntimeValue? part, string which, Context ctx)
    {
        switch (part)
        {
            case null:
                return null;
            case RuntimeNumeric n when n.Imaginary is null:
                return n;
            case RuntimeLogical l:
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"An MWComplex's {which} part must be numeric; a logical was given.");
            default:
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"An MWComplex's {which} part must be numeric; a {part.ClassName} was given.");
        }
    }

    /// <summary>
    /// An MWSparse: <c>sparse(RowIndex, ColumnIndex, Array, NumRows, NumColumns)</c> with the indices
    /// as typed arrays or scalars (an array of Variants is refused, as R2025b refuses it), the values
    /// double or logical, nothing when its Array is unset (in.sparse_empty is a missing argument);
    /// duplicates summed as MATLAB's sparse sums them; the data conversion not applied (x.sparse.in_component_coerce).
    /// </summary>
    private static RuntimeValue? FromSparse(ExcelSparse sparse, Context ctx)
    {
        if (sparse.Array is null)
        {
            return null;
        }

        if (sparse.Array is object[] || sparse.RowIndex is object[] || sparse.ColumnIndex is object[])
        {
            throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, "An MWSparse's Array, RowIndex and ColumnIndex must be typed arrays of numbers (a Double() or Long() array), not Variants.");
        }

        double[] values;
        bool logical;
        switch (sparse.Array)
        {
            case bool b:
                values = [b ? 1 : 0];
                logical = true;
                break;
            case bool[] bools:
                values = [.. bools.Select(static b => b ? 1.0 : 0.0)];
                logical = true;
                break;
            case double d:
                values = [d];
                logical = false;
                break;
            case double[] doubles:
                values = doubles;
                logical = false;
                break;
            case Array other when other.Rank == 1 && other.GetType().GetElementType() == typeof(double):
                values = [.. other.Cast<double>()];
                logical = false;
                break;
            default:
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, "An MWSparse's Array must be a Double() or Boolean() array.");
        }

        int[] rows = Indices(sparse.RowIndex, "RowIndex", values.Length, ctx);
        int[] columns = Indices(sparse.ColumnIndex, "ColumnIndex", values.Length, ctx);
        int numRows = sparse.NumRows ?? (rows.Length == 0 ? 0 : rows.Max());
        int numColumns = sparse.NumColumns ?? (columns.Length == 0 ? 0 : columns.Max());
        for (int k = 0; k < values.Length; k++)
        {
            if (rows[k] < 1 || rows[k] > numRows || columns[k] < 1 || columns[k] > numColumns)
            {
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId,
                    $"An MWSparse index ({rows[k]}, {columns[k]}) lies outside its {numRows}-by-{numColumns} matrix.");
            }
        }

        // Compressed columns, duplicates summed, as sparse(i, j, v, m, n) builds them.
        var cells = new SortedDictionary<(int Column, int Row), double>();
        for (int k = 0; k < values.Length; k++)
        {
            (int Column, int Row) key = (columns[k] - 1, rows[k] - 1);
            cells[key] = cells.TryGetValue(key, out double sum) ? sum + values[k] : values[k];
        }

        var rowIndices = new List<int>();
        var stored = new List<double>();
        var starts = new int[numColumns + 1];
        int column = 0;
        foreach (((int c, int r), double v) in cells)
        {
            while (column < c)
            {
                starts[++column] = stored.Count;
            }

            if (logical && v == 0)
            {
                continue;
            }

            rowIndices.Add(r);
            stored.Add(logical ? 1.0 : v);
        }

        while (column < numColumns)
        {
            starts[++column] = stored.Count;
        }

        return new RuntimeSparse(numRows, numColumns, rowIndices, starts, stored, logical);
    }

    private static int[] Indices(object? given, string name, int count, Context ctx)
    {
        double[] numbers = given switch
        {
            null => [],
            double d => [d],
            int i => [i],
            Array a when a.Rank == 1 => [.. a.Cast<object>().Select(static e => System.Convert.ToDouble(e, CultureInfo.InvariantCulture))],
            IConvertible c => [c.ToDouble(CultureInfo.InvariantCulture)],
            _ => throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"An MWSparse's {name} must be a Double() or Long() array."),
        };
        if (numbers.Length != count)
        {
            throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"An MWSparse's {name} has {numbers.Length} elements where its Array has {count}.");
        }

        // A fraction is rounded to the nearest index, as R2025b rounds it (x.sparse.fraction_index: 1.5 is row 2).
        int[] rounded = [.. numbers.Select(static n => (int)Math.Round(n, MidpointRounding.AwayFromZero))];
        foreach (int n in rounded)
        {
            if (n < 1)
            {
                throw new ComCallException(ComCallException.Fail, ctx.Owner.ProgId, $"An MWSparse's {name} must hold positive indices; {n} was given.");
            }
        }

        return rounded;
    }

    // ---- CoerceNumericToType --------------------------------------------------------------------

    /// <summary>
    /// <c>CoerceNumericToType</c> over a scalar: 3 logical (nonzero true), 4 char (the VARIANT's text:
    /// 3.7, True, a date's text), 6 double, 7 single, 8 to 13 the integer classes (rounded to even,
    /// saturated); a logical true is VARIANT_TRUE, -1, so -1 as a signed integer or a double and the
    /// type's maximum as an unsigned one (x.Coerce.*). Text is never coerced. Where R2025b answers an
    /// integer's minimum for NaN and -FLT_MAX for a single NaN, JGraph answers 0 and NaN (ADR 0317).
    /// </summary>
    private static RuntimeValue Coerce(RuntimeValue value, ExcelDataConversionFlags data, object original)
    {
        int type = data.CoerceNumericToType;
        if (type == 0 || value is RuntimeChar)
        {
            return value;
        }

        if (type == 4)
        {
            return RuntimeChar.Row(TextOf(original, value));
        }

        return CoerceArray(value, data);
    }

    /// <summary>Coerces every element of a numeric or logical array (text answers a cell of texts).</summary>
    private static RuntimeValue CoerceArray(RuntimeValue value, ExcelDataConversionFlags data)
    {
        int type = data.CoerceNumericToType;
        if (type is 0 or 1 or 2 or 5 or > 13 || value is RuntimeChar or RuntimeCell)
        {
            return value;
        }

        int[] dims = [.. value.Dimensions];
        double[] numbers;
        bool fromLogical = value is RuntimeLogical;
        switch (value)
        {
            case RuntimeNumeric n when n.Imaginary is null:
                numbers = [.. n.Real];
                break;
            case RuntimeLogical l:
                // VARIANT_TRUE is -1 (x.Coerce.6.bool: -1; x.Coerce.9.bool: 255).
                numbers = [.. l.Elements.Select(static b => b ? -1.0 : 0.0)];
                break;
            default:
                return value;
        }

        if (type == 3)
        {
            return new RuntimeLogical([.. numbers.Select(static x => x != 0 && !double.IsNaN(x))], dims);
        }

        if (type == 4)
        {
            RuntimeValue[] texts = [.. numbers.Select(x => (RuntimeValue)RuntimeChar.Row(fromLogical ? (x != 0 ? "True" : "False") : NumberText(x)))];
            return value.Count == 1 ? texts[0] : new RuntimeCell(texts, dims);
        }

        string cls = type switch
        {
            6 => "double",
            7 => "single",
            8 => "int8",
            9 => "uint8",
            10 => "int16",
            11 => "uint16",
            12 => "int32",
            _ => "uint32",
        };
        if (cls is "double" or "single")
        {
            return new RuntimeNumeric(cls, numbers, null, dims);
        }

        (double min, double max) = cls switch
        {
            "int8" => ((double)sbyte.MinValue, (double)sbyte.MaxValue),
            "uint8" => (byte.MinValue, byte.MaxValue),
            "int16" => (short.MinValue, short.MaxValue),
            "uint16" => (ushort.MinValue, ushort.MaxValue),
            "int32" => (int.MinValue, int.MaxValue),
            _ => (uint.MinValue, uint.MaxValue),
        };
        double[] ints = new double[numbers.Length];
        for (int k = 0; k < numbers.Length; k++)
        {
            double x = numbers[k];
            if (fromLogical && x == -1 && min == 0)
            {
                // A true as an unsigned integer wraps to the type's maximum (x.Coerce.9.bool: 255).
                ints[k] = max;
                continue;
            }

            // (+ 0.0 turns the -0 that rounding -0.5 leaves into 0.)
            ints[k] = double.IsNaN(x) ? 0 : Math.Clamp(Math.Round(x, MidpointRounding.ToEven), min, max) + 0.0;
        }

        return new RuntimeNumeric(cls, ints, null, dims);
    }

    /// <summary>The text CoerceNumericToType 4 makes of a value: a date's text, True or False, a number's shortest digits (inf and -inf as R2025b writes them; NaN as NaN).</summary>
    private static string TextOf(object original, RuntimeValue value)
    {
        switch (original)
        {
            case DateTime date:
                return ExcelVariants.TextOf(date) ?? date.ToString(CultureInfo.CurrentCulture);
            case bool b:
                return b ? "True" : "False";
            case RuntimeValue:
                break;
        }

        if (value is RuntimeLogical l && l.Count == 1)
        {
            return l.Elements[0] ? "True" : "False";
        }

        return value is RuntimeNumeric n && n.Count == 1 ? NumberText(n.Real[0]) : value.ToString() ?? "";
    }

    private static string NumberText(double x) =>
        double.IsNaN(x) ? "NaN" : double.IsPositiveInfinity(x) ? "inf" : double.IsNegativeInfinity(x) ? "-inf" : x.ToString("R", CultureInfo.InvariantCulture);

    // ---- Out -----------------------------------------------------------------------------------

    /// <summary>A result's conversion to the client: whether the Cell format is on, how deep it reaches, whether dates and NaN are replaced (not for a struct's fields), and whether it goes into a Range.</summary>
    private readonly record struct Out(ExcelFlags Flags, ComServed Owner, bool Cell, int Depth, bool Raw, bool ForRange)
    {
        public Out Element(bool cell, int depth) => this with { Cell = cell, Depth = depth };
    }

    /// <summary>
    /// Converts a value for the client under <paramref name="flags"/>: a .NET scalar, string, array
    /// or <see cref="DateTime"/> the marshaller writes, a <see cref="ComServed"/> object for a struct,
    /// complex or sparse value, or null for VT_EMPTY. <paramref name="depth"/> is the indirection the
    /// Cell format reaches into cells with (<c>OutputArrayIndFlag</c>, one more for varargout).
    /// </summary>
    /// <exception cref="ComCallException">The value's class cannot cross.</exception>
    public static object? ToClient(RuntimeValue value, ExcelFlags? flags, ComServed owner, int depth = -1, bool forRange = false)
    {
        flags ??= owner.FlagsForResults ?? ExcelFlags.Defaults;
        bool cell = flags.ArrayFormat.OutputArrayFormat == FormatCell;
        return Convert(value, new Out(flags, owner, cell, depth < 0 ? flags.ArrayFormat.OutputArrayIndFlag : depth, Raw: false, forRange));
    }

    private static object? Convert(RuntimeValue value, Out o)
    {
        int[] dims = [.. value.Dimensions];
        switch (value)
        {
            case RuntimeNumeric numeric when numeric.Imaginary is not null:
            {
                if (o.Cell)
                {
                    // An array of Variants, each an MWComplex of one element (x.OutputArrayFormat.2.complexmat).
                    return numeric.Count == 0 ? null : Fill(typeof(object), dims, k => ExcelComplex.Of(new RuntimeNumeric(numeric.ClassName, [numeric.Real[k]], [numeric.Imaginary![k]], [1, 1]), o.Flags, o.Owner));
                }

                return ExcelComplex.Of(numeric, o.Flags, o.Owner);
            }

            case RuntimeNumeric numeric:
            {
                if (numeric.Count == 0)
                {
                    // out.empty, out.emptyrow: an empty value is nothing, in every format.
                    return null;
                }

                if (o.Cell)
                {
                    // The Cell format: every element raw, NaN nothing (x.OutputArrayFormat.2.nanmat under ReplaceMatlabNaN 1).
                    return Fill(typeof(object), dims, k => double.IsNaN(numeric.Real[k]) ? null : Scalar(numeric.ClassName, numeric.Real[k]));
                }

                bool floating = numeric.ClassName is "double" or "single";
                if (numeric.Count == 1 && IsScalar(dims))
                {
                    return Number(numeric.ClassName, numeric.Real[0], o);
                }

                bool anyNaN = floating && numeric.Real.Any(double.IsNaN);
                bool asDate = !o.Raw && o.Flags.DataConversion.OutputAsDate && numeric.ClassName == "double";
                if (anyNaN || asDate)
                {
                    // out.nanmat: Variants, each as the scalar would be; dates where OutputAsDate makes them (R2025b
                    // cannot build that array: x.OutputAsDate.True.doublerow, ADR 0317).
                    object?[] made = [.. Enumerable.Range(0, numeric.Count).Select(k => Number(numeric.ClassName, numeric.Real[k], o))];
                    if (asDate && made.All(static m => m is DateTime))
                    {
                        return Fill(typeof(DateTime), dims, k => made[k]);
                    }

                    return Fill(typeof(object), dims, k => made[k]);
                }

                return Fill(ElementType(numeric.ClassName), dims, k => Scalar(numeric.ClassName, numeric.Real[k]));
            }

            case RuntimeLogical logical:
                if (logical.Count == 0)
                {
                    return null;
                }

                if (o.Cell)
                {
                    return Fill(typeof(object), dims, k => logical.Elements[k]);
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
                    return o.Cell ? Fill(typeof(object), [1, 1], _ => text.ToString()) : text.ToString();
                }

                // out.charmat: a char matrix is one string per character, in its shape.
                string all = text.Characters;
                return Fill(o.Cell ? typeof(object) : typeof(string), dims, k => all[k].ToString());
            }

            case RuntimeString strings:
                // R2025b answers a type mismatch for every string; JGraph answers the text (ADR 0316).
                if (strings.Count == 1 && IsScalar(dims))
                {
                    return o.Cell ? Fill(typeof(object), [1, 1], _ => strings.Elements[0] ?? "") : strings.Elements[0] ?? "";
                }

                return strings.Count == 0 ? null : Fill(o.Cell ? typeof(object) : typeof(string), dims, k => strings.Elements[k] ?? "");

            case RuntimeCell cell:
            {
                // out.cell, out.cellempty: a cell is Variants in its shape, an empty one an empty array, not
                // nothing; under the Cell format its elements take the format while the depth lasts
                // (x.OutputArrayIndFlag.*.fmt2.cell, .cellnested).
                Out element = o.Element(o.Cell && o.Depth >= 1, o.Depth - 1);
                return Fill(typeof(object), dims, k => Convert(cell.Elements[k], element));
            }

            case RuntimeStruct record:
                return ExcelStruct.Of(record, o.Flags, o.Owner, field => Convert(field, o with { Raw = true, Depth = 0 }));

            case RuntimeSparse sparse:
                return ExcelSparse.Of(sparse, isLogical: sparse.ClassName == "logical", o.Flags, o.Owner);

            default:
                throw new ComCallException(ComCallException.Fail, o.Owner.ProgId,
                    $"A {value.ClassName} value cannot be returned to a COM client; return a number, text, a logical, a struct or a cell of them.");
        }
    }

    /// <summary>A number as a scalar VARIANT: NaN by ReplaceMatlabNaN (nothing, 0, #N/A), a datenum a DATE under OutputAsDate when the biased value is one OLE holds, else the biased double (x.OutputAsDate.True.*).</summary>
    private static object? Number(string cls, double x, Out o)
    {
        bool floating = cls is "double" or "single";
        if (floating && double.IsNaN(x))
        {
            if (o.Raw)
            {
                return null;
            }

            switch (o.Flags.DataConversion.ReplaceMatlabNaN)
            {
                case 1:
                    x = 0.0;
                    break;
                case 2:
                    return o.ForRange ? new ErrorWrapper(ExcelErrorNA) : "#N/A";
                default:
                    return null;
            }
        }

        if (!o.Raw && o.Flags.DataConversion.OutputAsDate && cls == "double")
        {
            double d = x - o.Flags.DataConversion.DateBias;
            if (d >= OleDateMin && d <= OleDateMax)
            {
                return DateTime.FromOADate(d);
            }

            return d;
        }

        return Scalar(cls, x);
    }

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
    internal static object Scalar(string cls, double x) => cls switch
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

    /// <summary>A one-dimensional one-based array of <paramref name="count"/> elements, the shape MWStruct's Dims and FieldNames, MWSparse's indices and values and MWPack's result take (k17_com: out.sparse.members [1..2], util.MWPack.three [1..3]).</summary>
    internal static Array Fill1(Type element, int count, Func<int, object?> at)
    {
        Array made = Array.CreateInstance(element, [count], [1]);
        for (int k = 0; k < count; k++)
        {
            made.SetValue(at(k), k + 1);
        }

        return made;
    }

    /// <summary>
    /// An array of <paramref name="element"/> in MATLAB's shape, element k (column-major) from
    /// <paramref name="at"/>, with every lower bound 1: the SAFEARRAYs R2025b's add-in answers are
    /// one-based, as VBA's are (k17_com: out.doublerow [1..1,1..3], out.cellempty [1..0,1..0]).
    /// </summary>
    internal static Array Fill(Type element, int[] dims, Func<int, object?> at)
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

    // ---- Ranges --------------------------------------------------------------------------------

    /// <summary>
    /// Writes an output into a Range the client gave as its slot (k17_com: rangeout.*; k17_com_extra:
    /// x.rangeout.*): nothing for an empty value; a scalar into the range's first cell (resized to one
    /// cell under AutoResizeOutput); an array transposed under TransposeOutput, then written into the
    /// range as it is when its cells are as many as the array's elements (a row into a column fills
    /// the column: x.rangeout.row_into_col), else into the range resized from its first cell to the
    /// array's shape under AutoResizeOutput, else into the range as given (Excel fills what fits and
    /// #N/A beyond); a nested array in a cell becomes a blank; a struct, complex or sparse object is
    /// handed to Excel as it is, which takes an MWComplex's Real and nothing of the others (R2025b's
    /// rangeout.complex and rangeout.struct). Answers the range written (the resized one when it
    /// changed), or null when nothing was written.
    /// </summary>
    public static object? WriteRange(object range, RuntimeValue value, ExcelFlags flags, ComServed owner, int depth)
    {
        object? converted = ToClient(value, flags, owner, depth, forRange: true);
        return WriteRangeObject(range, converted, flags.ArrayFormat.AutoResizeOutput, flags.ArrayFormat.TransposeOutput, owner);
    }

    /// <summary>Writes an already converted value into a Range (the utility library's MWUnpack writes so, with no transposition).</summary>
    public static object? WriteRangeObject(object range, object? converted, bool autoResize, bool transpose, ComServed owner)
    {
        _ = owner;
        Type type = range.GetType();
        object target = range;
        try
        {
            if (converted is null)
            {
                // Nothing written; the range is still resized to its first cell (x.rangeout.empty_into_2x2).
                return autoResize && Count(range) != 1 ? Resize(range, 1, 1) : null;
            }

            if (converted is Array array && array.Rank >= 2)
            {
                if (transpose && array.Rank == 2)
                {
                    array = Transposed(array);
                }

                array = Flattened(array);
                int rows = array.GetLength(0);
                int cols = array.Rank >= 2 ? array.GetLength(1) : 1;
                if (autoResize)
                {
                    int cells = Count(range);
                    if (cells == rows * cols && (Rows(range) != rows || Columns(range) != cols))
                    {
                        // As many cells as elements: the array fills the range's own shape, column by column.
                        array = Reshaped(array, Rows(range), Columns(range));
                    }
                    else if (Rows(range) != rows || Columns(range) != cols)
                    {
                        target = Resize(range, rows, cols);
                    }
                }

                Set(target, array);
                return target;
            }

            if (converted is ComServed served)
            {
                // Excel reads an object's default property: an MWComplex's Real (rangeout.complex writes 1);
                // an MWStruct, MWSparse or an MWComplex with an array Real gives it nothing (rangeout.struct).
                if (served is ExcelComplex { Real: { } real } && real is not Array)
                {
                    Set(target, real);
                    return target;
                }

                return null;
            }

            // A scalar, text or date: the first cell when resizing, the whole range (Excel fills every cell) when not.
            if (autoResize && Count(range) != 1)
            {
                target = Resize(range, 1, 1);
            }

            Set(target, converted is Array one ? Flattened(one) : converted);
            return target;
        }
        catch (Exception ex) when (ex is TargetInvocationException or COMException or MissingMemberException or ArgumentException or InvalidCastException)
        {
            // Excel refused the value (a 3-D array, an array of bytes, an object with no value): nothing
            // is written and the slot keeps the range it had, as R2025b's add-in leaves it.
            return ReferenceEquals(target, range) ? null : target;
        }
    }

    /// <summary>Writes <paramref name="value"/> (null clears) as the range's <c>Value2</c>.</summary>
    internal static void Set(object range, object? value) =>
        range.GetType().InvokeMember("Value2", BindingFlags.SetProperty, null, range, [value], CultureInfo.InvariantCulture);

    /// <summary><c>Range.Resize(rows, cols)</c>, a parameterized property of Excel's: the range from the same first cell with that many rows and columns.</summary>
    private static object Resize(object range, int rows, int cols) =>
        range.GetType().InvokeMember("Resize", BindingFlags.GetProperty, null, range, [rows, cols], CultureInfo.InvariantCulture)!;

    private static int Count(object range) => System.Convert.ToInt32(range.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, range, null, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static int Rows(object range) => Count(range.GetType().InvokeMember("Rows", BindingFlags.GetProperty, null, range, null, CultureInfo.InvariantCulture)!);

    private static int Columns(object range) => Count(range.GetType().InvokeMember("Columns", BindingFlags.GetProperty, null, range, null, CultureInfo.InvariantCulture)!);

    /// <summary>The array with nested arrays and objects Excel cannot hold in a cell blanked (x.rangeout.cellnested: a cell's array element is nothing).</summary>
    private static Array Flattened(Array array)
    {
        if (array.GetType().GetElementType() != typeof(object))
        {
            return array;
        }

        int[] lengths = [.. Enumerable.Range(0, array.Rank).Select(array.GetLength)];
        int[] lower = [.. Enumerable.Range(0, array.Rank).Select(array.GetLowerBound)];
        Array made = Array.CreateInstance(typeof(object), lengths, lower);
        object?[] elements = ColumnMajor(array);
        var subs = new int[array.Rank];
        for (int k = 0; k < elements.Length; k++)
        {
            int rest = k;
            for (int d = 0; d < array.Rank; d++)
            {
                subs[d] = rest % lengths[d] + lower[d];
                rest /= lengths[d];
            }

            object? e = elements[k];
            made.SetValue(e is Array or ComServed ? null : e, subs);
        }

        return made;
    }

    private static Array Transposed(Array array)
    {
        int rows = array.GetLength(0);
        int cols = array.GetLength(1);
        Array made = Array.CreateInstance(array.GetType().GetElementType()!, [cols, rows], [1, 1]);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                made.SetValue(array.GetValue(r + array.GetLowerBound(0), c + array.GetLowerBound(1)), c + 1, r + 1);
            }
        }

        return made;
    }

    private static Array Reshaped(Array array, int rows, int cols)
    {
        object?[] elements = ColumnMajor(array);
        Array made = Array.CreateInstance(array.GetType().GetElementType()!, [rows, cols], [1, 1]);
        for (int k = 0; k < elements.Length && k < rows * cols; k++)
        {
            made.SetValue(elements[k], k % rows + 1, k / rows + 1);
        }

        return made;
    }
}
