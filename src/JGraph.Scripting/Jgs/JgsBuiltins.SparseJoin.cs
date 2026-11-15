using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A bracket, <c>horzcat</c> or <c>vertcat</c> with a sparse piece (open item 331, ADR 0369): the
/// answer is sparse, measured in R2025b (probe p6b_a's j_ rows).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Joins the rows of a bracket that holds a sparse matrix. A 0-by-0 piece leaves; every other
    /// piece is a block whose heights agree along a row and whose rows agree in width. The answer is
    /// complex when a piece is (the flag kept, as a dense join keeps it), logical when every piece is
    /// logical, single when a piece is single; an integer or char piece is R2025b's refusal.
    /// </summary>
    internal static JgsValue ConcatenateSparse(List<JgsValue[]> rows, int line, int col)
    {
        if (rows.Count == 1 && rows[0] is [{ Type: JgsType.Sparse } only])
        {
            return only; // [S] is S
        }

        bool complex = false, allLogical = true, single = false;
        var joined = new List<List<JgsComplexSparse>>(rows.Count);
        foreach (JgsValue[] row in rows)
        {
            var blocks = new List<JgsComplexSparse>(row.Length);
            foreach (JgsValue piece in row)
            {
                if (piece.Type != JgsType.Sparse && SizeDims(piece) is [0, 0])
                {
                    continue; // [S []] is S
                }

                blocks.Add(SparseJoinBlock(piece, line, col));
                complex |= piece.IsComplexSparse || IsComplexDense(piece);
                allLogical &= IsLogicalValue(piece);
                single |= piece.NumericClass == JgsNumericClass.Single;
            }

            if (blocks.Count > 0)
            {
                joined.Add(blocks);
            }
        }

        var triplets = new List<(int, int, Complex)>();
        int top = 0, width = -1;
        foreach (List<JgsComplexSparse> blocks in joined)
        {
            int height = blocks[0].Rows;
            int left = 0;
            foreach (JgsComplexSparse block in blocks)
            {
                if (block.Rows != height)
                {
                    throw DimensionMismatch(line, col);
                }

                foreach ((int r, int c, Complex v) in block.Entries())
                {
                    triplets.Add((top + r, left + c, v));
                }

                left += block.Cols;
            }

            if (width >= 0 && left != width)
            {
                throw DimensionMismatch(line, col);
            }

            width = left;
            top += height;
        }

        JgsComplexSparse matrix = JgsComplexSparse.FromTriplets(top, Math.Max(width, 0), triplets);
        JgsValue answer = SparseOfComplex(matrix, keepFlag: complex);
        if (joined.Count > 0 && allLogical && !answer.IsComplexSparse)
        {
            return LogicalSparseOf(answer.AsSparse);
        }

        return single ? SparseInClassOf(answer, SingleLike) : answer;

        static JgsRuntimeException DimensionMismatch(int line, int col) =>
            new(line, col, "MATLAB:catenate:dimensionMismatch", "Dimensions of arrays being concatenated are not consistent.");
    }

    /// <summary>One piece of a sparse join as complex entries, refused in R2025b's words where it has no sparse class.</summary>
    private static JgsComplexSparse SparseJoinBlock(JgsValue piece, int line, int col)
    {
        if (piece.Type == JgsType.Sparse)
        {
            return ComplexEntriesOf(piece);
        }

        bool text = piece.Type == JgsType.String || piece.IsCharMatrix;
        if (text || piece.NumericClass.IsInteger())
        {
            string target = text ? "char" : piece.NumericClass.MatlabName();
            throw new JgsRuntimeException(line, col, "MATLAB:UnableToConvert",
                $"The following error occurred converting from double to {target}:\nAttempt to convert to unimplemented sparse type");
        }

        if (piece.Type is not (JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array) || piece.IsStringArray || piece.IsTime)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:catenate:dimensionMismatch",
                $"A sparse matrix joins numbers and logical values, not a {piece.TypeName}.");
        }

        int[] dims = SizeDims(piece);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:catenate:dimensionMismatch",
                "Dimensions of arrays being concatenated are not consistent.");
        }

        return JgsComplexSparse.FromColumnMajor(ColumnComplex("concatenation", piece, line, col), dims[0], dims[1]);
    }
}
