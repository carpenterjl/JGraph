namespace JGraph.Scripting.Jgs;

/// <summary>
/// What an empty array is worth, and what happens when you join one to something (M96b, ADR 0097).
/// </summary>
/// <remarks>
/// MATLAB has more than one empty. <c>[]</c> is 0-by-0, <c>zeros(1, 0)</c> is a row that happens to
/// hold nothing, and <c>zeros(0, 1)</c> is such a column; they are not interchangeable, because
/// every reader that walks "the first non-singleton dimension" walks down a 0-by-0 and across a
/// 1-by-0. <c>fft([], 4)</c> is a 4-by-0 empty and <c>fft(zeros(1, 0), 4)</c> is a 1-by-4 of zeros
/// for exactly that reason.
///
/// Concatenation is where the difference is felt most: MATLAB omits an empty from a join, so
/// <c>[[], 1]</c> is 1-by-1 rather than a shape error, and a script that grows a result with
/// <c>out = [out; row]</c> from <c>out = []</c> depends on it. When every piece is empty there is
/// nothing to omit into, and the shape has to be settled from the empties themselves — that is what
/// <see cref="JoinAcross"/> and <see cref="JoinDown"/> are for.
/// </remarks>
internal static class JgsEmpty
{
    /// <summary>Whether a value is an array holding no elements, whatever shape it wears.</summary>
    public static bool IsEmptyArray(JgsValue value) => value.Type == JgsType.Array && value.ArrayLength == 0;

    /// <summary>An empty of the given shape, packed or boxed to match the run's storage.</summary>
    public static JgsValue Shaped(int rows, int cols) => JgsPacking.Enabled
        ? JgsValue.Shaped(JgsPacking.Allocate(0), rows, cols)
        : JgsValue.Shaped([], rows, cols);

    /// <summary>The 0-by-0 empty: what <c>[]</c> means in MATLAB.</summary>
    public static JgsValue Zero() => Shaped(0, 0);

    /// <summary>
    /// The shape a row of side-by-side blocks settles on when every one of them is empty.
    /// </summary>
    /// <remarks>
    /// A 0-by-0 carries no shape at all and is dropped outright; if that leaves nothing, 0-by-0 is
    /// the answer. What remains can still disagree, and MATLAB does not refuse it: for blocks that
    /// are all zero columns wide it answers the tallest by zero — <c>[zeros(1, 0), zeros(2, 0)]</c>
    /// is 2-by-0 — and where the disagreement is not that clean it gives up and answers 0-by-0, as
    /// <c>[zeros(2, 0), zeros(0, 3)]</c> does. Those last two are measured behaviour rather than
    /// documented behaviour, and no ordinary script reaches them.
    /// </remarks>
    public static (int Rows, int Cols) JoinAcross(IReadOnlyList<(int Rows, int Cols)> blocks)
    {
        List<(int Rows, int Cols)> kept = WithoutZeroByZero(blocks);
        if (kept.Count == 0)
        {
            return (0, 0);
        }

        int rows = kept[0].Rows;
        int cols = 0;
        bool agree = true;
        int tallest = 0;
        bool allNarrow = true;
        foreach ((int blockRows, int blockCols) in kept)
        {
            agree &= blockRows == rows;
            cols += blockCols;
            tallest = Math.Max(tallest, blockRows);
            allNarrow &= blockCols == 0;
        }

        return agree ? (rows, cols) : allNarrow ? (tallest, 0) : (0, 0);
    }

    /// <summary><see cref="JoinAcross"/> with the two dimensions swapped: stacking, not joining.</summary>
    public static (int Rows, int Cols) JoinDown(IReadOnlyList<(int Rows, int Cols)> blocks)
    {
        List<(int Rows, int Cols)> kept = WithoutZeroByZero(blocks);
        if (kept.Count == 0)
        {
            return (0, 0);
        }

        int cols = kept[0].Cols;
        int rows = 0;
        bool agree = true;
        int widest = 0;
        bool allShort = true;
        foreach ((int blockRows, int blockCols) in kept)
        {
            agree &= blockCols == cols;
            rows += blockRows;
            widest = Math.Max(widest, blockCols);
            allShort &= blockRows == 0;
        }

        return agree ? (rows, cols) : allShort ? (0, widest) : (0, 0);
    }

    /// <summary>
    /// The pieces of a concatenation that actually join — every one that is not an empty array.
    /// Returns the list unchanged when there is nothing to drop, and drops nothing when every piece
    /// is empty: there would be no join left to make, and the shape is <see cref="JoinAcross"/>'s or
    /// <see cref="JoinDown"/>'s to settle.
    /// </summary>
    public static IReadOnlyList<JgsValue> WithoutEmpties(IReadOnlyList<JgsValue> parts)
    {
        bool anyEmpty = false;
        bool anyKept = false;
        foreach (JgsValue part in parts)
        {
            bool empty = IsEmptyArray(part);
            anyEmpty |= empty;
            anyKept |= !empty;
        }

        if (!anyEmpty || !anyKept)
        {
            return parts;
        }

        var kept = new List<JgsValue>(parts.Count);
        foreach (JgsValue part in parts)
        {
            if (!IsEmptyArray(part))
            {
                kept.Add(part);
            }
        }

        return kept;
    }

    /// <summary>The shapes of the pieces, for the all-empty joins above.</summary>
    public static List<(int Rows, int Cols)> ShapesOf(IReadOnlyList<JgsValue> parts)
    {
        var shapes = new List<(int Rows, int Cols)>(parts.Count);
        foreach (JgsValue part in parts)
        {
            shapes.Add(part.Type == JgsType.Array
                ? (JgsMatrix.RowCount(part), JgsMatrix.ColCount(part))
                : (1, 1));
        }

        return shapes;
    }

    private static List<(int Rows, int Cols)> WithoutZeroByZero(IReadOnlyList<(int Rows, int Cols)> blocks)
    {
        var kept = new List<(int Rows, int Cols)>(blocks.Count);
        foreach ((int Rows, int Cols) block in blocks)
        {
            if (block is not (0, 0))
            {
                kept.Add(block);
            }
        }

        return kept;
    }
}
