using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// How R2025b weighs a candidate tick step on a ruler of a given length (open items 99 and 119).
/// A candidate holding more ticks than <see cref="MaxLanding"/> (both limits on ticks) or
/// <see cref="MaxFree"/> (otherwise) is out; among the rest the score is the coverage of the range
/// by the ticks, less <see cref="Under"/> per tick short of <see cref="Target"/> and <see cref="Over"/>
/// per tick past it, plus <see cref="Landing"/> when both limits sit on ticks. The best score wins;
/// an equal score goes to the finer step unless that step sits on both limits at its landing cap.
/// A cap of 0 is no cap. <see cref="FitCap"/> is the most ticks a tick-aligned limit may move out
/// to hold: the free cap where there is one, and on a scored band the count past which the
/// over-penalty outweighs the landing allowance.
/// </summary>
public readonly record struct TickScore(double Target, double Under, double Over, double Landing, int MaxLanding = 0, int MaxFree = 0, int FitCap = 0)
{
    /// <summary>The batch 18 rule alone: caps and coverage, no target.</summary>
    public static TickScore Caps(int landing, int free) => new(0, 0, 0, 0, landing, free, free);

    /// <summary>The same score with the landing allowance withdrawn, which is how limits are fitted.</summary>
    public TickScore WithoutLanding() => this with { Landing = 0, MaxLanding = MaxFree };
}

/// <summary>
/// How many ticks a ruler may carry: the hint the scale-specific generators aim for, and the score
/// R2025b's linear rule weighs candidate steps by (open items 99 and 119). A budget without a score
/// asks for the classic ladder a target count drove, which is what a caller with no pixels to
/// measure still gets.
/// </summary>
/// <param name="Target">The count the logarithmic, date and category generators aim near.</param>
/// <param name="Score">R2025b's score for the ruler's length and font, or null for the classic ladder.</param>
public readonly record struct TickBudget(int Target, TickScore? Score)
{
    /// <summary>A budget with only the classic target count, for a ruler nobody has measured.</summary>
    public static TickBudget Of(int target) => new(target, null);

    /// <summary>Whether R2025b's scored choice applies.</summary>
    public bool HasCaps => Score is not null;

    /// <summary>
    /// R2025b's budget for a ruler of a given pixel length and font, keeping <paramref name="target"/>
    /// for the generators that do not read scores.
    /// </summary>
    public static TickBudget ForRuler(double pixels, double fontPoints, bool vertical, int target, double drawnFontPoints = double.NaN) =>
        new(target, TickRule.ScoreFor(pixels, fontPoints, vertical, drawnFontPoints));
}

/// <summary>
/// R2025b's choice of linear ticks and of the limits around them, as a rule fitted to its answers
/// (open items 99 and 119; probe_99, probe_99b, probe_99c in the batch 17 and 18 scratch, probe_119
/// in batch 19, probe_119b and probe_119c in batch 25). The arithmetic:
/// <list type="bullet">
/// <item>Candidate steps are 1, 2 and 5 times a power of ten.</item>
/// <item>A ruler's length in pixels puts it in a band whose edges were measured at 8, 10 and 13
/// points. On the shorter rulers (under 290 pixels across, 230 up, at 10 points) each candidate is
/// scored: the span of its ticks over the range, less a penalty for every tick it falls short of a
/// target count and a larger one for every tick over it, plus an allowance when both limits sit on
/// ticks; the target rises from three to nine through the bands, and the vertical bands come sooner
/// because labels stack by their height. On the longer rulers a count cap replaces the target
/// (twelve when both limits sit on ticks, eleven otherwise, at the top) and the best coverage wins;
/// a tie goes to the finer step unless it sits on both limits at its landing cap. A candidate is
/// counted tolerantly (a limit a hair past a tick still counts it), and one that then exceeds its
/// cap, or a scored band's whole target, is counted again in R2025b's own double arithmetic, where
/// 3 × 0.1 overshoots 0.3, and drawn by that count.</item>
/// <item>A <c>tickaligned</c> limit moves outward to the next tick when that is at most a quarter of
/// the data range away, and a step whose moved limits then prefer a coarser one gives way to it. A
/// single value gets a unit either side.</item>
/// <item>A <c>padded</c> limit sits about 7% outside the data and snaps to a tick multiple of the
/// data's own step when one is within 3% of the range.</item>
/// </list>
/// The scored bands reproduce 99 to 100 per cent of the 22,000 answers recorded at a fixed font; the
/// capped ones 99 per cent from 217 pixels up. On the shortest rulers (under 110 pixels across, 65
/// up) R2025b also reads the sign of the limits, taking more ticks when zero lies inside a range than
/// when it does not, which no score of counts and coverage expresses; that band reproduces 96 per
/// cent of the ranges that start at zero, and the divergence list records the rest.
/// </summary>
public static class TickRule
{
    /// <summary>The fraction of the data range a tick-aligned limit may move outward.</summary>
    public const double AlignmentReach = 0.25;

