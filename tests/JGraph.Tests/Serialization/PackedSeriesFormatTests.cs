using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>
/// M22 (.graph v4): large series persist as packed base64 doubles, small series stay readable JSON
/// arrays, older documents load unchanged, and save/load stream without giant strings.
/// </summary>
public class PackedSeriesFormatTests
{
    private static FigureModel FigureWithLine(double[] xs, double[] ys)
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(xs, ys);
        return figure;
    }

    [Fact]
    public void SmallSeries_StayAsReadableJsonArrays()
    {
        string json = GraphFormat.Serialize(FigureWithLine([1, 2, 3], [4, 5, 6]));

        Assert.Contains("\"xs\"", json);
        Assert.DoesNotContain("xsPacked", json);
        Assert.Contains($"\"formatVersion\": {GraphFormat.CurrentVersion}", json);

        var line = Assert.IsType<LinePlot>(Assert.Single(GraphFormat.Deserialize(json).Axes).Plots[0]);
        Assert.Equal(3, line.Data.Count);
        Assert.Equal(5, line.Data.GetY(1));
    }

    [Fact]
    public void LargeSeries_RoundTripPacked_IncludingNaNAndInfinity()
    {
        const int count = 20_000; // over the packing threshold
        double[] xs = new double[count];
        double[] ys = new double[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = i * 0.25;
            ys[i] = Math.Sin(i * 0.001) * 1e6;
        }

        ys[7] = double.NaN;
        ys[8] = double.PositiveInfinity;
        ys[9] = double.NegativeInfinity;

        string json = GraphFormat.Serialize(FigureWithLine(xs, ys));
        Assert.Contains("xsPacked", json);
        Assert.DoesNotContain("\"xs\"", json);

        var line = Assert.IsType<LinePlot>(Assert.Single(GraphFormat.Deserialize(json).Axes).Plots[0]);
        Assert.Equal(count, line.Data.Count);
        Assert.Equal(xs[12_345], line.Data.GetX(12_345));
        Assert.Equal(ys[54], line.Data.GetY(54));
        Assert.True(double.IsNaN(line.Data.GetY(7)));
        Assert.True(double.IsPositiveInfinity(line.Data.GetY(8)));
        Assert.True(double.IsNegativeInfinity(line.Data.GetY(9)));
    }

    [Fact]
    public void SaveAndLoad_StreamThroughFiles()
    {
        const int count = 50_000;
        double[] xs = new double[count];
        double[] ys = new double[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = i;
            ys[i] = i * 2.5;
        }

        string path = Path.Combine(Path.GetTempPath(), $"jgraph-m22-{Guid.NewGuid():N}.graph");
        try
        {
            GraphFormat.Save(FigureWithLine(xs, ys), path);

            // A packed 50k-point figure is well under a MB; digit lists would be several.
            Assert.True(new FileInfo(path).Length < 1_200_000);

            var line = Assert.IsType<LinePlot>(Assert.Single(GraphFormat.Load(path).Axes).Plots[0]);
            Assert.Equal(count, line.Data.Count);
            Assert.Equal((count - 1) * 2.5, line.Data.GetY(count - 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VersionThreeDocuments_WithPlainArrays_LoadUnchanged()
    {
        // A small series serializes with plain arrays, which is exactly the v3 shape (v4 only
        // added optional members) — restamping the version yields a faithful v3-era document.
        string v3Json = GraphFormat.Serialize(FigureWithLine([1, 2, 3], [10, 20, 30]))
            .Replace($"\"formatVersion\": {GraphFormat.CurrentVersion}", "\"formatVersion\": 3");
        Assert.Contains("\"formatVersion\": 3", v3Json);

        var line = Assert.IsType<LinePlot>(Assert.Single(GraphFormat.Deserialize(v3Json).Axes).Plots[0]);
        Assert.Equal(3, line.Data.Count);
        Assert.Equal(30, line.Data.GetY(2));
    }

    [Fact]
    public void CorruptPackedPayload_FailsWithAClearError()
    {
        string json = GraphFormat.Serialize(FigureWithLine(new double[20_000], new double[20_000]));
        string corrupted = json.Replace("\"count\": 20000", "\"count\": 19999");
        Assert.Throws<GraphFormatException>(() => GraphFormat.Deserialize(corrupted));
    }
}
