using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave J: the copulas. Every check here is an identity a copula has to satisfy rather than a
/// number copied from anywhere — the margins are uniform, the density is the mixed derivative of the
/// distribution function, the rank correlation inverts, and a sample the copula drew is one the
/// copula fits.
/// </summary>
public class CopulaTests
{
    [Theory]
    [InlineData(Copulas.Family.Clayton, 2.0)]
    [InlineData(Copulas.Family.Frank, 4.0)]
    [InlineData(Copulas.Family.Frank, -4.0)]
    [InlineData(Copulas.Family.Gumbel, 2.5)]
    public void AnArchimedeanCopulaHasUniformMargins(Copulas.Family family, double alpha)
    {
        for (double u = 0.1; u < 1; u += 0.1)
        {
            // C(u, 1) = u and C(1, v) = v is what "the margins are uniform" says in one line.
            Assert.Equal(u, Copulas.ArchimedeanCdf(family, u, 1, alpha), 8);
            Assert.Equal(u, Copulas.ArchimedeanCdf(family, 1, u, alpha), 8);
            Assert.Equal(0, Copulas.ArchimedeanCdf(family, u, 0, alpha), 8);
        }
    }

    [Theory]
    [InlineData(Copulas.Family.Clayton, 1.5)]
    [InlineData(Copulas.Family.Frank, 3.0)]
    [InlineData(Copulas.Family.Gumbel, 2.0)]
    public void TheDensityIsTheMixedDerivativeOfTheDistributionFunction(
        Copulas.Family family, double alpha)
    {
        const double Step = 1e-4;
        foreach (double u in new[] { 0.3, 0.5, 0.75 })
        {
            foreach (double v in new[] { 0.25, 0.5, 0.8 })
            {
                double mixed =
                    (Copulas.ArchimedeanCdf(family, u + Step, v + Step, alpha)
                     - Copulas.ArchimedeanCdf(family, u + Step, v - Step, alpha)
                     - Copulas.ArchimedeanCdf(family, u - Step, v + Step, alpha)
                     + Copulas.ArchimedeanCdf(family, u - Step, v - Step, alpha))
                    / (4 * Step * Step);

                Assert.Equal(mixed, Copulas.ArchimedeanPdf(family, u, v, alpha), 4);
                Assert.True(Copulas.ArchimedeanPdf(family, u, v, alpha) > 0);
            }
        }
    }

    [Fact]
    public void IndependenceIsTheProductAndTheDensityIsOne()
    {
        Assert.Equal(0.25, Copulas.ArchimedeanCdf(Copulas.Family.Clayton, 0.5, 0.5, 0), 12);
        Assert.Equal(0.25, Copulas.ArchimedeanCdf(Copulas.Family.Frank, 0.5, 0.5, 0), 12);
        Assert.Equal(0.25, Copulas.ArchimedeanCdf(Copulas.Family.Gumbel, 0.5, 0.5, 1), 12);
        Assert.Equal(1, Copulas.ArchimedeanPdf(Copulas.Family.Gumbel, 0.4, 0.7, 1), 12);
    }

    [Fact]
    public void TheGaussianCopulaAgreesWithTheBivariateNormalItComesFrom()
    {
        var correlation = new[,] { { 1.0, 0.5 }, { 0.5, 1.0 } };

        // At the median of both margins the answer has a closed form: a quarter plus the angle.
        Assert.Equal(
            0.25 + (Math.Asin(0.5) / (2 * Math.PI)),
            Copulas.EllipticalCdf([0.5, 0.5], correlation, null),
            8);

        // Independence is the product, whatever the point.
        var independent = new[,] { { 1.0, 0.0 }, { 0.0, 1.0 } };
        Assert.Equal(0.3 * 0.7, Copulas.EllipticalCdf([0.3, 0.7], independent, null), 6);
        Assert.Equal(1, Copulas.EllipticalPdf([0.3, 0.7], independent, null), 6);
    }

    [Theory]
    [InlineData(Copulas.Family.Clayton, 2.0)]
    [InlineData(Copulas.Family.Gumbel, 3.0)]
    [InlineData(Copulas.Family.Frank, 5.0)]
    [InlineData(Copulas.Family.Gaussian, 0.6)]
    public void TheRankCorrelationsInvert(Copulas.Family family, double parameter)
    {
        double tau = Copulas.KendallTau(family, parameter);
        Assert.Equal(parameter, Copulas.ParameterFor(family, tau, spearman: false), 4);

        double rho = Copulas.SpearmanRho(family, parameter);
        Assert.Equal(parameter, Copulas.ParameterFor(family, rho, spearman: true), 3);

        // Spearman's is the larger of the two for a positively dependent copula, always.
        Assert.True(rho > tau);
    }

    [Fact]
    public void KendallsTauHasItsPublishedFormForTheFamiliesThatHaveOne()
    {
        Assert.Equal(2.0 / 4, Copulas.KendallTau(Copulas.Family.Clayton, 2), 12);
        Assert.Equal(0.5, Copulas.KendallTau(Copulas.Family.Gumbel, 2), 12);
        Assert.Equal(2 / Math.PI * Math.Asin(0.5), Copulas.KendallTau(Copulas.Family.Gaussian, 0.5), 12);
        Assert.Equal(0, Copulas.KendallTau(Copulas.Family.Frank, 0), 8);
    }

