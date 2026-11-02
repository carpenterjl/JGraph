using System.Text.Json;
using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>
/// The App Designer window plan's stage W9b: Comment, Uncomment and Wrap Comments against R2025b's own <c>CommentCode</c>, <c>WrapComments</c>
/// and its sections, run by <c>ui-probes/w9/oracle/comment.js</c> (CommentOracle.json).
/// </summary>
public class CommentOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Editing", "CommentOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static List<string> Words(JsonElement array) => array.EnumerateArray().Select(static e => e.GetString()!).ToList();

    [Fact]
    public void CommentAndUncommentAreCommentCodes()
    {
        foreach (JsonElement run in Oracle("comments").EnumerateArray())
        {
            List<string> lines = Words(run.GetProperty("lines"));
            int first = run.GetProperty("startLine").GetInt32();
            int last = run.GetProperty("endLine").GetInt32();
            Assert.Equal(run.GetProperty("level").GetInt32(), CommentRules.CommentLevel(lines, first, last));
            List<string> commented = [.. lines];
            CommentRules.Comment(commented, first, last);
            Assert.Equal(Words(run.GetProperty("commented")), commented);
            List<string> uncommented = [.. lines];
            CommentRules.Uncomment(uncommented, first, last);
            Assert.Equal(Words(run.GetProperty("uncommented")), uncommented);
        }
    }

    [Fact]
    public void WrapCommentsIsR2025bsSections()
    {
        int wrapped = 0;
        foreach (JsonElement run in Oracle("wraps").EnumerateArray())
        {
            if (run.GetProperty("error").ValueKind != JsonValueKind.Null)
            {
                continue;
            }

            List<string> lines = Words(run.GetProperty("lines"));
            int caret = run.GetProperty("caretLine").GetInt32();
            JsonElement selection = run.GetProperty("selection");
            int first = -1;
            int last = -1;
            if (selection.ValueKind == JsonValueKind.Array)
            {
                first = selection[0].GetInt32();
                last = selection[1].GetInt32();
            }

            int sections = WrapComments.Wrap(lines, caret, first, last, run.GetProperty("maxLength").GetInt32(), run.GetProperty("fromCommentStart").GetBoolean());
            Assert.Equal(run.GetProperty("sections").GetInt32(), sections);
            Assert.Equal(Words(run.GetProperty("wrapped")), lines);
            wrapped++;
        }

        Assert.True(wrapped > 450);
    }
}
