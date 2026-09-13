using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The switch for the affine index read (item 12a, ADR 0159). While enabled, a paren read whose
/// subscript is a colon range with unclassed, integral, in-range bounds — or <c>:</c>, or a scalar
/// — is served straight off the packed buffer as a block or strided copy, without materialising the
/// range and without the integer pick list; while disabled, every read takes the general gather.
/// <c>JGRAPH_AFFINE_INDEX=1|0</c> forces the mode, which is the parity lever: the two roads must
/// answer the same bytes and throw the same words.
/// </summary>
internal static class JgsAffineIndex
{
    /// <summary>The built-in default before any environment override.</summary>
    public const bool DefaultEnabled = true;

    /// <summary>Whether affine reads take the copy path.</summary>
    public static bool Enabled { get; set; } = ReadEnvironmentOverride() ?? DefaultEnabled;

    /// <summary>
    /// How many reads have taken the copy path in this process — the tests' way of asserting that a
    /// script took (or was refused) the fast path, since a correct fast path is invisible in the output.
    /// </summary>
    internal static long Reads;

    private static bool? ReadEnvironmentOverride() =>
        Environment.GetEnvironmentVariable("JGRAPH_AFFINE_INDEX") switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
}

/// <summary>
/// The affine range read (item 12a, ADR 0159). <c>x(a:b)</c>, <c>x(a:s:b)</c>, <c>M(k, :)</c>,
/// <c>M(:, a:b)</c> and <c>M(a:b, c:d)</c> used to reach the general gather as a materialised
/// double range turned into an integer pick list turned into a scalar gather. When every bound is an
/// unclassed double and the start and step are whole numbers, every element the range would have
/// held is a whole number too, so proving the first and the last inside the extent proves them all,
/// and the read is a copy: one block when the step is one, a strided loop otherwise. The count is
/// the colon's own count, the elements are the colon's own <c>start + i·step</c> (exact in whole
/// numbers, so the forward and backward halves of <see cref="PackedMath.RangeElement"/> agree), and
/// anything the proof cannot cover — a classed or timed bound, a fractional start or step, an empty
/// range, a position outside the extent — takes the general path with the values already evaluated,
/// so it throws the general path's own words and evaluates each bound exactly once either way.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>A run of positions, 0-based: <c>Start + i·Step</c> for <c>i</c> below <c>Count</c>, every one proved inside its extent.</summary>
    private readonly record struct AffineSelector(int Start, int Step, int Count)
    {
        /// <summary>The positions as the general path's pick list, in order.</summary>
        public int[] Picks()
        {
            var picks = new int[Count];
            int at = Start;
            for (int i = 0; i < picks.Length; i++)
            {
                picks[i] = at;
                at += Step;
            }

            return picks;
        }
    }

    /// <summary>
    /// One subscript slot after evaluation: an affine run when it could be proved, otherwise the index
    /// value the general path reads (null for a lone ':' over an empty extent).
    /// </summary>
    private readonly struct SubscriptSlot
    {
        private readonly bool _scalar;

        public SubscriptSlot(AffineSelector selector, bool scalar)
        {
            IsAffine = true;
            Selector = selector;
            Index = null;
            _scalar = scalar;
        }

        public SubscriptSlot(JgsValue? index)
        {
            IsAffine = false;
            Selector = default;
            Index = index;
            _scalar = false;
        }

        public bool IsAffine { get; }

        public AffineSelector Selector { get; }

        public JgsValue? Index { get; }

        /// <summary>The general path's "one position" reading: a scalar subscript rather than an array or ':'.</summary>
        public bool Scalar => IsAffine ? _scalar : Index is { Type: not JgsType.Array };
    }

    /// <summary>
    /// Evaluates one subscript with <c>end</c> bound to its slot's extent, proving an affine run where
    /// it can. A colon range has its three bounds evaluated here in the order <c>EvaluateRange</c>
    /// evaluates them, once, and is materialised through <see cref="RangeFromValues"/> only when the
    /// proof fails, so the general path sees the same values and no bound is evaluated twice.
    /// </summary>
    private SubscriptSlot EvaluateSlot(Expr argument, int[] extents, int slot, JgsEnvironment env)
    {
        int extent = extents[slot];
        if (argument is AllExpr)
        {
            return extent > 0
                ? new SubscriptSlot(new AffineSelector(0, 1, extent), scalar: false)
                : new SubscriptSlot((JgsValue?)null);
        }

        if (argument is RangeExpr range)
        {
            JgsValue startValue;
            JgsValue stepValue;
            JgsValue stopValue;
            _indexContext.Add((extents, slot));
            try
            {
                startValue = Evaluate(range.Start, env);
                stepValue = range.Step is null ? JgsValue.Number(1) : Evaluate(range.Step, env);
                stopValue = Evaluate(range.Stop, env);
            }
            finally
            {
                _indexContext.RemoveAt(_indexContext.Count - 1);
            }

            if (TryAffineRange(startValue, stepValue, stopValue, extent, out AffineSelector selector))
            {
                return new SubscriptSlot(selector, scalar: false);
            }

            return new SubscriptSlot(RangeFromValues(range, startValue, stepValue, stopValue));
        }

        JgsValue? index = EvaluateIndexArgument(argument, extents, slot, env);
        if (index is not null && IsUnclassedDouble(index) && TryPosition(index.AsNumber, extent, out int position))
        {
            return new SubscriptSlot(new AffineSelector(position, 1, 1), scalar: true);
        }

        return new SubscriptSlot(index);
    }

    /// <summary>A plain double scalar: no integer or single class to round or saturate through, and not a time.</summary>
    private static bool IsUnclassedDouble(JgsValue value) =>
        value.Type == JgsType.Number && value.NumericClass == JgsNumericClass.Double && value.TimeTag is null;

    /// <summary>Whether <paramref name="raw"/> is a whole number naming a position inside <paramref name="extent"/> under the dialect's base.</summary>
    private bool TryPosition(double raw, int extent, out int position)
    {
        int indexBase = Dialect.IndexBase;
        if (raw >= indexBase && raw <= extent - 1 + indexBase && raw == Math.Floor(raw))
        {
            position = (int)raw - indexBase;
            return true;
        }

        position = 0;
        return false;
    }

    /// <summary>
    /// The proof: unclassed bounds, a whole-number start and step, a non-empty count by the colon's
    /// own rule, and the first and last element both inside the extent. With a whole start and step
    /// every element between is whole and between them, so nothing else needs checking.
    /// </summary>
    private bool TryAffineRange(JgsValue startValue, JgsValue stepValue, JgsValue stopValue, int extent,
        out AffineSelector selector)
    {
        selector = default;
        if (!IsUnclassedDouble(startValue) || !IsUnclassedDouble(stepValue) || !IsUnclassedDouble(stopValue))
        {
            return false;
        }

        double start = startValue.AsNumber;
        double step = stepValue.AsNumber;
        double stop = stopValue.AsNumber;
        if (step == 0 || !double.IsFinite(start) || !double.IsFinite(step)
            || start != Math.Floor(start) || step != Math.Floor(step))
        {
            return false;
        }

        long count = RangeCountOf(start, step, stop);
        if (count < 1)
        {
            return false;
        }

        double last = start + ((count - 1) * step);
        if (!TryPosition(start, extent, out int first) || !TryPosition(last, extent, out _))
        {
            return false;
        }

        // Both ends inside the extent bound the count by the extent, so the narrowing is safe.
        selector = new AffineSelector(first, (int)step, (int)count);
        return true;
    }

    /// <summary><c>x(a:s:b)</c> on a packed vector or matrix: the run copied out, shaped as the general gather shapes it.</summary>
    private JgsValue AffineGather(JgsValue target, AffineSelector selector)
    {
        NumericBuffer source = target.AsBuffer;
        NumericBuffer dest = JgsPacking.Allocate(selector.Count);
        CopyAffine(source, selector.Start, selector.Step, dest.AsSpan(0, selector.Count));
        GC.KeepAlive(source);
        JgsAffineIndex.Reads++;
        return OrientGather(JgsValue.Packed(dest, target.PackedKind), target, indexRows: 1, indexCols: selector.Count, logicalIndex: false);
    }

    /// <summary><c>M(r, c)</c> with an affine run in each slot: the block copied column by column, shaped as the general path shapes it.</summary>
    private JgsValue AffineGather(JgsValue target, int rows, AffineSelector rowRun, AffineSelector colRun)
    {
        NumericBuffer source = target.AsBuffer;
        NumericBuffer dest = JgsPacking.Allocate((long)rowRun.Count * colRun.Count);
        Span<double> into = dest.AsSpan();
        int column = colRun.Start;
        for (int j = 0; j < colRun.Count; j++)
        {
            CopyAffine(source, (column * rows) + rowRun.Start, rowRun.Step, into.Slice(j * rowRun.Count, rowRun.Count));
            column += colRun.Step;
        }

        GC.KeepAlive(source);
        JgsAffineIndex.Reads++;
        JgsValue result = JgsValue.Packed(dest, target.PackedKind);
        result.Reshape(rowRun.Count, colRun.Count);
        return result;
    }

    /// <summary>One run: a block copy when the step is one, a strided loop otherwise (a negative step walks backwards).</summary>
    private static void CopyAffine(NumericBuffer source, int start, int step, Span<double> into)
    {
        if (step == 1)
        {
            source.AsSpan(start, into.Length).CopyTo(into);
            return;
        }

        ReadOnlySpan<double> all = source.AsSpan();
        int at = start;
        for (int i = 0; i < into.Length; i++)
        {
            into[i] = all[at];
            at += step;
        }
    }
}
