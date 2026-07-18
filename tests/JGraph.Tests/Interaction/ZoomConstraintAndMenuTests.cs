using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Interaction.Modes;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>M21a: constrained rectangle zoom and the tool-aware plot context menu.</summary>
public class ZoomConstraintAndMenuTests
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

    private static void Drag(InteractionController c, Point2D from, Point2D to)
    {
        c.PointerDown(new PointerEventArgs(from, PointerButton.Left, ModifierKeys.None));
        c.PointerMove(new PointerEventArgs(to, PointerButton.Left, ModifierKeys.None));
        c.PointerUp(new PointerEventArgs(to, PointerButton.Left, ModifierKeys.None));
    }

    [Fact]
    public void HorizontalZoom_LeavesY_ExactlyUntouched()
    {
        (InteractionController controller, AxesModel axes, _) = Setup();
        controller.SetMode(InteractionModeKind.RectangleZoom);
        controller.RectangleZoom.Constraint = RectangleZoomConstraint.Horizontal;

        Drag(controller, new Point2D(20, 90), new Point2D(60, 91)); // barely any vertical drag

        Assert.Equal(2, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(6, axes.PrimaryXAxis.Range.Max, 6);
        Assert.Equal(new DataRange(0, 10), axes.PrimaryYAxis.Range); // exactly as before
    }

    [Fact]
    public void VerticalZoom_LeavesX_ExactlyUntouched()
    {
        (InteractionController controller, AxesModel axes, _) = Setup();
        controller.SetMode(InteractionModeKind.RectangleZoom);
        controller.RectangleZoom.Constraint = RectangleZoomConstraint.Vertical;

        Drag(controller, new Point2D(50, 20), new Point2D(50, 60)); // no horizontal drag at all

        Assert.Equal(new DataRange(0, 10), axes.PrimaryXAxis.Range);
        Assert.Equal(4, axes.PrimaryYAxis.Range.Min, 6); // pixel y=60 -> data 4
        Assert.Equal(8, axes.PrimaryYAxis.Range.Max, 6); // pixel y=20 -> data 8
    }

    [Fact]
    public void ZoomMenu_OffersTheThreeConstraints_AndSetsThem()
    {
        (InteractionController controller, _, _) = Setup();
        controller.SetMode(InteractionModeKind.RectangleZoom);

        IReadOnlyList<ContextMenuItem> items = controller.BuildContextMenu(new Point2D(50, 50));

        ContextMenuItem unconstrained = Assert.Single(items, i => i.Header == "Unconstrained Zoom");
        Assert.True(unconstrained.IsChecked);
        ContextMenuItem horizontal = Assert.Single(items, i => i.Header == "Horizontal Zoom");
        Assert.False(horizontal.IsChecked);
        Assert.Single(items, i => i.Header == "Vertical Zoom");
        Assert.Single(items, i => i.Header == "Restore View");

        horizontal.Invoke!();
        Assert.Equal(RectangleZoomConstraint.Horizontal, controller.RectangleZoom.Constraint);
    }

    [Fact]
    public void PointerMenu_OnATip_OffersDeletion_Undoably()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Setup();
        var tip = new DataTipAnnotation(new Point2D(5, 5), new Point2D(6, 6));
        axes.Annotations.Add(tip);

        // Without rendered bounds the tip's label cannot be hit, but "Delete All" still appears.
        IReadOnlyList<ContextMenuItem> items = controller.BuildContextMenu(new Point2D(50, 50));
        ContextMenuItem deleteAll = Assert.Single(items, i => i.Header == "Delete All Data Tips");

        deleteAll.Invoke!();
        Assert.Empty(axes.Annotations);
        Assert.True(surface.UndoStack.CanUndo);

        surface.UndoStack.Undo();
        Assert.Same(tip, Assert.Single(axes.Annotations));
    }

    [Fact]
    public void Menu_WithNoTips_InPointerMode_StillOffersRestoreView()
    {
        (InteractionController controller, _, _) = Setup();

        IReadOnlyList<ContextMenuItem> items = controller.BuildContextMenu(new Point2D(50, 50));

        Assert.Single(items, i => i.Header == "Restore View");
        Assert.DoesNotContain(items, i => i.Header.Contains("Data Tip"));
    }

    [Fact]
    public void RestoreView_FromTheMenu_ResetsTheAxesUnderThePointer()
    {
        (InteractionController controller, AxesModel axes, _) = Setup();
        axes.PrimaryXAxis.Range = new DataRange(3, 4); // zoomed in

        IReadOnlyList<ContextMenuItem> items = controller.BuildContextMenu(new Point2D(50, 50));
        Assert.Single(items, i => i.Header == "Restore View").Invoke!();

        Assert.True(axes.PrimaryXAxis.AutoScale); // back to auto-fit
    }
}
