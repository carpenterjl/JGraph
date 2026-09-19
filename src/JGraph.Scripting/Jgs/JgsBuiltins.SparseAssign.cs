using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// V6 (ADR 0167): indexed assignment into a sparse matrix — <c>S(i, j) = v</c>, <c>S(k) = v</c>,
/// <c>S(mask) = v</c>, <c>S(i, :) = row</c> and deletion. A <see cref="CscMatrix"/> is immutable, so
/// a write is a rebuild of the stored entries: the entries the write names are replaced, a written
/// zero is dropped rather than stored, a subscript past the extent grows the matrix, and the answer
/// is sparse. Nothing here expands the target to its dense size; only the right-hand side is read
/// as the values it is.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string SparseSizeMismatch =
        "Unable to perform assignment because the indices on the left side are not compatible with the size of the right side.";

    /// <summary>
    /// <paramref name="matrix"/> with <paramref name="rhs"/> written at the subscripts (a null
    /// subscript is <c>:</c>), or with the named rows, columns or elements removed when
    /// <paramref name="rhs"/> is <c>[]</c>.
    /// </summary>
    internal static CscMatrix SparseAssigned(
        CscMatrix matrix, IReadOnlyList<JgsValue?> subscripts, JgsValue rhs, JgsDialect dialect, int line, int col)
    {
        if (subscripts.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "N-dimensional indexing allowed for full matrices only.");
        }

        if (rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray)
        {
            return SparseDeleted(matrix, subscripts, dialect, line, col);
        }

        double[]? values = SparseWrittenValues(rhs, line, col, out double scalar);
        return subscripts.Count == 1
            ? SparseAssignedLinear(matrix, subscripts[0], values, scalar, dialect, line, col)
            : SparseAssignedBlock(matrix, subscripts[0], subscripts[1], values, scalar, rhs, dialect, line, col);
    }

    /// <summary>The right-hand side as column-major doubles, or null with <paramref name="scalar"/> set for one value.</summary>
    private static double[]? SparseWrittenValues(JgsValue rhs, int line, int col, out double scalar)
    {
        scalar = 0;
        switch (rhs.Type)
        {
            case JgsType.Number:
                scalar = rhs.AsNumber;
                return null;
            case JgsType.Bool:
                scalar = rhs.AsBool ? 1 : 0;
                return null;
            case JgsType.Sparse:
                return Single(rhs.AsSparse.ToColumnMajor(), out scalar);
            case JgsType.Array when !rhs.IsStringArray && !HasComplexElements(rhs):
                return Single(ToDoubles("sparse assignment", rhs, line, col), out scalar);
            case JgsType.Complex:
            case JgsType.Array when HasComplexElements(rhs):
                throw new JgsRuntimeException(line, col,
                    "A sparse matrix here holds real values; a complex value cannot be assigned into one.");
            default:
                throw new JgsRuntimeException(line, col, $"Conversion to double from {ClassOf(rhs, JgsDialect.Matlab)} is not possible.");
        }

        static double[]? Single(double[] flat, out double one)
        {
            one = flat.Length == 1 ? flat[0] : 0;
            return flat.Length == 1 ? null : flat;
        }
    }

    private static CscMatrix SparseAssignedBlock(
        CscMatrix matrix, JgsValue? rowIndex, JgsValue? colIndex, double[]? values, double scalar, JgsValue rhs,
        JgsDialect dialect, int line, int col)
    {
        int[] rows = SparseWritePicks(rowIndex, matrix.Rows, dialect, line, col);
        int[] cols = SparseWritePicks(colIndex, matrix.Cols, dialect, line, col);
        if (values is not null)
        {
            // The right-hand side must be the selection's shape; a vector may lie either way.
            int rhsRows = rhs.Type == JgsType.Sparse ? rhs.AsSparse.Rows : rhs.Rows;
            bool vector = rows.Length == 1 || cols.Length == 1;
            if (values.Length != (long)rows.Length * cols.Length || (!vector && rhsRows != rows.Length))
            {
                throw new JgsRuntimeException(line, col, SparseSizeMismatch);
            }
        }

        int newRows = Math.Max(matrix.Rows, rows.Length == 0 ? 0 : rows.Max() + 1);
        int newCols = Math.Max(matrix.Cols, cols.Length == 0 ? 0 : cols.Max() + 1);
        Dictionary<long, double> entries = SparseEntries(matrix, newRows);
        for (int c = 0; c < cols.Length; c++)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                SparsePut(entries, ((long)cols[c] * newRows) + rows[r], values is null ? scalar : values[(c * rows.Length) + r]);
            }
        }

        return SparseFromEntries(entries, newRows, newCols);
    }

    private static CscMatrix SparseAssignedLinear(
        CscMatrix matrix, JgsValue? index, double[]? values, double scalar, JgsDialect dialect, int line, int col)
    {
        long total = (long)matrix.Rows * matrix.Cols;
        int[] picks = SparseWritePicks(index, checked((int)total), dialect, line, col);
        if (values is not null && values.Length != picks.Length)
        {
            throw new JgsRuntimeException(line, col,
                "Unable to perform assignment because the left and right sides have a different number of elements.");
        }

        int newRows = matrix.Rows, newCols = matrix.Cols;
        int highest = picks.Length == 0 ? -1 : picks.Max();
        if (highest >= total)
        {
            // Only a vector (or nothing at all) can grow along a linear subscript.
            if (matrix.Rows == 1 || total == 0)
            {
                newRows = 1;
                newCols = highest + 1;
            }
            else if (matrix.Cols == 1)
            {
                newRows = highest + 1;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "In an assignment  A(I) = B, a matrix A cannot be resized.");
            }
        }

        Dictionary<long, double> entries = SparseEntries(matrix, newRows);
        for (int k = 0; k < picks.Length; k++)
        {
            // A linear position is column-major over the rows the matrix has now.
            long r = picks[k] % newRows, c = picks[k] / newRows;
            SparsePut(entries, (c * newRows) + r, values is null ? scalar : values[k]);
        }

        return SparseFromEntries(entries, newRows, newCols);
    }

    private static CscMatrix SparseDeleted(
        CscMatrix matrix, IReadOnlyList<JgsValue?> subscripts, JgsDialect dialect, int line, int col)
    {
        if (subscripts.Count == 1)
        {
            if (subscripts[0] is null)
            {
                return CscMatrix.FromTriplets(0, 0, []);
            }

            if (matrix.Rows != 1 && matrix.Cols != 1)
            {
                throw new JgsRuntimeException(line, col,
                    "Deleting elements of a sparse matrix by one subscript needs a vector; name the rows or the columns.");
            }

            int[] gone = SparseReadPicks(subscripts[0]!, matrix.Rows * matrix.Cols, dialect, line, col);
            return matrix.Rows == 1
                ? SparseWithout(matrix, [], gone)
                : SparseWithout(matrix, gone, []);
        }

        bool allRows = subscripts[0] is null, allCols = subscripts[1] is null;
        if (!allRows && !allCols)
        {
            throw new JgsRuntimeException(line, col,
                "A null assignment can have only one non-colon index.");
        }

        if (allRows && allCols)
        {
            return CscMatrix.FromTriplets(0, matrix.Cols, []);
        }

        return allCols
            ? SparseWithout(matrix, SparseReadPicks(subscripts[0]!, matrix.Rows, dialect, line, col), [])
            : SparseWithout(matrix, [], SparseReadPicks(subscripts[1]!, matrix.Cols, dialect, line, col));
    }

    /// <summary><paramref name="matrix"/> without the given rows and columns, the rest closed up.</summary>
    private static CscMatrix SparseWithout(CscMatrix matrix, int[] rows, int[] cols)
    {
        int[] rowMap = Remap(matrix.Rows, rows, out int keptRows);
        int[] colMap = Remap(matrix.Cols, cols, out int keptCols);
        var triplets = new List<(int, int, double)>(matrix.NonZeroCount);
        for (int c = 0; c < matrix.Cols; c++)
        {
            if (colMap[c] < 0)
            {
                continue;
            }

            for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
            {
                int r = rowMap[matrix.RowIndices[k]];
                if (r >= 0)
                {
                    triplets.Add((r, colMap[c], matrix.Values[k]));
                }
            }
        }

        return CscMatrix.FromTriplets(keptRows, keptCols, triplets);

        static int[] Remap(int extent, int[] gone, out int kept)
        {
            var map = new int[extent];
            foreach (int g in gone)
            {
                map[g] = -1;
            }

            kept = 0;
            for (int i = 0; i < extent; i++)
            {
                map[i] = map[i] < 0 ? -1 : kept++;
            }

            return map;
        }
    }

    /// <summary>The stored entries keyed by column-major position over <paramref name="rows"/> rows.</summary>
    private static Dictionary<long, double> SparseEntries(CscMatrix matrix, int rows)
    {
        var entries = new Dictionary<long, double>(matrix.NonZeroCount);
        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
            {
                entries[((long)c * rows) + matrix.RowIndices[k]] = matrix.Values[k];
            }
        }

        return entries;
    }

    private static void SparsePut(Dictionary<long, double> entries, long key, double value)
    {
        if (value == 0)
        {
            entries.Remove(key); // a written zero is no entry at all
        }
        else
        {
            entries[key] = value;
        }
    }

    private static CscMatrix SparseFromEntries(Dictionary<long, double> entries, int rows, int cols)
    {
        var triplets = new List<(int, int, double)>(entries.Count);
        foreach (KeyValuePair<long, double> entry in entries)
        {
            triplets.Add(((int)(entry.Key % rows), (int)(entry.Key / rows), entry.Value));
        }

        return CscMatrix.FromTriplets(rows, cols, triplets);
    }

    /// <summary>
    /// The 0-based positions a write subscript names over an extent it may exceed: <c>:</c>, a
    /// mask (logical array, or a sparse one's nonzeros), a position or an array of them.
    /// </summary>
    private static int[] SparseWritePicks(JgsValue? index, int extent, JgsDialect dialect, int line, int col)
    {
        if (index is null)
        {
            var all = new int[extent];
            for (int i = 0; i < extent; i++)
            {
                all[i] = i;
            }

            return all;
        }

        if (index.Type == JgsType.Sparse)
        {
            double[] flat = index.AsSparse.ToColumnMajor();
            return Enumerable.Range(0, flat.Length).Where(i => flat[i] != 0).ToArray();
        }

        if (index.Type == JgsType.Bool)
        {
            return index.AsBool ? [0] : [];
        }

        double[] raw = ToDoubles("subscript", index, line, col);
        bool mask = index.Type == JgsType.Array && index.ArrayLength > 0
            && (index.IsPacked ? index.PackedKind == JgsPackedKind.Bool : index.AsArray.All(v => v.Type == JgsType.Bool));
        if (mask)
        {
            return Enumerable.Range(0, raw.Length).Where(i => raw[i] != 0).ToArray();
        }

        var picks = new int[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            if (!double.IsFinite(raw[i]) || raw[i] != Math.Truncate(raw[i]) || raw[i] < dialect.IndexBase)
            {
                throw new JgsRuntimeException(line, col, dialect.IsMatlab
                    ? "Array indices must be positive integers or logical values."
                    : $"Index {raw[i]} is out of range (indexing is {dialect.IndexBase}-based).");
            }

            picks[i] = (int)raw[i] - dialect.IndexBase;
        }

        return picks;
    }

    /// <summary>The positions a deletion names, each inside the extent.</summary>
    private static int[] SparseReadPicks(JgsValue index, int extent, JgsDialect dialect, int line, int col)
    {
        int[] picks = SparseWritePicks(index, extent, dialect, line, col);
        foreach (int pick in picks)
        {
            if (pick >= extent)
            {
                throw new JgsRuntimeException(line, col, "Matrix index is out of range for deletion.");
            }
        }

        return picks;
    }
}
