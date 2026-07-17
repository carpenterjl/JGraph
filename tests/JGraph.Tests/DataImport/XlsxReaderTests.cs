using System.Text;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Data.Import;
using Xunit;

namespace JGraph.Tests.DataImport;

public class XlsxReaderTests
{
    [Fact]
    public void Read_SharedAndInlineStrings_AndNumbers()
    {
        MemoryStream stream = new XlsxFixture()
            .Sheet(
                "Data",
                new[] { XCell.Text("name"), XCell.Text("score") },
                new[] { XCell.Text("Alice"), XCell.Number(91) },
                new[] { XCell.Inline("Bob"), XCell.Number(85) })
            .BuildStream();

        ImportResult result = XlsxReader.Read(stream);

        Assert.True(result.HasHeader);
        Assert.Equal(new[] { "name", "score" }, result.Table.ColumnNames);
        Assert.Equal(ColumnType.Text, result.Table["name"].Type);
        Assert.Equal("Alice", ((TextColumn)result.Table["name"]).GetString(0));
        Assert.Equal("Bob", ((TextColumn)result.Table["name"]).GetString(1));
        Assert.Equal(91.0, result.Table["score"].GetNumber(0));
    }

    [Fact]
    public void Read_BuiltinDateFormat_BecomesDateTime()
    {
        var when = new DateTime(2024, 1, 15);
        MemoryStream stream = new XlsxFixture()
            .Sheet(
                "S",
                new[] { XCell.Text("t"), XCell.Text("v") },
                new[] { XCell.Date(when), XCell.Number(1) })
            .BuildStream();

        ImportResult result = XlsxReader.Read(stream);
        Assert.Equal(ColumnType.DateTime, result.Table["t"].Type);
        Assert.Equal(DateTimeAxis.ToValue(when), result.Table["t"].GetNumber(0), 6);
        Assert.Equal(when, ((DateTimeColumn)result.Table["t"]).GetDateTime(0));
    }

    [Fact]
    public void Read_CustomDateFormat_BecomesDateTime()
    {
        var when = new DateTime(2023, 12, 31);
        MemoryStream stream = new XlsxFixture()
            .Sheet(
                "S",
                new[] { XCell.Text("d") },
                new[] { XCell.DateCustom(when) })
            .BuildStream();

        ImportResult result = XlsxReader.Read(stream);
        Assert.Equal(ColumnType.DateTime, result.Table["d"].Type);
        Assert.Equal(when, ((DateTimeColumn)result.Table["d"]).GetDateTime(0));
    }

    [Fact]
    public void Read_BooleanAndErrorCells()
    {
        MemoryStream stream = new XlsxFixture()
            .Sheet(
                "S",
                new[] { XCell.Text("flag"), XCell.Text("bad") },
                new[] { XCell.Bool(true), XCell.Error("#DIV/0!") },
                new[] { XCell.Bool(false), XCell.Number(2) })
            .BuildStream();

        ImportResult result = XlsxReader.Read(stream);
        Assert.Equal(ColumnType.Text, result.Table["flag"].Type);
        Assert.Equal("TRUE", ((TextColumn)result.Table["flag"]).GetString(0));
        Assert.True(result.Table["bad"].IsMissing(0)); // error cell → missing
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Read_GapCells_AreMissing()
    {
        MemoryStream stream = new XlsxFixture()
            .Sheet(
                "S",
                new[] { XCell.Text("a"), XCell.Text("b") },
                new[] { XCell.Number(1), XCell.Empty },
                new[] { XCell.Number(3), XCell.Number(4) })
            .BuildStream();

        ImportResult result = XlsxReader.Read(stream);
        Assert.True(result.Table["b"].IsMissing(0));
        Assert.Equal(4.0, result.Table["b"].GetNumber(1));
    }

    [Fact]
    public void Read_SecondSheetByName()
    {
        MemoryStream stream = new XlsxFixture()
            .Sheet("First", new[] { XCell.Number(1) })
            .Sheet("Second", new[] { XCell.Text("q") }, new[] { XCell.Number(9) })
            .BuildStream();

        ImportResult result = XlsxReader.Read(stream, new ImportOptions { SheetName = "second" });
        Assert.Equal(new[] { "q" }, result.Table.ColumnNames);
        Assert.Equal(9.0, result.Table["q"].GetNumber(0));
    }

    [Fact]
    public void Read_UnknownSheet_Throws()
    {
        MemoryStream stream = new XlsxFixture()
            .Sheet("Only", new[] { XCell.Number(1) })
            .BuildStream();

        var ex = Assert.Throws<ImportException>(() => XlsxReader.Read(stream, new ImportOptions { SheetName = "Ghost" }));
        Assert.Contains("Only", ex.Message);
    }

    [Fact]
    public void GetSheetNames_FromStreamWrapper_ReturnsOrder()
    {
        // Exercises sheet enumeration via a temp file (GetSheetNames takes a path).
        MemoryStream stream = new XlsxFixture()
            .Sheet("Alpha", new[] { XCell.Number(1) })
            .Sheet("Beta", new[] { XCell.Number(2) })
            .BuildStream();

        string path = Path.Combine(Path.GetTempPath(), $"jgraph_xlsx_{Guid.NewGuid():N}.xlsx");
        try
        {
            File.WriteAllBytes(path, stream.ToArray());
            Assert.Equal(new[] { "Alpha", "Beta" }, XlsxReader.GetSheetNames(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_NotAZip_Throws()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is not a workbook"));
        Assert.Throws<ImportException>(() => XlsxReader.Read(stream));
    }
}
