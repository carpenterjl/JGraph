namespace JGraph.Scripting.Jgs;

/// <summary>
/// The interpreter's side of <c>categorical</c> (open item 115): what <c>c(k) = rhs</c> and
/// <c>c(r, k) = rhs</c> write, what a bracket holding a categorical makes, and the refusals an
/// operator gives one. The codes are an ordinary numeric array underneath (<see cref="JgsCategoricalTag"/>),
/// so a read goes through the array roads; a write rebuilds the codes here, because a new name may
/// widen the categories and the tag lives with the value that comes back.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>Whether any piece of a bracket literal is a categorical.</summary>
    private static bool AnyCategorical(IReadOnlyList<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.IsCategorical)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Concatenates a bracket literal with a categorical in it: the categories are merged piece by
    /// piece in order, text pieces join as names (a new one added unless the set is protected), and
    /// a number is R2025b's refusal. The codes then go through the ordinary assembly, which settles
    /// the shape and every mismatch as it does for numbers.
    /// </summary>
    private JgsValue ConcatenateCategoricals(List<JgsValue[]> rows, Node at)
    {
        JgsCategoricalTag? tag = null;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.IsCategorical)
                {
                    tag = tag is null ? piece.CategoricalTag! : JgsCategorical.Merge(tag, piece.CategoricalTag!, at.Line, at.Column);
                }
            }
        }

        var mapped = new List<JgsValue[]>(rows.Count);
        foreach (JgsValue[] row in rows)
        {
            var pieces = new JgsValue[row.Length];
            for (int i = 0; i < row.Length; i++)
            {
                JgsValue piece = row[i];
                if (piece.IsCategorical)
                {
                    pieces[i] = JgsMatrix.FromColumnMajor(JgsCategorical.Remap(JgsCategorical.CodesOf(piece), piece.CategoricalTag!, tag!), piece.Rows, piece.Cols);
                    continue;
                }

                if (piece.Type == JgsType.Array && piece.ArrayLength == 0 && !piece.IsStringArray)
                {
                    pieces[i] = piece; // [] joins as nothing
                    continue;
                }

                string?[] names = JgsCategorical.TextNames(piece) ?? (IsBareMissing(piece) ? new string?[] { null } : null)
                    ?? throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:categorical:cat:TypeMismatch",
                        $"Unable to concatenate a {JgsBuiltins.ClassOf(piece, JgsDialect.Matlab)} array and a categorical array.");
                (double[] codes, tag) = JgsCategorical.Absorb(tag!, names, at.Line, at.Column);
                pieces[i] = piece.Type == JgsType.Cell || piece.IsStringArray
                    ? JgsMatrix.FromColumnMajor(codes, piece.Rows, piece.Cols)
                    : JgsMatrix.FromColumnMajor(codes, 1, codes.Length);
            }

            mapped.Add(pieces);
        }

        JgsValue assembled = AssembleMatrix(at, mapped);
        return JgsCategorical.Wrap(assembled, tag!);
    }

    /// <summary>The bare <c>missing</c> value, which writes an undefined element.</summary>
    private static bool IsBareMissing(JgsValue value) => JgsBuiltins.IsBareMissingValue(value);

    /// <summary>Whether an indexed write lands on a categorical the variable holds, or starts one from a categorical written into nothing.</summary>
    private bool WritesCategorical(Expr container, JgsValue rhs, JgsEnvironment env)
    {
        if (container is not VariableExpr named)
        {
            return false;
        }

        if (!LookUp(named.Name, env, out JgsValue held))
        {
            return rhs.IsCategorical;
        }

        return held.IsCategorical
            || (rhs.IsCategorical && held.Type == JgsType.Array && held.ArrayLength == 0 && !held.IsStringArray && !held.IsTime);
    }

    /// <summary>
    /// <c>c(k) = rhs</c> and <c>c(r, k) = rhs</c> on a categorical: text or a categorical on the
    /// right, each name its code and a new name a new category unless the set is protected; the
    /// bare <c>missing</c>, an empty or <c>&lt;undefined&gt;</c> text the undefined code; a number
    /// R2025b's refusal; <c>[]</c> a deletion. A slot past the end grows the array undefined between.
    /// </summary>
    private JgsValue AssignIntoCategorical(Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsCategoricalTag tag = existing.CategoricalTag!;
        double[] codes = JgsCategorical.CodesOf(existing);
        int rows = existing.Rows;
        int cols = existing.Cols;
        bool deleting = rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray && !rhs.IsCategorical;
        if (subscripts.Count is not (1 or 2))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "A categorical is written with one or two subscripts.");
        }

        double[] written = [];
        if (!deleting)
        {
            if (rhs.IsCategorical)
            {
                tag = JgsCategorical.Merge(tag, rhs.CategoricalTag!, at.Line, at.Column);
                written = JgsCategorical.Remap(JgsCategorical.CodesOf(rhs), rhs.CategoricalTag!, tag);
            }
            else
            {
                string?[] names = JgsCategorical.TextNames(rhs) ?? (IsBareMissing(rhs) ? new string?[] { null } : null)
                    ?? throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:categorical:InvalidRHS",
                        "Right hand side of an assignment to a categorical array must be a categorical or text representing a category name.");
                (written, tag) = JgsCategorical.Absorb(tag, names, at.Line, at.Column);
            }
        }

        double[] result;
        int newRows;
        int newCols;
        if (subscripts.Count == 1)
        {
            JgsValue? index = EvaluateIndexArgument(subscripts[0], codes.Length, env);
            bool column = cols == 1 && rows > 1;
            bool grid = rows > 1 && cols > 1;
            if (deleting)
            {
                if (index is null)
                {
                    result = [];
                    newRows = 0;
                    newCols = 0;
                }
                else
                {
                    var drop = new HashSet<int>(WritePicks(index, codes.Length, at));
                    if (drop.Any(slot => slot >= codes.Length))
                    {
                        throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsdeldimmismatch", "Index exceeds array bounds.");
                    }

                    result = [.. codes.Where((_, i) => !drop.Contains(i))];
                    newRows = column ? result.Length : (result.Length == 0 ? 0 : 1);
                    newCols = column ? (result.Length == 0 ? 0 : 1) : result.Length;
                }
            }
            else
            {
                int[] picks = index is null ? [.. Enumerable.Range(0, codes.Length)] : WritePicks(index, codes.Length, at);
                if (written.Length != 1 && written.Length != picks.Length)
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsassignnumelmismatch",
                        "Unable to perform assignment because the left and right sides have a different number of elements.");
                }

                int needed = Math.Max(codes.Length, picks.Length == 0 ? 0 : Highest(picks) + 1);
                if (grid && needed > codes.Length)
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:index:growAmbiguous", "Attempt to grow array along ambiguous dimension.");
                }

                result = new double[needed];
                Array.Fill(result, double.NaN);
                Array.Copy(codes, result, codes.Length);
                for (int i = 0; i < picks.Length; i++)
                {
                    result[picks[i]] = written[written.Length == 1 ? 0 : i];
                }

                (newRows, newCols) = grid ? (rows, cols) : column ? (needed, 1) : (1, needed);
            }
        }
        else
        {
            if (codes.Length == 1 && rows == 0)
            {
                rows = 1;
                cols = 1;
            }

            JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], rows, env);
            JgsValue? colIndex = EvaluateIndexArgument(subscripts[1], cols, env);
            if (deleting)
            {
                if (rowIndex is not null && colIndex is not null)
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsdeldimmismatch", "A null assignment can have only one non-colon index.");
                }

                var dropRows = new HashSet<int>(rowIndex is null ? [] : WritePicks(rowIndex, rows, at));
                var dropCols = new HashSet<int>(colIndex is null ? [] : WritePicks(colIndex, cols, at));
                newRows = rows - dropRows.Count;
                newCols = cols - dropCols.Count;
                var kept = new List<double>(newRows * newCols);
                for (int c = 0; c < cols; c++)
                {
                    if (dropCols.Contains(c))
                    {
                        continue;
                    }

                    for (int r = 0; r < rows; r++)
                    {
                        if (!dropRows.Contains(r))
                        {
                            kept.Add(codes[r + (c * rows)]);
                        }
                    }
                }

                result = [.. kept];
            }
            else
            {
                int[] rowPicks = rowIndex is null ? [.. Enumerable.Range(0, Math.Max(rows, 1))] : WritePicks(rowIndex, rows, at);
                int[] colPicks = colIndex is null ? [.. Enumerable.Range(0, Math.Max(cols, 1))] : WritePicks(colIndex, cols, at);
                if (written.Length != 1 && written.Length != rowPicks.Length * colPicks.Length)
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsassignnumelmismatch",
                        "Unable to perform assignment because the left and right sides have a different number of elements.");
                }

                newRows = Math.Max(rows, rowPicks.Length == 0 ? 0 : Highest(rowPicks) + 1);
                newCols = Math.Max(cols, colPicks.Length == 0 ? 0 : Highest(colPicks) + 1);
                result = new double[newRows * newCols];
                Array.Fill(result, double.NaN);
                for (int c = 0; c < cols; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        result[r + (c * newRows)] = codes[r + (c * rows)];
                    }
                }

                int taken = 0;
                foreach (int c in colPicks)
                {
                    foreach (int r in rowPicks)
                    {
                        result[r + (c * newRows)] = written[written.Length == 1 ? 0 : taken++];
                    }
                }
            }
        }

        StoreBack(target, JgsCategorical.FromCodes(result, newRows, newCols, tag), at, env);
        return rhs;
    }
}
