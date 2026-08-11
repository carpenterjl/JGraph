using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave D: <c>yyaxis</c> and the limit verbs. What is checked is that naming a side redirects
/// everything y-facing at once — the label, the limits, the ticks, and the plots drawn next — and that
/// re-plotting one side leaves the other alone, which is the whole point of a two-sided axes.
/// </summary>
[Collection("JG facade")]
public class MatlabYyAxisTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabYyAxisTests() => JG.Reset();

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
    public async Task EachSideKeepsItsOwnLabelLimitsAndTicks()
    {
        await RunAsserting("""
            figure(1);
            yyaxis left;
            plot(1:10, 1:10);
            ylabel('small');
            yyaxis right;
            plot(1:10, (1:10).^3);
            ylabel('large');

            ax = gca;
            yyaxis left;
            disp(get(ax, 'YLabel'));
            small = ylim;
            yyaxis right;
            disp(get(ax, 'YLabel'));
            large = ylim;
            disp(large(2) > small(2));

            yticks([0 500 1000]);
            disp(yticks);
            yyaxis left;
            disp(numel(yticks) > 3);
            """);

        Assert.Equal(new[] { "small", "large", "true", "[0, 500, 1000]", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ThePlotsDrawnAfterASideNameBelongToThatSide()
    {
        await RunAsserting("""
            figure(1);
            yyaxis left;
            plot(1:10, 1:10);
            yyaxis right;
            plot(1:10, (1:10).^2);
            disp(numel(findobj(gcf, 'Type', 'line')));

            % Replacing the right side must not take the left one with it.
            plot(1:10, (1:10).^3);
            disp(numel(findobj(gcf, 'Type', 'line')));
            """);

        Assert.Equal(new[] { "2", "2" }, _output.NormalLines);
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
            yyaxis(first, 'right');
            disp(gca == second);
            """);

        Assert.Equal(new[] { "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMisspelledSideSaysWhichSidesThereAre()
    {
        Assert.Contains("'left' or 'right'", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            yyaxis middle;
            """), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitsAreWrittenBothWaysAndReadBack()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            xlim([0 20]);
            disp(xlim);
            xlim(1, 5);
            disp(xlim);
            ylim([-1 1]);
            disp(ylim);
            zlim([0 2]);
            disp(zlim);
            """);

        Assert.Equal(new[] { "[0, 20]", "[1, 5]", "[-1, 1]", "[0, 2]" }, _output.NormalLines);
    }

    [Fact]
    public async Task AutoFitsTheDataAgainAndManualFreezesWhatIsShowing()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            xlim([0 100]);
            disp(xlim);
            xlim('auto');
            fitted = xlim;
            disp(fitted(2) < 20);
            xlim('manual');
            disp(isequal(xlim, fitted));
            """);

        Assert.Equal(new[] { "[0, 100]", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnUnfittedAxisIsFittedBeforeItsLimitsAreRead()
    {
        // The M51 rule: a freshly plotted axes has not been through the layout pass, so a read that
        // trusted the stored range would answer with the placeholder 0..1 instead of the data.
        await RunAsserting("""
            figure(1);
            plot(1:10, (1:10) * 100);
            lim = ylim;
            disp(lim(2) > 900);
            """);

        Assert.Equal(new[] { "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task BadLimitsAndBadWordsBothSayWhatIsWrong()
    {
        Assert.Contains("must exceed", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            ylim([5 1]);
            """), StringComparison.Ordinal);

        Assert.Contains("'auto'", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            ylim('atuo');
            """), StringComparison.Ordinal);
    }
}
