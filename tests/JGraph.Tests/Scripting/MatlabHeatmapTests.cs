using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave D: <c>heatmap</c> as scripts write it — the three call forms, the option tail, and the
/// four properties that belong to the axes here and to a chart container in MATLAB.
/// </summary>
[Collection("JG facade")]
public class MatlabHeatmapTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabHeatmapTests() => JG.Reset();

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
    public async Task AMatrixBecomesAGridWhoseColumnsAndRowsAreNumbered()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap([1 2 3; 4 5 6]);
            disp(get(h, 'Type'));
            disp(get(h, 'ColorData'));
            disp(get(h, 'ColorLimits'));
            x = get(h, 'XData');
            disp(x{3});
            y = get(h, 'YData');
            disp(y{2});
            """);

        Assert.Equal(
            new[] { "heatmap", "[1, 2, 3; 4, 5, 6]", "[1, 6]", "3", "2" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ARowOfNumbersIsAChartOneCellTall()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap([1 2 3]);
            disp(size(get(h, 'ColorData')));
            v = heatmap([1; 2; 3]);
            disp(size(get(v, 'ColorData')));
            """);

        Assert.Equal(new[] { "[1, 3]", "[3, 1]" }, _output.NormalLines);
    }

    [Fact]
    public async Task NamesGivenUpFrontLandOnTheRulers()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap({'a', 'b'}, {'one', 'two', 'three'}, [1 2; 3 4; 5 6]);
            names = get(h, 'XData');
            disp(names{2});
            rows = get(h, 'YData');
            disp(rows{3});
            disp(xticklabels);
            disp(yticklabels);
            """);

        Assert.Equal(
            new[] { "b", "three", "{'a', 'b'}", "{'one', 'two', 'three'}" },
            _output.NormalLines);
    }

    [Fact]
    public async Task EveryAppearanceOptionIsReadBackUnderTheNameItWasSetBy()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap([1 2; 3 4], 'Colormap', 'jet', 'ColorLimits', [0 10], ...
                'ColorScaling', 'scaledcolumns', 'GridVisible', 'off', 'FontSize', 14, ...
                'CellLabelFormat', '%.2f', 'MissingDataLabel', 'gone');
            disp(get(h, 'ColorLimits'));
            disp(get(h, 'ColorScaling'));
            disp(get(h, 'GridVisible'));
            disp(get(h, 'FontSize'));
            disp(get(h, 'MissingDataLabel'));
            disp(size(get(h, 'Colormap'), 2));
            """);

        Assert.Equal(
            new[] { "[0, 10]", "scaledcolumns", "off", "14", "gone", "3" },
            _output.NormalLines);
    }

    [Fact]
    public async Task CellLabelColorSaysAllThreeOfOffAutomaticAndAColour()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap([1 2; 3 4]);
            disp(get(h, 'CellLabelColor'));
            set(h, 'CellLabelColor', 'none');
            disp(get(h, 'CellLabelColor'));
            disp(get(h, 'ShowCellLabels'));
            set(h, 'CellLabelColor', 'r');
            disp(get(h, 'CellLabelColor'));
            disp(get(h, 'ShowCellLabels'));
            """);

        Assert.Equal(
            new[] { "auto", "none", "off", "[1, 0, 0]", "on" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheTitleAndLabelsAndColorbarBelongToTheAxesTheChartIsOn()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap([1 2; 3 4], 'Title', 'counts', 'XLabel', 'across', ...
                'YLabel', 'down', 'ColorbarVisible', 'on');
            disp(get(h, 'Title'));
            disp(get(h, 'ColorbarVisible'));

            % They are the axes' own properties, so the axes handle answers with the same words.
            ax = gca;
            disp(get(ax, 'Title'));
            disp(get(ax, 'XLabel'));
            disp(get(ax, 'YLabel'));
            """);

        Assert.Equal(
            new[] { "counts", "on", "counts", "across", "down" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ATableIsGroupedByItsTwoVariablesAndCountedByDefault()
    {
        await RunAsserting("""
            figure(1);
            t = table(["x"; "y"; "x"; "x"], ["p"; "p"; "q"; "p"], [10; 20; 30; 40], ...
                'VariableNames', {'across', 'down', 'value'});

            counts = heatmap(t, 'across', 'down');
            disp(get(counts, 'ColorData'));
            names = get(counts, 'XData');
            disp(names{1});

            summed = heatmap(t, 'across', 'down', 'ColorVariable', 'value', 'ColorMethod', 'sum');
            disp(get(summed, 'ColorData'));

            averaged = heatmap(t, 'across', 'down', 'ColorVariable', 'value');
            disp(get(averaged, 'ColorData'));
            """);

        Assert.Equal(
            new[] { "[2, 1; 1, 0]", "x", "[50, 20; 30, NaN]", "[25, 20; 30, NaN]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheGridCanBeRewrittenThroughSetAndTheRulersFollowIt()
    {
        await RunAsserting("""
            figure(1);
            h = heatmap([1 2 3]);
            set(h, 'ColorData', [1 2; 3 4]);
            disp(size(get(h, 'ColorData')));
            set(h, 'XData', {'p', 'q'});
            disp(xticklabels);
            """);

        Assert.Equal(new[] { "[2, 2]", "{'p', 'q'}" }, _output.NormalLines);
    }

    [Fact]
    public async Task AHeatmapIsDrawnOnANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            h = heatmap(first, [1 2; 3 4]);
            disp(gca == second);
            disp(numel(findobj(gcf, 'Type', 'heatmap')));
            """);

        Assert.Equal(new[] { "true", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task NamesThatDoNotCountTheCellsAreRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            heatmap({'a', 'b', 'c'}, {'one'}, [1 2; 3 4]);
            """);

        Assert.Contains("3 names but 2", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AColorMethodWithNothingToWorkOnIsRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            t = table(["x"; "y"], ["p"; "q"], 'VariableNames', {'across', 'down'});
            heatmap(t, 'across', 'down', 'ColorMethod', 'mean');
            """);

        Assert.Contains("ColorVariable", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOptionThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            heatmap([1 2; 3 4], 'ColorScalling', 'log');
            """);

        Assert.Contains("ColorScalling", message, StringComparison.Ordinal);
        Assert.Contains("ColorScaling", message, StringComparison.Ordinal);
    }
}
