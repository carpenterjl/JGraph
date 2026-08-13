using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>
/// M57 wave C: round-tripping the three-dimensional charts through the .graph format. The format
/// stays at v6 — these are new plot types within it, not a new shape of file.
/// </summary>
public class Chart3DSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    [Fact]
    public void Stem3DPlot_RoundTrips_ItsSamplesAndStyle()
    {
        var figure = new FigureModel();
        Stem3DPlot plot = figure.AddAxes().AddStem3D([0, 1], [2, 3], [4, 5]);
        plot.Color = Colors.Red;
        plot.LineWidth = 2.5;
        plot.DashStyle = DashStyle.Dot;
        plot.Baseline = -1;
        plot.Marker = MarkerType.Square;
        plot.MarkerSize = 9;
        plot.MarkerFill = Colors.Blue;

        var loaded = (Stem3DPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([0, 1], loaded.X);
        Assert.Equal([2, 3], loaded.Y);
        Assert.Equal([4, 5], loaded.Z);
        Assert.Equal(Colors.Red, loaded.Color);
        Assert.Equal(2.5, loaded.LineWidth);
        Assert.Equal(DashStyle.Dot, loaded.DashStyle);
        Assert.Equal(-1, loaded.Baseline);
        Assert.Equal(MarkerType.Square, loaded.Marker);
        Assert.Equal(Colors.Blue, loaded.MarkerFill);
    }

    [Fact]
    public void Bar3DPlot_RoundTrips_ItsMatrixAndLayout()
    {
        var figure = new FigureModel();
        Bar3DPlot plot = figure.AddAxes().AddBar3D(new double[,] { { 1, 2 }, { 3, 4 } }, [10, 20]);
        plot.Style = Bar3DStyle.Stacked;
        plot.Horizontal = true;
        plot.BarWidth = 0.5;
        plot.Baseline = -2;
        plot.FaceColor = Colors.Green;
        plot.FaceAlpha = 0.25;
        plot.Colormap = Colormap.Hot;

        var loaded = (Bar3DPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(1, loaded.ZData[0, 0]);
        Assert.Equal(4, loaded.ZData[1, 1]);
        Assert.Equal<double>([10, 20], loaded.RowPositions!);
        Assert.Equal(Bar3DStyle.Stacked, loaded.Style);
        Assert.True(loaded.Horizontal);
        Assert.Equal(0.5, loaded.BarWidth);
        Assert.Equal(-2, loaded.Baseline);
        Assert.Equal(Colors.Green, loaded.FaceColor);
        Assert.Equal(0.25, loaded.FaceAlpha);
        Assert.Equal("Hot", loaded.Colormap.Name);
    }

    [Fact]
    public void ABarChartRemembersThatItsEdgesWereTurnedOff()
    {
        // Null means "the default black" in a format that omits nulls, so an edge deliberately
        // turned off has to be recorded separately — the rule the patch already follows.
        var figure = new FigureModel();
        Bar3DPlot plot = figure.AddAxes().AddBar3D(new double[,] { { 1 } });
        plot.EdgeColor = null;

        Assert.Null(((Bar3DPlot)RoundTrip(figure).Axes[0].Plots[0]).EdgeColor);
    }

    [Fact]
    public void Pie3DPlot_RoundTrips_ItsWedgesAndTheirLabels()
    {
        var figure = new FigureModel();
        Pie3DPlot plot = figure.AddAxes().AddPie3D([1, 2, 3]);
        plot.Explode = [0, 0.1, 0];
        plot.Labels = ["one", "two", "three"];
        plot.Colormap = Colormap.Cool;
        plot.EdgeColor = Colors.Black;
        plot.LineWidth = 2;
        plot.FaceAlpha = 0.5;
        plot.StartAngle = 45;
        plot.Clockwise = true;
        plot.Height = 0.6;
        plot.LabelRadius = 1.5;

        var loaded = (Pie3DPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([1, 2, 3], loaded.Values);
        Assert.Equal<double>([0, 0.1, 0], loaded.Explode!);
        Assert.Equal<string>(["one", "two", "three"], loaded.Labels!);
        Assert.Equal("Cool", loaded.Colormap.Name);
        Assert.Equal(Colors.Black, loaded.EdgeColor);
        Assert.Equal(2, loaded.LineWidth);
        Assert.Equal(0.5, loaded.FaceAlpha);
        Assert.Equal(45, loaded.StartAngle);
        Assert.True(loaded.Clockwise);
        Assert.Equal(0.6, loaded.Height);
        Assert.Equal(1.5, loaded.LabelRadius);
    }

    [Fact]
    public void ARaisedPieKeepsItsWhiteOutlineWhenNothingChangedIt()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddPie3D([1, 1]);

        Assert.Equal(Colors.White, ((Pie3DPlot)RoundTrip(figure).Axes[0].Plots[0]).EdgeColor);
    }
}
