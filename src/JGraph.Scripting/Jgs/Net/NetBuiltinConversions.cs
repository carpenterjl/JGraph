namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// What MATLAB's conversion functions make of a .NET value (interop plan, stages 1 and 4, ADRs 0174
/// and 0177): <c>char</c> and <c>string</c> of a <c>System.String</c> and of an enum member; the
/// numeric classes and <c>logical</c> of a numeric .NET array (a rectangular jagged one as a matrix);
/// <c>string</c> of a <c>String[]</c>; <c>cell</c> of an array of objects, strings or arrays. An enum
/// member converts to <c>double</c> and to its own underlying class only. Anything else is refused
/// with R2025b's <c>MATLAB:invalidConversion</c> — <c>double(System.String('x'))</c>,
/// <c>double(decimal)</c>, <c>char</c> of a <c>Char[]</c> and <c>cell</c> of a <c>Double[]</c>
/// included (measured, net_basics, net_conversions, net_arrays, net_enums, probe4).
/// </summary>
internal static class NetBuiltinConversions
{
    public static JgsValue Numeric(string name, JgsNumericClass numericClass, JgsValue value, int line, int col)
    {
        Alive(value, line, col);
        if (value.AsExternal is NetObject { Target: not null } enumNet && enumNet.Type.IsEnum
            && numericClass != JgsNumericClass.Double && numericClass != UnderlyingClass(enumNet.Type))
        {
            throw Refused(name, value, line, col);
        }

        return value.AsExternal is NetObject net && NumbersOf(name, net, numericClass, line, col) is { } numbers
            ? numbers
            : throw Refused(name, value, line, col);
    }

    public static JgsValue Logical(JgsValue value, int line, int col)
    {
        Alive(value, line, col);
        if (value.AsExternal is NetObject { Target: bool[] flags })
        {
            JgsValue[] cells = flags.Select(JgsValue.Bool).ToArray();
            return JgsValue.Shaped(cells, 1, cells.Length, JgsPackedKind.Bool);
        }

        if (value.AsExternal is NetObject { Target: not null } net && !net.Type.IsEnum
            && NumbersOf("logical", net, JgsNumericClass.Double, line, col) is { } numbers)
        {
            double[] flat = JgsBuiltins.ToDoubles("logical", numbers, line, col);
            JgsValue[] cells = flat.Select(static x => JgsValue.Bool(x != 0)).ToArray();
            return JgsValue.Shaped(cells, numbers.Rows, numbers.Cols, JgsPackedKind.Bool);
        }

        throw Refused("logical", value, line, col);
    }

    public static JgsValue Char(JgsValue value, int line, int col) => Alive(value, line, col).AsExternal switch
    {
        NetObject { Target: string text } => JgsValue.Str(text),
        NetObject { Target: { } member } net when net.Type.IsEnum => JgsValue.Str(member.ToString()!),
        _ => throw Refused("char", value, line, col),
    };

    public static JgsValue String(JgsValue value, int line, int col)
    {
        switch (Alive(value, line, col).AsExternal)
        {
            case NetObject { Target: string text }:
                return JgsValue.StringScalar(text);
            case NetObject { Target: string?[] texts }:
                JgsValue[] cells = texts.Select(static t => JgsValue.Str(t ?? JgsBuiltins.MissingSentinel)).ToArray();
                return JgsValue.StringArray(cells, 1, cells.Length);
            case NetObject { Target: { } member } net when net.Type.IsEnum:
                return JgsValue.StringScalar(member.ToString()!);
            default:
                throw Refused("string", value, line, col);
        }
    }

    /// <summary>
    /// <c>cellstr</c> of a .NET value: an enum member's name in a cell (probe4 <c>enum.cellstr</c>) or a
    /// <c>System.String</c>'s text; a <c>String[]</c> and everything else refused (net_arrays).
    /// </summary>
    public static JgsValue CellStr(JgsValue value, int line, int col) => Alive(value, line, col).AsExternal switch
    {
        NetObject { Target: string text } => JgsValue.Cell([JgsValue.Str(text)]),
        NetObject { Target: { } member } net when net.Type.IsEnum => JgsValue.Cell([JgsValue.Str(member.ToString()!)]),
        _ => throw Refused("cellstr", value, line, col),
    };

    /// <summary>
    /// <c>cell</c> of a one-dimensional array of a reference type: a row, each element as the element
    /// type answers it — a <c>String[]</c>'s as char, an <c>Object[]</c>'s as a member declared
    /// <c>Object</c> answers (a string stays a <c>System.String</c>), a jagged array's rows as MATLAB
    /// arrays. An array of numbers, logicals or chars, and a 2-D array, are refused (probe4).
    /// </summary>
    public static JgsValue Cell(JgsValue value, int line, int col)
    {
        Alive(value, line, col);
        if (value.AsExternal is NetObject { Target: Array array } net && array.Rank == 1
            && net.Type.GetElementType() is { IsPrimitive: false, IsEnum: false } element)
        {
            JgsValue[] cells = new JgsValue[array.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                object? held = array.GetValue(i);
                cells[i] = held switch
                {
                    string text when element == typeof(string) => JgsValue.Str(text),
                    Array inner when element.IsArray => Unfolded(inner, line, col),
                    _ => NetConvert.ToMatlab(held, element, line, col),
                };
            }

            JgsValue row = JgsValue.Cell(cells);
            row.Reshape(1, cells.Length);
            return row;
        }

        throw Refused("cell", value, line, col);
    }

