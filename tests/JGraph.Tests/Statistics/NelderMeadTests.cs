using JGraph.Statistics.Optimize;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave C: the simplex search, exercised on its own before any distribution is fitted with it.
/// The plan named this the wave's declared risk, and a minimizer that quietly returns its starting
/// point looks exactly like a fitter with a bad closed form, so it is pinned here where the failure
/// can only be the optimizer.
/// </summary>
public class NelderMeadTests
{
    [Fact]
    public void FindsTheMinimumOfASmoothBowl()
    {
        NelderMead.Result result = NelderMead.Minimize(
            p => ((p[0] - 3) * (p[0] - 3)) + ((p[1] + 1) * (p[1] + 1)) + 7, [0, 0]);

        Assert.True(result.Converged);
        Assert.Equal(3, result.Solution[0], 6);
        Assert.Equal(-1, result.Solution[1], 6);
        Assert.Equal(7, result.Value, 8);
    }

    /// <summary>
    /// Rosenbrock's banana: the standard test that a search follows a curved valley rather than
    /// stalling across it. The minimum is (1, 1) with value zero.
    /// </summary>
    [Fact]
    public void FollowsACurvedValley()
    {
        NelderMead.Result result = NelderMead.Minimize(
            p => (100 * Math.Pow(p[1] - (p[0] * p[0]), 2)) + Math.Pow(1 - p[0], 2),
            [-1.2, 1],
            new NelderMead.Settings(MaxIterations: 2000, MaxEvaluations: 4000));

        Assert.True(result.Converged);
        Assert.Equal(1, result.Solution[0], 4);
        Assert.Equal(1, result.Solution[1], 4);
    }

    /// <summary>
    /// The fitters keep a shape parameter positive by answering infinity outside the domain rather
    /// than by constraining the search, so a barrier the simplex walks into has to push it back.
    /// </summary>
    [Fact]
    public void RetreatsFromAnInfiniteBarrier()
    {
        NelderMead.Result result = NelderMead.Minimize(
            p => p[0] <= 0 ? double.PositiveInfinity : ((p[0] - 2) * (p[0] - 2)), [5]);

        Assert.True(result.Converged);
        Assert.Equal(2, result.Solution[0], 6);
    }

    [Fact]
    public void StretchesAZeroStartingCoordinateRatherThanScalingIt()
    {
        // Scaling zero by 1.05 gives zero, so a simplex built that way would be degenerate and could
        // never move in this coordinate. The answer here is only reachable if it was displaced.
        NelderMead.Result result = NelderMead.Minimize(p => (p[0] - 4) * (p[0] - 4), [0]);

        Assert.True(result.Converged);
        Assert.Equal(4, result.Solution[0], 6);
    }

    [Fact]
    public void ReportsThatItRanOutOfBudgetRatherThanClaimingConvergence()
    {
        NelderMead.Result result = NelderMead.Minimize(
            p => (100 * Math.Pow(p[1] - (p[0] * p[0]), 2)) + Math.Pow(1 - p[0], 2),
            [-1.2, 1],
            new NelderMead.Settings(MaxIterations: 5, MaxEvaluations: 5));

        Assert.False(result.Converged);
    }
}
