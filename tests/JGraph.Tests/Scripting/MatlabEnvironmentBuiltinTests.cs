using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The console environment builtins (M36): <c>format</c> switching numeric display precision,
/// <c>whos</c> listing the workspace, and <c>help</c> reading the builtin catalog.
/// </summary>
[Collection("JG facade")]
public class MatlabEnvironmentBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabEnvironmentBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public async Task FormatShort_TrimsToFiveSignificantDigits()
    {
        await using IScriptSession session = NewSession();

        await Run(session, "disp(pi)");
        Assert.Contains("3.141592653589793", _output.NormalText, StringComparison.Ordinal);

        _output.Mark();
        await Run(session, "format short\ndisp(pi)");
        Assert.Contains("3.1416", _output.TextSinceMark, StringComparison.Ordinal);
        Assert.DoesNotContain("3.14159", _output.TextSinceMark, StringComparison.Ordinal);

        // Whole numbers stay exact under short.
        _output.Mark();
        await Run(session, "disp(123456789)");
        Assert.Contains("123456789", _output.TextSinceMark, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatLongAndBareFormat_RestoreFullPrecision()
    {
        await using IScriptSession session = NewSession();
        await Run(session, "format short");

        _output.Mark();
        await Run(session, "format long\ndisp(pi)");
        Assert.Contains("3.141592653589793", _output.TextSinceMark, StringComparison.Ordinal);

        await Run(session, "format shortE");
        _output.Mark();
        await Run(session, "disp(pi)");
        Assert.Contains("3.1416e+00", _output.TextSinceMark, StringComparison.Ordinal);

        await Run(session, "format");
        _output.Mark();
        await Run(session, "disp(pi)");
        Assert.Contains("3.141592653589793", _output.TextSinceMark, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatWithANonsenseWord_IsAnError()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, "format sideways");

        Assert.False(result.Success);
        Assert.Contains("does not recognize", _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewSession_StartsAtDefaultPrecision()
    {
        await using (IScriptSession first = NewSession())
        {
            await Run(first, "format short");
        }

        await using IScriptSession second = NewSession();
        await Run(second, "disp(pi)");
        Assert.Contains("3.141592653589793", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whos_ListsVariablesWithSizeAndClass()
    {
        await using IScriptSession session = NewSession();
        await Run(session, "x = [1 2 3]; name = 'probe'; M = [1 2; 3 4];");

        _output.Mark();
        ScriptRunResult result = await Run(session, "whos");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        string listing = _output.TextSinceMark;
        Assert.Contains("x", listing, StringComparison.Ordinal);
        Assert.Contains("1x3", listing, StringComparison.Ordinal);
        Assert.Contains("double", listing, StringComparison.Ordinal);
        Assert.Contains("2x2", listing, StringComparison.Ordinal);
        Assert.Contains("1x5", listing, StringComparison.Ordinal);
        Assert.Contains("char", listing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whos_OnAnEmptyWorkspace_PrintsNothing()
    {
        await using IScriptSession session = NewSession();

        _output.Mark();
        ScriptRunResult result = await Run(session, "whos");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(string.Empty, _output.TextSinceMark.Trim());
    }

    [Fact]
    public async Task Help_ShowsACatalogEntry_AndKnowsWhatItDoesNotKnow()
    {
        await using IScriptSession session = NewSession();

        await Run(session, "help sin");
        Assert.Contains("sin(x)", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("Sine", _output.NormalText, StringComparison.Ordinal);

        _output.Mark();
        await Run(session, "help nonsensename");
        Assert.Contains("No help found", _output.TextSinceMark, StringComparison.Ordinal);

        _output.Mark();
        await Run(session, "help");
        Assert.Contains("plot", _output.TextSinceMark, StringComparison.Ordinal);
    }
}
