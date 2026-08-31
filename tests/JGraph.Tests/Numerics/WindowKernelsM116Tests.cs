using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M116: the sliding statistics stop rebuilding each window and start carrying it. What has to hold
/// is that the answer did not move, so every case below is measured against a reference that is the
/// walk this replaced — window built as a list, NaN dropped from it when asked, the same LINQ
/// summary at the end of it — over data chosen to make each rule matter: NaN inside the window and
/// at its edge, both infinities, both zeros, runs of equal values, and every endpoint rule against
/// every width.
/// </summary>
/// <remarks>
/// Where the fold is exact — the largest value, the smallest, the middle one — the comparison is bit
/// for bit. Where it is not — a sum, a mean, a product, a variance — a two-stack fold combines in a
/// different order than a left-to-right walk and is entitled to differ in the last place, so those
/// are compared to a relative tolerance instead. That distinction is the point rather than a
/// convenience: a test that let the largest value drift would not be testing anything.
/// </remarks>
public class WindowKernelsM116Tests
{
    /// <summary>Widths that put the point at the start of its window, the end, the middle, and alone.</summary>
    public static TheoryData<int, int> Widths() => new()
    {
        { 0, 0 },
        { 1, 0 },
        { 0, 1 },
        { 1, 1 },
        { 2, 2 },
        { 3, 1 },
        { 5, 5 },
        { 9, 0 },
        { 0, 9 },
        { 40, 40 }, // wider than the data, so every window is an incomplete one
    };

    public static TheoryData<WindowStat> Summaries() => new()
    {
        WindowStat.Sum,
        WindowStat.Mean,
        WindowStat.Max,
        WindowStat.Min,
        WindowStat.Product,
        WindowStat.Variance,
        WindowStat.StandardDeviation,
        WindowStat.Median,
    };

