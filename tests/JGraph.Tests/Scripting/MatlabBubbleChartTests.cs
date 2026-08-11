using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave F: <c>bubblechart</c> and the three verbs that scale and explain it, as scripts write
/// them — the call forms, the option tail, the shared scale, and the legend.
/// </summary>
[Collection("JG facade")]
public class MatlabBubbleChartTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabBubbleChartTests() => JG.Reset();

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
    public async Task ABubbleChartIsAScatterCarryingSizes()
    {
        await RunAsserting("""
            figure(1);
            b = bubblechart([1 2 3], [10 20 30], [0 50 100]);

            % MATLAB answers 'scatter' here too: a bubble chart is a scatter with sizes, not a type
            % of its own.
            disp(get(b, 'Type'));
            disp(get(b, 'SizeData'));
            disp(bubblelim);
            disp(bubblesize);
            disp(get(b, 'BubbleDiameters'));
            """);

        Assert.Equal(
            new[] { "scatter", "[0, 50, 100]", "[0, 100]", "[6, 40]", "[6, 28.600699292150182, 40]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheSizeRangeAndTheValueLimitsBothRescaleTheBubbles()
    {
        await RunAsserting("""
            figure(1);
            b = bubblechart([1 2 3], [1 2 3], [0 50 100]);

            bubblesize([10 30]);
            disp(bubblesize);
            disp(get(b, 'BubbleDiameters'));

            % Narrower limits flatten what lies past them rather than hiding it.
            bubblelim([0 50]);
            disp(get(b, 'BubbleDiameters'));

            bubblelim('auto');
            disp(bubblelim);
            """);

        Assert.Equal(
            new[] { "[10, 30]", "[10, 22.360679774997898, 30]", "[10, 30, 30]", "[0, 100]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheFourthArgumentIsEitherOneColourOrOneValuePerBubble()
    {
        await RunAsserting("""
            figure(1);
            named = bubblechart([1 2 3 4], [1 2 3 4], [1 2 3 4], 'r');
            disp(get(named, 'MarkerEdgeColor'));

            figure(2);
            mapped = bubblechart([1 2 3 4], [1 2 3 4], [1 2 3 4], [10 20 30 40]);
            disp(get(mapped, 'ColorData'));

            % A colour still reads as a colour when options follow it.
            figure(3);
            both = bubblechart([1 2], [1 2], [1 2], [0 0 1], 'LineWidth', 3);
            disp(get(both, 'MarkerEdgeColor'));
            disp(get(both, 'LineWidth'));
            """);

        Assert.Equal(
            new[] { "[1, 0, 0]", "[10, 20, 30, 40]", "[0, 0, 1]", "3" },
            _output.NormalLines);
    }

    [Fact]
    public async Task EveryAppearanceOptionIsReadBackUnderTheNameItWasSetBy()
    {
        await RunAsserting("""
            figure(1);
            h = bubblechart([1 2 3], [1 2 3], [1 2 3], ...
                'MarkerFaceColor', 'g', 'MarkerEdgeColor', 'k', 'MarkerFaceAlpha', 0.25, ...
                'MarkerEdgeAlpha', 0.8, 'LineWidth', 2, 'Marker', 's', 'DisplayName', 'runs');
            disp(get(h, 'MarkerFaceColor'));
            disp(get(h, 'MarkerFaceAlpha'));
            disp(get(h, 'MarkerEdgeAlpha'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'Marker'));
            disp(get(h, 'DisplayName'));
            """);

        Assert.Equal(
            new[] { "[0, 0.5019607843137255, 0]", "0.25098039215686274", "0.8", "2", "s", "runs" },
            _output.NormalLines);
    }

    [Fact]
    public async Task BubblesAreDrawnPartTransparentSoOverlappingOnesCanBothBeSeen()
    {
        await RunAsserting("""
            figure(1);
            h = bubblechart([1 2], [1 2], [1 2]);
            disp(get(h, 'MarkerFaceAlpha'));
            disp(get(h, 'MarkerEdgeAlpha'));
            """);

        Assert.Equal(new[] { "0.6", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task TwoChartsOnOneAxesShareTheScaleBetweenThem()
    {
        await RunAsserting("""
            figure(1);
            first = bubblechart([1 2], [1 2], [1 2]);
            hold on;
            second = bubblechart([3 4], [3 4], [3 100]);

            % One scale over both, so the two charts can be compared by eye.
            disp(bubblelim);
            disp(get(first, 'BubbleDiameters'));
            disp(get(second, 'BubbleDiameters'));
            """);

        Assert.Equal(
            new[] { "[1, 100]", "[6, 7.197081338847005]", "[8.221676203546306, 40]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task OneSizeForEveryBubbleIsALegalCall()
    {
        await RunAsserting("""
            figure(1);
            h = bubblechart([1 2 3], [1 2 3], 7);
            disp(get(h, 'SizeData'));
            disp(get(h, 'BubbleDiameters'));
            """);

        // Every value the same means every bubble the same, halfway up the size range.
        Assert.Equal(
            new[] { "[7, 7, 7]", "[28.600699292150182, 28.600699292150182, 28.600699292150182]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ABubbleLegendCarriesItsTitleArrangementAndTheValuesItShows()
    {
        await RunAsserting("""
            figure(1);
            bubblechart([1 2 3], [1 2 3], [10 50 90]);
            lg = bubblelegend('Population', 'Location', 'northwest', 'NumBubbles', 4, ...
                'Style', 'telescopic', 'LimitLabels', 'on', 'Box', 'off');
            disp(get(lg, 'Type'));
            disp(get(lg, 'Title'));
            disp(get(lg, 'Location'));
            disp(get(lg, 'NumBubbles'));
            disp(get(lg, 'Style'));
            disp(get(lg, 'LimitLabels'));
            disp(get(lg, 'Box'));
            disp(get(lg, 'BubbleValues'));
            """);

        Assert.Equal(
            new[]
            {
                "bubblelegend", "Population", "northwest", "4", "telescopic", "on", "off",
                "[10, 36.666666666666664, 63.33333333333333, 90]",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheScaleCanBeWrittenThroughSetAndTheBubblesFollow()
    {
        await RunAsserting("""
            figure(1);
            h = bubblechart([1 2 3], [1 2 3], [1 2 3]);
            set(h, 'SizeData', [0 5 10]);
            disp(get(h, 'SizeData'));

            set(gca, 'BubbleSizeLimits', [0 10]);
            disp(get(gca, 'BubbleSizeLimits'));
            disp(get(gca, 'BubbleSizeRange'));
            disp(get(h, 'BubbleDiameters'));

            set(gca, 'BubbleSizeRange', [10 20]);
            disp(bubblesize);
            """);

        Assert.Equal(
            new[] { "[0, 5, 10]", "[0, 10]", "[6, 40]", "[6, 28.600699292150182, 40]", "[10, 20]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ABubbleChartIsDrawnOnANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            bubblechart(first, [1 2], [1 2], [1 2]);
            bubblesize(first, [5 15]);
            disp(gca == second);
            disp(bubblesize(first));
            disp(bubblesize(second));
            """);

        // Each axes has its own scale, so sizing one leaves the other where it was.
        Assert.Equal(new[] { "true", "[5, 15]", "[6, 40]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ACallWithoutSizesIsRefusedBecauseTheSizesAreThePoint()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            bubblechart([1 2 3], [1 2 3]);
            """);

        Assert.Contains("bubblechart(x, y, sz)", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SizesThatDoNotCountTheBubblesAreRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            bubblechart([1 2 3], [1 2 3], [1 2]);
            """);

        Assert.Contains("sz has 2 values but x has 3", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMisspeltOptionNamesTheOnesThatAreReal()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            bubblechart([1 2 3], [1 2 3], [1 2 3], 'MarkerFaceColour', 'r');
            """);

        Assert.Contains("MarkerFaceColour", message, StringComparison.Ordinal);
        Assert.Contains("MarkerFaceColor", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASizeRangeThatDoesNotIncreaseIsRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            bubblesize([30 10]);
            """);

        Assert.Contains("two increasing numbers", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnArrangementThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            bubblechart([1 2], [1 2], [1 2]);
            bubblelegend('Style', 'sideways');
            """);

        Assert.Contains("sideways", message, StringComparison.Ordinal);
        Assert.Contains("vertical, horizontal or telescopic", message, StringComparison.Ordinal);
    }
}
