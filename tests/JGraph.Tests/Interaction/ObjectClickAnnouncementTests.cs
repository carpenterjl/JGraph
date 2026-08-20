using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Objects;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>
/// Every press is announced with what it landed on, before the active mode acts and whatever mode
/// that is (M71 Wave C) — MATLAB's ButtonDownFcn fires with the zoom tool selected too. The
/// resolution is <see cref="FigureHitTesting"/>, the same one the edit mode selects with.
/// </summary>
public class ObjectClickAnnouncementTests
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
        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 100, 100));
        return (new InteractionController(surface), plot, surface);
    }

    private static void Press(InteractionController c, double x, double y, PointerButton button = PointerButton.Left) =>
        c.PointerDown(new PointerEventArgs(new Point2D(x, y), button, ModifierKeys.None));

    [Fact]
    public void APressOnTheLine_NamesTheLine_WithItsDataPoint()
    {
        (InteractionController controller, LinePlot plot, FakeInteractionSurface surface) = Setup();

        Press(controller, 50, 50);

        (FigureHit hit, PointerButton button) = Assert.Single(surface.ObjectClicks);
        Assert.Same(plot, hit.Target);
        Assert.Equal(PointerButton.Left, button);
        Assert.NotNull(hit.DataPoint);
    }

    [Fact]
    public void APressOnEmptyAxes_NamesTheAxes_WithTheClickInDataSpace()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();

        Press(controller, 80, 90); // inside the plot area, far from the diagonal line

        (FigureHit hit, _) = Assert.Single(surface.ObjectClicks);
        Assert.Same(surface.DefaultAxes, hit.Target);
        Assert.NotNull(hit.DataPoint);
    }

    [Fact]
    public void APressOutsideEveryAxes_NamesNothing()
    {
        (InteractionController controller, _, FakeInteractionSurface surface) = Setup();

        Press(controller, 300, 300);

        (FigureHit hit, _) = Assert.Single(surface.ObjectClicks);
        Assert.Null(hit.Target);
        Assert.Null(hit.Axes);
        Assert.Null(hit.DataPoint);
    }

    [Fact]
    public void EveryModeAnnounces_NotJustEdit()
    {
        (InteractionController controller, LinePlot plot, FakeInteractionSurface surface) = Setup();
        controller.SetMode(InteractionModeKind.RectangleZoom);

        Press(controller, 50, 50);

        (FigureHit hit, _) = Assert.Single(surface.ObjectClicks);
        Assert.Same(plot, hit.Target);
    }

    [Fact]
    public void AnUnselectableLine_IsInvisibleToTheClick()
    {
        (InteractionController controller, LinePlot plot, FakeInteractionSurface surface) = Setup();
        plot.Selectable = false; // MATLAB's HitTest 'off'

        Press(controller, 50, 50);

        (FigureHit hit, _) = Assert.Single(surface.ObjectClicks);
        Assert.Same(surface.DefaultAxes, hit.Target);
    }
}
