using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// How many ticks a ruler may carry: the hint the scale-specific generators aim for, and the two
/// counts R2025b's linear rule is bounded by (open item 99). A budget without caps asks for the
/// classic ladder a target count drove, which is what a caller with no pixels to measure still gets.
/// </summary>
/// <param name="Target">The count the logarithmic, date and category generators aim near.</param>
/// <param name="MaxLanding">The most ticks allowed when both limits sit on ticks; 0 for no cap.</param>
/// <param name="MaxFree">The most ticks allowed otherwise; 0 for no cap.</param>
public readonly record struct TickBudget(int Target, int MaxLanding, int MaxFree)
{
    /// <summary>A budget with only the classic target count, for a ruler nobody has measured.</summary>
    public static TickBudget Of(int target) => new(target, 0, 0);

    /// <summary>Whether R2025b's bounded count applies.</summary>
    public bool HasCaps => MaxFree > 0;

    /// <summary>
    /// R2025b's budget for a ruler of a given pixel length and font, keeping <paramref name="target"/>
    /// for the generators that do not read caps.
    /// </summary>
    public static TickBudget ForRuler(double pixels, double fontPoints, bool vertical, int target) =>
        new(target, TickRule.MaxTicks(pixels, fontPoints, vertical, landing: true), TickRule.MaxTicks(pixels, fontPoints, vertical, landing: false));

    /// <summary>The same caps with the landing allowance withdrawn, which is how limits are fitted.</summary>
    public TickBudget WithoutLanding() => new(Target, MaxFree, MaxFree);
}

/// <summary>
/// R2025b's choice of linear ticks and of the limits around them, as a rule fitted to its answers
/// (open item 99; probe_99, probe_99b, probe_99c in the batch 17 and 18 scratch). The arithmetic:
/// <list type="bullet">
/// <item>Candidate steps are 1, 2 and 5 times a power of ten.</item>
/// <item>A candidate is allowed while its ticks inside the limits do not exceed a cap that grows
/// with the ruler's pixel length over its font size and stops at eleven (twelve when both limits
/// sit on ticks).</item>
/// <item>Among the allowed, the one whose first and last tick span most of the range wins; a tie
/// goes to the finer step. This is what prefers 5 over 2 on <c>[0 15]</c> and 0.1 over 0.2 on
/// <c>[0.123 0.987]</c>.</item>
/// <item>A <c>tickaligned</c> limit moves outward to the next tick when that is at most a quarter of
/// the data range away, and a step whose ticks then overflow the cap gives way to the next coarser
/// one. A single value gets a unit either side.</item>
/// <item>A <c>padded</c> limit sits about 7% outside the data and snaps to a tick multiple of the
/// data's own step when one is within 3% of the range.</item>
/// </list>
/// The caps are a table read off the probes rather than MATLAB's algorithm, which scores candidates
/// against a length-and-font density target; on rulers under about 200 pixels the two part company
/// now and then (R2025b takes <c>[0 5]</c> for <c>[0 6]</c> on a 100-pixel ruler and <c>[0 2 4]</c>
/// for <c>[0 5]</c> on a 150-pixel one), and the divergence list records where. Batch 19 refitted the
/// table at a fixed font (probe_119): the batch 18 sweep had let R2025b's font shrink with the box
/// (open item 118), which the first table absorbed.
/// </summary>
public static class TickRule
{
    /// <summary>The fraction of the data range a tick-aligned limit may move outward.</summary>
    public const double AlignmentReach = 0.25;

    /// <summary>The fraction of the range a padded limit snaps across to reach a tick multiple.</summary>
    public const double PaddedSnap = 0.03;

    /// <summary>
    /// The tick font the caps were fitted against: R2025b's default axes font of 10 points, which a
    /// JGraph axes draws at its own built-in 11, so a figure at default fonts takes R2025b's
    /// default-figure counts and a larger or smaller font scales the length by the same ratio. The
    /// table's lengths are R2025b pixels at its 10 points, which are JGraph pixels at 11.
    /// </summary>
    public const double ReferenceFontPoints = 11;

