using JGraph.Core.Model;
using JGraph.Data;
using Xunit;

namespace JGraph.Tests.Data;

public class TableTests
{
    [Fact]
    public void Constructor_UnequalColumnLengths_Throws()
    {
        var a = new NumberColumn("a", new[] { 1.0, 2.0 });
        var b = new NumberColumn("b", new[] { 1.0 });
        Assert.Throws<ArgumentException>(() => new Table(new TableColumn[] { a, b }));
    }

    [Fact]
    public void Constructor_DuplicateNames_ThrowsCaseInsensitive()
    {
        var a = new NumberColumn("Value", new[] { 1.0 });
        var b = new NumberColumn("value", new[] { 2.0 });
        Assert.Throws<ArgumentException>(() => new Table(new TableColumn[] { a, b }));
    }

    [Fact]
    public void Indexer_ByName_IsCaseInsensitive()
    {
        var table = new Table(new TableColumn[] { new NumberColumn("Voltage", new[] { 1.0 }) });
        Assert.Same(table["Voltage"], table["voltage"]);
        Assert.True(table.TryGetColumn("VOLTAGE", out _));
    }

    [Fact]
    public void Indexer_MissingName_ThrowsWithAvailableNames()
    {
        var table = new Table(new TableColumn[]
        {
            new NumberColumn("x", new[] { 1.0 }),
            new NumberColumn("y", new[] { 2.0 }),
        });

        var ex = Assert.Throws<KeyNotFoundException>(() => table["z"]);
        Assert.Contains("x", ex.Message);
        Assert.Contains("y", ex.Message);
    }

    [Fact]
    public void RowCountAndColumnNames_ReflectColumns()
    {
        var table = new Table(new TableColumn[]
        {
            new NumberColumn("x", new[] { 1.0, 2.0, 3.0 }),
            new TextColumn("label", new string?[] { "a", "b", "c" }),
        });

        Assert.Equal(3, table.RowCount);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(new[] { "x", "label" }, table.ColumnNames);
    }

    [Fact]
    public void TextColumn_Categories_AreDistinctInFirstAppearanceOrder()
    {
        var column = new TextColumn("g", new string?[] { "b", "a", "b", "c", "a" });
        Assert.Equal(new[] { "b", "a", "c" }, column.Categories);
        Assert.Equal(0, column.GetNumber(0));
        Assert.Equal(1, column.GetNumber(1));
        Assert.Equal(2, column.GetNumber(3));
    }

    [Fact]
    public void TextColumn_MissingValue_IsNaNAndNull()
    {
        var column = new TextColumn("g", new string?[] { "a", null });
        Assert.True(column.IsMissing(1));
        Assert.True(double.IsNaN(column.GetNumber(1)));
        Assert.Null(column.GetString(1));
    }

    [Fact]
    public void DateTimeColumn_RoundTripsThroughOADate()
    {
        var when = new DateTime(2024, 3, 15, 8, 30, 0);
        var column = new DateTimeColumn("t", new[] { when });
        Assert.Equal(ColumnType.DateTime, column.Type);
        Assert.Equal(DateTimeAxis.ToValue(when), column.GetNumber(0), 9);
        Assert.Equal(when, column.GetDateTime(0));
    }

    [Fact]
    public void NumberColumn_NaN_IsMissing()
    {
        var column = new NumberColumn("v", new[] { 1.0, double.NaN, 3.0 });
        Assert.False(column.IsMissing(0));
        Assert.True(column.IsMissing(1));
        Assert.Equal(string.Empty, column.GetText(1));
    }
}
