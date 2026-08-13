using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M57 wave F: <c>scatterhistogram</c> through the script surface — the three axes it lays out, what
/// each marginal is drawn as, how a grouping splits the picture, and what a misspelled option says.
/// </summary>
[Collection("JG facade")]
public class MatlabScatterHistogramTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabScatterHistogramTests() => JG.Reset();

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

    /// <summary>Ten points that rise together, so both marginals have something to say.</summary>
    private const string Points = """
        x = [1 2 3 4 5 6 7 8 9 10];
        y = [2 1 4 3 6 5 8 7 10 9];
        """;

    [Fact]
    public async Task ThePointsComeWithADistributionAlongEachEdge()
    {
        await RunAsserting($$"""
            figure(1);
            {{Points}}
            [s, ax] = scatterhistogram(x, y);
            disp(numel(s));
            disp(numel(ax));
            disp(get(s, 'Type'));
            disp(get(s, 'XData'));
            """);

        Assert.Equal(
            new[] { "1", "3", "scatter", "[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task EachMarginalIsLinkedToTheRulerItDescribes()
    {
        // A marginal that went on describing the whole sample while the scatter showed part of it
        // would be answering a different question from the one being looked at.
        await RunAsserting($$"""
            figure(1);
            {{Points}}
            [s, ax] = scatterhistogram(x, y);
            disp(isequal(get(ax(1), 'XLim'), get(ax(2), 'XLim')));
            disp(isequal(get(ax(1), 'YLim'), get(ax(3), 'YLim')));

            % Limits set on the scatter reach the marginal that shares that ruler.
            [s2, ax2] = scatterhistogram(x, y, 'XLimits', [0 12]);
            disp(get(ax2(2), 'XLim'));
            """);

        Assert.Equal(new[] { "true", "true", "[0, 12]" }, _output.NormalLines);
    }

    [Fact]
    public async Task EachMarginalIsBarsUnlessAnotherShapeIsAskedFor()
    {
        await RunAsserting($$"""
            figure(1);
            {{Points}}
            scatterhistogram(x, y, 'NumBins', 4);
            show();
            """);

        FigureModel figure = _figures[^1].Figure;
        Assert.Equal(3, figure.Axes.Count);
        Assert.IsType<ScatterPlot>(figure.Axes[0].Plots[0]);

        var across = Assert.IsType<BarPlot>(figure.Axes[1].Plots[0]);
        var beside = Assert.IsType<BarPlot>(figure.Axes[2].Plots[0]);
        Assert.Equal(4, across.Data.Count);
        Assert.Equal(4, beside.Data.Count);

        // Every reading is counted once and only once, which is what the bin edges are for.
        Assert.Equal(10, Counted(across));

        // The one running up the page is the same chart lying on its side.
        Assert.False(across.Horizontal);
        Assert.True(beside.Horizontal);
    }

    [Fact]
    public async Task ADistributionCanBeDrawnAsAStaircaseOrAsASmoothCurve()
    {
        await RunAsserting($$"""
            figure(1);
            {{Points}}
            scatterhistogram(x, y, 'NumBins', 4, 'HistogramDisplayStyle', 'stairs');
            show();
            """);

        var steps = Assert.IsType<LinePlot>(_figures[^1].Figure.Axes[1].Plots[0]);

        // Two points a bin, plus the ends the outline comes down to.
        Assert.Equal(10, steps.Data.Count);

        await RunAsserting($$"""
            figure(2);
            {{Points}}
            scatterhistogram(x, y, 'HistogramDisplayStyle', 'smooth');
            show();
            """);

        var curve = Assert.IsType<LinePlot>(_figures[^1].Figure.Axes[1].Plots[0]);
        Assert.Equal(100, curve.Data.Count);
        Assert.True(curve.GetYDataBounds().Max > 0);
    }

    [Fact]
    public async Task AGroupingSplitsThePointsAndTheDistributionsWithThem()
    {
        await RunAsserting($$"""
            figure(1);
            {{Points}}
            g = {'a','a','a','a','a','b','b','b','b','b'};
            [s, ax] = scatterhistogram(x, y, 'GroupData', g, 'MarkerSize', 8);
            disp(numel(s));
            disp(get(s(1), 'XData'));
            disp(get(s(2), 'XData'));
            disp(get(s(1), 'MarkerSize'));
            show();
            """);

        Assert.Equal(
            new[] { "2", "[1, 2, 3, 4, 5]", "[6, 7, 8, 9, 10]", "8" },
            _output.NormalLines);

        FigureModel figure = _figures[^1].Figure;
        Assert.Equal(2, figure.Axes[1].Plots.Count);
        Assert.True(figure.Axes[0].Legend.Visible);
    }

    [Fact]
    public async Task ATableNamesTheRulersAfterItsOwnVariables()
    {
        await RunAsserting("""
            figure(1);
            t = table([1;2;3], [10;20;30], 'VariableNames', {'t', 'v'});
            s = scatterhistogram(t, 't', 'v');
            disp(get(s, 'YData'));

            % The scatter is left current, so a following label lands on the picture.
            disp(get(gca, 'XLabel'));
            """);

        Assert.Equal(new[] { "[10, 20, 30]", "t" }, _output.NormalLines);
    }

    [Fact]
    public async Task TheScatterSitsInWhicheverCornerWasAskedFor()
    {
        await RunAsserting($$"""
            figure(1);
            {{Points}}
            scatterhistogram(x, y, 'ScatterPlotLocation', 'northeast');
            show();
            """);

        FigureModel figure = _figures[^1].Figure;
        AxesModel points = figure.Axes[0];
        AxesModel across = figure.Axes[1];

        // The scatter is up the page and the distribution of x is under it, which is the layout
        // turned upside down from the default.
        Assert.True(points.NormalizedBounds.Y < across.NormalizedBounds.Y);
    }

    [Fact]
    public async Task AMisspelledShapeNamesTheThreeItKnows()
    {
        string error = await RunExpectingFailure(
            "scatterhistogram([1 2], [1 2], 'HistogramDisplayStyle', 'blob');");

        Assert.Contains("HistogramDisplayStyle is one of bar, stairs, smooth", error);
    }

    [Fact]
    public async Task TheBinsAreAWholeNumberOfOneOrMore()
    {
        string error = await RunExpectingFailure("scatterhistogram([1 2], [1 2], 'NumBins', 0);");
        Assert.Contains("NumBins is a whole number of one or more", error);
    }

    [Fact]
    public async Task GroupsForArraysComeInGroupDataRatherThanFromATable()
    {
        string error = await RunExpectingFailure(
            "scatterhistogram([1 2], [1 2], 'GroupVariable', 'g');");

        Assert.Contains("GroupVariable names a variable of a table", error);
        Assert.Contains("GroupData", error);
    }

    [Fact]
    public async Task EveryPointStillNeedsBothOfItsCoordinates()
    {
        string error = await RunExpectingFailure("scatterhistogram([1 2 3], [1 2]);");
        Assert.Contains("x has 3 values but y has 2", error);
    }

    /// <summary>How many readings a marginal's bars account for.</summary>
    private static double Counted(BarPlot bars)
    {
        double total = 0;
        for (int i = 0; i < bars.Data.Count; i++)
        {
            total += bars.Data.GetY(i);
        }

        return total;
    }
}
