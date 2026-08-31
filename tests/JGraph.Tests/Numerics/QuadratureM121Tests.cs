using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The adaptive Gauss–Kronrod quadrature behind <c>integral</c> and <c>quadgk</c> (M121).
/// </summary>
/// <remarks>
/// <para>
/// The first test here is the one that matters most, and it needs no reference at all: a quadrature
/// rule can be checked against its own definition. The fifteen-point Kronrod rule integrates every
/// polynomial up to degree 22 exactly and must fail at 24; the seven-point Gauss rule nested in it
/// is exact to degree 13. A mistyped node or weight breaks that at low degree and unmistakably,
/// which is the guarantee a table copied out of a book does not carry.
/// </para>
/// <para>
/// The rest are integrals with closed forms, graded against the tolerance that was asked for rather
/// than against MATLAB's digits — at a relative tolerance of 1e-6 two correct adaptive integrators
/// need not agree past the sixth figure, and MATLAB's own answer for ∫₀¹ log x is 1.1e-8 away from
/// −1.
/// </para>
/// </remarks>
public class QuadratureM121Tests
{
    private const double Relative = 1e-6;
    private const double Absolute = 1e-10;

    private static double Of(Func<double, double> f, double a, double b,
        double relative = Relative, double absolute = Absolute,
        IReadOnlyList<double>? waypoints = null) =>
        Quadrature.Integrate(
            at => Array.ConvertAll(at, x => f(x)), a, b, relative, absolute, waypoints).Value;

    private static void Within(double expected, double actual, string what, double relative = Relative)
    {
        double allowed = Math.Max(Absolute, relative * Math.Abs(expected));
        Assert.True(
            Math.Abs(actual - expected) <= allowed,
            $"{what}: {actual:R} is {Math.Abs(actual - expected):E2} from {expected:R}, "
            + $"which is more than the {allowed:E2} asked for");
    }

    [Fact]
    public void TheRuleIntegratesExactlyWhatARuleOfItsOrderMust()
    {
        // Both rules are read out of one panel over [-1, 1], by integrating x^k and comparing with
        // the closed form. This is the whole verification of the node and weight tables.
        for (int degree = 0; degree <= 22; degree++)
        {
            int power = degree;
            double exact = power % 2 == 0 ? 2.0 / (power + 1) : 0.0;

            // One panel only: no adaptation, no transform, so what is measured is the rule itself.
            // A tolerance this loose cannot be met by refinement, so it is met by exactness or not
            // at all.
            double got = Of(x => Math.Pow(x, power), -1.0, 1.0, relative: 1e-13, absolute: 1e-13);
            Assert.True(
                Math.Abs(got - exact) <= 1e-12,
                $"degree {power}: {got:R} against the exact {exact:R}");
        }
    }

    [Theory]
    [InlineData(0.0, 1.0, 1.0 / 3.0)]            // a polynomial the rule is exact on
    [InlineData(-1.0, 1.0, 2.0 / 3.0)]
    [InlineData(0.0, 3.0, 9.0)]
    public void APolynomialIsAnsweredExactly(double a, double b, double expected) =>
        Within(expected, Of(x => x * x, a, b), $"x^2 over [{a}, {b}]");

    [Fact]
    public void TheSmoothTextbookIntegralsCome_Out()
    {
        Within(Math.E - 1, Of(Math.Exp, 0, 1), "exp on [0,1]");
        Within(Math.PI / 4, Of(x => 1 / (1 + (x * x)), 0, 1), "1/(1+x^2) on [0,1]");
        Within(2.0 / 3.0, Of(Math.Sqrt, 0, 1), "sqrt on [0,1]");
        Within(0.0, Of(Math.Cos, 0, 2 * Math.PI), "cos over a whole period");
        Within(2.0, Of(Math.Sin, 0, Math.PI), "sin over a half period");
    }

    [Fact]
    public void AnIntegrableSingularityOnALimitIsAnsweredRatherThanDividedBy()
    {
        // The endpoint transform's whole purpose: 1/0 is never formed because the rule's nodes never
        // reach the end of the panel.
        Within(2.0, Of(x => 1 / Math.Sqrt(x), 0, 1), "1/sqrt(x) on [0,1]");
        Within(-1.0, Of(Math.Log, 0, 1), "log on [0,1]");
        Within(Math.PI / 2, Of(x => 1 / Math.Sqrt(1 - (x * x)), 0, 1), "1/sqrt(1-x^2) on [0,1]");
        Within(4.0, Of(x => Math.Log(1 / x) / Math.Sqrt(x), 0, 1), "log(1/x)/sqrt(x) on [0,1]");
    }

