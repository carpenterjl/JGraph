using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave A: what a handle is and what it answers to. The property table replaced four hand-written
/// switches, so most of what is checked here is that the old answers survived — and the rest is the
/// three things the wave's own probe found: only <c>plot</c> handed a handle back, a drawing verb used
/// as a statement echoed <c>ans</c>, and a ruler disagreed with its own axes about its limits.
/// </summary>
[Collection("JG facade")]
public class MatlabHandleSurfaceTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabHandleSurfaceTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public async Task AFigureHandleIsItsNumber()
    {
        await RunAsserting("""
            f = figure(3);
            disp(f == 3);
            disp(f.Number);
            disp(f.Type);
            """);

        Assert.Equal(new[] { "true", "3", "figure" }, _output.NormalLines);
    }

    [Fact]
    public async Task EveryDrawingVerbHandsBackAHandle()
    {
        // Before M54 only `plot` did, so `set` and `get` — the point of the milestone — would have
        // reached lines and nothing else.
        await RunAsserting("""
            figure(1); hold on;
            h = [];
            h(end+1) = plot(1:3, [1 2 3]);
            h(end+1) = scatter(1:3, [1 2 3]);
            h(end+1) = bar(1:3, [1 2 3]);
            h(end+1) = stem(1:3, [1 2 3]);
            h(end+1) = histogram([1 2 2 3]);
            h(end+1) = errorbar(1:3, [1 2 3], [.1 .1 .1]);
            h(end+1) = semilogx(1:3, [1 2 3]);
            h(end+1) = plot3(1:3, 1:3, 1:3);
            h(end+1) = fill([0 1 1], [0 0 1], 'r');
            disp(numel(h));
            disp(numel(unique(h)));
            """);

        // Nine handles, and no two of them the same object.
        Assert.Equal(new[] { "9", "9" }, _output.NormalLines);
    }

    [Fact]
    public async Task ADrawingVerbUsedAsAStatementPrintsNothing()
    {
        await RunAsserting("""
            figure(1); hold on;
            plot(1:3, [1 2 3])
            scatter(1:3, [1 2 3])
            bar(1:3, [1 2 3])
            """);

        Assert.DoesNotContain("ans", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTypeWordComesFromTheObjectRatherThanItsClassName()
    {
        await RunAsserting("""
            figure(1); hold on;
            disp(plot(1:3, [1 2 3]).Type);
            disp(scatter(1:3, [1 2 3]).Type);
            disp(bar(1:3, [1 2 3]).Type);
            ax = gca;
            disp(ax.Type);
            disp(ax.XAxis.Type);
            """);

        Assert.Equal(
            new[] { "line", "scatter", "bar", "axes", "numericruler" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AHandleAnswersThePropertiesTheModelDeclares()
    {
        // Nothing lists these by hand; they are reachable because the model marks them browsable.
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            ax = gca;
            disp(p.Opacity);
            disp(ax.AutoScalePadding);
            disp(ax.EqualAspect);
            p.Opacity = 0.5;
            disp(p.Opacity);
            """);

        Assert.Equal(new[] { "1", "0.05", "off", "0.5" }, _output.NormalLines);
    }

    [Fact]
    public async Task ChildrenAndParentNameTheSameObjectsBackAgain()
    {
        await RunAsserting("""
            figure(1); hold on;
            p = plot(1:3, [1 2 3]);
            s = scatter(1:3, [3 2 1]);
            ax = gca;
            kids = ax.Children;
            disp(numel(kids));
            disp(kids(1) == s);
            disp(kids(2) == p);
            disp(p.Parent == ax);
            disp(ax.Parent == 1);
            """);

        // Children come newest first, which is the order MATLAB lists them in.
        Assert.Equal(new[] { "2", "true", "true", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ARulerAgreesWithItsAxesAboutTheLimits()
    {
        // Both spellings have to fit the data first. Reading the stored range instead answers the
        // placeholder the axes was created with, which is the M51 lesson one level further down.
        await RunAsserting("""
            figure(1);
            plot(1:5, [10 20 30 40 50]);
            ax = gca;
            disp(isequal(ax.XAxis.Limits, ax.XLim));
            disp(isequal(ax.YAxis.Limits, ax.YLim));
            ax.XLim = [0 10];
            disp(ax.XAxis.Limits);
            """);

        Assert.Equal(new[] { "true", "true", "[0, 10]" }, _output.NormalLines);
    }

    [Fact]
    public async Task BarStandsItsValuesAtOneTwoThree()
    {
        await RunAsserting("""
            figure(1);
            b = bar([4 5 6]);
            disp(b.XData);
            disp(b.YData);
            """);

        Assert.Equal(new[] { "[1, 2, 3]", "[4, 5, 6]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ClosingAFigureLetsGoOfTheHandlesIntoIt()
    {
        await RunAsserting("""
            figure(7);
            p = plot(1:3, [1 2 3]);
            disp(p.Type);
            close(7);
            try
                disp(p.Type);
            catch err
                disp('gone');
            end
            """);

        Assert.Equal(new[] { "line", "gone" }, _output.NormalLines);
    }

    [Fact]
    public async Task TagAndUserDataRoundTrip()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            p.Tag = 'series one';
            p.UserData = {1, 'two'};
            disp(p.Tag);
            disp(class(p.UserData));
            disp(numel(p.UserData));
            """);

        Assert.Equal(new[] { "series one", "cell", "2" }, _output.NormalLines);
    }
}
