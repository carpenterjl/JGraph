using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M96b seen from a MATLAB script: <c>filter</c> takes the packed kernels whenever its denominator
/// has no feedback in it, and nothing may notice. Every test here runs its script twice — packing
/// forced on, then forced off — and the printed output must be byte-identical at seventeen
/// significant digits, with reciprocals alongside so a flipped zero sign cannot hide. What the
/// scripts sweep is what the fast path claims: every dimension, both outputs, the initial and final
/// conditions, a denominator that only looks trivial, and the shapes and classes it refuses.
/// </summary>
[Collection("JG facade")]
public class MatlabPackedFilterM96Tests : IDisposable
{
    public MatlabPackedFilterM96Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    private static void AssertParity(string code, bool expectSuccess = true)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.Equal(boxedOk, packedOk);
        if (expectSuccess)
        {
            Assert.True(boxedOk, boxedMessage);
        }
        else
        {
            Assert.False(boxedOk);
            Assert.Equal(boxedMessage, packedMessage);
        }

        Assert.Equal(boxedOut, packedOut);
    }

    private const string Show = """
        function show(v)
            fprintf('%dx%dx%d|', size(v, 1), size(v, 2), size(v, 3));
            fprintf('%.17g ', v);
            fprintf('|');
            fprintf('%.17g ', 1 ./ v);
            fprintf('\n');
        end
        """ + "\n";

    [Fact]
    public void EveryShapeAndDimensionFiltersTheSameWhicheverWayTheSamplesAreStored()
    {
        AssertParity(Show + """
            b = [0.25 0.25 0.25 0.25];
            x = [3 1 2 1 -4 9 0 7 2 5];
            show(filter(b, 1, x)); show(filter(b, 1, x')); show(filter(b, 2, x));
            show(filter(b, [1 0 0], x)); show(filter(1, 1, x)); show(filter([2], 4, x));
            A = [3 1; 1 4; 2 9; 0 5];
            show(filter(b, 1, A)); show(filter(b, 1, A, [], 1)); show(filter(b, 1, A, [], 2));
            show(filter(b, 1, A, [], 3));
            C = reshape((1:24) - 12.5, 2, 3, 4);
            show(filter(b, 1, C, [], 1)); show(filter(b, 1, C, [], 2)); show(filter(b, 1, C, [], 3));
            show(filter(b, 1, 5)); show(filter(b, 1, [7]));
            """);
    }

    [Fact]
    public void TheConditionsItStartsAndFinishesOnSurviveTheMove()
    {
        AssertParity(Show + """
            b = [0.5 0.2 0.3];
            a = 1;
            x = [1 2 3 4 5 6 7 8];
            zi = [0.7 -0.4];
            [y, zf] = filter(b, a, x, zi);
            show(y); show(zf);
            [y2, zf2] = filter(b, a, x);
            show(y2); show(zf2);
            % filtering in two halves must resume exactly where the first left off
            [p, zp] = filter(b, a, x(1:3));
            [q, zq] = filter(b, a, x(4:8), zp);
            show([p q]); show(zq);
            A = [1 2; 3 4; 5 6];
            [ya, za] = filter(b, a, A);
            show(ya); show(za);
            [yb, zb] = filter(b, a, A, [], 2);
            show(yb); show(zb);
            """);
    }

    [Fact]
    public void ADenominatorWithRealFeedbackKeepsTheRecurrence()
    {
        AssertParity(Show + """
            bf = [0.0201 0.0402 0.0201];
            af = [1.0000 -1.5610 0.6414];
            x = sin((1:40) * 0.3);
            [y, zf] = filter(bf, af, x);
            show(y); show(zf);
            show(filter([1 1], [1 0 -0.5], x));
            show(filter([1 1], [1 -0.0], x));
            """);
    }

    [Fact]
    public void TheClassesAndKindsTheFastPathWillNotTakeAnswerExactlyAsBefore()
    {
        AssertParity(Show + """
            b = [0.5 0.5];
            show(filter(b, 1, single([1 2 3 4])));
            show(filter(b, 1, int32([1 2 3 4])));
            show(filter(b, 1, logical([1 0 1 1])));
            show(filter(b, 1, true));
            c = {1, 2, 3};
            show(filter(b, 1, [c{:}]));
            """);
    }

    [Fact]
    public void TheRefusalsKeepTheirWording()
    {
        AssertParity(Show + "filter([1 1], 0, [1 2 3]);", expectSuccess: false);
        AssertParity(Show + "filter([], 1, [1 2 3]);", expectSuccess: false);
        AssertParity(Show + "filter([1 1], 1, [1 2 3], [1 2 3]);", expectSuccess: false);
        AssertParity(Show + "filter([1 1], 1, 'text');", expectSuccess: false);
    }

    /// <summary>
    /// The one deliberate change, seen from a script. A filter with no feedback cannot carry a value
    /// further than its own length, and now does not: the recurrence used to form <c>0 · y</c> for
    /// every coefficient it had been told was zero, and that product is NaN once an output is not
    /// finite, so one bad sample used to spoil the rest of the signal. Both roads agree with each
    /// other — they run the same kernel — so what is asserted is the reach itself.
    /// </summary>
    [Fact]
    public void ANaNReachesOnlyAsFarAsTheFilterIsLong()
    {
        AssertParity(Show + """
            b = [0.5 0.25 0.25];
            x = [1 2 NaN 4 5 6 7 8];
            show(filter(b, 1, x));
            show(filter(b, 1, [1 Inf 3 4 5 6]));
            """);

        (string[] output, bool ok, string? message) = RunWith(packed: true, """
            b = [0.5 0.25 0.25];
            y = filter(b, 1, [1 2 NaN 4 5 6 7 8]);
            fprintf('%d %d %d %d\n', isnan(y(2)), isnan(y(3)), isnan(y(5)), isnan(y(6)));
            fprintf('%.17g\n', y(6));
            """);

        Assert.True(ok, message);
        Assert.Equal("0 1 1 0", output[0].Trim());
        Assert.Equal(((0.5 * 6) + (0.25 * 5) + (0.25 * 4)).ToString("R"), output[1].Trim());
    }

    [Fact]
    public void ALongSignalFiltersToWhatTheShortRoadWouldHaveGiven()
    {
        // Long enough to cross the grain boundary threads are handed out on, so the seam between
        // one grain and the next has to be invisible.
        (string[] output, bool ok, string? message) = RunWith(packed: true, """
            n = 300000;
            x = mod((1:n) * 0.618033988749895, 1) - 0.5;
            b = ones(1, 64) / 64;
            y = filter(b, 1, x);
            k = [1 63 64 65 65535 65536 65537 131072 n];
            e = 0;
            for i = 1:numel(k)
                j = k(i);
                lo = max(1, j - 63);
                e = max(e, abs(y(j) - sum(x(lo:j)) / 64));
            end
            fprintf('%.3g\n', e);
            fprintf('%.17g\n', sum(y));
            """);

        Assert.True(ok, message);
        Assert.True(double.Parse(output[0].Trim()) < 1e-15, output[0]);

        (string[] boxed, bool boxedOk, _) = RunWith(packed: false, """
            n = 300000;
            x = mod((1:n) * 0.618033988749895, 1) - 0.5;
            b = ones(1, 64) / 64;
            y = filter(b, 1, x);
            fprintf('%.3g\n', 0);
            fprintf('%.17g\n', sum(y));
            """);

        Assert.True(boxedOk);
        Assert.Equal(boxed[1], output[1]);
    }

    [Fact]
    public void TheSeparableConvolutionAnswersWhatTheBuiltKernelAnswered()
    {
        AssertParity(Show + """
            A = [3 1 4 1; 5 9 2 6; 5 3 5 8; 9 7 9 3];
            u = [0.25 0.5 0.25];
            v = [0.2 0.6 0.2];
            show(conv2(u, v, A));
            show(conv2(u, v, A, 'same'));
            show(conv2(u, v, A, 'valid'));
            show(conv2(u, v, A, 'full'));
            show(conv2([1], [1], A, 'same'));
            show(conv2(u', v, A, 'same'));
            """);
    }
}
