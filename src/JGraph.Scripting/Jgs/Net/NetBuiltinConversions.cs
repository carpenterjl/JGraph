namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// What MATLAB's conversion functions make of a .NET value (interop plan, stage 1, ADR 0174):
/// <c>char</c> and <c>string</c> of a <c>System.String</c>, the numeric classes and <c>logical</c> of
/// a numeric .NET array or an enum member, <c>cell</c> of an array. Anything else is refused with
/// R2025b's <c>MATLAB:invalidConversion</c> — <c>double(System.String('x'))</c> and
/// <c>double(decimal)</c> included (measured, net_basics and net_conversions).
/// </summary>
internal static class NetBuiltinConversions
{
    public static JgsValue Numeric(string name, JgsNumericClass numericClass, JgsValue value, int line, int col) =>
        value.AsExternal is NetObject net && NetConvert.NumbersOf(net, numericClass) is { } numbers
            ? numbers
            : throw Refused(name, value, line, col);

    public static JgsValue Logical(JgsValue value, int line, int col)
    {
        if (value.AsExternal is NetObject { Target: bool[] flags })
        {
            JgsValue[] cells = flags.Select(JgsValue.Bool).ToArray();
            return JgsValue.Shaped(cells, 1, cells.Length, JgsPackedKind.Bool);
        }

        if (value.AsExternal is NetObject net && NetConvert.NumbersOf(net, JgsNumericClass.Double) is { } numbers)
        {
            double[] flat = JgsBuiltins.ToDoubles("logical", numbers, line, col);
            JgsValue[] cells = flat.Select(static x => JgsValue.Bool(x != 0)).ToArray();
            return JgsValue.Shaped(cells, numbers.Rows, numbers.Cols, JgsPackedKind.Bool);
        }

        throw Refused("logical", value, line, col);
    }

    public static JgsValue Char(JgsValue value, int line, int col) => value.AsExternal switch
    {
        NetObject { Target: string text } => JgsValue.Str(text),
        NetObject { Target: char[] characters } => JgsValue.Str(new string(characters)),
        NetObject { Target: { } member } net when net.Type.IsEnum => JgsValue.Str(member.ToString()!),
        _ => throw Refused("char", value, line, col),
    };

    public static JgsValue String(JgsValue value, int line, int col)
    {
        switch (value.AsExternal)
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

    public static JgsValue Cell(JgsValue value, int line, int col)
    {
        if (value.AsExternal is NetObject { Target: Array array } net && array.Rank == 1)
        {
            Type element = net.Type.GetElementType()!;
            JgsValue[] cells = new JgsValue[array.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                object? held = array.GetValue(i);
                cells[i] = held is string text ? JgsValue.Str(text) : NetConvert.ToMatlab(held, element, line, col);
            }

            JgsValue row = JgsValue.Cell(cells);
            row.Reshape(1, cells.Length);
            return row;
        }

        throw Refused("cell", value, line, col);
    }

    private static JgsRuntimeException Refused(string name, JgsValue value, int line, int col) =>
        new(line, col, "MATLAB:invalidConversion",
            $"Conversion to {name} from {value.AsExternal.ClassName} is not possible.");
}
