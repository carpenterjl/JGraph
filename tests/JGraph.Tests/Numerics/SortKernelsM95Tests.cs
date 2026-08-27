using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M95: <c>sort</c> leaves the boxed road, and what licenses it is that the order does not change.
/// A sort's answer cannot move with the schedule the way a fold's can, so the claim under test here
/// is the tie rule instead: values compared with <c>&lt;</c> so the two zeros tie, equal values left
/// in the order they arrived, NaN lifted out and put back where <c>MissingPlacement</c> says. Every
/// kernel result below is compared against a reference built from a stable library sort over the
/// same rule — values bit for bit, positions exactly — across both layouts, every flag, and data
/// chosen to make each part of the rule matter.
/// </summary>
public class SortKernelsM95Tests
{
    /// <summary>Shapes that cover both layouts: contiguous slices, one long slice, interleaved
    /// pages, slices of one element, and a scalar.</summary>
    public static TheoryData<int, int, int> Shapes() => new()
    {
        { 1, 7, 5 },   // contiguous columns
        { 1, 40, 1 },  // one contiguous slice
        { 4, 6, 3 },   // interleaved pages, read with a stride
        { 6, 1, 4 },   // slices of one element
        { 5, 9, 1 },   // one page, wide
        { 1, 1, 1 },   // a scalar
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryLayoutAndFlagOrdersExactlyAsTheStableReferenceDoes(int inner, int n, int outer)
    {
        var split = new ReduceKernels.Split(inner, n, outer);
        foreach (double[] data in Cases((int)split.Total))
        {
            foreach (bool descending in new[] { false, true })
            {
                foreach (bool missingFirst in new[] { false, true })
                {
                    AssertMatchesReference(data, split, descending, missingFirst);
                }
            }
        }
    }

    // --- The tie rule, pinned one claim at a time -----------------------------------------------

    [Fact]
    public void TheTwoZerosTieAndKeepTheOrderTheyArrivedIn()
    {
        // -0 and +0 are the one pair of distinct doubles a comparison cannot separate, so they are
        // the one pair a sort could silently reorder. MATLAB's does not, in either direction.
        double[] data = [0.0, -0.0, 1.0, -0.0, 0.0, -1.0];
        var split = new ReduceKernels.Split(1, data.Length, 1);

        (double[] up, _) = Run(data, split, descending: false, missingFirst: false, positions: false);
        AssertBitsEach([-1.0, 0.0, -0.0, -0.0, 0.0, 1.0], up);

        (double[] down, _) = Run(data, split, descending: true, missingFirst: false, positions: false);
        AssertBitsEach([1.0, 0.0, -0.0, -0.0, 0.0, -1.0], down);
    }

    [Fact]
    public void TheValuesOnlyPathAndThePositionsPathAgreeOnTheZerosToo()
    {
        // The two paths repair the zeros differently — one rewrites the signs it remembered, the
        // other re-reads each value through the position it kept — so they are worth comparing.
        double[] data = [0.0, -0.0, -0.0, 0.0, -0.0];
        var split = new ReduceKernels.Split(1, data.Length, 1);
        (double[] alone, _) = Run(data, split, false, false, positions: false);
        (double[] paired, double[] at) = Run(data, split, false, false, positions: true);
        AssertBitsEach(data, alone);   // all equal, so arrival order is the answer
        AssertBitsEach(data, paired);
        Assert.Equal<double>([1, 2, 3, 4, 5], at);
    }

    [Fact]
    public void EqualValuesReportTheirOwnPositionsInAscendingOrderBothWaysRound()
    {
        double[] data = [5, 1, 5, 1, 5];
        var split = new ReduceKernels.Split(1, data.Length, 1);

        (_, double[] up) = Run(data, split, descending: false, missingFirst: false, positions: true);
        Assert.Equal<double>([2, 4, 1, 3, 5], up);

        (_, double[] down) = Run(data, split, descending: true, missingFirst: false, positions: true);
        Assert.Equal<double>([1, 3, 5, 2, 4], down);
    }

    [Fact]
    public void MissingValuesGoToTheEndTheyAreAskedForAndKeepTheirOwnOrder()
    {
        double[] data = [double.NaN, 3, -1, double.NaN, 2];
        var split = new ReduceKernels.Split(1, data.Length, 1);

        (_, double[] last) = Run(data, split, descending: false, missingFirst: false, positions: true);
        Assert.Equal<double>([3, 5, 2, 1, 4], last);

        (_, double[] first) = Run(data, split, descending: false, missingFirst: true, positions: true);
        Assert.Equal<double>([1, 4, 3, 5, 2], first);

        (_, double[] down) = Run(data, split, descending: true, missingFirst: true, positions: true);
        Assert.Equal<double>([1, 4, 2, 5, 3], down);
    }

    [Fact]
    public void AMissingValueKeepsWhicheverBitsItArrivedWith()
    {
        // A NaN is not one value: it has a sign and a payload, and a sort moves it rather than
        // remaking it. The positions path re-reads it from the source, which is what keeps this true.
        double[] data =
        [
            BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8_0000_0000_0000)),
            4,
            BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0001),
        ];

        var split = new ReduceKernels.Split(1, data.Length, 1);
        foreach (bool positions in new[] { false, true })
        {
            (double[] values, _) = Run(data, split, false, false, positions);
            AssertBits(4, values[0]);
            AssertBits(data[0], values[1]);
            AssertBits(data[2], values[2]);
        }
    }

    [Fact]
    public void AWhollyMissingSliceIsLeftExactlyAsItCame()
    {
        double[] data = [double.NaN, double.NaN, double.NaN];
        var split = new ReduceKernels.Split(1, data.Length, 1);
        (double[] values, double[] at) = Run(data, split, false, false, positions: true);
        Assert.All(values, v => Assert.True(double.IsNaN(v)));
        Assert.Equal<double>([1, 2, 3], at);
    }

    [Fact]
    public void AnAlreadySortedRunIsRecognisedRatherThanSortedAgain()
    {
        // The kernel skips the sort when a run is already ascending. That shortcut has to leave the
        // positions alone as well, which is only right because the scatter kept arrival order.
        double[] data = new double[600_000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i / 3;   // ascending with long runs of equals
        }

        var split = new ReduceKernels.Split(1, data.Length, 1);
        (double[] values, double[] at) = Run(data, split, false, false, positions: true);
        for (int i = 0; i < data.Length; i++)
        {
            AssertBits(data[i], values[i]);
            Assert.Equal(i + 1, at[i]);
        }
    }

    [Fact]
    public void ASliceOfOneRepeatedValueSortsWithoutLosingItsPositions()
    {
        // Every splitter comes out the same value, so the whole slice lands in one bucket — the
        // degenerate cut, and the one most likely to be got wrong.
        double[] data = new double[600_000];
        Array.Fill(data, 2.5);
        var split = new ReduceKernels.Split(1, data.Length, 1);
        (double[] values, double[] at) = Run(data, split, false, false, positions: true);
        Assert.All(values, v => AssertBits(2.5, v));
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(i + 1, at[i]);
        }
    }

    // --- A thread count is not an input ---------------------------------------------------------

    /// <summary>Sizes that certainly take the threaded road: one slice past
    /// <see cref="SortKernels.SliceThreshold"/>, and one strided slice past it too.</summary>
    public static TheoryData<int, int, int> ThreadedShapes() => new()
    {
        { 1, 700_003, 1 },   // one long contiguous slice, split by value
        { 3, 200_003, 1 },   // strided slices, gathered and split
        { 1, 1_024, 4_179 }, // many slices, one thread each
    };

    [Theory]
    [MemberData(nameof(ThreadedShapes))]
    public void TheSameBitsComeBackAtOneThreadAndAtSixteen(int inner, int n, int outer)
    {
        var split = new ReduceKernels.Split(inner, n, outer);
        double[] data = Scattered((int)split.Total, nanEvery: 97, zeroEvery: 313);

        foreach (bool descending in new[] { false, true })
        {
            foreach (bool positions in new[] { false, true })
            {
                (double[] valuesOne, double[] atOne) =
                    AtDegree(1, () => Run(data, split, descending, false, positions));
                (double[] valuesMany, double[] atMany) =
                    AtDegree(16, () => Run(data, split, descending, false, positions));
                AssertSameBits(valuesOne, valuesMany);
                Assert.Equal(atOne, atMany);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ThreadedShapes))]
    public void TheThreadedRoadOrdersExactlyAsTheStableReferenceDoes(int inner, int n, int outer)
    {
        var split = new ReduceKernels.Split(inner, n, outer);
        double[] data = Scattered((int)split.Total, nanEvery: 97, zeroEvery: 313);
        AssertMatchesReference(data, split, descending: false, missingFirst: false);
        AssertMatchesReference(data, split, descending: true, missingFirst: true);
    }

    // --- Machinery ------------------------------------------------------------------------------

    /// <summary>Slices worth ordering: clean, NaN-speckled, wholly NaN, both zeros, all equal,
    /// already ordered, and ordered the wrong way.</summary>
    private static IEnumerable<double[]> Cases(int total)
    {
        yield return Scattered(total, nanEvery: 0, zeroEvery: 0);
        yield return Scattered(total, nanEvery: 3, zeroEvery: 0);
        yield return Scattered(total, nanEvery: 4, zeroEvery: 5);

        var blank = new double[total];
        Array.Fill(blank, double.NaN);
        yield return blank;

        var same = new double[total];
        Array.Fill(same, -7.25);
        yield return same;

        var up = new double[total];
        var down = new double[total];
        for (int i = 0; i < total; i++)
        {
            up[i] = i * 0.5;
            down[i] = (total - i) * 0.5;
        }

        yield return up;
        yield return down;

        // Infinities at both ends, and duplicates enough that ties decide most of the answer.
        var few = new double[total];
        for (int i = 0; i < total; i++)
        {
            few[i] = (i % 5) switch
            {
                0 => double.PositiveInfinity,
                1 => double.NegativeInfinity,
                2 => 1,
                3 => -1,
                _ => 0,
            };
        }

        yield return few;
    }

    private static double[] Scattered(int length, int nanEvery, int zeroEvery)
    {
        var data = new double[length];
        for (int i = 0; i < length; i++)
        {
            if (nanEvery > 0 && i % nanEvery == 1)
            {
                data[i] = double.NaN;
            }
            else if (zeroEvery > 0 && i % zeroEvery == 2)
            {
                data[i] = i % (2 * zeroEvery) == 2 ? -0.0 : 0.0;
            }
            else
            {
                data[i] = (((i * 7_919) % 9_973) * 0.001_37) - 6.5;
            }
        }

        return data;
    }

    /// <summary>
    /// The order MATLAB gives, built the plain way: NaN taken out, everything else through a stable
    /// library sort over a comparison that only ever asks <c>&lt;</c> — which is what makes the two
    /// zeros tie and their arrival order the answer.
    /// </summary>
    private static (double[] Values, double[] At) Reference(
        double[] slice, bool descending, bool missingFirst)
    {
        IComparer<double> byValue = Comparer<double>.Create(
            static (a, b) => a < b ? -1 : b < a ? 1 : 0);

        var present = new List<int>();
        var absent = new List<int>();
        for (int i = 0; i < slice.Length; i++)
        {
            (double.IsNaN(slice[i]) ? absent : present).Add(i);
        }

        IEnumerable<int> ordered = descending
            ? present.OrderByDescending(i => slice[i], byValue)
            : present.OrderBy(i => slice[i], byValue);

        int[] at = missingFirst
            ? [.. absent, .. ordered]
            : [.. ordered, .. absent];
        return ([.. at.Select(i => slice[i])], [.. at.Select(i => (double)(i + 1))]);
    }

    private static void AssertMatchesReference(
        double[] data, ReduceKernels.Split split, bool descending, bool missingFirst)
    {
        (double[] values, double[] at) = Run(data, split, descending, missingFirst, positions: true);
        for (int s = 0; s < split.Slices; s++)
        {
            (double[] wantValues, double[] wantAt) =
                Reference(SliceOf(data, split, s), descending, missingFirst);
            for (int j = 0; j < split.Count; j++)
            {
                AssertBits(wantValues[j], ElementOf(values, split, s, j));
                Assert.Equal(wantAt[j], ElementOf(at, split, s, j));
            }
        }

        // Asked for the values alone the kernel takes a different road through the same rule, so
        // the two must land on the same bits.
        (double[] alone, _) = Run(data, split, descending, missingFirst, positions: false);
        AssertSameBits(values, alone);
    }

    private static (double[] Values, double[] At) Run(
        double[] data, ReduceKernels.Split split, bool descending, bool missingFirst, bool positions)
    {
        using NumericBuffer src = ManagedBuffer.Adopt((double[])data.Clone());
        using var values = new ManagedBuffer((int)split.Total);
        using ManagedBuffer? at = positions ? new ManagedBuffer((int)split.Total) : null;
        SortKernels.SortAlong(src, values, at, split, descending, missingFirst, indexBase: 1);
        return (values.AsSpan().ToArray(), at is null ? [] : at.AsSpan().ToArray());
    }

    private static double[] SliceOf(double[] data, ReduceKernels.Split split, int s)
    {
        var slice = new double[split.Count];
        for (int j = 0; j < split.Count; j++)
        {
            slice[j] = ElementOf(data, split, s, j);
        }

        return slice;
    }

    private static double ElementOf(double[] flat, ReduceKernels.Split split, int s, int j) =>
        flat[((s / split.Inner) * split.Inner * split.Count) + (j * split.Inner) + (s % split.Inner)];

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private static void AssertBitsEach(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertBits(expected[i], actual[i]);
        }
    }

    private static void AssertSameBits(double[] left, double[] right) => AssertBitsEach(left, right);

    private static T AtDegree<T>(int degree, Func<T> body)
    {
        int previous = ParallelKernels.MaxDegree;
        ParallelKernels.MaxDegree = degree;
        try
        {
            return body();
        }
        finally
        {
            ParallelKernels.MaxDegree = previous;
        }
    }
}
