using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>repmat</c> as R2025b answers it in MATLAB code (open item 127): any number of counts, given as
/// one row or as separate scalars, so an answer can have a third dimension and more; the counts
/// checked the way R2025b checks them; and every kind of array tiled in the container it came in.
/// </summary>
/// <remarks>
/// <para>
/// The two-dimensional tiling below the dialect check is older than shaped arrays and is kept for
/// JGS code, which keeps its own sentences. MATLAB code goes through <see cref="RepmatMatlab"/>:
/// the answer's element at each place is the source element at that place modulo the source's
/// extent, dimension by dimension, so the tiling is one gather by position whatever the elements
/// are — numbers, characters, cells, strings, structs. An array of objects and a categorical reach
/// here as their stand-ins (positions and codes) through the wrappers outside this one, so they come
/// with it.
/// </para>
/// <para>
/// The refusals were measured (probe_127): a function handle cannot be tiled at all, not even once
/// (<c>MATLAB:err_non_scalar_function_handles</c>); a table cannot take a third count
/// (<c>MATLAB:table:NDSubscript</c>); a sparse matrix stays sparse in two dimensions and cannot
/// have a third (<c>MATLAB:repmat:NdSparseOutput</c>); a count that is not a whole number, a column,
/// an empty or a vector beside other counts is <c>MATLAB:repmat:invalidReplications</c>, and text is
/// <c>MATLAB:repmat:nonNumericReplications</c>.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>R2025b's refusal of an array of function handles, which <c>[@sin @cos]</c> and <c>repmat(@sin, 1, 2)</c> both meet.</summary>
    internal static JgsRuntimeException NonScalarFunctionHandles(int line, int col) =>
        new(line, col, "MATLAB:err_non_scalar_function_handles",
            "Nonscalar arrays of function handles are not allowed; use cell arrays instead.");

    /// <summary><c>repmat</c> in MATLAB code: the counts read as R2025b reads them, then the tiling.</summary>
    private static JgsValue RepmatMatlab(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        JgsValue source = args[0];
        if (source.Type == JgsType.Function)
        {
            throw NonScalarFunctionHandles(line, col);
        }

        int[] reps = RepmatCounts(args, line, col);
        int[] sourceDims = RepmatSourceDims(source);
        bool flat = reps.Length == 2 && sourceDims.Length == 2;
        switch (source.Type)
        {
            case JgsType.Table when !flat:
                throw new JgsRuntimeException(line, col, "MATLAB:table:NDSubscript",
                    "Subscripting into a table using three or more subscripts (as in t(i,j,k)) is not supported. Always specify a row subscript and a variable subscript, as in t(rows,vars).");
            case JgsType.Sparse when !flat:
                throw new JgsRuntimeException(line, col, "MATLAB:repmat:NdSparseOutput", "N-D sparse output arrays are not supported.");
            case JgsType.Sparse:
            {
                JGraph.Numerics.Sparse.CscMatrix matrix = source.AsSparse;
                JgsValue tiled = RepmatGather(JgsMatrix.FromColumnMajor(matrix.ToColumnMajor(), matrix.Rows, matrix.Cols), [matrix.Rows, matrix.Cols], reps);
                double[] full = ColumnMajorOf("repmat", tiled, out int rows, out int cols, line, col);
                return JgsValue.Sparse(JGraph.Numerics.Sparse.CscMatrix.FromColumnMajor(full, rows, cols));
            }
        }

        // Two counts over a two-dimensional source keep the tiling MATLAB code has always had, save
        // for a struct, which that road answered as an array of numbers (probe_127).
        if (flat && source.Type is not (JgsType.Struct or JgsType.Complex) && !source.IsPackedComplex)
        {
            return Repmat2D(source, reps[0], reps[1]);
        }

        return RepmatGather(source, sourceDims, reps);
    }

    /// <summary>
    /// The replication counts, in R2025b's rules: one argument is a scalar (that many down and
    /// across) or a row of counts; several are one scalar count each; a negative count is none.
    /// Trailing ones past the second are dropped, so <c>[2 2 1]</c> is a two-dimensional tiling.
    /// </summary>
    private static int[] RepmatCounts(IReadOnlyList<JgsValue> args, int line, int col)
    {
        static JgsRuntimeException Invalid(int line, int col) =>
            new(line, col, "MATLAB:repmat:invalidReplications", "Replication factors must be a row vector of integers or integer scalars.");

        for (int i = 1; i < args.Count; i++)
        {
            JgsValue count = args[i];
            if (count.Type is not (JgsType.Number or JgsType.Bool or JgsType.Array) || count.IsStringArray || count.IsCharMatrix || count.IsCategorical)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:repmat:nonNumericReplications", "Replication factors must be numeric.");
            }
        }

        var counts = new List<int>();
        void Take(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Floor(value))
            {
                throw Invalid(line, col);
            }

            counts.Add((int)Math.Max(0, Math.Min(value, int.MaxValue)));
        }

        if (args.Count == 2)
        {
            JgsValue row = args[1];
            if (row.Type == JgsType.Array && (row.ArrayLength == 0 || row.DimCount > 2 || row.Rows != 1))
            {
                throw Invalid(line, col);
            }

            foreach (double value in ToDoubles("repmat", row, line, col))
            {
                Take(value);
            }

            if (counts.Count == 1)
            {
                counts.Add(counts[0]);
            }
        }
        else
        {
            for (int i = 1; i < args.Count; i++)
            {
                JgsValue scalar = args[i];
                if (scalar.Type == JgsType.Array && scalar.ArrayLength != 1)
                {
                    throw Invalid(line, col);
                }

                Take(scalar.Type == JgsType.Array ? scalar.ElementAt(0).AsNumber : scalar.AsNumber);
            }
        }

        while (counts.Count > 2 && counts[^1] == 1)
        {
            counts.RemoveAt(counts.Count - 1);
        }

        return [.. counts];
    }

    /// <summary>The source's size as MATLAB counts it: a char row is 1-by-n (and <c>''</c> 0-by-0), a scalar 1-by-1.</summary>
    private static int[] RepmatSourceDims(JgsValue source) => source.Type switch
    {
        JgsType.String => source.AsString.Length == 0 ? [0, 0] : [1, source.AsString.Length],
        JgsType.Array or JgsType.Cell or JgsType.Struct or JgsType.Object => source.Dims,
        JgsType.Sparse => [source.AsSparse.Rows, source.AsSparse.Cols],
        _ => [1, 1],
    };

    /// <summary>The two-count tiling MATLAB code took before open item 127, by container.</summary>
    private static JgsValue Repmat2D(JgsValue source, int down, int across)
    {
        if (source.Type == JgsType.String)
        {
            return TiledCharRow(source.AsString, down, across);
        }

        if (source.IsStringArray)
        {
            return TiledStringArray(source, down, across);
        }

        if (source.Type == JgsType.Cell)
        {
            return TiledCell(source, down, across);
        }

        if (source.Type != JgsType.Array)
        {
            return JgsMatrix.BuildValues(down, across, (_, _) => source);
        }

        int rows = JgsMatrix.RowCount(source);
        int cols = JgsMatrix.ColCount(source);
        return JgsMatrix.BuildValues(
            rows * down, cols * across, (r, c) => JgsMatrix.At(source, r % rows, c % cols));
    }

    /// <summary>
    /// The tiling as one gather: each place of the answer takes the source element at that place
    /// modulo the source's extent in every dimension, and the answer is the source's own container.
    /// </summary>
    private static JgsValue RepmatGather(JgsValue source, int[] sourceDims, int[] reps)
    {
        int n = Math.Max(sourceDims.Length, reps.Length);
        var from = new int[n];
        var dims = new int[n];
        long total = 1;
        for (int k = 0; k < n; k++)
        {
            from[k] = k < sourceDims.Length ? sourceDims[k] : 1;
            dims[k] = from[k] * (k < reps.Length ? reps[k] : 1);
            total *= dims[k];
        }

        if (total > int.MaxValue)
        {
            throw new JgsRuntimeException(0, 0, "MATLAB:array:SizeLimitExceeded", "Requested array exceeds the maximum possible variable size.");
        }

        var picks = new int[total];
        for (int i = 0; i < picks.Length; i++)
        {
            long rest = i;
            int stride = 1;
            int at = 0;
            for (int k = 0; k < n; k++)
            {
                int sub = (int)(rest % dims[k]);
                rest /= dims[k];
                at += sub % from[k] * stride;
                stride *= from[k];
            }

            picks[i] = at;
        }

        switch (source.Type)
        {
            case JgsType.String:
            {
                string text = source.AsString;
                var codes = new double[picks.Length];
                for (int i = 0; i < codes.Length; i++)
                {
                    codes[i] = text[picks[i]];
                }

                return JgsMatrix.FromColumnMajorDims(codes, dims).MarkCharMatrix();
            }

            case JgsType.Cell:
            {
                JgsValue[] cells = source.AsCell;
                var picked = new JgsValue[picks.Length];
                for (int i = 0; i < picked.Length; i++)
                {
                    picked[i] = JgsValue.Share(cells[picks[i]]); // M2: each tile is an entry of its own
                }

                JgsValue cell = JgsValue.Cell(picked);
                cell.ReshapeDims(dims);
                return cell;
            }

            case JgsType.Struct:
            {
                JgsStructArray structs = source.AsStructArray;
                var picked = new Dictionary<string, JgsValue>[picks.Length];
                for (int i = 0; i < picked.Length; i++)
                {
                    picked[i] = JgsStructArray.SharedCopy(structs.Elements[picks[i]]);
                }

                JgsValue tiled = JgsValue.StructArray(new JgsStructArray(picked, structs.FieldNames), 1, picked.Length);
                tiled.ReshapeDims(dims);
                return tiled;
            }

            case JgsType.Complex:
            {
                var parts = new Complex[picks.Length];
                Array.Fill(parts, source.AsComplex);
                return ComplexStorage(parts, dims);
            }
        }

        if (source.Type == JgsType.Array && source.IsPackedComplex)
        {
            Complex[] elements = ColumnComplex("repmat", source, 0, 0);
            var parts = new Complex[picks.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = elements[picks[i]];
            }

            return ComplexStorage(parts, dims, source.AsPackedComplex.PreserveComplex);
        }

        if (source.Type == JgsType.Array && source.IsStringArray)
        {
            var picked = new JgsValue[picks.Length];
            for (int i = 0; i < picked.Length; i++)
            {
                picked[i] = source.ElementAt(picks[i]);
            }

            JgsValue strings = JgsValue.StringArray(picked, 1, picked.Length);
            strings.ReshapeDims(dims);
            return strings;
        }

        if (source.Type == JgsType.Array && source.IsPacked)
        {
            NumericBuffer buffer = source.AsBuffer;
            double[] stored = buffer.AsSpan().ToArray();
            GC.KeepAlive(buffer);
            var values = new double[picks.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = stored[picks[i]];
            }

            JgsValue packed = JgsMatrix.FromColumnMajorDims(values, dims);
            return source.IsCharMatrix ? packed.MarkCharMatrix() : packed;
        }

        if (source.Type == JgsType.Array)
        {
            var picked = new JgsValue[picks.Length];
            for (int i = 0; i < picked.Length; i++)
            {
                picked[i] = source.ElementAt(picks[i]);
            }

            JgsValue boxed = picked.Length == 1 ? JgsValue.Array(picked) : JgsMatrix.FromElementsDims(picked, dims);
            boxed.ReshapeDims(dims);
            return source.IsCharMatrix ? boxed.MarkCharMatrix() : boxed;
        }

        // A number or a logical: the same value at every place.
        if (source.Type == JgsType.Number)
        {
            var values = new double[picks.Length];
            Array.Fill(values, source.AsNumber);
            return JgsMatrix.FromColumnMajorDims(values, dims);
        }

        var same = new JgsValue[picks.Length];
        Array.Fill(same, source);
        JgsValue filled = JgsValue.Array(same);
        filled.ReshapeDims(dims);
        return filled;
    }
}
