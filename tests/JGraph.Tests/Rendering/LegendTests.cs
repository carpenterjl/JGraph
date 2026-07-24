using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Engineering;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

public class LegendTests
{
    /// <summary>A figure with three named series and a visible legend, already synced by one render.</summary>
    private static (FigureModel Figure, AxesModel Axes, RecordingRenderContext Context) ThreeSeriesFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Legend.Visible = true;

        foreach (string name in new[] { "Alpha", "Beta", "Gamma" })
        {
            LinePlot line = axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
            line.DisplayName = name;
        }

        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(figure, context, Theme.Light);
        return (figure, axes, context);
    }

    private static RecordingRenderContext Paint(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(figure, context, Theme.Light);
        return context;
    }

    private static List<string> Render(FigureModel figure) => Paint(figure).Texts;

    /// <summary>The same reconciliation the renderer runs before each paint.</summary>
    private static bool Sync(AxesModel axes) =>
        axes.Legend.SyncEntries(axes.Plots.Where(p => p is ILegendItem));

    [Fact]
    public void Render_CreatesOneEntryPerLegendableSeries()
    {
        (FigureModel _, AxesModel axes, RecordingRenderContext context) = ThreeSeriesFigure();

        Assert.Equal(3, axes.Legend.Entries.Count);
        Assert.Equal(
            new[] { "Alpha", "Beta", "Gamma" },
            axes.Legend.Entries.Select(e => e.Plot?.DisplayName));
        Assert.Contains("Alpha", context.Texts);
    }

    [Fact]
    public void SyncEntries_IsIdempotentAndReportsNoChange()
    {
        (FigureModel _, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();

        // A steady-state repaint must not touch the collection, or every frame would rebuild the tree.
        Assert.False(Sync(axes));
        Assert.Equal(3, axes.Legend.Entries.Count);
    }

    [Fact]
    public void SyncEntries_DropsRowsForRemovedPlotsAndAppendsNewOnes()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();

        axes.Plots.RemoveAt(1);
        Render(figure);

        Assert.Equal(new[] { "Alpha", "Gamma" }, axes.Legend.Entries.Select(e => e.Plot?.DisplayName));

        LinePlot added = axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        added.DisplayName = "Delta";
        Render(figure);

        Assert.Equal(
            new[] { "Alpha", "Gamma", "Delta" },
            axes.Legend.Entries.Select(e => e.Plot?.DisplayName));
    }

    [Fact]
    public void SyncEntries_IgnoresPlotsThatCannotAppearInALegend()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Legend.Visible = true;
        axes.Plots.Add(new SmithGrid());
        LinePlot line = axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        line.DisplayName = "S11";

        Render(figure);

        LegendEntryModel entry = Assert.Single(axes.Legend.Entries);
        Assert.Same(line, entry.Plot);
    }

    [Fact]
    public void Render_DrawsRowsInEntryOrderWithLabelOverrides()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();

        axes.Legend.Entries[2].Label = "Renamed";
        axes.Legend.Entries.Move(2, 0);

        List<string> texts = Render(figure);
        int renamed = texts.IndexOf("Renamed");
        int alpha = texts.IndexOf("Alpha");

        Assert.True(renamed >= 0 && alpha >= 0);
        Assert.True(renamed < alpha, "the moved row should be drawn first");

        // The override wins over the series' own name, which is left untouched.
        Assert.DoesNotContain("Gamma", texts);
        Assert.Equal("Gamma", axes.Plots[2].DisplayName);
    }

    [Fact]
    public void Render_ExcludedEntryIsSkippedButLeavesOtherSeriesColorsAlone()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();

        IReadOnlyList<Color> palette = Theme.Light.SeriesPalette;
        Assert.Contains(palette[2], Paint(figure).LineColors);

        axes.Legend.Entries[0].Visible = false;
        RecordingRenderContext after = Paint(figure);

        Assert.DoesNotContain("Alpha", after.Texts);
        Assert.Contains("Beta", after.Texts);

        // The palette index follows draw order, not row order, so hiding a row must not shift the
        // colors of the series below it.
        Assert.Contains(palette[2], after.LineColors);
    }

    [Fact]
    public void Render_HidingTheSeriesAlsoDropsItsRow()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();

        axes.Plots[1].Visible = false;

        List<string> texts = Render(figure);
        Assert.DoesNotContain("Beta", texts);
        Assert.Equal(3, axes.Legend.Entries.Count);
    }

    [Fact]
    public void Render_PublishesTheLegendBoxForInteraction()
    {
        (FigureModel figure, AxesModel _, RecordingRenderContext _) = ThreeSeriesFigure();
        var context = new RecordingRenderContext(new Size2D(400, 300));

        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.NotNull(result.Axes[0].LegendBounds);
        Rect2D bounds = result.Axes[0].LegendBounds!.Value;
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.True(result.Axes[0].PlotArea.Contains(new Point2D(bounds.Left + 1, bounds.Top + 1)));
    }

    [Fact]
    public void Render_HiddenLegendPublishesNoBounds()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();
        axes.Legend.Visible = false;

        var context = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Null(result.Axes[0].LegendBounds);
    }

    [Fact]
    public void Render_CustomPositionPlacesTheBoxAtTheStoredLocation()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();
        axes.Legend.Position = LegendPosition.Custom;
        axes.Legend.Location = new Point2D(0.25, 0.5);

        var context = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        AxesRenderInfo info = result.Axes[0];
        Rect2D box = info.LegendBounds!.Value;
        Assert.Equal(info.PlotArea.Left + (0.25 * info.PlotArea.Width), box.Left, 3);
        Assert.Equal(info.PlotArea.Top + (0.5 * info.PlotArea.Height), box.Top, 3);
    }

    [Fact]
    public void Render_CustomPositionKeepsTheBoxInsideThePlotArea()
    {
        (FigureModel figure, AxesModel axes, RecordingRenderContext _) = ThreeSeriesFigure();
        axes.Legend.Position = LegendPosition.Custom;
        axes.Legend.Location = new Point2D(5, 5);

        var context = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        AxesRenderInfo info = result.Axes[0];
        Rect2D box = info.LegendBounds!.Value;
        Assert.True(box.Right <= info.PlotArea.Right + 1e-9);
        Assert.True(box.Bottom <= info.PlotArea.Bottom + 1e-9);
    }

}
