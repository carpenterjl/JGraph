using System.Text.Json;
using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>
/// The App Designer window plan's stage W9a: the editor's Find and Replace, its history and seed, bookmarks and Go To's line, against R2025b's
/// own <c>FindUtilities</c>, <c>FindReplaceActions</c>, <c>FindPreference</c>, <c>Bookmarks</c> and <c>GotoDialog</c>, run by
/// <c>ui-probes/w9/oracle/find.js</c> (EditorToolsOracle.json).
/// </summary>
public class EditorToolsOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Editing", "EditorToolsOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static string[] Words(JsonElement array) => array.EnumerateArray().Select(static e => e.GetString()!).ToArray();

    private static SearchOptions OptionsOf(JsonElement search, bool? backward = null)
    {
        JsonElement options = search.GetProperty("options");
        bool inSelection = options.TryGetProperty("findInSelection", out JsonElement held) && held.GetBoolean();
        return new SearchOptions
        {
            Text = search.GetProperty("text").GetString()!,
            MatchCase = options.GetProperty("caseSensitive").GetBoolean(),
            WholeWord = options.GetProperty("wholeWords").GetBoolean(),
            Regex = options.GetProperty("regularExpression").GetBoolean(),
            Backward = backward ?? options.GetProperty("backward").GetBoolean(),
            StartLine = options.GetProperty("startAt").GetProperty("line").GetInt32(),
            StartColumn = options.GetProperty("startAt").GetProperty("column").GetInt32(),
            SelectionFirstLine = inSelection ? options.GetProperty("findInSelectionStartLine").GetInt32() : -1,
            SelectionLastLine = inSelection ? options.GetProperty("findInSelectionEndLine").GetInt32() : -1,
        };
    }

    private static SearchMatch MatchOf(JsonElement m) =>
        new(m.GetProperty("line").GetInt32(), m.GetProperty("start").GetInt32(), m.GetProperty("end").GetInt32(), m.GetProperty("searchIndex").GetInt32());

    [Fact]
    public void EveryOccurrenceAndTheCurrentOneAreFindUtilities()
    {
        int compared = 0;
        foreach (JsonElement search in Oracle("searches").EnumerateArray())
        {
            string[] lines = Words(search.GetProperty("lines"));
            SearchOptions options = OptionsOf(search);
            if (search.TryGetProperty("error", out _))
            {
                Assert.True(options.Regex);
                Assert.NotNull(TextSearch.WhyNotRegex(options.Text));
                continue;
            }

            SearchResult result = TextSearch.Find(lines, options, search.GetProperty("wrap").GetBoolean());
            SearchMatch[] expected = search.GetProperty("found").EnumerateArray().Select(MatchOf).ToArray();
            Assert.Equal(expected, result.Matches);
            Assert.Equal(search.GetProperty("current").GetInt32(), result.CurrentIndex);
            compared++;
            if (search.TryGetProperty("steps", out JsonElement steps))
            {
                foreach (JsonElement step in steps.EnumerateArray())
                {
                    SearchResult next = TextSearch.Find(lines, OptionsOf(search, step.GetProperty("backward").GetBoolean()), step.GetProperty("wrap").GetBoolean(), result);
                    Assert.Equal(expected, next.Matches);
                    Assert.Equal(step.GetProperty("current").GetInt32(), next.CurrentIndex);
                }
            }
        }

        Assert.True(compared > 600);
    }

    [Fact]
    public void ARegularExpressionsReplacementIsProcessCaptureGroups()
    {
        foreach (JsonElement replace in Oracle("replaces").EnumerateArray())
        {
            string got = TextSearch.Replacement(
                replace.GetProperty("line").GetString()!,
                MatchOf(replace.GetProperty("occurrence")),
                replace.GetProperty("pattern").GetString()!,
                replace.GetProperty("caseSensitive").GetBoolean(),
                replace.GetProperty("replaceWith").GetString()!);
            Assert.Equal(replace.GetProperty("text").GetString(), got);
        }
    }

    [Fact]
    public void TheHistoryIsFindPreferences()
    {
        foreach (JsonElement run in Oracle("histories").EnumerateArray())
        {
            var history = new FindHistory();
            foreach (JsonElement step in run.EnumerateArray())
            {
                history.Add(step.GetProperty("text").GetString()!);
                Assert.Equal(Words(step.GetProperty("history")), history.Texts);
                Assert.Equal(step.GetProperty("latest").GetString(), history.Latest);
                Assert.Equal(Words(step.GetProperty("menu")), history.MenuItems);
            }
        }
    }

    [Fact]
    public void TheSeedIsTheFirstNonBlankLine()
    {
        foreach (JsonElement seed in Oracle("seeds").EnumerateArray())
        {
            Assert.Equal(seed.GetProperty("seed").GetString(), TextSearch.FirstNonBlankLine(seed.GetProperty("selected").GetString()));
        }
    }

    [Fact]
    public void BookmarksToggleAndJumpAsR2025bDoes()
    {
        foreach (JsonElement run in Oracle("bookmarks").EnumerateArray())
        {
            bool[] blank = run.GetProperty("blank").EnumerateArray().Select(static b => b.GetBoolean()).ToArray();
            int cursor = run.GetProperty("start").GetInt32();
            var marked = new SortedSet<int>();
            foreach (JsonElement op in run.GetProperty("ops").EnumerateArray())
            {
                switch (op.GetProperty("op").GetString())
                {
                    case "toggle":
                        BookmarkRules.Toggle(marked, op.GetProperty("at").GetInt32(), line => blank[line]);
                        Assert.Equal(op.GetProperty("marked").EnumerateArray().Select(static m => m.GetInt32()).ToArray(), marked);
                        break;
                    case "move":
                        cursor = op.GetProperty("at").GetInt32();
                        break;
                    case "next":
                    case "previous":
                        int? to = BookmarkRules.Next(marked, blank.Length, cursor, op.GetProperty("op").GetString() == "previous", line => blank[line]);
                        JsonElement expected = op.GetProperty("to");
                        Assert.Equal(expected.ValueKind == JsonValueKind.Null ? null : expected.GetInt32(), to);
                        if (to is { } moved)
                        {
                            cursor = moved;
                        }

                        break;
                }
            }
        }
    }

    [Fact]
    public void GoToTakesWhatNumberReadsAsAWholeLine()
    {
        foreach (JsonElement line in Oracle("gotoLines").EnumerateArray())
        {
            string text = line.GetProperty("text").GetString()!;
            Assert.Equal(line.GetProperty("valid").GetBoolean(), GoToLineRules.IsValid(text));
            if (line.GetProperty("valid").GetBoolean())
            {
                Assert.Equal(line.GetProperty("value").GetInt64(), GoToLineRules.Parse(text));
            }
        }
    }
}
