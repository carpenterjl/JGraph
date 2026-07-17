using System.Globalization;
using JGraph.Data;
using JGraph.Data.Import;
using Xunit;

namespace JGraph.Tests.DataImport;

public class DelimitedTextReaderTests
{
    [Fact]
    public void Parse_QuotedFieldWithEmbeddedDelimiter_KeptWhole()
    {
        ImportResult result = DelimitedTextReader.Parse("name,value\n\"Smith, John\",42");
        Assert.Equal("Smith, John", ((TextColumn)result.Table["name"]).GetString(0));
        Assert.Equal(42.0, result.Table["value"].GetNumber(0));
    }

    [Fact]
    public void Parse_DoubledQuoteEscape_ProducesSingleQuote()
    {
        ImportResult result = DelimitedTextReader.Parse("text\n\"a \"\"quoted\"\" word\"", new ImportOptions { HasHeader = true });
        Assert.Equal("a \"quoted\" word", ((TextColumn)result.Table["text"]).GetString(0));
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedNewline_KeptWhole()
    {
        ImportResult result = DelimitedTextReader.Parse("a,b\n\"line1\nline2\",2");
        Assert.Equal("line1\nline2", ((TextColumn)result.Table["a"]).GetString(0));
    }

    [Fact]
    public void Parse_StripsByteOrderMark()
    {
        ImportResult result = DelimitedTextReader.Parse("﻿x,y\n1,2");
        Assert.Equal(new[] { "x", "y" }, result.Table.ColumnNames);
    }

    [Theory]
    [InlineData("x,y\n1,2", ',')]
    [InlineData("x;y\n1;2", ';')]
    [InlineData("x\ty\n1\t2", '\t')]
    [InlineData("x|y\n1|2", '|')]
    public void Parse_DetectsDelimiter(string text, char expected)
    {
        ImportResult result = DelimitedTextReader.Parse(text);
        Assert.Equal(expected, result.Delimiter);
        Assert.Equal(2, result.Table.ColumnCount);
    }

    [Fact]
    public void Parse_ExplicitDelimiter_Overrides()
    {
        ImportResult result = DelimitedTextReader.Parse("a;b,c\n1;2,3", new ImportOptions { Delimiter = ';' });
        Assert.Equal(';', result.Delimiter);
        Assert.Equal(2, result.Table.ColumnCount);
    }

    [Fact]
    public void Parse_DetectsHeader_WhenLabelsOverNumbers()
    {
        ImportResult result = DelimitedTextReader.Parse("time,value\n1,10\n2,20");
        Assert.True(result.HasHeader);
        Assert.Equal(new[] { "time", "value" }, result.Table.ColumnNames);
        Assert.Equal(2, result.Table.RowCount);
    }

    [Fact]
    public void Parse_NoHeader_WhenFirstRowIsNumeric()
    {
        ImportResult result = DelimitedTextReader.Parse("1,10\n2,20\n3,30");
        Assert.False(result.HasHeader);
        Assert.Equal(new[] { "Column1", "Column2" }, result.Table.ColumnNames);
        Assert.Equal(3, result.Table.RowCount);
    }

    [Fact]
    public void Parse_HasHeaderOverride_False_KeepsFirstRowAsData()
    {
        ImportResult result = DelimitedTextReader.Parse("a,b\n1,2", new ImportOptions { HasHeader = false });
        Assert.False(result.HasHeader);
        Assert.Equal(2, result.Table.RowCount);
    }

    [Fact]
    public void Parse_DuplicateHeaderNames_AreDisambiguated()
    {
        ImportResult result = DelimitedTextReader.Parse("v,v\n1,2", new ImportOptions { HasHeader = true });
        Assert.Equal(new[] { "v", "v_2" }, result.Table.ColumnNames);
    }

    [Fact]
    public void Parse_IntegerColumn_StaysNumberNotDate()
    {
        ImportResult result = DelimitedTextReader.Parse("n\n1\n2\n3", new ImportOptions { HasHeader = true });
        Assert.Equal(ColumnType.Number, result.Table["n"].Type);
    }

    [Fact]
    public void Parse_IsoDateColumn_BecomesDateTime()
    {
        ImportResult result = DelimitedTextReader.Parse(
            "t,v\n2024-01-15,1\n2024-01-16,2",
            new ImportOptions { Culture = CultureInfo.InvariantCulture });
        Assert.Equal(ColumnType.DateTime, result.Table["t"].Type);
    }

    [Fact]
    public void Parse_MixedColumn_BecomesText()
    {
        ImportResult result = DelimitedTextReader.Parse("v\n1\napple\n3", new ImportOptions { HasHeader = true });
        Assert.Equal(ColumnType.Text, result.Table["v"].Type);
    }

    [Fact]
    public void Parse_EmptyCells_AreMissing()
    {
        ImportResult result = DelimitedTextReader.Parse("a,b\n1,\n,2");
        Assert.True(result.Table["a"].IsMissing(1));
        Assert.True(result.Table["b"].IsMissing(0));
    }

    [Fact]
    public void Parse_DecimalCommaWithSemicolon_AutoDetectsCulture()
    {
        ImportResult result = DelimitedTextReader.Parse("x;y\n1,5;2,5\n3,5;4,5");
        Assert.Equal(';', result.Delimiter);
        Assert.Equal(1.5, result.Table["x"].GetNumber(0), 9);
        Assert.Equal(4.5, result.Table["y"].GetNumber(1), 9);
    }

    [Fact]
    public void Parse_ExplicitCulture_Overrides()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        ImportResult result = DelimitedTextReader.Parse("x;y\n1,5;2,5", new ImportOptions { Culture = german });
        Assert.Equal(1.5, result.Table["x"].GetNumber(0), 9);
    }

    [Fact]
    public void Parse_SkipRows_DiscardsLeadingLines()
    {
        ImportResult result = DelimitedTextReader.Parse("# comment\n# another\nx,y\n1,2", new ImportOptions { SkipRows = 2 });
        Assert.Equal(new[] { "x", "y" }, result.Table.ColumnNames);
        Assert.Equal(1, result.Table.RowCount);
    }

    [Fact]
    public void Parse_RaggedRow_IsPaddedWithWarning()
    {
        ImportResult result = DelimitedTextReader.Parse("a,b,c\n1,2,3\n4,5");
        Assert.True(result.Table["c"].IsMissing(1));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Parse_BlankLines_AreSkipped()
    {
        ImportResult result = DelimitedTextReader.Parse("x,y\n1,2\n\n3,4\n");
        Assert.Equal(2, result.Table.RowCount);
    }

    [Fact]
    public void Parse_EmptyInput_Throws()
    {
        Assert.Throws<ImportException>(() => DelimitedTextReader.Parse(""));
    }

    [Fact]
    public void Parse_SingleColumn_NoDelimiter()
    {
        ImportResult result = DelimitedTextReader.Parse("value\n1\n2\n3");
        Assert.Equal(1, result.Table.ColumnCount);
        Assert.Equal(ColumnType.Number, result.Table["value"].Type);
    }

    [Fact]
    public void Read_MissingFile_Throws()
    {
        Assert.Throws<ImportException>(() => DelimitedTextReader.Read("Z:/does/not/exist_42.csv"));
    }
}
