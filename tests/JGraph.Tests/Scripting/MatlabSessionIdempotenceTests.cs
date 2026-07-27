using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Running the same script twice in one console session must do the same thing twice — the MATLAB
/// expectation, and the one a long-lived session is most likely to break, because everything a
/// one-shot run rebuilds (figure registry, workspace, host globals) survives here.
/// </summary>
[Collection("JG facade")]
public class MatlabSessionIdempotenceTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSessionIdempotenceTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (number, figure) => _figures.Add((number, figure)));

    private IScriptSession NewSession(IScriptEngine engine) =>
        Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());

    private static Task<ScriptRunResult> RunFile(IScriptSession session, string code) =>
        session.ExecuteFileAsync(code, sourceId: "", CancellationToken.None);

    private static Task<ScriptRunResult> Prompt(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    /// <summary>The user's report: a function file that plots into two numbered figures, run twice.</summary>
    private const string TwoFigureFunctionFile = """
        function test1

        x = 1:9;
        y = [1 9 2 8 3 7 4 6 5];

        figure(1)
        plot(x, y)
        title('Figure 1')

        figure(2)
        plot(x, y)
        """;

    [Fact]
    public async Task RunningAFunctionFileTwice_ShowsItsFiguresBothTimes()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        ScriptRunResult first = await RunFile(session, TwoFigureFunctionFile);
        ScriptRunResult second = await RunFile(session, TwoFigureFunctionFile);

        Assert.True(first.Success, first.Message + _output.ErrorText);
        Assert.True(second.Success, second.Message + _output.ErrorText);
        Assert.Equal(2, first.FiguresShown);
        Assert.Equal(2, second.FiguresShown);
        Assert.Equal(new[] { 1, 2, 1, 2 }, _figures.Select(static f => f.Number));
    }

    [Fact]
    public async Task ARerunPlotsIntoTheSameNumberedFigures_WithFreshContent()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await RunFile(session, TwoFigureFunctionFile);

        await RunFile(session, TwoFigureFunctionFile);

        Assert.Equal(new[] { 1, 2 }, JG.FigureNumbers);
        Assert.True(JG.TryGetFigure(1, out FigureModel one));
        Assert.Equal("Figure 1", one.Axes[0].Title);
        Assert.Single(one.Axes[0].Plots); // the second run replaced the first run's line, not stacked on it
    }

    [Fact]
    public async Task RunningAScriptTwice_ProducesIdenticalOutputAndVariables()
    {
        const string script = "x = 1:4;\ny = sum(x)\nz = mean(x)\n";
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        _output.Mark();
        ScriptRunResult first = await RunFile(session, script);
        string firstText = _output.TextSinceMark;
        string[] firstVariables = Project(first);

        _output.Mark();
        ScriptRunResult second = await RunFile(session, script);

        Assert.Equal(firstText, _output.TextSinceMark);
        Assert.Equal(firstVariables, Project(second));
    }

    [Fact]
    public async Task AFigureTheRunNeverTouched_IsNeitherCountedNorRedisplayed()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Prompt(session, "figure(4)"); // as if opened from the Data Viewer or a .graph file
        _figures.Clear();

        ScriptRunResult result = await RunFile(session, "figure(1)\nplot([0 1], [0 1])\n");

        Assert.Equal(1, result.FiguresShown);
        Assert.Equal(1, Assert.Single(_figures).Number);
    }

    [Fact]
    public async Task AStatementThatOpensNoFigure_ReportsNone()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        ScriptRunResult result = await Prompt(session, "x = 3");

        Assert.Equal(0, result.FiguresShown);
    }

    [Fact]
    public async Task SelectingAFigureAtThePrompt_ShowsIt()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await Prompt(session, "figure(1)\nfigure(2)");
        _figures.Clear();

        ScriptRunResult result = await Prompt(session, "figure(1)");

        Assert.Equal(1, result.FiguresShown);
        Assert.Equal(1, Assert.Single(_figures).Number);
    }

    [Fact]
    public async Task AJgsScriptRunTwice_ShowsItsFigureBothTimes()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        ScriptRunResult first = await RunFile(session, "figure(1)\nplot([0, 1], [0, 1])");
        ScriptRunResult second = await RunFile(session, "figure(1)\nplot([0, 1], [0, 1])");

        Assert.Equal(1, first.FiguresShown);
        Assert.Equal(1, second.FiguresShown);
    }

    [Fact]
    public async Task ARerunAutoScales_InsteadOfInheritingFrozenLimits()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await RunFile(session, "plot([1 2], [0 1])\nylim([0 1])\n");

        await RunFile(session, "plot([1 2], [0 500])\n");

        Assert.True(JG.Gca().PrimaryYAxis.AutoScale);
    }

    [Fact]
    public async Task PlottingAfresh_ClearsTheDecorationOfThePlotItReplaced()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await RunFile(session, "plot([1 2], [1 2])\ntitle('first')\nxlabel('t')\ngrid on\n");

        await RunFile(session, "plot([1 2], [3 4])\n");

        AxesModel axes = JG.Gca();
        Assert.Equal(string.Empty, axes.Title);
        Assert.Equal(string.Empty, axes.PrimaryXAxis.Label);
        Assert.False(axes.Grid.ShowMajor);
    }

    [Fact]
    public async Task HoldingKeepsTheDecoration()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await RunFile(session, "plot([1 2], [1 2])\ntitle('kept')\nhold on\n");

        await RunFile(session, "plot([1 2], [3 4])\n");

        Assert.Equal("kept", JG.Gca().Title);
        Assert.Equal(2, JG.Gca().Plots.Count);
    }

    [Fact]
    public async Task PlottingAfterASurface_ReturnsTheAxesTo2D()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        ScriptRunResult surface = await RunFile(
            session, "surf([1 2 3], [1 2 3], [1 2 3; 4 5 6; 7 8 9])\n");
        Assert.True(surface.Success, surface.Message + _output.ErrorText);
        Assert.True(JG.Gca().Is3D);

        await RunFile(session, "plot([1 2], [3 4])\n");

        Assert.False(JG.Gca().Is3D);
    }

    [Fact]
    public async Task RerunningASubplotScript_ReusesTheSameCells()
    {
        const string script = "subplot(2, 1, 1)\nplot([1 2], [1 2])\nsubplot(2, 1, 2)\nplot([1 2], [3 4])\n";
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        await RunFile(session, script);

        await RunFile(session, script);

        Assert.True(JG.TryGetFigure(1, out FigureModel figure));
        Assert.Equal(2, figure.Axes.Count);
        Assert.All(figure.Axes, static axes => Assert.Single(axes.Plots));
    }

    [Fact]
    public async Task AFileRun_ReadsTheDataSittingBesideTheScript()
    {
        string root = Directory.CreateTempSubdirectory("jgraph-m35-").FullName;
        try
        {
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(root, "data.csv"), "v\n1\n");
            File.WriteAllText(Path.Combine(sub, "data.csv"), "v\n2\n3\n");
            string scriptPath = Path.Combine(sub, "work.m");

            await using IScriptSession session = Assert
                .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
                .CreateSession(new ScriptContext(
                    _output, (number, figure) => _figures.Add((number, figure)), root));

            ScriptRunResult fromFile = await session.ExecuteFileAsync(
                "t = readcsv('data.csv');\nn = rowcount(t)\n", scriptPath, CancellationToken.None);

            Assert.True(fromFile.Success, fromFile.Message + _output.ErrorText);
            Assert.Equal(2d, Assert.Single(fromFile.Variables, v => v.Name == "n").RawValue);

            // The prompt has no script folder, so it resolves against the workspace root as before.
            ScriptRunResult fromPrompt = await Prompt(session, "t2 = readcsv('data.csv');\nm = rowcount(t2)\n");

            Assert.True(fromPrompt.Success, fromPrompt.Message + _output.ErrorText);
            Assert.Equal(1d, Assert.Single(fromPrompt.Variables, v => v.Name == "m").RawValue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>An ordered name/type/value projection of a run's workspace, for comparing two runs.</summary>
    private static string[] Project(ScriptRunResult result) => result.Variables
        .OrderBy(static v => v.Name, StringComparer.Ordinal)
        .Select(static v => $"{v.Name}:{v.Type}={v.DisplayValue}")
        .ToArray();
}
