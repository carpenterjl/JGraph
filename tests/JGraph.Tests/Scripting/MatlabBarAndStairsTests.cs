using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave B: <c>bar</c>, <c>barh</c> and <c>stairs</c> as scripts write them. None of the three is
/// a new kind of object — a horizontal chart and a stairstep are properties — so these tests are
/// mostly about the argument forms finding the right property.
/// </summary>
[Collection("JG facade")]
public class MatlabBarAndStairsTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabBarAndStairsTests() => JG.Reset();

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
    public async Task BarAnswersWithAHandleCarryingMatlabsOwnPropertyNames()
    {
        await RunAsserting("""
            figure(1);
            h = bar(1:4, [3 5 2 6]);
            disp(get(h, 'Type'));
            disp(get(h, 'BarWidth'));
            disp(get(h, 'BaseValue'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'LineStyle'));
            disp(get(h, 'Horizontal'));
            """);

        Assert.Equal(new[] { "bar", "0.8", "0", "1", "-", "off" }, _output.NormalLines);
    }

    [Fact]
    public async Task ValuesAloneStandAtOneTwoThreeAndATrailingScalarIsTheBarWidth()
    {
        await RunAsserting("""
            figure(1);
            a = bar([3 5 2]);
            x = get(a, 'XData');
            disp(x(1));
            disp(x(3));

            b = bar(1:3, [3 5 2], 0.5);
            disp(get(b, 'BarWidth'));

            % Two scalars are a single bar, not a value and a width.
            c = bar(2, 5);
            disp(get(c, 'XData'));
            disp(get(c, 'YData'));
            """);

        Assert.Equal(new[] { "1", "3", "0.5", "[2]", "[5]" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMatrixGroupsOneSeriesPerColumnUnlessItIsToldToStack()
    {
        await RunAsserting("""
            figure(1);
            hs = bar(1:3, [1 4; 2 5; 3 6]);
            disp(numel(hs));
            disp(get(hs(2), 'YData'));
            yl = ylim;
            disp(yl(2) < 9);

            ss = bar(1:3, [1 4; 2 5; 3 6], 'stacked');
            disp(get(ss(2), 'YData'));
            sl = ylim;
            disp(sl(2) >= 9);
            """);

        Assert.Equal(
            new[] { "2", "[4, 5, 6]", "true", "[4, 5, 6]", "true" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ALoneWordIsAColourOrALayoutAndTheOptionTailFollowsIt()
    {
        await RunAsserting("""
            figure(1);
            h = bar(1:3, [1 2 3], 'r', 'FaceAlpha', 0.4, 'LineWidth', 2, 'BaseValue', 1);
            c = get(h, 'FaceColor');
            disp(c(1) > c(3));
            disp(get(h, 'FaceAlpha'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'BaseValue'));

            % The legacy histc layout widens the bars until they touch.
            g = bar(1:3, [1 2 3], 'histc');
            disp(get(g, 'BarWidth'));
            """);

        Assert.Equal(new[] { "true", "0.4", "2", "1", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task BarhIsTheSameChartWithTheAxesSwapped()
    {
        await RunAsserting("""
            figure(1);
            h = barh(1:3, [1 2 3], 'stacked');
            disp(get(h, 'Horizontal'));
            disp(get(h, 'Type'));

            % The values now run along X, so that is the axis they set the limits on.
            xl = xlim;
            disp(xl(2) >= 3);
            """);

        Assert.Equal(new[] { "on", "bar", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task StairsIsALineThatKnowsItIsOneAndTakesEveryLineOption()
    {
        await RunAsserting("""
            figure(1);
            h = stairs(1:4, [3 5 2 6], 'r--o', 'LineWidth', 2);
            disp(get(h, 'Type'));
            disp(get(h, 'LineStyle'));
            disp(get(h, 'Marker'));
            disp(get(h, 'LineWidth'));
            disp(numel(get(h, 'YData')));
            disp(numel(findobj(gcf, 'Type', 'stair')));
            """);

        Assert.Equal(new[] { "stair", "--", "o", "2", "4", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task TwoOutputsHandBackTheStairstepPathAndDrawNothing()
    {
        await RunAsserting("""
            figure(1);
            [xb, yb] = stairs(1:3, [4 5 6]);
            disp(numel(xb));
            disp(xb);
            disp(yb);
            disp(numel(findobj(gcf, 'Type', 'stair')));
            """);

        Assert.Equal(
            new[] { "6", "[1, 2, 2, 3, 3, 3]", "[4, 4, 5, 5, 6, 6]", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ABarIsDrawnOnANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            h = bar(first, 1:3, [1 2 3], 'DisplayName', 'load');
            disp(gca == second);
            disp(get(h, 'DisplayName'));
            disp(numel(get(first, 'Children')));
            """);

        Assert.Equal(new[] { "true", "load", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnOptionThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            bar(1:3, [1 2 3], 'BarWith', 0.5);
            """);

        Assert.Contains("BarWith", message, StringComparison.Ordinal);
        Assert.Contains("BarWidth", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Found by <c>stess_27.m</c>: given heights and nothing else, <c>bar</c> and <c>area</c> built
    /// their own coordinates and counted from 1, while <c>plot</c>, <c>stem</c> and <c>stairs</c>
    /// let the figure facade choose and got its 0-based numbering — so two verbs drew the same data
    /// one place apart in the same axes. MATLAB counts every one of them from 1.
    /// </summary>
    [Fact]
    public async Task EveryVerbGivenHeightsAloneCountsItsSamplesFromOne()
    {
        await RunAsserting("""
            figure(1);
            disp(get(plot([3 5 2 6]), 'XData'));
            disp(get(stem([3 5 2 6]), 'XData'));
            disp(get(stairs([3 5 2 6]), 'XData'));
            disp(get(bar([3 5 2 6]), 'XData'));
            disp(get(area([3 5 2 6]), 'XData'));

            % The two-output form describes the same steps, so it starts in the same place.
            [xb, ~] = stairs([3 5 2 6]);
            disp(xb(1));

            % Named x values are still the values named.
            disp(get(plot([10 20 30], [1 2 3]), 'XData'));
            """);

        Assert.Equal(
            new[]
            {
                "[1, 2, 3, 4]", "[1, 2, 3, 4]", "[1, 2, 3, 4]", "[1, 2, 3, 4]", "[1, 2, 3, 4]",
                "1", "[10, 20, 30]",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheTwoOutputStairsFormRefusesArgumentsItCannotDescribe()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            [xb, yb] = stairs(1:3, [1 2 3], 'r--');
            """);

        Assert.Contains("two-output form", message, StringComparison.Ordinal);
    }
}