    /// <summary>
    /// The caps for a ruler lying across the page, by the pixels it has at the reference font: below
    /// each bound, the count allowed when both limits sit on ticks and the count allowed otherwise.
    /// The landing count steps up first and the other catches up a little further along.
    /// </summary>
    private static readonly (double Below, int Landing, int Free)[] HorizontalCaps =
    [
        (105, 4, 3), (165, 5, 5), (205, 6, 5), (290, 7, 6), (330, 8, 7), (380, 8, 8), (420, 10, 9), (470, 10, 10),
        (double.PositiveInfinity, 12, 11),
    ];

    /// <summary>The caps for a ruler standing up the page, whose labels stack by their height.</summary>
    private static readonly (double Below, int Landing, int Free)[] VerticalCaps =
    [
        (65, 4, 3), (85, 5, 5), (105, 6, 5), (125, 7, 6), (155, 8, 7), (205, 8, 8), (230, 10, 9), (252, 10, 10),
        (double.PositiveInfinity, 12, 11),
    ];

    /// <summary>
    /// The caps of R2025b's polar r ruler, which answered the same at every size and font measured
    /// (probe_119): <c>[0 1]</c> in fifths, <c>[0 6]</c> in twos, <c>[0 11]</c> in fives. Seven when
    /// both limits sit on ticks keeps the tie from flipping a six-tick landing step to a coarser one.
    /// </summary>
    public static TickBudget PolarRadius(int target) => new(target, 7, 5);

    /// <summary>
    /// The most ticks a ruler of <paramref name="pixels"/> takes at <paramref name="fontPoints"/>,
    /// the caps being read at <see cref="ReferenceFontPoints"/>; both limits on ticks are allowed
    /// one more over part of each band.
    /// </summary>
    public static int MaxTicks(double pixels, double fontPoints, bool vertical, bool landing)
    {
        double scaled = fontPoints > 0 ? pixels * ReferenceFontPoints / fontPoints : pixels;
        if (!double.IsFinite(scaled) || scaled < 0)
        {
            scaled = 0;
        }

        foreach ((double below, int atLanding, int atFree) in vertical ? VerticalCaps : HorizontalCaps)
        {
            if (scaled < below)
            {
                return landing ? atLanding : atFree;
            }
        }

        return landing ? 12 : 11;
    }

    /// <summary>Whether a limit sits exactly on a multiple of the step, in double arithmetic as R2025b tests it.</summary>
    public static bool Lands(double limit, double step)
    {
        double ratio = limit / step;
        return double.IsFinite(ratio) && ratio == System.Math.Round(ratio);
    }

