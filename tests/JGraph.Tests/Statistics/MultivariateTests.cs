using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave E: the multivariate normal and t.
/// </summary>
/// <remarks>
/// A multivariate probability has almost no published values to check against, but it has a great many
/// identities: an orthant of equicorrelated variables is a closed form, a probability over independent
/// variables is a product of one-dimensional ones, and integrating a variable over the whole line has
/// to give the distribution of the rest. Each of those is a check a wrong quadrature fails.
/// </remarks>
public class MultivariateTests
{
    private static double[,] Equicorrelated(int n, double r)
    {
        var matrix = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                matrix[i, j] = i == j ? 1 : r;
            }
        }

        return matrix;
    }

    private static double[] Filled(int n, double value)
    {
        var array = new double[n];
        Array.Fill(array, value);
        return array;
    }

    /// <summary>
    /// The bivariate orthant is ¼ + asin(r)/2π exactly, for every correlation.
    /// </summary>
    [Theory]
    [InlineData(-0.9)]
    [InlineData(-0.5)]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.8)]
    [InlineData(0.99)]
    public void TheBivariateOrthantMatchesItsClosedForm(double r)
    {
        double expected = 0.25 + (Math.Asin(r) / (2 * Math.PI));
        Assert.Equal(expected, Multivariate.BivariateNormalCdf(0, 0, r), 12);
    }

    /// <summary>
    /// The trivariate orthant is ⅛ + (asin r₁₂ + asin r₁₃ + asin r₂₃)/4π — the case that says whether
    /// the transformed quadrature above two dimensions is right, since it is the first dimension the
    /// exact bivariate reduction does not reach.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(-0.4)]
    [InlineData(0.0)]
    [InlineData(0.85)]
    public void TheTrivariateOrthantMatchesItsClosedForm(double r)
    {
        double expected = 0.125 + (3 * Math.Asin(r) / (4 * Math.PI));
        (double probability, double error) = Multivariate.NormalCdf(
            Filled(3, double.NegativeInfinity), Filled(3, 0), Equicorrelated(3, r));

        Assert.Equal(expected, probability, 9);
        Assert.True(error < 1e-8, $"the reported error {error} is larger than the rule's own accuracy.");
    }

    /// <summary>Independent variables multiply, in every dimension the quadrature handles.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void IndependentVariablesMultiply(int n)
    {
        var upper = new double[n];
        double expected = 1;
        for (int i = 0; i < n; i++)
        {
            upper[i] = 0.3 + (0.4 * i);
            expected *= ContinuousDistributions.NormalCdf(upper[i], 0, 1);
        }

        (double probability, _) = Multivariate.NormalCdf(
            Filled(n, double.NegativeInfinity), upper, Equicorrelated(n, 0));

        Assert.Equal(expected, probability, 9);
    }

    /// <summary>
    /// Letting a variable run over the whole line has to leave the distribution of the others, however
    /// strongly they are correlated with it.
    /// </summary>
    [Fact]
    public void IntegratingAVariableOutLeavesTheRest()
    {
        double[,] sigma = Equicorrelated(4, 0.6);
        var reduced = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                reduced[i, j] = sigma[i, j];
            }
        }

        (double whole, _) = Multivariate.NormalCdf(
            Filled(4, double.NegativeInfinity), [0.5, -0.25, 1.0, double.PositiveInfinity], sigma);
        (double part, _) = Multivariate.NormalCdf(
            Filled(3, double.NegativeInfinity), [0.5, -0.25, 1.0], reduced);

        Assert.Equal(part, whole, 9);
    }

    /// <summary>A rectangle is the corners of the distribution function added and subtracted.</summary>
    [Fact]
    public void ARectangleIsTheCornersOfTheDistributionFunction()
    {
        double[,] sigma = { { 2, 0.8 }, { 0.8, 1.5 } };
        (double box, _) = Multivariate.NormalCdf([-1, -0.5], [1, 2], sigma);

        double s1 = Math.Sqrt(2);
        double s2 = Math.Sqrt(1.5);
        double r = 0.8 / (s1 * s2);
        double corners = Multivariate.BivariateNormalCdf(1 / s1, 2 / s2, r)
            - Multivariate.BivariateNormalCdf(-1 / s1, 2 / s2, r)
            - Multivariate.BivariateNormalCdf(1 / s1, -0.5 / s2, r)
            + Multivariate.BivariateNormalCdf(-1 / s1, -0.5 / s2, r);

        Assert.Equal(corners, box, 12);
    }

    /// <summary>
    /// The multivariate normal density with a diagonal covariance is the product of the one-dimensional
    /// ones, and with a correlated one it still integrates back to the distribution function.
    /// </summary>
    [Fact]
    public void TheDensityFactorsWhenTheVariablesAreIndependent()
    {
        double[,] sigma = { { 4, 0 }, { 0, 9 } };
        double expected = ContinuousDistributions.NormalPdf(1, 0, 2)
            * ContinuousDistributions.NormalPdf(-3, 0, 3);

        Assert.Equal(expected, Multivariate.NormalPdf([1, -3], [0, 0], sigma), 15);
    }

    /// <summary>
    /// A published value: the standard bivariate density at the origin with correlation ½ is
    /// 1/(2π√(1−¼)).
    /// </summary>
    [Fact]
    public void TheDensityAtTheOriginIsTheKnownConstant()
    {
        double[,] sigma = { { 1, 0.5 }, { 0.5, 1 } };
        Assert.Equal(1 / (2 * Math.PI * Math.Sqrt(0.75)), Multivariate.NormalPdf([0, 0], [0, 0], sigma), 15);
    }

    /// <summary>The t density collapses to Student's own in one dimension.</summary>
    [Theory]
    [InlineData(1.0, 3.0)]
    [InlineData(-2.5, 7.0)]
    [InlineData(0.0, 12.0)]
    public void TheTDensityMatchesStudentsInOneDimension(double x, double df)
    {
        double[,] one = { { 1.0 } };
        Assert.Equal(ContinuousDistributions.TPdf(x, df), Multivariate.TPdf([x], one, df), 14);
    }

    /// <summary>
    /// The t probability over a variable left free is Student's for the rest — the marginal check, and
    /// the one that catches a scaling variable applied to the wrong side.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ATFreeVariableLeavesStudentsDistribution(int n)
    {
        var upper = new double[n];
        Array.Fill(upper, double.PositiveInfinity);
        upper[0] = 1.4;

        (double probability, _) = Multivariate.TCdf(
            Filled(n, double.NegativeInfinity), upper, Equicorrelated(n, 0.35), 6);

        Assert.Equal(ContinuousDistributions.TCdf(1.4, 6), probability, 8);
    }

    /// <summary>
    /// The bivariate t orthant has the same closed form as the normal's — the scaling variable is
    /// common to both coordinates, so it cannot move a probability about the origin.
    /// </summary>
    [Theory]
    [InlineData(0.5, 4.0)]
    [InlineData(-0.7, 9.0)]
    public void TheBivariateTOrthantMatchesTheNormalOne(double r, double df)
    {
        double[,] correlation = { { 1, r }, { r, 1 } };
        (double probability, _) = Multivariate.TCdf(
            Filled(2, double.NegativeInfinity), Filled(2, 0), correlation, df);

        Assert.Equal(0.25 + (Math.Asin(r) / (2 * Math.PI)), probability, 9);
    }

    /// <summary>A t with a great many degrees of freedom is a normal.</summary>
    [Fact]
    public void ALargeDegreeOfFreedomTIsANormal()
    {
        double[,] correlation = Equicorrelated(3, 0.4);
        (double student, _) = Multivariate.TCdf(
            Filled(3, double.NegativeInfinity), [0.5, 1.0, -0.3], correlation, 20000);
        (double normal, _) = Multivariate.NormalCdf(
            Filled(3, double.NegativeInfinity), [0.5, 1.0, -0.3], correlation);

        Assert.Equal(normal, student, 4);
    }

    /// <summary>
    /// <c>cholcov</c> answers a factor whose transpose times itself is the covariance — a square one
    /// where the matrix is positive definite, and a shorter one where it is singular.
    /// </summary>
    [Fact]
    public void TheCovarianceFactorReproducesTheCovariance()
    {
        double[,] full = { { 4, 1, 0.5 }, { 1, 3, 0.25 }, { 0.5, 0.25, 2 } };
        (double[,] factor, int rank) = Multivariate.CovarianceFactor(full)!.Value;

        Assert.Equal(3, rank);
        AssertProduct(factor, full);

        // A singular covariance: the third variable is the sum of the first two.
        double[,] singular = { { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 2 } };
        (double[,] shortFactor, int shortRank) = Multivariate.CovarianceFactor(singular)!.Value;

        Assert.Equal(2, shortRank);
        AssertProduct(shortFactor, singular);
    }

    /// <summary>A matrix with a negative eigenvalue is no covariance and gets no factor.</summary>
    [Fact]
    public void AMatrixThatIsNotACovarianceHasNoFactor()
    {
        double[,] indefinite = { { 1, 2 }, { 2, 1 } };
        Assert.Null(Multivariate.CovarianceFactor(indefinite));
    }

    /// <summary>Draws have the mean and covariance they were asked for, and repeat under a seed.</summary>
    [Fact]
    public void DrawsCarryTheirMeanAndCovariance()
    {
        double[,] sigma = { { 4, 1.2 }, { 1.2, 1 } };
        double[] mu = [3, -1];
        (double[,] factor, _) = Multivariate.CovarianceFactor(sigma)!.Value;

        const int count = 40000;
        var first = new double[count];
        var second = new double[count];
        var random = new Random(20260808);
        for (int i = 0; i < count; i++)
        {
            double[] draw = Multivariate.NormalSample(random, mu, factor);
            first[i] = draw[0];
            second[i] = draw[1];
        }

        Assert.Equal(3, Mean(first), 1);
        Assert.Equal(-1, Mean(second), 1);
        Assert.Equal(4, Covariance(first, first), 1);
        Assert.Equal(1.2, Covariance(first, second), 1);
        Assert.Equal(1, Covariance(second, second), 1);

        var repeat = new Random(20260808);
        Assert.Equal(
            Multivariate.NormalSample(new Random(20260808), mu, factor),
            Multivariate.NormalSample(repeat, mu, factor));
    }

    /// <summary>A t draw is heavier-tailed than the normal it is built from, and centred like it.</summary>
    [Fact]
    public void TDrawsAreHeavierTailedThanNormalOnes()
    {
        double[,] correlation = { { 1, 0.5 }, { 0.5, 1 } };
        (double[,] factor, _) = Multivariate.CovarianceFactor(correlation)!.Value;

        const int count = 40000;
        var random = new Random(4242);
        int beyond = 0;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            double[] draw = Multivariate.TSample(random, factor, 4);
            values[i] = draw[0];
            if (Math.Abs(draw[0]) > 3)
            {
                beyond++;
            }
        }

        Assert.Equal(0, Mean(values), 1);

        // Student's t on four degrees of freedom puts about 4% of its mass past three; a normal puts
        // 0.27% there, so the two are never confusable at forty thousand draws.
        double fraction = (double)beyond / count;
        Assert.InRange(fraction, 0.03, 0.05);
    }

    /// <summary>Asking for more variables than the quadrature covers is refused rather than guessed.</summary>
    [Fact]
    public void TooManyVariablesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Multivariate.NormalCdf(
            Filled(6, double.NegativeInfinity), Filled(6, 0), Equicorrelated(6, 0.2)));
        Assert.Throws<ArgumentException>(() => Multivariate.TCdf(
            Filled(5, double.NegativeInfinity), Filled(5, 0), Equicorrelated(5, 0.2), 8));
    }

    private static void AssertProduct(double[,] factor, double[,] expected)
    {
        int rank = factor.GetLength(0);
        int n = factor.GetLength(1);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int k = 0; k < rank; k++)
                {
                    sum += factor[k, r] * factor[k, c];
                }

                Assert.Equal(expected[r, c], sum, 10);
            }
        }
    }

    private static double Mean(double[] values)
    {
        double sum = 0;
        foreach (double value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }

    private static double Covariance(double[] left, double[] right)
    {
        double a = Mean(left);
        double b = Mean(right);
        double sum = 0;
        for (int i = 0; i < left.Length; i++)
        {
            sum += (left[i] - a) * (right[i] - b);
        }

        return sum / (left.Length - 1);
    }
}
