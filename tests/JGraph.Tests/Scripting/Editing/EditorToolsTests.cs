using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>The App Designer window plan's stage W9a: the editor's tools' rules the oracle does not reach - the messages, the label, the store, the clamp.</summary>
public class EditorToolsTests
{
    [Fact]
    public void ARegularExpressionIsRefusedWithR2025bsWords()
    {
        Assert.Null(TextSearch.WhyNotRegex("a+b"));
        Assert.Equal(TextSearch.InvalidRegexMessage, TextSearch.WhyNotRegex("("));
        Assert.Equal(TextSearch.NewLineRegexMessage, TextSearch.WhyNotRegex("a\\nb"));
        Assert.Equal(TextSearch.NewLineRegexMessage, TextSearch.WhyNotRegex("\\r"));
    }

    [Theory]
    [InlineData(-1, 0, "0 / 0")]
    [InlineData(0, 3, "1 / 3")]
    [InlineData(2, 3, "3 / 3")]
    [InlineData(-2, 3, "? / 3")]
    [InlineData(-1, 3, "? / 3")]
    [InlineData(5, 20000, "6 / 19999+")]
    public void TheLabelIsTheStrips(int current, int count, string label) => Assert.Equal(label, TextSearch.Label(current, count));

    [Fact]
    public void WholeWordBreaksAtR2025bsCharacters()
    {
        string[] lines = ["app.Value = app_Value + xapp;", "(app)"];
        SearchResult result = TextSearch.Find(lines, new SearchOptions { Text = "app", WholeWord = true }, wrapAround: true);
        Assert.Equal([new SearchMatch(0, 0, 3, 0), new SearchMatch(1, 1, 4, 0)], result.Matches);
        Assert.Equal(0, result.CurrentIndex);
    }

    [Fact]
    public void ReplaceAllOverLinesKeepsEachLinesOwnText()
    {
        string[] lines = ["x1 = x2;", "y = x10;"];
        IReadOnlyList<SearchMatch> matches = TextSearch.FindAll(lines, new SearchOptions { Text = "x(\\d)", Regex = true });
        Assert.Equal(3, matches.Count);
        Assert.Equal("v1", TextSearch.Replacement(lines[0], matches[0], "x(\\d)", false, "v$1"));
        Assert.Equal("v2", TextSearch.Replacement(lines[0], matches[1], "x(\\d)", false, "v$1"));
        Assert.Equal("v1", TextSearch.Replacement(lines[1], matches[2], "x(\\d)", false, "v$1"));
    }

    [Fact]
    public void TheStoreKeepsEachFilesMarks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgw9a_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BookmarkStore(Path.Combine(folder, BookmarkStore.FileName));
            Assert.Empty(store.Read(@"C:\a\one.m"));
            store.Write(@"C:\a\one.m", [2, 5]);
            store.Write(@"C:\a\two.m", [0]);
            Assert.Equal([2, 5], store.Read(@"C:\a\one.m"));
            Assert.Equal([0], store.Read(@"C:\a\two.m"));
            store.Write(@"C:\a\one.m", []);
            Assert.Empty(store.Read(@"C:\a\one.m"));
            Assert.Equal([0], new BookmarkStore(store.Path).Read(@"C:\a\two.m"));
            string xml = File.ReadAllText(store.Path);
            Assert.Contains("<bookmarks>", xml, StringComparison.Ordinal);
            Assert.Contains("<file absolutepath=\"C:\\a\\two.m\">", xml, StringComparison.Ordinal);
            Assert.Contains("<line number=\"0\" />", xml, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(5, 10, 5)]
    [InlineData(50, 10, 10)]
    [InlineData(3, 0, 1)]
    public void TheLineIsHeldToTheText(long asked, int lines, int line) => Assert.Equal(line, GoToLineRules.Clamp(asked, lines));

    [Fact]
    public void AnEmptyLineTakesNoBookmark()
    {
        var marked = new SortedSet<int>();
        Assert.False(BookmarkRules.Toggle(marked, 1, static line => line == 1));
        Assert.True(BookmarkRules.Toggle(marked, 2, static line => line == 1));
        Assert.Equal([2], marked);
        Assert.Equal(2, BookmarkRules.Next(marked, 4, 0, previous: false, static _ => false));
        Assert.Equal(2, BookmarkRules.Next(marked, 4, 3, previous: false, static _ => false));
        Assert.Equal(2, BookmarkRules.Next(marked, 4, 2, previous: true, static _ => false));
        Assert.Null(BookmarkRules.Next(marked, 4, 2, previous: true, static line => line == 2));
    }
}
