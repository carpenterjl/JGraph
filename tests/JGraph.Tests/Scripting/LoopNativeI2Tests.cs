using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I2 (ADR 0325): a compiled loop that runs long moves from the register
/// machine to its program emitted as IL. Every script runs on three roads - the walk, the register
/// machine alone, and the emitted method with the move after one back edge, so every loop with a
/// second pass runs emitted - in a session whose workspace survives a failure; the outcome, the
/// text and every variable's bits must agree, and the emitted road must really have moved.
/// </summary>
[Collection("JG facade")]
public class LoopNativeI2Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = [];
    private readonly long _limit = Interpreter.MaxSteps;

    public LoopNativeI2Tests() => JG.Reset();

    public void Dispose()
    {
        Interpreter.MaxSteps = _limit;
        JG.Reset();
    }

    private enum Road
    {
        Walk,
        Registers,
        Emitted,
    }

    private sealed record Outcome(bool Success, string? Message, string Text, string Workspace, long Moves, long Bails);

    private async Task<Outcome> RunAsync(Road road, string code, long limit = 0)
    {
        (bool jit, bool native, int threshold) = (JgsLoopJit.Enabled, JgsLoopNative.Enabled, JgsLoopNative.Threshold);
        JgsLoopJit.Enabled = road != Road.Walk;
        JgsLoopNative.Enabled = road == Road.Emitted;
        JgsLoopNative.Threshold = 1;
        if (limit > 0)
        {
            Interpreter.MaxSteps = limit;
        }

        try
        {
            JG.Reset();
            _output.Mark();
            long moves = JgsLoopNative.Moves;
            long bails = JgsLoopJit.Bails;
            await using var session = (JgsReplSession)new MatlabScriptEngine().CreateSession(
                new ScriptContext(_output, (_, figure) => _figures.Add(figure), null));
            ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
            string workspace = string.Join("\n", session.Workspace.Locals
                .OrderBy(static p => p.Key, StringComparer.Ordinal)
                .Select(static p => p.Key + "=" + Describe(p.Value)));
            return new Outcome(result.Success, result.Message, _output.TextSinceMark, workspace,
                JgsLoopNative.Moves - moves, JgsLoopJit.Bails - bails);
        }
        finally
        {
            JgsLoopJit.Enabled = jit;
            JgsLoopNative.Enabled = native;
            JgsLoopNative.Threshold = threshold;
            Interpreter.MaxSteps = _limit;
        }
    }

    /// <summary>A variable's identity down to the bit: the class, and for a scalar or a packed array its bits.</summary>
    private static string Describe(JgsValue value) => value.Type switch
    {
        JgsType.Number => $"{value.NumericClass}:{BitConverter.DoubleToInt64Bits(value.AsNumber):X16}",
        JgsType.Bool => $"logical:{value.AsBool}",
        JgsType.Complex => $"complex:{BitConverter.DoubleToInt64Bits(value.AsComplex.Real):X16},{BitConverter.DoubleToInt64Bits(value.AsComplex.Imaginary):X16}",
        JgsType.Array when value.IsPacked => $"array[{value.Rows}x{value.Cols}]:" + string.Join(",",
            value.AsBuffer.AsSpan().ToArray().Select(static d => BitConverter.DoubleToInt64Bits(d).ToString("X16"))),
        _ => value.TypeName + $"[{value.Rows}x{value.Cols}]",
    };

    /// <summary>The three roads agree; answers the emitted road's outcome.</summary>
    private async Task<Outcome> AssertRoadsAgreeAsync(string code, bool mustMove = true, long limit = 0)
    {
        Outcome walk = await RunAsync(Road.Walk, code, limit);
        Outcome registers = await RunAsync(Road.Registers, code, limit);
        Outcome emitted = await RunAsync(Road.Emitted, code, limit);
        foreach ((string name, Outcome fast) in new[] { ("registers", registers), ("emitted", emitted) })
        {
            Assert.True(walk.Success == fast.Success, $"{name}: success {walk.Success} vs {fast.Success} ({walk.Message} / {fast.Message})");
            Assert.True(walk.Message == fast.Message, $"{name}: message '{walk.Message}' vs '{fast.Message}'");
            Assert.True(walk.Text == fast.Text, $"{name}: text\n{walk.Text}\nvs\n{fast.Text}");
            Assert.True(walk.Workspace == fast.Workspace, $"{name}: workspace\n{walk.Workspace}\nvs\n{fast.Workspace}");
        }

        Assert.Equal(0, registers.Moves);
        if (mustMove)
        {
            Assert.True(emitted.Moves > 0, "the loop never moved to its emitted method");
        }

        return emitted;
    }

    [Fact]
    public Task TheHead2HeadLoopRunsEmittedAndKeepsEveryBit() => AssertRoadsAgreeAsync("""
        acc = 0;
        v = 1.0;
        for k = 1:20000
            v = mod(v * 1.0000001 + 0.001, 2);
            if v > 1
                acc = acc + v;
            end
        end
        fprintf('%.17g %.17g %d\n', acc, v, k);
        """);

    [Fact]
    public Task EveryScalarOpAndBuiltinAgrees() => AssertRoadsAgreeAsync("""
        a = 0; b = 1; c = -2.5; t = true; n = 0;
        for k = 1:200
            x = k / 7 - 3;
            a = a + sin(x) * cos(x) - tan(x / 9) + atan(x) + exp(-abs(x)) + floor(x) + ceil(x) + round(x);
            b = b + sqrt(k) + log(k) + log10(k) + asin(1 / k) + acos(-1 / k) + atan2(x, 2) + rem(x, 3) + mod(x, -3);
            c = min(c, x) + max(x, 0) - x^2 / 100 + 2^(-k / 50) + (-x) - (+x);
            t = (x < 1 & k >= 3) | ~(x == 2) | (x ~= 0 && k <= 100) || x > 5;
            n = n + (x < 0) + (x <= 0) + (x > 0) + (x >= 0) + (x == 0) + (x ~= 0) + pi;
        end
        fprintf('%.17g %.17g %.17g %d %.17g\n', a, b, c, t, n);
        """);

    [Fact]
    public Task NaNTakesTheSameSideOfEveryComparisonAndBranch() => AssertRoadsAgreeAsync("""
        hits = zeros(1, 1); h = 0; q = 0;
        for k = 1:12
            x = NaN;
            if k > 6, x = k; end
            if x < 3, h = h + 1; end
            if x <= 3, h = h + 10; end
            if x > 3, h = h + 100; end
            if x >= 3, h = h + 1000; end
            if x == 7, h = h + 10000; end
            if x ~= 7, h = h + 100000; end
            if x, q = q + 1; end
            if ~x, q = q + 10; end
            q = q + (x < 3) + (x ~= 7) * 2 + ~(x > 3) * 4;
        end
        fprintf('%d %d\n', h, q);
        """);

    [Fact]
    public Task NestedRangesWhileLoopsBreakAndContinue() => AssertRoadsAgreeAsync("""
        s = 0; w = 0;
        for i = 1:40
            for j = i:-0.5:1
                if j == 3, continue; end
                s = s + i * j;
                if s > 1e6, break; end
            end
            k = 0;
            while k < i
                k = k + 1.5;
                if k > 30, break; end
                w = w + k;
            end
        end
        for z = 1:0
            s = -1;
        end
        fprintf('%.17g %.17g %g %g\n', s, w, k, j);
        disp(isempty(z));
        """);

    [Fact]
    public async Task AnAnswerThatLeavesTheRealsBailsAndFinishesByTheWalk()
    {
        Outcome emitted = await AssertRoadsAgreeAsync("""
            v = 4; acc = 0;
            for k = 1:30
                v = v - 0.5;
                acc = acc + sqrt(v);
                p = (v - 2)^0.5;
            end
            disp(acc); disp(p);
            """);
        Assert.True(emitted.Bails > 0);
    }

    [Fact]
    public async Task AWalkedStatementLeavesAndReentersTheEmittedMethod()
    {
        Outcome emitted = await AssertRoadsAgreeAsync("""
            acc = 0; m = 0;
            for k = 1:50
                acc = acc + k / 3;
                if mod(k, 10) == 0
                    fprintf('%d %.17g\n', k, acc);
                    m = max([m k acc]);
                end
            end
            disp(m);
            """);
        Assert.True(emitted.Bails >= 5);
    }

    [Fact]
    public Task ALogicalVariableStaysLogicalThroughTheMove() => AssertRoadsAgreeAsync("""
        f = false; g = 0; cnt = 0;
        for k = 1:25
            f = ~f;
            g = f;
            if g, cnt = cnt + 1; end
        end
        disp(class(f)); disp(class(g)); disp(cnt);
        """);

    [Fact]
    public async Task TheStepLimitStopsOnTheSameStatementWithTheSameVariables()
    {
        const string Code = """
            acc = 0; v = 1;
            for k = 1:200
                v = v * 1.25 + 1;
                acc = acc + v;
                w = v - acc;
            end
            fprintf('%.17g|%.17g|%.17g|%d\n', acc, v, w, k);
            """;
        for (long budget = 3; budget <= 70; budget++)
        {
            await AssertRoadsAgreeAsync(Code, mustMove: false, limit: budget);
        }
    }

    [Fact]
    public Task AVectorOpKeepsTheLoopOnTheRegisterMachine() => AssertRoadsAgreeAsync("""
        x = zeros(1, 50);
        for k = 1:50
            x(k) = k^2 / 3;
        end
        fprintf('%.17g\n', sum(x));
        """, mustMove: false);
}
