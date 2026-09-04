using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The explicit ODE family (M125): <c>ode23</c>, <c>ode78</c>, <c>ode89</c> and <c>ode113</c>
/// beside <c>ode45</c>, the options they all act on, <c>odextend</c>, and the output functions.
/// </summary>
/// <remarks>
/// The step counts here are R2025b's, recorded by the parity fixture
/// <c>m125_ode_explicit.m</c>; a count is the algorithm rather than the answer, and one that
/// differs by a single step says a constant differs. What is graded loosely is graded at the
/// accuracy the run was asked for.
/// </remarks>
[Collection("JG facade")]
public class MatlabOdeFamilyM125Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabOdeFamilyM125Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Refuses(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal");
        return result.Message ?? string.Empty;
    }

    [Fact]
    public void EverySolverTakesTheStepsMatlabTakesOnVanDerPol() =>
        Assert.Equal("ode23 161 21 547|ode45 59 10 415|ode78 35 6 667|ode89 39 9 954|ode113 175 12 363|", Run("""
            vdp = @(t, y) [y(2); (1 - y(1)^2) * y(2) - y(1)];
            names = {'ode23', 'ode45', 'ode78', 'ode89', 'ode113'};
            for i = 1:5
                f = str2func(names{i});
                sol = f(vdp, [0 20], [2; 0]);
                fprintf('%s %d %d %d|', sol.solver, sol.stats.nsteps, sol.stats.nfailed, sol.stats.nfevals);
            end
            """));

    [Fact]
    public void EverySolverAnswersInTheThreeShapes() =>
        Assert.Equal("ok ok ok ok ok ", Run("""
            osc = @(t, y) [y(2); -y(1)];
            ev = @(t, y) evfun(t, y);
            names = {'ode23', 'ode45', 'ode78', 'ode89', 'ode113'};
            for i = 1:5
                f = str2func(names{i});
                sol = f(osc, [0 4], [1; 0]);
                [t, y] = f(osc, [0 4], [1; 0]);
                [t2, y2, te, ye, ie] = f(osc, [0 4], [1; 0], odeset('Events', ev, 'RelTol', 1e-8, 'AbsTol', 1e-10));
                good = isstruct(sol) && isequal(fieldnames(sol)', {'solver', 'extdata', 'x', 'y', 'stats', 'idata'});
                good = good && size(y, 1) == numel(t) && size(y, 2) == 2 && t(end) == 4;
                good = good && numel(te) == 1 && abs(te - pi/2) < 1e-7 && ie == 1 && size(ye, 2) == 2 && abs(t2(end) - te) < 1e-12;
                if good, fprintf('ok '); else, fprintf('bad '); end
            end
            function [v, term, dir] = evfun(t, y)
            v = y(1); term = 1; dir = -1;
            end
            """));

    [Fact]
    public void ATerminalEventStopsTheRunAndOdextendCarriesOn() =>
        Assert.Equal("4.08163265306 10 11 22 20 7.75510204082 ode23 2", Run("""
            ball = @(t, y) [y(2); -9.8];
            sol = ode23(ball, [0 30], [0; 20], odeset('Events', @bounce, 'Refine', 1));
            ext = odextend(sol, ball, 30, [0; -0.9 * sol.ye(2)]);
            ext2 = odextend(ext, [], 40);
            fprintf('%.12g %d %d %d %d %.12g %s %d', sol.xe, sol.stats.nsteps, numel(sol.x), numel(ext.x), ...
              ext.stats.nsteps, ext.xe(2), ext.solver, numel(ext2.xe));
            function [v, term, dir] = bounce(t, y)
            v = y(1); term = 1; dir = -1;
            end
            """));

    [Fact]
    public void DevalReadsEverySolversOwnInterpolantAndItsDerivative() =>
        Assert.Equal("ok ok ok ok ok ", Run("""
            osc = @(t, y) [y(2); -y(1)];
            names = {'ode23', 'ode45', 'ode78', 'ode89', 'ode113'};
            for i = 1:5
                f = str2func(names{i});
                sol = f(osc, [0 4], [1; 0], odeset('RelTol', 1e-8, 'AbsTol', 1e-10));
                [z, zp] = deval(sol, [0.5 1.5 2.5]);
                good = max(abs(z(1, :) - cos([0.5 1.5 2.5]))) < 1e-6 && max(abs(zp(1, :) + sin([0.5 1.5 2.5]))) < 1e-5;
                good = good && isequal(size(deval(sol, [1 2], 2)), [1 2]) && deval(sol, 0, 1) == 1;
                if good, fprintf('ok '); else, fprintf('bad '); end
            end
            """));

    [Fact]
    public void TheSolutionStructureHasMatlabsPagesAndFields() =>
        Assert.Equal("[2 7 16] [2 4 26] [2 12 16] [2 14 16] [2 7 28] [6 28] 6 [1 28]", Run("""
            osc = @(t, y) [y(2); -y(1)];
            a = ode45(osc, [0 4], [1; 0]); b = ode23(osc, [0 4], [1; 0]);
            c = ode78(osc, [0 4], [1; 0]); d = ode89(osc, [0 4], [1; 0]);
            e = ode113(osc, [0 4], [1; 0]);
            fprintf('%s %s %s %s %s %s %d %s', mat2str(size(a.idata.f3d)), mat2str(size(b.idata.f3d)), ...
              mat2str(size(c.idata.f3d)), mat2str(size(d.idata.f3d)), mat2str(size(e.idata.phi3d)), ...
              mat2str(size(e.idata.psi2d)), e.idata.klastvec(end), mat2str(size(e.idata.klastvec)));
            """));

    [Fact]
    public void NonNegativeHoldsTheFloorAndCountsTheExtraEvaluations() =>
        Assert.Equal("117 141 309 316 91 | 0 0 0 0 0", Run("""
            decay = @(t, y) -abs(y);
            names = {'ode23', 'ode45', 'ode78', 'ode89', 'ode113'};
            evals = zeros(1, 5); mins = zeros(1, 5);
            for i = 1:5
                f = str2func(names{i});
                sol = f(decay, [0 40], 1, odeset('NonNegative', 1));
                evals(i) = sol.stats.nfevals; mins(i) = min(sol.y) < 0;
            end
            fprintf('%d %d %d %d %d | %d %d %d %d %d', evals, mins);
            """));

    [Fact]
    public void AMassMatrixIsTheDerivativeDividedByIt() =>
        Assert.Equal("1 1", Run("""
            osc = @(t, y) [y(2); -y(1)];
            M = [2 1; 1 3];
            [t1, y1] = ode45(osc, [0 5], [1; 0], odeset('Mass', M));
            [t2, y2] = ode45(@(t, y) M \ osc(t, y), [0 5], [1; 0]);
            [t3, y3] = ode23(osc, [0 5], [1; 0], odeset('Mass', @(t) [2 + t, 0; 0, 1], 'MStateDependence', 'none'));
            [t4, y4] = ode23(@(t, y) [2 + t, 0; 0, 1] \ osc(t, y), [0 5], [1; 0]);
            fprintf('%d %d', numel(t1) == numel(t2) && max(abs(y1(:) - y2(:))) < 1e-12, ...
              numel(t3) == numel(t4) && max(abs(y3(:) - y4(:))) < 1e-12);
            """));

    [Fact]
    public void RefineNormControlAndARequestedGridAreObeyed() =>
        Assert.Equal("1 32 17 1", Run("""
            vdp = @(t, y) [y(2); (1 - y(1)^2) * y(2) - y(1)];
            osc = @(t, y) [y(2); -y(1)];
            [t, ~] = ode78(vdp, [0 20], [2; 0]);
            [t1, ~] = ode78(vdp, [0 20], [2; 0], odeset('Refine', 1));
            sol = ode89(vdp, [0 20], [2; 0], odeset('NormControl', 'on'));
            [tg, yg] = ode113(osc, 0:0.25:4, [1; 0]);
            fprintf('%d %d %d %d', numel(t) == 8 * (numel(t1) - 1) + 1, sol.stats.nsteps, numel(tg), abs(yg(5, 1) - cos(1)) < 1e-3);
            """));

    [Fact]
    public void AnOutputFunctionSeesEveryBatchAndMayStopTheRun()
    {
        string text = Run("""
            osc = @(t, y) [y(2); -y(1)];
            [t, ~] = ode45(osc, [0 10], [1; 0], odeset('OutputFcn', @(t, y, flag) ~isempty(t) && t(end) > 2));
            fprintf('%d %d\n', t(end) > 2, t(end) < 10);
            ode23(osc, [0 1], [1; 0], odeset('OutputFcn', @odeprint, 'OutputSel', 1));
            """);
        Assert.StartsWith("1 1", text);
        Assert.Contains("t =", text);
        Assert.Contains("y =", text);
    }

    [Fact]
    public void StatsPrintsTheThreeCounts() =>
        Assert.Equal("10 successful steps\n0 failed attempts\n61 function evaluations\n", Run("""
            [t, y] = ode45(@(t, y) -2 * y, [0 1], 1, odeset('Stats', 'on'));
            """).Replace("\r\n", "\n"));

    [Fact]
    public void TheStatementFormDrawsThroughOdeplotAndAnswersNothing()
    {
        string text = Run("""
            ode45(@(t, y) [y(2); -y(1)], [0 4], [1; 0]);
            n = numel(get(gca, 'Children'));
            fprintf('%d', n);
            """);
        Assert.Equal("2", text);
    }

    [Fact]
    public void TheOutputFunctionsAcceptTheProtocolOnTheirOwn() =>
        Assert.Equal("0 0 0 0 0 0", Run("""
            a = odeplot([0 1], [1; 0], 'init'); b = odeplot([0.5 1], [0.5 0; 0.2 0.1], ''); c = odeplot([], [], 'done');
            d = odephas2([0 1], [1; 0], 'init'); e = odephas2(0.5, [0.5; 0.2], ''); f = odephas2([], [], 'done');
            fprintf('%d %d %d %d %d %d', a, b, c, d, e, f);
            """));

    [Fact]
    public void TheStiffSolversOptionsAreStoredAndLeftAlone() =>
        Assert.Equal("1 on 3 1", Run("""
            o = odeset('JPattern', [1 0; 0 1], 'BDF', 'on', 'MaxOrder', 3, 'Vectorized', 'on', 'MStateDependence', 'strong');
            [t, y] = ode23(@(t, y) -y, [0 1], 1, o);
            fprintf('%d %s %d %d', isequal(odeget(o, 'JPattern'), [1 0; 0 1]), odeget(o, 'BDF'), odeget(o, 'MaxOrder'), numel(t) > 1);
            """));

    [Fact]
    public void TheRefusalsAreMatlabs()
    {
        Assert.Contains("must be different from the first", Refuses("ode45(@(t, y) -y, [0 0], 1);"));
        Assert.Contains("strictly increase or decrease", Refuses("ode23(@(t, y) -y, [0 1 0.5], 1);"));
        Assert.Contains("NonNegative", Refuses("ode113(@(t, y) -y, [0 1], -1, odeset('NonNegative', 1));"));
        Assert.Contains("singular mass matrix", Refuses("ode89(@(t, y) -y, [0 1], 1, odeset('Mass', 2, 'MassSingular', 'yes'));"));
        Assert.Contains("outside the interval", Refuses("deval(ode45(@(t, y) -y, [0 1], 1), 2);"));
        Assert.Contains("cannot be extended", Refuses("odextend(ode45(@(t, y) -y, [0 1], 1), [], -1);"));
        Assert.Contains("length of initial conditions", Refuses("ode78(@(t, y) [1; 2], [0 1], 1);"));
    }

    [Fact]
    public void OdextendWarnsAndAnswersTheSameSolutionWhenNothingIsLeftToDo() =>
        Assert.Equal("1 1", Run("""
            sol = ode45(@(t, y) -y, [0 1], 1);
            same = odextend(sol, [], 0.5);
            fprintf('%d %d', isequal(same.x, sol.x), ~isempty(lastwarn));
            """));
}
