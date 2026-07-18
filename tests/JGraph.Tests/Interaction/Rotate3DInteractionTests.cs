using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>M20b: drag-rotate, wheel dolly, undo, and mode guards on a 3D axes.</summary>
public class Rotate3DInteractionTests
{
    private static (InteractionController Controller, AxesModel Axes, FakeInteractionSurface Surface) Setup3D()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Is3D = true;
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        axes.ZAxis.AutoScale = false;
        axes.ZAxis.Range = new DataRange(0, 10);

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100));
        return (new InteractionController(surface), axes, surface);
    }

    private static void Drag(InteractionController controller, Point2D from, Point2D to)
    {
        controller.PointerDown(new PointerEventArgs(from, PointerButton.Left, ModifierKeys.None));
        controller.PointerMove(new PointerEventArgs(to, PointerButton.None, ModifierKeys.None));
        controller.PointerUp(new PointerEventArgs(to, PointerButton.Left, ModifierKeys.None));
    }

    [Fact]
    public void Drag_RotatesCamera_InsteadOfPanning()
    {
        (InteractionController controller, AxesModel axes, _) = Setup3D();

        Drag(controller, new Point2D(50, 50), new Point2D(70, 40));

        // az = start - dx*0.4; el = clamp(start + dy*0.4).
        Assert.Equal(-37.5 - 8, axes.Azimuth, 6);
        Assert.Equal(30 - 4, axes.Elevation, 6);

        // Ranges untouched (no pan happened).
        Assert.Equal(0, axes.PrimaryXAxis.Range.Min);
        Assert.Equal(10, axes.PrimaryXAxis.Range.Max);
    }

    [Fact]
    public void Elevation_ClampsAtPlusMinus90()
    {
        (InteractionController controller, AxesModel axes, _) = Setup3D();

        Drag(controller, new Point2D(50, 50), new Point2D(50, 5000));
        Assert.Equal(90, axes.Elevation);

        Drag(controller, new Point2D(50, 50), new Point2D(50, -5000));
        Assert.Equal(-90, axes.Elevation);
    }

    [Fact]
    public void Rotate_IsUndoable()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup3D();

        Drag(controller, new Point2D(50, 50), new Point2D(80, 60));
        Assert.NotEqual(-37.5, axes.Azimuth);
        Assert.True(surface.UndoStack.CanUndo);

        surface.UndoStack.Undo();
        Assert.Equal(-37.5, axes.Azimuth, 6);
        Assert.Equal(30, axes.Elevation, 6);

        surface.UndoStack.Redo();
        Assert.Equal(-37.5 - 12, axes.Azimuth, 6);
    }

    [Fact]
    public void Escape_CancelsRotation_RestoringCamera()
    {
        (InteractionController controller, AxesModel axes, _) = Setup3D();

        controller.PointerDown(new PointerEventArgs(new Point2D(50, 50), PointerButton.Left, ModifierKeys.None));
        controller.PointerMove(new PointerEventArgs(new Point2D(90, 90), PointerButton.None, ModifierKeys.None));
        Assert.NotEqual(-37.5, axes.Azimuth);

        controller.KeyDown(new KeyEventArgs(InteractionKey.Escape, ModifierKeys.None));

        Assert.Equal(-37.5, axes.Azimuth, 6);
        Assert.Equal(30, axes.Elevation, 6);
    }

    [Fact]
    public void Wheel_DolliesAllThreeRanges_AndIsUndoable()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup3D();

        controller.Wheel(new WheelEventArgs(new Point2D(50, 50), 120, ModifierKeys.None));

        Assert.True(axes.PrimaryXAxis.Range.Length < 10);
        Assert.True(axes.PrimaryYAxis.Range.Length < 10);
        Assert.True(axes.ZAxis.Range.Length < 10);

        // Symmetric about the centers.
        Assert.Equal(5, (axes.ZAxis.Range.Min + axes.ZAxis.Range.Max) / 2, 6);

        surface.UndoStack.Undo();
        Assert.Equal(0, axes.ZAxis.Range.Min, 6);
        Assert.Equal(10, axes.ZAxis.Range.Max, 6);
    }

    [Fact]
    public void RectangleZoom_IsInert_OnA3DAxes()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup3D();
        controller.SetMode(InteractionModeKind.RectangleZoom);

        Drag(controller, new Point2D(20, 20), new Point2D(60, 80));

        Assert.Equal(0, axes.PrimaryXAxis.Range.Min);
        Assert.Equal(10, axes.PrimaryXAxis.Range.Max);
        Assert.False(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void DataCursor_ReportsNothing_OnA3DAxes()
    {
        (InteractionController controller, _, _) = Setup3D();
        controller.SetMode(InteractionModeKind.DataCursor);

        controller.PointerDown(new PointerEventArgs(new Point2D(50, 50), PointerButton.Left, ModifierKeys.None));

        Assert.Null(controller.DataCursor);
    }

    [Fact]
    public void PanMode_StillPans_2DAxes()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100));
        var controller = new InteractionController(surface);

        Drag(controller, new Point2D(50, 50), new Point2D(60, 50));

        Assert.Equal(-1, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(-37.5, axes.Azimuth); // camera untouched
    }
}
