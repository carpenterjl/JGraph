using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M131: <c>pdepe</c> and <c>pdeval</c>, and the legacy expression helpers <c>symvar</c>,
/// <c>vectorize</c>, <c>inline</c>, <c>inlineeval</c> and <c>fcnchk</c>.
/// </summary>
/// <remarks>
/// The numbers here are R2025b's, recorded by the parity fixture <c>m131_pde.m</c>. What the tests
/// watch is the discretisation's corners rather than the answer's accuracy: the singular interpolant
/// a mesh starting on the axis asks for, the algebraic rows a <c>q = 0</c> boundary condition makes,
/// the three-dimensional shape of a system's answer, and <c>symvar</c>'s rules about what is a
/// variable — every one of which is a piece of MATLAB syntax rather than a principle.
/// </remarks>
[Collection("JG facade")]
public class MatlabPdepeM131Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabPdepeM131Tests() => JG.Reset();

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

    /// <summary>The documentation's first example: heat flow with one end held and one end fluxed.</summary>
    private const string Pdex1 =
        "pde = @(x, t, u, DuDx) deal(pi^2, DuDx, 0);\n"
        + "ic = @(x) sin(pi * x);\n"
        + "bc = @(xl, ul, xr, ur, t) deal(ul, 0, pi * exp(-t), 1);\n"
        + "x = linspace(0, 1, 20);\n"
        + "t = linspace(0, 2, 5);\n";

    [Fact]
    public void TheSlabProblemAnswersTheSurfaceMatlabAnswers() =>
        Assert.Equal(
            "5 20|0.1348283171|0.2263522578|",
            Run(Pdex1 + """
                sol = pdepe(0, pde, ic, bc, x, t);
                fprintf('%d %d|', size(sol, 1), size(sol, 2));
                fprintf('%.10g|%.10g|', sol(end, 10), sol(3, 5));
                """));

    /// <summary>A single unknown answers a two-dimensional array, as MATLAB's trailing singleton does.</summary>
    [Fact]
    public void OneUnknownAnswersATwoDimensionalArrayAndASystemAnswersThree() =>
        Assert.Equal(
            "2|3|9 13 2|",
            Run("""
                pde1 = @(x, t, u, DuDx) deal(pi^2, DuDx, 0);
                sol1 = pdepe(0, pde1, @(x) sin(pi * x), ...
                    @(xl, ul, xr, ur, t) deal(ul, 0, ur, 0), linspace(0, 1, 5), linspace(0, 1, 3));
                fprintf('%d|', ndims(sol1));
                x = [0 0.005 0.01 0.05 0.1 0.2 0.5 0.7 0.9 0.95 0.99 0.995 1];
                t = [0 0.005 0.01 0.05 0.1 0.5 1 1.5 2];
                sol = pdepe(0, @pde4, @(x) [1; 0], @bc4, x, t);
                fprintf('%d|', ndims(sol));
                fprintf('%d %d %d|', size(sol, 1), size(sol, 2), size(sol, 3));

                function [c, f, s] = pde4(x, t, u, DuDx)
                c = [1; 1];
                f = [0.024; 0.17] .* DuDx;
                F = exp(5.73 * (u(1) - u(2))) - exp(-11.47 * (u(1) - u(2)));
                s = [-F; F];
                end

                function [pl, ql, pr, qr] = bc4(xl, ul, xr, ur, t)
                pl = [0; ul(2)];
                ql = [1; 0];
                pr = [ur(1) - 1; 0];
                qr = [0; 1];
                end
                """));

    /// <summary>
    /// Spherical symmetry with the axis in the mesh: the singular interpolant is used on every
    /// subinterval, and the left boundary condition is ignored because boundedness sets the flux.
    /// </summary>
    [Fact]
    public void TheSphericalProblemUsesTheSingularInterpolantThroughout() =>
        Assert.Equal(
            "8 18|-2.873375939|1|",
            Run("""
                x = [0 0.1 0.2 0.3 0.4 0.45 0.475 0.5 0.525 0.55 0.6 0.7 0.8 0.9 0.95 0.975 0.99 1];
                t = [0 0.001 0.005 0.01 0.05 0.1 0.5 1];
                sol = pdepe(2, @pde2, @ic2, @bc2, x, t);
                fprintf('%d %d|', size(sol, 1), size(sol, 2));
                fprintf('%.10g|%g|', sol(end, 1), sol(end, end));

                function [c, f, s] = pde2(x, t, u, DuDx)
                c = 1;
                if x <= 0.5
                    f = 5 * DuDx;
                    s = -1000 * exp(u);
                else
                    f = DuDx;
                    s = -exp(u);
                end
                end

                function u0 = ic2(x)
                if x < 1
                    u0 = 0;
                else
                    u0 = 1;
                end
                end

                function [pl, ql, pr, qr] = bc2(xl, ul, xr, ur, t)
                pl = 0;
                ql = 0;
                pr = ur - 1;
                qr = 0;
                end
                """));

    /// <summary>The five-output event form, which stops the run at a terminal zero.</summary>
    [Fact]
    public void AnEventOnTheMeshSolutionStopsTheRunAndReportsWhereItStopped() =>
        Assert.Equal(
            "3 20|3|1|0.6907439451|1|0.5|",
            Run(Pdex1 + """
                opts = odeset('Events', @half);
                [sol, tsol, sole, te, ie] = pdepe(0, pde, ic, bc, x, t, opts);
                fprintf('%d %d|', size(sol, 1), size(sol, 2));
                fprintf('%d|%d|', numel(tsol), numel(te));
                fprintf('%.10g|%d|%.10g|', te(1), ie(1), sole(1, 10));

                function [value, isterminal, direction] = half(m, t, xmesh, umesh)
                value = umesh(10) - 0.5;
                isterminal = 1;
                direction = -1;
                end
                """));

    /// <summary>
    /// <c>pdeval</c> reads the same interpolant the scheme is built on, so its derivative at a mesh
    /// point is the discretisation's flux there and not a difference quotient.
    /// </summary>
    [Fact]
    public void PdevalReadsTheSolutionAndItsDerivativeBetweenMeshPoints() =>
        Assert.Equal(
            "1 5|0|0.09555794354|0.1347895776|-0.001189382152|"
            + "0.4238517857|0.3124847553|-0.001472101115|-0.42531876|",
            Run(Pdex1 + """
                sol = pdepe(0, pde, ic, bc, x, t);
                [uo, du] = pdeval(0, x, sol(end, :), [0 0.25 0.5 0.75 1]);
                fprintf('%d %d|', size(uo, 1), size(uo, 2));
                fprintf('%.10g|', uo(1), uo(2), uo(3), uo(5));
                fprintf('%.10g|', du(1), du(2), du(3), du(5));
                """));

    /// <summary>Every refusal <c>pdepe</c> and <c>pdeval</c> make about their mesh and their span.</summary>
    [Fact]
    public void TheMeshAndTheSpanAreCheckedBeforeAnythingIsIntegrated()
    {
        Assert.Contains("0, 1, or 2", Refuses(Pdex1 + "pdepe(3, pde, ic, bc, x, t);"));
        Assert.Contains("TSPAN", Refuses(Pdex1 + "pdepe(0, pde, ic, bc, x, [0 1]);"));
        Assert.Contains("XMESH", Refuses(Pdex1 + "pdepe(0, pde, ic, bc, [0 1], t);"));
        Assert.Contains("strictly increasing", Refuses(Pdex1 + "pdepe(0, pde, ic, bc, [1 0 2], t);"));
        Assert.Contains("non-negative", Refuses(Pdex1 + "pdepe(1, pde, ic, bc, [-1 0 1], t);"));
        Assert.Contains("interval", Refuses(Pdex1 + "pdeval(0, x, x, 1.5);"));
    }

    /// <summary>The rules that decide what <c>symvar</c> calls a variable.</summary>
    [Fact]
    public void SymvarLeavesOutConstantsFunctionCallsFieldNamesAndQuotedText() =>
        Assert.Equal(
            "beta1,x|x,y||f,theta|a,b|q,str|bar,x|a,b|x,z|A,a,a_1|I,J|",
            Run("""
                names = {'cos(pi*x - beta1)', 'x^2+y', '2', 'sin(2*pi*f + theta)', ...
                    'a*b + f(1) + i + j + pi + eps + Inf + NaN', 'str.fname + 1.e10 + q', ...
                    'foo (x) + bar', 'a''+b', 'x + ''hello y'' + z', '_a + a_1 + A + a', ...
                    'inf+nan+Inf+NaN+eps+I+J'};
                for k = 1:numel(names)
                    fprintf('%s|', strjoin(symvar(names{k})', ','));
                end
                """));

    /// <summary>An empty expression answers an empty cell, which is what <c>inline</c> falls back on.</summary>
    [Fact]
    public void SymvarOfNothingIsAnEmptyCellAndInlineFallsBackToX() =>
        Assert.Equal("0|cell|x|", Run("""
            e = symvar('');
            fprintf('%d|%s|', numel(e), class(e));
            fprintf('%s|', strjoin(argnames(inline('2'))', ','));
            """));

    /// <summary>A dot before every operator, and none doubled when the text already has one.</summary>
    [Fact]
    public void VectorizeAddsOneDotAndNeverTwo() =>
        Assert.Equal("x.^2|a.*b./c.^d|a.*b./c.^d|x.*.*y|0|", Run("""
            forms = {'x^2', 'a*b/c^d', 'a.*b./c.^d', 'x**y'};
            for k = 1:numel(forms)
                fprintf('%s|', vectorize(forms{k}));
            end
            fprintf('%d|', numel(vectorize('')));
            """));

    /// <summary>
    /// An inline remembers the text it was written as, which is what <c>formula</c> and <c>char</c>
    /// answer — printing the parsed expression back would give <c>(x ^ 2) + y</c> instead.
    /// </summary>
    [Fact]
    public void AnInlineCarriesItsFormulaItsArgumentNamesAndItsValue() =>
        Assert.Equal(
            "x^2+y|x,y|x^2+y|3|sin(2*pi*f + theta)|f,theta|x^P1|x,P1|8|x.^2.*y|",
            Run("""
                f = inline('x^2+y', 'x', 'y');
                fprintf('%s|%s|%s|%g|', formula(f), strjoin(argnames(f)', ','), char(f), f(1, 2));
                g = inline('  sin(2*pi*f + theta) ');
                fprintf('%s|%s|', formula(g), strjoin(argnames(g)', ','));
                h = inline('x^P1', 1);
                fprintf('%s|%s|%g|', formula(h), strjoin(argnames(h)', ','), h(2, 3));
                fprintf('%s|', formula(vectorize(inline('x^2*y'))));
                """));

    /// <summary><c>fcnchk</c> on a name, an expression, a handle, and with 'vectorized'.</summary>
    [Fact]
    public void FcnchkAnswersAHandleForANameAndAnInlineForAnExpression() =>
        Assert.Equal(
            "function_handle|0.4794255386|x^2+y|x,y|7|5|x.^2.*ones(size(x))|14|x.*y.*ones(size(x))|[]|",
            Run("""
                q = fcnchk('sin');
                fprintf('%s|%.10g|', class(q), q(0.5));
                q2 = fcnchk('x^2+y');
                fprintf('%s|%s|%g|', formula(q2), strjoin(argnames(q2)', ','), q2(2, 3));
                fprintf('%g|', feval(fcnchk(@(x) x + 1), 4));
                q4 = fcnchk('x^2', 'vectorized');
                fprintf('%s|%g|', formula(q4), sum(q4([1 2 3])));
                fprintf('%s|', formula(fcnchk('x*y', 'x', 'y', 'vectorized')));
                fprintf('%s|', formula(fcnchk('')));
                """));

    /// <summary>The helper <c>@inline/subsref</c> evaluates a formula through.</summary>
    [Fact]
    public void InlineevalBindsTheInputsTheAssignmentStringNames() =>
        Assert.Equal("13|", Run(
            "fprintf('%g|', inlineeval({3, 4}, "
            + "' a = INLINE_INPUTS_{1}; b = INLINE_INPUTS_{2};', 'a^2 + b'));"));
}
