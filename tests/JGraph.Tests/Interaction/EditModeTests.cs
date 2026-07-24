using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Objects;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

public class EditModeTests
{
    private static readonly Rect2D PlotArea = new(0, 0, 100, 100);

    /// <summary>Axes over [0,10]² mapped onto a 100×100 plot rect: 10 px = 1 data unit, Y flipped.</summary>
    private static (FigureModel Figure, AxesModel Axes, FakeInteractionSurface Surface, InteractionController Controller) CreateRig()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);

        var surface = new FakeInteractionSurface(axes, PlotArea)
        {
            FigureMapper = new NormalizedCoordinateMapper(PlotArea),
        };
        var controller = new InteractionController(surface);
        controller.SetMode(InteractionModeKind.Edit);
        return (figure, axes, surface, controller);
    }

    private static PointerEventArgs Pointer(double x, double y, PointerButton button = PointerButton.Left) =>
        new(new Point2D(x, y), button, ModifierKeys.None);

    private static TestAnnotation AddAnnotationAt(AxesModel axes, double dataX, double dataY)
    {
        // Data (5,5) sits at pixel (50,50); simulate its painted bounds around that.
        var annotation = new TestAnnotation(dataX, dataY);
        axes.Annotations.Add(annotation);
        annotation.SetBounds(new Rect2D((dataX * 10) - 10, 100 - (dataY * 10) - 10, 20, 20));
        return annotation;
    }

    [Fact]
    public void Click_OnAnnotation_SelectsIt()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);

        controller.PointerDown(Pointer(50, 50));

        Assert.Same(annotation, controller.Selection.Selected);
        Assert.True(annotation.IsSelected);
    }

    [Fact]
    public void Click_OnPlot_SelectsIt()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        LinePlot line = axes.AddLine(new double[] { 0, 5, 10 }, new double[] { 0, 5, 10 });

        controller.PointerDown(Pointer(50, 50)); // data (5,5) is on the line

        Assert.Same(line, controller.Selection.Selected);
    }

    [Fact]
    public void Click_OnEmptyPlotArea_SelectsTheAxes()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });

        controller.PointerDown(Pointer(90, 20)); // far from the line

        Assert.Same(axes, controller.Selection.Selected);
    }

    [Fact]
    public void Click_OutsideAxes_ClearsSelection()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);
        controller.PointerDown(Pointer(50, 50));
        controller.PointerUp(Pointer(50, 50));
        Assert.Same(annotation, controller.Selection.Selected);

        controller.PointerDown(Pointer(150, 150));

        Assert.Null(controller.Selection.Selected);
    }

    [Fact]
    public void Drag_MovesAnnotation_AndPushesOneUndoableAction()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);

        controller.PointerDown(Pointer(50, 50));
        controller.PointerMove(Pointer(55, 45));
        controller.PointerMove(Pointer(60, 40)); // +10 px right, −10 px up → data (+1, +1)
        controller.PointerUp(Pointer(60, 40));

        Assert.Equal(6, annotation.Position.X, precision: 9);
        Assert.Equal(6, annotation.Position.Y, precision: 9);
        Assert.True(surface.UndoStack.CanUndo);

        surface.UndoStack.Undo();
        Assert.Equal(5, annotation.Position.X, precision: 9);
        Assert.Equal(5, annotation.Position.Y, precision: 9);
        Assert.False(surface.UndoStack.CanUndo); // the whole gesture was one action
    }

    [Fact]
    public void Drag_FigureSpaceAnnotation_MovesInNormalizedCoordinates()
    {
        (FigureModel figure, _, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        var note = new TestAnnotation(0.5, 0.5) { Space = AnnotationSpace.Figure };
        figure.Annotations.Add(note);
        note.SetBounds(new Rect2D(45, 45, 10, 10));

        controller.PointerDown(Pointer(50, 50));
        Assert.Same(note, controller.Selection.Selected);

        controller.PointerMove(Pointer(60, 60)); // +10 px each way = +0.1 normalized
        controller.PointerUp(Pointer(60, 60));

        Assert.Equal(0.6, note.Position.X, precision: 9);
        Assert.Equal(0.6, note.Position.Y, precision: 9);
        Assert.True(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void Escape_DuringDrag_CancelsMoveButKeepsSelection()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);

        controller.PointerDown(Pointer(50, 50));
        controller.PointerMove(Pointer(70, 30));
        controller.KeyDown(new KeyEventArgs(InteractionKey.Escape, ModifierKeys.None));

        Assert.Equal(5, annotation.Position.X, precision: 9);
        Assert.Equal(5, annotation.Position.Y, precision: 9);
        Assert.Same(annotation, controller.Selection.Selected);
        Assert.False(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void Escape_WhenIdle_ClearsSelection()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);
        controller.PointerDown(Pointer(50, 50));
        controller.PointerUp(Pointer(50, 50));

        controller.KeyDown(new KeyEventArgs(InteractionKey.Escape, ModifierKeys.None));

        Assert.Null(controller.Selection.Selected);
        Assert.False(annotation.IsSelected);
    }

    [Fact]
    public void Delete_RemovesSelectedAnnotation_Undoably()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);
        controller.PointerDown(Pointer(50, 50));
        controller.PointerUp(Pointer(50, 50));

        controller.KeyDown(new KeyEventArgs(InteractionKey.Delete, ModifierKeys.None));

        Assert.Empty(axes.Annotations);
        Assert.Null(controller.Selection.Selected);

        surface.UndoStack.Undo();
        Assert.Same(annotation, Assert.Single(axes.Annotations));
    }

    [Fact]
    public void Delete_WithPlotSelected_DoesNothing()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LinePlot line = axes.AddLine(new double[] { 0, 5, 10 }, new double[] { 0, 5, 10 });
        controller.PointerDown(Pointer(50, 50));
        Assert.Same(line, controller.Selection.Selected);

        controller.KeyDown(new KeyEventArgs(InteractionKey.Delete, ModifierKeys.None));

        Assert.Single(axes.Plots);
        Assert.False(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void HiddenAnnotations_AreNotHit()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);
        annotation.Visible = false;

        controller.PointerDown(Pointer(50, 50));

        Assert.NotSame(annotation, controller.Selection.Selected);
    }

    // ---- Legend ----

    /// <summary>Marks the legend visible and publishes a 40×20 box at (20, 10) as its last paint.</summary>
    private static LegendModel ShowLegendAt(AxesModel axes, FakeInteractionSurface surface)
    {
        axes.Legend.Visible = true;
        surface.LegendBounds = new Rect2D(20, 10, 40, 20);
        return axes.Legend;
    }

    [Fact]
    public void Click_OnLegend_SelectsIt()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LegendModel legend = ShowLegendAt(axes, surface);

        controller.PointerDown(Pointer(30, 15));

        Assert.Same(legend, controller.Selection.Selected);
    }

    [Fact]
    public void Click_OnLegend_WinsOverAnAnnotationBeneathIt()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LegendModel legend = ShowLegendAt(axes, surface);
        TestAnnotation annotation = AddAnnotationAt(axes, 4, 9);   // painted bounds cover (30, 0)-(50, 20)

        controller.PointerDown(Pointer(35, 15));

        // The legend draws over the plot area, so it is picked first.
        Assert.Same(legend, controller.Selection.Selected);
        Assert.False(annotation.IsSelected);
    }

    [Fact]
    public void Drag_MovesTheLegendAndSwitchesToCustomPlacement()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LegendModel legend = ShowLegendAt(axes, surface);

        controller.PointerDown(Pointer(30, 15));
        controller.PointerMove(Pointer(50, 45));
        controller.PointerUp(Pointer(50, 45));

        Assert.Equal(LegendPosition.Custom, legend.Position);

        // The box started at (20, 10) in a 100×100 plot area and moved by (+20, +30).
        Assert.Equal(0.4, legend.Location.X, 6);
        Assert.Equal(0.4, legend.Location.Y, 6);
    }

    [Fact]
    public void Drag_IsUndoneInASingleStep()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LegendModel legend = ShowLegendAt(axes, surface);
        legend.Position = LegendPosition.TopLeft;

        controller.PointerDown(Pointer(30, 15));
        controller.PointerMove(Pointer(50, 45));
        controller.PointerUp(Pointer(50, 45));

        Assert.Equal("Move legend", surface.UndoStack.NextUndoDescription);

        surface.UndoStack.Undo();

        // One step restores both the placement mode and the location.
        Assert.False(surface.UndoStack.CanUndo);
        Assert.Equal(LegendPosition.TopLeft, legend.Position);
        Assert.Equal(0.2, legend.Location.X, 6);
        Assert.Equal(0.1, legend.Location.Y, 6);
    }

    [Fact]
    public void Drag_DoesNotAccumulateAcrossMoves()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LegendModel legend = ShowLegendAt(axes, surface);

        controller.PointerDown(Pointer(30, 15));
        controller.PointerMove(Pointer(40, 25));
        controller.PointerMove(Pointer(50, 45));
        controller.PointerUp(Pointer(50, 45));

        // Each move re-derives from the gesture start, so the intermediate move leaves no residue.
        Assert.Equal(0.4, legend.Location.X, 6);
        Assert.Equal(0.4, legend.Location.Y, 6);
    }

    [Fact]
    public void Escape_CancelsALegendDrag()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        LegendModel legend = ShowLegendAt(axes, surface);
        legend.Position = LegendPosition.BottomRight;

        controller.PointerDown(Pointer(30, 15));
        controller.PointerMove(Pointer(50, 45));
        controller.KeyDown(new KeyEventArgs(InteractionKey.Escape, ModifierKeys.None));

        Assert.Equal(LegendPosition.BottomRight, legend.Position);
        Assert.False(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void Click_OnHiddenLegend_FallsThroughToWhatIsBelow()
    {
        (_, AxesModel axes, FakeInteractionSurface surface, InteractionController controller) = CreateRig();
        ShowLegendAt(axes, surface);
        axes.Legend.Visible = false;

        controller.PointerDown(Pointer(30, 15));

        Assert.NotSame(axes.Legend, controller.Selection.Selected);
    }

    [Fact]
    public void SwitchingMode_AwayFromEdit_KeepsSelection()
    {
        (_, AxesModel axes, _, InteractionController controller) = CreateRig();
        TestAnnotation annotation = AddAnnotationAt(axes, 5, 5);
        controller.PointerDown(Pointer(50, 50));
        controller.PointerUp(Pointer(50, 50));

        controller.SetMode(InteractionModeKind.Pan);

        Assert.Same(annotation, controller.Selection.Selected);
    }
}
