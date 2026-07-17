using JGraph.Core.Data;
using JGraph.Data;
using Xunit;

namespace JGraph.Tests.Data;

public class TableSeriesTests
{
    private static Table SampleTable() => new(new TableColumn[]
    {
        new NumberColumn("x", new[] { 10.0, 20.0, 30.0 }),
        new NumberColumn("y", new[] { 1.0, 2.0, 3.0 }),
        new TextColumn("g", new string?[] { "a", "b", "a" }),
    });

    [Fact]
    public void Create_NumberColumns_IsZeroCopy()
    {
        Table table = SampleTable();
        IDataSeries series = TableSeries.Create(table, "x", "y");

        Assert.True(series.TryGetSpans(out ReadOnlySpan<double> xs, out ReadOnlySpan<double> ys));
        Assert.Equal(10.0, xs[0]);
        Assert.Equal(3.0, ys[2]);

        // The Y span must be the column's own backing storage (no copy).
        Assert.True(ys == ((NumberColumn)table["y"]).Values);
    }

    [Fact]
    public void Create_NullXColumn_UsesRowIndices()
    {
        IDataSeries series = TableSeries.Create(SampleTable(), null, "y");
        Assert.Equal(0.0, series.GetX(0));
        Assert.Equal(1.0, series.GetX(1));
        Assert.Equal(2.0, series.GetX(2));
    }

    [Fact]
    public void Create_TextXColumn_ProducesCategoryIndices()
    {
        IDataSeries series = TableSeries.Create(SampleTable(), "g", "y");
        Assert.Equal(0.0, series.GetX(0)); // "a"
        Assert.Equal(1.0, series.GetX(1)); // "b"
        Assert.Equal(0.0, series.GetX(2)); // "a" again
    }

    [Fact]
    public void GetNumbers_MissingValues_PreservedAsNaN()
    {
        var table = new Table(new TableColumn[]
        {
            new NumberColumn("v", new[] { 1.0, double.NaN, 3.0 }),
        });

        double[] numbers = TableSeries.GetNumbers(table, "v");
        Assert.True(double.IsNaN(numbers[1]));
    }
}
