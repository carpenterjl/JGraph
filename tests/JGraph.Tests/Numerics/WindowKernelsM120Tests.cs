using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The moving-window walk after M120 let a second thread pick it up part way along.
/// </summary>
/// <remarks>
/// There is one claim here and everything tests it: the threaded answer is the one-threaded answer
/// <em>to the bit</em>. That is a stronger thing to ask than "close enough", and it has to be asked
/// that way, because the window is carried as two folds and a block that resumed in the wrong place
/// would split them differently — the same additions in a different order, agreeing to fourteen
/// figures and disagreeing in the last. A tolerance would pass that. Equality does not.
/// </remarks>
public class WindowKernelsM120Tests
{
    /// <summary>Long enough to cross the threading threshold, with a tail that is not a round number.</summary>
    private const int Long = 300_007;

    private static double[] Series(int n, int seed)
    {
        var data = new double[n];
        var random = new Random(seed);
        for (int i = 0; i < n; i++)
        {
            // Magnitudes several orders apart are what make an addition care about its order: a
            // sum of like-sized numbers would agree whatever the grouping and prove nothing.
            data[i] = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-6, 7));
        }

        return data;
    }

    /// <summary>
    /// The thread count is one process-wide setting, so the two runs a comparison needs are taken
    /// under a lock rather than left to collide with another class doing the same thing.
    /// </summary>
    private static readonly object ThreadCount = new();

    private static (double[] One, double[] Many) BothWays(Func<double[]> run)
    {
        lock (ThreadCount)
        {
            int held = ParallelKernels.MaxDegree;
            try
            {
                ParallelKernels.MaxDegree = 1;
                double[] one = run();
                ParallelKernels.MaxDegree = 16;
                return (one, run());
            }
            finally
            {
                ParallelKernels.MaxDegree = held;
            }
        }
    }

    public static TheoryData<WindowStat> EveryCarriedStatistic()
    {
        var data = new TheoryData<WindowStat>();
        foreach (WindowStat stat in Enum.GetValues<WindowStat>())
        {
            if (WindowKernels.Handles(stat))
            {
                data.Add(stat);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCarriedStatistic))]
    public void ThreadedAnswersTheSameBitsAsOneThread(WindowStat stat)
    {
        double[] values = Series(Long, 17 + (int)stat);
        foreach (WindowEnds ends in Enum.GetValues<WindowEnds>())
        {
            // Widths that do and do not divide the block stride, centred and lopsided both ways.
            foreach ((int behind, int ahead) in new[]
                     {
                         (0, 0), (1, 0), (0, 1), (25, 25), (50, 0), (0, 50), (7, 3), (63, 64), (511, 512),
                     })
            {
                (int back, int forward) = (behind, ahead);
                (double[] one, double[] many) = BothWays(() => WindowKernels.Slide(
                    stat, values, back, forward, ends, 0.0, omitNan: false, identity: double.NaN));

                Assert.Equal(one.Length, many.Length);
                for (int i = 0; i < one.Length; i++)
                {
                    Assert.True(
                        one[i].Equals(many[i]),
                        $"{stat} {ends} behind {behind} ahead {ahead}: output {i} is " +
                        $"{many[i]:R} threaded and {one[i]:R} on one thread");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryCarriedStatistic))]
    public void HolesInTheDataDoNotMoveTheResumptionPoints(WindowStat stat)
    {
        // A skipped value still takes a place in the queue, so a NaN must not shift where the
        // turnovers fall — if it did, a block would resume in the wrong place only for data that
        // happened to have gaps in it, which is the worst kind of wrong.
        double[] values = Series(Long, 91);
        for (int i = 3; i < values.Length; i += 977)
        {
            values[i] = double.NaN;
        }

        foreach (bool omitNan in new[] { false, true })
        {
            bool skip = omitNan;
            (double[] one, double[] many) = BothWays(() => WindowKernels.Slide(
                stat, values, 30, 30, WindowEnds.Shrink, 0.0, skip, identity: double.NaN));

            for (int i = 0; i < one.Length; i++)
            {
                Assert.True(
                    one[i].Equals(many[i]) || (double.IsNaN(one[i]) && double.IsNaN(many[i])),
                    $"{stat} omitnan {omitNan}: output {i} is {many[i]:R} threaded and {one[i]:R} on one");
            }
        }
    }

    [Fact]
    public void ASumThatDependsOnItsOrderIsWhatThisIsTesting()
    {
        // Without this the test above could be passing because every grouping of these particular
        // numbers happens to give the same answer, which would make it prove nothing at all. Adding
        // the same values in the opposite order has to change the result, or the data is too tame.
        double[] values = Series(Long, 17);
        double forwards = 0;
        double backwards = 0;
        for (int i = 0; i < values.Length; i++)
        {
            forwards += values[i];
            backwards += values[values.Length - 1 - i];
        }

        Assert.False(forwards.Equals(backwards), "the sample is too well conditioned to test grouping");
    }

    [Fact]
    public void AShortSeriesIsNotSplitAtAll()
    {
        // Below the threshold there is one block and the walk is the one it always was. Worth
        // pinning: the resumption arithmetic must not produce a point inside a run too short to
        // have a whole window in the middle of it.
        foreach (int n in new[] { 1, 2, 3, 100, 5000 })
        {
            double[] values = Series(n, n);
            foreach (int behind in new[] { 0, 1, 7, 60 })
            {
                int width = behind;
                (double[] one, double[] many) = BothWays(() => WindowKernels.Slide(
                    WindowStat.Mean, values, width, width, WindowEnds.Shrink, 0.0, false, double.NaN));
                Assert.Equal(one, many);
            }
        }
    }

    [Fact]
    public void AWindowWiderThanABlockIsLeftOnOneThread()
    {
        // A block has to hold whole turnovers, so a window wider than half the data has nowhere to
        // resume: the answer must still be right, on one thread, rather than resumed in the middle
        // of a turnover.
        double[] values = Series(Long, 5);
        (double[] one, double[] many) = BothWays(() => WindowKernels.Slide(
            WindowStat.StandardDeviation, values, 90_000, 90_000, WindowEnds.Shrink, 0.0, false, double.NaN));
        Assert.Equal(one, many);
    }
}
