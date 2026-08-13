namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>meshgrid</c> and <c>ndgrid</c>, in as many dimensions as they are given vectors.
/// </summary>
/// <remarks>
/// <para>
/// These were a two-dimensional pair until M59, which is the milestone that needed a scalar reading
/// at every point of a box: <c>[X, Y, Z] = meshgrid(x, y, z)</c> is how a script writes one down, and
/// without it none of the volume verbs can be handed anything to draw.
/// </para>
/// <para>
/// The two names differ only in which vector runs along which dimension. <c>ndgrid</c> is the plain
/// reading — the first vector runs along the first dimension — while <c>meshgrid</c> swaps the first
/// two, so the first vector runs across the columns and the second down the rows. That swap is not an
/// oddity to be tidied away: it is what makes a <c>meshgrid</c> field agree with the row-and-column
/// order <c>surf</c>, <c>contour</c> and every matrix here already use.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static JgsValue Grids(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(verb, args, 1, 8, line, col);

        // One vector means the same vector twice, which is what both names document.
        var vectors = new List<double[]>();
        for (int i = 0; i < args.Count; i++)
        {
            vectors.Add(DoubleArray(verb, args, i, line, col));
        }

        if (vectors.Count == 1)
        {
            vectors.Add(vectors[0]);
        }

        int count = vectors.Count;
        bool swapped = verb.Equals("meshgrid", StringComparison.Ordinal);

        // Which argument supplies the positions along each dimension.
        var sourceOfDim = new int[count];
        for (int d = 0; d < count; d++)
        {
            sourceOfDim[d] = d;
        }

        if (swapped)
        {
            (sourceOfDim[0], sourceOfDim[1]) = (sourceOfDim[1], sourceOfDim[0]);
        }

        var dims = new int[count];
        for (int d = 0; d < count; d++)
        {
            dims[d] = vectors[sourceOfDim[d]].Length;
        }

        if (count == 2)
        {
            // The two-dimensional answer is built directly, so it stays the plain matrix every
            // surface verb in this build already reads.
            double[] first = vectors[0];
            double[] second = vectors[1];
            return swapped
                ? JgsValue.Array([
                    JgsMatrix.Build(second.Length, first.Length, (r, c) => first[c]),
                    JgsMatrix.Build(second.Length, first.Length, (r, c) => second[r]),
                ])
                : JgsValue.Array([
                    JgsMatrix.Build(first.Length, second.Length, (r, c) => first[r]),
                    JgsMatrix.Build(first.Length, second.Length, (r, c) => second[c]),
                ]);
        }

        int total = 1;
        foreach (int size in dims)
        {
            total *= size;
        }

        var grids = new JgsValue[count];
        for (int a = 0; a < count; a++)
        {
            int dimension = Array.IndexOf(sourceOfDim, a);
            double[] positions = vectors[a];
            var flat = new double[total];

            // How far apart two neighbours along this dimension sit in column-major order.
            int stride = 1;
            for (int d = 0; d < dimension; d++)
            {
                stride *= dims[d];
            }

            for (int i = 0; i < total; i++)
            {
                flat[i] = positions[i / stride % positions.Length];
            }

            grids[a] = JgsMatrix.FromColumnMajorDims(flat, dims);
        }

        return JgsValue.Array(grids);
    }
}