    /// <summary>The fraction of the range a padded limit snaps across to reach a tick multiple.</summary>
    public const double PaddedSnap = 0.03;

    /// <summary>
    /// The tick font the tables were measured against: R2025b's default axes font of 10 points,
    /// which a JGraph axes draws at its own built-in 11, so a figure at default fonts takes R2025b's
    /// default-figure counts and every other font is read as ten elevenths of itself. Only a written
    /// font counts: the automatic shrink of a small axes (open item 118) leaves R2025b's ticks
    /// exactly where the unshrunk font puts them (probe_119d), so a caller passes the nominal font
    /// for an automatic one.
    /// </summary>
    public const double ReferenceFontPoints = 11;

    /// <summary>R2025b's default axes font, the points the tables are written in.</summary>
    private const double TablePoints = 10;

    /// <summary>
    /// The scores of a ruler lying across the page, from the shortest band up: the target count
    /// rises from three to five and a half. Fitted to probe_119b and probe_119c (181 ranges at every
    /// five pixels, ranges slid and stretched).
    /// </summary>
    private static readonly TickScore[] HorizontalScores =
    [
        new(3, 0.08, 0.40, 0, FitCap: 3),
        new(3.5, 0.40, 0.24, 0, FitCap: 5),
        new(4.5, 0.32, 0.48, 0.30, FitCap: 5),
        new(5.5, 0.16, 0.48, 0.30, FitCap: 6),
    ];

    /// <summary>The scores of a ruler standing up the page, whose labels stack by their height.</summary>
    private static readonly TickScore[] VerticalScores =
    [
        new(3, 0.08, 0.40, 0, FitCap: 3),
        new(3.5, 0.40, 0.24, 0, FitCap: 5),
        new(4.5, 0.32, 0.48, 0.30, FitCap: 5),
        new(5.5, 0.16, 0.48, 0.30, FitCap: 6),
        new(7, 0.08, 0.64, 0.30, FitCap: 7),
        new(7.5, 0.08, 0.48, 0.30, FitCap: 8),
        new(9, 0.08, 0.48, 0.30, FitCap: 9),
    ];

    /// <summary>
    /// Where the scored bands end, in pixels, at each font R2025b was measured at (probe_119b at 8,
    /// 10 and 13 points): the length below which a ruler takes the band's score, one entry per
    /// score, the last being where the capped bands begin. The edges do not scale with the font:
    /// the first horizontal one stays near 110 pixels from 8 to 10 points and only then moves, and
    /// the vertical ones hardly move below 10 points. Between measured fonts the edges are
    /// interpolated; past 13 points they scale with the font, and under 8 they stay. The 13-point
    /// sweep ended at 260 pixels, so its later edges are the 10-point ones scaled.
    /// </summary>
    private static readonly (double Points, double[] Edges)[] HorizontalEdges =
    [
        (8, [105, 145, 175, 215]),
        (10, [110, 170, 210, 290]),
        (13, [175, 235, 273, 377]),
    ];

    /// <summary>The vertical bands' edges by font, as <see cref="HorizontalEdges"/>.</summary>
    private static readonly (double Points, double[] Edges)[] VerticalEdges =
    [
        (8, [65, 85, 105, 125, 155, 175, 215]),
        (10, [65, 90, 110, 130, 155, 205, 230]),
        (13, [105, 145, 175, 215, 255, 266, 299]),
    ];

    /// <summary>
    /// The capped bands of the longer horizontal rulers: below each length in pixels at
    /// <see cref="TablePoints"/>, scaled by the font, the caps (landing, free). Measured at every
    /// five pixels from 260 to 500 at a written 10 points (probe_119d, 181 ranges a length): each
    /// band reproduces every range but the two or three where R2025b's own double arithmetic reads
    /// a limit as off its tick (<c>[0 0.55]</c> in twentieths).
    /// </summary>
    private static readonly (double Below, TickScore Score)[] HorizontalCaps =
    [
        (338, TickScore.Caps(8, 7)),
        (383, TickScore.Caps(8, 8)),
        (433, TickScore.Caps(10, 9)),
        (478, TickScore.Caps(11, 10)),
        (double.PositiveInfinity, TickScore.Caps(12, 11)),
    ];

