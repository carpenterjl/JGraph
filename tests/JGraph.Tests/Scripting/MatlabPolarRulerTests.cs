using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M56 wave E: the nine angular ruler verbs. r is an ordinary scale, so its five verbs are the M54
/// machinery pointed at <c>RAxis</c>; θ is stored in degrees whatever unit the axes speaks, so its
/// four convert at the boundary and answer through the same spoke arithmetic the renderer draws with —
/// which is what makes <c>thetaticks</c> unable to report spokes the chart is not drawing.
/// </summary>
[Collection("JG facade")]
public class MatlabPolarRulerTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPolarRulerTests() => JG.Reset();

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
    public async Task TheDefaultTurnIsWholeAndItsSpokesStandEveryThirtyDegrees()
    {
        await RunAsserting("""
            figure(1);
            polaraxes;
            polarplot([0 pi/4 pi/2], [1 2 3]);
            disp(thetalim);
            disp(rlim);
            disp(thetaticks);
            labs = thetaticklabels;
            disp(labs{2});
            """);

        Assert.Equal(
            new[]
            {
                "[0, 360]",
                "[0, 3]",
                "[0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330]",
                "30°",
            },
            _output.NormalLines);
    }

    /// <summary>
    /// The five r verbs are the Cartesian machinery pointed at the r ruler, and rlim('auto') hands the
    /// range back to the data the way ylim('auto') does — the rings refit to the largest radius drawn.
    /// </summary>
    [Fact]
    public async Task TheRingVerbsAreTheCartesianTickMachineryPointedAtTheRRuler()
    {
        await RunAsserting("""
            figure(1);
            polarplot([0 1 2], [1 2 3]);
            rlim([0 5]);
            disp(rlim);
            rticks([0 2 4]);
            disp(rticks);
            rticklabels({'lo','mid','hi'});
            r = rticklabels;
            disp(r{3});
            rtickformat('%.1f');
            disp(rtickformat);
            rtickangle(45);
            disp(rtickangle);
            rlim('auto');
            disp(rlim);
            """);

        Assert.Equal(
            new[] { "[0, 5]", "[0, 2, 4]", "hi", "0.0", "45", "[0, 3]" },
            _output.NormalLines);
    }

    /// <summary>
    /// thetalim cuts the circle to a wedge and the default spokes keep to it; 'auto' is the whole
    /// circle back, because θ is never fitted to the data — the circle is the chart.
    /// </summary>
    [Fact]
    public async Task ThetalimCutsTheCircleToAWedgeAndAutoIsTheWholeCircleBack()
    {
        await RunAsserting("""
            figure(1);
            polaraxes;
            thetalim([0 180]);
            disp(thetalim);
            disp(thetaticks);
            thetaticks([0 45 90 135 180]);
            disp(thetaticks);
            t = thetaticklabels;
            disp(t{2});
            thetaticklabels({'N','NE','E','SE','S'});
            named = thetaticklabels;
            disp(named{1});
            thetaticklabels('auto');
            thetalim('auto');
            disp(thetalim);
            """);

        Assert.Equal(
            new[]
            {
                "[0, 180]",
                "[0, 30, 60, 90, 120, 150, 180]",
                "[0, 45, 90, 135, 180]",
                "45°",
                "N",
                "[0, 360]",
            },
            _output.NormalLines);
    }

    /// <summary>A printf format owns the whole label, so the degree sign is its to drop.</summary>
    [Fact]
    public async Task APrintfFormatOwnsTheWholeLabelSoTheDegreeSignIsItsToDrop()
    {
        await RunAsserting("""
            figure(1);
            polaraxes;
            thetaticks([0 45 90]);
            thetatickformat('%g');
            t = thetaticklabels;
            disp(t{2});
            thetatickformat('auto');
            t = thetaticklabels;
            disp(t{2});
            """);

        Assert.Equal(new[] { "45", "45°" }, _output.NormalLines);
    }

    /// <summary>
    /// The ruler always holds degrees; ThetaAxisUnits governs the numbers crossing the boundary. So a
    /// turn set in radians reads back in radians, and reads in degrees again the moment the axes
    /// changes its mind — same ruler, same turn, two spellings.
    /// </summary>
    [Fact]
    public async Task RadiansUnitsConvertAtTheBoundaryAndTheRulerStillHoldsDegrees()
    {
        await RunAsserting("""
            figure(1);
            pax = polaraxes;
            set(pax, 'ThetaAxisUnits', 'radians');
            thetalim([0 pi]);
            disp(round(thetalim * 1000) / 1000);
            thetaticks([0 pi/2 pi]);
            disp(round(thetaticks * 1000) / 1000);
            t = thetaticklabels;
            disp(t{2});
            set(pax, 'ThetaAxisUnits', 'degrees');
            disp(thetalim);
            disp(thetaticks);
            """);

        Assert.Equal(
            new[] { "[0, 3.142]", "[0, 1.571, 3.142]", "1.571", "[0, 180]", "[0, 90, 180]" },
            _output.NormalLines);
    }

    /// <summary>
    /// The verb and the property spelling go through the same stores, so they cannot come to
    /// disagree — the M54 rule, now holding on a circle.
    /// </summary>
    [Fact]
    public async Task TheVerbAndThePropertySpellingCannotDisagree()
    {
        await RunAsserting("""
            figure(1);
            pax = polaraxes;
            rticks([0 3 6]);
            thetalim([0 270]);
            disp(isequal(get(pax, 'RTick'), rticks));
            disp(get(pax, 'ThetaLim'));
            set(pax, 'RLim', [0 8]);
            disp(rlim);
            """);

        Assert.Equal(new[] { "true", "[0, 270]", "[0, 8]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ANamedAxesIsConfiguredWithoutBecomingCurrent()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = polaraxes;
            subplot(2, 1, 2);
            second = polaraxes;
            rlim(first, [0 9]);
            thetaticks(first, [0 120 240]);
            disp(rlim(first));
            disp(thetaticks(first));
            disp(gca == second);
            """);

        Assert.Equal(new[] { "[0, 9]", "[0, 120, 240]", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ACartesianAxesIsRefusedByName()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            plot([1 2 3], [1 2 3]);
            rticks([1 2]);
            """);

        Assert.Contains("rticks", message, StringComparison.Ordinal);
        Assert.Contains("polaraxes", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackwardLimitsAndUnsortedTicksAreRefused()
    {
        string limits = await RunExpectingFailure("""
            figure(1);
            polaraxes;
            thetalim([180 0]);
            """);
        Assert.Contains("the second limit must exceed the first", limits, StringComparison.Ordinal);

        string ticks = await RunExpectingFailure("""
            figure(2);
            polaraxes;
            thetaticks([90 45]);
            """);
        Assert.Contains("tick values increase", ticks, StringComparison.Ordinal);
    }

    /// <summary>
    /// A turn asked for beyond 360° is trimmed to one: the chart cannot show an angle twice, and
    /// drawing the circle and a half literally would wind the frame over itself.
    /// </summary>
    [Fact]
    public async Task MoreThanOneTurnIsTrimmedToTheCircle()
    {
        await RunAsserting("""
            figure(1);
            polaraxes;
            thetalim([0 720]);
            disp(thetalim);
            """);

        Assert.Equal(new[] { "[0, 360]" }, _output.NormalLines);
    }
}
