using JGraph.Data;
using Xunit;

namespace JGraph.Tests.Data;

/// <summary>The Data Viewer's UI-free grid projection: headers, cell text, and row-window paging.</summary>
public class TableGridAdapterTests
{
    [Fact]
    public void ForTable_ExposesHeadersAndCellText()
    {
        Table table = Table.Parse("x,y\n1,10\n2,20\n3,30");
        TableGridAdapter adapter = TableGridAdapter.ForTable(table);

        Assert.Equal(new[] { "x", "y" }, adapter.ColumnNames);
        Assert.Equal(3, adapter.RowCount);
        Assert.Equal(1, adapter.PageCount);
        Assert.Equal("20", adapter.GetText(1, 1));
        Assert.Contains("3×2", adapter.Title);
    }

    [Fact]
    public void ForArray_ShowsIndexValuePairs()
    {
        TableGridAdapter adapter = TableGridAdapter.ForArray(new[] { 1.5, 2.5 });

        Assert.Equal(new[] { "Index", "Value" }, adapter.ColumnNames);
        Assert.Equal(2, adapter.RowCount);
        Assert.Equal("0", adapter.GetText(0, 0));
        Assert.Equal("2.5", adapter.GetText(1, 1));
    }

    [Fact]
    public void GetPage_ReturnsTheRowWindow()
    {
        Table table = Table.Parse("v\n10\n20\n30");
        TableGridAdapter adapter = TableGridAdapter.ForTable(table);

        IReadOnlyList<string[]> page = adapter.GetPage(0, out int firstRow);
        Assert.Equal(0, firstRow);
        Assert.Equal(3, page.Count);
        Assert.Equal("30", page[2][0]);
    }

    [Fact]
    public void Paging_SplitsLargeData_AndClampsOutOfRangePages()
    {
        var values = new double[TableGridAdapter.PageSize * 2 + 500];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i;
        }

        TableGridAdapter adapter = TableGridAdapter.ForArray(values);
        Assert.Equal(3, adapter.PageCount);

        Assert.Equal(TableGridAdapter.PageSize, adapter.GetPage(0, out int first0).Count);
        Assert.Equal(0, first0);

        IReadOnlyList<string[]> middle = adapter.GetPage(1, out int first1);
        Assert.Equal(TableGridAdapter.PageSize, first1);
        Assert.Equal(first1.ToString(System.Globalization.CultureInfo.InvariantCulture), middle[0][1]);

        Assert.Equal(500, adapter.GetPage(2, out _).Count);

        // Out-of-range pages clamp instead of throwing.
        adapter.GetPage(99, out int clamped);
        Assert.Equal(TableGridAdapter.PageSize * 2, clamped);
    }

    [Fact]
    public void GetText_OutOfRange_Throws()
    {
        TableGridAdapter adapter = TableGridAdapter.ForArray(new[] { 1.0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.GetText(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.GetText(0, 2));
    }
}