    [Fact]
    public void AnInfiniteLimitIsFoldedOntoAFiniteOne()
    {
        Within(Math.Sqrt(Math.PI), Of(x => Math.Exp(-x * x), double.NegativeInfinity, double.PositiveInfinity),
            "the Gaussian over the whole line");
        Within(1.0, Of(x => Math.Exp(-x), 0, double.PositiveInfinity), "exp(-x) on [0, inf)");
        Within(Math.PI / 2, Of(x => 1 / (1 + (x * x)), double.NegativeInfinity, 0), "1/(1+x^2) on (-inf, 0]");
        Within(1.0, Of(x => x * Math.Exp(-x), 0, double.PositiveInfinity), "x exp(-x) on [0, inf)");
        Within(Math.PI, Of(x => 1 / Math.Cosh(x), double.NegativeInfinity, double.PositiveInfinity), "sech over the line");
    }

    [Fact]
    public void ACornerIsFoundWithoutBeingNamed()
    {
        // The case that made the error estimate worth scaling. On the raw Kronrod-minus-Gauss
        // difference this came out 2.5e-6 wrong against a 1e-6 contract, because at a kink both
        // rules are wrong the same way and their difference says the panel is fine.
        Within(6.1609284709235476, Of(x => Math.Abs(Math.Sin(x)), 0, 10), "|sin x| on [0,10]");
        Within(0.29, Of(x => Math.Abs(x - 0.3), 0, 1), "|x-0.3| on [0,1]");
        Within(0.125, Of(x => Math.Max(0, x - 0.5), 0, 1), "a hinge on [0,1]");
    }

    [Fact]
    public void ANamedWaypointIsMadeAPanelBoundary()
    {
        double found = Of(x => Math.Abs(x - 0.3), 0, 1, waypoints: [0.3]);
        Within(0.29, found, "|x-0.3| with its corner named");

        // A waypoint outside the interval is not a boundary of it, and must not fold the interval.
        Within(0.29, Of(x => Math.Abs(x - 0.3), 0, 1, waypoints: [-5, 0.3, 17]), "waypoints outside the range");
    }

    [Fact]
    public void ANarrowSpikeIsNotSteppedOver()
    {
        // Fifteen points over [0, 1] can miss a feature this narrow entirely and report a confident
        // zero. It is why the mesh starts at ten panels rather than one.
        Within(Math.Sqrt(Math.PI / 1000), Of(x => Math.Exp(-1000 * (x - 0.5) * (x - 0.5)), 0, 1),
            "a spike of width ~0.03");
        Within(Math.Sqrt(Math.PI / 1e6), Of(x => Math.Exp(-1e6 * (x - 0.5) * (x - 0.5)), 0, 1),
            "a spike of width ~0.001");
    }

    [Fact]
    public void ReversedLimitsNegateAndEqualLimitsVanish()
    {
        Assert.Equal(-Of(x => x * x, 0, 1), Of(x => x * x, 1, 0), 12);
        Assert.Equal(0.0, Of(x => x * x, 1, 1));
        Assert.Equal(0.0, Of(x => 1 / x, 0, 0));
    }

    [Fact]
    public void ATighterToleranceIsActuallyHonoured()
    {
        // Not just accepted: the answer has to get better. sin over a half period is 2 exactly, and
        // the default tolerance leaves room the tight one does not.
        double tight = Of(x => Math.Sin(x), 0, Math.PI, relative: 1e-14, absolute: 1e-14);
        Assert.True(
            Math.Abs(tight - 2.0) < 1e-13,
            $"asked for 1e-14 and got {Math.Abs(tight - 2.0):E2} of error");
    }

    [Fact]
    public void TheErrorBoundIsABoundAndConvergenceIsReported()
    {
        Quadrature.Result easy = Quadrature.Integrate(
            at => Array.ConvertAll(at, Math.Sin), 0, Math.PI, Relative, Absolute);

        Assert.True(easy.Converged);
        Assert.True(
            Math.Abs(easy.Value - 2.0) <= easy.ErrorBound + 1e-15,
            $"the error {Math.Abs(easy.Value - 2.0):E2} is outside the bound {easy.ErrorBound:E2} it claimed");

        // A divergent integral cannot be answered, and must say so rather than quietly returning a
        // number. What that number is, is not the claim; that Converged is false is.
        Quadrature.Result divergent = Quadrature.Integrate(
            at => Array.ConvertAll(at, x => 1 / x), 0, 1, Relative, Absolute);
        Assert.False(divergent.Converged);
    }

    [Fact]
    public void AnOverflowLateInTheSubdivisionDoesNotReplaceAnAnswerAlreadyFound()
    {
        // log(1/x)/sqrt(x) is unbounded at 0 even after the transform, so subdividing towards the
        // end eventually underflows x to zero and produces an infinite panel. The answer must be
        // the one the finite panels gave, not the infinity the five-hundredth split found.
        double found = Of(x => Math.Log(1 / x) / Math.Sqrt(x), 0, 1);
        Assert.True(double.IsFinite(found), $"the answer was {found:R}");
        Within(4.0, found, "log(1/x)/sqrt(x)");
    }

    [Fact]
    public void AnIntegrandThatAnswersTheWrongNumberOfValuesIsRefused()
    {
        ArgumentException raised = Assert.Throws<ArgumentException>(
            () => Quadrature.Integrate(at => new double[at.Length + 1], 0, 1));
        Assert.Contains("point", raised.Message, StringComparison.Ordinal);
    }
}
