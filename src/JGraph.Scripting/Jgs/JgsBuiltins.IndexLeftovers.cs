namespace JGraph.Scripting.Jgs;

/// <summary>
/// The leftovers ADR 0354 met in its probe that are not indexing itself (open item 320, ADR 0366):
/// <c>cell</c>'s sizes and <c>reshape</c>'s refusals in R2025b's words.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// MATLAB's <c>cell</c> with numeric sizes (open item 320, measured): <c>cell(n)</c> is n-by-n, a size
    /// vector or any number of sizes is the grid as written, an empty size is 0, a negative one 0,
    /// trailing singletons past the second are dropped; NaN or Inf is <c>MATLAB:nonaninf</c> and a
    /// fraction <c>MATLAB:NonIntegerInput</c>. Every element is R2025b's 0-by-0 double.
    /// </summary>
    private static JgsValue MatlabCell(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var sizes = new List<double>();
        foreach (JgsValue arg in args)
        {
            double[] values = arg.Type is JgsType.Number or JgsType.Bool ? [arg.AsNumber] : ToDoubles("cell", arg, line, col);
            foreach (double value in values)
            {
                if (!double.IsFinite(value))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nonaninf", "NaN and Inf not allowed.");
                }

                if (value != Math.Floor(value))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:NonIntegerInput", "Size inputs must be integers.");
                }
            }

            // cell(2, []) is 2-by-0: an empty size among several is a 0 (measured).
            if (args.Count > 1 && values.Length == 0)
            {
                values = [0];
            }

            sizes.AddRange(values);
        }

        int[] dims = sizes.Count switch
        {
            0 => [0, 0],
            1 => [Size(sizes[0]), Size(sizes[0])],
            _ => [.. sizes.Select(Size)],
        };

        long count = 1;
        foreach (int dim in dims)
        {
            count *= dim;
        }

        if (count > int.MaxValue)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:array:SizeLimitExceeded",
                "Requested array exceeds the maximum possible variable size.");
        }

        var elements = new JgsValue[count];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = JgsEmpty.Zero();
        }

        JgsValue built = JgsValue.Cell(elements);
        built.ReshapeDims(dims);
        return built;

        static int Size(double value) => value > 0 ? (int)Math.Min(value, int.MaxValue) : 0;
    }
}
