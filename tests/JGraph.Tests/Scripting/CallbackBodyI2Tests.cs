using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I2 (ADR 0325): the scalar callbacks compiled - a solver's objective
/// (<c>fzero</c>, <c>fminbnd</c>, <c>fminsearch</c>), the function <c>arrayfun</c> applies to each
/// element, and the integrand <c>integral</c> and <c>quadgk</c> ask for a panel at a time. Every
/// script runs with the compiler on and off and must print the same bytes (outputs as
/// <c>num2hex</c>) with the same outcome, and each case names the road it expects through the
/// compiler's counters.
/// </summary>
[Collection("JG facade")]
public class CallbackBodyI2Tests : IDisposable
{
    public CallbackBodyI2Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Compiled, long Walked);

    private static Run RunWith(bool compile, string code)
    {
        bool previous = JgsCallbackCompile.Enabled;
        JgsCallbackCompile.Enabled = compile;
        long compiledBefore = JgsCallbackCompile.Compiled;
        long walkedBefore = JgsCallbackCompile.Walked;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText, result.Success, result.Message,
                JgsCallbackCompile.Compiled - compiledBefore, JgsCallbackCompile.Walked - walkedBefore);
        }
        finally
        {
            JgsCallbackCompile.Enabled = previous;
        }
    }

    /// <summary>Both roads, the same bytes and the same outcome; answers the compiled run.</summary>
    private static Run AssertParity(string code, long compiled, bool expectSuccess = true)
    {
        Run fast = RunWith(compile: true, code);
        Run walk = RunWith(compile: false, code);
        Assert.True(expectSuccess == walk.Success, walk.Message);
        Assert.Equal(walk.Success, fast.Success);
        Assert.Equal(walk.Message, fast.Message);
        Assert.Equal(walk.Output, fast.Output);
        Assert.Equal(0, walk.Compiled);
        Assert.Equal(compiled, fast.Compiled);
        return fast;
    }

    private const string Hex = "h = @(x) sprintf('%s', reshape(num2hex(x(:))', 1, []));\n";

    [Fact]
    public void FzeroInALoopCompilesEachHandleAndKeepsEveryBit()
    {
        Run run = AssertParity(Hex + """
            acc = 0;
            for k = 1:40
                acc = acc + fzero(@(x) x^3 - k*x - 1, [0 50]);
            end
            disp(h(acc));
            """, compiled: 40);
        Assert.Equal(0, run.Walked);
    }

    [Fact]
    public void FzeroFromAGuessWithEveryOutputAndADisplay() => AssertParity(Hex + """
        [x, fv, flag, out] = fzero(@(x) cos(x) - x/3, 1);
        disp(h([x fv flag out.funcCount out.iterations out.intervaliterations])); disp(out.message);
        x = fzero(@(x) exp(-x) - 0.5, [0 5], optimset('Display', 'iter')); disp(h(x));
        [x, fv] = fzero(@(x) tan(x) - 1, [0 1], optimset('Display', 'final')); disp(h([x fv]));
        x = fzero(@(x) x^2 + 1, 0); disp(h(x));
        """, compiled: 4);

    [Fact]
    public void FminbndAndFminsearchCompileOverTheirPoints() => AssertParity(Hex + """
        disp(h(fminbnd(@(x) (x - 2)^2 + sin(3*x), 0, 5)));
        acc = 0;
        for k = 1:10
            p = fminsearch(@(v) (v(1) - k/100)^2 + 10*(v(2) - v(1)^2)^2, [0 0]);
            acc = acc + p(1);
        end
        disp(h(acc));
        [p, fv, flag, out] = fminsearch(@(v) abs(v - pi) + v^2/100, 1); disp(h([p fv flag out.funcCount]));
        p = fminsearch(@(v) (v(1)-1)^2 + (v(2)+2)^2 + (v(3)-v(4))^2 + v(4)^2, [0; 0; 1; 1]); disp(h(p));
        """, compiled: 13);

    [Fact]
    public void AnAnswerThatLeavesTheRealsWalksThatPoint()
    {
        // sqrt over a bracket reaching below zero: the walk's answer there is complex, which the
        // solver refuses in its own words; the compiled road hands exactly those points back.
        Run run = AssertParity(Hex + """
            disp(h(fzero(@(x) sqrt(x) - 1.2, [0 4])));
            try
                fzero(@(x) sqrt(x) - 1.2, [-1 4])
            catch e
                disp(e.message);
            end
            """, compiled: 2);
        Assert.True(run.Walked >= 1);
    }

    [Fact]
    public void FunValCheckStillRefusesANaN() => AssertParity("""
        try
            fminsearch(@(v) log(v) + 0/0, 2, optimset('FunValCheck', 'on'))
        catch e
            disp(e.identifier); disp(e.message);
        end
        try
            fzero(@(x) x - 1 + 0/0, [0 3], optimset('FunValCheck', 'on'))
        catch e
            disp(e.identifier); disp(e.message);
        end
        """, compiled: 2);

    [Theory]
    [InlineData("@(x) x^2 - c", "a captured scalar")]
    [InlineData("@(x) x(1)^2 - 2", "a literal index of a one-element point")]
    [InlineData("@(x) -(-x) - 3\\6 + pi/4", "a backslash, a negation and a bare constant")]
    public void ScalarObjectivesThatCompile(string objective, string why)
    {
        _ = why;
        AssertParity(Hex + $"c = 2.5;\ndisp(h(fzero({objective}, [0 3])));\n", compiled: 1);
    }

    [Theory]
    [InlineData("fzero(@(x, y) x - 1, [0 3])")]
    [InlineData("fzero(@(x) x - c(2), [0 3])")]
    [InlineData("fzero(@(x) x - s, [0 3])")]
    [InlineData("fzero(@(x) x - n, [0 3])")]
    [InlineData("fzero(@(x) f(x), [0 3])")]
    [InlineData("fzero(@(x) x - (x > 1), [0 3])")]
    [InlineData("fzero(@(x) x - 1, [0 3], [], 7)")]
    [InlineData("fzero(@sin, [3 4])")]
    [InlineData("fminsearch(@(v) sum(v.^2), [1 1])")]
    [InlineData("fminsearch(@(v) v(3)^2, [1 1])")]
    [InlineData("fminsearch(@(v) v^2, [1 1])")]
    public void WhatIsOutsideTheWhitelistIsWalked(string call) => AssertParity(Hex + """
        c = [1 2]; s = single(1); n = int8(1); f = @(x) x - 1;
        try
            disp(h(double(
        """ + call + """
        )));
        catch e
            disp(e.message);
        end
        """, compiled: 0);

    [Fact]
    public void ArrayfunCompilesOverARealDoubleArrayInItsShape() => AssertParity(Hex + """
        x = linspace(-3, 7, 1000);
        a = arrayfun(@(a) a^2 + sin(a), x); disp(h(a)); disp(size(a));
        b = arrayfun(@(a) mod(a, 2) - atan2(a, 3), reshape(x, 10, 100)); disp(h(b)); disp(size(b));
        c = arrayfun(@(a) exp(-a) .* cos(a) ./ (1 + a.^2), x'); disp(h(c)); disp(size(c));
        d = arrayfun(@(a) a / 2, reshape(1:24, 2, 3, 4)); disp(h(d)); disp(size(d));
        e = arrayfun(@(a) floor(a) + round(a) - ceil(a) + abs(a), [2.5 -2.5 0.5]); disp(h(e));
        disp(class(arrayfun(@(a) a + 1, 5:7)));
        """, compiled: 6);

    [Fact]
    public void ArrayfunLeavesEverythingElseToTheWalk()
    {
        Run run = AssertParity(Hex + """
            disp(h(arrayfun(@(a) a^2, int8([1 2 3]))));
            disp(h(arrayfun(@(a) a^2, [true false true])));
            disp(h(arrayfun(@(a) a^2, single([1 2]))));
            disp(h(arrayfun(@(a, b) a + b, [1 2], [3 4])));
            [m, n] = arrayfun(@(a) deal(a, -a), [1 2]); disp(h([m n]));
            disp(arrayfun(@(a) a + 1, [1 2], 'UniformOutput', false));
            disp(h(arrayfun(@(a) a + 1, 4)));
            disp(size(arrayfun(@(a) a + 1, zeros(0, 3))));
            disp(h(arrayfun(@sin, [1 2])));
            r = arrayfun(@(a) sqrt(a), [4 -1 9]);
            disp(h([real(r) imag(r)])); fprintf('isreal=%d size=%s\n', isreal(r), mat2str(size(r)));
            """, compiled: 1);
        Assert.Equal(1, run.Walked); // the sqrt call compiled, met -1 and was walked whole
        Assert.Contains("isreal=0 size=[1 3]", run.Output); // R2025b's complex row (open item 255)
    }

    [Fact]
    public void IntegralCompilesItsPanelsAndKeepsEveryBit()
    {
        Run run = AssertParity(Hex + """
            acc = 0;
            for k = 1:10
                acc = acc + integral(@(x) exp(-k*x/1000) .* cos(x), 0, 10);
            end
            disp(h(acc));
            acc = 0;
            for k = 1:10
                acc = acc + integral(@(x) cos(k*x) ./ (1 + x.^2), 0, 20);
            end
            disp(h(acc));
            disp(h(quadgk(@(x) exp(-x.^2/3), -Inf, Inf)));
            [q, err] = quadgk(@(x) 1./sqrt(x), 0, 1); disp(h([q err]));
            disp(h(integral(@(x) x.^0.5 .* log(x + 1), 0, 3, 'RelTol', 1e-10)));
            disp(h(integral(@(x) atan2(x, 2) - rem(x, 0.7), -1, 4, 'Waypoints', [0.7 1.4])));
            disp(h(integral(@(x) 3*x + x/2 - 2\x, 1, 0)));
            """, compiled: 25);
        Assert.Equal(0, run.Walked);
    }

    [Fact]
    public void AnIntegrandThatGoesComplexOnAPanelWalksThatPanel()
    {
        // The midpoint probe is real, so the panels are compiled; the first panel below 1 leaves the
        // reals and the walk answers it complex, which starts the integral again on the complex road
        // (open item 256), as it does with the compiler off.
        Run run = AssertParity(Hex + """
            q = integral(@(x) sqrt(x - 1), 0, 2);
            disp(h([real(q) imag(q)])); disp(isreal(q));
            fprintf('%.6f %.6f\n', real(q), imag(q));
            disp(integral(@(x) log(x), 1, 2));
            """, compiled: 2);
        Assert.True(run.Walked >= 1);
        Assert.Contains("0.666667 0.666667", run.Output);
    }

    [Fact]
    public void ADeclarationsTemplateReadsEachHandlesOwnCaptures() => AssertParity(Hex + """
        vals = {2, single(3), 4, int8(5), 6};
        for i = 1:5
            k = vals{i};
            r = arrayfun(@(a) a*k + 1, [1 2 3]);
            disp(class(r)); disp(h(double(r)));
        end
        for i = 1:3
            try
                p = fminsearch(@(v) sum(v.^2) + i, [1 1]); disp(h(p));
            catch e
                disp(e.message);
            end
        end
        """, compiled: 3); // the three double captures; single and int8 walk, the sum never compiles

    [Fact]
    public void ARowOfNumberLiteralsIsAFreshArrayEveryTime()
    {
        Run run = RunWith(compile: true, """
            for k = 1:3
                v = [0 50];
                w = v;
                v(1) = k;
                fprintf('%g %g|%g %g\n', v, w);
            end
            u = [1.5 -0 2^-1050]; fprintf('%s %d %d\n', class(u), size(u)); for q = u, fprintf('%s\n', num2hex(q)); end
            for k = 1:2
                z = [7 8 9]; z(end + 1) = k; fprintf('%g ', z); fprintf('\n');
            end
            """);
        Assert.True(run.Success, run.Message);
        Assert.Equal(
            "1 50|0 50\n2 50|0 50\n3 50|0 50\ndouble 1 3\n"
            + "3ff8000000000000\n8000000000000000\n0000000001000000\n"
            + "7 8 9 1 \n7 8 9 2 \n",
            run.Output.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("integral(@(x) 5, 0, 1)")]
    [InlineData("integral(@(x) x*x, 0, 1)")]
    [InlineData("integral(@(x) x^2, 0, 1)")]
    [InlineData("integral(@(x) 1/x, 1, 2)")]
    [InlineData("integral(@(x) x\\2, 1, 2)")]
    [InlineData("integral(@(x) mod(3, x), 1, 2)")]
    [InlineData("integral(@(x) [x; x], 0, 1)")]
    [InlineData("integral(@(x) x.^2, 0, 1, 'ArrayValued', true)")]
    [InlineData("integral(@(x) x.^2, 0, 1, 'Vectorized', false)")]
    [InlineData("integral(@(x) x.^2 + 1i, 0, 1)")]
    [InlineData("integral(@(x) x(1), 0, 1)")]
    public void IntegrandsThatAreNotElementwiseAreWalked(string call) => AssertParity(Hex + """
        try
            disp(h(
        """ + call + """
        ));
        catch e
            disp(e.message);
        end
        """, compiled: 0);
}
