using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I1 (ADR 0324): an ODE right-hand side compiled once per solve. Every script
/// runs with the compiler on and off and must print the same bytes (each output as
/// <c>num2hex</c>), and each case names the road it expects through the compiler's counters, since a
/// compiled body that agrees by accident on one case is invisible in the output.
/// </summary>
[Collection("JG facade")]
public class OdeBodyI1Tests : IDisposable
{
    public OdeBodyI1Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Compiled, long Walked);

    private static Run RunWith(bool compile, string code)
    {
        bool previous = JgsOdeCompile.Enabled;
        JgsOdeCompile.Enabled = compile;
        long compiledBefore = JgsOdeCompile.CompiledSolves;
        long walkedBefore = JgsOdeCompile.Walked;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText, result.Success, result.Message,
                JgsOdeCompile.CompiledSolves - compiledBefore, JgsOdeCompile.Walked - walkedBefore);
        }
        finally
        {
            JgsOdeCompile.Enabled = previous;
        }
    }

    /// <summary>Both roads, the same bytes and the same outcome; answers the compiled run.</summary>
    private static Run AssertParity(string code, long compiledSolves, bool expectSuccess = true)
    {
        Run fast = RunWith(compile: true, code);
        Run walk = RunWith(compile: false, code);
        Assert.Equal(expectSuccess, walk.Success);
        Assert.Equal(walk.Success, fast.Success);
        Assert.Equal(walk.Message, fast.Message);
        Assert.Equal(walk.Output, fast.Output);
        Assert.Equal(0, walk.Compiled);
        Assert.Equal(compiledSolves, fast.Compiled);
        return fast;
    }

    private const string Hex = "h = @(x) sprintf('%s', reshape(num2hex(x(:))', 1, []));\n";

    [Fact]
    public void TheExplicitFamilyOnLorenzCompilesAndKeepsEveryBit()
    {
        Run run = AssertParity(Hex + """
            lorenz = @(t, y) [10*(y(2) - y(1)); y(1)*(28 - y(3)) - y(2); y(1)*y(2) - (8/3)*y(3)];
            names = {'ode45', 'ode23', 'ode113', 'ode78', 'ode89'};
            for k = 1:5
                s = str2func(names{k});
                [t, y] = s(lorenz, [0 5], [1; 1; 1]);
                fprintf('%s %d %s\n', names{k}, numel(t), h([t; y(:)]));
            end
            """, compiledSolves: 5);
        Assert.Equal(0, run.Walked);
    }

    [Fact]
    public void CapturedScalarsPowersAndTheScalarBuiltinsCompile() => AssertParity(Hex + """
        wn = 4; zeta = 0.15; sin2 = 2;
        osc = @(t, y) [y(2); -2*zeta*wn*y(2) - wn^2*y(1) + sin(t)*pi/sin2];
        orbit = @(t, y) [y(3); y(4); -y(1)/(y(1)^2 + y(2)^2)^1.5; -y(2)/(y(1)^2 + y(2)^2)^1.5];
        odd = @(t, y) [sqrt(abs(y(2)) + 1) - 1; -y(1) .* exp(-t); mod(t, 3) + atan2(y(1), 2) - rem(y(3), 2)/(2\y(2))];
        [t, y] = ode45(osc, [0 6], [1; 0]); disp(h([t; y(:)]));
        [t, y] = ode113(orbit, [0 6], [1; 0; 0; 1], odeset('RelTol', 1e-9, 'AbsTol', 1e-11)); disp(h([t; y(:)]));
        [t, y] = ode23(odd, [0 3], [1; 4; 0.5]); disp(h([t; y(:)]));
        """, compiledSolves: 3);

    [Fact]
    public void AScalarStateMayBeReadBareAndAPerCallCaptureCompilesEachTime() => AssertParity(Hex + """
        acc = 0;
        for k = 1:5
            [~, y] = ode45(@(t, y) -k*y/100, [0 1], 1);
            acc = acc + y(end);
        end
        disp(num2hex(acc));
        """, compiledSolves: 5);

    [Fact]
    public void TheStiffFamilyDifferencesItsJacobianThroughTheCompiledBody() => AssertParity(Hex + """
        vdp = @(t, y) [y(2); 1000*(1 - y(1)^2)*y(2) - y(1)];
        names = {'ode15s', 'ode23s', 'ode23t', 'ode23tb'};
        for k = 1:4
            s = str2func(names{k});
            sol = s(vdp, [0 300], [2; 0]);
            fprintf('%s %d %d %d %s\n', names{k}, sol.stats.nsteps, sol.stats.npds, sol.stats.nfevals, h([sol.x(:); sol.y(:)]));
        end
        """, compiledSolves: 4);

    [Fact]
    public void ARowAnswerCompilesAsTheWalkReadsIt() => AssertParity(Hex + """
        [t, y] = ode45(@(t, y) [-y(2), y(1)], [0 2], [1; 0]);
        disp(h([t; y(:)]));
        """, compiledSolves: 1);

    [Fact]
    public void APowerThatLeavesTheRealsWalksThatCallAndFailsAsTheWalkDoes()
    {
        Run run = AssertParity("""
            [t, y] = ode45(@(t, y) [1; (y(1) - 2)^0.5], [0 4], [0; 0]);
            """, compiledSolves: 1, expectSuccess: false);
        Assert.True(run.Walked > 0);
    }

    [Fact]
    public void ASqrtOutsideItsDomainWalksThatCall()
    {
        Run run = AssertParity("""
            [t, y] = ode45(@(t, y) [1; sqrt(y(1) - 2)], [0 4], [0; 0]);
            """, compiledSolves: 1, expectSuccess: false);
        Assert.True(run.Walked > 0);
    }

    [Theory]
    [InlineData("f = @(t, y) [y(1); y(2)]; ode45(f, [0 1], [1; 2; 3]);", false)]       // the walk's size refusal
    [InlineData("f = @(t, y) [y(1); y(4); y(2)]; ode45(f, [0 1], [1; 2; 3]);", false)] // an index past the state
    [InlineData("f = @(t, y) -y; [t, y] = ode45(f, [0 1], [1; 2]); disp(y(end, :));", true)] // the whole state
    [InlineData("A = [1 2]; f = @(t, y) -A(2)*y; [t, y] = ode45(f, [0 1], 1); disp(y(end));", true)] // a captured array
    [InlineData("k = single(2); f = @(t, y) -k*y; [t, y] = ode45(f, [0 1], 1); disp(class(y)); disp(y(end));", true)]
    [InlineData("k = int8(2); f = @(t, y) -k*y; [t, y] = ode45(f, [0 1], 1); disp(y(end));", true)]
    [InlineData("f = @(t, y) [y(2); -helper(y(1))]; [t, y] = ode45(f, [0 1], [1; 0]); disp(y(end, :));\nfunction r = helper(x)\nr = 3*x;\nend", true)]
    [InlineData("sin = @(x) 2*x; f = @(t, y) -sin(y); [t, y] = ode45(f, [0 1], 1); disp(y(end));", true)] // a captured handle
    [InlineData("f = @(t, y) [y(2); -y(1)*t > 1]; [t, y] = ode45(f, [0 1], [1; 0]); disp(y(end, :));", true)] // a comparison
    [InlineData("f = @(t, y) -y(1:1); [t, y] = ode45(f, [0 1], 1); disp(y(end));", true)]
    [InlineData("f = @(t, y, z) -y; [t, y] = ode45(f, [0 1], 1); disp(y(end));", true)]
    public void BodiesOutsideTheWhitelistWalk(string code, bool expectSuccess) =>
        AssertParity(code, compiledSolves: 0, expectSuccess);

    [Fact]
    public void AVariableNamedLikeABuiltinIsTheCapturedScalar() => AssertParity(Hex + """
        exp = 3;
        [t, y] = ode45(@(t, y) -exp*y, [0 1], 1);
        disp(h([t; y]));
        """, compiledSolves: 1);

    [Fact]
    public void AnAnonymousBodyAsTheEventsHandlerIsLeftAlone() => AssertParity(Hex + """
        ball = @(t, y) [y(2); -9.8];
        sol = ode23(ball, [0 30], [0; 20], odeset('Events', @(t, y) deal(y(1), 1, -1), 'Refine', 1));
        disp(h([sol.x(:); sol.y(:); sol.xe(:)]));
        """, compiledSolves: 1);
}
