using System;
using System.IO;
using System.Linq;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Objects.Engineering;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

public class SerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    private static AxesModel SingleAxes(PlotObject plot)
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Plots.Add(plot);
        return axes;
    }

    // ---- Document header ----

    [Fact]
    public void Serialize_WritesFormatTagAndVersion()
    {
        string json = GraphFormat.Serialize(new FigureModel());
        Assert.Contains("\"format\": \"jgraph\"", json);
        Assert.Contains($"\"formatVersion\": {GraphFormat.CurrentVersion}", json);
    }

    // ---- Plot round-trips ----

    [Fact]
    public void Line_RoundTripsDataAndStyle()
    {
        var figure = new FigureModel();
        LinePlot line = figure.AddAxes().AddLine(new double[] { 0, 1, 2 }, new double[] { 3, 4, 5 });
        line.Color = Colors.Red;
        line.LineWidth = 3;
        line.DashStyle = DashStyle.Dash;
        line.Marker = MarkerType.Diamond;
        line.DisplayName = "series";

        var loaded = (LinePlot)RoundTrip(figure).Axes[0].Plots[0];
        Assert.Equal(3, loaded.Data.Count);
        Assert.Equal(5, loaded.Data.GetY(2));
        Assert.Equal(Colors.Red, loaded.Color);
        Assert.Equal(3, loaded.LineWidth);
        Assert.Equal(DashStyle.Dash, loaded.DashStyle);
        Assert.Equal(MarkerType.Diamond, loaded.Marker);
        Assert.Equal("series", loaded.DisplayName);
    }

    [Fact]
    public void Scatter_RoundTrips()
    {
        var scatter = new ScatterPlot(new double[] { 1, 2 }, new double[] { 3, 4 }) { Marker = MarkerType.Cross, EdgeWidth = 2 };
        var figure = new FigureModel();
        figure.AddAxes().Plots.Add(scatter);
        var loaded = (ScatterPlot)RoundTrip(figure).Axes[0].Plots[0];
        Assert.Equal(MarkerType.Cross, loaded.Marker);
        Assert.Equal(2, loaded.EdgeWidth);
        Assert.Equal(2, loaded.Data.Count);
    }

    [Fact]
    public void Bar_RoundTripsOrientationAndBaseline()
    {
        var bar = new BarPlot(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 }) { Horizontal = true, Baseline = 1, BarWidthFraction = 0.5 };
        var loaded = (BarPlot)RoundTrip(WithAxes(bar)).Axes[0].Plots[0];
        Assert.True(loaded.Horizontal);
        Assert.Equal(1, loaded.Baseline);
        Assert.Equal(0.5, loaded.BarWidthFraction);
    }

    [Fact]
    public void Stem_RoundTrips()
    {
        var stem = new StemPlot(new double[] { 0, 1 }, new double[] { 2, 3 }) { Baseline = -1 };
        var loaded = (StemPlot)RoundTrip(WithAxes(stem)).Axes[0].Plots[0];
        Assert.Equal(-1, loaded.Baseline);
        Assert.Equal(2, loaded.Data.Count);
    }

    [Fact]
    public void Histogram_RoundTripsSamplesAndBins()
    {
        var hist = new HistogramPlot(new double[] { 1, 2, 2, 3, 3, 3 }) { BinCount = 3, Normalization = HistogramNormalization.Probability };
        var loaded = (HistogramPlot)RoundTrip(WithAxes(hist)).Axes[0].Plots[0];
        Assert.Equal(3, loaded.BinCount);
        Assert.Equal(HistogramNormalization.Probability, loaded.Normalization);
        Assert.Equal(hist.BinHeights.ToArray(), loaded.BinHeights.ToArray());
    }

    [Fact]
    public void ErrorBar_RoundTripsErrors()
    {
        var eb = new ErrorBarPlot(
            new Core.Data.ArrayDataSeries(new double[] { 0, 1 }, new double[] { 10, 20 }),
            new double[] { 1, 2 },
            new double[] { 3, 4 })
        { CapSize = 8, ShowLine = false };
        var loaded = (ErrorBarPlot)RoundTrip(WithAxes(eb)).Axes[0].Plots[0];
        Assert.Equal(new double[] { 1, 2 }, loaded.ErrorNeg.ToArray());
        Assert.Equal(new double[] { 3, 4 }, loaded.ErrorPos.ToArray());
        Assert.Equal(8, loaded.CapSize);
        Assert.False(loaded.ShowLine);
        Assert.Equal(24, loaded.GetYDataBounds().Max); // max(y + errorPos) = 20 + 4
    }

    [Fact]
    public void Image_RoundTripsFieldColormapAndExtents()
    {
        var image = new ImagePlot(new double[,] { { 0, 1, 2 }, { 3, 4, 5 } })
        {
            Colormap = Colormap.Jet,
            XExtent = new DataRange(-2, 2),
            YExtent = new DataRange(0, 4),
            Interpolate = true,
            RowZeroAtTop = false,
        };
        var loaded = (ImagePlot)RoundTrip(WithAxes(image)).Axes[0].Plots[0];
        Assert.Equal(2, loaded.Rows);
        Assert.Equal(3, loaded.Columns);
        Assert.Equal(5, loaded.Values[1, 2]);
        Assert.Equal("Jet", loaded.Colormap.Name);
        Assert.Equal(-2, loaded.XExtent.Min);
        Assert.Equal(4, loaded.YExtent.Max);
        Assert.True(loaded.Interpolate);
        Assert.False(loaded.RowZeroAtTop);
    }

    [Fact]
    public void Polar_RoundTripsGridAndConvertedSeries()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddPolar(new[] { 0.0, System.Math.PI / 2 }, new[] { 1.0, 2.0 });

        AxesModel loaded = RoundTrip(figure).Axes[0];
        Assert.True(loaded.EqualAspect);
        Assert.False(loaded.FrameVisible);
        Assert.Contains(loaded.Plots, p => p is PolarGrid);
        Assert.Contains(loaded.Plots, p => p is LinePlot);
    }

    [Fact]
    public void Smith_RoundTripsGrid()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddSmith(new[] { 1.0, 0.0 }, new[] { 0.0, 0.0 });
        AxesModel loaded = RoundTrip(figure).Axes[0];
        Assert.Contains(loaded.Plots, p => p is SmithGrid);
        Assert.True(loaded.EqualAspect);
    }

    [Fact]
    public void Eye_RoundTripsSignalAndSymbolRate()
    {
        var eye = new EyeDiagramPlot(new double[] { 1, 2, 3, 4, 5, 6, 7, 8 }, samplesPerSymbol: 4, symbolsPerTrace: 2);
        var loaded = (EyeDiagramPlot)RoundTrip(WithAxes(eye)).Axes[0].Plots[0];
        Assert.Equal(4, loaded.SamplesPerSymbol);
        Assert.Equal(2, loaded.SymbolsPerTrace);
        Assert.Equal(8, loaded.Signal.Length);
    }

    // ---- Structure, scales, styles ----

    [Fact]
    public void Figure_RoundTripsTitleSizeAndSubplots()
    {
        var figure = new FigureModel { Title = "My Figure", Size = new Size2D(1024, 768), Background = Colors.WhiteSmoke };
        AxesModel top = figure.AddSubplot(2, 1, 1);
        top.Title = "Top";
        top.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        AxesModel bottom = figure.AddSubplot(2, 1, 2);
        bottom.AddBar(new[] { "A", "B" }, new double[] { 3, 4 });

        FigureModel loaded = RoundTrip(figure);
        Assert.Equal("My Figure", loaded.Title);
        Assert.Equal(1024, loaded.Size.Width);
        Assert.Equal(Colors.WhiteSmoke, loaded.Background);
        Assert.Equal(2, loaded.Axes.Count);
        Assert.Equal("Top", loaded.Axes[0].Title);
        Assert.True(loaded.Axes[0].NormalizedBounds.Y < loaded.Axes[1].NormalizedBounds.Y);
        // Category axis preserved.
        Assert.Equal(AxisScaleType.Category, loaded.Axes[1].PrimaryXAxis.Scale);
        Assert.Equal(new[] { "A", "B" }, loaded.Axes[1].PrimaryXAxis.Categories);
    }

    [Fact]
    public void Axis_RoundTripsScaleRangeAndTicks()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 1, 10 }, new double[] { 1, 100 });
        AxisModel y = axes.PrimaryYAxis;
        y.Scale = AxisScaleType.Logarithmic;
        y.AutoScale = false;
        y.Range = new DataRange(1, 1000);
        y.Label = "gain";
        y.Inverted = true;
        y.ShowMinorTicks = true;

        AxisModel loaded = RoundTrip(figure).Axes[0].PrimaryYAxis;
        Assert.Equal(AxisScaleType.Logarithmic, loaded.Scale);
        Assert.False(loaded.AutoScale);
        Assert.Equal(1000, loaded.Range.Max);
        Assert.Equal("gain", loaded.Label);
        Assert.True(loaded.Inverted);
        Assert.True(loaded.ShowMinorTicks);
    }

    [Fact]
    public void Legend_RoundTrips()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        axes.Legend.Visible = true;
        axes.Legend.Position = LegendPosition.BottomLeft;
        axes.Legend.Title = "Series";

        LegendModel loaded = RoundTrip(figure).Axes[0].Legend;
        Assert.True(loaded.Visible);
        Assert.Equal(LegendPosition.BottomLeft, loaded.Position);
        Assert.Equal("Series", loaded.Title);
    }

    // ---- Annotations ----

    [Fact]
    public void Annotations_RoundTripInBothSpaces()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });

        var text = axes.AddText(0.5, 0.5, "label");
        text.Color = Colors.Blue;
        text.Bold = true;
        text.HorizontalAlignment = HorizontalAlignment.Center;

        axes.AddArrow(0, 0, 1, 1);
        axes.AddRectangleAnnotation(0.1, 0.1, 0.4, 0.4);
        axes.AddEllipseAnnotation(0.2, 0.2, 0.6, 0.6);

        TextAnnotation figureNote = figure.AddText(0.9, 0.9, "corner");

        FigureModel loaded = RoundTrip(figure);
        AxesModel la = loaded.Axes[0];
        Assert.Equal(4, la.Annotations.Count);
        var loadedText = (TextAnnotation)la.Annotations[0];
        Assert.Equal("label", loadedText.Text);
        Assert.Equal(Colors.Blue, loadedText.Color);
        Assert.True(loadedText.Bold);
        Assert.Equal(HorizontalAlignment.Center, loadedText.HorizontalAlignment);
        Assert.IsType<ArrowAnnotation>(la.Annotations[1]);
        Assert.IsType<RectangleAnnotation>(la.Annotations[2]);
        Assert.IsType<EllipseAnnotation>(la.Annotations[3]);

        Assert.Single(loaded.Annotations);
        Assert.Equal("corner", ((TextAnnotation)loaded.Annotations[0]).Text);
        Assert.Equal(AnnotationSpace.Figure, loaded.Annotations[0].Space);
    }

    // ---- Data fidelity ----

    [Fact]
    public void Nan_GapsArePreserved()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddLine(new double[] { 0, 1, 2 }, new double[] { 0, double.NaN, 2 });
        var loaded = (LinePlot)RoundTrip(figure).Axes[0].Plots[0];
        Assert.True(double.IsNaN(loaded.Data.GetY(1)));
        Assert.Equal(2, loaded.Data.GetY(2));
    }

    [Fact]
    public void CommonPlotProperties_RoundTrip()
    {
        var figure = new FigureModel();
        LinePlot line = figure.AddAxes().AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        line.Visible = false;
        line.ZOrder = 5;
        line.Opacity = 0.5;
        line.HitTestVisible = false;

        var loaded = (LinePlot)RoundTrip(figure).Axes[0].Plots[0];
        Assert.False(loaded.Visible);
        Assert.Equal(5, loaded.ZOrder);
        Assert.Equal(0.5, loaded.Opacity);
        Assert.False(loaded.HitTestVisible);
    }

    // ---- File I/O ----

    [Fact]
    public void SaveAndLoad_RoundTripsThroughFile()
    {
        var figure = new FigureModel { Title = "Persisted" };
        figure.AddAxes().AddLine(new double[] { 0, 1, 2 }, new double[] { 5, 6, 7 });

        string path = Path.Combine(Path.GetTempPath(), $"jgraph-test-{Guid.NewGuid():N}{GraphFormat.FileExtension}");
        try
        {
            GraphFormat.Save(figure, path);
            FigureModel loaded = GraphFormat.Load(path);
            Assert.Equal("Persisted", loaded.Title);
            Assert.Equal(7, ((LinePlot)loaded.Axes[0].Plots[0]).Data.GetY(2));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // ---- Error handling ----

    [Fact]
    public void Deserialize_RejectsWrongFormatTag() =>
        Assert.Throws<GraphFormatException>(() => GraphFormat.Deserialize("{\"format\":\"other\",\"formatVersion\":1,\"figure\":{}}"));

    [Fact]
    public void Deserialize_RejectsNewerVersion() =>
        Assert.Throws<GraphFormatException>(() => GraphFormat.Deserialize("{\"format\":\"jgraph\",\"formatVersion\":9999,\"figure\":{}}"));

    [Fact]
    public void Deserialize_RejectsMalformedJson() =>
        Assert.Throws<GraphFormatException>(() => GraphFormat.Deserialize("{ this is not json"));

    private static FigureModel WithAxes(PlotObject plot)
    {
        AxesModel axes = SingleAxes(plot);
        return (FigureModel)axes.Parent!;
    }
}
