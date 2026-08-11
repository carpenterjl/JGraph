using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave G: <c>pareto</c>, <c>plotmatrix</c> and <c>plotyy</c> — the three verbs that arrange plots
/// and axes that already exist rather than drawing anything of their own.
/// </summary>
[Collection("JG facade")]
public class MatlabCompositionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCompositionTests() => JG.Reset();

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

    // --- pareto ---------------------------------------------------------------------------------

    [Fact]
    public async Task AParetoIsBarsRankedAgainstACumulativeCurveOnASecondRuler()
    {
        await RunAsserting("""
            figure(1);
            [h, ax] = pareto([20 60 10 110]);
            disp(get(h(1), 'Type'));
            disp(get(h(2), 'Type'));
            disp(get(h(1), 'YData'));
            disp(get(h(2), 'YData'));

            % The bars are measured in their own units and the curve in percent, so the two rulers are
            % pinned rather than fitted: the top of one has to mean the top of the other.
            disp(get(ax, 'YLim'));
            yyaxis right;
            disp(get(ax, 'YLim'));

            % The bars stand at 1, 2, 3, so the labels have to say which contributor each one is.
            disp(get(ax, 'XTickLabel'));
            """);

        Assert.Equal(
            new[]
            {
                "bar", "line", "[110, 60, 20]", "[55, 85, 95]", "[0, 200]", "[0, 100]", "{'4', '2', '1'}",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheThresholdAndTheTenBarCapBothDecideHowMuchIsShown()
    {
        await RunAsserting("""
            % Asking for all of it keeps the tail, and the curve ends at 100.
            figure(1);
            all = pareto([20 60 10 110], 1);
            disp(get(all(1), 'YData'));
            disp(get(all(2), 'YData'));

            % Half of it is carried by the first two of four equal contributors.
            figure(2);
            half = pareto([25 25 25 25], 0.5);
            disp(get(half(1), 'YData'));

            % Twenty equal contributors need all twenty to reach 100%, but ten bars is the most a
            % pareto chart says anything with.
            figure(3);
            capped = pareto(ones(1, 20), 1);
            disp(size(get(capped(1), 'YData')));
            """);

        Assert.Equal(
            new[] { "[110, 60, 20, 10]", "[55, 85, 95, 100]", "[25, 25]", "[1, 10]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task NamesFollowTheirValuesIntoTheOrderTheyAreRankedIn()
    {
        await RunAsserting("""
            figure(1);
            pareto([20 60 10 110], {'north', 'south', 'east', 'west'});
            disp(get(gca, 'XTickLabel'));

            % Numbers name the bars just as well as words do.
            figure(2);
            pareto([20 60 10 110], [7 8 9 10]);
            disp(get(gca, 'XTickLabel'));
            """);

        Assert.Equal(
            new[] { "{'west', 'south', 'north'}", "{'10', '8', '7'}" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AParetoIsDrawnOnANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            [h, ax] = pareto(first, [3 1 6]);
            disp(gca == second);
            disp(ax == first);
            disp(get(h(1), 'YData'));
            """);

        Assert.Equal(new[] { "true", "true", "[6, 3, 1]" }, _output.NormalLines);
    }

    // --- plotmatrix -----------------------------------------------------------------------------

    [Fact]
    public async Task APlotMatrixScattersEveryPairOfColumnsAndShowsEachOneAloneOnTheDiagonal()
    {
        await RunAsserting("""
            figure(1);
            [H, AX, Big, P, PAx] = plotmatrix([1 2 3; 2 4 6; 3 5 8]);
            disp(size(H));
            disp(size(AX));
            disp(size(P));
            disp(size(PAx));
            disp(get(H(1, 2), 'Type'));
            disp(get(P(1), 'Type'));

            % A column against itself would draw a straight line and say nothing, so the diagonal
            % carries no scatter at all.
            disp(H(2, 2));

            % MATLAB's BigAx is an invisible axes that exists only to hang a title on. An invisible
            % axes here draws nothing, its title included, so the slot answers with nothing and
            % sgtitle is what writes over the whole grid.
            disp(Big);
            """);

        Assert.Equal(
            new[] { "[3, 3]", "[3, 3]", "[1, 3]", "[1, 3]", "scatter", "histogram", "0", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ASecondSetOfColumnsGivesTheGridItsRowsAndDropsTheDiagonal()
    {
        await RunAsserting("""
            figure(1);
            [H, AX, ~, P] = plotmatrix([1 2 3; 2 4 7]', [1 2 3]');
            disp(size(H));
            disp(size(AX));
            disp(size(P));
            disp(get(H(1, 1), 'XData'));
            disp(get(H(1, 1), 'YData'));
            """);

        // One row of y against two columns of x, and nothing is a column against itself.
        Assert.Equal(
            new[] { "[1, 2]", "[1, 2]", "[1, 0]", "[1, 2, 3]", "[1, 2, 3]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ALineSpecColoursAndMarksEveryPlotInTheGrid()
    {
        await RunAsserting("""
            figure(1);
            H = plotmatrix([1 2; 3 4; 5 7], 'rs');
            disp(get(H(1, 2), 'Marker'));
            disp(get(H(1, 2), 'MarkerEdgeColor'));

            % The grid replaces what the figure held, the way drawing any other chart into it does.
            figure(2);
            plot([1 2 3], [1 2 3]);
            plotmatrix([1 2; 3 4; 5 7]);
            disp(size(get(gcf, 'Children')));
            """);

        Assert.Equal(new[] { "s", "[1, 0, 0]", "[1, 4]" }, _output.NormalLines);
    }

    // --- plotyy ---------------------------------------------------------------------------------

    [Fact]
    public async Task PlotYyMeasuresTwoSeriesAgainstTwoScalesOnOneAxes()
    {
        await RunAsserting("""
            figure(1);
            [AX, H1, H2] = plotyy([1 2 3], [1 2 3], [1 2 3], [100 200 300]);
            disp(size(AX));
            disp(get(AX(1), 'Type'));

            % Each side fits its own series, which is the whole point of drawing them together.
            disp(get(AX(1), 'Limits'));
            disp(get(AX(2), 'Limits'));

            % And each ruler wears the colour of the series it measures, because the tick numbers are
            % the only thing saying which curve they belong to.
            disp(get(H1, 'Color') == get(AX(1), 'Color'));
            disp(get(H2, 'Color') == get(AX(2), 'Color'));
            """);

        Assert.Equal(
            new[]
            {
                "[1, 2]", "numericruler", "[0.9, 3.1]", "[90, 310]",
                "[true, true, true]", "[true, true, true]",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task EachSideCanBeDrawnWithTheVerbItIsNamedBy()
    {
        await RunAsserting("""
            figure(1);
            [~, bars, stems] = plotyy([1 2 3], [1 2 3], [1 2 3], [5 6 7], 'bar', 'stem');
            disp(get(bars, 'Type'));
            disp(get(stems, 'Type'));

            % One verb named once draws both sides with it.
            figure(2);
            [~, left, right] = plotyy([1 2], [1 2], [1 2], [3 4], 'area');
            disp(get(left, 'Type'));
            disp(get(right, 'Type'));

            % A log verb takes the ruler it was named for with it, and no other.
            figure(3);
            AX = plotyy([1 2 3], [1 2 3], [1 2 3], [1 10 100], 'plot', 'semilogy');
            disp(get(AX(1), 'Scale'));
            disp(get(AX(2), 'Scale'));
            """);

        Assert.Equal(
            new[] { "bar", "stem", "area", "area", "linear", "log" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ARulerHandleLabelsAndLimitsTheSideItNames()
    {
        await RunAsserting("""
            figure(1);
            AX = plotyy([1 2 3], [1 2 3], [1 2 3], [100 200 300]);

            % Every axes verb takes a ruler where it takes an axes, which is what makes the two sides
            % of a plotyy addressable without yyaxis being told to switch first.
            ylabel(AX(1), 'volts');
            ylabel(AX(2), 'ohms');
            ylim(AX(1), [0 10]);
            yticks(AX(2), [0 150 300]);
            disp(get(AX(1), 'YLabel'));
            disp(get(AX(2), 'Label'));
            disp(get(AX(1), 'YLim'));
            disp(get(AX(2), 'TickValues'));

            % Naming a ruler does not move the active side, so a bare verb still says what it said.
            title(AX(1), 'both sides');
            disp(get(gca, 'Title'));
            """);

        Assert.Equal(
            new[] { "volts", "ohms", "[0, 10]", "[0, 150, 300]", "both sides" },
            _output.NormalLines);
    }

    // --- what these verbs refuse ----------------------------------------------------------------

    [Fact]
    public async Task AThresholdThatIsNotAFractionIsRefused()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            pareto([1 2 3], 2);
            """);

        Assert.Contains("fraction above 0 and at most 1", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValuesThatCannotBeContributionsAreRefused()
    {
        string negative = await RunExpectingFailure("figure(1); pareto([1 -2 3]);");
        Assert.Contains("none of the values may be negative", negative, StringComparison.Ordinal);

        string empty = await RunExpectingFailure("figure(2); pareto([0 0 0]);");
        Assert.Contains("add up to more than zero", empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVerbPlotYyCannotDrawWithNamesTheOnesItCan()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            plotyy([1 2], [1 2], [1 2], [1 2], 'pie');
            """);

        Assert.Contains("'pie'", message, StringComparison.Ordinal);
        Assert.Contains("semilogy", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlotMatrixCannotBeAimedAtAnAxesBecauseItLaysOutItsOwn()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            plotmatrix(gca, [1 2; 3 4]);
            """);

        Assert.Contains("lays out its own grid", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALetterShapedPropertyOnTheWrongRulerSaysSo()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            AX = plotyy([1 2], [1 2], [1 2], [3 4]);
            disp(get(AX(2), 'XLim'));
            """);

        Assert.Contains("XLim names a horizontal ruler", message, StringComparison.Ordinal);
    }
}
