using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M67 wave A: what the animated verbs do once something can show their steps.
/// <para>
/// The player is a seam rather than a window, which is what makes any of this testable: these install
/// a recording player that applies every step at once and remembers what it was asked to do, so the
/// step machinery — the one part a headless run never exercised — is checked here rather than only by
/// a person watching a figure. What a real window adds on top is timing, and that is the live check.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabAnimationTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;
    private readonly List<(int Steps, double Pace)> _played = new();

    public MatlabAnimationTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        // The seam is process-wide, so putting it back is not tidiness but correctness: the next test
        // in this collection must see the batch behaviour it was written against.
        ScriptAnimation.SetPlayer(null);
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Installs a player that runs every step immediately and records what it was given.</summary>
    private void RecordSteps() =>
        ScriptAnimation.SetPlayer((_, steps, pace) =>
        {
            _played.Add((steps.Count, pace));
            foreach (Action step in steps)
            {
                step();
            }

            return true;
        });

    /// <summary>Installs a player that refuses to run at all — a window the user closed mid-animation.</summary>
    private void RefuseToPlay() =>
        ScriptAnimation.SetPlayer((_, steps, pace) =>
        {
            _played.Add((steps.Count, pace));
            return false;
        });

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code,
            new ScriptContext(
                _output,
                (_, _) => { },
                _directory,
                resolvePath: null,
                figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private void Ok(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message + _output.ErrorText);

    private static T Sole<T>() where T : PlotObject =>
        Assert.IsType<T>(Assert.Single(JG.CurrentFigure.Axes[^1].Plots));

    // --- comet ------------------------------------------------------------------------------------

    [Fact]
    public void CometStepsAlongTheCurveAndLeavesTheWholeOfIt()
    {
        RecordSteps();
        Ok(Run("figure(1); comet(1:20, (1:20).^2);"));

        // Nineteen steps to travel the twenty points, and one more to put the whole curve back.
        Assert.Equal(20, Assert.Single(_played).Steps);
        Assert.Equal(20, Sole<LinePlot>().Data.Count);
    }

    [Fact]
    public void ACometThatIsNeverPlayedStillLeavesTheWholeCurve()
    {
        RefuseToPlay();
        Ok(Run("figure(1); comet(1:20, (1:20).^2);"));
        Assert.Equal(20, Sole<LinePlot>().Data.Count);
    }

    [Fact]
    public void TheTailFractionChangesTheStepsAndNotTheDrawing()
    {
        RecordSteps();
        Ok(Run("figure(1); comet(1:20, (1:20).^2, 0.5);"));
        Assert.Equal(20, Assert.Single(_played).Steps);
        Assert.Equal(20, Sole<LinePlot>().Data.Count);
    }

    // --- movie ------------------------------------------------------------------------------------

    [Fact]
    public void MovieDrawsItsLastFrameWithNothingToPlayItOn()
    {
        Ok(Run("figure(1); plot(1:10); f = getframe; movie(f);"));

        // The plot is gone and the picture of it is what is left, which is what MATLAB leaves too.
        RgbImagePlot screen = Sole<RgbImagePlot>();
        Assert.True(screen.Width > 0 && screen.Height > 0);
    }

    [Fact]
    public void MoviePlaysEveryFrameOfEveryRepeat()
    {
        RecordSteps();
        Ok(Run("figure(1); plot(1:10); f = getframe; g = getframe; movie([f g], 3, 24);"));

        (int steps, double pace) = Assert.Single(_played);
        Assert.Equal(6, steps);
        Assert.Equal(1.0 / 24, pace, 12);
    }

    [Fact]
    public void MovieRefusesFramesOfDifferentSizes()
    {
        ScriptRunResult result = Run("""
            figure(1); plot(1:10); f = getframe;
            g = f; g.cdata = f.cdata(1:10, 1:10, :);
            movie([f g]);
            """);
        Assert.False(result.Success);
        Assert.Contains("same size", result.Message + _output.ErrorText);
    }

    [Fact]
    public void MovieRefusesCdataThatIsNotAPicture()
    {
        ScriptRunResult result = Run("movie(struct('cdata', zeros(4, 4)));");
        Assert.False(result.Success);
        Assert.Contains("height-by-width-by-3", result.Message + _output.ErrorText);
    }

    // --- streamparticles --------------------------------------------------------------------------

    private const string Field = """
        figure(1);
        [x, y] = meshgrid(0:0.2:2, 0:0.2:2);
        u = ones(size(x)); v = zeros(size(y));
        lines = stream2(x, y, u, v, [0 0.4 0.8], [0.4 0.8 1.2]);
        """;

    [Fact]
    public void ParticlesStandStillUnlessTheyAreAskedToMove()
    {
        RecordSteps();
        Ok(Run(Field + "streamparticles(lines, 0.5);"));

        // MATLAB's Animate defaults to none, so a script that does not ask for motion gets a still
        // cloud whether or not a window is there to move it.
        Assert.Empty(_played);
    }

    [Fact]
    public void AskingForPassesAnimatesAndPutsTheCloudBack()
    {
        RecordSteps();
        Ok(Run(Field + "p = streamparticles(lines, 0.5, 'Animate', 2, 'FrameRate', 30);"));

        (int steps, double pace) = Assert.Single(_played);
        Assert.Equal((2 * 60) + 1, steps); // Two passes of sixty places, then back to the start.
        Assert.Equal(1.0 / 30, pace, 12);
    }

    [Fact]
    public void TheCloudKeepsItsSizeWhileItDrifts()
    {
        var counts = new List<int>();
        ScriptAnimation.SetPlayer((_, steps, _) =>
        {
            foreach (Action step in steps)
            {
                step();
                counts.Add(Sole<Scatter3DPlot>().X.Count);
            }

            return true;
        });

        Ok(Run(Field + "streamparticles(lines, 0.5, 'Animate', 1);"));

        // A particle that runs off the end of its line comes back at the start, so no frame is
        // emptier than any other: this is what stops the cloud draining away downstream.
        Assert.NotEmpty(counts);
        Assert.Single(counts.Distinct());
    }

    [Fact]
    public void AFrameRateOfZeroIsRefusedRatherThanDividedBy()
    {
        RecordSteps();
        ScriptRunResult result = Run(Field + "streamparticles(lines, 0.5, 'Animate', 1, 'FrameRate', 0);");
        Assert.False(result.Success);
        Assert.Contains("frames a second", result.Message + _output.ErrorText);
    }
}