    /// <summary>The next step up the 1-2-5 ladder.</summary>
    public static double Coarser(double step)
    {
        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(step) + 1e-9));
        double mantissa = step / magnitude;
        return mantissa < 1.5 ? 2 * magnitude : mantissa < 3.5 ? 5 * magnitude : 10 * magnitude;
    }

    /// <summary>The next step down the ladder.</summary>
    public static double Finer(double step)
    {
        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(step) + 1e-9));
        double mantissa = step / magnitude;
        return mantissa < 1.5 ? 0.5 * magnitude : mantissa < 3.5 ? magnitude : 2 * magnitude;
    }

    /// <summary>
    /// The ticks of <paramref name="step"/> inside <paramref name="min"/>..<paramref name="max"/>:
    /// how many, and the first and last of them. A limit a hair short of a tick, as 0.7 is of 7 × 0.1,
    /// still counts the tick.
    /// </summary>
    public static int CountWithin(double min, double max, double step, out double first, out double last) =>
        Count(min, max, step, step * 1e-9, out first, out last);

    /// <summary>
    /// The same count in R2025b's own double arithmetic, where 3 × 0.1 overshoots 0.3: the reading a
    /// ruler falls back on when the tolerant one holds too many ticks, so <c>[0 0.3]</c> on a short
    /// ruler is drawn in tenths with three ticks (probe_119).
    /// </summary>
    public static int CountExactly(double min, double max, double step, out double first, out double last) =>
        Count(min, max, step, 0, out first, out last);

    private static int Count(double min, double max, double step, double slack, out double first, out double last)
    {
        double lowIndex = System.Math.Ceiling((min - slack) / step);
        double highIndex = System.Math.Floor((max + slack) / step);
        first = lowIndex * step;
        last = highIndex * step;
        return highIndex < lowIndex ? 0 : (int)System.Math.Min(highIndex - lowIndex + 1, int.MaxValue);
    }

    /// <summary>
    /// The step R2025b takes for the limits <paramref name="min"/>..<paramref name="max"/> under
    /// <paramref name="budget"/>, or 0 when the range is not one ticks can be placed on.
    /// </summary>
    public static double ChooseStep(double min, double max, TickBudget budget)
    {
        double span = max - min;
        if (!(span > 0) || !double.IsFinite(span))
        {
            return 0;
        }

        int exponent = (int)System.Math.Floor(System.Math.Log10(span));
        double bestCoverage = -1;
        double bestStep = 0;
        bool bestAtLandingCap = false;
        double widest = 0;
        double widestPair = 0; // the coarsest step with two ticks inside, for a ruler nothing fits
        for (int z = exponent - 2; z <= exponent + 1; z++)
        {
            foreach (int mantissa in (ReadOnlySpan<int>)[1, 2, 5])
            {
                double step = mantissa * System.Math.Pow(10, z);
                int count = CountWithin(min, max, step, out double first, out double last);
                if (count == 0)
                {
                    continue;
                }

                widest = step;
                if (count >= 2)
                {
                    widestPair = step;
                }

                bool lands = Lands(min, step) && Lands(max, step);
                int cap = lands ? budget.MaxLanding : budget.MaxFree;
                if (count > cap)
                {
                    // Too many by the tolerant reading: the exact one may lose the last tick to
                    // floating point and fit after all, under the free cap (probe_119).
                    count = CountExactly(min, max, step, out first, out last);
                    if (count == 0 || count > budget.MaxFree)
                    {
                        continue;
                    }

                    lands = false;
                }

                // Equal coverage goes to the finer step, which was seen first, unless that step sits
                // on both limits with exactly as many ticks as it may have: [0 6] on a 217-pixel ruler
                // takes 2 over 1, while [0 6] on a 300-pixel one takes 1 (probe_99b).
                double coverage = (last - first) / span;
                bool better = coverage > bestCoverage + 1e-12
                    || (System.Math.Abs(coverage - bestCoverage) <= 1e-12 && bestAtLandingCap);
                if (better)
                {
                    bestCoverage = coverage;
                    bestStep = step;
                    bestAtLandingCap = lands && count >= budget.MaxLanding;
                }
            }
        }

        // Nothing fitted under the cap: the coarsest step that still puts two ticks inside, as
        // R2025b draws [0.123 0.987] on a 50-pixel ruler in fifths (probe_119), else one tick.
        return bestStep > 0 ? bestStep : widestPair > 0 ? widestPair : widest;
    }

    /// <summary>
    /// R2025b's <c>tickaligned</c> limits for a data extent: the step the extent would take, each
    /// limit moved outward to its next tick when that is within <see cref="AlignmentReach"/> of the
    /// range, and the step given up for a coarser one when the moved limits hold too many ticks. The
    /// landing allowance does not apply here, which is why <c>[-1 1000]</c> ends in steps of 200.
    /// </summary>
    public static DataRange Align(DataRange data, TickBudget budget)
    {
        if (!data.IsValid)
        {
            data = AroundValue(data);
            if (!data.IsValid)
            {
                return data;
            }
        }

        if (!budget.HasCaps)
        {
            return ClassicAlign(data);
        }

        TickBudget free = budget.WithoutLanding();
        double step = ChooseStep(data.Min, data.Max, free);
        if (!(step > 0))
        {
            return data;
        }

        double span = data.Length;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            // A limit a hair past a tick is read as on it, and a limit is never moved inward: R2025b
            // leaves plot(1, -1e-7)'s lower limit at -1.0000001 rather than at -1 or -1.2 (probe_99b).
            double low = System.Math.Floor((data.Min / step) + 1e-6) * step;
            if (low > data.Min || data.Min - low > AlignmentReach * span * (1 + 1e-9))
            {
                low = data.Min;
            }

            double high = System.Math.Ceiling((data.Max / step) - 1e-6) * step;
            if (high < data.Max || high - data.Max > AlignmentReach * span * (1 + 1e-9))
            {
                high = data.Max;
            }

            if (CountWithin(low, high, step, out _, out _) <= free.MaxFree)
            {
                return new DataRange(low, high);
            }

            step = Coarser(step);
        }

        return data;
    }

    /// <summary>A single value gets a unit either side, as R2025b gives plot(1, 5) the limits [4 6].</summary>
    public static DataRange AroundValue(DataRange data) =>
        double.IsFinite(data.Min) && data.Min == data.Max ? new DataRange(data.Min - 1, data.Max + 1) : data;

    /// <summary>
    /// R2025b's <c>padded</c> limits: <paramref name="fraction"/> of the range outside the data on
    /// each side, snapped to a multiple of the data's own tick step when one lies within
    /// <see cref="PaddedSnap"/> of the range and not inside the data.
    /// </summary>
    public static DataRange Pad(DataRange data, double fraction, TickBudget budget)
    {
        if (!data.IsValid)
        {
            return Align(data, budget);
        }

        double span = data.Length;
        double low = data.Min - (fraction * span);
        double high = data.Max + (fraction * span);
        if (!budget.HasCaps)
        {
            return new DataRange(low, high);
        }

        // The data's own step, landing allowance and all: [0 1] snaps to tenths although its padded
        // range is then drawn in fifths (probe_99c).
        double step = ChooseStep(data.Min, data.Max, budget);
        if (step > 0)
        {
            double reach = PaddedSnap * span * (1 + 1e-9);
            double lowSnap = System.Math.Round(low / step) * step;
            if (System.Math.Abs(lowSnap - low) <= reach && lowSnap <= data.Min)
            {
                low = lowSnap;
            }

            double highSnap = System.Math.Round(high / step) * step;
            if (System.Math.Abs(highSnap - high) <= reach && highSnap >= data.Max)
            {
                high = highSnap;
            }
        }

        return new DataRange(low, high);
    }

    /// <summary>
    /// The alignment every figure had before the caps existed: outward to multiples of a step from a
    /// five-interval ladder. Kept for a ruler nobody has measured.
    /// </summary>
    private static DataRange ClassicAlign(DataRange range)
    {
        double span = range.Max - range.Min;
        if (span <= 0 || !double.IsFinite(span))
        {
            return range;
        }

        double raw = span / 5;
        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
        double step = magnitude * (raw / magnitude) switch { <= 1 => 1, <= 2 => 2, <= 5 => 5, _ => 10 };
        double min = System.Math.Floor(range.Min / step) * step;
        double max = System.Math.Ceiling(range.Max / step) * step;
        return min == max ? new DataRange(min - step, max + step) : new DataRange(min, max);
    }

    /// <summary>
    /// The screen lengths R2025b's 3-D rulers are budgeted by: the box is stretched to fill the plot
    /// area, and each edge counts the larger of its run across the page and its rise up it, read
    /// against the table for that direction. For the default view that makes a 434 by 342 axes an x
    /// ruler of about 245 pixels across, a y ruler of 188 across and a z ruler of 189 up; seen from
    /// above, the y ruler is 342 up (probe_99c).
    /// </summary>
    public static ((double Pixels, bool Vertical) X, (double Pixels, bool Vertical) Y, (double Pixels, bool Vertical) Z) EdgePixels3D(
        double width, double height, double azimuthDegrees, double elevationDegrees)
    {
        double az = azimuthDegrees * System.Math.PI / 180;
        double el = elevationDegrees * System.Math.PI / 180;
        double cosAz = System.Math.Abs(System.Math.Cos(az));
        double sinAz = System.Math.Abs(System.Math.Sin(az));
        double cosEl = System.Math.Abs(System.Math.Cos(el));
        double sinEl = System.Math.Abs(System.Math.Sin(el));
        double across = cosAz + sinAz;
        double up = ((sinAz + cosAz) * sinEl) + cosEl;
        double scaleX = across > 1e-9 ? width / across : 0;
        double scaleY = up > 1e-9 ? height / up : 0;
        static (double, bool) Longer(double acrossPixels, double upPixels) =>
            upPixels > acrossPixels ? (upPixels, true) : (acrossPixels, false);
        return (
            Longer(scaleX * cosAz, scaleY * sinAz * sinEl),
            Longer(scaleX * sinAz, scaleY * cosAz * sinEl),
            Longer(0, scaleY * cosEl));
    }
}
