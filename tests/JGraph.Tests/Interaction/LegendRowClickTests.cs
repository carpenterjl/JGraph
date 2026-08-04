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
