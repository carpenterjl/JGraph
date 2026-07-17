using JGraph.Core.Model;
using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Model;

public class AxisLinkGroupTests
{
    private static AxesModel AxesWithXRange(double min, double max)
    {
        var axes = new AxesModel();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(min, max);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(min, max);
        return axes;
    }

    [Fact]
    public void Linking_UnifiesRangesToUnion()
    {
        AxesModel a = AxesWithXRange(0, 10);
        AxesModel b = AxesWithXRange(5, 20);

        using var _ = AxisLinkGroup.Link(AxisLinkMode.X, a, b);

        Assert.Equal(new DataRange(0, 20), a.PrimaryXAxis.Range);
        Assert.Equal(new DataRange(0, 20), b.PrimaryXAxis.Range);
    }

    [Fact]
    public void ChangingOneRangeMirrorsToOthers()
    {
        AxesModel a = AxesWithXRange(0, 10);
        AxesModel b = AxesWithXRange(0, 10);
        using var _ = AxisLinkGroup.Link(AxisLinkMode.X, a, b);

        a.PrimaryXAxis.Range = new DataRange(2, 4);

        Assert.Equal(new DataRange(2, 4), b.PrimaryXAxis.Range);
    }

    [Fact]
    public void XLinkLeavesYIndependent()
    {
        AxesModel a = AxesWithXRange(0, 10);
        AxesModel b = AxesWithXRange(0, 10);
        using var _ = AxisLinkGroup.Link(AxisLinkMode.X, a, b);

        a.PrimaryYAxis.Range = new DataRange(100, 200);

        Assert.NotEqual(new DataRange(100, 200), b.PrimaryYAxis.Range);
    }

    [Fact]
    public void BothMode_MirrorsXAndY()
    {
        AxesModel a = AxesWithXRange(0, 10);
        AxesModel b = AxesWithXRange(0, 10);
        using var _ = AxisLinkGroup.Link(AxisLinkMode.Both, a, b);

        a.PrimaryYAxis.Range = new DataRange(3, 7);

        Assert.Equal(new DataRange(3, 7), b.PrimaryYAxis.Range);
    }

    [Fact]
    public void Dispose_StopsMirroring()
    {
        AxesModel a = AxesWithXRange(0, 10);
        AxesModel b = AxesWithXRange(0, 10);
        AxisLinkGroup group = AxisLinkGroup.Link(AxisLinkMode.X, a, b);
        group.Dispose();

        a.PrimaryXAxis.Range = new DataRange(1, 2);

        Assert.NotEqual(new DataRange(1, 2), b.PrimaryXAxis.Range);
    }

    [Fact]
    public void Linking_DisablesAutoScaleOnMembers()
    {
        AxesModel a = AxesWithXRange(0, 10);
        a.PrimaryXAxis.AutoScale = true;
        AxesModel b = AxesWithXRange(0, 10);
        using var _ = AxisLinkGroup.Link(AxisLinkMode.X, a, b);

        Assert.False(a.PrimaryXAxis.AutoScale);
        Assert.False(b.PrimaryXAxis.AutoScale);
    }

    [Fact]
    public void NoInfiniteLoopUnderMutualUpdates()
    {
        AxesModel a = AxesWithXRange(0, 10);
        AxesModel b = AxesWithXRange(0, 10);
        using var _ = AxisLinkGroup.Link(AxisLinkMode.X, a, b);

        // A rapid series of updates must settle (the re-entrancy guard prevents recursion).
        for (int i = 0; i < 50; i++)
        {
            a.PrimaryXAxis.Range = new DataRange(i, i + 5);
        }

        Assert.Equal(a.PrimaryXAxis.Range, b.PrimaryXAxis.Range);
    }
}
