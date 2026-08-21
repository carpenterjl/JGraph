using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Three console-parity fixes, each found by typing at the console what one types at MATLAB's.
/// A bare <c>path</c> answers its value instead of showing nothing; an index write conjures the
/// variable it names (<c>x(5) = 123</c> on no <c>x</c> makes <c>[0 0 0 0 123]</c>); and the
/// Workspace pane's type column says what <c>class()</c> says instead of a private vocabulary.
/// </summary>
[Collection("JG facade")]
public class MatlabConsoleParityTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabConsoleParityTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string TypeOf(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).Type;

    // --- A bare name that answers a question -----------------------------------------------------

    [Fact]
    public async Task BarePath_AnswersTheSameStringTheCallDoes()
    {
        ScriptRunResult result = await RunMatlab("""
            bare = path;
            called = path();
            same = strcmp(bare, called);
            kind = class(bare);
            """);

        Assert.True(result.Success, result.Message);
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "same").RawValue));
        Assert.Equal("char", Assert.Single(result.Variables, v => v.Name == "kind").RawValue);
    }

    [Fact]
    public async Task BarePathsep_AnswersItsValueToo()
    {
        ScriptRunResult result = await RunMatlab("sep = pathsep; n = numel(sep);");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1.0, Number(result, "n"));
    }

    // --- An index write conjures the variable it names --------------------------------------------

    [Fact]
    public async Task IndexWriteOnNoVariable_ConjuresAZeroFilledVector()
    {
        ScriptRunResult result = await RunMatlab("""
            x(5) = 123;
            n = numel(x);
            first = x(1);
            last = x(5);
            y(1) = 7;
            ny = numel(y);
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(5.0, Number(result, "n"));
        Assert.Equal(0.0, Number(result, "first"));
        Assert.Equal(123.0, Number(result, "last"));
        Assert.Equal(1.0, Number(result, "ny"));
    }

    [Fact]
    public async Task TwoSubscriptWriteOnNoVariable_ConjuresAMatrix()
    {
        ScriptRunResult result = await RunMatlab("""
            M(2, 3) = 9;
            r = size(M, 1);
            c = size(M, 2);
            corner = M(1, 1);
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2.0, Number(result, "r"));
        Assert.Equal(3.0, Number(result, "c"));
        Assert.Equal(0.0, Number(result, "corner"));
    }

    [Fact]
    public async Task AWriteThatFails_LeavesNoConjuredVariableBehind()
    {
        // Index 0 is an error in the 1-based dialect; the variable the write named must not survive it.
        ScriptRunResult result = await RunMatlab("""
            try
                bad(0) = 1;
            catch
            end
            stays = exist('bad', 'var');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0.0, Number(result, "stays"));
    }

    [Fact]
    public async Task Jgs_StillRequiresLet_ForAFirstIndexWrite()
    {
        // JGS's typo net is not defeated by adding a subscript: a first write must still say let.
        ScriptRunResult result = await new JgsScriptEngine().RunAsync(
            "x(5) = 123", new ScriptContext(_output, static (_, _) => { }), default);

        Assert.False(result.Success);
    }

    // --- The Workspace pane speaks class()'s vocabulary -------------------------------------------

    [Fact]
    public async Task WorkspaceTypeColumn_MatchesClass()
    {
        ScriptRunResult result = await RunMatlab("""
            a = 3;
            c = 'hey';
            d = "str";
            e = true;
            h = [true false];
            g = int8(7);
            z = 1 + 2i;
            f = @sin;
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("double", TypeOf(result, "a"));
        Assert.Equal("char", TypeOf(result, "c"));
        Assert.Equal("string", TypeOf(result, "d"));
        Assert.Equal("logical", TypeOf(result, "e"));
        Assert.Equal("logical", TypeOf(result, "h"));
        Assert.Equal("int8", TypeOf(result, "g"));
        Assert.Equal("double", TypeOf(result, "z")); // class(1+2i) is double; complexity is an attribute
        Assert.Equal("function_handle", TypeOf(result, "f"));
    }
}
