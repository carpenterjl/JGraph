namespace JGraph.Numerics;

/// <summary>Which summary a sliding window is being asked for.</summary>
public enum WindowStat
{
    /// <summary>One this file has no incremental form for; the caller walks each window itself.</summary>
    Other,

    /// <summary>The sum of the window.</summary>
    Sum,

    /// <summary>The mean of the window.</summary>
    Mean,

    /// <summary>The largest value in the window.</summary>
    Max,

    /// <summary>The smallest value in the window.</summary>
    Min,

    /// <summary>The product of the window.</summary>
    Product,

    /// <summary>The sample variance of the window, dividing by n-1.</summary>
    Variance,

    /// <summary>The square root of <see cref="Variance"/>.</summary>
    StandardDeviation,

    /// <summary>The middle value of the window, or the mean of the middle two.</summary>
    Median,

    /// <summary>
    /// The median absolute deviation: how far the window's readings sit from their own middle,
    /// answered by the middle of those distances.
    /// </summary>
    MedianDeviation,
}

/// <summary>What an incomplete window at either end of the data means.</summary>
public enum WindowEnds
{
    /// <summary>Keep the part of the window that is there, and summarise that.</summary>
    Shrink,

    /// <summary>Drop the points whose window would not fit, so the answer is shorter.</summary>
    Discard,

    /// <summary>Answer NaN wherever the window would not fit.</summary>
    Fill,

    /// <summary>Put a chosen value in every place the window reaches past the data.</summary>
    Pad,
}

/// <summary>
/// The sliding statistics — <c>movmean</c> and its eight relatives, <c>smoothdata</c>'s default, the
/// moving fences under <c>isoutlier</c> — computed once per element rather than once per element per
/// window. The window's summary is carried from one point to the next instead of being rebuilt, so
/// the cost stops depending on how wide the window is.
/// </summary>
/// <remarks>
/// <para>
/// The carrying is done by a queue that never subtracts. A running total that adds the arriving
/// value and takes the departing one away is the obvious way to do this and it is a trap: it cannot
/// un-add a NaN or an infinity, its error grows without bound over a long series, and a rolling sum
/// of positive numbers can end up negative — the failure pandas and xarray both have open issues
/// about. What is used here instead is the two-stack queue: values are pushed onto a back stack that
/// carries a running fold, and when the front runs out the whole back stack is flipped into it
/// carrying <em>suffix</em> folds, so the window's answer is always one combine of two folds that
/// were each built by adding alone. Every element is folded exactly twice however wide the window
/// is, and the answer is the fold of the values that are in the window — not a fold of everything
/// that ever was, with the rest taken back off.
/// </para>
/// <para>
/// That the fold is applied in a different order than a walk over the window would apply it is the
/// one thing this changes, and it changes it only where the fold is inexact. <c>movsum</c>,
/// <c>movmean</c> and <c>movprod</c> can differ from a per-window walk in the last place — for the
/// two sums in the direction of being more accurate rather than less, since a two-stack fold is a
/// partial pairwise summation and a walk is a straight left fold. <c>movmax</c>, <c>movmin</c> and
/// <c>movmedian</c> answer the same bits as before; only the sign of a zero can move, and only when
/// a window holds zeros of both signs.
/// </para>
/// <para>
/// NaN needs no special case anywhere. <c>includenan</c> pushes it and lets the fold carry it —
/// which works precisely because nothing is ever taken back off — and <c>omitnan</c> pushes the
/// fold's identity in its place and leaves it out of the count. The one summary that cannot be
/// folded this way is the variance, whose merge is Chan's rather than a monoid's and which answers
/// an infinity where a two-pass walk answers NaN; a count of the non-finite values in the window
/// catches that case and settles it directly.
/// </para>
/// </remarks>
public static class WindowKernels
{
    /// <summary>Whether <paramref name="stat"/> has an incremental form here.</summary>
    public static bool Handles(WindowStat stat) => stat != WindowStat.Other;

