namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB implicit expansion (R2016b): two arrays combine elementwise when, dimension by dimension,
/// their sizes match or one of them is 1 — the singleton side is repeated along that dimension, so a
/// column plus a row is their outer sum. One alignment engine serves the elementwise operators,
/// <c>bsxfun</c>, and the two-argument numeric builtins, so the shape rule cannot fork.
/// </summary>
internal static class JgsBroadcast
{
    /// <summary>Whether two array values already have identical dimensions (no expansion needed).</summary>
    public static bool SameShape(JgsValue left, JgsValue right) =>
        JgsMatrix.DimsOf(left).AsSpan().SequenceEqual(JgsMatrix.DimsOf(right));

    /// <summary>
    /// Combines two arrays elementwise under the expansion rule, calling <paramref name="combine"/>
    /// per element pair and shaping the result to the expanded size. Throws the incompatibility
    /// error (naming both sizes) when the rule cannot align them.
    /// </summary>
    public static JgsValue Map(
        JgsValue left, JgsValue right, string symbol, int line, int col,
        Func<JgsValue, JgsValue, JgsValue> combine)
    {
        int[] dimsLeft = JgsMatrix.DimsOf(left);
        int[] dimsRight = JgsMatrix.DimsOf(right);
        int rank = System.Math.Max(dimsLeft.Length, dimsRight.Length);

        var outDims = new int[rank];
        var padLeft = new int[rank];
        var padRight = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            padLeft[d] = d < dimsLeft.Length ? dimsLeft[d] : 1;
            padRight[d] = d < dimsRight.Length ? dimsRight[d] : 1;
            if (padLeft[d] == padRight[d] || padLeft[d] == 1 || padRight[d] == 1)
            {
                outDims[d] = padLeft[d] == 1 ? padRight[d] : padLeft[d];
            }
            else
            {
                // Two plain vectors keep the message scripts have always seen; anything shaped
                // names both sizes.
                bool vectors = dimsLeft.Length == 2 && dimsRight.Length == 2
                    && dimsLeft[0] == 1 && dimsRight[0] == 1;
                throw new JgsRuntimeException(line, col, vectors
                    ? $"Cannot apply '{symbol}' to arrays of different lengths ({left.ArrayLength} and {right.ArrayLength})."
                    : $"Cannot apply '{symbol}' to a {string.Join("x", dimsLeft)} array and a {string.Join("x", dimsRight)} array.");
            }
        }

        // Per-operand strides over their own storage; a singleton dimension gets stride 0, which is
        // the whole expansion trick — its one element is read for every position along that axis.
        var strideLeft = new long[rank];
        var strideRight = new long[rank];
        long runLeft = 1;
        long runRight = 1;
        long total = 1;
        for (int d = 0; d < rank; d++)
        {
            strideLeft[d] = padLeft[d] == 1 ? 0 : runLeft;
            strideRight[d] = padRight[d] == 1 ? 0 : runRight;
            runLeft *= padLeft[d];
            runRight *= padRight[d];
            total *= outDims[d];
        }

        var elements = new JgsValue[total];
        var counter = new int[rank]; // odometer over the result, column-major
        for (long n = 0; n < total; n++)
        {
            long slotLeft = 0;
            long slotRight = 0;
            for (int d = 0; d < rank; d++)
            {
                slotLeft += counter[d] * strideLeft[d];
                slotRight += counter[d] * strideRight[d];
            }

            elements[n] = combine(left.ElementAt((int)slotLeft), right.ElementAt((int)slotRight));
            for (int d = 0; d < rank; d++)
            {
                if (++counter[d] < outDims[d])
                {
                    break;
                }

                counter[d] = 0;
            }
        }

        JgsValue result = JgsMatrix.FromElements(elements, 1, elements.Length);
        result.ReshapeDims(outDims);
        return result;
    }
}
