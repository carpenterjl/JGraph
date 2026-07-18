using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>
/// M21a: the default Pointer mode — click places a persistent data tip, drag pans, the cursor turns
/// into a crosshair near pickable points — and the Data Tips tool's replace-last behavior.
/// </summary>
public class PointerModeTests
{
    private static (InteractionController Controller, AxesModel Axes, FakeInteractionSurface Surface) Setup()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        axes.AddLine(new double[] { 0, 5, 10 }, new double[] { 0, 5, 10 });

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100));
        var controller = new InteractionController(surface);
        return (controller, axes, surface);
    }

    private static void Press(InteractionController c, double x, double y) =>
        c.PointerDown(new PointerEventArgs(new Point2D(x, y), PointerButton.Left, ModifierKeys.None));

    private static void Move(InteractionController c, double x, double y) =>
        c.PointerMove(new PointerEventArgs(new Point2D(x, y), PointerButton.Left, ModifierKeys.None));

    private static void Release(InteractionController c, double x, double y) =>
        c.PointerUp(new PointerEventArgs(new Point2D(x, y), PointerButton.Left, ModifierKeys.None));

    [Fact]
    public void PointerMode_IsTheDefault()
    {
        (InteractionController controller, _, _) = Setup();
        Assert.Equal(InteractionModeKind.Pointer, controller.CurrentMode.Kind);
    }

    [Fact]
    public void Click_OnAPoint_PlacesAPersistentTip_Undoably()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();

        Press(controller, 50, 50);   // data (5, 5)
        Release(controller, 51, 50); // 1 px of jitter is still a click

        var tip = Assert.IsType<DataTipAnnotation>(Assert.Single(axes.Annotations));
        Assert.Equal(5, tip.PinnedPoint.X, 6);
        Assert.Equal(5, tip.PinnedPoint.Y, 6);
        Assert.Same(tip, controller.Selection.Selected);

        surface.UndoStack.Undo();
        Assert.Empty(axes.Annotations);
        surface.UndoStack.Redo();
        Assert.Single(axes.Annotations);
    }

    [Fact]
    public void Click_AwayFromAnyPoint_PlacesNothing()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();

        Press(controller, 90, 20);   // far from the y=x diagonal
        Release(controller, 90, 20);

        Assert.Empty(axes.Annotations);
        Assert.False(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void Drag_BeyondTheThreshold_Pans_InsteadOfPlacingATip()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();

        Press(controller, 50, 50);
        Move(controller, 70, 50);
        Release(controller, 70, 50);

        Assert.Empty(axes.Annotations);            // no tip
        Assert.NotEqual(0, axes.PrimaryXAxis.Range.Min); // the view moved
        Assert.True(surface.UndoStack.CanUndo);    // committed as an undoable pan
    }

    [Fact]
    public void Hover_NearAPoint_ShowsTheCrosshair_AndClearsAway()
    {
        (InteractionController controller, _, _) = Setup();

        controller.PointerMove(new PointerEventArgs(new Point2D(50, 50), PointerButton.None, ModifierKeys.None));
        Assert.Equal(InteractionCursor.Cross, controller.Cursor);

        controller.PointerMove(new PointerEventArgs(new Point2D(90, 20), PointerButton.None, ModifierKeys.None));
        Assert.Equal(InteractionCursor.Arrow, controller.Cursor);
    }

    [Fact]
    public void DataTipsTool_ReplacesItsOwnLastTip_ButKeepsPointerTips()
    {
        (InteractionController controller, AxesModel axes, _) = Setup();

        // A pointer-placed tip persists.
        Press(controller, 50, 50);
        Release(controller, 50, 50);
        Assert.Single(axes.Annotations);

        // The Data Tips tool roves: two clicks leave only ITS latest tip plus the pointer's.
        controller.SetMode(InteractionModeKind.DataTips);
        Press(controller, 0, 100);   // data (0, 0)
        Press(controller, 100, 0);   // data (10, 10)

        Assert.Equal(2, axes.Annotations.Count);
        var roving = Assert.IsType<DataTipAnnotation>(axes.Annotations[^1]);
        Assert.Equal(10, roving.PinnedPoint.X, 6);
    }

    [Fact]
    public void DataTipsTool_Undo_RestoresThePreviousTip()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();
        controller.SetMode(InteractionModeKind.DataTips);

        Press(controller, 0, 100);
        Press(controller, 100, 0);
        Assert.Single(axes.Annotations);

        surface.UndoStack.Undo(); // back to the first tip
        var restored = Assert.IsType<DataTipAnnotation>(Assert.Single(axes.Annotations));
        Assert.Equal(0, restored.PinnedPoint.X, 6);

        surface.UndoStack.Undo(); // back to none
        Assert.Empty(axes.Annotations);
    }
}
