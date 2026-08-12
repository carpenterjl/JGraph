using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M56 wave C: <c>polarhistogram</c> and <c>rose</c> as a script writes them. The counting itself is
/// the object's, and tested there; what these check is the argument surface, and above all that the
/// counts a polar histogram draws are the counts <c>histcounts</c> reports for the same call.
/// </summary>
[Collection("JG facade")]
public class MatlabPolarHistogramTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPolarHistogramTests() => JG.Reset();

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
    /// The claim the wave is built on: the wedges and <c>histcounts</c> are the same arithmetic, so a
    /// script can check a chart against the numbers rather than against a picture.
    /// </summary>
    [Fact]
    public async Task TheCountsAreTheOnesHistcountsReportsForTheSameCall()
    {
        await RunAsserting("""
            figure(1);
            t = [0.1 0.2 1.0 1.1 1.2 3.0 4.0 5.0 6.0 6.2];
            h = polarhistogram(t, 4);
            disp(get(gca, 'Type'));
            disp(get(h, 'Type'));
            disp(get(h, 'BinCounts'));
            disp(histcounts(t, 4));
            disp(isequal(get(h, 'BinCounts'), histcounts(t, 4)));
            disp(get(h, 'NumBins'));
            """);

        Assert.Equal(
            new[] { "polaraxes", "histogram", "[5, 1, 1, 3]", "[5, 1, 1, 3]", "true", "4" },
            _output.NormalLines);
    }

    /// <summary>
    /// Bearings that point the same way are counted together. A call that names its own edges has said
    /// which turn it means, so nothing is wrapped under it.
    /// </summary>
    [Fact]
    public async Task AnglesAreWrappedIntoOneTurnUnlessTheCallSaysWhereItsBinsGo()
    {
        await RunAsserting("""
            figure(1);
            wrapped = polarhistogram([-0.1 0.1], 2);
            disp(get(wrapped, 'BinCounts'));

            figure(2);
            asked = polarhistogram([-0.1 0.1], [-pi 0 pi]);
            disp(get(asked, 'BinCounts'));
            disp(round(get(asked, 'BinEdges') * 1000) / 1000);
            """);

        Assert.Equal(
            new[] { "[1, 1]", "[1, 1]", "[-3.142, 0, 3.142]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheNormalizationWordsAndTheDisplayStyleBothReachTheChart()
    {
        await RunAsserting("""
            figure(1);
            t = [0.2 0.3 0.4 3.0];
            p = polarhistogram(t, 2, 'Normalization', 'probability', 'DisplayStyle', 'stairs');
            disp(get(p, 'Values'));
            disp(sum(get(p, 'Values')));
            disp(get(p, 'DisplayStyle'));

            figure(2);
            c = polarhistogram(t, 2, 'Normalization', 'cdf');
            disp(get(c, 'Values'));
            """);

        Assert.Equal(
            new[] { "[0.75, 0.25]", "1", "stairs", "[0.75, 1]" },
            _output.NormalLines);
    }

    /// <summary>MATLAB's counts-only form: bins and heights, with no data behind them.</summary>
    [Fact]
    public async Task CountsCanBeGivenInsteadOfAnglesToCount()
    {
        await RunAsserting("""
            figure(1);
            h = polarhistogram('BinEdges', [0 pi 2*pi], 'BinCounts', [3 7]);
            disp(get(h, 'BinCounts'));
            disp(isempty(get(h, 'Data')));
            disp(get(h, 'NumBins'));
            """);

        Assert.Equal(new[] { "[3, 7]", "true", "2" }, _output.NormalLines);
    }

    [Fact]
    public async Task TheAppearanceTailReachesTheWedgesAndTheAxesHandleAimsThem()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = polaraxes;
            subplot(2, 1, 2);
            second = polaraxes;

            h = polarhistogram(first, [0.1 0.2 1.0], 2, ...
                'FaceColor', 'r', 'FaceAlpha', 0.4, 'LineWidth', 2, 'DisplayName', 'bearings');
            disp(gca == second);
            disp(get(h, 'FaceColor'));
            disp(get(h, 'FaceAlpha'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'DisplayName'));
            disp(numel(get(first, 'Children')));
            """);

        Assert.Equal(
            new[] { "true", "[1, 0, 0]", "0.4", "2", "bearings", "1" },
            _output.NormalLines);
    }

    /// <summary>
    /// <c>rose</c> divides the whole turn whatever the data covers — twenty petals by default — which
    /// is where it parts company with <c>polarhistogram</c>, and it draws an ordinary line.
    /// </summary>
    [Fact]
    public async Task RoseDrawsTwentyPetalsOverAFullTurnAsAnOrdinaryLine()
    {
        await RunAsserting("""
            figure(1);
            r = rose([0.1 0.2 1.0 3.0]);
            disp(get(gca, 'Type'));
            disp(get(r, 'Type'));
            disp(numel(get(r, 'XData')));
            disp(max(get(r, 'YData')));
            """);

        Assert.Equal(new[] { "polaraxes", "line", "80", "2" }, _output.NormalLines);
    }

    /// <summary>
    /// Asked for two outputs <c>rose</c> hands back the outline and draws nothing, which is what makes
    /// <c>polarplot(tout, rout)</c> reproduce the chart exactly.
    /// </summary>
    [Fact]
    public async Task AskedForTwoOutputsRoseAnswersWithThePetalOutlineAndNoChart()
    {
        await RunAsserting("""
            figure(1);
            [tout, rout] = rose([0.1 0.2 1.0 3.0], 4);
            disp(numel(tout));
            disp(round(tout(1:4) * 1000) / 1000);
            disp(rout(1:4));
            disp(numel(get(gca, 'Children')));
            """);

        Assert.Equal(
            new[] { "16", "[0, 0, 1.571, 1.571]", "[0, 3, 3, 0]", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task BinCentresSayWhereRosePutsItsPetals()
    {
        await RunAsserting("""
            figure(1);
            [tout, rout] = rose([0.4 0.6 2.0], [0.5 1.5 2.5]);
            disp(round(tout(1:4) * 100) / 100);
            disp(rout(1:4));
            disp(numel(tout));
            """);

        Assert.Equal(new[] { "[0, 0, 1, 1]", "[0, 2, 2, 0]", "12" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMisspelledOptionNamesTheOnesThatExist()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polarhistogram([0.1 0.2], 'Normalisation', 'pdf');
            """);

        Assert.Contains("Normalisation", message, StringComparison.Ordinal);
        Assert.Contains("'Normalization'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EdgesAndACountCannotBothSayWhereTheBinsGo()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polarhistogram([0.1 0.2], [0 1 2], 'BinWidth', 0.5);
            """);

        Assert.Contains("bin edges already say where every bin is", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsWithoutEdgesSaysWhatIsMissing()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polarhistogram('BinCounts', [3 7]);
            """);

        Assert.Contains("needs 'BinEdges'", message, StringComparison.Ordinal);
    }
}
