using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The selection kernel M120 put under <c>median</c> and <c>prctile</c>.
/// </summary>
/// <remarks>
/// Every check here is against a full sort of the same data, because that is exactly the claim
/// being made: a selection answers what a sort would have answered, and costs less. The input
/// shapes were not guessed at: the partitions each one causes were counted first, which is how the
/// two that exhaust the recursion budget were found and how the several that were assumed to and
/// do not were ruled out.
/// </remarks>
public class SelectKernelsM120Tests
{
    private static double[] Shape(string kind, int n, int seed)
    {
        var data = new double[n];
        var random = new Random(seed);
        for (int i = 0; i < n; i++)
        {
            data[i] = kind switch
            {
                "random" => random.NextDouble() * 200 - 100,
                "ascending" => i,
                "descending" => n - i,
                "equal" => 7.5,
                "twovalues" => i % 2,
                "fewvalues" => i % 5,

                // Up then down, and down then up. These are the two shapes in this set that make
                // median-of-three choose badly: counting partitions over twenty thousand elements
                // gives 40 and 50 levels against random data's 15, and a budget of 30 — so they
                // are the ones that actually reach the fallback, and the only ones that do.
                "organpipe" => i < n / 2 ? i : n - i,
                "valley" => i < n / 2 ? (n / 2) - i : i - (n / 2),
                "sawtooth" => i % 64,
                _ => throw new ArgumentException(kind),
            };
        }

        return data;
    }

    [Theory]
    [InlineData("random")]
    [InlineData("ascending")]
    [InlineData("descending")]
    [InlineData("equal")]
    [InlineData("twovalues")]
    [InlineData("fewvalues")]
    [InlineData("organpipe")]
    [InlineData("valley")]
    [InlineData("sawtooth")]
    public void EveryRequestedRankHoldsWhatASortWouldHavePutThere(string kind)
    {
        foreach (int n in new[] { 1, 2, 3, 7, 23, 24, 25, 100, 1001, 5000 })
        {
            double[] source = Shape(kind, n, 11 + n);
            double[] sorted = (double[])source.Clone();
            Array.Sort(sorted);

            // One rank at a time, so a rank that only happens to land right because a neighbour
            // was asked for cannot hide.
            for (int rank = 0; rank < n; rank++)
            {
                double[] scratch = (double[])source.Clone();
                Span<int> one = [rank];
                SelectKernels.PartialSort(scratch, one);
                Assert.Equal(sorted[rank], scratch[rank]);
            }
        }
    }

    [Theory]
    [InlineData("random")]
    [InlineData("organpipe")]
    [InlineData("fewvalues")]
    [InlineData("equal")]
    public void SeveralRanksAtOnceAnswerTheSameAsSeveralOneAtATime(string kind)
    {
        foreach (int n in new[] { 5, 40, 777, 4096 })
        {
            double[] source = Shape(kind, n, 3 + n);
            double[] sorted = (double[])source.Clone();
            Array.Sort(sorted);

            int[] wanted = [0, n - 1, n / 2, n / 4, (3 * n) / 4, n / 3];
            wanted = [.. wanted.Distinct()];

            double[] scratch = (double[])source.Clone();
            SelectKernels.PartialSort(scratch, wanted.AsSpan());
            foreach (int rank in wanted)
            {
                Assert.Equal(sorted[rank], scratch[rank]);
            }
        }
    }

    [Fact]
    public void ARepeatedRankIsNotAProblem()
    {
        double[] source = Shape("random", 500, 9);
        double[] sorted = (double[])source.Clone();
        Array.Sort(sorted);

        double[] scratch = (double[])source.Clone();
        Span<int> repeated = [250, 250, 100, 100, 100, 499, 0];
        SelectKernels.PartialSort(scratch, repeated);
        Assert.Equal(sorted[250], scratch[250]);
        Assert.Equal(sorted[100], scratch[100]);
        Assert.Equal(sorted[499], scratch[499]);
        Assert.Equal(sorted[0], scratch[0]);
    }

    [Fact]
    public void APlacedRankHasNothingGreaterBelowItAndNothingSmallerAbove()
    {
        // The rank being right is not the whole promise: a percentile reads the element beside the
        // one it asked for, and that is only sound if the array is split about the placed rank.
        double[] scratch = Shape("random", 3000, 42);
        Span<int> ranks = [750, 1500, 2250];
        SelectKernels.PartialSort(scratch, ranks);

        foreach (int rank in new[] { 750, 1500, 2250 })
        {
            double pivot = scratch[rank];
            for (int i = 0; i < rank; i++)
            {
                Assert.True(scratch[i] <= pivot, $"index {i} is above the element placed at {rank}");
            }

            for (int i = rank + 1; i < scratch.Length; i++)
            {
                Assert.True(scratch[i] >= pivot, $"index {i} is below the element placed at {rank}");
            }
        }
    }

    [Theory]
    [InlineData("organpipe")]
    [InlineData("valley")]
    public void TheShapesThatExhaustTheBudgetAreStillAnswered(string kind)
    {
        // These are the two shapes measured to run out of levels: at twenty thousand elements they
        // take 40 and 50 partitions where the budget allows 30, so the fallback sort is what
        // finishes them. It is worth saying what that fallback is and is not for -- median-of-three
        // does not collapse on these, it merely doubles or trebles the elements it touches, and the
        // budget is a ceiling on a case nobody here has managed to construct rather than a rescue
        // from one that happens. This asserts the answer, which is the part that must hold either
        // way; disabling the budget entirely leaves this passing, and passing quickly.
        const int n = 200_000;
        double[] scratch = Shape(kind, n, 1);
        double[] sorted = (double[])scratch.Clone();
        Array.Sort(sorted);

        Span<int> ranks = [n / 4, n / 2, (3 * n) / 4];
        SelectKernels.PartialSort(scratch, ranks);
        Assert.Equal(sorted[n / 4], scratch[n / 4]);
        Assert.Equal(sorted[n / 2], scratch[n / 2]);
        Assert.Equal(sorted[(3 * n) / 4], scratch[(3 * n) / 4]);
    }

    [Fact]
    public void NothingIsAskedOfAnEmptyArrayOrAnEmptyRankList()
    {
        double[] nothing = [];
        Span<int> none = [];
        SelectKernels.PartialSort(nothing, none);

        double[] some = [3, 1, 2];
        SelectKernels.PartialSort(some, none);
        Assert.Equal([3.0, 1.0, 2.0], some);
    }

    [Fact]
    public void TheSingleRankHelperReadsBackWhatItPlaced()
    {
        double[] scratch = Shape("random", 999, 5);
        double[] sorted = (double[])scratch.Clone();
        Array.Sort(sorted);
        Assert.Equal(sorted[333], SelectKernels.NthSmallest(scratch, 333));
    }

    [Fact]
    public void InfinitiesAndSignedZerosSortWhereASortPutsThem()
    {
        double[] source =
        [
            double.PositiveInfinity, -0.0, 0.0, double.NegativeInfinity, 1, -1,
            double.PositiveInfinity, double.Epsilon, -double.Epsilon,
        ];
        double[] sorted = (double[])source.Clone();
        Array.Sort(sorted);

        for (int rank = 0; rank < source.Length; rank++)
        {
            double[] scratch = (double[])source.Clone();
            Span<int> one = [rank];
            SelectKernels.PartialSort(scratch, one);

            // Signed zeros compare equal, so which of the two lands at a given rank is not defined
            // by either routine — their values are what has to agree, and 0.0 == -0.0.
            Assert.Equal(sorted[rank], scratch[rank]);
        }
    }
}
