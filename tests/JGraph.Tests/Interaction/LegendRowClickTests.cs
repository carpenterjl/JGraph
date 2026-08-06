using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Objects;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>
/// Clicking a legend row (M51). The legend could already be dragged; what was missing was a way to
/// say that a press and release in the same place is a click on the entry, which is what a script's
/// <c>ItemHitFcn</c> is waiting for.
/// </summary>
public class LegendRowClickTests
{
    private static (InteractionController Controller, LinePlot Plot, FakeInteractionSurface Surface) Setup()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);
        LinePlot plot = axes.AddLine(new double[] { 0, 5, 10 }, new double[] { 0, 5, 10 });
        axes.Legend.Visible = true;
        axes.Legend.Entries.Add(new LegendEntryModel { Plot = plot });

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100))
        {
            LegendBounds = new Rect2D(60, 5, 35, 20),
            LegendRow = (plot, new Rect2D(60, 5, 35, 20)),
        };

        return (new InteractionController(surface), plot, surface);
    }

    private static void Press(InteractionController c, double x, double y) =>
        c.PointerDown(new PointerEventArgs(new Point2D(x, y), PointerButton.Left, ModifierKeys.None));

    private static void Move(InteractionController c, double x, double y) =>
        c.PointerMove(new PointerEventArgs(new Point2D(x, y), PointerButton.None, ModifierKeys.None));

    private static void Release(InteractionController c, double x, double y) =>
        c.PointerUp(new PointerEventArgs(new Point2D(x, y), PointerButton.Left, ModifierKeys.None));

    [Fact]
    public void PressAndReleaseOnARow_IsAClickOnThatSeries()
    {
        (InteractionController controller, LinePlot plot, FakeInteractionSurface surface) = Setup();

        Press(controller, 70, 12);
        Release(controller, 70, 12);

        (AxesModel _, PlotObject clicked) = Assert.Single(surface.LegendRowClicks);
        Assert.Same(plot, clicked);
    }

    [Fact]
    public void ReleasingAwayFromTheRow_IsNotAClick()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();

        Press(controller, 70, 12);
        Release(controller, 20, 80);

        Assert.Empty(surface.LegendRowClicks);
    }

    [Fact]
    public void AClickOnTheLegendPlacesNoDataTip()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();
        AxesModel axes = surface.DefaultAxes!;

        Press(controller, 70, 12);
        Release(controller, 70, 12);

        Assert.Empty(axes.Annotations);
    }

    [Fact]
    public void JitterWithinTheThreshold_IsStillAClick()
    {
        (InteractionController controller, LinePlot plot, FakeInteractionSurface surface) = Setup();

        Press(controller, 70, 12);
        Move(controller, 71, 13); // a hand is never perfectly still
        Release(controller, 71, 13);

        (AxesModel _, PlotObject clicked) = Assert.Single(surface.LegendRowClicks);
        Assert.Same(plot, clicked);
        Assert.NotEqual(LegendPosition.Custom, surface.DefaultAxes!.Legend.Position);
    }

    [Fact]
    public void DraggingTheLegendWithTheDefaultPointer_MovesItInsteadOfClicking()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();
        LegendModel legend = surface.DefaultAxes!.Legend;

        Press(controller, 70, 12);
        Move(controller, 90, 40);
        Release(controller, 90, 40);

        // The box followed the pointer: it started drawn at (60, 5) in a 100×100 plot area and the
        // pointer travelled (+20, +28), so the stored fraction is the drawn origin plus that.
        Assert.Equal(LegendPosition.Custom, legend.Position);
        Assert.Equal(0.8, legend.Location.X, 6);
        Assert.Equal(0.33, legend.Location.Y, 6);
        Assert.Empty(surface.LegendRowClicks);
        Assert.True(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void DraggingTheLegend_UndoesInOneStep()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();
        LegendModel legend = surface.DefaultAxes!.Legend;
        LegendPosition before = legend.Position;

        Press(controller, 70, 12);
        Move(controller, 90, 40);
        Release(controller, 90, 40);
        surface.UndoStack.Undo();

        Assert.Equal(before, legend.Position);
        Assert.False(surface.UndoStack.CanUndo);
    }

    [Fact]
    public void TheLegendAnswersBelowThePlotAreaToo()
    {
        // A long legend hangs outside the plot area — the user's 38-serial legend reached well below
        // its subplot — and every part of it must respond, not just the part in front of the data.
        (InteractionController controller, LinePlot plot, FakeInteractionSurface surface) = Setup();
        surface.LegendBounds = new Rect2D(60, 80, 35, 60);          // bottom edge at y = 140
        surface.LegendRow = (plot, new Rect2D(60, 120, 35, 15));    // a row entirely below y = 100

        Press(controller, 70, 125);
        Release(controller, 70, 125);

        (AxesModel _, PlotObject clicked) = Assert.Single(surface.LegendRowClicks);
        Assert.Same(plot, clicked);
    }

    [Fact]
    public void AClickAwayFromTheLegendStillWorksTheOldWay()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();
        AxesModel axes = surface.DefaultAxes!;

        Press(controller, 50, 50); // the middle data point, well clear of the legend box
        Release(controller, 50, 50);

        Assert.Empty(surface.LegendRowClicks);
        Assert.Single(axes.Annotations);
    }
}
