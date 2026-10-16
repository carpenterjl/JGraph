using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave C: <c>pie</c> as scripts write it. Most of the work is in telling the two optional
/// positions apart — an explode vector is numbers and labels are text — so that is most of what is
/// tested here. Since open item 112 (ADR 0229) the MATLAB dialect draws R2025b's pie: a patch and a
/// text per slice, answered in a row, with R2025b's refusals; the JGraph extras still shape it.
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
    public async Task APieIsAPatchAndATextPerSliceLabelledWithWholePercentages()
    {
        await RunAsserting("""
            figure(1);
            h = pie([1 2 3 4]);
            disp(size(h));
            disp(get(h(1), 'Type'));
            disp(get(h(2), 'Type'));
            disp(get(h(2), 'String'));
            disp(get(h(8), 'String'));

            % pie.m fans a slice out over max(1, ceil(100 x)) arc points, plus the centre twice, and
            % colours each slice by its number.
            disp(numel(get(h(1), 'XData')));
            c = unique(get(h(1), 'CData'));
            disp(c(1));
            c = unique(get(h(7), 'CData'));
            disp(numel(c));
            disp(c(1));
            """);

        Assert.Equal(
            new[] { "[1, 8]", "patch", "text", "10%", "40%", "13", "1", "1", "4" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AnExplodeVectorIsNumbersAndLabelsAreText()
    {
        await RunAsserting("""
            figure(1);
            e = pie([1 1 1 1], [0 1 0 0]);
            x = get(e(3), 'XData');
            disp(abs(x(1)) > 0.05);
            x = get(e(1), 'XData');
            disp(abs(x(1)) < 1e-9);

            l = pie([1 1], {'left', 'right'});
            disp(get(l(4), 'String'));
            disp(get(l(3), 'DisplayName'));

            % Both at once, in MATLAB's order.
            b = pie([1 1], [1 0], {'out', 'in'});
            x = get(b(1), 'XData');
            disp(abs(x(1)) > 0.05);
            disp(get(b(2), 'String'));
            """);

        Assert.Equal(
            new[] { "true", "true", "right", "right", "true", "out" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ATotalUnderOneDrawsThePieItAsksForRatherThanFillingTheCircle()
    {
        await RunAsserting("""
            figure(1);
            h = pie([0.25 0.25]);
            disp(get(h(2), 'String'));
            disp(get(h(4), 'String'));

            % Half a circle from the top, counter-clockwise: the second slice ends at the bottom.
            y = get(h(3), 'YData');
            disp(abs(y(end - 1) + 1) < 1e-9);
            """);

        Assert.Equal(new[] { "25%", "25%", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ThePieTurnsItsAxesIntoARoundFramelessOne()
    {
        await RunAsserting("""
            figure(1);
            pie([1 1 1]);
            ax = gca;
            disp(get(ax, 'Visible'));
            disp(xlim);
            disp(ylim);
            disp(get(ax, 'CLim'));
            """);

        Assert.Equal(new[] { "    off", "[-1.2, 1.2]", "[-1.2, 1.2]", "[1, 3]" }, _output.NormalLines);
    }

    [Fact]
    public async Task EveryAppearanceOptionStillShapesTheSlices()
    {
        await RunAsserting("""
            figure(1);
            h = pie([1 2], 'FaceAlpha', 0.5, 'LineWidth', 2, 'EdgeColor', 'k', ...
                'StartAngle', 0, 'Clockwise', 'on', 'ShowLabels', 'off', ...
                'LabelRadius', 1.5, 'Colormap', 'jet', 'DisplayName', 'share');
            disp(numel(h));
            disp(get(h(1), 'FaceAlpha'));
            disp(get(h(1), 'LineWidth'));
            disp(get(h(1), 'EdgeColor'));
            disp(get(h(1), 'DisplayName'));

            % A start angle of zero puts the first arc point on the x axis.
            x = get(h(1), 'XData');
            y = get(h(1), 'YData');
            disp(abs(x(2) - 1) < 1e-9 && abs(y(2)) < 1e-9);
            """);

        Assert.Equal(
            new[] { "2", "0.5", "2", "[0, 0, 0]", "share", "true" },
            _output.NormalLines);
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
            disp(numel(findobj(gcf, 'Type', 'patch')));
            """);

        Assert.Equal(new[] { "true", "4", "2" }, _output.NormalLines);
    }

    [Fact]
    public async Task ANegativeValueIsDroppedWithAWarningAndAllNegativeIsRefused()
    {
        await RunAsserting("""
            figure(1);
            h = pie([1 -2 3]);
            disp(numel(h));
            [~, id] = lastwarn;
            disp(id);
            disp(get(h(2), 'String'));
            """);

        Assert.Equal(new[] { "4", "MATLAB:pie:NegativeData", "25%" }, _output.NormalLines);

        string message = await RunExpectingFailure("""
            figure(1);
            pie([-1 -2]);
            """);

        Assert.Contains("nonnegative", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LabelsThatDoNotCountTheWedgesAreRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            pie([1 2 3], {'a', 'b'});
            """);

        Assert.Contains("same number of elements as X", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A word in the second position is a label or a format to R2025b, so the option tail begins at
    /// a name the verb knows; a misspelt one after it names the ones that exist.
    /// </summary>
    [Fact]
    public async Task AnOptionThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            pie([1 2], 'FaceAlpha', 0.5, 'StartAngel', 30);
            """);

        Assert.Contains("StartAngel", message, StringComparison.Ordinal);
        Assert.Contains("StartAngle", message, StringComparison.Ordinal);

        string label = await RunExpectingFailure("""
            figure(1);
            pie([1 2], 'StartAngel', 30);
            """);

        Assert.Contains("same number of elements as X", label, StringComparison.Ordinal);
    }
}
