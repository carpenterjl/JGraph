using JGraph.Maths;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M116: a histogram stops searching its edges once per reading. The claim under test is not that
/// the finder is quick but that it is the same function — for every set of edges below, evenly
/// spread or not, and for every reading including the ones that sit exactly on an edge, the bin it
/// answers is the bin <see cref="Binning.BinOf"/> answers.
/// </summary>
/// <remarks>
/// The readings deliberately include each edge itself, values a hair either side of it, both ends of
/// the range, values outside it, and NaN — because the arithmetic shortcut is exactly what a value
/// sitting on a boundary is most likely to get wrong, and the repair step is what has to catch it.
/// </remarks>
public class BinFinderM116Tests
{
    public static TheoryData<string, double[]> EdgeSets() => new()
    {
        { "evenly spread", Spread(0, 1, 8) },
        { "evenly spread, many", Spread(-3.25, 11.75, 256) },
        { "evenly spread, negative", Spread(-100, -10, 9) },
        { "evenly spread, tiny", Spread(0, 1e-9, 5) },
        { "evenly spread, huge", Spread(-1e12, 1e12, 17) },
        { "one bin", [2, 5] },
        { "unevenly spread", [0, 0.1, 0.15, 3, 3.001, 90] },
        { "geometric", [1, 2, 4, 8, 16, 32, 64] },
        { "a repeated edge", [0, 1, 1, 2, 3] },
        { "two edges the same", [4, 4] },
        { "one edge", [7] },
        { "no edges", [] },
    };

    [Theory]
    [MemberData(nameof(EdgeSets))]
    public void TheFinderAnswersWhatTheSearchAnswers(string what, double[] edges)
    {
        Binning.BinFinder finder = Binning.BinFinder.For(edges);
        foreach (double value in Readings(edges))
        {
            Assert.Equal(Binning.BinOf(value, edges), finder.Of(value));
        }

        Assert.True(what.Length > 0);
    }

    [Theory]
    [MemberData(nameof(EdgeSets))]
    public void TheRightClosedFinderAnswersWhatTheRightClosedSearchAnswers(string what, double[] edges)
    {
        Binning.BinFinder finder = Binning.BinFinder.For(edges);
        foreach (double value in Readings(edges))
        {
            Assert.Equal(RightBinOf(value, edges), finder.OfRightClosed(value));
        }

        Assert.True(what.Length > 0);
    }

    /// <summary>A finder nobody built still refuses every reading rather than falling over.</summary>
    [Fact]
    public void AFinderOverNothingBinsNothing()
    {
        Binning.BinFinder none = default;
        Assert.Equal(-1, none.Of(0));
        Assert.Equal(-1, none.OfRightClosed(0));
    }

    private static double[] Spread(double low, double high, int bins)
    {
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = low + ((high - low) * i / bins);
        }

        edges[^1] = high;
        return edges;
    }

    /// <summary>
    /// Every edge, a hair either side of every edge, the midpoints, and values that miss altogether.
    /// </summary>
    private static IEnumerable<double> Readings(double[] edges)
    {
        yield return double.NaN;
        yield return double.NegativeInfinity;
        yield return double.PositiveInfinity;

        for (int i = 0; i < edges.Length; i++)
        {
            yield return edges[i];
            yield return Math.BitIncrement(edges[i]);
            yield return Math.BitDecrement(edges[i]);
            yield return edges[i] - 1e-9;
            yield return edges[i] + 1e-9;
            if (i + 1 < edges.Length)
            {
                yield return (edges[i] + edges[i + 1]) / 2;
            }
        }

        if (edges.Length > 0)
        {
            yield return edges[0] - 1;
            yield return edges[^1] + 1;
        }

        for (int i = 0; i < 200; i++)
        {
            yield return -5 + (i * 0.63);
        }
    }

    /// <summary>The right-closed rule as <c>discretize</c> wrote it before the finder existed.</summary>
    private static int RightBinOf(double value, double[] edges)
    {
        if (edges.Length < 2 || double.IsNaN(value) || value < edges[0] || value > edges[^1])
        {
            return -1;
        }

        if (value == edges[0])
        {
            return 0;
        }

        int low = 0;
        int high = edges.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (value <= edges[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }
}
