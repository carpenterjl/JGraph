using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The special-function kernel, checked against values that are known exactly or published to full
/// double precision. The inverses are checked by round trip as well, which catches a wrong branch
/// that a single reference value might happen to agree with.
/// </summary>
public class SpecialFunctionsTests
{
    private const double Tolerance = 1e-13;

    [Theory]
    [InlineData(1.0, 1.0)]                    // Γ(1) = 0! = 1
    [InlineData(5.0, 24.0)]                   // Γ(5) = 4!
    [InlineData(0.5, 1.7724538509055159)]     // Γ(1/2) = √π
    [InlineData(-0.5, -3.5449077018110318)]   // reflection: -2√π
    public void Gamma_MatchesItsClosedForms(double x, double expected) =>
        Assert.Equal(expected, SpecialFunctions.Gamma(x), 12);

    [Fact]
    public void LogGamma_StaysFiniteWhereGammaOverflows()
    {
        Assert.Equal(359.1342053695754, SpecialFunctions.LogGamma(100), 10);
        Assert.True(double.IsInfinity(SpecialFunctions.Gamma(200)));
        Assert.Equal(857.9336698258574, SpecialFunctions.LogGamma(200), 9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5204998778130465)]
    [InlineData(1.0, 0.8427007929497149)]
    [InlineData(2.0, 0.9953222650189527)]
    [InlineData(-1.0, -0.8427007929497149)]
    public void Erf_MatchesPublishedValues(double x, double expected) =>
        Assert.Equal(expected, SpecialFunctions.Erf(x), Tolerance);

    [Fact]
    public void Erfc_KeepsItsDigitsIntoTheTail()
    {
        Assert.Equal(0.1572992070502851, SpecialFunctions.Erfc(1.0), Tolerance);
        Assert.Equal(2.2090496998585441e-05, SpecialFunctions.Erfc(3.0), 1e-18);

        // erfc(30) is 2.6e-393 — flat zero as a double — which is exactly why erfcx exists.
        Assert.Equal(0.0, SpecialFunctions.Erfc(30.0));
        // The asymptotic series 1/(x√π)·(1 - 1/2x² + 3/4x⁴ - 15/8x⁶) gives this to ten digits.
        Assert.Equal(0.018795888861416747, SpecialFunctions.ErfcScaled(30.0), 1e-15);
        Assert.Equal(0.42758357615580700, SpecialFunctions.ErfcScaled(1.0), Tolerance);
    }

    [Fact]
    public void ErfInverses_UndoTheirFunctions()
    {
        Assert.Equal(0.47693627620446987, SpecialFunctions.ErfInverse(0.5), Tolerance);
        Assert.Equal(0.0, SpecialFunctions.ErfInverse(0.0), Tolerance);

        foreach (double y in new[] { -0.99, -0.5, 0.1, 0.5, 0.9, 0.999999 })
        {
            Assert.Equal(y, SpecialFunctions.Erf(SpecialFunctions.ErfInverse(y)), 1e-12);
        }

        foreach (double y in new[] { 1e-12, 1e-6, 0.01, 0.5, 1.0, 1.9 })
        {
            Assert.Equal(y, SpecialFunctions.Erfc(SpecialFunctions.ErfcInverse(y)), y * 1e-9);
        }
    }

    [Fact]
    public void IncompleteGamma_MatchesTheExponentialItReducesTo()
    {
        // P(1, x) = 1 - e^-x, and P(2, x) = 1 - e^-x(1 + x): the two cases with a closed form.
        Assert.Equal(1.0 - Math.Exp(-1.0), SpecialFunctions.GammaLower(1, 1), Tolerance);
        Assert.Equal(1.0 - (Math.Exp(-3.0) * 4.0), SpecialFunctions.GammaLower(2, 3), Tolerance);
        Assert.Equal(Math.Exp(-3.0) * 4.0, SpecialFunctions.GammaUpper(2, 3), Tolerance);
        Assert.Equal(1.0, SpecialFunctions.GammaLower(2, 3) + SpecialFunctions.GammaUpper(2, 3), Tolerance);
    }

    [Fact]
    public void IncompleteGammaInverse_RoundTrips()
    {
        foreach (double a in new[] { 0.5, 1.0, 2.5, 20.0 })
        {
            foreach (double p in new[] { 0.01, 0.25, 0.5, 0.9, 0.999 })
            {
                Assert.Equal(p, SpecialFunctions.GammaLower(a, SpecialFunctions.GammaInverse(a, p)), 1e-10);
                Assert.Equal(p, SpecialFunctions.GammaUpper(a, SpecialFunctions.GammaInverse(a, p, upper: true)), 1e-10);
            }
        }
    }

    [Fact]
    public void IncompleteBeta_MatchesTheBinomialSumItEquals()
    {
        // For whole a and b, I_x(a, b) is the tail of a binomial: I_½(2,3) = (6 + 4 + 1)/16.
        Assert.Equal(0.6875, SpecialFunctions.BetaRegularized(0.5, 2, 3), Tolerance);
        Assert.Equal(0.5, SpecialFunctions.BetaRegularized(0.5, 1, 1), Tolerance);
        Assert.Equal(1.0 / 12.0, SpecialFunctions.Beta(2, 3), Tolerance);

        foreach (double p in new[] { 0.05, 0.5, 0.95 })
        {
            Assert.Equal(p, SpecialFunctions.BetaRegularized(SpecialFunctions.BetaInverse(p, 2, 5), 2, 5), 1e-12);
        }
    }

    [Fact]
    public void DigammaAndPolygamma_MatchTheirSpecialValues()
    {
        Assert.Equal(-0.57721566490153286, SpecialFunctions.Digamma(1.0), Tolerance);      // -γ
        Assert.Equal(1.0 - 0.57721566490153286, SpecialFunctions.Digamma(2.0), Tolerance); // ψ(n+1) = ψ(n) + 1/n
        Assert.Equal(Math.PI * Math.PI / 6.0, SpecialFunctions.Polygamma(1, 1.0), 1e-12);  // ψ'(1) = ζ(2)
        Assert.Equal(-2.4041138063191885, SpecialFunctions.Polygamma(2, 1.0), 1e-11);      // ψ''(1) = -2ζ(3)
    }
}