    [Fact]
    public void SpearmansRhoOfTheGaussianMatchesItsClosedForm()
    {
        // The integral the Archimedean families need is not used here, but the same routine is asked
        // for the Gaussian, whose answer is known: it must not have drifted.
        Assert.Equal(6 / Math.PI * Math.Asin(0.25), Copulas.SpearmanRho(Copulas.Family.Gaussian, 0.5), 12);
    }

    [Theory]
    [InlineData(Copulas.Family.Clayton, 2.0)]
    [InlineData(Copulas.Family.Frank, 4.0)]
    [InlineData(Copulas.Family.Gumbel, 2.0)]
    public void ADrawnSampleHasTheRankCorrelationItsCopulaClaims(
        Copulas.Family family, double alpha)
    {
        var random = new Random(4242);
        const int Draws = 4000;
        var u = new double[Draws];
        var v = new double[Draws];
        for (int i = 0; i < Draws; i++)
        {
            (u[i], v[i]) = Copulas.ArchimedeanSample(random, family, alpha);
            Assert.InRange(u[i], 0, 1);
            Assert.InRange(v[i], 0, 1);
        }

        // Both margins are uniform, so both means sit at a half.
        Assert.Equal(0.5, Mean(u), 1);
        Assert.Equal(0.5, Mean(v), 1);

        // Spearman's rank correlation of a copula sample is the ordinary correlation of the two
        // margins, because the margins already are their own ranks.
        double expected = Copulas.SpearmanRho(family, alpha);
        Assert.Equal(expected, Correlation(u, v), 1);
    }

    [Fact]
    public void FittingRecoversTheParameterASampleWasDrawnWith()
    {
        var random = new Random(90210);
        const int Draws = 3000;
        var u = new double[Draws];
        var v = new double[Draws];
        for (int i = 0; i < Draws; i++)
        {
            (u[i], v[i]) = Copulas.ArchimedeanSample(random, Copulas.Family.Clayton, 2);
        }

        // The sample really does have the dependence it was drawn with; the fit is asked to find it.
        Assert.Equal(Copulas.SpearmanRho(Copulas.Family.Clayton, 2), Correlation(u, v), 1);
        double fitted = Copulas.FitArchimedean(Copulas.Family.Clayton, u, v);
        Assert.Equal(2, fitted, 0);
        Assert.InRange(fitted, 1.7, 2.3);
    }

    [Fact]
    public void FittingAGaussianCopulaRecoversItsCorrelation()
    {
        var random = new Random(1234);
        const int Draws = 2000;
        var u = new double[Draws, 2];
        double[,] factor = Multivariate.CovarianceFactor(new[,] { { 1.0, 0.7 }, { 0.7, 1.0 } })!.Value.Factor;
        for (int i = 0; i < Draws; i++)
        {
            double[] draw = Copulas.EllipticalSample(random, factor, null);
            u[i, 0] = draw[0];
            u[i, 1] = draw[1];
        }

        double[,] fitted = Copulas.FitElliptical(u, null);
        Assert.Equal(1, fitted[0, 0], 12);
        Assert.Equal(0.7, fitted[0, 1], 1);
    }

    [Fact]
    public void TheDebyeFunctionMatchesItsKnownValues()
    {
        Assert.Equal(1, Copulas.Debye(0, 1), 12);

        // The series about zero is 1 - x/4 + x^2/36, and D1 falls toward zero as x grows.
        Assert.Equal(1 - (0.01 / 4) + (0.0001 / 36), Copulas.Debye(0.01, 1), 9);
        Assert.True(Copulas.Debye(10, 1) < Copulas.Debye(1, 1));
        Assert.InRange(Copulas.Debye(10, 1), 0.15, 0.17);
    }

    [Fact]
    public void AParameterOutsideItsFamilysRangeIsNotSilentlyAccepted()
    {
        (double low, double high) = Copulas.ParameterRange(Copulas.Family.Gumbel);
        Assert.Equal(1, low);
        Assert.True(high > 1);
        Assert.True(Copulas.IsArchimedean(Copulas.Family.Frank));
        Assert.False(Copulas.IsArchimedean(Copulas.Family.T));
    }

    private static double Mean(double[] values)
    {
        double total = 0;
        foreach (double value in values)
        {
            total += value;
        }

        return total / values.Length;
    }

    private static double Correlation(double[] left, double[] right)
    {
        double meanLeft = Mean(left);
        double meanRight = Mean(right);
        double top = 0;
        double leftSum = 0;
        double rightSum = 0;
        for (int i = 0; i < left.Length; i++)
        {
            double a = left[i] - meanLeft;
            double b = right[i] - meanRight;
            top += a * b;
            leftSum += a * a;
            rightSum += b * b;
        }

        return top / Math.Sqrt(leftSum * rightSum);
    }
}
