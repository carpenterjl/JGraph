using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// Accuracy checks for <see cref="BesselFunctions"/>. The reference values are the standard tabulated
/// ones; where a closed form exists (half-integer order, the Wronskians) the identity is asserted
/// instead, because it pins the answer at arguments no table covers.
/// </summary>
public class BesselFunctionsTests
{
    private const double Tight = 1e-13;

    [Theory]
    [InlineData(0, 1.0, 0.7651976865579665)]
    [InlineData(1, 1.0, 0.4400505857449335)]
    [InlineData(0, 5.0, -0.1775967713143383)]
    [InlineData(1, 5.0, -0.3275791375914652)]
    [InlineData(2, 5.0, 0.0465651162777522)]
    [InlineData(5, 10.0, -0.2340615281867936)]
    public void J_MatchesTheTabulatedValues(double nu, double x, double expected) =>
        Assert.Equal(expected, BesselFunctions.J(nu, x), Tight);

    [Theory]
    [InlineData(0, 1.0, 0.0882569642156770)]
    [InlineData(1, 1.0, -0.7812128213002887)]
    [InlineData(0, 5.0, -0.3085176252490338)]
    [InlineData(1, 5.0, 0.1478631433912268)]
    public void Y_MatchesTheTabulatedValues(double nu, double x, double expected) =>
        Assert.Equal(expected, BesselFunctions.Y(nu, x), Tight);

    [Theory]
    [InlineData(0, 1.0, 1.2660658777520084)]
    [InlineData(1, 1.0, 0.5651591039924851)]
    [InlineData(0, 5.0, 27.2398718236044)]
    [InlineData(2, 3.0, 2.2452124409298)]
    public void I_MatchesTheTabulatedValues(double nu, double x, double expected) =>
        Assert.Equal(expected, BesselFunctions.I(nu, x), Math.Abs(expected) * 1e-13);

    [Theory]
    [InlineData(0, 1.0, 0.4210244382407083)]
    [InlineData(1, 1.0, 0.6019072301972346)]
    [InlineData(0, 5.0, 0.0036910983340425)]
    // Cross-checked against the integral ∫₀^∞ e^{-x cosh t}cosh(νt) dt, evaluated by the trapezoid
    // rule — the integrand is even in t, so that converges spectrally and settles the last digits.
    [InlineData(2, 3.0, 0.0615104584717420)]
    public void K_MatchesTheTabulatedValues(double nu, double x, double expected) =>
        Assert.Equal(expected, BesselFunctions.K(nu, x), Math.Abs(expected) * 1e-13);

    /// <summary>
    /// At half-integer order every one of these collapses to an elementary function, which is the
    /// sharpest available check: it holds at any argument, not just the ones a table lists.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(1.0)]
    [InlineData(4.5)]
    [InlineData(30.0)]
    public void HalfIntegerOrder_CollapsesToElementaryFunctions(double x)
    {
        double front = Math.Sqrt(2.0 / (Math.PI * x));

        Assert.Equal(front * Math.Sin(x), BesselFunctions.J(0.5, x), Math.Abs(front) * 1e-13);
        Assert.Equal(-front * Math.Cos(x), BesselFunctions.Y(0.5, x), Math.Abs(front) * 1e-13);
        Assert.Equal(front * Math.Sinh(x), BesselFunctions.I(0.5, x), Math.Abs(front * Math.Sinh(x)) * 1e-13);
        Assert.Equal(
            Math.Sqrt(Math.PI / (2 * x)) * Math.Exp(-x),
            BesselFunctions.K(0.5, x),
            Math.Sqrt(Math.PI / (2 * x)) * Math.Exp(-x) * 1e-13);
    }

