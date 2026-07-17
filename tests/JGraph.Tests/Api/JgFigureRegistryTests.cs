using JGraph.Api;
using JGraph.Core.Model;
using Xunit;

namespace JGraph.Tests.Api;

/// <summary>M19: the numbered-figure registry behind MATLAB-style figure(n) handles.</summary>
[Collection("JG facade")]
public class JgFigureRegistryTests : IDisposable
{
    public JgFigureRegistryTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    [Fact]
    public void Figure_AssignsSequentialNumbers_AndFigureN_SelectsOrCreates()
    {
        FigureModel first = JG.Figure();
        Assert.Equal(1, JG.CurrentFigureNumber);

        FigureModel second = JG.Figure();
        Assert.Equal(2, JG.CurrentFigureNumber);
        Assert.NotSame(first, second);

        Assert.Same(first, JG.Figure(1));   // Re-select, not re-create.
        Assert.Equal(1, JG.CurrentFigureNumber);
        Assert.Same(first, JG.CurrentFigure);

        FigureModel fifth = JG.Figure(5);   // Creating a gap is fine.
        Assert.Equal(5, JG.CurrentFigureNumber);
        Assert.NotSame(fifth, JG.Figure()); // The next no-arg figure takes 3, the lowest free number.
        Assert.Equal(3, JG.CurrentFigureNumber);
    }

    [Fact]
    public void Figure_NoArg_FillsTheLowestFreeNumber()
    {
        JG.Figure(2);
        FigureModel one = JG.Figure();
        Assert.Equal(1, JG.CurrentFigureNumber);
        Assert.Same(one, JG.CurrentFigure);
    }

    [Fact]
    public void CurrentFigure_LazilyCreatesFigureOne()
    {
        FigureModel figure = JG.CurrentFigure;
        Assert.Equal(1, JG.CurrentFigureNumber);
        Assert.True(JG.TryGetFigure(1, out FigureModel registered));
        Assert.Same(figure, registered);
    }

    [Fact]
    public void ReSelectingAFigure_KeepsItsAxesCurrent()
    {
        JG.Figure(1);
        JG.Plot(new double[] { 1, 2 }, new double[] { 3, 4 });
        JG.Figure(2);
        JG.Plot(new double[] { 1 }, new double[] { 1 });

        // Interleaved plotting: back to figure 1, hold, add a second series there.
        JG.Figure(1);
        JG.Hold(true);
        JG.Plot(new double[] { 5, 6 }, new double[] { 7, 8 });

        Assert.True(JG.TryGetFigure(1, out FigureModel one));
        Assert.True(JG.TryGetFigure(2, out FigureModel two));
        Assert.Equal(2, one.Axes[0].Plots.Count);
        Assert.Single(two.Axes[0].Plots);
    }

    [Fact]
    public void RegisterFigure_AssignsNextNumber_AndIsIdempotent()
    {
        var external = new FigureModel();
        Assert.Equal(0, JG.GetFigureNumber(external));

        int number = JG.RegisterFigure(external);
        Assert.Equal(1, number);
        Assert.Equal(1, JG.GetFigureNumber(external));
        Assert.Same(external, JG.CurrentFigure);

        Assert.Equal(1, JG.RegisterFigure(external)); // Re-registering keeps the number.
    }

    [Fact]
    public void Reset_ClearsTheRegistry()
    {
        JG.Figure(3);
        JG.Reset();

        Assert.False(JG.TryGetFigure(3, out _));
        _ = JG.CurrentFigure;
        Assert.Equal(1, JG.CurrentFigureNumber);
    }

    [Fact]
    public void Figure_RejectsNonPositiveNumbers() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => JG.Figure(0));
}
