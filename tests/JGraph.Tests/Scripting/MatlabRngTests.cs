using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: <c>rng</c> and the one random stream behind it. Before this, every draw came from an unseeded
/// generator — two of them, in fact, since the sparse builtins kept their own — so no script that used
/// randomness could be run twice and checked.
/// </summary>
/// <remarks>
/// The assertions here are about repeatability, never about particular numbers: this is not MATLAB's
/// Mersenne Twister and does not claim to produce MATLAB's values for a seed. What it claims is that
/// the same seed gives the same run, that a captured state restores, and that every builtin that draws
/// is drawing from the one stream — which is the thing a test can actually pin.
/// </remarks>
[Collection("JG facade")]
public class MatlabRngTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabRngTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    [Theory]
    [InlineData("rand(3)")]
    [InlineData("randn(2, 4)")]
    [InlineData("randi(100, 1, 8)")]
    [InlineData("randperm(12)")]
    public void TheSameSeedGivesTheSameDraws(string draw)
    {
        // One line per generator: seed, draw, seed again, draw again, compare. A generator that
        // ignored the seed would pass nothing here.
        string output = RunAndRead($"""
            rng(42); a = {draw};
            rng(42); b = {draw};
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void DifferentSeedsGiveDifferentDraws()
    {
        // The other half of the claim: seeding does something. Without this, a generator that reset to
        // a constant stream would satisfy every repeatability test above.
        string output = RunAndRead("""
            rng(1); a = rand(1, 20);
            rng(2); b = rand(1, 20);
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void SparseDrawsFromTheSameStream()
    {
        // sprand used to hold a private, unseeded generator, so this script could not be repeated at
        // all. It is the reason the stream is threaded rather than constructed per registrar.
        string output = RunAndRead("""
            rng(7); a = full(sprand(4, 4, 0.5));
            rng(7); b = full(sprand(4, 4, 0.5));
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void OneSeedRepeatsAWholeRunNotJustOneCall()
    {
        // What a script actually needs: seed once at the top, and everything downstream repeats —
        // across builtins, and across a mixture of them.
        string output = RunAndRead("""
            rng(3);
            a = [rand(1, 3), randn(1, 3), randi(10, 1, 3), randperm(5)];
            rng(3);
            b = [rand(1, 3), randn(1, 3), randi(10, 1, 3), randperm(5)];
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void RngDefaultIsItsOwnRepeatableSeed()
    {
        string output = RunAndRead("""
            rng('default'); a = rand(1, 5);
            rng('default'); b = rand(1, 5);
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void AStateCanBeCapturedAndRestored()
    {
        // s = rng captures where the stream is; rng(s) puts it back, so the same values come out of
        // the middle of a run. This is the form a script uses to repeat one stretch of work.
        string output = RunAndRead("""
            rng(11);
            warmup = rand(1, 4);
            s = rng;
            a = rand(1, 6);
            rng(s);
            b = rand(1, 6);
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void ARestoredStateResumesRatherThanRestarts()
    {
        // The captured state is a position in the stream, not just the seed: restoring it must give
        // the values that follow the capture, not the ones that followed the seed.
        string output = RunAndRead("""
            rng(11);
            first = rand(1, 4);
            s = rng;
            next = rand(1, 4);
            rng(s);
            again = rand(1, 4);
            fprintf('%d %d\n', isequal(next, again), isequal(first, again));
            """);

        Assert.Equal("1 0", output.Trim());
    }

    [Fact]
    public void TheStateReportsItsGeneratorAndSeed()
    {
        string output = RunAndRead("""
            rng(19);
            s = rng;
            fprintf('%s %d\n', s.Type, s.Seed);
            """);

        Assert.Equal("twister 19", output.Trim());
    }

    [Fact]
    public void SeedingPrintsNothingButQueryingAnswers()
    {
        // rng(seed) is a command, so a bare statement must not echo ans; `s = rng` still has to be a
        // state rather than the builtin itself, which is what AutoCallsBare buys.
        string output = RunAndRead("""
            rng(5)
            s = rng;
            fprintf('%d\n', s.Seed);
            """);

        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void ShuffleMovesOffTheSeededStream()
    {
        string output = RunAndRead("""
            rng(0); a = rand(1, 10);
            rng('shuffle'); b = rand(1, 10);
            fprintf('%d\n', isequal(a, b));
            """);

        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void AnUnknownGeneratorIsRefusedByName()
    {
        ScriptRunResult result = RunMatlab("rng(1, 'philox');");

        Assert.False(result.Success);
        Assert.Contains("philox", result.Message + _output.ErrorText, StringComparison.Ordinal);
        Assert.Contains("twister", result.Message + _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownOptionWordNamesTheAlternatives()
    {
        ScriptRunResult result = RunMatlab("rng('reset');");

        Assert.False(result.Success);
        string message = result.Message + _output.ErrorText;
        Assert.Contains("default", message, StringComparison.Ordinal);
        Assert.Contains("shuffle", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonWholeSeedIsRefused()
    {
        ScriptRunResult result = RunMatlab("rng(2.5);");

        Assert.False(result.Success);
        Assert.Contains("whole number", result.Message + _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void RngWorksInTheJgsDialectToo()
    {
        // Seeding is not a MATLAB-only idea and the JGS surface draws from the same stream.
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            """
            rng(8)
            let a = rand(5)
            rng(8)
            let b = rand(5)
            print(a == b)
            """,
            context,
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.DoesNotContain("false", _output.NormalText, StringComparison.OrdinalIgnoreCase);
    }
}
