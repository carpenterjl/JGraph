using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

public class InteractionControllerTests
{
    private static (InteractionController Controller, AxesModel Axes, FakeInteractionSurface Surface) Setup()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100));
        return (new InteractionController(surface), axes, surface);
    }

    [Fact]
    public void Wheel_ZoomsInAndRecordsUndo()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();

        controller.Wheel(new WheelEventArgs(new Point2D(50, 50), 120, ModifierKeys.None));

        Assert.True(axes.PrimaryXAxis.Range.Length < 10);
        Assert.True(surface.UndoStack.CanUndo);

        surface.UndoStack.Undo();
        Assert.Equal(0, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(10, axes.PrimaryXAxis.Range.Max, 6);
    }

    [Fact]
    public void PanGesture_PansAndRecordsSingleUndo()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();
        controller.SetMode(InteractionModeKind.Pan);

        controller.PointerDown(new PointerEventArgs(new Point2D(50, 50), PointerButton.Left, ModifierKeys.None));
        controller.PointerMove(new PointerEventArgs(new Point2D(60, 50), PointerButton.None, ModifierKeys.None));
        controller.PointerUp(new PointerEventArgs(new Point2D(60, 50), PointerButton.Left, ModifierKeys.None));

        Assert.Equal(-1, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(9, axes.PrimaryXAxis.Range.Max, 6);

        int undos = 0;
        while (surface.UndoStack.CanUndo)
        {
            surface.UndoStack.Undo();
            undos++;
        }

        Assert.Equal(1, undos);
    }

    [Fact]
    public void RectangleZoom_ZoomsToDraggedRegion()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();
        controller.SetMode(InteractionModeKind.RectangleZoom);

        controller.PointerDown(new PointerEventArgs(new Point2D(20, 20), PointerButton.Left, ModifierKeys.None));
        controller.PointerMove(new PointerEventArgs(new Point2D(60, 80), PointerButton.None, ModifierKeys.None));
        Assert.NotNull(controller.RubberBand);

        controller.PointerUp(new PointerEventArgs(new Point2D(60, 80), PointerButton.Left, ModifierKeys.None));

        Assert.Null(controller.RubberBand);
        Assert.Equal(2, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(6, axes.PrimaryXAxis.Range.Max, 6);
        Assert.True(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void Escape_CancelsRubberBandWithoutZoom()
    {
        (InteractionController controller, AxesModel axes, _) = Setup();
        controller.SetMode(InteractionModeKind.RectangleZoom);

        controller.PointerDown(new PointerEventArgs(new Point2D(20, 20), PointerButton.Left, ModifierKeys.None));
        controller.PointerMove(new PointerEventArgs(new Point2D(60, 80), PointerButton.None, ModifierKeys.None));
        controller.KeyDown(new KeyEventArgs(InteractionKey.Escape, ModifierKeys.None));

        Assert.Null(controller.RubberBand);
        // View unchanged.
        Assert.Equal(0, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(10, axes.PrimaryXAxis.Range.Max, 6);
    }

    [Fact]
    public void DataTips_ClickPlacesATip_PinnedToTheNearestPoint()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        JGraph.Objects.AxesExtensions.AddLine(axes, new double[] { 0, 5, 10 }, new double[] { 0, 5, 10 });

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100));
        var controller = new InteractionController(surface);
        controller.SetMode(InteractionModeKind.DataTips);

        // Pixel (50,50) maps to data (5,5), which is a data point.
        controller.PointerDown(new PointerEventArgs(new Point2D(50, 50), PointerButton.Left, ModifierKeys.None));

        var tip = Assert.IsType<JGraph.Objects.Annotations.DataTipAnnotation>(Assert.Single(axes.Annotations));
        Assert.Equal(5, tip.PinnedPoint.X, 6);
        Assert.Equal(5, tip.PinnedPoint.Y, 6);
        Assert.True(surface.UndoStack.CanUndo);
    }
}
