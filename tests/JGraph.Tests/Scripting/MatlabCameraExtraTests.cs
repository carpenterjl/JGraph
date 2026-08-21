using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave F: the camera verbs as a script sees them — the two matrix builders, the roll, and the
/// three that move the view by changing what the limits admit.
/// </summary>
[Collection("JG facade")]
public class MatlabCameraExtraTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCameraExtraTests() => JG.Reset();

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
    public async Task ViewmtxAnswersAMatrixAndDoesNotTouchTheAxes()
    {
        await RunAsserting("""
            figure(1);
            surf(peaks(8));
            before = view;

            A = viewmtx(0, 90);
            disp(size(A, 1));
            disp(size(A, 2));
            disp(A(1, 1));
            disp(A(4, 4));
            disp(isequal(before, view));

            % The perspective form divides by depth; the orthographic one does not.
            disp(isequal(viewmtx(0, 90, 0), A));
            P = viewmtx(-37.5, 30, 25);
            disp(P(4, 3) < 0);
            """);

        Assert.Equal(new[] { "4", "4", "1", "1", "true", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task MakehgtformComposesItsStepsInOrder()
    {
        await RunAsserting("""
            I = makehgtform();
            disp(isequal(I, eye(4)));

            T = makehgtform('translate', [1 2 3]);
            disp(T(1, 4));
            disp(T(3, 4));
            disp(isequal(T, makehgtform('translate', 1, 2, 3)));

            % One number scales all three, which is the only place a lone value is not "x only".
            S = makehgtform('scale', 2);
            disp(S(2, 2));
            disp(S(3, 3));

            % Rotate then translate is not translate then rotate.
            a = makehgtform('translate', [1 0 0], 'zrotate', pi/2);
            b = makehgtform('zrotate', pi/2, 'translate', [1 0 0]);
            disp(isequal(a, b));
            disp(round(a(1, 4)));
            disp(round(b(2, 4)));

            % axisrotate about z is the zrotate it generalizes.
            disp(max(max(abs(makehgtform('axisrotate', [0 0 1], 0.7) - makehgtform('zrotate', 0.7)))) < 1e-12);
            """);

        Assert.Equal(
            new[] { "true", "1", "3", "true", "2", "2", "false", "1", "1", "true" },
            _output.NormalLines);
    }

    [Fact]
    public async Task CamrollAccumulatesAndReadsBackThroughTheHandle()
    {
        await RunAsserting("""
            figure(1);
            surf(peaks(8));
            ax = gca;
            disp(get(ax, 'Roll'));
            camroll(15);
            camroll(10);
            disp(get(ax, 'Roll'));
            set(ax, 'Roll', 0);
            disp(get(ax, 'Roll'));
            """);

        Assert.Equal(new[] { "0", "25", "0" }, _output.NormalLines);
    }

    [Fact]
    public async Task CamdollyAndCampanSlideTheLimitsWithoutResizingThem()
    {
        await RunAsserting("""
            figure(1);
            plot(1:10, 1:10);
            before = xlim;

            camdolly(0.5, 0, 0);
            after = xlim;
            disp(round((after(2) - after(1)) - (before(2) - before(1)), 6));
            disp(after(1) > before(1));

            % 'data' is a shift in the units the axes is drawn in, so it is exactly what was asked.
            camdolly(2, 0, 0, 'movetarget', 'data');
            moved = xlim;
            disp(round(moved(1) - after(1), 6));

            campan(5, 0);
            panned = xlim;
            disp(panned(1) > moved(1));
            """);

        Assert.Equal(new[] { "0", "true", "2", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task CamlookatFitsTheAxesAroundWhatItIsGiven()
    {
        await RunAsserting("""
            figure(1);
            h = plot(1:10, 1:10);
            hold on;
            plot(1:10, (1:10) + 100);

            camlookat(h);
            y = ylim;

            % Framed on the first line alone, with a margin, so the second is out of view.
            disp(y(1) < 1);
            disp(y(2) < 20);

            camlookat();
            all = ylim;
            disp(all(2) > 100);
            """);

        Assert.Equal(new[] { "true", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task CamprojChoosesTheProjectionAndSaysWhatElseItTakes()
    {
        await RunAsserting("""
            figure(1);
            surf(peaks(8));
            disp(camproj);
            camproj('perspective');
            disp(camproj);
            camproj('orthographic');
            disp(camproj);
            """);

        // Since M74 the word is real: perspective is a projection the renderer draws, not a request
        // it accepts and forgets.
        Assert.Equal(new[] { "orthographic", "perspective", "orthographic" }, _output.NormalLines);

        Assert.Contains("orthographic", await RunExpectingFailure("""
            figure(1);
            surf(peaks(8));
            camproj('fisheye');
            """), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCameraVerbsAimAtANamedAxesWithoutMovingTheCurrentOne()
    {
        await RunAsserting("""
            figure(1);
            subplot(2, 1, 1);
            first = gca;
            surf(peaks(8));
            subplot(2, 1, 2);
            second = gca;
            plot(1:5, 1:5);

            camroll(first, 20);
            disp(gca == second);
            disp(get(first, 'Roll'));
            disp(get(second, 'Roll'));
            """);

        Assert.Equal(new[] { "true", "20", "0" }, _output.NormalLines);
    }
}
