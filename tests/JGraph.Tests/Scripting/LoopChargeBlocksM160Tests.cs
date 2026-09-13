using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0160 (12b): a compiled loop charges a straight-line block's statements at once and fuses a
/// comparison into its branch and a for loop's back edge into one op — and the step limit must
/// still fall on the same statement, with the same variables left behind, as the tree walk's own
/// per-statement count puts it. Every script here runs under a sweep of step budgets that places
/// the limit on every statement of the loop in turn (each block boundary at −1, 0 and +1), on the
/// walk and on the compiled road with the fusion and the blocks on and off, in a session whose
/// workspace survives the failure; the outcome, the printed text and every variable's bits must
/// agree across the roads at every budget.
/// </summary>
[Collection("JG facade")]
public class LoopChargeBlocksM160Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = [];
    private readonly long _limit = Interpreter.MaxSteps;

    public LoopChargeBlocksM160Tests() => JG.Reset();

    public void Dispose()
    {
        Interpreter.MaxSteps = _limit;
        JG.Reset();
    }

    private sealed record Road(bool Jit, bool Fusion, bool Blocks, bool Unchecked = true)
    {
        public override string ToString() => Jit ? $"jit(fusion={Fusion}, blocks={Blocks}, unchecked={Unchecked})" : "walk";
    }

    private static readonly Road[] Roads =
    [
        new(Jit: false, Fusion: true, Blocks: true),
        new(Jit: true, Fusion: true, Blocks: true),
        new(Jit: true, Fusion: false, Blocks: true),
        new(Jit: true, Fusion: true, Blocks: false),
        new(Jit: true, Fusion: false, Blocks: false),
        new(Jit: true, Fusion: true, Blocks: true, Unchecked: false),
    ];

    private sealed record Outcome(bool Success, string? Message, string Text, string Workspace, long Compiled);

    private async Task<Outcome> RunAsync(Road road, long limit, string code)
    {
        (bool jit, bool fusion, bool blocks, bool noChecks) = (JgsLoopJit.Enabled, JgsLoopJit.Fusion, JgsLoopJit.ChargeBlocks, JgsLoopJit.Unchecked);
        JgsLoopJit.Enabled = road.Jit;
        JgsLoopJit.Fusion = road.Fusion;
        JgsLoopJit.ChargeBlocks = road.Blocks;
        JgsLoopJit.Unchecked = road.Unchecked;
        Interpreter.MaxSteps = limit;
        try
        {
            JG.Reset();
            _output.Mark();
            long before = JgsLoopJit.CompiledRuns;
            await using var session = (JgsReplSession)new MatlabScriptEngine().CreateSession(
                new ScriptContext(_output, (_, figure) => _figures.Add(figure), null));
            ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
            string workspace = string.Join("\n", session.Workspace.Locals
                .OrderBy(static p => p.Key, StringComparer.Ordinal)
                .Select(static p => p.Key + "=" + Describe(p.Value)));
            return new Outcome(result.Success, result.Message, _output.TextSinceMark, workspace, JgsLoopJit.CompiledRuns - before);
        }
        finally
        {
            JgsLoopJit.Enabled = jit;
            JgsLoopJit.Fusion = fusion;
            JgsLoopJit.ChargeBlocks = blocks;
            JgsLoopJit.Unchecked = noChecks;
            Interpreter.MaxSteps = _limit;
        }
    }

    /// <summary>A variable's identity down to the bit: the class, and for a scalar its bits.</summary>
    private static string Describe(JgsValue value) => value.Type switch
    {
        JgsType.Number => $"{value.NumericClass}:{BitConverter.DoubleToInt64Bits(value.AsNumber):X16}",
        JgsType.Bool => $"logical:{value.AsBool}",
        JgsType.Array when value.IsPacked => $"array[{value.Rows}x{value.Cols}]:" + string.Join(",",
            value.AsBuffer.AsSpan().ToArray().Select(static d => BitConverter.DoubleToInt64Bits(d).ToString("X16"))),
        _ => value.TypeName,
    };

    /// <summary>
    /// Runs <paramref name="code"/> on every road at every budget from 1 to <paramref name="budgets"/>
    /// and asserts the roads agree at each: same success, same message, same text, same workspace
    /// bits. At least one budget must be generous enough for the script to finish, and the compiled
    /// roads must really have compiled the loop.
    /// </summary>
    private async Task SweepAsync(string code, int budgets, bool mustFinish = true)
    {
        bool finished = false;
        bool compiled = false;
        string? last = null;
        for (long limit = 1; limit <= budgets; limit++)
        {
            Outcome walk = await RunAsync(Roads[0], limit, code);
            finished |= walk.Success;
            last = walk.Message;
            foreach (Road road in Roads.Skip(1))
            {
                Outcome fast = await RunAsync(road, limit, code);
                string where = $"{road} at budget {limit}";
                Assert.True(walk.Success == fast.Success, $"{where}: success {walk.Success} vs {fast.Success} ({walk.Message} / {fast.Message})");
                Assert.True(walk.Message == fast.Message, $"{where}: message '{walk.Message}' vs '{fast.Message}'");
                Assert.True(walk.Text == fast.Text, $"{where}: text\n{walk.Text}\nvs\n{fast.Text}");
                Assert.True(walk.Workspace == fast.Workspace, $"{where}: workspace\n{walk.Workspace}\nvs\n{fast.Workspace}");
                compiled |= fast.Compiled > 0;
            }
        }

        Assert.True(finished || !mustFinish, $"no budget let the script finish; the last message was: {last}");
        Assert.True(compiled, "the compiled roads never compiled the loop");
    }

    [Fact]
    public Task AStraightLineBlockStopsAtTheSameStatement() => SweepAsync("""
        acc = 0; v = 1;
        for k = 1:12
            v = v * 1.5 + 1;
            acc = acc + v;
            w = v - acc;
        end
        fprintf('%.17g|%.17g|%.17g|%d\n', acc, v, w, k);
        """, budgets: 60);

    [Fact]
    public Task BreakAndContinueSplitTheBlocks() => SweepAsync("""
        a = 0; b = 0; c = 0;
        for k = 1:30
            a = a + 1;
            if a > 3
                a = a - 1;
                continue;
            end
            b = b + 2;
            if b > 5
                break;
            end
            c = c + 3;
            c = c * 1.001;
        end
        fprintf('%.17g|%.17g|%.17g|%d\n', a, b, c, k);
        """, budgets: 40);

    [Fact]
    public Task ArmsOfUnequalLengthChargeWhatTheyRun() => SweepAsync("""
        a = 0; b = 0; c = 0; d = 0;
        for k = 1:9
            if mod(k, 3) == 0
                a = a + k;
                b = b + a;
                c = c + b;
            elseif k > 6
                d = d - 1;
            else
                d = d + 1;
            end
            a = a + 0.5;
        end
        fprintf('%.17g|%.17g|%.17g|%.17g\n', a, b, c, d);
        """, budgets: 60);

    [Fact]
    public Task AGuardedCallThatLeavesTheRealsFinishesByTheWalkOnBudget() => SweepAsync("""
        a = 0; b = 0; c = 0;
        for k = 1:8
            a = a + 3;
            b = sqrt(a - 10);
            c = c + 1;
            c = c + b;
        end
        fprintf('%.17g|%s\n', c, class(b));
        """, budgets: 50);

    [Fact]
    public Task AConditionThatBailsResumesTheBlocks() => SweepAsync("""
        a = 0; hits = 0; c = 0;
        for k = 1:10
            a = a + 3;
            if sqrt(a - 12) > 1
                hits = hits + 1;
            end
            c = c + a;
            c = c - 1;
        end
        fprintf('%.17g|%.17g|%.17g\n', a, hits, c);
        """, budgets: 100);

    [Fact]
    public Task NestedLoopsAndAWhileChargeTheirOwnBlocks() => SweepAsync("""
        s = 0; t = 0;
        for i = 1:4
            s = s + i;
            for j = 1:i
                s = s + j;
                t = t + 1;
            end
            t = t * 2;
        end
        n = 0;
        while n < 5
            n = n + 1;
            s = s - n;
            t = t + n;
        end
        fprintf('%.17g|%.17g|%d|%d|%d\n', s, t, i, j, n);
        """, budgets: 80);

    [Fact]
    public Task ANestedZeroStepStopsWhereTheWalkThrows() => SweepAsync("""
        w = 0;
        for k = 1:3
            w = w + 1;
            w = w + 1;
            for j = 1:(k - 3):5
                w = w + 100;
            end
        end
        fprintf('never\n');
        """, budgets: 30, mustFinish: false);

    /// <summary>An output sink that cancels the run the moment a given line is printed.</summary>
    private sealed class CancelOnLine(string line, CancellationTokenSource source) : IScriptOutput
    {
        public List<string> Lines { get; } = [];

        public void Write(string text) => See(text);

        public void WriteLine(string text) => See(text + "\n");

        public void WriteError(string text)
        {
        }

        private void See(string text)
        {
            Lines.Add(text);
            if (text.Contains(line, StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }

    /// <summary>
    /// Cancellation between blocks: the walk polls at every statement and the compiled loop at
    /// every iteration (the M98 rule, unchanged by ADR 0160), so a cancellation that lands during
    /// the last statement of an iteration is seen by both at the next iteration's boundary, with
    /// the same variables left behind. The print that cancels is a walked statement (12d) inside
    /// the compiled loop.
    /// </summary>
    [Fact]
    public async Task CancellationBetweenBlocksLeavesTheSameVariables()
    {
        const string code = """
            acc = 0; k2 = 0;
            for k = 1:100
                acc = acc + k;
                acc = acc * 1.001;
                k2 = k * k;
                fprintf('line %d\n', k);
            end
            """;
        string? walkWorkspace = null;
        foreach (Road road in Roads)
        {
            (bool jit, bool fusion, bool blocks) = (JgsLoopJit.Enabled, JgsLoopJit.Fusion, JgsLoopJit.ChargeBlocks);
            JgsLoopJit.Enabled = road.Jit;
            JgsLoopJit.Fusion = road.Fusion;
            JgsLoopJit.ChargeBlocks = road.Blocks;
            try
            {
                JG.Reset();
                using var source = new CancellationTokenSource();
                var sink = new CancelOnLine("line 3\n", source);
                long before = JgsLoopJit.CompiledRuns;
                await using var session = (JgsReplSession)new MatlabScriptEngine().CreateSession(
                    new ScriptContext(sink, (_, figure) => _figures.Add(figure), null));
                ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", source.Token);
                Assert.False(result.Success, $"{road}: the run was not cancelled");
                Assert.Contains("cancel", result.Message ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.Equal(3, sink.Lines.Count);
                string workspace = string.Join("\n", session.Workspace.Locals
                    .OrderBy(static p => p.Key, StringComparer.Ordinal)
                    .Select(static p => p.Key + "=" + Describe(p.Value)));
                if (road.Jit && JgsLoopJit.Vectors) // the print that cancels is a walked statement, a 12d feature
                {
                    Assert.True(JgsLoopJit.CompiledRuns > before, $"{road} did not compile the loop");
                    Assert.True(walkWorkspace == workspace, $"{road}: workspace\n{walkWorkspace}\nvs\n{workspace}");
                }
                else
                {
                    walkWorkspace = workspace;
                    Assert.Contains("k=Double:4008000000000000", workspace); // k is 3
                }
            }
            finally
            {
                JgsLoopJit.Enabled = jit;
                JgsLoopJit.Fusion = fusion;
                JgsLoopJit.ChargeBlocks = blocks;
            }
        }
    }

    [Fact]
    public async Task FusedComparisonsAgreeOnNanAndSignedZero()
    {
        // Seven values a scalar loop can make — 0, -0, 1, -1, NaN, Inf, -Inf — paired every way
        // through the six comparisons, each pair's six answers folded into two sums.
        const string code = """
            s1 = 0; s2 = 0;
            for i = 1:7
                for j = 1:7
                    if i == 1; a = 0; elseif i == 2; a = -0; elseif i == 3; a = 1; elseif i == 4; a = -1;
                    elseif i == 5; a = 0/0; elseif i == 6; a = 1/0; else; a = -1/0; end
                    if j == 1; b = 0; elseif j == 2; b = -0; elseif j == 3; b = 1; elseif j == 4; b = -1;
                    elseif j == 5; b = 0/0; elseif j == 6; b = 1/0; else; b = -1/0; end
                    r = 0;
                    if a < b; r = r + 1; end
                    if a <= b; r = r + 2; end
                    if a > b; r = r + 4; end
                    if a >= b; r = r + 8; end
                    if a == b; r = r + 16; end
                    if a ~= b; r = r + 32; end
                    w = 0;
                    while w < b && w < 3
                        w = w + 1;
                    end
                    s1 = s1 + r * (i * 7 + j) + w;
                    s2 = s2 + r * r * (i * 7 + j);
                end
            end
            fprintf('%.17g|%.17g\n', s1, s2);
            """;
        Outcome walk = await RunAsync(Roads[0], 1_000_000, code);
        Assert.True(walk.Success, walk.Message);
        foreach (Road road in Roads.Skip(1))
        {
            Outcome fast = await RunAsync(road, 1_000_000, code);
            Assert.True(fast.Success, fast.Message);
            Assert.True(fast.Compiled > 0, $"{road} did not compile the loop");
            Assert.Equal(walk.Text, fast.Text);
            Assert.Equal(walk.Workspace, fast.Workspace);
        }
    }
}
