using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Data.Import;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.DataImport;

public class TablePlotBuilderTests
{
    private static Table NumericTable() => DelimitedTextReader.Parse("x,y,z\n1,2,3\n4,5,6").Table;

    private static AxesModel NewAxes() => new FigureModel().AddAxes();

    [Fact]
    public void Build_MultipleYColumns_CreatesPlotsWithNamesAndLegend()
    {
        AxesModel axes = NewAxes();
        var spec = new TablePlotSpec(NumericTable(), ImportPlotKind.Line, "x", new[] { "y", "z" });

        IReadOnlyList<PlotObject> plots = TablePlotBuilder.Build(axes, spec);

        Assert.Equal(2, plots.Count);
        Assert.All(plots, p => Assert.IsType<LinePlot>(p));
        Assert.Equal("y", plots[0].DisplayName);
        Assert.Equal("z", plots[1].DisplayName);
        Assert.True(axes.Legend.Visible);
    }

    [Fact]
    public void Build_DateTimeXColumn_SetsDateScale()
    {
        Table table = DelimitedTextReader.Parse("t,v\n2024-01-01,1\n2024-01-02,2").Table;
        AxesModel axes = NewAxes();
        TablePlotBuilder.Build(axes, new TablePlotSpec(table, ImportPlotKind.Line, "t", new[] { "v" }));

        Assert.Equal(AxisScaleType.DateTime, axes.PrimaryXAxis.Scale);
    }

    [Fact]
    public void Build_TextXColumn_SetsCategoryScale()
    {
        Table table = DelimitedTextReader.Parse("g,v\napple,1\nbanana,2\napple,3").Table;
        AxesModel axes = NewAxes();
        TablePlotBuilder.Build(axes, new TablePlotSpec(table, ImportPlotKind.Scatter, "g", new[] { "v" }));

        Assert.Equal(AxisScaleType.Category, axes.PrimaryXAxis.Scale);
        Assert.Equal(new[] { "apple", "banana" }, axes.PrimaryXAxis.Categories);
    }

    [Fact]
    public void Build_Bar_CreatesBarPlot()
    {
        Table table = DelimitedTextReader.Parse("name,value\na,10\nb,20").Table;
        AxesModel axes = NewAxes();
        IReadOnlyList<PlotObject> plots = TablePlotBuilder.Build(
            axes, new TablePlotSpec(table, ImportPlotKind.Bar, "name", new[] { "value" }));

        Assert.Single(plots);
        Assert.IsType<BarPlot>(plots[0]);
        Assert.Equal(AxisScaleType.Category, axes.PrimaryXAxis.Scale);
    }

    [Fact]
    public void Build_Stem_CreatesStemPlot()
    {
        AxesModel axes = NewAxes();
        IReadOnlyList<PlotObject> plots = TablePlotBuilder.Build(
            axes, new TablePlotSpec(NumericTable(), ImportPlotKind.Stem, "x", new[] { "y" }));
        Assert.IsType<StemPlot>(plots[0]);
    }

    [Fact]
    public void Build_Histogram_CreatesHistogram()
    {
        AxesModel axes = NewAxes();
        IReadOnlyList<PlotObject> plots = TablePlotBuilder.Build(
            axes, new TablePlotSpec(NumericTable(), ImportPlotKind.Histogram, null, new[] { "y" }, HistogramBins: 5));
        var histogram = Assert.IsType<HistogramPlot>(plots[0]);
        Assert.Equal(5, histogram.BinCount);
    }

    [Fact]
    public void Build_ErrorBar_CreatesErrorBar()
    {
        AxesModel axes = NewAxes();
        IReadOnlyList<PlotObject> plots = TablePlotBuilder.Build(
            axes, new TablePlotSpec(NumericTable(), ImportPlotKind.ErrorBar, "x", new[] { "y" }, ErrorColumn: "z"));
        Assert.IsType<ErrorBarPlot>(plots[0]);
    }

    [Fact]
    public void Build_ErrorBar_WithoutErrorColumn_Throws()
    {
        AxesModel axes = NewAxes();
        Assert.Throws<ArgumentException>(() => TablePlotBuilder.Build(
            axes, new TablePlotSpec(NumericTable(), ImportPlotKind.ErrorBar, "x", new[] { "y" })));
    }

    [Fact]
    public void Build_SetsAxisLabelsFromColumnNames_OnlyWhenEmpty()
    {
        AxesModel axes = NewAxes();
        axes.PrimaryXAxis.Label = "Preset";
        TablePlotBuilder.Build(axes, new TablePlotSpec(NumericTable(), ImportPlotKind.Line, "x", new[] { "y" }));

        Assert.Equal("Preset", axes.PrimaryXAxis.Label); // not overwritten
        Assert.Equal("y", axes.PrimaryYAxis.Label); // set because it was empty
    }
}
