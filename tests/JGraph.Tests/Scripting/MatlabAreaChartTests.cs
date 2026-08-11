using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M55 wave A: <c>area</c> as a script writes it. This is the exemplar the rest of the milestone's
/// chart verbs follow — every documented argument form, the option tail, the handle it hands back,
/// and the complaint a misspelt option makes.
/// </summary>
[Collection("JG facade")]
public class MatlabAreaChartTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabAreaChartTests() => JG.Reset();

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
    public async Task AreaDrawsABandAndAnswersWithAHandleToIt()
    {
        await RunAsserting("""
            figure(1);
            h = area(1:5, [2 4 3 5 1]);
            disp(get(h, 'Type'));
            disp(get(h, 'BaseValue'));
            disp(get(h, 'ShowBaseLine'));
            disp(get(h, 'FaceAlpha'));
            disp(get(h, 'LineStyle'));
            disp(numel(get(h, 'YData')));
            """);

        Assert.Equal(new[] { "area", "0", "on", "1", "-", "5" }, _output.NormalLines);
    }

    [Fact]
    public async Task ValuesAloneStandAtOneTwoThreeAndATrailingScalarIsTheBaseValue()
    {
        await RunAsserting("""
            figure(1);
            a = area([2 4 3]);
            x = get(a, 'XData');
            disp(x(1));
            disp(x(3));

            b = area(1:3, [2 4 3], 1.5);
            disp(get(b, 'BaseValue'));

            % The base value floors the view as well as the fill.
            yl = ylim;
            disp(yl(1) <= 1.5);
            """);

        Assert.Equal(new[] { "1", "3", "1.5", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMatrixStacksOneBandPerColumnAndEachKeepsItsOwnValues()
    {
        await RunAsserting("""
            figure(1);
            hs = area(1:3, [1 10; 2 20; 3 30]);
            disp(numel(hs));
            disp(get(hs(1), 'YData'));
            disp(get(hs(2), 'YData'));

            % The stack reaches the total of the columns, not just the tallest of them.
            yl = ylim;
            disp(yl(2) >= 33);
            disp(numel(findobj(gcf, 'Type', 'area')));
            """);

        Assert.Equal(
            new[] { "2", "[1, 2, 3]", "[10, 20, 30]", "true", "2" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheOptionTailReachesEveryBandTheCallDrew()
    {
        await RunAsserting("""
            figure(1);
            hs = area(1:3, [1 10; 2 20; 3 30], ...
                'FaceAlpha', 0.5, 'LineStyle', ':', 'ShowBaseLine', 'off', 'LineWidth', 3);
            disp(get(hs(1), 'FaceAlpha'));
            disp(get(hs(2), 'FaceAlpha'));
            disp(get(hs(2), 'LineStyle'));
            disp(get(hs(2), 'ShowBaseLine'));
            disp(get(hs(1), 'LineWidth'));

            set(hs(1), 'FaceColor', [1 0 0]);
            c = get(hs(1), 'FaceColor');
            disp(c(1) > c(2));
            """);

        Assert.Equal(
            new[] { "0.5", "0.5", ":", "off", "3", "true" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ABandIsDecoratedAndNamedLikeAnyOtherSeries()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            h = area(first, 1:3, [1 2 3], 'DisplayName', 'load');
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
            area(1:3, [1 2 3], 'FaceAlfa', 0.5);
            """);

        Assert.Contains("FaceAlfa", message, StringComparison.Ordinal);
        Assert.Contains("FaceAlpha", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MismatchedLengthsSayWhichIsWhich()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            area(1:4, [1 2 3]);
            """);

        Assert.Contains("4", message, StringComparison.Ordinal);
        Assert.Contains("3", message, StringComparison.Ordinal);
    }
}
