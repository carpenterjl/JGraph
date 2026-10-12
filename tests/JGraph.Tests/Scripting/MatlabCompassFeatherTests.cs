using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M56 wave D: the four verbs that draw what the object model already had. Since open item 112
/// (ADR 0229) <c>compass</c> and <c>feather</c> are R2025b's: a line per arrow, five points each
/// from <c>compass.m</c>'s arrow, on one colour-order seat, with <c>feather</c>'s baseline;
/// <c>polar</c> is <c>polarplot</c> under its older name, and <c>polarbubblechart</c> is
/// <c>bubblechart</c>'s sizing read round a circle.
/// </summary>
[Collection("JG facade")]
public class MatlabCompassFeatherTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCompassFeatherTests() => JG.Reset();

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

    /// <summary>
    /// A line per arrow, each the five points of compass.m's arrow turned and scaled by its reading,
    /// read on the polar axes as angles and radii; all of them on the first one's seat.
    /// </summary>
    [Fact]
    public async Task CompassDrawsALinePerArrowFromTheMiddleOnOneSeat()
    {
        await RunAsserting("""
            figure(1);
            h = compass([1 0 -1], [0 2 0]);
            disp(get(gca, 'Type'));
            disp(size(h));
            disp(get(h(1), 'Type'));
            disp(numel(get(h(1), 'XData')));
            disp(round(get(h(2), 'YData') * 1000) / 1000);
            disp(round(get(h(2), 'XData') * 1000) / 1000);
            disp(isequal(get(h(1), 'Color'), get(h(3), 'Color')));
            disp(get(h(3), 'SeriesIndex'));
            """);

        Assert.Equal(
            new[]
            {
                "polaraxes", "[3, 1]", "line", "5", "[0, 2, 1.608, 2, 1.608]",
                "[0, 1.571, 1.67, 1.571, 1.471]", "true", "1",
            },
            _output.NormalLines);
    }

    /// <summary>
    /// A complex array is read as real and imaginary here, which is the opposite of how
    /// <c>polarplot</c> reads one: these verbs are handed components, that one is handed a position.
    /// </summary>
    [Fact]
    public async Task AComplexArrayIsReadAsComponentsNotAsAnAngleAndAMagnitude()
    {
        await RunAsserting("""
            figure(1);
            c = compass([1+1i, -2]);
            disp(numel(c));
            r = get(c(1), 'YData');
            disp(round(r(2) * 1000) / 1000);
            r = get(c(2), 'YData');
            disp(r(2));

            figure(2);
            p = polarplot([1+1i, -2]);
            disp(round(get(p, 'XData') * 1000) / 1000);
            """);

        Assert.Equal(
            new[] { "2", "1.414", "2", "[0.785, 3.142]" },
            _output.NormalLines);
    }

    /// <summary>
    /// The arrows are the readings, not a sample of a field, so nothing is scaled to fit: an arrow of
    /// length two is two units long, a feather's arrows sit one per sample along the x axis with a
    /// NaN between shaft and head, and a baseline runs under them.
    /// </summary>
    [Fact]
    public async Task FeatherLaysTheSameArrowsAlongTheXAxisOnSquarePaperAndScalesNothing()
    {
        await RunAsserting("""
            figure(1);
            f = feather([1 2 3], [1 -1 0]);
            disp(get(gca, 'Type'));
            disp(size(f));
            disp(round(get(f(1), 'XData') * 100) / 100);
            disp(round(get(f(1), 'YData') * 100) / 100);
            disp(get(f(4), 'XData'));
            disp(get(f(4), 'YData'));
            disp(get(gca, 'ColorOrderIndex'));
            """);

        Assert.Equal(
            new[]
            {
                "axes", "[4, 1]", "[1, 2, NaN, 1.72, 2, 1.88]", "[0, 1, NaN, 0.88, 1, 0.72]",
                "[1, 3]", "[0, 0]", "2",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheLineSpecAndTheOptionTailBothReachTheArrowsAndAnAxesHandleAimsThem()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = polaraxes;
            subplot(2, 1, 2);
            second = polaraxes;

            h = compass(first, [1 1], [1 -1], 'LineWidth', 3, 'ShowArrowHead', 'off');
            disp(gca == second);
            disp(get(h(2), 'LineWidth'));
            disp(numel(get(h(1), 'XData')));
            disp(numel(get(first, 'Children')));

            figure(2);
            spec = compass([1 1], [1 -1], 'r');
            disp(get(spec(2), 'Color'));
            """);

        Assert.Equal(
            new[] { "true", "3", "2", "2", "[1, 0, 0]" },
            _output.NormalLines);
    }

    /// <summary>
    /// compass.m leaves the colour count where its lines took it, so a second compass on hold takes
    /// the colour after them all; feather.m hands the next plot the seat after its own.
    /// </summary>
    [Fact]
    public async Task ASecondCompassOnHoldTakesTheColourAfterAllTheArrows()
    {
        await RunAsserting("""
            figure(1);
            h = compass([1 2], [0 0]);
            hold on;
            k = compass(1, 1);
            disp(get(h(1), 'SeriesIndex'));
            disp(get(h(2), 'SeriesIndex'));
            disp(get(k, 'SeriesIndex'));
            """);

        Assert.Equal(new[] { "1", "1", "3" }, _output.NormalLines);
    }

    /// <summary>The older name for the same chart, line spec and all.</summary>
    [Fact]
    public async Task PolarIsPolarplotUnderTheNameThatCameFirst()
    {
        await RunAsserting("""
            figure(1);
            p = polar([0 1 2], [1 2 3], 'r--');
            disp(get(gca, 'Type'));
            disp(get(p, 'Type'));
            disp(get(p, 'Color'));
            disp(get(p, 'XData'));
            disp(get(p, 'YData'));
            """);

        Assert.Equal(
            new[] { "polaraxes", "line", "[1, 0, 0]", "[0, 1, 2]", "[1, 2, 3]" },
            _output.NormalLines);
    }

    /// <summary>
    /// Being round changes where a bubble sits and nothing about how big it is: the sizes are still
    /// data values read against the axes' bubble scale, exactly as on square paper.
    /// </summary>
    [Fact]
    public async Task PolarBubbleChartSizesItsBubblesTheWayBubblechartDoes()
    {
        await RunAsserting("""
            figure(1);
            b = polarbubblechart([0 1 2], [1 2 3], [10 20 30]);
            disp(get(gca, 'Type'));
            disp(get(b, 'Type'));
            disp(get(b, 'SizeData'));
            disp(get(b, 'XData'));

            figure(2);
            c = polarbubblechart([0 1], [1 2], [10 20], 'DisplayName', 'sweep');
            disp(get(c, 'DisplayName'));
            """);

        Assert.Equal(
            new[] { "polaraxes", "scatter", "[10, 20, 30]", "[0, 1, 2]", "sweep" },
            _output.NormalLines);
    }

    [Fact]
    public async Task OneComponentArrayIsNotEnoughForAnArrow()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            compass([1 2]);
            """);

        Assert.Contains("compass(u, v)", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMisspelledOptionIsRefusedByName()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            feather([1 2], [1 2], 'Colour', 'r');
            """);

        Assert.Contains("Colour", message, StringComparison.Ordinal);
    }
}
