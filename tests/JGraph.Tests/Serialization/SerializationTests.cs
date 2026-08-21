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

    /// <summary>
    /// M55 bumped the format to 6 for the chart types of the M55–M60 arc. The bump only ever says
    /// "there may be plot kinds in here you have not met"; it removes and renames nothing, so a
    /// document written by the previous build still loads with everything in it.
    /// </summary>
    [Fact]
    public void AVersionFiveDocumentStillLoadsUnderVersionSix()
    {
        const string v5 = """
            {
              "format": "jgraph",
              "formatVersion": 5,
              "figure": {
                "title": "Measured",
                "axes": [
                  {
                    "title": "Run 1",
                    "plots": [
                      { "type": "line", "displayName": "signal", "lineWidth": 2.5,
                        "series": { "xs": [1, 2, 3], "ys": [4, 5, 6] } }
                    ]
                  }
                ]
              }
            }
            """;

        FigureModel figure = GraphFormat.Deserialize(v5);

        Assert.Equal("Measured", figure.Title);
        AxesModel axes = Assert.Single(figure.Axes);
        Assert.Equal("Run 1", axes.Title);
        var line = Assert.IsType<LinePlot>(Assert.Single(axes.Plots));
        Assert.Equal("signal", line.DisplayName);
        Assert.Equal(2.5, line.LineWidth);
        Assert.Equal(3, line.Data.Count);
    }

    [Fact]
    public void Area_RoundTripsItsFillItsFloorAndItsStack()
    {
        var band = new AreaPlot(new[] { 1.0, 2, 3 }, new[] { 4.0, 5, 6 })
        {
            FaceColor = Colors.Red,
            EdgeColor = Colors.Black,
            FaceAlpha = 0.4,
            LineWidth = 3,
            Dash = DashStyle.Dot,
            BaseValue = -1,
            ShowBaseLine = false,
            LowerEdge = new[] { 0.5, 1.0, 1.5 },
        };

        var restored = (AreaPlot)RoundTrip(WithAxes(band)).Axes[0].Plots[0];

        Assert.Equal(Colors.Red, restored.FaceColor);
        Assert.Equal(Colors.Black, restored.EdgeColor);
        Assert.Equal(0.4, restored.FaceAlpha);
        Assert.Equal(3, restored.LineWidth);
        Assert.Equal(DashStyle.Dot, restored.Dash);
        Assert.Equal(-1, restored.BaseValue);
        Assert.False(restored.ShowBaseLine);
        Assert.Equal(new[] { 0.5, 1.0, 1.5 }, restored.LowerEdge);
        Assert.Equal(6, restored.Data.GetY(2));
    }

    [Fact]
    public void Bar_RoundTripsItsArrangementAsWellAsItsAppearance()
    {
        var bar = new BarPlot(new[] { 1.0, 2, 3 }, new[] { 4.0, 5, 6 })
        {
            FillColor = Colors.Red,
            EdgeColor = Colors.Black,
            EdgeWidth = 3,
            FaceAlpha = 0.4,
            Dash = DashStyle.Dot,
            BarWidthFraction = 0.5,
            Baseline = -1,
            Horizontal = true,
            GroupIndex = 1,
            GroupCount = 3,
            PositionOffset = 0.5,
            LowerEdge = new[] { 0.5, 1.0, 1.5 },
        };

        var restored = (BarPlot)RoundTrip(WithAxes(bar)).Axes[0].Plots[0];

        Assert.Equal(Colors.Red, restored.FillColor);
        Assert.Equal(0.4, restored.FaceAlpha);
        Assert.Equal(DashStyle.Dot, restored.Dash);
        Assert.Equal(0.5, restored.BarWidthFraction);
        Assert.Equal(-1, restored.Baseline);
        Assert.True(restored.Horizontal);
        Assert.Equal(1, restored.GroupIndex);
        Assert.Equal(3, restored.GroupCount);
        Assert.Equal(0.5, restored.PositionOffset);
        Assert.Equal(new[] { 0.5, 1.0, 1.5 }, restored.LowerEdge);
    }

    [Fact]
    public void Line_RoundTripsTheStepThatMakesItAStaircase()
    {
        var stairs = new LinePlot(new[] { 1.0, 2, 3 }, new[] { 4.0, 5, 6 }) { Steps = StepMode.Post };

        var restored = (LinePlot)RoundTrip(WithAxes(stairs)).Axes[0].Plots[0];

        Assert.Equal(StepMode.Post, restored.Steps);
    }

    [Fact]
    public void PolarHistogram_RoundTripsItsBinsItsCountsAndTheAnglesBehindThem()
    {
        var histogram = new PolarHistogramPlot([0.5, 0.5, 2.0], [0, 1, 2, 3])
        {
            Normalization = HistogramNormalization.CountDensity,
            DisplayStyle = PolarHistogramDisplayStyle.Stairs,
            FaceColor = Colors.Red,
            EdgeColor = Colors.Black,
            FaceAlpha = 0.4,
            EdgeAlpha = 0.8,
            LineWidth = 2,
            LineStyle = DashStyle.Dash,
        };

        var restored = (PolarHistogramPlot)RoundTrip(WithAxes(histogram)).Axes[0].Plots[0];

        Assert.Equal(new[] { 0.5, 0.5, 2.0 }, restored.Data);
        Assert.Equal(new[] { 0.0, 1, 2, 3 }, restored.BinEdges);
        Assert.Equal(new[] { 2.0, 0, 1 }, restored.BinCounts);
        Assert.Equal(HistogramNormalization.CountDensity, restored.Normalization);
        Assert.Equal(PolarHistogramDisplayStyle.Stairs, restored.DisplayStyle);
        Assert.Equal(Colors.Red, restored.FaceColor);
        Assert.Equal(Colors.Black, restored.EdgeColor);
        Assert.Equal(0.4, restored.FaceAlpha);
        Assert.Equal(0.8, restored.EdgeAlpha);
        Assert.Equal(2, restored.LineWidth);
        Assert.Equal(DashStyle.Dash, restored.LineStyle);
    }

    /// <summary>
    /// The counts-only form has no data to count again, so the file has to carry the heights
    /// themselves — which is why the counts are saved even when there is data behind them.
    /// </summary>
    [Fact]
    public void PolarHistogram_KeepsCountsThatWereNeverCountedFromData()
    {
        PolarHistogramPlot histogram = PolarHistogramPlot.FromCounts([0, 1, 2], [3, 7]);

        var restored = (PolarHistogramPlot)RoundTrip(WithAxes(histogram)).Axes[0].Plots[0];

        Assert.Empty(restored.Data);
        Assert.Equal(new[] { 3.0, 7 }, restored.BinCounts);
    }

    [Fact]
    public void Pie_RoundTripsItsWedgesTheirLabelsAndTheColoursTheyCameFrom()
    {
        var pie = new PiePlot(new[] { 1.0, 2, 3 })
        {
            Explode = new[] { 0.0, 0.1, 0 },
            Labels = new[] { "one", "two", "three" },
            Colormap = Colormap.Jet,
            EdgeColor = Colors.Black,
            LineWidth = 2,
            FaceAlpha = 0.6,
            StartAngle = 30,
            Clockwise = true,
            ShowLabels = false,
            LabelRadius = 1.4,
            LabelStyle = new TextStyle(Colors.Red, 14),
        };

        var restored = (PiePlot)RoundTrip(WithAxes(pie)).Axes[0].Plots[0];

        Assert.Equal(new[] { 1.0, 2, 3 }, restored.Values);
        Assert.Equal(new[] { 0.0, 0.1, 0 }, restored.Explode);
        Assert.Equal(new[] { "one", "two", "three" }, restored.Labels);
        Assert.Equal(Colormap.Jet.Stops, restored.Colormap.Stops);
        Assert.Equal(Colors.Black, restored.EdgeColor);
        Assert.Equal(2, restored.LineWidth);
        Assert.Equal(0.6, restored.FaceAlpha);
        Assert.Equal(30, restored.StartAngle);
        Assert.True(restored.Clockwise);
        Assert.False(restored.ShowLabels);
        Assert.Equal(1.4, restored.LabelRadius);
        Assert.Equal(14, restored.LabelStyle?.FontSize);
    }

    [Fact]
    public void Heatmap_RoundTripsItsCellsTheirNamesAndEverythingAboutTheirColour()
    {
        var heatmap = new HeatmapPlot(new double[,] { { 1, 2, 3 }, { 4, 5, double.NaN } })
        {
            XData = ["a", "b", "c"],
            YData = ["top", "bottom"],
            Colormap = Colormap.Jet,
            ColorLimits = new DataRange(0, 10),
            ColorScaling = HeatmapScaling.ScaledRows,
            ShowCellLabels = false,
            CellLabelColor = Colors.Red,
            CellLabelFormat = "0.00",
            CellLabelStyle = new TextStyle(Colors.Blue, 14),
            GridVisible = false,
            GridColor = Colors.Black,
            MissingDataColor = Colors.Gray,
            MissingDataLabel = "gone",
        };

        var restored = (HeatmapPlot)RoundTrip(WithAxes(heatmap)).Axes[0].Plots[0];

        Assert.Equal(2, restored.Rows);
        Assert.Equal(3, restored.Columns);
        Assert.Equal(5, restored.ColorData[1, 1]);
        Assert.True(double.IsNaN(restored.ColorData[1, 2]));
        Assert.Equal(new[] { "a", "b", "c" }, restored.XData);
        Assert.Equal(new[] { "top", "bottom" }, restored.YData);
        Assert.Equal(Colormap.Jet.Stops, restored.Colormap.Stops);
        Assert.Equal(new DataRange(0, 10), restored.ColorLimits);
        Assert.Equal(HeatmapScaling.ScaledRows, restored.ColorScaling);
        Assert.False(restored.ShowCellLabels);
        Assert.Equal(Colors.Red, restored.CellLabelColor);
        Assert.Equal("0.00", restored.CellLabelFormat);
        Assert.Equal(14, restored.CellLabelStyle.FontSize);
        Assert.False(restored.GridVisible);
        Assert.Equal(Colors.Black, restored.GridColor);
        Assert.Equal(Colors.Gray, restored.MissingDataColor);
        Assert.Equal("gone", restored.MissingDataLabel);
    }

    [Fact]
    public void BoxChart_RoundTripsItsObservationsTheirGroupingAndHowTheBoxesAreDrawn()
    {
        var chart = new BoxChartPlot([1, 1, 2, 2], [1, 3, 10, 100])
        {
            BoxFaceColor = Colors.Red,
            BoxFaceAlpha = 0.25,
            BoxEdgeColor = Colors.Blue,
            BoxMedianLineColor = Colors.Green,
            BoxWidth = 0.8,
            LineWidth = 2.5,
            WhiskerLineColor = Colors.Magenta,
            WhiskerLineStyle = DashStyle.Dash,
            MarkerStyle = MarkerType.Plus,
            MarkerSize = 12,
            MarkerColor = Colors.Black,
            Notch = true,
            JitterOutliers = true,
            Horizontal = true,
        };

        var restored = (BoxChartPlot)RoundTrip(WithAxes(chart)).Axes[0].Plots[0];

        Assert.Equal(new[] { 1.0, 1, 2, 2 }, restored.XData);
        Assert.Equal(new[] { 1.0, 3, 10, 100 }, restored.YData);
        Assert.Equal(Colors.Red, restored.BoxFaceColor);
        Assert.Equal(0.25, restored.BoxFaceAlpha);
        Assert.Equal(Colors.Blue, restored.BoxEdgeColor);
        Assert.Equal(Colors.Green, restored.BoxMedianLineColor);
        Assert.Equal(0.8, restored.BoxWidth);
        Assert.Equal(2.5, restored.LineWidth);
        Assert.Equal(Colors.Magenta, restored.WhiskerLineColor);
        Assert.Equal(DashStyle.Dash, restored.WhiskerLineStyle);
        Assert.Equal(MarkerType.Plus, restored.MarkerStyle);
        Assert.Equal(12, restored.MarkerSize);
        Assert.Equal(Colors.Black, restored.MarkerColor);
        Assert.True(restored.Notch);
        Assert.True(restored.JitterOutliers);
        Assert.True(restored.Horizontal);

        // The boxes are summarized again from the observations rather than stored, so a loaded
        // chart says the same thing about them as the one that was saved.
        Assert.Equal([2, 55], restored.Groups().Select(g => g.Summary.Median));
    }

    [Fact]
    public void BubbleChart_RoundTripsItsSizesTheScaleTheyAreReadAgainstAndTheLegend()
    {
        var chart = new ScatterPlot([1, 2, 3], [10, 20, 30])
        {
            BubbleSizing = true,
            Marker = MarkerType.Square,
            EdgeWidth = 2,
        };
        chart.SizeData = [0, 50, 100];
        chart.ColorData = [1, 2, 3];

        FigureModel figure = WithAxes(chart);
        AxesModel axes = figure.Axes[0];
        axes.BubbleSizeRange = new DataRange(10, 30);
        axes.BubbleSizeLimits = new DataRange(0, 200);
        axes.BubbleLegend.Visible = true;
        axes.BubbleLegend.Title = "Population";
        axes.BubbleLegend.Style = BubbleLegendStyle.Telescopic;
        axes.BubbleLegend.NumBubbles = 5;
        axes.BubbleLegend.LimitLabels = true;
        axes.BubbleLegend.Position = LegendPosition.BottomLeft;

        FigureModel loaded = RoundTrip(figure);
        AxesModel restoredAxes = loaded.Axes[0];
        var restored = (ScatterPlot)restoredAxes.Plots[0];

        Assert.True(restored.BubbleSizing);
        Assert.Equal([0.0, 50, 100], restored.SizeData);
        Assert.Equal([1.0, 2, 3], restored.ColorData);
        Assert.Equal(MarkerType.Square, restored.Marker);
        Assert.Equal(2, restored.EdgeWidth);

        Assert.Equal(new DataRange(10, 30), restoredAxes.BubbleSizeRange);
        Assert.Equal(new DataRange(0, 200), restoredAxes.BubbleSizeLimits);
        Assert.True(restoredAxes.BubbleLegend.Visible);
        Assert.Equal("Population", restoredAxes.BubbleLegend.Title);
        Assert.Equal(BubbleLegendStyle.Telescopic, restoredAxes.BubbleLegend.Style);
        Assert.Equal(5, restoredAxes.BubbleLegend.NumBubbles);
        Assert.True(restoredAxes.BubbleLegend.LimitLabels);
        Assert.Equal(LegendPosition.BottomLeft, restoredAxes.BubbleLegend.Position);

        // The diameters are worked out again from the sizes and the scale rather than stored, so a
        // loaded chart draws the bubbles the saved one drew.
        Assert.Equal(chart.DiameterAt(1), restored.DiameterAt(1), 12);
    }

    [Fact]
    public void Axes_RoundTripsWhichYRulerEachSeriesIsMeasuredAgainst()
    {
        // What pareto and plotyy draw is two series against two scales, and which series belongs to
        // which ruler is the one thing about that arrangement a saved figure could lose.
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.UseYAxis(0);
        LinePlot left = axes.AddLine([1, 2, 3], [10, 20, 30]);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 60);

        axes.UseYAxis(1);
        LinePlot right = axes.AddLine([1, 2, 3], [55, 85, 95]);
        axes.ActiveYAxis.AutoScale = false;
        axes.ActiveYAxis.Range = new DataRange(0, 100);
        axes.ActiveYAxis.Label = "percent";

        Assert.Equal(0, left.YAxisIndex);
        Assert.Equal(1, right.YAxisIndex);

        AxesModel loaded = RoundTrip(figure).Axes[0];

        Assert.Equal(2, loaded.YAxes.Count);
        Assert.Equal(0, loaded.Plots[0].YAxisIndex);
        Assert.Equal(1, loaded.Plots[1].YAxisIndex);
        Assert.Equal(new DataRange(0, 60), loaded.YAxes[0].Range);
        Assert.Equal(new DataRange(0, 100), loaded.YAxes[1].Range);
        Assert.Equal("percent", loaded.YAxes[1].Label);
        Assert.False(loaded.YAxes[1].AutoScale);
    }

    /// <summary>
    /// M56: a polar axes is a mode plus two rulers, all of them defaulted fields, so a v6 document
    /// carries it with no discriminator and no bump. What a file could lose here is the turn — an
    /// axes reloaded pointing the other way is a chart whose every bearing is wrong.
    /// </summary>
    [Fact]
    public void PolarAxes_RoundTripsItsModeItsTurnAndBothRulers()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.MakePolar();
        axes.ThetaZeroLocation = ThetaZeroLocation.Top;
        axes.ThetaDirection = ThetaDirection.Clockwise;
        axes.ThetaAxisUnits = AngleUnits.Radians;
        axes.RAxisLocation = 45;
        axes.RAxis.AutoScale = false;
        axes.RAxis.Range = new DataRange(0, 12);
        axes.RAxis.Label = "gain";
        axes.ThetaAxis.Range = new DataRange(0, 180);
        axes.ThetaAxis.TickPositions = new[] { 0.0, 90.0, 180.0 };
        axes.AddLine([0, 1, 2], [3, 6, 9]);

        AxesModel loaded = RoundTrip(figure).Axes[0];

        Assert.True(loaded.IsPolar);
        Assert.Equal(ThetaZeroLocation.Top, loaded.ThetaZeroLocation);
        Assert.Equal(ThetaDirection.Clockwise, loaded.ThetaDirection);
        Assert.Equal(AngleUnits.Radians, loaded.ThetaAxisUnits);
        Assert.Equal(45, loaded.RAxisLocation);
        Assert.False(loaded.RAxis.AutoScale);
        Assert.Equal(new DataRange(0, 12), loaded.RAxis.Range);
        Assert.Equal("gain", loaded.RAxis.Label);
        Assert.Equal(new DataRange(0, 180), loaded.ThetaAxis.Range);
        Assert.Equal(new[] { 0.0, 90.0, 180.0 }, loaded.ThetaAxis.TickPositions);
    }

    /// <summary>A document written before polar existed reads back as ordinary square paper.</summary>
    [Fact]
    public void PolarAxes_AnOrdinaryAxesStaysOrdinaryAcrossTheRoundTrip()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddLine([1, 2], [3, 4]);

        AxesModel loaded = RoundTrip(figure).Axes[0];

        Assert.False(loaded.IsPolar);
        Assert.Equal(ThetaZeroLocation.Right, loaded.ThetaZeroLocation);
        Assert.Equal(new DataRange(0, 360), loaded.ThetaAxis.Range);
    }

    /// <summary>
    /// A compass is an arrow field with its automatic scaling switched off, and that switch is the
    /// part worth pinning: a saved chart that came back scaling itself would draw arrows of a length
    /// its own numbers do not have.
    /// </summary>
    [Fact]
    public void Compass_KeepsItsArrowsUnscaledOnAPolarAxes()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.MakePolar();
        QuiverPlot arrows = axes.AddQuiver([0, System.Math.PI / 2], [0, 0], [0, 0], [1, 2]);
        arrows.AutoScale = false;
        arrows.Scale = 1;

        AxesModel loaded = RoundTrip(figure).Axes[0];
        var restored = (QuiverPlot)loaded.Plots[0];

        Assert.True(loaded.IsPolar);
        Assert.False(restored.AutoScale);
        Assert.Equal(1, restored.EffectiveScale);
        Assert.Equal(new[] { 1.0, 2 }, restored.V);
    }

    [Fact]
    public void Axis_RoundTripsManualTicksAndTheirAngle()
    {
        var figure = new FigureModel();
        AxisModel axis = figure.AddAxes().PrimaryXAxis;
        axis.TickPositions = new[] { 0.0, 2.5, 5.0 };
        axis.TickLabelOverrides = new[] { "low", "mid", "high" };
        axis.TickLabelAngle = 30;

        AxisModel loaded = RoundTrip(figure).Axes[0].PrimaryXAxis;

        Assert.Equal(new[] { 0.0, 2.5, 5.0 }, loaded.TickPositions);
        Assert.Equal(new[] { "low", "mid", "high" }, loaded.TickLabelOverrides);
        Assert.Equal(30, loaded.TickLabelAngle);
    }

    [Fact]
    public void Axis_AnAutomaticRulerSaysSoRatherThanSavingAnEmptyList()
    {
        // Null and empty mean different things — no ticks named versus no ticks wanted — so a figure
        // written before these fields existed has to come back automatic, not blank.
        var figure = new FigureModel();
        figure.AddAxes();

        AxisModel loaded = RoundTrip(figure).Axes[0].PrimaryXAxis;

        Assert.Null(loaded.TickPositions);
        Assert.Null(loaded.TickLabelOverrides);
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
    public void ConstantLine_RoundTripsWhereItIsAndWhatItSays()
    {
        var xline = new ConstantLinePlot(ConstantLineDirection.Vertical, 7.5)
        {
            Label = "limit",
            Dash = DashStyle.Dot,
            LineWidth = 2,
            LabelHorizontalAlignment = HorizontalAlignment.Left,
            LabelVerticalAlignment = VerticalAlignment.Bottom,
        };

        var loaded = (ConstantLinePlot)RoundTrip(WithAxes(xline)).Axes[0].Plots[0];

        Assert.Equal(ConstantLineDirection.Vertical, loaded.Direction);
        Assert.Equal(7.5, loaded.Value);
        Assert.Equal("limit", loaded.Label);
        Assert.Equal(DashStyle.Dot, loaded.Dash);
        Assert.Equal(2, loaded.LineWidth);
        Assert.Equal(HorizontalAlignment.Left, loaded.LabelHorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, loaded.LabelVerticalAlignment);
    }

    [Fact]
    public void ContourLabelling_RoundTrips()
    {
        var contour = new ContourPlot(
            [0, 1], [0, 1], new double[,] { { 0, 1 }, { 2, 3 } })
        {
            ShowText = true,
            LabelLevels = [1, 2],
        };

        var loaded = (ContourPlot)RoundTrip(WithAxes(contour)).Axes[0].Plots[0];

        Assert.True(loaded.ShowText);
        Assert.Equal(new double[] { 1, 2 }, loaded.LabelLevels);
    }

    [Fact]
    public void Subtitle_RoundTripsWithItsOwnStyle()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Title = "over";
        axes.Subtitle = "under";
        axes.SubtitleStyle = axes.SubtitleStyle.WithSize(7);

        AxesModel loaded = RoundTrip(figure).Axes[0];

        Assert.Equal("over", loaded.Title);
        Assert.Equal("under", loaded.Subtitle);
        Assert.Equal(7, loaded.SubtitleStyle.FontSize);
    }

    [Fact]
    public void CameraRoll_RoundTrips()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Is3D = true;
        axes.Roll = 22.5;

        Assert.Equal(22.5, RoundTrip(figure).Axes[0].Roll, 12);
    }

    [Fact]
    public void ASolidFaceColor_RoundTripsWithTheSurface()
    {
        var surface = new SurfacePlot(
            new double[] { 0, 1 }, new double[] { 0, 1 }, new double[,] { { 0, 1 }, { 1, 2 } })
        {
            Style = SurfaceStyle.FilledWithWireframe,
            FaceColor = Color.FromRgb(0x11, 0x22, 0x33),
        };

        var loaded = (SurfacePlot)RoundTrip(WithAxes(surface)).Axes[0].Plots[0];
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), loaded.FaceColor);
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

    /// <summary>
    /// M44 wave 3: a palette is not a gradient, and the flag saying so has to survive the file. A
    /// discrete map that came back interpolating would blend its colors into each other and look
    /// nothing like what was saved.
    /// </summary>
    [Fact]
    public void Image_RoundTripsADiscretePalette()
    {
        var image = new ImagePlot(new double[,] { { 0, 1 }, { 2, 3 } }) { Colormap = Colormap.Lines };
        var loaded = (ImagePlot)RoundTrip(WithAxes(image)).Axes[0].Plots[0];

        Assert.Equal("Lines", loaded.Colormap.Name);
        Assert.True(loaded.Colormap.Discrete);
        Assert.Equal(Colormap.Lines.Stops, loaded.Colormap.Stops);
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

    [Fact]
    public void LegendEntries_RoundTripRenamedReorderedAndExcludedRows()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        foreach (string name in new[] { "Alpha", "Beta", "Gamma" })
        {
            axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 }).DisplayName = name;
        }

        axes.Legend.Visible = true;
        axes.Legend.SyncEntries(axes.Plots);
        axes.Legend.Entries[2].Label = "Renamed";
        axes.Legend.Entries[1].Visible = false;
        axes.Legend.Entries.Move(2, 0);

        LegendModel loaded = RoundTrip(figure).Axes[0].Legend;

        Assert.Equal(3, loaded.Entries.Count);
        Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, loaded.Entries.Select(e => e.Plot?.DisplayName));
        Assert.Equal("Renamed", loaded.Entries[0].Label);
        Assert.Null(loaded.Entries[1].Label);
        Assert.False(loaded.Entries[2].Visible);
    }

    [Fact]
    public void LegendCustomPlacement_RoundTrips()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        axes.Legend.Visible = true;
        axes.Legend.Position = LegendPosition.Custom;
        axes.Legend.Location = new Point2D(0.31, 0.72);

        LegendModel loaded = RoundTrip(figure).Axes[0].Legend;

        Assert.Equal(LegendPosition.Custom, loaded.Position);
        Assert.Equal(0.31, loaded.Location.X, 6);
        Assert.Equal(0.72, loaded.Location.Y, 6);
    }

    [Fact]
    public void LegendEntries_AreRebuiltWhenADocumentHasNone()
    {
        // Documents written before legends had rows carry no entries; the first paint reconciles them.
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 }).DisplayName = "Alpha";
        axes.Legend.Visible = true;

        AxesModel loaded = RoundTrip(figure).Axes[0];
        Assert.Empty(loaded.Legend.Entries);

        Assert.True(loaded.Legend.SyncEntries(loaded.Plots));
        Assert.Same(loaded.Plots[0], Assert.Single(loaded.Legend.Entries).Plot);
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

    [Fact]
    public void AnAxesRoundTripsItsCameraItsAlphaMappingAndHowItClipsAndSorts()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Is3D = true;
        axes.CameraPosition = new Vector3D(1, 2, 3);
        axes.CameraTarget = new Vector3D(4, 5, 6);
        axes.CameraUpVector = new Vector3D(0, 1, 0);
        axes.CameraViewAngle = 20;
        axes.Projection = ProjectionType.Perspective;
        axes.SortMethod = SortMethodType.ChildOrder;
        axes.Clipping = false;
        axes.AlphaLimits = new DataRange(0.25, 0.75);
        axes.Alphamap = [0, 0.5, 1];
        axes.AlphaScale = ColorScaleType.Log;

        AxesModel restored = RoundTrip(figure).Axes[0];

        Assert.Equal(new Vector3D(1, 2, 3), restored.CameraPosition);
        Assert.Equal(new Vector3D(4, 5, 6), restored.CameraTarget);
        Assert.Equal(new Vector3D(0, 1, 0), restored.CameraUpVector);
        Assert.Equal(20, restored.CameraViewAngle);
        Assert.Equal(ProjectionType.Perspective, restored.Projection);
        Assert.Equal(SortMethodType.ChildOrder, restored.SortMethod);
        Assert.False(restored.Clipping);
        Assert.Equal(new DataRange(0.25, 0.75), restored.AlphaLimits);
        Assert.Equal(new double[] { 0, 0.5, 1 }, restored.Alphamap);
        Assert.Equal(ColorScaleType.Log, restored.AlphaScale);
    }

    [Fact]
    public void AFigureRoundTripsItsWindowItsPageAndTheMapsItsAxesFallBackOn()
    {
        var figure = new FigureModel
        {
            Colormap = Colormap.Hot,
            Alphamap = [0, 0.25, 1],
            NextPlot = FigureNextPlot.ReplaceChildren,
            NumberTitle = false,
            FileName = "somewhere.fig",
            InvertHardcopy = true,
            GraphicsSmoothing = false,
            Pointer = PointerShape.Watch,
            Resizable = false,
            ToolBar = FigureToolBarMode.None,
            WindowState = FigureWindowState.Maximized,
            Position = new Point2D(120, 240),
            PaperUnits = PaperUnitType.Centimeters,
            PaperOrientation = PaperOrientationType.Landscape,
            PaperPosition = new Rect2D(1, 2, 4, 3),
            PaperPositionAuto = false,
        };
        figure.PaperSize = new Size2D(5, 7);

        FigureModel restored = RoundTrip(figure);

        Assert.Equal(Colormap.Hot.Stops.Count, restored.Colormap!.Stops.Count);
        Assert.Equal(new double[] { 0, 0.25, 1 }, restored.Alphamap);
        Assert.Equal(FigureNextPlot.ReplaceChildren, restored.NextPlot);
        Assert.False(restored.NumberTitle);
        Assert.Equal("somewhere.fig", restored.FileName);
        Assert.True(restored.InvertHardcopy);
        Assert.False(restored.GraphicsSmoothing);
        Assert.Equal(PointerShape.Watch, restored.Pointer);
        Assert.False(restored.Resizable);
        Assert.Equal(FigureToolBarMode.None, restored.ToolBar);
        Assert.Equal(FigureWindowState.Maximized, restored.WindowState);
        Assert.True(restored.PositionSpecified);
        Assert.Equal(new Point2D(120, 240), restored.Position);
        Assert.Equal(PaperUnitType.Centimeters, restored.PaperUnits);
        Assert.Equal(PaperSizes.CustomName, restored.PaperType);
        Assert.Equal(new Size2D(5, 7), restored.PaperSize);
        Assert.Equal(PaperOrientationType.Landscape, restored.PaperOrientation);
        Assert.Equal(new Rect2D(1, 2, 4, 3), restored.PaperPosition);
        Assert.False(restored.PaperPositionAuto);
    }

    [Fact]
    public void AFigureNobodyTouchedRoundTripsAsTheFigureItAlwaysWas()
    {
        // Every M75 field is absent from a document written before it, and absent has to mean the
        // figure that document described: unplaced, numbered, resizable, on letter paper.
        FigureModel restored = RoundTrip(new FigureModel());

        Assert.Null(restored.Colormap);
        Assert.Null(restored.Alphamap);
        Assert.Equal(FigureNextPlot.Add, restored.NextPlot);
        Assert.True(restored.NumberTitle);
        Assert.False(restored.PositionSpecified);
        Assert.True(restored.Resizable);
        Assert.True(restored.GraphicsSmoothing);
        Assert.False(restored.InvertHardcopy);
        Assert.Equal("usletter", restored.PaperType);
        Assert.True(restored.PaperPositionAuto);
    }

    [Fact]
    public void AnAxesRoundTripsThePlotBoxItWasPinnedTo()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.InnerTarget = new Rect2D(0.2, 0.25, 0.5, 0.5);
        axes.PositionConstraint = PositionConstraintType.InnerPosition;

        AxesModel restored = RoundTrip(figure).Axes[0];

        Assert.Equal(new Rect2D(0.2, 0.25, 0.5, 0.5), restored.InnerTarget);
        Assert.Equal(PositionConstraintType.InnerPosition, restored.PositionConstraint);

        // And an axes nobody pinned comes back placed by its cell, as every axes was before M75.
        var plain = new FigureModel();
        plain.AddAxes();
        AxesModel untouched = RoundTrip(plain).Axes[0];
        Assert.Null(untouched.InnerTarget);
        Assert.Equal(PositionConstraintType.OuterPosition, untouched.PositionConstraint);
    }

    [Fact]
    public void AnAxesThatWasNeverToldAboutItsCameraRoundTripsAsAutomatic()
    {
        // Everything the M74 wave added is absent from a document written before it, and absent has
        // to mean the axes it described: the automatic camera, no alpha mapping, clipped and sorted.
        var figure = new FigureModel();
        figure.AddAxes();

        AxesModel restored = RoundTrip(figure).Axes[0];

        Assert.True(restored.HasAutomaticCamera);
        Assert.Null(restored.CameraPosition);
        Assert.Null(restored.CameraViewAngle);
        Assert.Null(restored.AlphaLimits);
        Assert.Null(restored.Alphamap);
        Assert.Equal(ColorScaleType.Linear, restored.AlphaScale);
        Assert.Equal(SortMethodType.Depth, restored.SortMethod);
        Assert.True(restored.Clipping);
    }

    [Fact]
    public void ASurfaceRoundTripsItsAlphaDataAndTheFlatModeThatDrawsIt()
    {
        var surface = new SurfacePlot(new double[,] { { 1, 2 }, { 3, 4 } })
        {
            AlphaData = new double[,] { { 0, 0.25 }, { 0.5, 1 } },
            FaceAlphaFlat = true,
        };

        var restored = (SurfacePlot)RoundTrip(WithAxes(surface)).Axes[0].Plots[0];

        Assert.NotNull(restored.AlphaData);
        Assert.Equal(0.25, restored.AlphaData![0, 1]);
        Assert.Equal(1, restored.AlphaData[1, 1]);
        Assert.True(restored.FaceAlphaFlat);
    }

    private static FigureModel WithAxes(PlotObject plot)
    {
        AxesModel axes = SingleAxes(plot);
        return (FigureModel)axes.Parent!;
    }
}
