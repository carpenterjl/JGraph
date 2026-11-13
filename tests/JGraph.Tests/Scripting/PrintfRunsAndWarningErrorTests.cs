using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 184, 295 and 189 (ADR 0346), every expectation measured against R2025b: %s over a
/// numeric argument takes the run of character codes left in it, %c takes one character of text, a
/// value that is no code is written as %e under either, an empty argument is one value that writes
/// nothing, and warning('error', id) turns the warning under id into an error.
/// </summary>
[Collection("JG facade")]
public class PrintfRunsAndWarningErrorTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public PrintfRunsAndWarningErrorTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        _output.Normal.Clear();
        _output.Errors.Clear();
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    /// <summary>The codes of what <c>sprintf(<paramref name="call"/>)</c> makes, so control characters compare too.</summary>
    private string SprintfCodes(string call)
    {
        ScriptRunResult result = RunMatlab($"fprintf('%s', mat2str(double(sprintf({call}))));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private static string Codes(string text) =>
        text.Length == 0 ? "zeros(1,0)"
        : text.Length == 1 ? ((int)text[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "[" + string.Join(' ', text.Select(static c => ((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

    [Theory]
    // Item 184: the run of codes in one argument.
    [InlineData("'arg%d\\t%s\\n', 2, [2 3]", "arg2\t\u0002\u0003\n")]
    [InlineData("'%s|', [72 105]", "Hi|")]
    [InlineData("'%s|', [65 66 67.5 68 69]", "AB|6.750000e+01|DE|")]
    [InlineData("'%s|', [65 -1 66]", "A|-1.000000e+00|B|")]
    [InlineData("'%s|', [65 NaN 66]", "A|NaN|B|")]
    [InlineData("'%d %s|', 1, [65 66], 4", "1 AB|4 ")]
    [InlineData("'%d %s|', [1 65 66]", "1 AB|")]
    [InlineData("'%s,%s;', 65, 66, 67", "A,B;C,")]
    [InlineData("'%s=%d\\n', [120 121], 1", "xy=1\n")]
    [InlineData("'%5s|', [65 66]", "   AB|")]
    [InlineData("'%.1s|', [65 66 67]", "A|")]
    [InlineData("'%6s|', [65 66 2.5 67]", "    AB|2.500000e+00|     C|")]
    [InlineData("'%s|', logical([1 0 1])", "\u0001\u0000\u0001|")]
    [InlineData("'%s|', int8([65 66 -3 67])", "AB|-3.000000e+00|C|")]
    [InlineData("'%s', 65+2i", "A")]
    [InlineData("'%s', 65536", "6.553600e+04")]
    [InlineData("'%x %s|', [255 65 66]", "ff AB|")]
    // %c: one character of text, and %e for a value that is no code.
    [InlineData("'%c|', 'abc'", "a|b|c|")]
    [InlineData("'%c%c|', 'ab', 65", "ab|A")]
    [InlineData("'%c%s|', 'abc', 'de'", "abc|de|")]
    [InlineData("'%s%c|', 'abc', 'de'", "abcd|e")]
    [InlineData("'%c|', [65.5 66]", "6.550000e+01|B|")]
    [InlineData("'%+c|', 2.5", "+2.500000e+00|")]
    [InlineData("'%c', NaN", "NaN")]
    [InlineData("'%c', 70000", "7.000000e+04")]
    [InlineData("'%d %s|', 'abc'", "97 bc|")]
    // Item 295: an empty argument is one value that writes nothing.
    [InlineData("'%.17g ', []", " ")]
    [InlineData("'%d,%d;', []", ",")]
    [InlineData("'lead %d mid %s tail', []", "lead  mid ")]
    [InlineData("'%d %d\\n', [], 5", " 5\n")]
    [InlineData("'<%d>', [], 5, []", "<><5><>")]
    [InlineData("'%5d|', []", "     |")]
    [InlineData("'%05d|', []", "00000|")]
    [InlineData("'%+d|', []", "|")]
    [InlineData("'%d|', ''", "|")]
    [InlineData("'%s,', '', 'x', ''", ",x,,")]
    [InlineData("'%s|', strings(0)", "|")]
    [InlineData("'[%d]', zeros(0, 3)", "[]")]
    [InlineData("'%*d|', 5, []", "     |")]
    [InlineData("'%*d|', []", "")]
    [InlineData("'%*d|'", "")]
    [InlineData("'%1$d|', []", "")]
    [InlineData("'a%db%dc'", "a")]
    public void Sprintf_FollowsR2025b(string call, string expected) =>
        Assert.Equal(Codes(expected), SprintfCodes(call));

    /// <summary>A lone surrogate is a MATLAB char like any other (an attribute cannot carry one as text).</summary>
    [Fact]
    public void Sprintf_LoneSurrogateIsACharacter() =>
        Assert.Equal("55296", SprintfCodes("'%s', 55296"));

    [Fact]
    public void Fprintf_TakesTheSameRun()
    {
        ScriptRunResult result = RunMatlab("fprintf('arg%d\\t%s\\n', 2, [72 105]); fprintf('%.17g ', []); fprintf('|\\n');");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("arg2\tHi\n |\n", _output.NormalText);
    }

    [Theory]
    [InlineData("sprintf('abc', {1})", "cell")]
    [InlineData("sprintf('%d|', 1, {2})", "cell")]
    [InlineData("sprintf('%s', struct('a', 1))", "struct")]
    [InlineData("fprintf('%d|', 1, {2})", "cell")]
    public void Printf_RefusesCellsAndStructs(string call, string kind)
    {
        ScriptRunResult result = RunMatlab($"try, {call}; catch e, fprintf('%s|%s', e.identifier, e.message); end");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal($"MATLAB:printf:invalidInputType|Function is not defined for '{kind}' inputs.", _output.NormalText);
    }

    [Fact]
    public void WarningError_RaisesTheWarningAsAnError()
    {
        ScriptRunResult result = RunMatlab(
            "s = warning('error', 'oi189:a'); fprintf('%s=%s;', s.identifier, s.state);\n"
            + "q = warning('query', 'oi189:a'); fprintf('%s;', q.state);\n"
            + "lastwarn('', '');\n"
            + "try\n warning('oi189:a', 'hello %d', 5);\ncatch e\n fprintf('%s|%s|%s;', class(e), e.identifier, e.message);\nend\n"
            + "[m, i] = lastwarn; fprintf('[%s][%s];', m, i);\n"
            + "p = warning('off', 'oi189:a'); fprintf('%s;', p.state);\n"
            + "warning('oi189:a', 'quiet'); [m, i] = lastwarn; fprintf('%s/%s;', m, i);\n"
            + "warning(struct('identifier', 'oi189:a', 'state', 'error')); q = warning('query', 'oi189:a'); fprintf('%s;', q.state);\n"
            + "warning('on', 'all'); q = warning('query', 'oi189:a'); fprintf('%s;', q.state);");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(
            "oi189:a=on;error;MException|oi189:a|hello 5;[][];error;quiet/oi189:a;error;on;",
            _output.NormalText);
    }

    [Fact]
    public void WarningError_UncaughtStopsTheScript()
    {
        ScriptRunResult result = RunMatlab("warning('error', 'oi189:u'); warning('oi189:u', 'boom %d', 3); fprintf('not reached');");
        Assert.False(result.Success);
        Assert.Contains("boom 3", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not reached", _output.NormalText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("warning('error', 'all')", "MATLAB:warning:cannotUseAllWithError")]
    [InlineData("warning('error', 'backtrace')", "MATLAB:warning:unknownSettingOrId")]
    [InlineData("warning('error', 'noColon')", "MATLAB:warning:unknownSettingOrId")]
    [InlineData("warning('error', 5)", "MATLAB:badopt")]
    [InlineData("warning('error', 'a:b', 'c')", "MATLAB:maxrhs")]
    public void WarningError_Refusals(string call, string identifier)
    {
        ScriptRunResult result = RunMatlab($"try, {call}; catch e, fprintf('%s', e.identifier); end");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(identifier, _output.NormalText);
    }

    [Fact]
    public void WarningError_OneArgumentIsAMessage()
    {
        ScriptRunResult result = RunMatlab("lastwarn(''); warning('error'); [m, i] = lastwarn; fprintf('[%s][%s]', m, i);");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("[error][]", _output.NormalText);
    }

    [Fact]
    public void WarningError_ReachesTheEnginesOwnWarnings()
    {
        ScriptRunResult result = RunMatlab(
            "x = 1:10; warning('error', 'MATLAB:colon:nonIntegerIndex');\n"
            + "try\n y = x(1.5:3);\ncatch e\n fprintf('%s;', e.identifier);\nend\n"
            + "warning('on', 'MATLAB:colon:nonIntegerIndex'); lastwarn('', ''); y = x(1.5:3); [~, i] = lastwarn; fprintf('%s', i);");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("MATLAB:colon:nonIntegerIndex;MATLAB:colon:nonIntegerIndex", _output.NormalText);
    }

    [Fact]
    public void WarningError_QueryStatementSaysError()
    {
        ScriptRunResult result = RunMatlab("warning('error', 'oi189:s'); warning('query', 'oi189:s');");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("The state of warning 'oi189:s' is 'error'.", _output.NormalText, StringComparison.Ordinal);
    }
}
