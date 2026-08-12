using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M56 wave A: <c>polaraxes</c> as a script writes it. The verb itself is small — the milestone's
/// work is the mode — so what these check is that the mode is reachable, readable and settable
/// through the ordinary handle surface, with no property code written for it.
/// </summary>
[Collection("JG facade")]
public class MatlabPolarAxesTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPolarAxesTests() => JG.Reset();

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
    public async Task PolaraxesAnswersWithAHandleThatKnowsItIsACircle()
    {
        await RunAsserting("""
            figure(1);
            pax = polaraxes;
            disp(get(pax, 'Type'));
            disp(get(pax, 'IsPolar'));
            disp(get(pax, 'ThetaZeroLocation'));
            disp(get(pax, 'ThetaDirection'));
            disp(get(pax, 'ThetaAxisUnits'));
            disp(get(pax, 'RAxisLocation'));
            """);

        Assert.Equal(
            new[] { "polaraxes", "on", "right", "counterclockwise", "degrees", "80" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheTurnCanBeSetInTheCallOrAfterwards()
    {
        await RunAsserting("""
            figure(1);
            pax = polaraxes('ThetaDirection', 'clockwise', 'ThetaZeroLocation', 'top');
            disp(get(pax, 'ThetaDirection'));
            disp(get(pax, 'ThetaZeroLocation'));

            set(pax, 'RAxisLocation', 45, 'ThetaAxisUnits', 'radians');
            disp(get(pax, 'RAxisLocation'));
            disp(get(pax, 'ThetaAxisUnits'));
            """);

        Assert.Equal(new[] { "clockwise", "top", "45", "radians" }, _output.NormalLines);
    }

    /// <summary>
    /// A polar axes is not of type axes, as it is not in MATLAB, so a script sweeping a figure for
    /// Cartesian axes does not pick one up by accident.
    /// </summary>
    [Fact]
    public async Task FindobjTellsACircleApartFromSquarePaper()
    {
        await RunAsserting("""
            figure(1);
            subplot(1, 2, 1);
            plot([1 2 3]);
            subplot(1, 2, 2);
            polaraxes;

            disp(numel(findobj(gcf, 'Type', 'polaraxes')));
            disp(numel(findobj(gcf, 'Type', 'axes')));
            """);

        Assert.Equal(new[] { "1", "1" }, _output.NormalLines);
    }

    /// <summary>
    /// Polar is a mode, so drawing something else in the same axes puts the paper back — the bargain
    /// <c>Is3D</c> already struck. Holding is what keeps it, which is MATLAB's rule for every verb.
    /// </summary>
    [Fact]
    public async Task DrawingAnOrdinaryChartPutsTheSquarePaperBackUnlessTheAxesIsHeld()
    {
        await RunAsserting("""
            figure(1);
            polaraxes;
            plot([1 2 3]);
            disp(get(gca, 'Type'));

            polaraxes;
            hold on;
            plot([1 2 3]);
            disp(get(gca, 'Type'));
            """);

        Assert.Equal(new[] { "axes", "polaraxes" }, _output.NormalLines);
    }

    [Fact]
    public async Task NamingAnAxesSelectsItRatherThanClearingIt()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = polaraxes;
            subplot(2, 1, 2);
            second = polaraxes;

            disp(gca == second);
            polaraxes(first);
            disp(gca == first);
            """);

        Assert.Equal(new[] { "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnOddPropertyTailSaysWhatTheShapeShouldBe()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polaraxes('ThetaDirection');
            """);

        Assert.Contains("name/value pairs", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APropertyThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            polaraxes('ThetaDirction', 'clockwise');
            """);

        Assert.Contains("ThetaDirection", message, StringComparison.Ordinal);
    }
}
