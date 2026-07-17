using JGraph.Data.Import;
using Xunit;

namespace JGraph.Tests.DataImport;

public class ClipboardTableParserTests
{
    [Fact]
    public void Parse_TabDelimitedExcelPaste_UsesTab()
    {
        ImportResult result = ClipboardTableParser.Parse("x\ty\n1\t2\n3\t4");
        Assert.Equal('\t', result.Delimiter);
        Assert.Equal(2, result.Table.ColumnCount);
        Assert.Equal(2, result.Table.RowCount);
    }

    [Fact]
    public void Parse_NonTabText_FallsBackToAutoDetect()
    {
        ImportResult result = ClipboardTableParser.Parse("x,y\n1,2");
        Assert.Equal(',', result.Delimiter);
    }

    [Theory]
    [InlineData("a\tb\t c", true)]
    [InlineData("x,y\n1,2", true)]
    [InlineData("just some prose", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeTable_Heuristic(string? text, bool expected)
    {
        Assert.Equal(expected, ClipboardTableParser.LooksLikeTable(text));
    }
}
