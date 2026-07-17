using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: the string builtins (sprintf, str/num conversion, split/join, case, search).</summary>
[Collection("JG facade")]
public class JgsStringBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsStringBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task<string> Eval(string expression)
    {
        _output.Normal.Clear();
        ScriptRunResult result = await Run($"print({expression})");
        Assert.True(result.Success, result.Message);
        return _output.NormalText.Trim();
    }

    [Theory]
    [InlineData("sprintf(\"%d items\", 3)", "3 items")]
    [InlineData("sprintf(\"%i\", 2.6)", "3")]
    [InlineData("sprintf(\"%.2f\", 3.14159)", "3.14")]
    [InlineData("sprintf(\"%f\", 1.5)", "1.500000")]
    [InlineData("sprintf(\"|%5d|\", 42)", "|   42|")]
    [InlineData("sprintf(\"%-5d|\", 42)", "42   |")]
    [InlineData("sprintf(\"%05d\", 42)", "00042")]
    [InlineData("sprintf(\"%05d\", -42)", "-0042")]
    [InlineData("sprintf(\"%.2e\", 12345.0)", "1.23e+04")]
    [InlineData("sprintf(\"%g\", 0.5)", "0.5")]
    [InlineData("sprintf(\"%s=%s\", \"key\", 7)", "key=7")]
    [InlineData("sprintf(\"%x\", 255)", "ff")]
    [InlineData("sprintf(\"100%%\")", "100%")]
    public async Task Sprintf_FormatsItsSupportedVerbs(string expression, string expected) =>
        Assert.Equal(expected, await Eval(expression));

    [Fact]
    public async Task Sprintf_UnsupportedVerb_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(sprintf(\"%q\", 1))");

        Assert.False(result.Success);
        Assert.Contains("does not support", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Sprintf_ArgumentCountMismatch_IsRuntimeError()
    {
        ScriptRunResult tooFew = await Run("print(sprintf(\"%d %d\", 1))");
        Assert.False(tooFew.Success);
        Assert.Contains("needs more arguments", Assert.Single(tooFew.Diagnostics).Message);

        ScriptRunResult tooMany = await Run("print(sprintf(\"%d\", 1, 2))");
        Assert.False(tooMany.Success);
        Assert.Contains("more argument(s) than the format uses", Assert.Single(tooMany.Diagnostics).Message);
    }

    [Fact]
    public async Task StrAndNum_ConvertBothWays()
    {
        Assert.Equal("3.5", await Eval("str(3.5)"));
        Assert.Equal("[1, 2]", await Eval("str([1, 2])"));
        Assert.Equal("42.5", await Eval("num(\"42.5\")"));
        Assert.Equal("-7", await Eval("num(\" -7 \")"));
        Assert.Equal("true", await Eval("isnan(num(\"garbage\"))"));
    }

    [Fact]
    public async Task SplitAndJoin_RoundTrip()
    {
        Assert.Equal("[a, b, c]", await Eval("split(\"a,b,c\", \",\")"));
        Assert.Equal("a-b-c", await Eval("join([\"a\", \"b\", \"c\"], \"-\")"));
        Assert.Equal("a,b,c", await Eval("join(split(\"a,b,c\", \",\"), \",\")"));
        Assert.Equal("1|2", await Eval("join([1, 2], \"|\")"));
    }

    [Fact]
    public async Task CaseAndTrim()
    {
        Assert.Equal("HELLO", await Eval("upper(\"Hello\")"));
        Assert.Equal("hello", await Eval("lower(\"HeLLo\")"));
        Assert.Equal("x", await Eval("trim(\"  x  \")"));
    }

    [Fact]
    public async Task SearchHelpers_AreOrdinal()
    {
        Assert.Equal("true", await Eval("startswith(\"SN-042\", \"SN-\")"));
        Assert.Equal("false", await Eval("startswith(\"sn-042\", \"SN-\")"));
        Assert.Equal("true", await Eval("endswith(\"data.csv\", \".csv\")"));
        Assert.Equal("a_b_c", await Eval("replace(\"a-b-c\", \"-\", \"_\")"));
        Assert.Equal("true", await Eval("contains(\"filename.csv\", \"name\")"));
        Assert.Equal("false", await Eval("contains(\"abc\", \"z\")"));
    }
}
