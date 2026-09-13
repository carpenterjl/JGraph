using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0160 (12d): the guarded vector class and the walked statement. Every script runs with the
/// compiler forced on and forced off and must print the same bytes (seventeen digits, or the hex
/// of every element); where the class must refuse, bail or walk, the counters say so, because a
/// fast path that silently never fires — or silently computes something the walk would not — is
/// invisible in the output.
/// </summary>
[Collection("JG facade")]
public class LoopVectorsM160Tests : IDisposable
{
    public LoopVectorsM160Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string[] Output, bool Success, string? Message, long Compiled, long Bails);

    private static Run RunWith(bool jit, string code)
    {
        bool previous = JgsLoopJit.Enabled;
        JgsLoopJit.Enabled = jit;
        long compiledBefore = JgsLoopJit.CompiledRuns;
        long bailsBefore = JgsLoopJit.Bails;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.Normal.ToArray(), result.Success, result.Message,
                JgsLoopJit.CompiledRuns - compiledBefore, JgsLoopJit.Bails - bailsBefore);
        }
        finally
        {
            JgsLoopJit.Enabled = previous;
        }
    }

    /// <summary>
    /// Both roads, same bytes; <paramref name="compiled"/> says whether the loop must have compiled
    /// (null: no claim), <paramref name="bailed"/> whether a guard must have handed something back.
    /// </summary>
    private static string[] AssertParity(string code, bool expectSuccess = true, bool? compiled = null, bool? bailed = null, bool needsPacked = true)
    {
        // A loop of the vector class compiles only where packing is on: in the unpacked lanes
        // (JGRAPH_JGS_PACKED=0) no vector is packed, the entry check refuses, and both roads are
        // the walk. A loop whose only 12d feature is a walked statement compiles in every lane.
        bool reachable = JgsLoopJit.Vectors && (JgsPacking.Enabled || !needsPacked);
        string script = Data + code + Helpers;
        Run fast = RunWith(jit: true, script);
        Run walk = RunWith(jit: false, script);

        Assert.Equal(walk.Success, fast.Success);
        if (expectSuccess)
        {
            Assert.True(walk.Success, walk.Message);
        }
        else
        {
            Assert.False(walk.Success);
            Assert.Equal(walk.Message, fast.Message);
        }

        Assert.Equal(walk.Output, fast.Output);
        if (compiled is { } mustCompile)
        {
            Assert.Equal(mustCompile && reachable, fast.Compiled > 0);
        }

        if (bailed is { } mustBail && reachable)
        {
            Assert.Equal(mustBail, fast.Bails > 0);
        }

        return fast.Output;
    }

    private const string Data = """
        phi = 0.618033988749895;
        x = mod((1:40) * phi, 1) - 0.5;
        x(7) = -0;
        x(11) = NaN;
        xc = x';
        w = mod((1:40) * 0.381966011250105, 1);

        """;

    private const string Helpers = """

        function showbits(name, v)
            fprintf('%s|%d %d|%s|%s\n', name, size(v, 1), size(v, 2), class(v), reshape(num2hex(double(v(:))).', 1, []));
        end
        """;

    [Fact]
    public void ElementReadsAndWritesAreTheWalksBytes() => AssertParity("""
        y = zeros(1, 40);
        z = zeros(40, 1);
        s = 0;
        for k = 1:40
            y(k) = x(k) * 2 + x(41 - k);
            z(k) = xc(k) - w(k);
            s = s + y(k) * z(k);
        end
        showbits('y', y);
        showbits('z', z);
        fprintf('%.17g\n', s);
        """, compiled: true, bailed: false);

    [Fact]
    public void ElementwiseCombinationsAreTheWalksBytes() => AssertParity("""
        v = x;
        u = w;
        s = 0;
        for k = 1:30
            v = v .* 0.5 + u;
            u = -u + v ./ 3 - 1;
            t = 2 * v;
            t = t / 7;
            q = v .^ 2 + abs(u);
            r = sin(v) - cos(u) + exp(q .* 0.01);
            s = s + t(k) + q(k) - r(k) + floor(v(3)) + w(k);
        end
        showbits('v', v);
        showbits('u', u);
        showbits('r', r);
        fprintf('%.17g\n', s);
        """, compiled: true, bailed: false);

    [Fact]
    public void ColumnsAndRowsKeepTheirOrientation() => AssertParity("""
        c = xc;
        r = x;
        for k = 1:5
            c = c * 1.5;
            r = r .* 2;
            c(k) = r(k);
        end
        showbits('c', c);
        showbits('r', r);
        """, compiled: true, bailed: false);

    [Fact]
    public void AGuardedKernelThatLeavesTheRealsHandsTheStatementBack() => AssertParity("""
        v = x;
        acc = 0;
        for k = 1:6
            v = v - 0.2;
            q = sqrt(v);
            p = v .^ 0.5;
            acc = acc + abs(q(1)) + abs(p(2));
        end
        fprintf('%.17g|%d|%d\n', acc, isreal(q), isreal(p));
        """, compiled: true, bailed: true);

    [Fact]
    public void ARowPlusAColumnExpandsByTheWalk() => AssertParity("""
        r = x(1:3);
        c = xc(1:3);
        for k = 1:3
            m = r + c;
            r = r * 2;
        end
        fprintf('%d %d|%.17g\n', size(m, 1), size(m, 2), sum(m(:)));
        """, compiled: true, bailed: true);

    [Fact]
    public void AClassedVectorRefusesTheFastPath() => AssertParity("""
        u = uint8(mod((1:20) * 7, 251));
        for k = 1:20
            u(k) = u(k) + 250;
        end
        fprintf('%s|%d\n', class(u), sum(double(u)));
        """, compiled: false);

    [Fact]
    public void AMatrixIndexedLinearlyRefusesTheFastPath() => AssertParity("""
        M = reshape(x, 8, 5);
        for k = 1:40
            M(k) = M(k) + 1;
        end
        fprintf('%.17g\n', sum(M(:)));
        """, compiled: false);

    [Fact]
    public void AScalarIndexedLikeAVectorRefusesTheFastPath() => AssertParity("""
        s = 5;
        t = 0;
        for k = 1:3
            t = t + s(1);
        end
        fprintf('%.17g\n', t);
        """, compiled: false);

    [Fact]
    public void AComplexVectorRefusesTheFastPath() => AssertParity("""
        z = complex(x, w);
        for k = 1:5
            z(k) = z(k) * 2;
        end
        fprintf('%.17g|%d\n', abs(z(3)), isreal(z));
        """, compiled: false);

    [Fact]
    public void ALogicalStoreDemotesByTheWalk() => AssertParity("""
        y = zeros(1, 10);
        flags = zeros(1, 10);
        for k = 1:10
            y(k) = x(k) > 0;
            t = x(k) < 0;
            flags(k) = t;
        end
        fprintf('%s|%s|%.17g|%.17g\n', class(y), class(flags), sum(y), sum(flags));
        """, compiled: true, bailed: true);

    [Fact]
    public void GrowthAndBadPositionsAreTheWalks()
    {
        AssertParity("""
            y = zeros(1, 3);
            for k = 1:6
                y(k) = k;
            end
            showbits('grown', y);
            """, compiled: true, bailed: true);
        AssertParity("""
            for k = 0:2
                t = x(k);
            end
            """, expectSuccess: false, compiled: true, bailed: true);
        AssertParity("""
            for k = 1:3
                x(k + 0.5) = 1;
            end
            """, expectSuccess: false, compiled: true, bailed: true);
        AssertParity("""
            for k = 40:42
                t = x(k);
            end
            """, expectSuccess: false, compiled: true, bailed: true);
    }

    [Fact]
    public void AProductOfTwoVectorsWalks() => AssertParity("""
        r = x(1:4);
        c = xc(1:4);
        s = 0;
        for k = 1:3
            d = r * c;
            s = s + d + r(k);
        end
        fprintf('%.17g\n', s);
        """, compiled: true, bailed: true);

    [Fact]
    public void WalkedStatementsRunByTheWalkInsideACompiledLoop() => AssertParity("""
        c = cell(1, 5);
        s = struct('n', 0);
        acc = 0;
        for k = 1:5
            acc = acc + k * 0.5;
            fprintf('%d:%.17g\n', k, acc);
            c{k} = sprintf('%d', k * k);
            s.n = s.n + acc;
            [q, r] = deal(k, k + 1);
            acc = acc + r - q;
        end
        fprintf('%s|%.17g|%.17g\n', strjoin(c, ','), s.n, acc);
        """, compiled: true, bailed: true, needsPacked: false);

    /// <summary>
    /// A loop whose every statement is walked compiles nothing, and refuses: measured on the d12
    /// cell-store loop, the program would be the walk plus a spill and a reload per statement.
    /// </summary>
    [Fact]
    public void ALoopOfOnlyWalkedStatementsRefuses()
    {
        AssertParity("""
            rowsc = cell(20, 1);
            for r = 1:20
                rowsc{r} = char(65 + mod((r - 1) + (0:9), 26));
            end
            fprintf('%s|%d\n', rowsc{7}, numel(rowsc));
            """, compiled: false, needsPacked: false);
        AssertParity("""
            rowsc = cell(20, 1);
            n = 0;
            for r = 1:20
                n = n + r;
                rowsc{r} = sprintf('%d', n);
            end
            fprintf('%s|%d\n', rowsc{7}, n);
            """, compiled: true, bailed: true, needsPacked: false);
    }

    [Fact]
    public void AWalkedStatementThatChangesAVariablesKindFinishesByTheWalk() => AssertParity("""
        v = x;
        acc = 0;
        for k = 1:6
            acc = acc + v(1);
            if k == 3
                v = 'text';
            end
            acc = acc + 1;
        end
        fprintf('%.17g|%s\n', acc, class(v));
        """, compiled: true, bailed: true);

    [Fact]
    public void ForbiddenConstructsInAWalkedStatementRefuseTheLoop()
    {
        AssertParity("""
            acc = 0;
            for k = 1:3
                acc = acc + k;
                eval('acc = acc * 10;');
            end
            fprintf('%.17g\n', acc);
            """, compiled: false);
        AssertParity("""
            acc = 0;
            for k = 1:3
                acc = acc + k;
                if k == 2
                    global shared
                    shared = acc;
                end
            end
            fprintf('%.17g\n', acc);
            """, compiled: false);
        AssertParity("""
            acc = 0;
            for k = 1:3
                acc = acc + k;
                clear acc
                acc = 1;
            end
            fprintf('%.17g\n', acc);
            """, compiled: false);
    }

    [Fact]
    public void AWalkedStatementThatShadowsABuiltinFinishesByTheWalk() => AssertParity("""
        function shadow()
            assignin('caller', 'floor', 100:110);
        end
        acc = 0;
        for k = 1:5
            acc = acc + floor(k);
            if k == 2
                shadow();
            end
        end
        fprintf('%.17g\n', acc);
        """, compiled: true, bailed: true, needsPacked: false);

    [Fact]
    public void AReturnInsideAWalkedStatementLeavesTheFunction() => AssertParity("""
        function r = partial(n)
            r = 0;
            for k = 1:n
                r = r + k;
                if r > 6
                    return;
                end
            end
            r = -1;
        end
        fprintf('%.17g|%.17g\n', partial(3), partial(10));
        """, compiled: true, bailed: true, needsPacked: false);

    [Fact]
    public void TheVectorClassOffRefusesWhatItRefusedBefore()
    {
        bool previous = JgsLoopJit.Vectors;
        JgsLoopJit.Vectors = false;
        try
        {
            Run fast = RunWith(jit: true, Data + """
                y = zeros(1, 5);
                for k = 1:5
                    y(k) = k;
                end
                fprintf('%.17g\n', sum(y));
                """ + Helpers);
            Assert.True(fast.Success, fast.Message);
            Assert.Equal(0, fast.Compiled);
        }
        finally
        {
            JgsLoopJit.Vectors = previous;
        }
    }
}
