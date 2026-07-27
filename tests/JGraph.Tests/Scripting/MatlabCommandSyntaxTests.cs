using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// MATLAB command syntax — <c>hold on</c>, <c>grid off</c>, <c>close all</c> — parses each bare word
/// into a string argument. These tests run the commands rather than just parsing them, because a
/// switch that reads the argument's truthiness rather than its wording turns <c>off</c> into on.
/// </summary>
[Collection("JG facade")]
public class MatlabCommandSyntaxTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCommandSyntaxTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession(IScriptEngine engine) => Assert
        .IsAssignableFrom<IScriptRepl>(engine)
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public async Task HoldOn_AccumulatesSeries()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        ScriptRunResult result = await Run(session, "plot([1 2], [1 2])\nhold on\nplot([1 2], [3 4])");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2, JG.Gca().Plots.Count);
    }

    [Fact]
    public async Task HoldOff_ActuallyStopsAccumulating()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Run(session, "plot([1 2], [1 2])\nhold on\nplot([1 2], [3 4])");

        ScriptRunResult result = await Run(session, "hold off\nplot([1 2], [5 6])");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Single(JG.Gca().Plots);
    }

    [Fact]
    public async Task GridOnAndOff_FollowTheWord()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Run(session, "plot([1 2], [1 2])");

        await Run(session, "grid on");
        Assert.True(JG.Gca().Grid.ShowMajor);

        await Run(session, "grid off");
        Assert.False(JG.Gca().Grid.ShowMajor);
    }

    [Fact]
    public async Task ANonsenseSwitchWord_IsAnError()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        ScriptRunResult result = await Run(session, "hold('sideways')");

        Assert.False(result.Success);
        Assert.Contains("'on' or 'off'", _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BooleanAndNumericSwitches_StillWork()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        await Run(session, "plot([1, 2], [1, 2])");

        await Run(session, "hold(true)");
        Assert.True(JG.IsHolding);

        await Run(session, "hold(0)");
        Assert.False(JG.IsHolding);
    }

    [Fact]
    public async Task BareHold_TogglesInMatlab_ButTurnsOnInJgs()
    {
        await using IScriptSession matlab = NewSession(new MatlabScriptEngine());
        await Run(matlab, "plot([1 2], [1 2])\nhold on");

        await Run(matlab, "hold");
        Assert.False(JG.IsHolding); // MATLAB's bare `hold` toggles

        JG.Reset();
        await using IScriptSession jgs = NewSession(new JgsScriptEngine());
        await Run(jgs, "plot([1, 2], [1, 2])\nhold()");
        await Run(jgs, "hold()");
        Assert.True(JG.IsHolding); // JGS keeps its older "a bare call turns it on"
    }

    [Fact]
    public async Task HoldBelongsToItsAxes_AndDoesNotFollowYouToANewFigure()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Run(session, "plot([1 2], [1 2])\nhold on");

        await Run(session, "figure(2)\nplot([1 2], [3 4])\nplot([1 2], [5 6])");

        Assert.Single(JG.Gca().Plots); // the second plot replaced the first: hold did not come along
    }

    [Fact]
    public async Task Clf_ClearsHoldWithTheAxes()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Run(session, "plot([1 2], [1 2])\nhold on");

        await Run(session, "clf\nplot([1 2], [3 4])\nplot([1 2], [5 6])");

        Assert.Single(JG.Gca().Plots);
    }

    [Fact]
    public async Task CloseAll_WorksInCommandSyntax()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Run(session, "figure(1)\nfigure(2)");

        ScriptRunResult result = await Run(session, "close all");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Empty(JG.FigureNumbers);
    }
}
