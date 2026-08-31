using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The text verbs that had only ever been handed one piece of text (M121): <c>split</c>,
/// <c>regexp</c>, <c>regexprep</c>, <c>extractBetween</c> and <c>strfind</c> over a container, the
/// pattern list every search-and-edit verb accepts, and <c>join</c> along a dimension.
/// </summary>
/// <remarks>
/// Every expectation is R2024a's own, measured rather than reasoned about — including the two the
/// documentation does not settle: <c>replace</c> applies its patterns in one pass while
/// <c>regexprep</c> applies them one after another, and the two disagree on the same inputs.
/// </remarks>
[Collection("JG facade")]
public class MatlabTextArrayM121Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabTextArrayM121Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>
    /// One script, and only what it printed. The sink is replaced each time rather than read from
    /// the end, because it accumulates across runs and a test that read the whole of it would
    /// silently pass on the previous script's output.
    /// </summary>
    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    /// <summary>The class and shape of an expression, which is most of what changed here.</summary>
    private string Shape(string setup, string expression) => Run(
        $"{setup}\nv = {expression};\nfprintf('%s %d %d', class(v), size(v, 1), size(v, 2));");

    private const string Two = "sarr = [\"a,b\"; \"c,d\"]; carr = {'a,b', 'c,d'};";

    [Fact]
    public void SplitOverAColumnSpreadsThePiecesAcrossTheColumns()
    {
        Assert.Equal("string 2 2", Shape(Two, "split(sarr, \",\")"));
        Assert.Equal("a c b d", Run($"{Two}\nv = split(sarr, \",\");\nfprintf('%s %s %s %s', v(1,1), v(2,1), v(1,2), v(2,2));"));
    }

    [Fact]
    public void SplitWithNoDelimiterBreaksOnWhitespace()
    {
        Assert.Equal("string 2 2", Shape("s = [\"a b\"; \"c d\"];", "split(s)"));
    }

    [Fact]
    public void TheRegularExpressionVerbsTakeAContainerAndAnswerOneShapedLikeIt()
    {
        Assert.Equal("string 2 1", Shape(Two, "regexprep(sarr, \"a\", \"z\")"));
        Assert.Equal("cell 1 2", Shape(Two, "regexprep(carr, \"a\", \"z\")"));
        Assert.Equal("cell 2 1", Shape(Two, "regexp(sarr, '[a-z]', 'match')"));
        Assert.Equal("cell 1 2", Shape(Two, "regexp(carr, '[a-z]', 'match')"));
        Assert.Equal("string 2 1", Shape(Two, "extractBetween(sarr, 1, 1)"));
        Assert.Equal("cell 1 2", Shape(Two, "extractBetween(carr, 1, 1)"));
        Assert.Equal("cell 2 1", Shape(Two, "strfind(sarr, \"a\")"));

        Assert.Equal("z,b c,d", Run($"{Two}\nv = regexprep(sarr, \"a\", \"z\");\nfprintf('%s %s', v(1), v(2));"));
        Assert.Equal("a c", Run($"{Two}\nv = extractBetween(sarr, 1, 1);\nfprintf('%s %s', v(1), v(2));"));
    }

    [Fact]
    public void OnePieceOfTextStillAnswersWhatItAlwaysDid()
    {
        // The scalar road has to stay exactly where it was for a char row: split of one gives a
        // cell, which is R2024a's answer and was not this repository's before M121 — it gave a bare
        // array of strings whose class read as 'double'.
        Assert.Equal("cell 2 1", Shape(string.Empty, "split('a,b', ',')"));
        Assert.Equal("string 2 1", Shape(string.Empty, "split(\"a,b\", \",\")"));
        Assert.Equal("cell 1 3", Shape(string.Empty, "regexp('abc', '[a-z]', 'match')"));
        Assert.Equal("double 1 2", Shape(string.Empty, "strfind('abcabc', 'b')"));
    }

    [Fact]
    public void ACharMatrixIsStillRefusedByEveryOneOfThem()
    {
        // MATLAB refuses a char matrix to all five, so mapping over its rows would have invented an
        // answer. Checked by name, because "it threw" is not the same as "it threw for this reason".
        foreach (string call in new[]
                 {
                     "split(cm, 'b')", "regexprep(cm, 'a', 'z')", "regexp(cm, '[a-z]', 'match')",
                     "extractBetween(cm, 1, 1)", "strfind(cm, 'a')",
                 })
        {
            _output = new RecordingScriptOutput();
            var context = new ScriptContext(_output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                $"cm = ['ab'; 'cd'];\nv = {call};", context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            Assert.False(result.Success, $"{call} was accepted");
        }
    }

    [Fact]
    public void AnEmptyContainerAnswersAnEmptyOneOfTheSameKind()
    {
        Assert.Equal("string 0 0", Shape("e = strings(0, 0);", "regexprep(e, \"a\", \"z\")"));
        Assert.Equal("string 0 0", Shape("e = strings(0, 0);", "split(e, \",\")"));
        Assert.Equal("cell 0 0", Shape("e = strings(0, 0);", "strfind(e, \"a\")"));
    }

    [Fact]
    public void EverySearchVerbTakesAListOfPatterns()
    {
        // Each does its own thing with the list, and each was measured: contains asks whether any
        // matched, count adds them up, erase takes them all out.
        Assert.Equal("1", Run("fprintf('%d', contains(\"abc\", [\"a\"; \"z\"]));"));
        Assert.Equal("0", Run("fprintf('%d', contains(\"abc\", [\"y\"; \"z\"]));"));
        Assert.Equal("1", Run("fprintf('%d', startsWith(\"abc\", [\"z\"; \"a\"]));"));
        Assert.Equal("1", Run("fprintf('%d', endsWith(\"abc\", [\"z\"; \"c\"]));"));
        Assert.Equal("4", Run("fprintf('%d', count(\"abcabc\", [\"a\"; \"b\"]));"));
        Assert.Equal("c", Run("fprintf('%s', erase(\"abc\", [\"a\"; \"b\"]));"));
        Assert.Equal("1 0", Run($"{Two}\nv = contains(sarr, [\"a\"; \"z\"]);\nfprintf('%d %d', v(1), v(2));"));
    }

    [Fact]
    public void ReplaceActsInOnePassWhereRegexprepActsInSeveral()
    {
        // The one place the two verbs genuinely disagree, and the reason they cannot share a body.
        // replace swaps a and b simultaneously; regexprep turns a into b and then that b into c.
        Assert.Equal("ba", Run("fprintf('%s', replace(\"ab\", [\"a\"; \"b\"], [\"b\"; \"a\"]));"));
        Assert.Equal("b", Run("fprintf('%s', replace(\"a\", [\"a\"; \"b\"], [\"b\"; \"c\"]));"));
        Assert.Equal("c", Run("fprintf('%s', regexprep(\"a\", [\"a\"; \"b\"], [\"b\"; \"c\"]));"));
        Assert.Equal("zbz", Run("fprintf('%s', replace(\"abc\", [\"a\"; \"c\"], \"z\"));"));
    }

    [Fact]
    public void JoinRunsOneDimensionTogetherAndTheCallerMayNameWhichOne()
    {
        const string Grid = "g = [\"a\" \"b\" \"c\"; \"d\" \"e\" \"f\"];";

        // The default is the last dimension that is not a singleton.
        Assert.Equal("string 2 1", Shape(Grid, "join(g)"));
        Assert.Equal("string 1 3", Shape(Grid, "join(g, 1)"));
        Assert.Equal("a b c d e f", Run($"{Grid}\nv = join(g);\nfprintf('%s %s', v(1), v(2));"));
        Assert.Equal("a|d b|e c|f", Run($"{Grid}\nv = join(g, \"|\", 1);\nfprintf('%s %s %s', v(1), v(2), v(3));"));

        // A column of text stays a column: joining along a singleton dimension joins nothing. This
        // is the case the head-to-head text script carries a comment about working around.
        Assert.Equal("string 2 1", Shape("s = [\"a,b\"; \"c,d\"];", "join(s, 2)"));
        Assert.Equal("cell 1 1", Shape("c = {'a', 'b'};", "join(c, \"-\")"));
    }

    [Fact]
    public void TheJoinDelimiterExpandsOverTheGapsItHasToFill()
    {
        const string Grid = "g = [\"a\" \"b\" \"c\"; \"d\" \"e\" \"f\"];";

        // A column of delimiters gives each row its own; a row of them gives each gap its own.
        Assert.Equal("a|b|c d-e-f", Run($"{Grid}\nv = join(g, [\"|\"; \"-\"]);\nfprintf('%s %s', v(1), v(2));"));
        Assert.Equal("a|b-c d|e-f", Run($"{Grid}\nv = join(g, [\"|\" \"-\"]);\nfprintf('%s %s', v(1), v(2));"));
    }

    [Fact]
    public void ABracketOfStringArraysStandsThemSideBySideRatherThanFlatteningThem()
    {
        // [s s] where s is 2-by-1 is 2-by-2, and was the 1-by-4 that running every piece into one
        // list makes of it. The function spellings must agree with the bracket, which is why they
        // now share its machinery.
        Assert.Equal("string 2 2", Shape("s = [\"a\"; \"b\"];", "[s s]"));
        Assert.Equal("string 2 2", Shape("s = [\"a\"; \"b\"];", "horzcat(s, s)"));
        Assert.Equal("string 4 1", Shape("s = [\"a\"; \"b\"];", "vertcat(s, s)"));
        Assert.Equal("string 2 2", Shape("s = [\"a\"; \"b\"];", "cat(2, s, s)"));
        Assert.Equal("string 4 1", Shape("s = [\"a\"; \"b\"];", "cat(1, s, s)"));

        // And the one-row cases it must not have changed.
        Assert.Equal("string 1 2", Shape(string.Empty, "[\"a\" \"b\"]"));
        Assert.Equal("string 1 3", Shape(string.Empty, "[\"a\" 'bc' 3]"));
    }
}
