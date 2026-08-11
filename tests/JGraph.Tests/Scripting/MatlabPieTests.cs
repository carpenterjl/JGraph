using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave C: <c>pie</c> as scripts write it. Most of the work is in telling the two optional
/// positions apart — an explode vector is numbers and labels are text — so that is most of what is
/// tested here, alongside the properties the chart answers to afterwards.
/// </summary>
[Collection("JG facade")]
public class MatlabPieTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPieTests() => JG.Reset();

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

    private async Task<string> RunExpectingFailure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    [Fact]
    public async Task APieLabelsItselfWithWholePercentagesAndKeepsTheValuesItWasGiven()
    {
        await RunAsserting("""
            figure(1);
            h = pie([1 2 3 4]);
            disp(get(h, 'Type'));
            disp(get(h, 'Values'));
            l = get(h, 'Labels');
            disp(l{1});
            disp(l{4});
            disp(get(h, 'StartAngle'));
            """);

        Assert.Equal(new[] { "pie", "[1, 2, 3, 4]", "10%", "40%", "90" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnExplodeVectorIsNumbersAndLabelsAreText()
    {
        await RunAsserting("""
            figure(1);
            e = pie([1 1 1 1], [0 1 0 0]);
            disp(get(e, 'Explode'));

            l = pie([1 1], {'left', 'right'});
            words = get(l, 'Labels');
            disp(words{2});
            disp(isempty(get(l, 'Explode')));

            % Both at once, in MATLAB's order.
            b = pie([1 1], [1 0], {'out', 'in'});
            disp(get(b, 'Explode'));
            names = get(b, 'Labels');
            disp(names{1});
            """);

        Assert.Equal(
            new[] { "[0, 0.1, 0, 0]", "right", "true", "[0.1, 0]", "out" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ATotalUnderOneDrawsThePieItAsksForRatherThanFillingTheCircle()
    {
        await RunAsserting("""
            figure(1);
            h = pie([0.25 0.25]);
            l = get(h, 'Labels');
            disp(l{1});
            disp(l{2});
            """);

        Assert.Equal(new[] { "25%", "25%" }, _output.NormalLines);
    }

    [Fact]
    public async Task ThePieTurnsItsAxesIntoARoundFramelessOne()
    {
        await RunAsserting("""
            figure(1);
            pie([1 1 1]);
            ax = gca;
            disp(get(ax, 'Box'));
            xl = xlim;
            yl = ylim;
            disp(xl(1) < -1);
            disp(abs((xl(2) - xl(1)) - (yl(2) - yl(1))) < 1e-9);
            """);

        Assert.Equal(new[] { "off", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task EveryAppearanceOptionIsReadBackUnderTheNameItWasSetBy()
    {
        await RunAsserting("""
            figure(1);
            h = pie([1 2], 'FaceAlpha', 0.5, 'LineWidth', 2, 'EdgeColor', 'k', ...
                'StartAngle', 0, 'Clockwise', 'on', 'ShowLabels', 'off', ...
                'LabelRadius', 1.5, 'Colormap', 'jet', 'DisplayName', 'share');
            disp(get(h, 'FaceAlpha'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'StartAngle'));
            disp(get(h, 'Clockwise'));
            disp(get(h, 'ShowLabels'));
            disp(get(h, 'LabelRadius'));
            disp(get(h, 'DisplayName'));
            disp(size(get(h, 'Colormap'), 2));
            """);

        Assert.Equal(
            new[] { "0.5", "2", "0", "on", "off", "1.5", "share", "3" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ThePieCanBeRewrittenThroughSetAfterwards()
    {
        await RunAsserting("""
            figure(1);
            h = pie([1 2 3]);
            set(h, 'Values', [1 1]);
            set(h, 'Labels', {'a', 'b'});
            set(h, 'Explode', [0 0.2]);
            disp(get(h, 'Values'));
            l = get(h, 'Labels');
            disp(l{2});
            disp(get(h, 'Explode'));
            """);

        Assert.Equal(new[] { "[1, 1]", "b", "[0, 0.2]" }, _output.NormalLines);
    }

    [Fact]
    public async Task APieIsDrawnOnANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            h = pie(first, [1 1]);
            disp(gca == second);
            disp(numel(get(first, 'Children')));
            disp(numel(findobj(gcf, 'Type', 'pie')));
            """);

        Assert.Equal(new[] { "true", "1", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task ANegativeValueIsRefusedBecauseAWedgeHasNoNegativeShare()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            pie([1 -2 3]);
            """);

        Assert.Contains("negative", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LabelsThatDoNotCountTheWedgesAreRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            pie([1 2 3], {'a', 'b'});
            """);

        Assert.Contains("2 labels but 3 values", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOptionThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            pie([1 2], 'StartAngel', 30);
            """);

        Assert.Contains("StartAngel", message, StringComparison.Ordinal);
        Assert.Contains("StartAngle", message, StringComparison.Ordinal);
    }
}