    /// <summary>
    /// One slice, window by window, where the window is <paramref name="behind"/> places back and
    /// <paramref name="ahead"/> places forward of each point. <paramref name="identity"/> is what a
    /// window with nothing left in it answers.
    /// </summary>
    public static double[] Slide(
        WindowStat stat,
        ReadOnlySpan<double> values,
        int behind,
        int ahead,
        WindowEnds ends,
        double pad,
        bool omitNan,
        double identity)
    {
        int room = behind + ahead + 1;
        return stat switch
        {
            WindowStat.Sum =>
                Walk(new Folded<SumFold>(room, mean: false), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.Mean =>
                Walk(new Folded<SumFold>(room, mean: true), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.Max =>
                Walk(new Folded<MaxFold>(room, mean: false), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.Min =>
                Walk(new Folded<MinFold>(room, mean: false), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.Product =>
                Walk(new Folded<ProductFold>(room, mean: false), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.Variance =>
                Walk(new Spread(room, root: false), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.StandardDeviation =>
                Walk(new Spread(room, root: true), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.Median =>
                Walk(new Middle(room), values, behind, ahead, ends, pad, omitNan, identity),
            WindowStat.MedianDeviation =>
                Walk(new Middle(room, spread: true), values, behind, ahead, ends, pad, omitNan, identity),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stat), stat, "there is no incremental form for this summary"),
        };
    }

    /// <summary>
    /// The same walk where the window is a distance along <paramref name="points"/> rather than a
    /// count of places. Only for points that never step backwards — <see cref="IsAscending"/> is the
    /// test — because that is what makes each window's two ends move forward and never back.
    /// </summary>
    public static double[] SlideOverPoints(
        WindowStat stat,
        ReadOnlySpan<double> values,
        ReadOnlySpan<double> points,
        double behind,
        double ahead,
        WindowEnds ends,
        bool omitNan,
        double identity)
    {
        int room = Math.Max(1, values.Length);
        return stat switch
        {
            WindowStat.Sum =>
                WalkPoints(new Folded<SumFold>(room, mean: false), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.Mean =>
                WalkPoints(new Folded<SumFold>(room, mean: true), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.Max =>
                WalkPoints(new Folded<MaxFold>(room, mean: false), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.Min =>
                WalkPoints(new Folded<MinFold>(room, mean: false), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.Product =>
                WalkPoints(new Folded<ProductFold>(room, mean: false), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.Variance =>
                WalkPoints(new Spread(room, root: false), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.StandardDeviation =>
                WalkPoints(new Spread(room, root: true), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.Median =>
                WalkPoints(new Middle(room), values, points, behind, ahead, ends, omitNan, identity),
            WindowStat.MedianDeviation =>
                WalkPoints(new Middle(room, spread: true), values, points, behind, ahead, ends, omitNan, identity),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stat), stat, "there is no incremental form for this summary"),
        };
    }

    /// <summary>Whether <paramref name="points"/> never steps backwards, a NaN counting as a refusal.</summary>
    public static bool IsAscending(ReadOnlySpan<double> points)
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (double.IsNaN(points[i]) || (i > 0 && points[i] < points[i - 1]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The walk itself: the window's two ends only ever move forward, so each element is added once
    /// and taken out once whatever the width.
    /// </summary>
    private static double[] Walk<TWindow>(
        TWindow window,
        ReadOnlySpan<double> values,
        int behind,
        int ahead,
        WindowEnds ends,
        double pad,
        bool omitNan,
        double identity)
        where TWindow : struct, IWindow
    {
        int length = values.Length;
        int from = ends == WindowEnds.Discard ? behind : 0;
        int last = ends == WindowEnds.Discard ? length - 1 - ahead : length - 1;
        var result = new double[Math.Max(0, last - from + 1)];
        if (result.Length == 0)
        {
            return result;
        }

        bool padded = ends == WindowEnds.Pad;
        int left = padded ? from - behind : Math.Max(0, from - behind);
        int right = left - 1;
        int count = 0;

        for (int i = from; i <= last; i++)
        {
            int lo = padded ? i - behind : Math.Max(0, i - behind);
            int hi = padded ? i + ahead : Math.Min(length - 1, i + ahead);

            while (left < lo)
            {
                double gone = At(values, left, pad);
                bool counted = !(omitNan && double.IsNaN(gone));
                window.Remove(gone, counted);
                if (counted)
                {
                    count--;
                }

                left++;
            }

            while (right < hi)
            {
                right++;
                double came = At(values, right, pad);
                bool counted = !(omitNan && double.IsNaN(came));
                window.Add(came, counted);
                if (counted)
                {
                    count++;
                }
            }

            bool complete = i - behind >= 0 && i + ahead < length;
            result[i - from] = !complete && ends == WindowEnds.Fill ? double.NaN
                : count == 0 ? identity
                : window.Result(count);
        }

        return result;
    }

    /// <summary>The walk when the window is a span along the sample points rather than a count.</summary>
    private static double[] WalkPoints<TWindow>(
        TWindow window,
        ReadOnlySpan<double> values,
        ReadOnlySpan<double> points,
        double behind,
        double ahead,
        WindowEnds ends,
        bool omitNan,
        double identity)
        where TWindow : struct, IWindow
    {
        int length = values.Length;
        var answers = new List<double>(length);
        int left = 0;
        int right = -1;
        int count = 0;

        for (int i = 0; i < length; i++)
        {
            // The window is what it always was — every reading within reach of this one's own place —
            // and its two ends only move forward because the places only rise.
            double lowest = points[i] - behind;
            double highest = points[i] + ahead;
            while (left < length && points[left] < lowest)
            {
                // Only what was actually put in has to be taken out: an empty window slides past
                // places it never reached.
                if (left <= right)
                {
                    double gone = values[left];
                    bool counted = !(omitNan && double.IsNaN(gone));
                    window.Remove(gone, counted);
                    if (counted)
                    {
                        count--;
                    }
                }

                left++;
            }

            if (right < left - 1)
            {
                right = left - 1;
            }

            while (right + 1 < length && points[right + 1] <= highest)
            {
                right++;
                double came = values[right];
                bool counted = !(omitNan && double.IsNaN(came));
                window.Add(came, counted);
                if (counted)
                {
                    count++;
                }
            }

            bool complete = points[i] - behind >= points[0] && points[i] + ahead <= points[length - 1];
            if (!complete && ends == WindowEnds.Discard)
            {
                continue;
            }

            answers.Add(!complete && ends == WindowEnds.Fill ? double.NaN
                : count == 0 ? identity
                : window.Result(count));
        }

        return [.. answers];
    }

    private static double At(ReadOnlySpan<double> values, int index, double pad) =>
        (uint)index < (uint)values.Length ? values[index] : pad;

    /// <summary>A window that can be told what arrived, what left, and what it adds up to.</summary>
    private interface IWindow
    {
        /// <summary><paramref name="counted"/> is false for a value <c>omitnan</c> is leaving out.</summary>
        void Add(double value, bool counted);

        /// <summary>Undoes the <see cref="Add"/> of the value that has now fallen off the back.</summary>
        void Remove(double value, bool counted);

        /// <summary>The summary of what is in the window now, which holds <paramref name="count"/> values.</summary>
        double Result(int count);
    }

    /// <summary>One associative summary: an identity, and a way of combining two of them.</summary>
    private interface IFold
    {
        /// <summary>The value that combines with anything to give that thing back.</summary>
        static abstract double Unit { get; }

        /// <summary>The two folds side by side, left before right.</summary>
        static abstract double Combine(double left, double right);
    }

    private readonly struct SumFold : IFold
    {
        public static double Unit => 0;

        public static double Combine(double left, double right) => left + right;
    }

    private readonly struct ProductFold : IFold
    {
        public static double Unit => 1;

        public static double Combine(double left, double right) => left * right;
    }

    /// <summary>
    /// The largest value with NaN skipped, which is what a walk over the window answered — and it is
    /// what makes NaN this fold's identity rather than something that swallows the rest.
    /// </summary>
    private readonly struct MaxFold : IFold
    {
        public static double Unit => double.NaN;

        public static double Combine(double left, double right) =>
            double.IsNaN(left) ? right : double.IsNaN(right) ? left : right > left ? right : left;
    }

    /// <summary>
    /// The smallest value with NaN winning, which is the other thing a walk over the window answered:
    /// here NaN swallows everything, so the identity has to be positive infinity instead.
    /// </summary>
    private readonly struct MinFold : IFold
    {
        public static double Unit => double.PositiveInfinity;

        public static double Combine(double left, double right) =>
            double.IsNaN(left) || double.IsNaN(right) ? double.NaN : right < left ? right : left;
    }

    /// <summary>The two-stack queue over one fold: push on the back, flip when the front runs out.</summary>
    private struct Folded<TFold> : IWindow
        where TFold : struct, IFold
    {
        private readonly double[] _back;
        private readonly double[] _front;
        private readonly bool _mean;
        private int _backCount;
        private double _backFold;
        private int _frontCount;
        private int _frontAt;

        public Folded(int capacity, bool mean)
        {
            int room = Math.Max(1, capacity);
            _back = new double[room];
            _front = new double[room];
            _mean = mean;
            _backCount = 0;
            _backFold = TFold.Unit;
            _frontCount = 0;
            _frontAt = 0;
        }

        public void Add(double value, bool counted)
        {
            double kept = counted ? value : TFold.Unit;
            _back[_backCount++] = kept;
            _backFold = TFold.Combine(_backFold, kept);
        }

        public void Remove(double value, bool counted)
        {
            _ = value;
            _ = counted; // a value left out still took a place, so there is still one to drop
            if (_frontAt == _frontCount)
            {
                Flip();
            }

            _frontAt++;
        }

        public readonly double Result(int count)
        {
            double total = _frontAt < _frontCount
                ? _backCount == 0 ? _front[_frontAt] : TFold.Combine(_front[_frontAt], _backFold)
                : _backFold;
            return _mean ? total / count : total;
        }

        /// <summary>
        /// The back stack becomes the front, each place holding the fold of itself and everything
        /// after it — which is what makes dropping the oldest value one step of an index.
        /// </summary>
        private void Flip()
        {
            double running = TFold.Unit;
            for (int i = _backCount - 1; i >= 0; i--)
            {
                running = TFold.Combine(_back[i], running);
                _front[i] = running;
            }

            _frontCount = _backCount;
            _frontAt = 0;
            _backCount = 0;
            _backFold = TFold.Unit;
        }
    }

    /// <summary>
    /// The variance, carried as a count, a mean and a sum of squared deviations and merged by Chan's
    /// formula, which is associative and stable where a running sum of squares is neither.
    /// </summary>
    private struct Spread : IWindow
    {
        private readonly Moments[] _back;
        private readonly Moments[] _front;
        private readonly bool _root;
        private int _backCount;
        private Moments _backFold;
        private int _frontCount;
        private int _frontAt;
        private int _wild;

        public Spread(int capacity, bool root)
        {
            int room = Math.Max(1, capacity);
            _back = new Moments[room];
            _front = new Moments[room];
            _root = root;
            _backCount = 0;
            _backFold = default;
            _frontCount = 0;
            _frontAt = 0;
            _wild = 0;
        }

        public void Add(double value, bool counted)
        {
            if (counted && !double.IsFinite(value))
            {
                _wild++;
            }

            Moments one = counted ? new Moments(1, value, 0) : default;
            _back[_backCount++] = one;
            _backFold = Merge(_backFold, one);
        }

        public void Remove(double value, bool counted)
        {
            if (counted && !double.IsFinite(value))
            {
                _wild--;
            }

            if (_frontAt == _frontCount)
            {
                Moments running = default;
                for (int i = _backCount - 1; i >= 0; i--)
                {
                    running = Merge(_back[i], running);
                    _front[i] = running;
                }

                _frontCount = _backCount;
                _frontAt = 0;
                _backCount = 0;
                _backFold = default;
            }

            _frontAt++;
        }

        public readonly double Result(int count)
        {
            // A walk over a window holding an infinity takes that infinity away from a mean that is
            // itself infinite, and answers NaN. Chan's merge would answer an infinity, so the case
            // is settled here rather than inside the formula.
            if (_wild > 0)
            {
                return count < 2 ? 0 : double.NaN;
            }

            if (count < 2)
            {
                return 0;
            }

            Moments total = _frontAt < _frontCount
                ? _backCount == 0 ? _front[_frontAt] : Merge(_front[_frontAt], _backFold)
                : _backFold;
            double variance = total.M2 / (total.Count - 1);
            return _root ? Math.Sqrt(variance) : variance;
        }

        private static Moments Merge(in Moments left, in Moments right)
        {
            if (left.Count == 0)
            {
                return right;
            }

            if (right.Count == 0)
            {
                return left;
            }

            double count = left.Count + right.Count;
            double step = right.Mean - left.Mean;
            return new Moments(
                count,
                left.Mean + (step * right.Count / count),
                left.M2 + right.M2 + (step * step * left.Count * right.Count / count));
        }

        private readonly record struct Moments(double Count, double Mean, double M2);
    }

    /// <summary>
    /// The median, held as a sorted array the arriving value is slid into and the departing one slid
    /// out of. A window changes by one value at each step, so keeping it in order costs a move of the
    /// block between the two places rather than a sort of the whole window.
    /// </summary>
    private struct Middle : IWindow
    {
        private readonly double[] _sorted;
        private readonly bool _spread;
        private int _count;
        private int _missing;

        public Middle(int capacity, bool spread = false)
        {
            _sorted = new double[Math.Max(1, capacity)];
            _spread = spread;
            _count = 0;
            _missing = 0;
        }

        public void Add(double value, bool counted)
        {
            if (!counted)
            {
                return;
            }

            if (double.IsNaN(value))
            {
                _missing++;
                return;
            }

            int at = PlaceOf(value);
            Array.Copy(_sorted, at, _sorted, at + 1, _count - at);
            _sorted[at] = value;
            _count++;
        }

        public void Remove(double value, bool counted)
        {
            if (!counted)
            {
                return;
            }

            if (double.IsNaN(value))
            {
                _missing--;
                return;
            }

            int at = PlaceOf(value);
            Array.Copy(_sorted, at + 1, _sorted, at, _count - at - 1);
            _count--;
        }

        public readonly double Result(int count)
        {
            // A window holding a missing reading has no middle. Anything counted here was not
            // stepped over, because stepping over it is exactly what 'omitnan' does before it
            // ever reaches this window.
            if (_missing > 0)
            {
                return double.NaN;
            }

            if (count <= 0)
            {
                return double.NaN;
            }

            int at = count / 2;
            double middle = count % 2 == 1
                ? ValueAt(at)
                : (ValueAt(at - 1) + ValueAt(at)) / 2.0;
            return _spread ? Deviation(count, at, middle) : middle;
        }

        /// <summary>
        /// The middle of the distances from the window's own middle, found by a merge rather than
        /// by ordering them.
        /// </summary>
        /// <remarks>
        /// Distances measured from the middle of an ordered window are two ordered runs — one
        /// walking down from the middle and one walking up — so their own middle is where those
        /// two runs meet. Reading it costs one pass over half the window, where sorting the
        /// distances afresh for every answer costs the window times its own logarithm. Over ten
        /// million readings in a window of fifty-one that is the difference between sixteen
        /// seconds and one.
        /// </remarks>
        private readonly double Deviation(int count, int at, double middle)
        {
            int below = at - 1;
            int above = at;
            double previous = 0;
            double current = 0;
            for (int taken = 0; taken <= count / 2; taken++)
            {
                previous = current;
                double down = below >= 0 ? middle - _sorted[below] : double.PositiveInfinity;
                double up = above < count ? _sorted[above] - middle : double.PositiveInfinity;
                if (down <= up)
                {
                    current = down;
                    below--;
                }
                else
                {
                    current = up;
                    above++;
                }
            }

            return count % 2 == 1 ? current : (previous + current) / 2.0;
        }

        /// <summary>
        /// Where the window's values sit once sorted: a sort over doubles puts every NaN in front,
        /// which is where the walk this replaces found them too.
        /// </summary>
        private readonly double ValueAt(int index) =>
            index < _missing ? double.NaN : _sorted[index - _missing];

        /// <summary>The first place holding something not smaller than <paramref name="value"/>.</summary>
        private readonly int PlaceOf(double value)
        {
            int low = 0;
            int high = _count;
            while (low < high)
            {
                int mid = (int)(((uint)low + (uint)high) >> 1);
                if (_sorted[mid] < value)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }
}
