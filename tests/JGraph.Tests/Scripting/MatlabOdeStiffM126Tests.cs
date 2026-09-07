using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The stiff ODE family (M126): <c>ode15s</c>, <c>ode23s</c>, <c>ode23t</c> and <c>ode23tb</c>, the
/// fully implicit pair <c>ode15i</c> and <c>decic</c>, and the options only these solvers read.
/// </summary>
/// <remarks>
/// The counts here are R2025b's, recorded by the parity fixture <c>m126_ode_stiff.m</c>. A stiff
/// solver's cost is not its function evaluations but its Jacobians, its factorizations and its
/// solves, so those are pinned beside the step counts: an iteration matrix refreshed one step early
/// costs a decomposition and shows here, where it would be invisible in the answer.
/// </remarks>
[Collection("JG facade")]
public class MatlabOdeStiffM126Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabOdeStiffM126Tests() => JG.Reset();

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

    /// <summary>Van der Pol at mu = 1000, which no explicit solver finishes in reasonable time.</summary>
    private const string Vdp1000 = "vdp = @(t, y) [y(2); 1000 * (1 - y(1)^2) * y(2) - y(1)];\n";

    [Fact]
    public void EveryStiffSolverTakesTheStepsMatlabTakesOnVanDerPol() =>
        Assert.Equal(
            "ode15s 591 225 45 289 1747|ode23s 743 16 743 759 2277|"
            + "ode23t 776 94 36 294 2012|ode23tb 573 93 44 269 3415|",
            Run(Vdp1000 + """
                names = {'ode15s', 'ode23s', 'ode23t', 'ode23tb'};
                for i = 1:4
                    f = str2func(names{i});
                    sol = f(vdp, [0 3000], [2; 0]);
                    s = sol.stats;
                    fprintf('%s %d %d %d %d %d|', sol.solver, s.nsteps, s.nfailed, s.npds, s.ndecomps, s.nsolves);
                end
                """));

    [Fact]
    public void AnAnalyticJacobianCostsNoEvaluationsAndTakesTheSameStepsOnRobertson() =>
        Assert.Equal("ode15s 293 7|ode23s 460 460|ode23t 824 7|ode23tb 275 11|", Run("""
            hb1 = @(t, y) [-0.04 * y(1) + 1e4 * y(2) * y(3); ...
                0.04 * y(1) - 1e4 * y(2) * y(3) - 3e7 * y(2)^2; 3e7 * y(2)^2];
            jac = @(t, y) [-0.04, 1e4 * y(3), 1e4 * y(2); ...
                0.04, -1e4 * y(3) - 6e7 * y(2), -1e4 * y(2); 0, 6e7 * y(2), 0];
            o = odeset('RelTol', 1e-4, 'AbsTol', [1e-8 1e-14 1e-6], 'Jacobian', jac);
            names = {'ode15s', 'ode23s', 'ode23t', 'ode23tb'};
            for i = 1:4
                f = str2func(names{i});
                sol = f(hb1, [0 4e6], [1; 0; 0], o);
                % A Jacobian in closed form is formed as often as ever and costs no evaluations,
                % so the count of them is the count of steps the solver decided to relinearize on.
                fprintf('%s %d %d|', sol.solver, sol.stats.nsteps, sol.stats.npds);
            end
            """));

    [Fact]
    public void ASparsityPatternGroupsTheColumnsAndCutsTheEvaluations() =>
        Assert.Equal("84 249 2|84 177 2|[40 6 85] 3", Run(Brusselator + """
            full = ode15s(@bruss, [0 10], y0);
            patt = ode15s(@bruss, [0 10], y0, odeset('JPattern', S));
            fprintf('%d %d %d|', full.stats.nsteps, full.stats.nfevals, full.stats.npds);
            fprintf('%d %d %d|', patt.stats.nsteps, patt.stats.nfevals, patt.stats.npds);
            fprintf('%s %d', mat2str(size(patt.idata.dif3d)), patt.idata.kvec(end));
            """));

    [Fact]
    public void ASingularMassMatrixMovesTheGuessOntoTheConstraintBeforeTheFirstStep() =>
        Assert.Equal("275 7 1 ok|356 8 1 ok|", Run(Hb1Dae + """
            M = [1 0 0; 0 1 0; 0 0 0];
            o = odeset('Mass', M, 'RelTol', 1e-4, 'AbsTol', [1e-6 1e-10 1e-6]);
            names = {'ode15s', 'ode23t'};
            for i = 1:2
                f = str2func(names{i});
                sol = f(@hb1dae, [0 4e6], [1; 0; 1e-3], o);
                % The guess violated the conservation law by 1e-3; the start is on it to the
                % accuracy the search was asked for.
                started = abs(sum(sol.y(:, 1)) - 1) < 1e-6;
                fprintf('%d %d %d %s|', sol.stats.nsteps, sol.stats.npds, started, 'ok');
            end
            """));

    [Fact]
    public void AConstantSingularMassMatrixThatIsNotDiagonalIsDecomposedInstead() =>
        Assert.Equal("449 114 1573 62 1200", Run(Amplifier + """
            sol = ode23t(@amp, [0 0.05], u0, odeset('Mass', M));
            s = sol.stats;
            fprintf('%d %d %d %d %d', s.nsteps, s.nfailed, s.nfevals, s.npds, s.nsolves);
            """));

    [Fact]
    public void DecicMovesOnlyTheComponentsItIsAllowedTo() =>
        Assert.Equal("1 0 1 -0.04 0.04 0 1", Run(ImplicitRobertson + """
            M = [1 0 0; 0 1 0; 0 0 0];
            o = odeset('RelTol', 1e-4, 'AbsTol', [1e-6 1e-10 1e-6], 'Jacobian', {[], M});
            [y0, yp0, r] = decic(@ihb1, 0, [1; 0; 1e-3], [1 1 0], [0; 0; 0], [], o);
            fprintf('%g %g %g %g %g %g %d', y0(1), y0(2), abs(y0(3)) < 1e-12, ...
                yp0(1), yp0(2), yp0(3), r < 1e-12);
            """));

    [Fact]
    public void Ode15iTakesTheStepsMatlabTakesWhileTheArithmeticStillAgrees() =>
        Assert.Equal("158 271 65 ode15i 1", Run(ImplicitRobertson + """
            M = [1 0 0; 0 1 0; 0 0 0];
            o = odeset('RelTol', 1e-4, 'AbsTol', [1e-6 1e-10 1e-6], 'Jacobian', {[], M});
            [y0, yp0] = decic(@ihb1, 0, [1; 0; 1e-3], [1 1 0], [0; 0; 0], [], o);
            sol = ode15i(@ihb1, [0 100], y0, yp0, o);
            s = sol.stats;
            fprintf('%d %d %d %s %d', s.nsteps, s.nfevals, s.ndecomps, sol.solver, ...
                abs(sol.y(2, end) - 6.1529930130314361e-06) < 1e-14);
            """));

    [Fact]
    public void OdextendContinuesAFullyImplicitSolutionFromTheSlopeItEndedAt() =>
        Assert.Equal("1 1 1", Run("""
            f = @(t, y, yp) yp + 3 * y - 1;
            o = odeset('RelTol', 1e-8, 'AbsTol', 1e-10);
            sol = ode15i(f, [0 1], 1, -2, o);
            ext = odextend(sol, f, 2);
            exact = @(t) 1/3 + (2/3) * exp(-3 * t);
            fprintf('%d %d %d', numel(ext.x) > numel(sol.x), ...
                abs(ext.y(end) - exact(2)) < 1e-7, abs(deval(ext, 1.5) - exact(1.5)) < 1e-7);
            """));

    [Fact]
    public void EveryStiffSolverAnswersInTheThreeShapesAndCarriesItsOwnInterpolationData() =>
        Assert.Equal("ode15s kvec,dif3d ok|ode23s k1,k2 ok|ode23t z,znew ok|ode23tb t2,y2 ok|", Run("""
            osc = @(t, y) [y(2); -101 * y(1) - 102 * y(2)];
            names = {'ode15s', 'ode23s', 'ode23t', 'ode23tb'};
            for i = 1:4
                f = str2func(names{i});
                sol = f(osc, [0 4], [1; 0]);
                [t, y] = f(osc, [0 4], [1; 0]);
                [t2, y2, te, ye, ie] = f(osc, [0 4], [1; 0], odeset('Events', @zeroed));
                names2 = fieldnames(sol.idata)';
                shapes = isstruct(sol) && numel(t) == numel(sol.x) && size(y, 2) == 2;
                shapes = shapes && numel(te) == numel(ie) && size(ye, 2) == 2;
                keep = names2(~strcmp(names2, 'idxNonNegative'));
                fprintf('%s %s %s|', sol.solver, strjoin(keep, ','), 'ok');
            end

            function [v, isterm, dir] = zeroed(t, y)
            v = y(1) - 0.2;
            isterm = 0;
            dir = 0;
            end
            """));

    [Fact]
    public void TheOrderCapAndTheFamilySwitchAreActedOn() =>
        Assert.Equal("107 2 4|92 4", Run(Brusselator + """
            capped = ode15s(@bruss, [0 10], y0, odeset('MaxOrder', 2));
            fprintf('%d %d %d|', capped.stats.nsteps, max(capped.idata.kvec), size(capped.idata.dif3d, 2));
            bdf = ode15s(@bruss, [0 10], y0, odeset('BDF', 'on'));
            fprintf('%d %d', bdf.stats.nsteps, max(bdf.idata.kvec));
            """));

    [Fact]
    public void AConstantJacobianIsFormedOnce() =>
        Assert.Equal("1", Run(Vdp1000 + """
            sol = ode15s(vdp, [0 100], [2; 0], odeset('JConstant', 'on'));
            fprintf('%d', sol.stats.npds);
            """));

    [Fact]
    public void ANonNegativityConstraintHoldsTheDecayAtZero() =>
        Assert.Equal("ode15s 62 1|ode23t 77 1|ode23tb 56 1|", Run("""
            decay = @(t, y) -abs(y);
            names = {'ode15s', 'ode23t', 'ode23tb'};
            for i = 1:3
                f = str2func(names{i});
                sol = f(decay, [0 40], 1, odeset('NonNegative', 1));
                fprintf('%s %d %d|', sol.solver, sol.stats.nsteps, min(sol.y) >= 0);
            end
            """));

    [Fact]
    public void AVectorizedDerivativeChangesTheCallsAndNotTheAnswer() =>
        Assert.Equal("84 177 84 177 1", Run(Brusselator + BrusselatorVectorized + """
            plain = ode15s(@bruss, [0 10], y0, odeset('JPattern', S));
            fast = ode15s(@brussv, [0 10], y0, odeset('JPattern', S, 'Vectorized', 'on'));
            fprintf('%d %d %d %d %d', plain.stats.nsteps, plain.stats.nfevals, ...
                fast.stats.nsteps, fast.stats.nfevals, ...
                abs(fast.y(1, end) - plain.y(1, end)) < 1e-12);
            """));

    [Fact]
    public void AnImplicitSolverPrintsSixStatisticsWhereAnExplicitOnePrintsThree()
    {
        string stiff = Run(Vdp1000 + "[t, y] = ode15s(vdp, [0 100], [2; 0], odeset('Stats', 'on'));");
        string explicitly = Run(Vdp1000 + "[t, y] = ode45(@(t, y) -y, [0 1], 1, odeset('Stats', 'on'));");
        Assert.Contains("partial derivatives", stiff);
        Assert.Contains("LU decompositions", stiff);
        Assert.Contains("solutions of linear systems", stiff);
        Assert.DoesNotContain("partial derivatives", explicitly);
    }

    [Fact]
    public void Ode23sRefusesWhatItCannotSolve()
    {
        Assert.Contains("non-constant mass matrix", Refuses(Vdp1000 + """
            ode23s(vdp, [0 1], [2; 0], odeset('Mass', @(t) eye(2), 'MStateDependence', 'none'));
            """));
        Assert.Contains("singular mass matrix", Refuses(Vdp1000 + """
            ode23s(vdp, [0 1], [2; 0], odeset('Mass', eye(2), 'MassSingular', 'yes'));
            """));
        Assert.Contains("singular mass matrix", Refuses(Vdp1000 + """
            ode23tb(vdp, [0 1], [2; 0], odeset('Mass', eye(2), 'MassSingular', 'yes'));
            """));
        Assert.Contains("nonzero", Refuses(Vdp1000 + """
            ode15s(vdp, [0 1], [2; 0], odeset('Mass', zeros(2)));
            """));
    }

    [Fact]
    public void ANonNegativityConstraintIsDeclinedBesideAMassMatrix() =>
        Assert.Equal("44 1", Run(Vdp1000 + """
            sol = ode15s(vdp, [0 100], [2; 0], odeset('Mass', [2 1; 1 3], 'NonNegative', 1));
            fprintf('%d %d', sol.stats.nsteps, contains(lastwarn, 'NonNegative'));
            """));

    private const string Brusselator = """
        N = 20;
        y0 = zeros(2 * N, 1);
        for i = 1:N
            y0(2 * i - 1) = 1 + sin((2 * pi / (N + 1)) * i);
            y0(2 * i) = 3;
        end
        S = zeros(2 * N, 2 * N);
        for i = 1:2 * N
            for j = max(1, i - 2):min(2 * N, i + 2)
                S(i, j) = 1;
            end
        end
        for i = 1:2:2 * N
            if i - 1 >= 1
                S(i, i - 1) = 0;
            end
        end
        for i = 2:2:2 * N
            if i + 1 <= 2 * N
                S(i, i + 1) = 0;
            end
        end
        function dydt = bruss(t, y)
        N = 20;
        c = 0.02 * (N + 1)^2;
        dydt = zeros(2 * N, 1);
        i = 1;
        dydt(i) = 1 + y(i + 1) * y(i)^2 - 4 * y(i) + c * (1 - 2 * y(i) + y(i + 2));
        dydt(i + 1) = 3 * y(i) - y(i + 1) * y(i)^2 + c * (3 - 2 * y(i + 1) + y(i + 3));
        for i = 3:2:2 * N - 3
            dydt(i) = 1 + y(i + 1) * y(i)^2 - 4 * y(i) + c * (y(i - 2) - 2 * y(i) + y(i + 2));
            dydt(i + 1) = 3 * y(i) - y(i + 1) * y(i)^2 + c * (y(i - 1) - 2 * y(i + 1) + y(i + 3));
        end
        i = 2 * N - 1;
        dydt(i) = 1 + y(i + 1) * y(i)^2 - 4 * y(i) + c * (y(i - 2) - 2 * y(i) + 1);
        dydt(i + 1) = 3 * y(i) - y(i + 1) * y(i)^2 + c * (y(i - 1) - 2 * y(i + 1) + 3);
        end

        """;

    private const string BrusselatorVectorized = """
        function dydt = brussv(t, y)
        N = 20;
        c = 0.02 * (N + 1)^2;
        dydt = zeros(2 * N, size(y, 2));
        i = 1;
        dydt(i, :) = 1 + y(i + 1, :) .* y(i, :).^2 - 4 * y(i, :) + c * (1 - 2 * y(i, :) + y(i + 2, :));
        dydt(i + 1, :) = 3 * y(i, :) - y(i + 1, :) .* y(i, :).^2 + c * (3 - 2 * y(i + 1, :) + y(i + 3, :));
        i = 3:2:2 * N - 3;
        dydt(i, :) = 1 + y(i + 1, :) .* y(i, :).^2 - 4 * y(i, :) + c * (y(i - 2, :) - 2 * y(i, :) + y(i + 2, :));
        dydt(i + 1, :) = 3 * y(i, :) - y(i + 1, :) .* y(i, :).^2 + c * (y(i - 1, :) - 2 * y(i + 1, :) + y(i + 3, :));
        i = 2 * N - 1;
        dydt(i, :) = 1 + y(i + 1, :) .* y(i, :).^2 - 4 * y(i, :) + c * (y(i - 2, :) - 2 * y(i, :) + 1);
        dydt(i + 1, :) = 3 * y(i, :) - y(i + 1, :) .* y(i, :).^2 + c * (y(i - 1, :) - 2 * y(i + 1, :) + 3);
        end

        """;

    private const string Hb1Dae = """
        function out = hb1dae(t, y)
        out = [-0.04 * y(1) + 1e4 * y(2) * y(3); ...
            0.04 * y(1) - 1e4 * y(2) * y(3) - 3e7 * y(2)^2; ...
            y(1) + y(2) + y(3) - 1];
        end

        """;

    private const string ImplicitRobertson = """
        function res = ihb1(t, y, yp)
        res = [yp(1) + 0.04 * y(1) - 1e4 * y(2) * y(3); ...
            yp(2) - 0.04 * y(1) + 1e4 * y(2) * y(3) + 3e7 * y(2)^2; ...
            y(1) + y(2) + y(3) - 1];
        end

        """;

    private const string Amplifier = """
        M = zeros(5, 5);
        c = 1e-6 * (1:3);
        M(1, 1) = -c(1); M(1, 2) = c(1); M(2, 1) = c(1); M(2, 2) = -c(1);
        M(3, 3) = -c(2); M(4, 4) = -c(3); M(4, 5) = c(3); M(5, 4) = c(3); M(5, 5) = -c(3);
        u0 = zeros(5, 1);
        u0(2) = 3; u0(3) = 3; u0(4) = 6.1; u0(5) = 0.1;
        function dudt = amp(t, u)
        Ub = 6; R0 = 1000; R = 9000; alpha = 0.99; beta = 1e-6; Uf = 0.026;
        Ue = 0.4 * sin(200 * pi * t);
        f23 = beta * (exp((u(2) - u(3)) / Uf) - 1);
        dudt = [-(Ue - u(1)) / R0; ...
            -(Ub / R - u(2) * 2 / R - (1 - alpha) * f23); ...
            -(f23 - u(3) / R); ...
            -((Ub - u(4)) / R - alpha * f23); ...
            u(5) / R];
        end

        """;
}
