using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M98 seen from a MATLAB script: a for or while whose body works entirely in scalar doubles now
/// compiles once to a register program and runs without the tree walk — and nothing may notice.
/// Every test here runs its script twice, the compiler forced on and then forced off, and the
/// printed output must be byte-identical at seventeen significant digits. The scripts lean on the
/// places the two roads could plausibly split: signed zeros, NaN truthiness, the logical class of a
/// spilled comparison, loop variables after empty ranges and breaks, answers that leave the reals
/// mid-loop, and the errors a range throws.
/// </summary>
[Collection("JG facade")]
public class MatlabLoopJitM98Tests : IDisposable
{
    public MatlabLoopJitM98Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message, long CompiledRuns) RunWith(bool jit, string code)
    {
        bool previous = JgsLoopJit.Enabled;
        JgsLoopJit.Enabled = jit;
        long before = JgsLoopJit.CompiledRuns;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message, JgsLoopJit.CompiledRuns - before);
        }
        finally
        {
            JgsLoopJit.Enabled = previous;
        }
    }

    /// <summary>
    /// Runs <paramref name="code"/> both ways and asserts byte-identical output. When
    /// <paramref name="expectCompiled"/> is set, additionally asserts that the compiled road really
    /// ran (or really refused) at least one loop — a fast path that silently never fires would pass
    /// every parity test while buying nothing.
    /// </summary>
    private static string[] AssertParity(string code, bool expectSuccess = true, bool? expectCompiled = null)
    {
        (string[] jitOut, bool jitOk, string? jitMessage, long compiled) = RunWith(jit: true, code);
        (string[] walkOut, bool walkOk, string? walkMessage, _) = RunWith(jit: false, code);

        Assert.Equal(walkOk, jitOk);
        if (expectSuccess)
        {
            Assert.True(walkOk, walkMessage);
        }
        else
        {
            Assert.False(walkOk);
            Assert.Equal(walkMessage, jitMessage); // the refusal must read the same too
        }

        Assert.Equal(walkOut, jitOut);
        if (expectCompiled is { } expected)
        {
            Assert.Equal(expected, compiled > 0);
        }

        return jitOut;
    }

    [Fact]
    public void BenchmarkLoopMatchesTheWalkBitForBit()
    {
        string[] output = AssertParity("""
            acc = 0;
            v = 1.0;
            for k = 1:2e4
                v = mod(v * 1.0000001 + 0.001, 2);
                if v > 1
                    acc = acc + v;
                end
            end
            fprintf('CHK|%.17g|%.17g|%.17g\n', acc, v, k);
            """, expectCompiled: true);
        Assert.StartsWith("CHK|", Assert.Single(output));
    }

    [Fact]
    public void BreakContinueAndNestedLoopsAgree() => AssertParity("""
        acc = 0;
        for k = 1:100
            if mod(k, 7) == 0
                continue;
            end
            if k > 60
                break;
            end
            for j = 1:3
                acc = acc + k * j;
            end
        end
        fprintf('%.17g|%.17g|%.17g\n', acc, k, j);
        """, expectCompiled: true);

    [Fact]
    public void WhileWithLogicalFlagsAgrees() => AssertParity("""
        n = 0; s = 1; go = true;
        while go && n < 50
            n = n + 1;
            s = mod(s * 1.1, 97);
            if s > 90
                go = false;
            end
        end
        fprintf('%.17g|%.17g|%d|%s\n', n, s, go, class(go));
        """, expectCompiled: true);

    [Fact]
    public void EveryWhitelistedKernelAgreesOverAwkwardValues() => AssertParity("""
        w = 0;
        for k = 1:40
            x = (k - 20.5) / 3;
            w = w + sin(x) + cos(x) + tan(x) + atan(x) + exp(-abs(x)) + floor(x) + ceil(x) + round(x);
            w = w + sqrt(abs(x)) + log(abs(x) + 1) + log10(abs(x) + 0.5) + asin(x / 10) + acos(x / 10);
            w = w + mod(x, 3) + rem(x, 3) + atan2(x, 2) + min(x, 0.5) + max(x, -0.5) + x^2 + 2^x;
        end
        fprintf('%.17g\n', w);
        """, expectCompiled: true);

    [Fact]
    public void SignedZeroNanAndInfinitySurviveTheRegisters() => AssertParity("""
        z = -0.0; q1 = 1/z; nn = 0/0;
        for k = 1:3
            z = -z;
            q1 = min(q1, k);
            nn = max(nn, -Inf);
        end
        fprintf('%.17g|%.17g|%.17g|%d|%d\n', 1/z, q1, nn, nn > 0, nn ~= nn);
        """, expectCompiled: true);

    [Fact]
    public void NanIsTruthyInACompiledCondition() => AssertParity("""
        hits = 0;
        for k = 1:5
            v = 0/0;
            if v
                hits = hits + 1;
            end
        end
        fprintf('%.17g\n', hits);
        """, expectCompiled: true);

    [Fact]
    public void BarePiAndEpsFoldToTheBuiltinsValues() => AssertParity("""
        area = 0;
        for k = 1:10
            area = area + pi * k * k + eps;
        end
        fprintf('%.17g\n', area);
        """, expectCompiled: true);

    [Fact]
    public void CopyingAVariableCarriesItsLogicalClass() => AssertParity("""
        for k = 1:4
            flag = k > 2;
            copied = flag;
        end
        fprintf('%d|%d|%s|%s\n', flag, copied, class(flag), class(copied));
        """, expectCompiled: true);

    [Fact]
    public void LoopVariableStaysUndefinedAfterAnEmptyRange() => AssertParity("""
        for q = 5:1
            x = 1;
        end
        fprintf('%d\n', exist('q'));
        for k = 10:-2:1
        end
        fprintf('%.17g\n', k);
        """, expectCompiled: true);

    [Fact]
    public void NestedRangesReadTheOuterVariable() => AssertParity("""
        tot = 0;
        for k = 1:20
            for j = k:2:(k + 6)
                tot = tot + j;
            end
        end
        fprintf('%.17g|%.17g|%.17g\n', tot, k, j);
        """, expectCompiled: true);

    [Fact]
    public void AnAnswerThatLeavesTheRealsHandsTheStatementBack() => AssertParity("""
        w = 0;
        for k = 1:6
            v = abs((-2)^(1/k));
            w = w + v;
        end
        fprintf('%.17g\n', w);
        """, expectCompiled: true);

    [Fact]
    public void AVariableGoneComplexFinishesTheLoopByTheWalk() => AssertParity("""
        cacc = 0;
        for k = 1:8
            v = sqrt(4 - k);
            cacc = cacc + abs(v);
        end
        fprintf('%.17g|%.17g|%d\n', cacc, k, isreal(v));
        """, expectCompiled: true);

    [Fact]
    public void AnEscapeInsideAnIfResumesTheStatementsAfterIt() => AssertParity("""
        dacc = 0;
        for k = 1:10
            if k == 3
                u = (-2)^0.5;
            end
            dacc = dacc + k;
        end
        fprintf('%.17g|%.17g\n', dacc, abs(u));
        """, expectCompiled: true);

    [Fact]
    public void AnEscapeThreeFramesDeepFinishesEveryLevelByTheWalk() => AssertParity("""
        tot = 0;
        for k = 1:6
            for j = 1:4
                if j == 3 && k == 2
                    z = sqrt(-j);
                end
                tot = tot + j;
            end
            tot = tot + 100;
        end
        fprintf('%.17g|%.17g|%.17g|%d\n', tot, k, j, isreal(z));
        """, expectCompiled: true);

    [Fact]
    public void ABreakAfterABailedStatementLandsOnTheRightLoop() => AssertParity("""
        tot = 0;
        for k = 1:5
            for j = 1:5
                v = sqrt(2 - j);
                if abs(v) > 1.2
                    break;
                end
                tot = tot + 1;
            end
            tot = tot + j * 10;
        end
        fprintf('%.17g|%.17g|%.17g\n', tot, k, j);
        """, expectCompiled: true);

    [Fact]
    public void AnEscapeInsideAConditionBranchesLikeTheWalk() => AssertParity("""
        ec = 0;
        for k = 1:6
            if sqrt(3 - k) < 1
                ec = ec + 1;
            end
        end
        fprintf('%.17g\n', ec);
        """, expectCompiled: true);

    [Fact]
    public void CompoundAssignmentsAgree() => AssertParity("""
        g = 1000;
        for k = 1:5
            g = g + k; g = g - 1; g = g * 1.5; g = g / 1.25;
        end
        fprintf('%.17g\n', g);
        """, expectCompiled: true);

    [Fact]
    public void EqualityTreatsNanAsUnequalToItself() => AssertParity("""
        e = 0;
        for k = 1:5
            a = 0/0;
            if a == a
                e = e + 1;
            end
            if a ~= a
                e = e + 10;
            end
        end
        fprintf('%.17g\n', e);
        """, expectCompiled: true);

    [Fact]
    public void AZeroRangeStepThrowsTheWalksError() => AssertParity("""
        acc = 0;
        for k = 1:0:5
            acc = acc + 1;
        end
        fprintf('never\n');
        """, expectSuccess: false);

    [Fact]
    public void ANestedZeroStepThrowsMidLoopWithStateKept() => AssertParity("""
        w = 0;
        for k = 1:3
            w = w + 1;
            for j = 1:0:5
                w = w + 100;
            end
        end
        fprintf('never\n');
        """, expectSuccess: false, expectCompiled: true);

    [Fact]
    public void AnOverLimitNestedRangeThrowsTheWalksError() => AssertParity("""
        s = 0;
        for k = 1:3
            for j = 1:1e9
                s = s + 1;
            end
        end
        fprintf('never\n');
        """, expectSuccess: false, expectCompiled: true);

    [Fact]
    public void AnUndefinedNameInTheBodyReportsItself() => AssertParity("""
        acc = 0;
        for k = 1:5
            acc = acc + undefinedname;
        end
        fprintf('never\n');
        """, expectSuccess: false, expectCompiled: false);

    [Fact]
    public void AShadowedBuiltinRefusesTheFastPathAndIndexes() => AssertParity("""
        mod = 42;
        acc = 0;
        for k = 1:5
            acc = acc + mod(1, 1);
        end
        fprintf('%.17g\n', acc);
        """, expectCompiled: false);

    [Fact]
    public void AGlobalVariableRefusesTheFastPath() => AssertParity("""
        global gcount
        gcount = 0;
        for k = 1:10
            gcount = gcount + k;
        end
        fprintf('%.17g\n', gcount);
        """, expectCompiled: false);

    [Fact]
    public void AClassedIntegerRefusesTheFastPathAndKeepsItsClass() => AssertParity("""
        x = int32(250);
        for k = 1:10
            x = x + 1;
        end
        fprintf('%d|%s\n', x, class(x));
        """, expectCompiled: false);

    [Fact]
    public void IndexedWritesStayOnTheWalk() => AssertParity("""
        xs = zeros(1, 5);
        for k = 1:5
            xs(k) = k * k;
        end
        fprintf('%.17g\n', sum(xs));
        """, expectCompiled: false);

    [Fact]
    public void RootRangeBoundsEvaluateExactlyOnce() => AssertParity("""
        function n = announce()
            fprintf('bound evaluated\n');
            n = 4;
        end
        total = 0;
        for k = 1:announce()
            total = total + k;
        end
        fprintf('%.17g\n', total);
        """, expectCompiled: true);

    [Fact]
    public void CompiledLoopsInsideAFunctionSpillTheFramesVariables() => AssertParity("""
        function r = tri(n)
            r = 0;
            for k = 1:n
                r = r + k;
            end
        end
        fprintf('%.17g|%.17g\n', tri(100), tri(1000));
        """, expectCompiled: true);

    [Fact]
    public void PersistentCountersSurviveCompiledLoops() => AssertParity("""
        function r = bump()
            persistent hits
            if isempty(hits)
                hits = 0;
            end
            for k = 1:5
                hits = hits + 1;
            end
            r = hits;
        end
        fprintf('%.17g|%.17g|%.17g\n', bump(), bump(), bump());
        """);

    [Fact]
    public void FractionalAndDescendingRangesAgreeElementForElement() => AssertParity("""
        acc = 0;
        for k = 0:0.1:1
            acc = acc + k;
        end
        fprintf('%.17g|%.17g\n', acc, k);
        for k = 1:-0.25:0
            fprintf('%.17g\n', k);
        end
        """, expectCompiled: true);

    [Fact]
    public void AWhileConditionReadsWhatTheBodyWrites() => AssertParity("""
        v = 100;
        rounds = 0;
        while v > 1
            v = v / 2;
            rounds = rounds + 1;
        end
        fprintf('%.17g|%.17g\n', v, rounds);
        """, expectCompiled: true);

    [Fact]
    public void AssigningPiInsideTheLoopShadowsTheConstant() => AssertParity("""
        total = 0;
        for k = 1:4
            total = total + pi;
            pi = k;
        end
        fprintf('%.17g|%.17g\n', total, pi);
        """, expectCompiled: true);

    [Fact]
    public void AnUnsuppressedAssignmentEchoesFromTheWalk() => AssertParity("""
        for k = 1:3
            v = k * 2
        end
        fprintf('%.17g\n', v);
        """, expectCompiled: false);
}
