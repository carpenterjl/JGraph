using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave C: the verbs that aim at one ruler — xticks and its siblings. What is checked is that a
/// verb and the matching property spelling agree (they share one implementation, and this is what
/// proves it), that 'auto' and 'manual' mean what MATLAB means by them, and that a misspelling says
/// what was allowed instead of quietly doing something.
/// </summary>
[Collection("JG facade")]
public class MatlabRulerTickTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabRulerTickTests() => JG.Reset();

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

    /// <summary>Runs code that is meant to fail, and answers only the complaint this run made.</summary>
    private async Task<string> RunExpectingFailure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    [Fact]
    public async Task TicksSetAndReadBackAsTheyWereGiven()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, (1:10).^2);
            xticks(0:2:10);
            disp(xticks);
            yticks([0 50 100]);
            disp(yticks);
            zticks([0 1]);
            disp(zticks);
            """);

        Assert.Equal(new[] { "[0, 2, 4, 6, 8, 10]", "[0, 50, 100]", "[0, 1]" }, _output.NormalLines);
    }

    [Fact]
    public async Task TheVerbAndThePropertySpellingAreTheSameThing()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            ax = gca;
            xticks([1 5 9]);
            disp(isequal(get(ax, 'XTick'), xticks));
            set(ax, 'XTick', [2 4 6]);
            disp(xticks);
            disp(isequal(get(ax.XAxis, 'TickValues'), xticks));
            """);

        Assert.Equal(new[] { "true", "[2, 4, 6]", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AutoGivesTheChoiceBackAndManualFreezesWhatIsShowing()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            xticks([3 4]);
            disp(xticks);
            xticks('auto');
            automatic = xticks;
            disp(numel(automatic) > 2);
            xticks('manual');
            disp(isequal(xticks, automatic));
            """);

        Assert.Equal(new[] { "[3, 4]", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task LabelsComeBackAsACellAndCanBeBlanked()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            xticklabels({'low', 'high'});
            lb = xticklabels;
            disp(class(lb));
            disp(numel(lb));
            disp(strjoin(lb, '|'));
            xticklabels([]);
            disp(isempty(xticklabels));
            xticklabels('auto');
            disp(numel(xticklabels) > 2);
            """);

        Assert.Equal(new[] { "cell", "2", "low|high", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnAngleIsWrittenReadAndSharedWithTheRuler()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            disp(xtickangle);
            xtickangle(45);
            disp(xtickangle);
            disp(get(gca, 'XTickLabelRotation'));
            set(gca.XAxis, 'TickLabelRotation', 10);
            disp(xtickangle);
            """);

        Assert.Equal(new[] { "0", "45", "45", "10" }, _output.NormalLines);
    }

    [Fact]
    public async Task AFormatChangesHowTheTickNumbersAreWritten()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            xticks([2 4]);
            xtickformat('%.2f');
            disp(xticklabels{1});
            xtickformat('usd');
            disp(xticklabels{1});
            xtickformat('degrees');
            disp(xticklabels{1});
            xtickformat('auto');
            disp(xtickformat);
            """);

        Assert.Equal(new[] { "2.00", "$2.00", "2°", "auto" }, _output.NormalLines);
    }

    [Fact]
    public async Task ANamedAxesIsAimedAtWithoutBecomingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;
            xticks(first, [1 2 3]);
            disp(xticks(first));
            disp(gca == second);
            """);

        Assert.Equal(new[] { "[1, 2, 3]", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ARulerValueAndANumberAreTheSameThingHere()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            r = gca.XAxis;
            disp(num2ruler(3, r));
            disp(ruler2num(3, r));
            """);

        Assert.Equal(new[] { "3", "3" }, _output.NormalLines);
    }

    [Fact]
    public async Task TicksOutOfOrderAndAMisspelledWordBothSayWhatIsWrong()
    {
        Assert.Contains("increase", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            xticks([5 1]);
            """), StringComparison.Ordinal);

        Assert.Contains("'auto'", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            xticklabels('atuo');
            """), StringComparison.Ordinal);

        Assert.Contains("usd", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            xtickformat('dollars');
            """), StringComparison.Ordinal);

        Assert.Contains("f, e, g, or d", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            xtickformat('%.2q');
            """), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandleWhereTickValuesBelongIsRefusedRatherThanPlotted()
    {
        // A handle to something that is neither an axes nor a ruler would quietly put a tick at a
        // million and a half, since a handle is an ordinary number. A ruler handle, on the other hand,
        // names the very thing the verb is about, and since M55 wave G it aims at that ruler.
        Assert.Contains("aims at an axes", await RunExpectingFailure("""
            figure(1);
            h = plot(1:10, 1:10);
            xticks(h);
            """), StringComparison.Ordinal);

        Assert.Contains("ruler", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            num2ruler(1, gca);
            """), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARulerHandleAimsATickVerbAtThatRuler()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            xticks([0 5 10]);
            yticks(gca.YAxis, [2 4]);
            disp(xticks(gca.XAxis));
            disp(yticks);
            """);

        Assert.Equal(new[] { "[0, 5, 10]", "[2, 4]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ThePolarRulerVerbsSayWhatTheyAreWaitingFor()
    {
        Assert.Contains("polar axes", await RunExpectingFailure("""
            figure(1);
            rticks(1:3);
            """), StringComparison.Ordinal);

        Assert.Contains("polar axes", await RunExpectingFailure("""
            figure(1);
            thetalim([0 90]);
            """), StringComparison.Ordinal);
    }
}
