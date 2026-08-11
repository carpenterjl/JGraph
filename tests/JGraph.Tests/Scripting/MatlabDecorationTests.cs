using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave E: the decoration verbs as a script sees them — the two title lines and their text
/// options, the frame, the reference lines, the contour labels, and texlabel.
/// </summary>
[Collection("JG facade")]
public class MatlabDecorationTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDecorationTests() => JG.Reset();

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
    public async Task TheTitleFamilyWritesTextAndTakesTheSameOptions()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            title('Growth', 'Color', 'r', 'FontSize', 14);
            subtitle('measured at 20 C', 'FontAngle', 'italic');
            sgtitle('Run 7');
            xlabel('t', 'FontWeight', 'bold');

            ax = gca;
            disp(get(ax, 'Title'));
            disp(get(ax, 'Subtitle'));
            disp(get(ax, 'XLabel'));
            disp(get(gcf, 'Title'));
            """);

        Assert.Equal(new[] { "Growth", "measured at 20 C", "t", "Run 7" }, _output.NormalLines);
    }

    [Fact]
    public async Task ATitleOptionThatIsNotOneNamesTheOnesThatAre()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            title('x', 'Colr', 'r');
            """);

        Assert.Contains("Colr", message, StringComparison.Ordinal);
        Assert.Contains("FontSize", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoxNamesTheFrameAndReadsBackThroughTheHandle()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            ax = gca;
            disp(get(ax, 'Box'));
            box off;
            disp(get(ax, 'Box'));
            box;
            disp(get(ax, 'Box'));
            set(ax, 'Box', 'off');
            disp(get(ax, 'Box'));
            """);

        Assert.Equal(new[] { "on", "off", "on", "off" }, _output.NormalLines);
    }

    [Fact]
    public async Task AConstantLineIsAHandleWithItsOwnPropertiesAndDoesNotMoveTheAxes()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            before = ylim;

            h = yline(5, '--r', 'threshold');
            disp(get(h, 'Type'));
            disp(get(h, 'Value'));
            disp(get(h, 'InterceptAxis'));
            disp(get(h, 'Label'));
            disp(get(h, 'LineStyle'));

            % A line far outside the data must not stretch the view to reach it.
            yline(100000);
            disp(isequal(before, ylim));
            """);

        Assert.Equal(
            new[] { "constantline", "5", "y", "threshold", "--", "true" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AVectorDrawsOneLinePerValueAndTheOptionsReachEveryOne()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            v = xline([2 4 6], 'LineWidth', 3);
            disp(numel(v));
            disp(get(v(1), 'LineWidth'));
            disp(get(v(3), 'Value'));
            disp(numel(findobj(gcf, 'Type', 'constantline')));
            """);

        Assert.Equal(new[] { "3", "3", "6", "3" }, _output.NormalLines);
    }

    [Fact]
    public async Task AConstantLineTakesItsLabelAndAlignmentByName()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            h = xline(3, 'Label', 'start', 'LabelHorizontalAlignment', 'left', 'Color', 'g');
            disp(get(h, 'Label'));
            c = get(h, 'Color');
            disp(c(2) > c(1));
            """);

        Assert.Equal(new[] { "start", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ContourAnswersWithItsMatrixAndItsHandleAndClabelLabelsIt()
    {
        await RunAsserting("""
            figure(1);
            [X, Y] = meshgrid(-2:0.25:2, -2:0.25:2);
            Z = X .* exp(-X.^2 - Y.^2);
            [C, h] = contour(X, Y, Z);

            disp(size(C, 1));
            disp(get(h, 'Type'));
            disp(get(h, 'ShowText'));

            clabel(C, h);
            disp(get(h, 'ShowText'));

            levels = get(h, 'LevelList');
            clabel(C, h, levels(2));
            disp(numel(get(h, 'LevelList')));
            """);

        Assert.Equal(new[] { "2", "contour", "off", "on", "8" }, _output.NormalLines);
    }

    [Fact]
    public async Task ClabelSaysWhatItWantedAndRefusesTheManualForm()
    {
        Assert.Contains("clabel(C, h)", await RunExpectingFailure("""
            figure(1);
            plot(1:10, 1:10);
            clabel(1, 2);
            """), StringComparison.Ordinal);

        Assert.Contains("cannot", await RunExpectingFailure("""
            figure(1);
            [X, Y] = meshgrid(-2:0.5:2, -2:0.5:2);
            [C, h] = contour(X, Y, X + Y);
            clabel(C, h, 'manual');
            """), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TexlabelTranslatesGreekSubscriptsAndExponents()
    {
        await RunAsserting("""
            disp(texlabel('lambda12^(3y)'));
            disp(texlabel('alpha/beta'));
            disp(texlabel('lambda', 'literal'));
            disp(texlabel('x^2'));
            """);

        Assert.Equal(
            new[] { "\\lambda_{12}^{3y}", "\\alpha/\\beta", "lambda", "x^{2}" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ANamedAxesIsDecoratedWithoutBecomingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            subplot(2, 1, 2);
            second = gca;

            title(first, 'top');
            subtitle(first, 'under it');
            box(first, 'off');
            xline(first, 4);

            disp(gca == second);
            disp(get(first, 'Title'));
            disp(get(first, 'Subtitle'));
            disp(get(first, 'Box'));
            disp(numel(get(first, 'Children')));
            """);

        Assert.Equal(new[] { "true", "top", "under it", "off", "1" }, _output.NormalLines);
    }

    /// <summary>
    /// M54 wave G, found by stess_26: <c>contour(Z)</c> — the shortest and commonest form there is —
    /// errored, because the verb insisted on x and y. It now indexes the grid by row and column, the
    /// way <c>surf(Z)</c> has since M20b.
    /// </summary>
    [Fact]
    public async Task ContourTakesHeightsWithNoGridAndStillTakesALevelArgument()
    {
        await RunAsserting("""
            figure(1);
            Z = peaks(20);
            h = contour(Z);
            disp(strcmp(get(h, 'Type'), 'contour'));

            % The x it made up runs 1..columns, which is what the grid form would have been given,
            % so the axes it fitted itself to spans the column numbers.
            x = xlim;
            disp(x(1) <= 1);
            disp(x(2) >= 20);

            % A second argument is the levels, not a y.
            hold on;
            n = contour(Z, 4);
            disp(numel(get(n, 'LevelList')));
            v = contour(Z, [-2 0 2]);
            disp(numel(get(v, 'LevelList')));

            % And the gridded form still means what it meant.
            [X, Y] = meshgrid(1:20, 1:20);
            g = contour(X, Y, Z, 4);
            disp(numel(get(g, 'LevelList')));
            """);

        Assert.Equal(new[] { "true", "true", "true", "4", "3", "4" }, _output.NormalLines);
    }
}
