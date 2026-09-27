namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// A .NET array under MATLAB's parentheses (interop plan, stage 4, ADR 0177): one 1-based scalar
/// subscript per dimension, read with <c>arr(i)</c> or <c>g(i, j)</c> and written with
/// <c>arr(i) = v</c> into the array itself, which every name for it sees. R2025b's rules, measured
/// (net_arrays, probe4): the subscript count must be the rank (a 2-D array takes no linear index,
/// and <c>arr()</c> is no element) or there is no <c>'()'</c> method; a subscript that is not a
/// positive whole number — zero, a fraction, a logical, a char, a range, <c>:</c>, an empty — is
/// <c>MATLAB:NET:InvalidArrayIndex</c>; <c>end</c> is <c>MATLAB:NET:UnsupportedEndIndexingArray</c>;
/// a position past the bounds is the array's own <c>IndexOutOfRangeException</c>; a value the element
/// type does not take is no <c>'Set'</c> method.
/// </summary>
internal static class NetArrays
{
    /// <summary>The refusal an <c>end</c> inside a .NET array's subscript raises.</summary>
    public static JgsRuntimeException EndRefused(int line, int col) =>
        new(line, col, "MATLAB:NET:UnsupportedEndIndexingArray", "'end' index is not supported for .NET objects.");

    /// <summary><c>arr(i, …)</c>: the element, as a member of the element type answers.</summary>
    public static JgsValue Read(
        NetObject net, Array array, IReadOnlyList<JgsValue?> subscripts, int indexBase, NetCatalog? session, int line, int col)
    {
        int[] at = Positions(net, array, subscripts, indexBase, line, col);
        object? held;
        try
        {
            held = array.GetValue(at);
        }
        catch (IndexOutOfRangeException fault)
        {
            throw NetInvoke.Raise(fault, "MethodInvoke", line, col);
        }

        return NetConvert.ToMatlab(held, array.GetType().GetElementType()!, line, col, session);
    }

    /// <summary><c>arr(i, …) = value</c>: converts as an argument of the element type does, and writes.</summary>
    public static void Write(
        NetObject net, Array array, IReadOnlyList<JgsValue?> subscripts, int indexBase, JgsValue value, int line, int col)
    {
        int[] at = Positions(net, array, subscripts, indexBase, line, col);
        Type element = array.GetType().GetElementType()!;
        if (!Takes(value, element))
        {
            throw NoMethod("Set", net, line, col);
        }

        object? converted = NetConvert.ToNet(value, element, 1, "value", line, col);
        try
        {
            array.SetValue(converted, at);
        }
        catch (IndexOutOfRangeException fault)
        {
            throw NetInvoke.Raise(fault, "MethodInvoke", line, col);
        }
        catch (Exception fault) when (fault is InvalidCastException or ArgumentException)
        {
            throw NoMethod("Set", net, line, col);
        }
    }

    /// <summary>
    /// Whether an element of <paramref name="element"/> takes <paramref name="value"/>: as an argument
    /// would, and one element only — a vector into a scalar slot and a char into a number are refused
    /// (probe4 <c>set.vector</c>, <c>set.char</c>).
    /// </summary>
    private static bool Takes(JgsValue value, Type element)
    {
        if (value.Type == JgsType.External)
        {
            return value.AsExternal is NetObject { Target: var held } && (held is null ? !element.IsValueType : element.IsInstanceOfType(held));
        }

        bool scalarSlot = !element.IsArray && element != typeof(object) && element != typeof(string);
        if (scalarSlot && (value.Type is JgsType.String || value.IsCharMatrix || (value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength != 1)))
        {
            return false;
        }

        return NetConvert.Rank(value, element) is not null;
    }

    private static int[] Positions(NetObject net, Array array, IReadOnlyList<JgsValue?> subscripts, int indexBase, int line, int col)
    {
        if (subscripts.Count != array.Rank)
        {
            throw NoMethod("()", net, line, col);
        }

        var at = new int[subscripts.Count];
        for (int i = 0; i < at.Length; i++)
        {
            at[i] = Position(subscripts[i], indexBase, line, col);
        }

        return at;
    }

    /// <summary>
    /// A subscript as a 0-based position: a real whole number of at least the dialect's first index
    /// (1 in MATLAB, 0 in JGS's brackets), in any numeric class.
    /// </summary>
    private static int Position(JgsValue? subscript, int indexBase, int line, int col)
    {
        double x;
        if (subscript is { Type: JgsType.Number } number)
        {
            x = number.AsNumber;
        }
        else if (subscript is { Type: JgsType.Array, ArrayLength: 1, IsStringArray: false, IsCharMatrix: false } one
            && one.PackedKind != JgsPackedKind.Bool && !one.IsPackedComplex)
        {
            x = one.BoxedElements()[0].AsNumber;
        }
        else
        {
            throw InvalidIndex(line, col);
        }

        if (x < indexBase || x != Math.Floor(x) || x - indexBase >= int.MaxValue)
        {
            throw InvalidIndex(line, col);
        }

        return (int)(x - indexBase);
    }

    private static JgsRuntimeException InvalidIndex(int line, int col) =>
        new(line, col, "MATLAB:NET:InvalidArrayIndex", "Array index must be a positive integer.");

    private static JgsRuntimeException NoMethod(string method, NetObject net, int line, int col) =>
        new(line, col, "MATLAB:class:UndefinedMethod",
            $"No method '{method}' with matching signature found for class '{net.ClassName}'.");
}
