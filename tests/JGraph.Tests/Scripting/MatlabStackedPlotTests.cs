using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M57 wave F: <c>stackedplot</c> through the script surface — what it lays out, what it links, and
/// what it says when a variable or an option is not there.
/// </summary>
[Collection("JG facade")]
public class MatlabStackedPlotTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStackedPlotTests() => JG.Reset();

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

    /// <summary>A run of readings and three things measured over it.</summary>
    private const string Readings = """
        X = (1:6)';
        Y = [X, X.^2, sin(X)];
        """;

    /// <summary>Three moments of one measurement, with a time column to draw them against.</summary>
    private const string Log = """
        t = table([1;2;3], [10;20;30], [5;4;3], 'VariableNames', {'t', 'v', 'i'});
        """;

    [Fact]
    public async Task EveryColumnGetsAPanelOfItsOwn()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            [h, ax] = stackedplot(X, Y);
            disp(numel(h));
            disp(numel(ax));
            disp(get(ax(1), 'Type'));
            disp(get(h(1), 'Type'));
            disp(get(h(2), 'YData'));
            """);

        Assert.Equal(
            new[] { "3", "3", "axes", "line", "[1, 4, 9, 16, 25, 36]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ThePanelsShareTheirXAndKeepTheirOwnY()
    {
        // The x is linked rather than merely equal, which is the difference between one chart and
        // three that happen to line up; the y scales stay apart, which is why the chart is stacked
        // rather than drawn over itself.
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            [h, ax] = stackedplot(X, Y);
            disp(isequal(get(ax(1), 'XLim'), get(ax(3), 'XLim')));
            disp(get(ax(1), 'XLim'));
            disp(isequal(get(ax(1), 'YLim'), get(ax(2), 'YLim')));
            """);

        Assert.Equal(new[] { "true", "[0.75, 6.25]", "false" }, _output.NormalLines);
    }

    [Fact]
    public async Task ATableNamesItsOwnPanelsAndCanNameTheirX()
    {
        await RunAsserting($$"""
            figure(1);
            {{Log}}
            [h, ax] = stackedplot(t, 'XVariable', 't');
            disp(numel(h));
            disp(get(ax(1), 'YLabel'));
            disp(get(ax(2), 'YLabel'));

            % The variable the panels are drawn against is not a panel of its own.
            disp(get(h(1), 'XData'));
            """);

        Assert.Equal(new[] { "2", "v", "i", "[1, 2, 3]" }, _output.NormalLines);
    }

    [Fact]
    public async Task TheVariablesToDrawAndTheLabelsToUseAreBothSaidByName()
    {
        await RunAsserting($$"""
            figure(1);
            {{Log}}
            [h, ax] = stackedplot(t, {'v'}, 'DisplayLabels', {'volts'}, 'LineWidth', 3);
            disp(numel(h));
            disp(get(ax(1), 'YLabel'));
            disp(get(h, 'LineWidth'));
            """);

        Assert.Equal(new[] { "1", "volts", "3" }, _output.NormalLines);
    }

    [Fact]
    public async Task OnlyTheBottomPanelSaysWhatXIs()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            stackedplot(X, Y);
            show();
            """);

        FigureModel figure = _figures[^1].Figure;
        Assert.Equal(3, figure.Axes.Count);
        Assert.False(figure.Axes[0].PrimaryXAxis.ShowTickLabels);
        Assert.False(figure.Axes[1].PrimaryXAxis.ShowTickLabels);
        Assert.True(figure.Axes[2].PrimaryXAxis.ShowTickLabels);
        Assert.All(figure.Axes, axes => Assert.IsType<LinePlot>(axes.Plots[0]));
    }

    [Fact]
    public async Task AMisspelledOptionNamesTheOnesItKnows()
    {
        string error = await RunExpectingFailure($$"""
            {{Log}}
            stackedplot(t, 'Wiggle', 1);
            """);

        Assert.Contains("stackedplot has no 'Wiggle' option", error);
        Assert.Contains("DisplayLabels", error);
    }

    [Fact]
    public async Task AVariableThatIsNotThereNamesTheOnesThatAre()
    {
        string error = await RunExpectingFailure($$"""
            {{Log}}
            stackedplot(t, 'XVariable', 'nope');
            """);

        Assert.Contains("the table has no variable 'nope'", error);
        Assert.Contains("t, v, i", error);
    }

    [Fact]
    public async Task AChartThatLaysOutItsOwnAxesCannotBeAimedAtOne()
    {
        string error = await RunExpectingFailure("""
            ax = subplot(1, 1, 1);
            stackedplot(ax, [1 2 3]);
            """);

        Assert.Contains("lays out its own column of axes", error);
    }

    [Fact]
    public async Task EveryReadingStillNeedsItsX()
    {
        string error = await RunExpectingFailure("stackedplot([1 2 3], [1 2]);");
        Assert.Contains("x has 3 values but a variable has 2", error);
    }
}