    /// <summary>
    /// The Wronskians are exact identities of the pairs, so they catch a normalization that is
    /// merely close — the failure mode a series-plus-recurrence implementation actually has.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.7, 3.0)]
    [InlineData(0.25, 12.0)]
    [InlineData(9.0, 4.0)]
    public void TheWronskiansHold(double nu, double x)
    {
        // J·Y' - J'·Y = 2/(πx), taking the derivatives from the recurrence relations.
        double j = BesselFunctions.J(nu, x);
        double j1 = BesselFunctions.J(nu + 1, x);
        double y = BesselFunctions.Y(nu, x);
        double y1 = BesselFunctions.Y(nu + 1, x);
        double jy = j * (nu / x * y - y1) - (nu / x * j - j1) * y;
        Assert.Equal(2.0 / (Math.PI * x), jy, Math.Abs(2.0 / (Math.PI * x)) * 1e-12);

        // I·K' - I'·K = -1/x.
        double i = BesselFunctions.I(nu, x);
        double i1 = BesselFunctions.I(nu + 1, x);
        double k = BesselFunctions.K(nu, x);
        double k1 = BesselFunctions.K(nu + 1, x);
        double ik = i * (nu / x * k - k1) - (nu / x * i + i1) * k;
        Assert.Equal(-1.0 / x, ik, Math.Abs(1.0 / x) * 1e-12);
    }

    [Fact]
    public void TheRecurrenceRelationHolds()
    {
        // J_{ν-1} + J_{ν+1} = 2ν/x · J_ν, which the downward recurrence has to reproduce exactly.
        const double x = 7.25;
        for (double nu = 1; nu <= 12; nu++)
        {
            double expected = 2 * nu / x * BesselFunctions.J(nu, x);
            double actual = BesselFunctions.J(nu - 1, x) + BesselFunctions.J(nu + 1, x);
            Assert.Equal(expected, actual, Math.Abs(expected) * 1e-12 + 1e-15);
        }
    }

    [Fact]
    public void NegativeOrder_ReflectsTheRightWay()
    {
        // Whole order flips sign with parity; fractional order mixes J and Y.
        Assert.Equal(-BesselFunctions.J(1, 2.0), BesselFunctions.J(-1, 2.0), Tight);
        Assert.Equal(BesselFunctions.J(2, 2.0), BesselFunctions.J(-2, 2.0), Tight);
        Assert.Equal(BesselFunctions.K(1.3, 2.0), BesselFunctions.K(-1.3, 2.0), Tight);

        double nu = 0.75;
        double expected = BesselFunctions.J(nu, 3.0) * Math.Cos(Math.PI * nu) - BesselFunctions.Y(nu, 3.0) * Math.Sin(Math.PI * nu);
        Assert.Equal(expected, BesselFunctions.J(-nu, 3.0), Tight);
    }

    /// <summary>
    /// The point of the scaled forms: past x ≈ 709 the plain functions have no representation left,
    /// and the scaled ones are still accurate to full precision.
    /// </summary>
    [Fact]
    public void TheScaledFormsSurviveWhereThePlainOnesDoNot()
    {
        Assert.Equal(0.0, BesselFunctions.K(0, 800));
        Assert.Equal(double.PositiveInfinity, BesselFunctions.I(0, 800));

        // e^x·K_0(x) → √(π/2x)·(1 - 1/8x + 9/128x² - …) for large x.
        double leading = Math.Sqrt(Math.PI / (2 * 800));
        double asymptotic = leading * (1 - 1.0 / (8 * 800) + 9.0 / (128 * 800 * 800));
        Assert.Equal(asymptotic, BesselFunctions.K(0, 800, scaled: true), leading * 1e-9);

        // e^-x·I_0(x) → 1/√(2πx)·(1 + 1/8x + 9/128x² + …).
        double front = 1.0 / Math.Sqrt(2 * Math.PI * 800);
        Assert.Equal(front * (1 + 1.0 / (8 * 800)), BesselFunctions.I(0, 800, scaled: true), front * 1e-6);

        // And they agree with the plain forms where both exist.
        Assert.Equal(BesselFunctions.K(1.5, 3.0) * Math.Exp(3.0), BesselFunctions.K(1.5, 3.0, scaled: true), 1e-13);
        Assert.Equal(BesselFunctions.I(1.5, 3.0) * Math.Exp(-3.0), BesselFunctions.I(1.5, 3.0, scaled: true), 1e-13);
    }

