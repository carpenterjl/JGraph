using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave E: <c>boxchart</c> as scripts write it — the two call forms, the grouping in numbers or
/// in names, the option tail, and <c>GroupByColor</c>, which is the one option that answers with
/// more than one handle.
/// </summary>
[Collection("JG facade")]
public class MatlabBoxChartTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabBoxChartTests() => JG.Reset();

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
    public async Task OneListOfObservationsIsOneBoxAtPositionOne()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart([1 2 3 4 5 6 7 8 100]);
            disp(get(h, 'Type'));
            disp(get(h, 'MedianValues'));
            disp(get(h, 'BoxPositions'));

            % The quartile convention is the toolbox's, so the two agree on the same sample.
            disp(prctile([1 2 3 4 5 6 7 8 100], 50));
            """);

        Assert.Equal(new[] { "boxchart", "[5]", "[1]", "5" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMatrixDrawsOneBoxPerColumn()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart([1 10; 2 20; 3 30; 4 40]);
            disp(get(h, 'MedianValues'));
            disp(get(h, 'BoxPositions'));
            """);

        Assert.Equal(new[] { "[2.5, 25]", "[1, 2]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ANumericGroupingPutsEachBoxWhereItSays()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart([1 1 1 5 5 5], [1 2 3 10 20 30]);
            disp(get(h, 'MedianValues'));
            disp(get(h, 'BoxPositions'));
            """);

        Assert.Equal(new[] { "[2, 20]", "[1, 5]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ANamedGroupingGoesOnACategoryRulerInNameOrder()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart({'b', 'b', 'a', 'a'}, [10 20 1 3]);
            disp(get(h, 'MedianValues'));
            disp(get(h, 'BoxPositions'));
            disp(xticklabels);
            """);

        Assert.Equal(new[] { "[2, 15]", "[0, 1]", "{'a', 'b'}" }, _output.NormalLines);
    }

    [Fact]
    public async Task EveryAppearanceOptionIsReadBackUnderTheNameItWasSetBy()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart([1 2 3 4 100], 'BoxFaceColor', 'r', 'BoxFaceAlpha', 0.3, ...
                'BoxEdgeColor', 'k', 'BoxMedianLineColor', [0 0 1], 'BoxWidth', 0.8, ...
                'LineWidth', 2, 'WhiskerLineColor', 'g', 'WhiskerLineStyle', '--', ...
                'MarkerStyle', '+', 'MarkerSize', 10, 'MarkerColor', 'm', ...
                'Notch', 'on', 'JitterOutliers', 'on', 'DisplayName', 'sample');
            disp(get(h, 'BoxFaceColor'));
            disp(get(h, 'BoxFaceAlpha'));
            disp(get(h, 'BoxWidth'));
            disp(get(h, 'WhiskerLineStyle'));
            disp(get(h, 'MarkerStyle'));
            disp(get(h, 'MarkerSize'));
            disp(get(h, 'Notch'));
            disp(get(h, 'JitterOutliers'));
            disp(get(h, 'DisplayName'));
            """);

        Assert.Equal(
            new[] { "[1, 0, 0]", "0.3", "0.8", "--", "+", "10", "on", "on", "sample" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TurningTheChartOnItsSideNamesTheGroupsDownTheOtherRuler()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart({'p', 'p', 'q', 'q'}, [1 3 10 30], 'Orientation', 'horizontal');
            disp(get(h, 'Orientation'));
            disp(yticklabels);
            """);

        Assert.Equal(new[] { "horizontal", "{'p', 'q'}" }, _output.NormalLines);
    }

    [Fact]
    public async Task GroupByColorDrawsOneChartPerColourGroupAndNamesEachOfThem()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart([1 1 2 2 1 1 2 2], [1 2 10 20 3 4 30 40], ...
                'GroupByColor', {'x', 'x', 'x', 'x', 'y', 'y', 'y', 'y'});
            disp(numel(h));
            disp(get(h(1), 'DisplayName'));
            disp(get(h(1), 'MedianValues'));
            disp(get(h(2), 'DisplayName'));
            disp(get(h(2), 'MedianValues'));

            % Two charts on one axes share the slot at each position rather than overlapping.
            disp(get(h(1), 'BoxWidth'));
            """);

        Assert.Equal(
            new[] { "2", "x", "[1.5, 15]", "y", "[3.5, 35]", "0.5" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheObservationsCanBeRewrittenThroughSetAndTheBoxesFollow()
    {
        await RunAsserting("""
            figure(1);
            h = boxchart([1 2 3]);
            set(h, 'YData', [10 20 30 40]);
            disp(get(h, 'MedianValues'));
            set(h, 'XData', [1 1 2 2]);
            disp(get(h, 'BoxPositions'));
            disp(get(h, 'MedianValues'));
            set(h, 'Orientation', 'horizontal');
            disp(get(h, 'Orientation'));
            """);

        Assert.Equal(
            new[] { "[25]", "[1, 2]", "[15, 35]", "horizontal" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ABoxChartIsDrawnOnANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            h = boxchart(first, [1 2 3 4]);
            disp(gca == second);
            disp(numel(findobj(gcf, 'Type', 'boxchart')));
            """);

        Assert.Equal(new[] { "true", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task AGroupingThatDoesNotCountTheObservationsIsRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            boxchart([1 1 2], [10 20]);
            """);

        Assert.Contains("3 values but ydata has 2", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrientationThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            boxchart([1 2 3], 'Orientation', 'sideways');
            """);

        Assert.Contains("sideways", message, StringComparison.Ordinal);
        Assert.Contains("vertical or horizontal", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOptionThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            boxchart([1 2 3], 'BoxFaceColour', 'r');
            """);

        Assert.Contains("BoxFaceColour", message, StringComparison.Ordinal);
        Assert.Contains("BoxFaceColor", message, StringComparison.Ordinal);
    }
}