    /// <summary>The capped bands of the longer vertical rulers (probe_119d, 230 to 350 pixels).</summary>
    private static readonly (double Below, TickScore Score)[] VerticalCaps =
    [
        (243, TickScore.Caps(10, 9)),
        (268, TickScore.Caps(11, 10)),
        (double.PositiveInfinity, TickScore.Caps(12, 11)),
    ];

    /// <summary>
    /// The score of R2025b's polar r ruler, which answers the same at every size and font measured
    /// (probe_119, probe_119b: 21 radii from 100 to 600 pixels): a target of five with a steep
    /// penalty past it. It reproduces 86 per cent of the ranges; the rest prefer or refuse a step
    /// by its leading digit in a way no score of counts expresses.
    /// </summary>
    public static TickBudget PolarRadius(int target) => new(target, new TickScore(5, 0.40, 0.68, 0.15));

    /// <summary>
    /// The score a ruler of <paramref name="pixels"/> takes at <paramref name="fontPoints"/>, the
    /// nominal font, which places the scored bands' edges; the capped bands beyond them scale with
    /// <paramref name="drawnFontPoints"/>, the font the labels are drawn at (the same unless the
    /// axes shrank it automatically), which is where R2025b's automatic shrink does count: a
    /// 255-pixel square axes at its 9.5 points takes eleven ticks on <c>[0 1]</c> where a 255-pixel
    /// ruler at a written 10 takes six (probe_119d, oi_patch_rows_axes_font_short_rulers).
    /// </summary>
    public static TickScore ScoreFor(double pixels, double fontPoints, bool vertical, double drawnFontPoints = double.NaN)
    {
        if (!double.IsFinite(pixels) || pixels < 0)
        {
            pixels = 0;
        }

        double points = PointsOf(fontPoints);
        TickScore[] scores = vertical ? VerticalScores : HorizontalScores;
        (double Points, double[] Edges)[] edges = vertical ? VerticalEdges : HorizontalEdges;
        for (int i = 0; i < scores.Length; i++)
        {
            if (pixels < EdgeAt(edges, i, points))
            {
                return scores[i];
            }
        }

        double scaled = pixels * TablePoints / PointsOf(double.IsNaN(drawnFontPoints) ? fontPoints : drawnFontPoints);
        foreach ((double below, TickScore score) in vertical ? VerticalCaps : HorizontalCaps)
        {
            if (scaled < below)
            {
                return score;
            }
        }

        return TickScore.Caps(12, 11);
    }

    /// <summary>A JGraph font in the tables' points: ten elevenths of it, or the table's own for none.</summary>
    private static double PointsOf(double fontPoints) =>
        fontPoints > 0 && double.IsFinite(fontPoints) ? fontPoints * TablePoints / ReferenceFontPoints : TablePoints;

