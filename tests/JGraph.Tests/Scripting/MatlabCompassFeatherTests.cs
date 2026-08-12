using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M56 wave D: the four verbs that draw what the object model already had. <c>compass</c> and
/// <c>feather</c> are arrow fields with their automatic scaling switched off, <c>polar</c> is
/// <c>polarplot</c> under its older name, and <c>polarbubblechart</c> is <c>bubblechart</c>'s sizing
/// read round a circle — so what these check is the arranging, which is the only new thing.
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
    /// The components are Cartesian and the chart is not, so the verb's whole job is the conversion:
    /// every arrow starts in the middle, points along its bearing, and reaches its own length.
    /// </summary>
    [Fact]
    public async Task CompassTurnsComponentsIntoBearingsAndLengthsFromTheMiddle()
    {
        await RunAsserting("""
            figure(1);
            h = compass([1 0 -1], [0 2 0]);
            disp(get(gca, 'Type'));
            disp(round(get(h, 'XData') * 1000) / 1000);
            disp(get(h, 'YData'));
            disp(get(h, 'UData'));
            disp(get(h, 'VData'));
            """);

        Assert.Equal(
            new[] { "polaraxes", "[0, 1.571, 3.142]", "[0, 0, 0]", "[0, 0, 0]", "[1, 2, 1]" },
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
            disp(round(get(c, 'XData') * 1000) / 1000);
            disp(round(get(c, 'VData') * 1000) / 1000);

            figure(2);
            p = polarplot([1+1i, -2]);
            disp(round(get(p, 'XData') * 1000) / 1000);
            """);

        Assert.Equal(
            new[] { "[0.785, 3.142]", "[1.414, 2]", "[0.785, 3.142]" },
            _output.NormalLines);
    }

    /// <summary>
    /// The arrows are the readings, not a sample of a field, so nothing is scaled to fit: an arrow of
    /// length two is two units long, and a feather's arrows sit one per sample along the x axis.
    /// </summary>
    [Fact]
    public async Task FeatherLaysTheSameArrowsAlongTheXAxisOnSquarePaperAndScalesNothing()
    {
        await RunAsserting("""
            figure(1);
            f = feather([1 2 3], [1 -1 0]);
            disp(get(gca, 'Type'));
            disp(get(f, 'XData'));
            disp(get(f, 'YData'));
            disp(get(f, 'UData'));
            disp(get(f, 'VData'));
            disp(get(f, 'AutoScale'));
            """);

        Assert.Equal(
            new[] { "axes", "[1, 2, 3]", "[0, 0, 0]", "[1, 2, 3]", "[1, -1, 0]", "off" },
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
            disp(get(h, 'LineWidth'));
            disp(get(h, 'ShowArrowHead'));
            disp(numel(get(first, 'Children')));

            figure(2);
            spec = compass([1 1], [1 -1], 'r');
            disp(get(spec, 'Color'));
            """);

        Assert.Equal(
            new[] { "true", "3", "off", "1", "[1, 0, 0]" },
            _output.NormalLines);
    }

    /// <summary>
    /// The six arrays only mean anything together, so one may be replaced and a replacement of the
    /// wrong length is refused rather than leaving a field half rewritten.
    /// </summary>
    [Fact]
    public async Task AnArrowFieldsArraysCanBeWrittenBackThroughItsHandle()
    {
        await RunAsserting("""
            figure(1);
            h = compass([1 2], [0 0]);
            set(h, 'VData', [5 6]);
            disp(get(h, 'VData'));
            """);

        Assert.Equal(new[] { "[5, 6]" }, _output.NormalLines);

        string message = await RunExpectingFailure("""
            figure(2);
            h = compass([1 2], [0 0]);
            set(h, 'VData', [1 2 3]);
            """);

        Assert.Contains("VData", message, StringComparison.Ordinal);
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
    public async Task AMisspelledOptionNamesTheOnesThatExist()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            feather([1 2], [1 2], 'Colour', 'r');
            """);

        Assert.Contains("'Colour'", message, StringComparison.Ordinal);
        Assert.Contains("ShowArrowHead", message, StringComparison.Ordinal);
    }
}
