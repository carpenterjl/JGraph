using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Quadrature above one dimension (M127): <c>integral2</c>, <c>integral3</c> and <c>quad2d</c>, and
/// the five names that came before them — <c>quad</c>, <c>quadl</c>, <c>quadv</c>, <c>dblquad</c>
/// and <c>triplequad</c>.
/// </summary>
/// <remarks>
/// The values here are R2025b's, recorded by the parity fixture <c>m127_quadrature.m</c>. What this
/// class adds beyond the fixture is the machinery a fixture cannot see: how many times a tiled
/// integration calls its integrand, what shape the arguments arrive in, and which of the two names
/// answers an error bound.
/// </remarks>
[Collection("JG facade")]
public class MatlabQuadratureM127Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabQuadratureM127Tests() => JG.Reset();

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
    public void ADoubleIntegralOverARectangleIsTheProductOfTwoSingleOnes() =>
        Assert.Equal("-9.8696044009 0.2500000000 6.0000000000", Run("""
            fprintf('%.10f %.10f %.10f', ...
                integral2(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi), ...
                integral2(@(x, y) x .* y, 0, 1, 0, 1), ...
                integral2(@(x, y) ones(size(x)), 0, 2, 0, 3));
            """));

    /// <summary>
    /// The boundary-weakening transform is what lets an integrand that is infinite at a corner be
    /// integrated at all: the true value here is pi/4 - 1/2, and neither engine ever forms 1/0.
    /// </summary>
    [Fact]
    public void AnIntegrandInfiniteAtACornerIsStillIntegrated() =>
        Assert.Equal("0.28539817539 0.28539816 0.28539816", Run("""
            fun = @(x, y) 1 ./ (sqrt(x + y) .* (1 + x + y) .^ 2);
            polarfun = @(theta, r) fun(r .* cos(theta), r .* sin(theta)) .* r;
            fprintf('%.11f %.8f %.8f', ...
                integral2(fun, 0, 1, 0, @(x) 1 - x), ...
                integral2(polarfun, 0, pi / 2, 0, @(t) 1 ./ (sin(t) + cos(t))), ...
                pi / 4 - 0.5);
            """));

    /// <summary>
    /// A tile is one call of the integrand over a 14-by-14 array — 196 points for one trip into the
    /// interpreter, which is what makes the method usable here at all. An integrand that answers its
    /// own <c>numel</c> divided by 196 therefore integrates to exactly the area of the region.
    /// </summary>
    [Fact]
    public void TheIntegrandIsAskedAboutAWholeTileAtOnce() =>
        Assert.Equal("1.0000000000", Run("""
            fprintf('%.10f', integral2(@(x, y) 0 * x + numel(x) / 196, 0, 1, 0, 1));
            """));

    /// <summary>
    /// An unbounded region cannot be tiled, so <c>'auto'</c> switches to the iterated method and
    /// <c>'tiled'</c> asked for by name is refused.
    /// </summary>
    [Fact]
    public void AnUnboundedRegionIsIteratedAndCannotBeTiled()
    {
        Assert.Equal("0.7853981634 0.7853981634", Run("""
            g = @(x, y) exp(-x .^ 2 - y .^ 2);
            fprintf('%.10f %.10f', integral2(g, 0, Inf, 0, Inf), pi / 4);
            """));
        Assert.Contains("Unbounded integration region",
            Refuses("integral2(@(x, y) exp(-x - y), 0, Inf, 0, 1, 'Method', 'tiled');"));
    }

    [Fact]
    public void Quad2dAnswersAnErrorBoundBesideItsValue() =>
        Assert.Equal("-9.86960440 1 1", Run("""
            [q, errbnd] = quad2d(@(x, y) y .* sin(x) + x .* cos(y), pi, 2 * pi, 0, pi);
            fprintf('%.8f %d %d', q, errbnd > 0, errbnd < 1e-5);
            """));

    /// <summary>
    /// <c>'Singular'</c> off is <c>quad2d</c>'s alone, and it is the plain product rule: on a
    /// polynomial over a rectangle the two readings agree exactly, which is what says the transform
    /// costs nothing where it is not needed.
    /// </summary>
    [Fact]
    public void TurningTheSingularityTransformOffAnswersTheSameOnASmoothIntegrand() =>
        Assert.Equal("0.250000000000 0.250000000000", Run("""
            fprintf('%.12f %.12f', ...
                quad2d(@(x, y) x .* y, 0, 1, 0, 1), ...
                quad2d(@(x, y) x .* y, 0, 1, 0, 1, 'Singular', false));
            """));

    /// <summary>
    /// The budget is counted in calls of the integrand, not in points, so three of them is two
    /// tiles' worth and the warning names the number that was set.
    /// </summary>
    [Fact]
    public void ReachingTheEvaluationBudgetWarnsAndNamesIt() =>
        Assert.Equal(
            "Reached the maximum number of function evaluations (3). The result fails the global error test."
            + "|Reached the maximum number of function evaluations (4). The result passes the global error test.",
            Run("""
                lastwarn('');
                quad2d(@(x, y) 1 ./ (x + y), 0, 1, 0, 1, 'AbsTol', 1e-4, 'MaxFunEvals', 3);
                first = lastwarn;
                lastwarn('');
                quad2d(@(x, y) 1 ./ (x + y), 0, 1, 0, 1, 'AbsTol', 1e-4, 'MaxFunEvals', 4);
                fprintf('%s|%s', first, lastwarn);
                """));

    /// <summary>
    /// An integrand written with a subscript rather than elementwise answers different values in
    /// two array shapes, which is exactly what the six repeated points are there to notice.
    /// </summary>
    [Fact]
    public void AnIntegrandThatIsNotElementwiseIsNoticed() =>
        Assert.StartsWith("Integrand function outputs did not match", Run("""
            lastwarn('');
            quad2d(@(x, y) x + y(1), 0, 1, 0, 1);
            fprintf('%s', lastwarn);
            """));

    [Fact]
    public void ATripleIntegralOverABoxAndOverTheUnitSphere() =>
        Assert.Equal("2.000000 4.500000 0.779555", Run("""
            fprintf('%.6f %.6f %.6f', ...
                integral3(@(x, y, z) y .* sin(x) + z .* cos(x), 0, pi, 0, 1, -1, 1), ...
                integral3(@(x, y, z) x .* y .* z, 0, 1, 0, 2, 0, 3), ...
                integral3(@(x, y, z) x .* cos(y) + x .^ 2 .* cos(z), -1, 1, ...
                    @(x) -sqrt(1 - x .^ 2), @(x) sqrt(1 - x .^ 2), ...
                    @(x, y) -sqrt(1 - x .^ 2 - y .^ 2), @(x, y) sqrt(1 - x .^ 2 - y .^ 2)));
            """));

    /// <summary>
    /// The volume of a tetrahedron, which is the case where both inner limits are functions and the
    /// region closes to a point.
    /// </summary>
    [Fact]
    public void ASolidWhoseLimitsAreBothFunctionsOfTheOuterVariables() =>
        Assert.Equal("0.16666667 0.16666667", Run("""
            fprintf('%.8f %.8f', ...
                integral3(@(x, y, z) ones(size(x)), 0, 1, 0, @(x) 1 - x, 0, @(x, y) 1 - x - y), 1 / 6);
            """));

    /// <summary>
    /// The evaluation count is the statement <c>quad</c> makes about its own method, so it is pinned
    /// beside the value rather than treated as an implementation detail.
    /// </summary>
    [Fact]
    public void QuadAndQuadlCountTheirOwnEvaluations() =>
        Assert.Equal("-0.460501740 41 -0.460501538 78", Run("""
            f = @(x) 1 ./ (x .^ 3 - 2 * x - 5);
            [q1, n1] = quad(f, 0, 2);
            [q2, n2] = quadl(f, 0, 2);
            fprintf('%.9f %d %.9f %d', q1, n1, q2, n2);
            """));

    /// <summary>
    /// A singularity on a limit is nudged inwards by one ulp of the interval rather than being
    /// allowed to poison the Simpson sum, so an integral that is finite is answered — at the cost of
    /// two hundred evaluations, and to five figures rather than to the six that were asked for.
    /// </summary>
    [Fact]
    public void ASingularityOnALimitIsNudgedInwardsRatherThanRefused() =>
        Assert.Equal("2.0000106|198|", Run("""
            lastwarn('');
            [q, n] = quad(@(x) 1 ./ sqrt(x), 0, 1);
            fprintf('%.7f|%d|%s', q, n, lastwarn);
            """));

    /// <summary>
    /// <c>quadv</c>'s integrand takes a scalar and answers an array, and the one tolerance is held
    /// against the largest component — so its answers are not what ten separate <c>quad</c> calls
    /// would give, which is the note MATLAB's own help puts on the name.
    /// </summary>
    [Fact]
    public void QuadvIntegratesEveryComponentOnOneMesh() =>
        Assert.Equal("1 10 0.693147200 0.095310180 17", Run("""
            [Q, n] = quadv(@(x) 1 ./ ((1:10) + x), 0, 1);
            sz = size(Q);
            fprintf('%d %d %.9f %.9f %d', sz(1), sz(2), Q(1), Q(10), n);
            """));

    /// <summary>
    /// Trailing arguments after <c>trace</c> are handed to the integrand. It is how a parameter is
    /// passed without a closure, and it is how <c>dblquad</c> fixes the outer variable.
    /// </summary>
    [Fact]
    public void TrailingArgumentsReachTheIntegrand() =>
        Assert.Equal("-0.4605017397 -0.4605015384", Run("""
            f = @(x, c) 1 ./ (x .^ 3 - 2 * x - c);
            fprintf('%.10f %.10f', quad(f, 0, 2, [], [], 5), quadl(f, 0, 2, [], [], 5));
            """));

    /// <summary>
    /// <c>dblquad</c> takes the quadrature rule as a handle, and any handle with <c>quad</c>'s
    /// calling sequence will do — so the outer integration is performed by calling it rather than by
    /// reaching for the engine underneath.
    /// </summary>
    [Fact]
    public void DblquadAndTriplequadTakeTheRuleAsAHandle() =>
        Assert.Equal("-9.86960438 -9.86960440 1.99999999 4.50000000", Run("""
            g = @(x, y) y .* sin(x) + x .* cos(y);
            fprintf('%.8f %.8f %.8f %.8f', ...
                dblquad(g, pi, 2 * pi, 0, pi), ...
                dblquad(g, pi, 2 * pi, 0, pi, 1e-8, @quadl), ...
                triplequad(@(x, y, z) y .* sin(x) + z .* cos(x), 0, pi, 0, 1, -1, 1), ...
                triplequad(@(x, y, z) x .* y .* z, 0, 1, 0, 2, 0, 3, 1e-8, @quadl));
            """));

    /// <summary>
    /// A region that is not a rectangle is handled by making the integrand zero outside it, which is
    /// the only way <c>dblquad</c> has and is what its own help says to do.
    /// </summary>
    [Fact]
    public void ANonRectangularRegionIsMaskedRatherThanBounded() =>
        Assert.Equal("2.0944109 2.0943951", Run("""
            fprintf('%.7f %.7f', ...
                dblquad(@(x, y) sqrt(max(1 - (x .^ 2 + y .^ 2), 0)), -1, 1, -1, 1), 2 * pi / 3);
            """));

    /// <summary>The limits and the options each name refuses, with the reason named.</summary>
    [Fact]
    public void TheLimitsAndOptionsEachNameRefuses()
    {
        Assert.Contains("function handle", Refuses("integral2('x+y', 0, 1, 0, 1);"));
        Assert.Contains("scalar", Refuses("integral2(@(x, y) x + y, [0 1], 1, 0, 1);"));
        Assert.Contains("finite", Refuses("quad2d(@(x, y) x + y, 0, Inf, 0, 1);"));
        Assert.Contains("'auto', 'tiled' or 'iterated'",
            Refuses("integral2(@(x, y) x + y, 0, 1, 0, 1, 'Method', 'clever');"));
        Assert.Contains("not a recognized option",
            Refuses("integral2(@(x, y) x + y, 0, 1, 0, 1, 'MaxFunEvals', 10);"));
        Assert.Contains("scalars", Refuses("quad(@(x) x, [0 1], 2);"));
    }
}