    /// <summary>
    /// The <paramref name="index"/>th band edge at <paramref name="points"/>: interpolated between
    /// the measured fonts, held under the smallest and scaled past the largest.
    /// </summary>
    private static double EdgeAt((double Points, double[] Edges)[] edges, int index, double points)
    {
        (double smallest, double[] first) = edges[0];
        if (points <= smallest)
        {
            return first[index];
        }

        (double largest, double[] last) = edges[^1];
        if (points >= largest)
        {
            return last[index] * points / largest;
        }

        for (int i = 1; i < edges.Length; i++)
        {
            (double upper, double[] above) = edges[i];
            if (points <= upper)
            {
                (double lower, double[] below) = edges[i - 1];
                double t = (points - lower) / (upper - lower);
                return below[index] + (t * (above[index] - below[index]));
            }
        }

        return last[index];
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
    public static double ChooseStep(double min, double max, TickBudget budget) =>
        ChooseStep(min, max, budget, out _);

    /// <summary>
    /// <see cref="ChooseStep(double, double, TickBudget)"/>, also saying whether the step's ticks are
    /// to be counted in exact double arithmetic (<paramref name="exactly"/>): a limit a hair short of
    /// a tick then loses it, as [0 0.3] on a 60-pixel ruler is drawn 0, 0.1, 0.2 while on a 250-pixel
    /// one it is drawn to 0.3 (probe_119b).
    /// </summary>
    public static double ChooseStep(double min, double max, TickBudget budget, out bool exactly)
    {
        exactly = false;
        double span = max - min;
        if (!(span > 0) || !double.IsFinite(span))
        {
            return 0;
        }

        TickScore score = budget.Score ?? TickScore.Caps(12, 11);
        int exponent = (int)System.Math.Floor(System.Math.Log10(span));
        double best = double.NegativeInfinity;
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
                int cap = lands ? score.MaxLanding : score.MaxFree;
                bool countedExactly = false;
                if (cap > 0 ? count > cap : count > System.Math.Ceiling(score.Target))
                {
                    // Too many by the tolerant reading, for the cap or for a scored band's whole
                    // target: the exact one may lose the last tick to floating point and fit after
                    // all, under the free cap (probe_119), and is then the count drawn: [0 0.3] on
                    // a 60-pixel ruler at 10 points is drawn 0, 0.1, 0.2, at 8 points to 0.3.
                    count = CountExactly(min, max, step, out first, out last);
                    if (count == 0 || (cap > 0 && score.MaxFree > 0 && count > score.MaxFree))
                    {
                        continue;
                    }

                    countedExactly = true;
                    if (cap > 0)
                    {
                        lands = false;
                    }
                }

                double coverage = (last - first) / span;
                double penalty = count < score.Target ? score.Under * (score.Target - count) : score.Over * (count - score.Target);
                double scored = coverage - penalty + (lands ? score.Landing : 0);

                // Equal scores go to the finer step, which was seen first, unless that step sits on
                // both limits with exactly as many ticks as its cap allows: [0 6] on a 217-pixel ruler
                // takes 2 over 1, while [0 6] on a 300-pixel one takes 1 (probe_99b).
                bool better = scored > best + 1e-12
                    || (System.Math.Abs(scored - best) <= 1e-12 && bestAtLandingCap);
                if (better)
                {
                    best = scored;
                    bestStep = step;
                    exactly = countedExactly;
                    bestAtLandingCap = lands && score.MaxLanding > 0 && count >= score.MaxLanding;
                }
            }
        }

        if (bestStep > 0)
        {
            return bestStep;
        }

        // Nothing fitted under the cap: the coarsest step that still puts two ticks inside, as
        // R2025b draws [0.123 0.987] on a 50-pixel ruler in fifths (probe_119), else one tick.
        exactly = false;
        return widestPair > 0 ? widestPair : widest;
    }

    /// <summary>
    /// R2025b's <c>tickaligned</c> limits for a data extent: the step the extent would take, each
    /// limit moved outward to its next tick when that is within <see cref="AlignmentReach"/> of the
    /// range, and the step given up for a coarser one when the moved limits hold more ticks than
    /// the band's <see cref="TickScore.FitCap"/>. The landing allowance does not apply here, which
    /// is why <c>[-1 1000]</c> ends in steps of 200 at <c>[-200 1000]</c>, not 100.
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

        if (budget.Score is not { } score)
        {
            return ClassicAlign(data);
        }

        TickBudget free = budget with { Score = score.WithoutLanding() };
        double step = ChooseStep(data.Min, data.Max, free);
        if (!(step > 0))
        {
            return data;
        }

        // A coarser step moves the data's own limits afresh: [-3 11] on a 60-pixel ruler at 10
        // points stays [-3 11] with ticks 0, 5, 10, since its fives would hold four ticks there
        // and its tens reach neither limit (oi_short_rulers).
        for (int attempt = 0; attempt < 8; attempt++)
        {
            DataRange moved = MoveToTicks(data, step);
            if (score.FitCap <= 0 || CountWithin(moved.Min, moved.Max, step, out _, out _) <= score.FitCap)
            {
                return moved;
            }

            step = Coarser(step);
        }

        return data;
    }

    /// <summary>
    /// Each limit moved outward to the next multiple of <paramref name="step"/> when that is within
    /// <see cref="AlignmentReach"/> of the range. A limit a hair past a tick is read as on it, and a
    /// limit is never moved inward: R2025b leaves plot(1, -1e-7)'s lower limit at -1.0000001 rather
    /// than at -1 or -1.2 (probe_99b).
    /// </summary>
    private static DataRange MoveToTicks(DataRange data, double step)
    {
        double span = data.Length;
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

        return new DataRange(low, high);
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
