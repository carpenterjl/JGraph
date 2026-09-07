using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The boundary-value and delay solvers (M130): <c>bvp4c</c>, <c>bvp5c</c>, <c>bvpinit</c>,
/// <c>bvpxtend</c>, <c>bvpset</c>, <c>bvpget</c>, <c>dde23</c>, <c>ddesd</c>, <c>ddensd</c>,
/// <c>ddeset</c>, <c>ddeget</c>, and <c>deval</c> on what they answer.
/// </summary>
/// <remarks>
/// The mesh counts here are R2025b's, recorded by the parity fixture <c>m130_bvp_dde.m</c>. A
/// collocation solver's answer is a mesh, and the mesh is decided by a residual estimate and a
/// redistribution rule with several bare constants in it — a factor of a hundred deciding whether
/// an interval gains one point or two, a factor of a half deciding whether three intervals become
/// two. A count that is one off says one of those constants is wrong, where the solution itself
/// would still look right to four figures.
/// </remarks>
[Collection("JG facade")]
public class MatlabBvpDdeM130Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabBvpDdeM130Tests() => JG.Reset();

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

    /// <summary>The documentation's own two-point problem: y'' = -|y| with y(0) = 0 and y(4) = -2.</summary>
    private const string TwoBvp =
        "ode = @(x, y) [y(2); -abs(y(1))];\nbc = @(ya, yb) [ya(1); yb(1) + 2];\n";

    /// <summary>Wille and Baker's delay problem, the first of the doc examples.</summary>
    private const string Ddex1 =
        "dde = @(t, y, Z) [Z(1, 1); Z(1, 1) + Z(2, 2); y(2)];\n";

    [Fact]
    public void Bvp4cChoosesTheMeshMatlabChooses() =>
        Assert.Equal(
            "22 0.00079453 1526 108|10 0.000939715 181 28|",
            Run(TwoBvp + """
                for branch = [1 -1]
                    sol = bvp4c(ode, bc, bvpinit(linspace(0, 4, 5), [branch 0]));
                    s = sol.stats;
                    fprintf('%d %.6g %d %d|', s.nmeshpoints, s.maxres, s.nODEevals, s.nBCevals);
                end
                """));

    [Fact]
    public void Bvp5cAnswersACoarserMeshBecauseItControlsTheErrorItself() =>
        Assert.Equal(
            "12 0.000881481 374 41|6 0.000740436 89 17|",
            Run(TwoBvp + """
                for branch = [1 -1]
                    sol = bvp5c(ode, bc, bvpinit(linspace(0, 4, 5), [branch 0]));
                    s = sol.stats;
                    fprintf('%d %.6g %d %d|', s.nmeshpoints, s.maxerr, s.nODEevals, s.nBCevals);
                end
                """));

    [Fact]
    public void ASolutionStructureCarriesTheFieldsMatlabPutsInIt() =>
        Assert.Equal(
            "bvp4c solver,x,y,yp,stats|bvp5c solver,x,y,idata,stats|ymid,yp|",
            Run(TwoBvp + """
                a = bvp4c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
                b = bvp5c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
                fprintf('%s %s|', a.solver, strjoin(fieldnames(a)', ','));
                fprintf('%s %s|', b.solver, strjoin(fieldnames(b)', ','));
                fprintf('%s|', strjoin(fieldnames(b.idata)', ','));
                """));

    [Fact]
    public void AnUnknownParameterIsFoundBesideTheSolution() =>
        Assert.Equal(
            "37 17.0973|25 17.0966|",
            Run("""
                q = 5;
                ode = @(x, y, lambda) [y(2); -(lambda - 2 * q * cos(2 * x)) * y(1)];
                bc = @(ya, yb, lambda) [ya(2); yb(2); ya(1) - 1];
                guess = @(x) [cos(4 * x); -4 * sin(4 * x)];
                for solver = {'bvp4c', 'bvp5c'}
                    f = str2func(solver{1});
                    sol = f(ode, bc, bvpinit(linspace(0, pi, 10), guess, 15));
                    fprintf('%d %.6g|', sol.stats.nmeshpoints, sol.parameters);
                end
                """));

    [Fact]
    public void ASingularTermIsFoldedIntoTheDerivative() =>
        Assert.Equal(
            "6 1.0000005 0.86602540|5 0.99999852 0.86602540|",
            Run("""
                opts = bvpset('SingularTerm', [0 0; 0 -2]);
                ode = @(x, y) [y(2); -y(1)^5];
                bc = @(ya, yb) [ya(2); yb(1) - sqrt(3) / 2];
                for solver = {'bvp4c', 'bvp5c'}
                    f = str2func(solver{1});
                    sol = f(ode, bc, bvpinit(linspace(0, 1, 5), [sqrt(3) / 2; 0]), opts);
                    fprintf('%d %.8g %.8f|', sol.stats.nmeshpoints, sol.y(1, 1), sol.y(1, end));
                end
                """));

    [Fact]
    public void AMultipointProblemKeepsItsInterfaceAsADoubleMeshPoint() =>
        Assert.Equal(
            "16 1 1.03946|10 1 1.03945|",
            Run("""
                xinit = [0, 0.25, 0.5, 0.75, 1, 1, 1.25, 1.5, 1.75, 2];
                n = 5e-2; lambda = 2; kappa = 5;
                eta = lambda^2 / (n * kappa^2);
                ode = @(x, y, region) [(y(2) - 1) / n; ...
                    (y(1) * y(2) - (region == 1) * x - (region == 2)) / eta];
                bc = @(yl, yr) [yl(1, 1); yr(1, 1) - yl(1, 2); yr(2, 1) - yl(2, 2); yr(2, end) - 1];
                for solver = {'bvp4c', 'bvp5c'}
                    f = str2func(solver{1});
                    sol = f(ode, bc, bvpinit(xinit, [1; 1]));
                    fprintf('%d %d %.6g|', sol.stats.nmeshpoints, sum(diff(sol.x) == 0), 1 / sol.y(1, end));
                end
                """));

    [Fact]
    public void AnAnalyticJacobianAndAVectorizedDerivativeCostFewerCalls() =>
        Assert.Equal(
            "113 25 9|",
            Run("""
                sol = bvpinit([-1 -0.5 0 0.5 1], [1 0]);
                for i = 2:4
                    e = 0.1 / 10^(i - 1);
                    opts = bvpset('FJacobian', @(x, y) [0 1; 0 -x / e], ...
                        'BCJacobian', {[1 0; 0 0], [0 0; 1 0]}, 'Vectorized', 'on');
                    ode = @(x, y) [y(2, :); ...
                        -x / e .* y(2, :) - pi^2 * cos(pi * x) - pi * x / e .* sin(pi * x)];
                    sol = bvp4c(ode, @(ya, yb) [ya(1) + 2; yb(1)], sol, opts);
                end
                s = sol.stats;
                fprintf('%d %d %d|', s.nmeshpoints, s.nODEevals, s.nBCevals);
                """));

    [Fact]
    public void DevalReadsACollocationSolutionOffItsOwnPolynomial() =>
        Assert.Equal(
            "2 4|2.0617114 -1.6557663|2.0496797 -0.26620917|-0.26619314 -2.0496816|",
            Run(TwoBvp + """
                sol = bvp4c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
                z = deval(sol, [0.5 1.5 2.5 3.5]);
                fprintf('%d %d|', size(z, 1), size(z, 2));
                fprintf('%.8g %.8g|', z(1, 2), z(2, 3));
                [v, vp] = deval(sol, 1.7);
                fprintf('%.8g %.8g|%.8g %.8g|', v(1), v(2), vp(1), vp(2));
                """));

    [Fact]
    public void DevalReadsABvp5cSolutionOffItsQuartic() =>
        Assert.Equal(
            "2.0616178 -1.6557993|2.04956699 -0.26629759|",
            Run(TwoBvp + """
                sol = bvp5c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
                z = deval(sol, [0.5 1.5 2.5 3.5]);
                fprintf('%.8g %.8g|', z(1, 2), z(2, 3));
                v = deval(sol, 1.7);
                fprintf('%.8f %.8f|', v(1), v(2));
                """));

    [Fact]
    public void BvpxtendExtrapolatesThreeWays() =>
        Assert.Equal(
            "constant 23 -2.0000000 0.0000000|linear 23 -4.8761188 -2.8761188|"
            + "solution 23 -6.3011279 -6.1583378|",
            Run(TwoBvp + """
                sol = bvp4c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
                for method = {'constant', 'linear', 'solution'}
                    ext = bvpxtend(sol, 5, method{1});
                    fprintf('%s %d %.7f %.7f|', method{1}, numel(ext.x), ext.y(1, end), ext.yp(1, end));
                end
                """));

    [Fact]
    public void BvpsetKeepsEveryFieldAndBvpgetTakesAnAbbreviation() =>
        Assert.Equal(
            "8|AbsTol,RelTol,SingularTerm,FJacobian,BCJacobian,Stats,Nmax,Vectorized|"
            + "0.0001 on 500|1e-06 500|7 3|0.5|",
            Run("""
                o = bvpset('RelTol', 1e-4, 'Stats', 'on', 'Nmax', 500);
                fprintf('%d|%s|', numel(fieldnames(o)), strjoin(fieldnames(o)', ','));
                fprintf('%g %s %d|', bvpget(o, 'RelTol'), bvpget(o, 'Stats'), bvpget(o, 'Nmax'));
                o2 = bvpset(o, 'RelTol', 1e-6);
                fprintf('%g %d|', bvpget(o2, 'RelTol'), bvpget(o2, 'Nmax'));
                fprintf('%g %g|', bvpget(o, 'Vectorized', 7), bvpget([], 'RelTol', 3));
                fprintf('%g|', bvpget(bvpset('rel', 0.5), 'RelTol'));
                """));

    [Fact]
    public void Dde23TracksTheDiscontinuitiesAndTakesTheStepsMatlabTakes() =>
        Assert.Equal(
            "27 26 0 118|10 0 3|19.174914 176.41204 190.33533|",
            Run(Ddex1 + """
                sol = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5]);
                s = sol.stats;
                fprintf('%d %d %d %d|', numel(sol.x), s.nsteps, s.nfailed, s.nfevals);
                fprintf('%d %g %g|', numel(sol.discont), sol.discont(1), sol.discont(end));
                fprintf('%.8g %.8g %.8g|', sol.y(1, end), sol.y(2, end), sol.y(3, end));
                """));

    [Fact]
    public void DdesdSolvesTheSameProblemFromDelayFunctions() =>
        Assert.Equal(
            "ddesd 24 19.173538|24 19.173538|",
            Run(Ddex1 + """
                a = ddesd(dde, @(t, y) [t - 1; t - 0.2], [1; 1; 1], [0, 5]);
                fprintf('%s %d %.8g|', a.solver, a.stats.nsteps, a.y(1, end));
                b = ddesd(dde, [1, 0.2], [1; 1; 1], [0, 5]);
                fprintf('%d %.8g|', b.stats.nsteps, b.y(1, end));
                """));

    [Fact]
    public void DdensdApproximatesADelayedDerivative() =>
        Assert.Equal(
            "ddensd 12 11 1 81 -1.0011612 0|",
            Run("""
                hist = @(t) cos(t);
                dde = @(t, y, ydel, ypdel) 1 + y - 2 * ydel^2 - ypdel;
                sol = ddensd(dde, @(t, y) t / 2, @(t, y) t - pi, hist, [0, pi]);
                s = sol.stats;
                fprintf('%s %d %d %d %d %.8g %d|', sol.solver, numel(sol.x), s.nsteps, ...
                    s.nfailed, s.nfevals, sol.y(1, end), sol.IVP);
                """));

    [Fact]
    public void AnInitialValueNeutralProblemStartsFromAConsistentPair() =>
        Assert.Equal(
            "11 10 65 1.2197105 1|11 10 65 1.0295911 1|",
            Run("""
                d = @(t, y) t / 2;
                dde = @(t, y, ydel, ypdel) 2 * cos(2 * t) * ydel^(2 * cos(t)) ...
                    + log(ypdel) - log(2 * cos(t)) - sin(t);
                for slope = [2, 0.4063757399599599]
                    sol = ddensd(dde, d, d, {1, slope}, [0, 0.1]);
                    s = sol.stats;
                    fprintf('%d %d %d %.8g %d|', numel(sol.x), s.nsteps, s.nfevals, sol.y(1, end), sol.IVP);
                end
                """));

    [Fact]
    public void ADelaySolutionStructureCarriesTheFieldsMatlabPutsInIt() =>
        Assert.Equal(
            "solver,history,discont,x,y,stats,yp|solver,history,x,y,stats,yp|"
            + "nsteps,nfailed,nfevals,tfinal|",
            Run(Ddex1 + """
                a = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5]);
                b = ddesd(dde, @(t, y) [t - 1; t - 0.2], [1; 1; 1], [0, 5]);
                fprintf('%s|%s|', strjoin(fieldnames(a)', ','), strjoin(fieldnames(b)', ','));
                fprintf('%s|', strjoin(fieldnames(a.stats)', ','));
                """));

    [Fact]
    public void DevalReadsADelaySolutionAndTheOptionsAreActedOn() =>
        Assert.Equal(
            "4.6458333 17.311779 16.611246|31 2|763|55 0.1000000|",
            Run(Ddex1 + """
                sol = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5]);
                z = deval(sol, 2.5);
                fprintf('%.8g %.8g %.8g|', z(1), z(2), z(3));
                a = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5], ddeset('InitialY', [2; 1; 1]));
                fprintf('%d %g|', a.stats.nsteps, a.y(1, 1));
                b = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5], ddeset('RelTol', 1e-8, 'AbsTol', 1e-10));
                fprintf('%d|', b.stats.nsteps);
                c = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5], ddeset('MaxStep', 0.1));
                fprintf('%d %.7f|', c.stats.nsteps, max(diff(c.x)));
                """));

    [Fact]
    public void EventsAreLocatedOnADelayProblem() =>
        Assert.Equal(
            "1 2.6299560 1 5.0000000|",
            Run(Ddex1 + """
                function [value, isterminal, direction] = watch(t, y, Z)
                value = y(1) - 5;
                isterminal = 0;
                direction = 0;
                end
                sol = dde23(dde, [1, 0.2], [1; 1; 1], [0, 5], ddeset('Events', @watch));
                fprintf('%d %.7f %d %.7f|', numel(sol.xe), sol.xe(1), sol.ie(1), sol.ye(1, 1));
                """));

    [Fact]
    public void ASolutionContinuesAsTheHistoryOfTheNextRun() =>
        Assert.Equal(
            "33 0 19.17495177 23|",
            Run(Ddex1 + """
                first = dde23(dde, [1, 0.2], [1; 1; 1], [0, 3]);
                second = dde23(dde, [1, 0.2], first, [3, 5]);
                fprintf('%d %g %.8f %d|', numel(second.x), second.x(1), second.y(1, end), ...
                    numel(second.discont));
                """));

    [Fact]
    public void DdesetKeepsEveryFieldAndDdegetTakesAnAbbreviation() =>
        Assert.Equal(
            "12|AbsTol,Events,InitialStep,InitialY,Jumps,MaxStep,NormControl,OutputFcn,"
            + "OutputSel,Refine,RelTol,Stats|1e-05 6|11 2 0.25|",
            Run("""
                d = ddeset('RelTol', 1e-5, 'Jumps', [1 2 3]);
                fprintf('%d|%s|', numel(fieldnames(d)), strjoin(fieldnames(d)', ','));
                fprintf('%g %g|', ddeget(d, 'RelTol'), sum(ddeget(d, 'Jumps')));
                fprintf('%g %g %g|', ddeget(d, 'MaxStep', 11), ddeget([], 'RelTol', 2), ...
                    ddeget(ddeset('max', 0.25), 'MaxStep'));
                """));

    [Fact]
    public void ADelayThatIsNotPositiveIsRefused() =>
        Assert.Contains("lags", Refuses(Ddex1 + "dde23(dde, [1, 0], [1; 1; 1], [0, 5]);"));

    [Fact]
    public void ASingularTermOffTheOriginIsRefused() =>
        Assert.Contains("[0,b]", Refuses(TwoBvp + """
            bvp4c(ode, bc, bvpinit(linspace(1, 4, 5), [1 0]), bvpset('SingularTerm', [0 0; 0 -2]));
            """));

    [Fact]
    public void APointInsideTheIntervalIsNotAnExtension() =>
        Assert.Contains("outside", Refuses(TwoBvp + """
            sol = bvp4c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
            bvpxtend(sol, 2);
            """));

    [Fact]
    public void DevalRefusesATimeOutsideTheSolution() =>
        Assert.Contains("outside the interval", Refuses(TwoBvp + """
            sol = bvp4c(ode, bc, bvpinit(linspace(0, 4, 5), [1 0]));
            deval(sol, 5);
            """));
}
