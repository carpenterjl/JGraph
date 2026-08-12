using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M56 wave B: <c>polarplot</c> and <c>polarscatter</c> as a script writes them. They draw the same
/// objects <c>plot</c> and <c>scatter</c> draw — the axes is what makes them round — so what these
/// check is the argument surface and the mode, not the geometry, which the rendering suite owns.
/// </summary>
[Collection("JG facade")]
public class MatlabPolarPlotTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPolarPlotTests() => JG.Reset();

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
    /// The angles are kept as they were given, in radians, as the series' x data — which is what lets
    /// every existing plot object draw on a circle without being told about one.
    /// </summary>
    [Fact]
    public async Task PolarplotKeepsTheAnglesItWasGivenAndMakesTheAxesACircle()
    {
        await RunAsserting("""
            figure(1);
            h = polarplot([0 pi/2 pi], [1 2 3]);
            disp(get(gca, 'Type'));
            disp(get(h, 'Type'));
            disp(round(get(h, 'XData') * 1000) / 1000);
            disp(get(h, 'YData'));
            """);

        Assert.Equal(
            new[] { "polaraxes", "line", "[0, 1.571, 3.142]", "[1, 2, 3]" },
            _output.NormalLines);
    }

    /// <summary>
    /// Values alone are spread over a full turn. Sample numbers — what <c>plot</c> would put on the
    /// other axis — are angles in radians here, and would wind a forty-point series round six times.
    /// </summary>
    [Fact]
    public async Task ValuesAloneAreSpreadOverAFullTurn()
    {
        await RunAsserting("""
            figure(1);
            h = polarplot([4 3 2 1]);
            theta = get(h, 'XData');
            disp(theta(1));
            disp(round(theta(4) * 1000) / 1000);
            disp(round(2 * pi * 1000) / 1000);
            """);

        Assert.Equal(new[] { "0", "6.283", "6.283" }, _output.NormalLines);
    }

    /// <summary>A complex array is angle and magnitude, which is MATLAB's reading of polarplot(z).</summary>
    [Fact]
    public async Task AComplexArrayIsReadAsAngleAndMagnitude()
    {
        await RunAsserting("""
            figure(1);
            h = polarplot([1+1i, 0+2i, -3+0i]);
            disp(round(get(h, 'YData') * 100) / 100);
            disp(round(get(h, 'XData') * 1000) / 1000);
            """);

        Assert.Equal(
            new[] { "[1.41, 2, 3]", "[0.785, 1.571, 3.142]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AMatrixDrawsOneSeriesPerColumnAndRepeatedGroupsDrawOneEach()
    {
        await RunAsserting("""
            figure(1);
            theta = [0; pi/2; pi; 3*pi/2];
            columns = polarplot(theta, [1 4; 2 5; 3 6; 4 7]);
            disp(numel(columns));

            figure(2);
            groups = polarplot(theta, [1;2;3;4], 'r-', theta, [2;3;4;5], 'b--');
            disp(numel(groups));
            disp(get(groups(2), 'Color'));
            """);

        Assert.Equal(new[] { "2", "2", "[0, 0, 1]" }, _output.NormalLines);
    }

    [Fact]
    public async Task TheSpecAndTheNameValueTailBothReachTheSeries()
    {
        await RunAsserting("""
            figure(1);
            h = polarplot([0 1 2], [1 2 3], 'r--o', 'LineWidth', 2.5, 'DisplayName', 'sweep');
            disp(get(h, 'Color'));
            disp(get(h, 'Marker'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'DisplayName'));
            """);

        Assert.Equal(new[] { "[1, 0, 0]", "o", "2.5", "sweep" }, _output.NormalLines);
    }

    /// <summary>
    /// Holding keeps the circle for the next angular verb, and an ordinary chart drawn unheld puts the
    /// square paper back — the bargain <c>Is3D</c> struck and polar keeps.
    /// </summary>
    [Fact]
    public async Task HoldKeepsTheCircleAndAnUnheldChartPutsThePaperBack()
    {
        await RunAsserting("""
            figure(1);
            polarplot([0 1 2], [1 2 3]);
            hold on;
            polarscatter([0 1 2], [2 3 4]);
            disp(get(gca, 'Type'));
            disp(numel(get(gca, 'Children')));

            figure(2);
            polarplot([0 1 2], [1 2 3]);
            plot([1 2 3]);
            disp(get(gca, 'Type'));
            disp(numel(get(gca, 'Children')));
            """);

        Assert.Equal(new[] { "polaraxes", "2", "axes", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task NamingAnAxesDrawsThereWithoutMakingItCurrent()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = polaraxes;
            subplot(2, 1, 2);
            second = polaraxes;

            polarplot(first, [0 1 2], [1 2 3]);
            disp(gca == second);
            disp(numel(get(first, 'Children')));
            disp(numel(get(second, 'Children')));
            """);

        Assert.Equal(new[] { "true", "1", "0" }, _output.NormalLines);
    }

    [Fact]
    public async Task PolarscatterTakesTheSizesTheColoursAndFilled()
    {
        await RunAsserting("""
            figure(1);
            s = polarscatter([0 1 2], [1 2 3], 60, 'filled');
            disp(get(gca, 'Type'));
            disp(get(s, 'Type'));
            disp(get(s, 'SizeData'));
            disp(round(get(s, 'MarkerFaceColor') * 100) / 100);

            figure(2);
            c = polarscatter([0 1 2], [1 2 3], 30, [4 5 6], 'Marker', 's');
            disp(get(c, 'Marker'));
            disp(get(c, 'CData'));
            """);

        Assert.Equal(
            new[]
            {
                "polaraxes", "scatter", "[60, 60, 60]", "[0, 0.45, 0.74]", "s", "[4, 5, 6]",
            },
            _output.NormalLines);
    }

    /// <summary>
    /// <c>scatter</c> gained the same tail in this wave, because the two verbs draw the same object and
    /// a property one understood and the other did not would be a difference with no cause.
    /// </summary>
    [Fact]
    public async Task ScatterTakesTheSameTailOnSquarePaper()
    {
        await RunAsserting("""
            figure(1);
            ax = gca;
            s = scatter(ax, [1 2 3], [4 5 6], 50, 'r', 'filled', 'LineWidth', 1.5);
            disp(get(gca, 'Type'));
            disp(get(s, 'MarkerFaceColor'));
            disp(get(s, 'LineWidth'));
            disp(get(s, 'SizeData'));
            """);

        Assert.Equal(
            new[] { "axes", "[1, 0, 0]", "1.5", "[50, 50, 50]" },
            _output.NormalLines);
    }

    /// <summary>
    /// A misspelled option used to be read as a line spec, which ignores the letters it does not know
    /// and draws the chart as though nothing were wrong. It now names the spellings that exist.
    /// </summary>
    [Fact]
    public async Task AMisspelledLineOptionNamesTheOnesThatExist()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polarplot([0 1 2], [1 2 3], 'LineWdth', 2);
            """);

        Assert.Contains("LineWdth", message, StringComparison.Ordinal);
        Assert.Contains("LineWidth", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWordThatIsNeitherMarkerNorOptionSaysWhatItCouldHaveBeen()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polarscatter([0 1 2], [1 2 3], 20, 'zz');
            """);

        Assert.Contains("not a colour, a marker, or an option", message, StringComparison.Ordinal);
        Assert.Contains("MarkerFaceColor", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAngleWithNoRadiusToGoWithItSaysSo()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polarscatter([0 1 2]);
            """);

        Assert.Contains("polarscatter needs both positions", message, StringComparison.Ordinal);
    }
}