    /// <summary>A jagged array's row in a cell: its numbers in their own class, or a cell of what it holds.</summary>
    private static JgsValue Unfolded(Array inner, int line, int col)
    {
        var net = new NetObject(inner, inner.GetType());
        Type element = inner.GetType().GetElementType()!;
        if (IsNumericElement(element))
        {
            return NumbersOf("cell", net, ClassOfElement(element), line, col)!;
        }

        return inner.Rank == 1 && !element.IsPrimitive ? Cell(JgsValue.External(net), line, col) : JgsValue.External(net);
    }

    /// <summary>
    /// A .NET value's numbers in <paramref name="wanted"/>: a numeric array of rank 1 (a row) or 2, a
    /// jagged array whose rows are numeric and of one length (as a matrix, a missing row as none), or an
    /// enum member's value. Null when it has none. A jagged array with rows of two lengths is R2025b's
    /// <c>NonRectJaggedArray</c> (net_arrays).
    /// </summary>
    private static JgsValue? NumbersOf(string name, NetObject net, JgsNumericClass wanted, int line, int col)
    {
        object? target = net.Target;

        // A Double[] or Double[,] is copied without boxing each element (64 µs for 100 before).
        if (target is double[] doubles)
        {
            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor((double[])doubles.Clone(), 1, doubles.Length), wanted);
        }

        if (target is double[,] grid)
        {
            int gridRows = grid.GetLength(0);
            int gridCols = grid.GetLength(1);
            var columnMajor = new double[grid.Length];
            for (int c = 0; c < gridCols; c++)
            {
                for (int r = 0; r < gridRows; r++)
                {
                    columnMajor[r + (c * gridRows)] = grid[r, c];
                }
            }

            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor(columnMajor, gridRows, gridCols), wanted);
        }

        if (target is Array array && array.Rank <= 2 && IsNumericElement(array.GetType().GetElementType()!))
        {
            int rows = array.Rank == 1 ? 1 : array.GetLength(0);
            int cols = array.Rank == 1 ? array.Length : array.GetLength(1);
            var flat = new double[array.Length];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    object? held = array.Rank == 1 ? array.GetValue(c) : array.GetValue(r, c);
                    flat[r + (c * rows)] = System.Convert.ToDouble(held, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor(flat, rows, cols), wanted);
        }

        if (target is Array jagged && jagged.Rank == 1 && jagged.GetType().GetElementType() is { IsArray: true } row
            && row.GetArrayRank() == 1 && IsNumericElement(row.GetElementType()!))
        {
            int rows = jagged.Length;
            int cols = -1;
            for (int r = 0; r < rows; r++)
            {
                int length = (jagged.GetValue(r) as Array)?.Length ?? 0;
                if (cols >= 0 && length != cols)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:NET:NetConversion:NonRectJaggedArray",
                        $"Error converting input type to {name}.  The input must be a jagged array of rectangular shape.  Consider calling cell.");
                }

                cols = length;
            }

            cols = Math.Max(cols, 0);
            var flat = new double[rows * cols];
            for (int r = 0; r < rows; r++)
            {
                if (jagged.GetValue(r) is Array entries)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        flat[r + (c * rows)] = System.Convert.ToDouble(entries.GetValue(c), System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }

            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor(flat, rows, cols), wanted);
        }

        if (target is not null && target.GetType().IsEnum)
        {
            return JgsNumericClasses.Stamp(JgsValue.Number(System.Convert.ToDouble(target, System.Globalization.CultureInfo.InvariantCulture)), wanted);
        }

        return null;
    }

    /// <summary>The MATLAB class an enum's underlying type is, the one integer class it converts to.</summary>
    internal static JgsNumericClass UnderlyingClass(Type enumType) => ClassOfElement(Enum.GetUnderlyingType(enumType));

    private static JgsNumericClass ClassOfElement(Type element) => Type.GetTypeCode(element) switch
    {
        TypeCode.SByte => JgsNumericClass.Int8,
        TypeCode.Byte => JgsNumericClass.UInt8,
        TypeCode.Int16 => JgsNumericClass.Int16,
        TypeCode.UInt16 => JgsNumericClass.UInt16,
        TypeCode.Int32 => JgsNumericClass.Int32,
        TypeCode.UInt32 => JgsNumericClass.UInt32,
        TypeCode.Int64 => JgsNumericClass.Int64,
        TypeCode.UInt64 => JgsNumericClass.UInt64,
        TypeCode.Single => JgsNumericClass.Single,
        _ => JgsNumericClass.Double,
    };

    private static bool IsNumericElement(Type element) => !element.IsEnum && Type.GetTypeCode(element) is
        TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double;

    /// <summary>
    /// R2025b's refusal of a .NET value where a reduction wants numbers, for the reductions measured
    /// (probe4: <c>sum</c> and <c>mean</c> say <c>sum:wrongInput</c>, <c>max</c> its own); null for any
    /// other value or builtin, which keeps the builtin's own sentence.
    /// </summary>
    public static JgsRuntimeException? NotNumeric(string builtin, JgsValue value, int line, int col) =>
        value.Type != JgsType.External ? null
        : builtin is "sum" or "mean" ? new(line, col, "MATLAB:sum:wrongInput", "Invalid data type. First argument must be numeric or logical.")
        : builtin is "max" ? new(line, col, "MATLAB:max:wrongInput", "First input array is an invalid data type.")
        : null;

    /// <summary>The value, or R2025b's refusal of a deleted handle (probe5h: <c>char</c> of a deleted <c>System.String</c>).</summary>
    private static JgsValue Alive(JgsValue value, int line, int col)
    {
        (value.AsExternalOrNull() as NetObject)?.Live(line, col);
        return value;
    }

    private static JgsRuntimeException Refused(string name, JgsValue value, int line, int col) =>
        new(line, col, "MATLAB:invalidConversion",
            $"Conversion to {name} from {value.AsExternal.ClassName} is not possible.");
}
