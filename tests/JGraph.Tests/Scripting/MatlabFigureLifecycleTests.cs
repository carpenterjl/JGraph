using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Figures come and go: <c>close</c>, <c>clf</c>, closing a window by hand, and what the next run
/// then does. The engine's registry and the host's windows have to agree, or a script plots into a
/// figure nobody can see.
/// </summary>
[Collection("JG facade")]
public class MatlabFigureLifecycleTests : IDisposable
{
    private readonly RecordingFigureSink _sink = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabFigureLifecycleTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(_sink.Context(_output));

    private static Task<ScriptRunResult> Prompt(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private static Task<ScriptRunResult> RunFile(IScriptSession session, string code) =>
        session.ExecuteFileAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public async Task ClosingAWindowByHand_ThenRerunning_BringsTheFigureBack()
    {
        const string script = "figure(1)\nplot([1 2], [3 4])\ntitle('again')\n";
        await using IScriptSession session = NewSession();
        await RunFile(session, script);
        _sink.SimulateUserClose(1);

        ScriptRunResult rerun = await RunFile(session, script);

        Assert.True(rerun.Success, rerun.Message + _output.ErrorText);
        Assert.Equal(1, rerun.FiguresShown);
        Assert.Contains(1, _sink.Open);
        Assert.Equal("again", _sink.Shown[^1].Figure.Axes[0].Title);
        Assert.NotSame(_sink.Shown[0].Figure, _sink.Shown[^1].Figure); // a fresh figure, not the orphan
    }

    [Fact]
    public async Task Close_ClosesTheCurrentFigure_AndTellsTheHost()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "figure(1)\nfigure(2)");

        await Prompt(session, "close");

        Assert.Equal(new[] { 1 }, JG.FigureNumbers);
        Assert.Equal(2, Assert.Single(_sink.Closed));
        Assert.DoesNotContain(2, _sink.Open);
    }

    [Fact]
    public async Task AfterClose_TheMostRecentlyUsedFigureBecomesCurrent()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "figure(1)\nfigure(2)\nfigure(3)\nfigure(1)");

        await Prompt(session, "close"); // closes figure 1
        ScriptRunResult result = await Prompt(session, "n = gcf()");

        Assert.Equal(3d, Assert.Single(result.Variables, v => v.Name == "n").RawValue);
    }

    [Fact]
    public async Task ClosingAMissingFigure_Fails()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Prompt(session, "close(7)");

        Assert.False(result.Success);
        Assert.Contains("no figure 7", _output.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloseAll_EmptiesTheRegistry_AndClosesEveryWindow()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "figure(1)\nfigure(2)\nfigure(5)");

        ScriptRunResult result = await Prompt(session, "close all");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Empty(JG.FigureNumbers);
        Assert.Equal(new[] { 1, 2, 5 }, _sink.Closed);
        Assert.Empty(_sink.Open);
    }

    [Fact]
    public async Task Clf_EmptiesTheFigure_ButKeepsItsNumber()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "figure(1)\nplot([1 2], [3 4])");

        await Prompt(session, "clf");

        Assert.Equal(new[] { 1 }, JG.FigureNumbers);
        Assert.True(JG.TryGetFigure(1, out FigureModel figure));
        Assert.Empty(figure.Axes);
        Assert.Empty(_sink.Closed);
    }

    [Fact]
    public async Task ClfWithANumber_ClearsThatFigureAndSelectsIt()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "figure(1)\nplot([1 2], [3 4])\nfigure(2)\nplot([1 2], [3 4])");

        await Prompt(session, "clf(1)");

        Assert.True(JG.TryGetFigure(1, out FigureModel one));
        Assert.True(JG.TryGetFigure(2, out FigureModel two));
        Assert.Empty(one.Axes);
        Assert.Single(two.Axes);
        Assert.Equal(1, JG.CurrentFigureNumber);
    }

    [Fact]
    public async Task ClfOnAMissingFigure_Fails()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Prompt(session, "clf(3)");

        Assert.False(result.Success);
        Assert.Contains("no figure 3", _output.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GcfWithNoFigures_CreatesFigureOne()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Prompt(session, "n = gcf()");

        Assert.Equal(1d, Assert.Single(result.Variables, v => v.Name == "n").RawValue);
        Assert.Equal(new[] { 1 }, JG.FigureNumbers);
    }

    [Fact]
    public async Task Gca_CreatesAxesToLabel()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Prompt(session, "gca\nxlabel('t')");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("t", JG.Gca().PrimaryXAxis.Label);
    }

    [Fact]
    public async Task PlottingAfterCloseAll_StartsAgainAtFigureOne()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "figure(3)\nplot([1 2], [3 4])");
        await Prompt(session, "close all");

        await Prompt(session, "plot([1 2], [3 4])");

        Assert.Equal(new[] { 1 }, JG.FigureNumbers);
    }
}
