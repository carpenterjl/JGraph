using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M54 wave E: what the decorations look like once drawn — the subtitle's own band above the plot
/// area, a constant line that spans the axes without stretching it, and a contour label written into
/// a gap in its own curve rather than over the top of it.
/// </summary>
public class DecorationRenderTests
{
    private static FigureModel Plotted()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 1, 2, 3 }, new double[] { 1, 2, 3 });
        return figure;
    }

    [Fact]
    public void ASubtitleSitsBetweenTheTitleAndThePlotArea()
    {
        FigureModel figure = Plotted();
        AxesModel axes = figure.Axes[0];
        axes.Title = "over";
        axes.Subtitle = "under";

        var context = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        double title = context.TextPositions[context.Texts.IndexOf("over")].Y;
        double subtitle = context.TextPositions[context.Texts.IndexOf("under")].Y;

        Assert.True(title < subtitle, "the title is above the subtitle");
        Assert.True(subtitle < result.Axes[0].PlotArea.Top, "and the subtitle is above the plot area");
    }

    [Fact]
    public void ASubtitleTakesRoomFromThePlotArea()
    {
        FigureModel plain = Plotted();
        var before = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult without = new FigureRenderer().Render(plain, before, Theme.Light);

        plain.Axes[0].Subtitle = "under";
        var after = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult with = new FigureRenderer().Render(plain, after, Theme.Light);

        Assert.True(with.Axes[0].PlotArea.Top > without.Axes[0].PlotArea.Top);
    }

    [Fact]
    public void AConstantLineSpansThePlotAreaAndDoesNotStretchIt()
    {
        FigureModel figure = Plotted();
        AxesModel axes = figure.Axes[0];

        // A threshold a thousand times the data must not flatten the series.
        axes.AddYLine(1000);
        figure.RecomputeDataBounds();

        Assert.True(axes.PrimaryYAxis.Range.Max < 10);

        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(figure, context, Theme.Light);

        // The line is off the top of the view, so nothing of it is drawn inside the plot rectangle —
        // what matters here is that asking for it did not move the axes.
        Assert.True(axes.PrimaryYAxis.Range.Max < 10);
    }

    [Fact]
    public void AConstantLineReachesBothEdgesOfThePlotArea()
    {
        FigureModel figure = Plotted();
        figure.Axes[0].Grid.ShowMajor = false;
        figure.Axes[0].AddXLine(2);

        var context = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);
        Rect2D area = result.Axes[0].PlotArea;

        // The frame is drawn edge by edge since M73, so its verticals also run the full height;
        // the constant line is the one dashed vertical among them.
        (Point2D From, Point2D To, LineStyle Style) span = Assert.Single(
            context.Lines,
            l => System.Math.Abs(l.From.X - l.To.X) < 0.001
                && System.Math.Abs(l.From.Y - area.Top) < 0.001
                && l.Style.Dash == DashStyle.Dash);

        Assert.Equal(area.Bottom, span.To.Y, 3);
    }

    [Fact]
    public void AConstantLineDrawsItsLabel()
    {
        FigureModel figure = Plotted();
        figure.Axes[0].AddYLine(2).Label = "threshold";

        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Contains("threshold", context.Texts);
    }

    [Fact]
    public void ALabelledContourWritesItsLevelsIntoGapsInTheCurves()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        // A cone: every level is one long closed ring, which is the case a label has room in.
        var z = new double[41, 41];
        for (int r = 0; r < 41; r++)
        {
            for (int c = 0; c < 41; c++)
            {
                z[r, c] = System.Math.Sqrt(((r - 20) * (r - 20)) + ((c - 20) * (c - 20)));
            }
        }

        double[] grid = Enumerable.Range(0, 41).Select(i => (double)i).ToArray();
        var contour = new ContourPlot(grid, grid, z) { LevelCount = 4 };
        axes.Plots.Add(contour);

        var unlabelled = new RecordingRenderContext(new Size2D(500, 400));
        new FigureRenderer().Render(figure, unlabelled, Theme.Light);
        int subpathsBefore = unlabelled.TotalSubpaths;

        contour.ShowText = true;
        var labelled = new RecordingRenderContext(new Size2D(500, 400));
        new FigureRenderer().Render(figure, labelled, Theme.Light);

        // One text per level, and each labelled curve became two stubs around its gap.
        foreach (double level in contour.ResolvedLevels)
        {
            Assert.Contains(level.ToString("G4", System.Globalization.CultureInfo.InvariantCulture), labelled.Texts);
        }

        Assert.True(
            labelled.TotalSubpaths > subpathsBefore,
            $"labelling should split curves, but the sub-path count went {subpathsBefore} -> {labelled.TotalSubpaths}");
    }

    [Fact]
    public void OnlyTheNamedLevelsAreLabelled()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        var z = new double[41, 41];
        for (int r = 0; r < 41; r++)
        {
            for (int c = 0; c < 41; c++)
            {
                z[r, c] = System.Math.Sqrt(((r - 20) * (r - 20)) + ((c - 20) * (c - 20)));
            }
        }

        double[] grid = Enumerable.Range(0, 41).Select(i => (double)i).ToArray();
        var contour = new ContourPlot(grid, grid, z)
        {
            Levels = [5, 10, 15],
            ShowText = true,
            LabelLevels = [10],
        };
        axes.Plots.Add(contour);

        var context = new RecordingRenderContext(new Size2D(500, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Contains("10", context.Texts);
        Assert.DoesNotContain("5", context.Texts);
        Assert.DoesNotContain("15", context.Texts);
    }
}