    [Fact]
    public void HankelFunctions_AreTheJyPair()
    {
        var h1 = BesselFunctions.H(0.5, 1, 2.0);
        Assert.Equal(BesselFunctions.J(0.5, 2.0), h1.Real, Tight);
        Assert.Equal(BesselFunctions.Y(0.5, 2.0), h1.Imaginary, Tight);

        var h2 = BesselFunctions.H(0.5, 2, 2.0);
        Assert.Equal(-BesselFunctions.Y(0.5, 2.0), h2.Imaginary, Tight);
    }

    [Theory]
    [InlineData(0, 0.0, 0.3550280538878172)]
    [InlineData(1, 0.0, -0.2588194037928068)]
    [InlineData(2, 0.0, 0.6149266274460007)]
    [InlineData(3, 0.0, 0.4482883573538264)]
    [InlineData(0, 1.0, 0.1352924163128814)]
    [InlineData(1, 1.0, -0.1591474412967932)]
    [InlineData(2, 1.0, 1.2074235949528713)]
    [InlineData(3, 1.0, 0.9324359333927754)]
    [InlineData(0, -1.0, 0.5355608832923521)]
    [InlineData(2, -1.0, 0.1039973894969446)]
    [InlineData(0, 5.0, 1.0834442813607441e-4)]
    [InlineData(2, 5.0, 657.7920441711711)]
    public void Airy_MatchesTheTabulatedValues(int kind, double x, double expected) =>
        Assert.Equal(expected, BesselFunctions.Airy(kind, x), Math.Abs(expected) * 1e-11 + 1e-15);

    [Fact]
    public void Airy_SatisfiesItsOwnDifferentialEquation()
    {
        // Ai'' = x·Ai, checked with a central second difference — an independent statement about
        // both the function and its derivative that no table lookup can accidentally satisfy.
        const double h = 1e-4;
        foreach (double x in new[] { -3.0, -0.5, 0.7, 2.5 })
        {
            double second = (BesselFunctions.Airy(0, x + h) - 2 * BesselFunctions.Airy(0, x) + BesselFunctions.Airy(0, x - h)) / (h * h);
            Assert.Equal(x * BesselFunctions.Airy(0, x), second, 1e-6);

            // And the first difference of Ai has to be Ai'.
            double first = (BesselFunctions.Airy(0, x + h) - BesselFunctions.Airy(0, x - h)) / (2 * h);
            Assert.Equal(BesselFunctions.Airy(1, x), first, 1e-8);
        }
    }

    [Fact]
    public void Airy_ScalesPastTheUnderflowPoint()
    {
        // Ai(200) is about 1e-1090 — gone as a double — while its scaled form is an ordinary number.
        Assert.Equal(0.0, BesselFunctions.Airy(0, 200));

        // Ai(x)·e^ζ → 1/(2√π x^{1/4}) for large x, with ζ = ⅔x^{3/2}.
        double leading = 1.0 / (2 * Math.Sqrt(Math.PI) * Math.Pow(200, 0.25));
        Assert.Equal(leading, BesselFunctions.Airy(0, 200, scaled: true), leading * 1e-3);

        // Bi overflows in the other direction, and its scaled form is 2× the same leading term.
        Assert.Equal(double.PositiveInfinity, BesselFunctions.Airy(2, 200));
        Assert.Equal(2 * leading, BesselFunctions.Airy(2, 200, scaled: true), leading * 1e-3);

        // Where both exist the scaling is exactly the exponential it claims to be.
        double zeta = 2.0 / 3.0 * Math.Pow(3.0, 1.5);
        Assert.Equal(BesselFunctions.Airy(0, 3.0) * Math.Exp(zeta), BesselFunctions.Airy(0, 3.0, scaled: true), 1e-13);
        Assert.Equal(BesselFunctions.Airy(2, 3.0) * Math.Exp(-zeta), BesselFunctions.Airy(2, 3.0, scaled: true), 1e-13);
    }

    [Fact]
    public void ComplexAnswers_AreRefusedRatherThanApproximated()
    {
        // J_n(-x) is real for whole n, so that one is answered.
        Assert.Equal(-BesselFunctions.J(1, 2.0), BesselFunctions.J(1, -2.0), Tight);

        // Everything else at negative argument is genuinely complex.
        Assert.Throws<ArgumentOutOfRangeException>(() => BesselFunctions.J(0.5, -2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BesselFunctions.K(1.0, -2.0));
    }
}