    [Theory]
    [MemberData(nameof(Summaries))]
    public void EverySummaryOverEveryEndpointRuleAnswersWhatTheWalkAnswered(WindowStat stat)
    {
        foreach (double[] values in Samples())
        {
            foreach ((int behind, int ahead) in Reaches())
            {
                foreach (WindowEnds ends in new[]
                    { WindowEnds.Shrink, WindowEnds.Discard, WindowEnds.Fill, WindowEnds.Pad })
                {
                    foreach (double pad in new[] { 0.0, 7.5, double.NaN })
                    {
                        foreach (bool omitNan in new[] { false, true })
                        {
                            double identity = IdentityFor(stat);
                            double[] wanted = Walked(
                                values, behind, ahead, ends, pad, omitNan, identity, stat);
                            double[] got = WindowKernels.Slide(
                                stat, values, behind, ahead, ends, pad, omitNan, identity);

                            Assert.Equal(wanted.Length, got.Length);
                            for (int i = 0; i < wanted.Length; i++)
                            {
                                AssertAgrees(stat, wanted[i], got[i], $"{stat} {ends} k=[{behind} {ahead}] pad={pad} omit={omitNan} at {i}");
                            }
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Summaries))]
    public void TheSamePlacesWalkedAsDistancesAnswerTheSameThing(WindowStat stat)
    {
        // Places that rise but are not evenly spread: the window then holds a different number of
        // readings at every point, which is the whole difference between this walk and the other.
        var places = new double[41];
        double at = 0;
        for (int i = 0; i < places.Length; i++)
        {
            places[i] = at;
            at += 0.4 + (0.6 * ((i * 7) % 5));
        }

        foreach (double[] values in Samples())
        {
            double[] slice = values[..Math.Min(values.Length, places.Length)];
            double[] used = places[..slice.Length];
            foreach (double reach in new[] { 0.0, 1.0, 2.5, 6.0, 1000.0 })
            {
                foreach (WindowEnds ends in new[] { WindowEnds.Shrink, WindowEnds.Discard, WindowEnds.Fill })
                {
                    foreach (bool omitNan in new[] { false, true })
                    {
                        double identity = IdentityFor(stat);
                        double[] wanted = WalkedOverPoints(
                            slice, used, reach, reach, ends, omitNan, identity, stat);
                        double[] got = WindowKernels.SlideOverPoints(
                            stat, slice, used, reach, reach, ends, omitNan, identity);

                        Assert.Equal(wanted.Length, got.Length);
                        for (int i = 0; i < wanted.Length; i++)
                        {
                            AssertAgrees(stat, wanted[i], got[i], $"{stat} {ends} reach={reach} omit={omitNan} at {i}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A window holding an infinity: the walk takes that infinity away from a mean which is itself
    /// infinite and answers NaN, and the merged form has to answer NaN too rather than the infinity
    /// its own formula would produce.
    /// </summary>
    [Fact]
    public void AnInfinityInTheWindowMakesTheSpreadNotANumber()
    {
        double[] values = [1, 2, double.PositiveInfinity, 4, 5];
        double[] variance = WindowKernels.Slide(
            WindowStat.Variance, values, 1, 1, WindowEnds.Shrink, 0, omitNan: false, double.NaN);

        Assert.Equal(0.5, variance[0], 12);          // 1, 2 — nothing wild in reach yet
        Assert.True(double.IsNaN(variance[1]));      // 1, 2, inf
        Assert.True(double.IsNaN(variance[2]));      // 2, inf, 4
        Assert.True(double.IsNaN(variance[3]));      // inf, 4, 5
        Assert.Equal(0.5, variance[4], 12);          // 4, 5 — past it again
    }

    /// <summary>
    /// The window a lone reading makes: a variance needs two values to divide by one, so one value
    /// is zero rather than a division by nothing, which is what the walk reported.
    /// </summary>
    [Fact]
    public void OneReadingHasNoSpread()
    {
        double[] values = [3, double.NaN, 5];
        double[] spread = WindowKernels.Slide(
            WindowStat.StandardDeviation, values, 0, 0, WindowEnds.Shrink, 0, omitNan: true, double.NaN);

        Assert.Equal(0, spread[0]);
        Assert.True(double.IsNaN(spread[1])); // nothing left in the window at all
        Assert.Equal(0, spread[2]);
    }

    /// <summary>Widths past the data are still asked for, and are the ones the endpoint rules are about.</summary>
    private static IEnumerable<(int Behind, int Ahead)> Reaches()
    {
        foreach (object[] row in Widths())
        {
            yield return ((int)row[0], (int)row[1]);
        }
    }

    /// <summary>
    /// Series that make each rule matter: plain values, values with NaN inside and at both edges,
    /// both infinities, both zeros, and long runs of one value.
    /// </summary>
    private static IEnumerable<double[]> Samples()
    {
        var plain = new double[41];
        for (int i = 0; i < plain.Length; i++)
        {
            plain[i] = Math.Sin(i * 1.7) * (1 + (i % 5));
        }

        yield return plain;

        double[] holed = [.. plain];
        holed[0] = double.NaN;
        holed[7] = double.NaN;
        holed[8] = double.NaN;
        holed[^1] = double.NaN;
        yield return holed;

        double[] wild = [.. plain];
        wild[3] = double.PositiveInfinity;
        wild[11] = double.NegativeInfinity;
        wild[12] = -0.0;
        wild[13] = 0.0;
        yield return wild;

        yield return [1, 1, 1, 1, 2, 2, 2, 2, 1, 1, 1, 1];
        yield return [double.NaN, double.NaN, double.NaN];
        yield return [42];
        yield return [];
    }

    private static double IdentityFor(WindowStat stat) => stat switch
    {
        WindowStat.Sum => 0,
        WindowStat.Product => 1,
        _ => double.NaN,
    };

    private static void AssertAgrees(WindowStat stat, double wanted, double got, string where)
    {
        bool exact = stat is WindowStat.Max or WindowStat.Min or WindowStat.Median;
        if (double.IsNaN(wanted) || double.IsNaN(got))
        {
            Assert.True(double.IsNaN(wanted) && double.IsNaN(got), $"{where}: wanted {wanted}, got {got}");
            return;
        }

        if (double.IsInfinity(wanted) || double.IsInfinity(got))
        {
            // A difference is no measure of two infinities, so they are compared as themselves.
            Assert.True(wanted.Equals(got), $"{where}: wanted {wanted:R}, got {got:R}");
            return;
        }

        if (exact)
        {
            // The two zeros are the one thing an order-independent fold may swap, because which of
            // them a comparison keeps depends on which arrived first.
            Assert.True(wanted.Equals(got) || (wanted == 0 && got == 0), $"{where}: wanted {wanted:R}, got {got:R}");
            return;
        }

        double scale = Math.Max(Math.Abs(wanted), Math.Abs(got));
        double slack = Math.Max(1e-9, scale * 1e-11);
        Assert.True(Math.Abs(wanted - got) <= slack, $"{where}: wanted {wanted:R}, got {got:R}");
    }

    /// <summary>The walk this replaced, kept here so the kernels have something to be measured against.</summary>
    private static double[] Walked(
        double[] values, int behind, int ahead, WindowEnds ends, double pad, bool omitNan,
        double identity, WindowStat stat)
    {
        int from = ends == WindowEnds.Discard ? behind : 0;
        int to = ends == WindowEnds.Discard ? values.Length - 1 - ahead : values.Length - 1;
        var result = new double[Math.Max(0, to - from + 1)];

        for (int i = from; i <= to; i++)
        {
            int start = i - behind;
            int stop = i + ahead;
            bool complete = start >= 0 && stop < values.Length;
            if (!complete && ends == WindowEnds.Fill)
            {
                result[i - from] = double.NaN;
                continue;
            }

            var window = new List<double>();
            for (int j = start; j <= stop; j++)
            {
                if (j >= 0 && j < values.Length)
                {
                    window.Add(values[j]);
                }
                else if (ends == WindowEnds.Pad)
                {
                    window.Add(pad);
                }
            }

            if (omitNan)
            {
                window.RemoveAll(double.IsNaN);
            }

            result[i - from] = window.Count == 0 ? identity : Summarise(stat, [.. window]);
        }

        return result;
    }

    private static double[] WalkedOverPoints(
        double[] values, double[] points, double behind, double ahead, WindowEnds ends,
        bool omitNan, double identity, WindowStat stat)
    {
        var answers = new List<double>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            bool complete = points[i] - behind >= points[0] && points[i] + ahead <= points[^1];
            if (!complete && ends == WindowEnds.Discard)
            {
                continue;
            }

            if (!complete && ends == WindowEnds.Fill)
            {
                answers.Add(double.NaN);
                continue;
            }

            var window = new List<double>();
            for (int j = 0; j < values.Length; j++)
            {
                if (points[j] < points[i] - behind || points[j] > points[i] + ahead)
                {
                    continue;
                }

                if (omitNan && double.IsNaN(values[j]))
                {
                    continue;
                }

                window.Add(values[j]);
            }

            answers.Add(window.Count == 0 ? identity : Summarise(stat, [.. window]));
        }

        return [.. answers];
    }

    private static double Summarise(WindowStat stat, double[] window) => stat switch
    {
        WindowStat.Sum => window.Sum(),
        WindowStat.Mean => window.Average(),
        WindowStat.Max => window.Max(),
        WindowStat.Min => window.Min(),
        WindowStat.Product => window.Aggregate(1.0, static (product, x) => product * x),
        WindowStat.Median => MedianOf(window),
        WindowStat.Variance => SampleVarianceOf(window),
        WindowStat.StandardDeviation => Math.Sqrt(SampleVarianceOf(window)),
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    private static double MedianOf(double[] window)
    {
        var sorted = (double[])window.Clone();
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    private static double SampleVarianceOf(double[] window)
    {
        if (window.Length < 2)
        {
            return 0;
        }

        double mean = window.Average();
        double total = 0;
        foreach (double x in window)
        {
            total += (x - mean) * (x - mean);
        }

        return total / (window.Length - 1);
    }
}
