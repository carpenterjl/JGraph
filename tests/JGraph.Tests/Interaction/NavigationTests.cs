using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Maths.Transforms;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Interaction;

public class NavigationTests
{
    private static AxesModel MakeAxes()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        return axes;
    }

    [Fact]
    public void ZoomAxis_KeepsFocusFixed()
    {
        AxesModel axes = MakeAxes();
        Navigation.ZoomAxis(axes.PrimaryXAxis, focusData: 5, factor: 0.5);

        DataRange r = axes.PrimaryXAxis.Range;
        Assert.Equal(2.5, r.Min, 6);
        Assert.Equal(7.5, r.Max, 6);
        Assert.False(axes.PrimaryXAxis.AutoScale);
    }

    [Fact]
    public void ZoomAboutPixel_ZoomsBothAxes()
    {
        AxesModel axes = MakeAxes();
        var mapper = new AxisTransform(
            new Rect2D(0, 0, 100, 100),
            LinearScaleTransform.Instance, new DataRange(0, 10), false,
            LinearScaleTransform.Instance, new DataRange(0, 10), false);

        Navigation.ZoomAboutPixel(axes, mapper, new Point2D(50, 50), 0.5);

        Assert.Equal(2.5, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(7.5, axes.PrimaryXAxis.Range.Max, 6);
        Assert.Equal(2.5, axes.PrimaryYAxis.Range.Min, 6);
        Assert.Equal(7.5, axes.PrimaryYAxis.Range.Max, 6);
    }

    [Fact]
    public void ZoomToRect_SetsRangesFromRectangle()
    {
        AxesModel axes = MakeAxes();
        var mapper = new AxisTransform(
            new Rect2D(0, 0, 100, 100),
            LinearScaleTransform.Instance, new DataRange(0, 10), false,
            LinearScaleTransform.Instance, new DataRange(0, 10), false);

        // Rectangle from pixel (20,20) to (60,80).
        Navigation.ZoomToRect(axes, mapper, new Rect2D(20, 20, 40, 60));

        Assert.Equal(2, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(6, axes.PrimaryXAxis.Range.Max, 6);
        Assert.Equal(2, axes.PrimaryYAxis.Range.Min, 6);
        Assert.Equal(8, axes.PrimaryYAxis.Range.Max, 6);
    }

    [Fact]
    public void Pan_ShiftsViewOppositeToDrag()
    {
        AxesModel axes = MakeAxes();
        var mapper = new AxisTransform(
            new Rect2D(0, 0, 100, 100),
            LinearScaleTransform.Instance, new DataRange(0, 10), false,
            LinearScaleTransform.Instance, new DataRange(0, 10), false);

        // Drag 10 px to the right (one data unit) => range shifts left by one unit.
        Navigation.Pan(axes, mapper, new DataRange(0, 10), new DataRange(0, 10),
            new Point2D(50, 50), new Point2D(60, 50));

        Assert.Equal(-1, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(9, axes.PrimaryXAxis.Range.Max, 6);
    }

    [Fact]
    public void ResetView_ReenablesAutoScaleAndFits()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AutoScalePadding = 0;
        axes.AddLine(new double[] { 0, 1, 2 }, new double[] { 0, 5, 10 });
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(-100, 100);

        Navigation.ResetView(axes);

        Assert.True(axes.PrimaryYAxis.AutoScale);
        Assert.Equal(0, axes.PrimaryYAxis.Range.Min, 6);
        Assert.Equal(10, axes.PrimaryYAxis.Range.Max, 6);
    }

    [Fact]
    public void ZoomAxis_LogScale_ZoomsInLogSpace()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        AxisModel axis = axes.PrimaryXAxis;
        axis.Scale = AxisScaleType.Logarithmic;
        axis.AutoScale = false;
        axis.Range = new DataRange(1, 100); // two decades

        // Focus at 10 (the log-space midpoint), zoom in by half.
        Navigation.ZoomAxis(axis, focusData: 10, factor: 0.5);

        // Log-space: [0,2] around focus 1 -> [0.5, 1.5] -> data [10^0.5, 10^1.5].
        Assert.Equal(System.Math.Pow(10, 0.5), axis.Range.Min, 4);
        Assert.Equal(System.Math.Pow(10, 1.5), axis.Range.Max, 4);
    }
}
