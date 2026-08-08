using JGraph.Statistics.Distributions;
using JGraph.Statistics.Optimize;
using JGraph.Statistics.Regression;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// The regression kernels of M53 wave G. Where a closed form exists the answer is written out
/// longhand; where one does not, the fit is pinned by an identity a wrong implementation would break —
/// a penalized fit with no penalty is least squares, a partial least squares fit with every component
/// is least squares, a two-category multinomial fit is a logistic regression, and a multivariate fit
/// whose responses share a design is each response fitted on its own.
/// </summary>
public class RegressionTests
{
    private static readonly double[] SimpleX = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly double[] SimpleY = [2.1, 3.9, 6.2, 7.8, 10.1, 12.2, 13.8, 16.1];

    private static double[,] Column(params double[] values)
    {
        var matrix = new double[values.Length, 1];
        for (int i = 0; i < values.Length; i++)
        {
            matrix[i, 0] = values[i];
        }

        return matrix;
    }

    private static double[,] Intercept(params double[] values) =>
        LeastSquares.WithIntercept(Column(values));

    // --- Least squares ------------------------------------------------------------------------------

    [Fact]
    public void LeastSquares_SimpleRegression_MatchesTheClosedForm()
    {
        LeastSquares.Fit fit = LeastSquares.Solve(Intercept(SimpleX), SimpleY);

        double meanX = SimpleX.Average(), meanY = SimpleY.Average();
        double sxy = 0, sxx = 0;
        for (int i = 0; i < SimpleX.Length; i++)
        {
            sxy += (SimpleX[i] - meanX) * (SimpleY[i] - meanY);
            sxx += (SimpleX[i] - meanX) * (SimpleX[i] - meanX);
        }

        Assert.Equal(sxy / sxx, fit.Coefficients[1], 10);
        Assert.Equal(meanY - (sxy / sxx * meanX), fit.Coefficients[0], 10);
        Assert.Equal(2, fit.Rank);
        Assert.Equal(6, fit.Df);
    }

    [Fact]
    public void LeastSquares_ResidualsAreOrthogonalToEveryColumn()
    {
        double[,] design = { { 1, 1, 4 }, { 1, 2, 1 }, { 1, 3, 9 }, { 1, 4, 2 }, { 1, 5, 6 } };
        double[] y = [3, 1, 8, 4, 7];
        LeastSquares.Fit fit = LeastSquares.Solve(design, y);

        for (int c = 0; c < 3; c++)
        {
            double dot = 0;
            for (int r = 0; r < 5; r++)
            {
                dot += design[r, c] * fit.Residuals[r];
            }

            Assert.Equal(0, dot, 9);
        }
    }

    /// <summary>
    /// A repeated column adds no information. The rank must notice, and — because the fitted values
    /// depend only on the space the columns span — nothing about the fit itself may change.
    /// </summary>
    [Fact]
    public void LeastSquares_RepeatedColumn_LosesRankButNotTheFit()
    {
        double[,] plain = { { 1, 1 }, { 1, 2 }, { 1, 3 }, { 1, 4 } };
        double[,] doubled = { { 1, 1, 1 }, { 1, 2, 2 }, { 1, 3, 3 }, { 1, 4, 4 } };
        double[] y = [1, 3, 2, 5];

        LeastSquares.Fit first = LeastSquares.Solve(plain, y);
        LeastSquares.Fit second = LeastSquares.Solve(doubled, y);

        Assert.Equal(2, first.Rank);
        Assert.Equal(2, second.Rank);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(first.Fitted[i], second.Fitted[i], 9);
            Assert.Equal(first.Leverage[i], second.Leverage[i], 9);
        }
    }

    [Fact]
    public void LeastSquares_LeverageSumsToTheRank()
    {
        double[,] design = { { 1, 1 }, { 1, 2 }, { 1, 3 }, { 1, 7 }, { 1, 9 } };
        LeastSquares.Fit fit = LeastSquares.Solve(design, [1, 2, 3, 4, 5]);
        Assert.Equal(2, fit.Leverage.Sum(), 9);
    }

    [Fact]
    public void LeastSquares_Weighting_MatchesFittingTheRepeatedObservation()
    {
        // An observation of weight three should count exactly as three copies of itself.
        double[,] weighted = { { 1, 1 }, { 1, 2 }, { 1, 3 } };
        double[,] repeated = { { 1, 1 }, { 1, 2 }, { 1, 2 }, { 1, 2 }, { 1, 3 } };
        double[] shortY = [2, 5, 7];
        double[] longY = [2, 5, 5, 5, 7];

        double[] a = LeastSquares.Solve(weighted, shortY, [1, 3, 1]).Coefficients;
        double[] b = LeastSquares.Solve(repeated, longY).Coefficients;
        Assert.Equal(b[0], a[0], 9);
        Assert.Equal(b[1], a[1], 9);
    }

    // --- Design matrices ----------------------------------------------------------------------------

    [Fact]
    public void X2fx_Quadratic_HasTheDocumentedTermsInOrder()
    {
        double[,] predictors = { { 2, 3 }, { 5, 7 } };
        double[,] design = DesignMatrix.Expand(predictors, ModelShape.Quadratic);

        // constant, x1, x2, x1·x2, x1², x2².
        Assert.Equal(6, design.GetLength(1));
        double[] expected = [1, 2, 3, 6, 4, 9];
        for (int c = 0; c < 6; c++)
        {
            Assert.Equal(expected[c], design[0, c], 12);
        }
    }

    [Theory]
    [InlineData(ModelShape.Linear, 4)]
    [InlineData(ModelShape.Interaction, 7)]
    [InlineData(ModelShape.PureQuadratic, 7)]
    [InlineData(ModelShape.Quadratic, 10)]
    public void X2fx_TermCount_FollowsTheShape(ModelShape shape, int expected) =>
        Assert.Equal(expected, DesignMatrix.Terms(shape, 3).Count);

    [Fact]
    public void X2fx_ExplicitExponents_AreTakenLiterally()
    {
        double[,] predictors = { { 2, 3 } };
        List<int[]> terms = [[0, 0], [3, 0], [1, 2]];
        double[,] design = DesignMatrix.Expand(predictors, terms);
        Assert.Equal(1, design[0, 0], 12);
        Assert.Equal(8, design[0, 1], 12);
        Assert.Equal(18, design[0, 2], 12);
    }

    [Fact]
    public void Dummyvar_EveryRowNamesOneLevelOfEachGroupingColumn()
    {
        double[,] groups = { { 1, 2 }, { 3, 1 }, { 2, 2 } };
        double[,] indicators = DesignMatrix.Indicators(groups);

        Assert.Equal(5, indicators.GetLength(1)); // three levels then two
        for (int r = 0; r < 3; r++)
        {
            double total = 0;
            for (int c = 0; c < 5; c++)
            {
                total += indicators[r, c];
            }

            Assert.Equal(2, total, 12);
        }

        Assert.Equal(1, indicators[1, 2], 12);
        Assert.Equal(1, indicators[1, 3], 12);
    }

    [Fact]
    public void Dummyvar_RefusesSomethingThatIsNotAGroupNumber() =>
        Assert.Throws<ArgumentException>(() => DesignMatrix.Indicators(new double[,] { { 1.5 } }));

    // --- regress ------------------------------------------------------------------------------------

    [Fact]
    public void Regress_ExactLine_LeavesNothingOverAndExplainsEverything()
    {
        double[] x = [1, 2, 3, 4, 5];
        var y = new double[5];
        for (int i = 0; i < 5; i++)
        {
            y[i] = 1 + (2 * x[i]);
        }

        LinearRegression.Regression fit = LinearRegression.Regress(y, Intercept(x), 0.05);
        Assert.Equal(1, fit.Coefficients[0], 9);
        Assert.Equal(2, fit.Coefficients[1], 9);
        Assert.Equal(1, fit.RSquare, 9);
        Assert.All(fit.Residuals, r => Assert.Equal(0, r, 9));
    }

    [Fact]
    public void Regress_TheIntervalIsTheCoefficientPlusOrMinusTAndItsError()
    {
        LinearRegression.Regression fit = LinearRegression.Regress(SimpleY, Intercept(SimpleX), 0.05);
        double critical = ContinuousDistributions.TInv(0.975, 6);
        double half = (fit.Upper[1] - fit.Lower[1]) / 2;
        double error = half / critical;

        // The slope over its own standard error is the statistic that decides the slope's probability.
        double t = fit.Coefficients[1] / error;
        Assert.Equal(fit.Coefficients[1], (fit.Lower[1] + fit.Upper[1]) / 2, 9);
        Assert.True(t > 20, $"a nearly exact line should have a huge statistic, and this one is {t}.");
    }

    [Fact]
    public void Regress_ALowerLevelWidensEveryInterval()
    {
        LinearRegression.Regression loose = LinearRegression.Regress(SimpleY, Intercept(SimpleX), 0.10);
        LinearRegression.Regression tight = LinearRegression.Regress(SimpleY, Intercept(SimpleX), 0.01);
        Assert.True(tight.Upper[1] - tight.Lower[1] > loose.Upper[1] - loose.Lower[1]);
    }

    /// <summary>
    /// The residual intervals exist to find outliers, so the one belonging to a point that was moved
    /// far off the line must be the one that no longer contains zero.
    /// </summary>
    [Fact]
    public void Regress_ResidualIntervalsSingleOutTheMovedPoint()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var y = new double[10];
        for (int i = 0; i < 10; i++)
        {
            y[i] = 3 + (0.5 * x[i]);
        }

        y[6] += 4;

        LinearRegression.Regression fit = LinearRegression.Regress(y, Intercept(x), 0.05);
        for (int i = 0; i < 10; i++)
        {
            bool clear = fit.ResidualLower[i] > 0 || fit.ResidualUpper[i] < 0;
            Assert.True(clear == (i == 6), $"observation {i + 1} was judged wrongly.");
        }
    }

    [Fact]
    public void Regress_WithoutAnIntercept_MeasuresVariationAboutZero()
    {
        double[] x = [1, 2, 3, 4];
        double[] y = [2, 4, 6, 8];
        LinearRegression.Regression fit = LinearRegression.Regress(y, Column(x), 0.05);
        Assert.Equal(2, fit.Coefficients[0], 9);
        Assert.Equal(1, fit.RSquare, 9);
    }

    // --- regstats -----------------------------------------------------------------------------------

    [Fact]
    public void Regstats_DeletedCoefficientsMatchActuallyRefittingWithoutTheObservation()
    {
        double[,] design = { { 1, 1 }, { 1, 2 }, { 1, 4 }, { 1, 6 }, { 1, 9 }, { 1, 11 } };
        double[] y = [2, 5, 6, 11, 13, 18];
        LinearRegression.Diagnostics stats = LinearRegression.Describe(y, design);

        for (int drop = 0; drop < 6; drop++)
        {
            var reduced = new double[5, 2];
            var kept = new double[5];
            int row = 0;
            for (int i = 0; i < 6; i++)
            {
                if (i == drop)
                {
                    continue;
                }

                reduced[row, 0] = design[i, 0];
                reduced[row, 1] = design[i, 1];
                kept[row] = y[i];
                row++;
            }

            double[] refitted = LeastSquares.Solve(reduced, kept).Coefficients;
            Assert.Equal(refitted[0], stats.DeletedCoefficients[drop, 0], 8);
            Assert.Equal(refitted[1], stats.DeletedCoefficients[drop, 1], 8);
        }
    }

    [Fact]
    public void Regstats_TheHatMatrixIsAProjection()
    {
        double[,] design = { { 1, 1 }, { 1, 3 }, { 1, 5 }, { 1, 8 } };
        LinearRegression.Diagnostics stats = LinearRegression.Describe([1, 2, 4, 7], design);

        for (int a = 0; a < 4; a++)
        {
            double squared = 0;
            for (int b = 0; b < 4; b++)
            {
                squared += stats.HatMatrix[a, b] * stats.HatMatrix[b, a];
                Assert.Equal(stats.HatMatrix[a, b], stats.HatMatrix[b, a], 9);
            }

            Assert.Equal(stats.HatMatrix[a, a], squared, 9);
            Assert.Equal(stats.HatMatrix[a, a], stats.Fit.Leverage[a], 9);
        }
    }

    [Fact]
    public void Regstats_TheAdjustedFractionIsBelowThePlainOne()
    {
        double[,] design = { { 1, 1, 2 }, { 1, 2, 1 }, { 1, 3, 5 }, { 1, 4, 3 }, { 1, 5, 8 }, { 1, 6, 4 } };
        LinearRegression.Diagnostics stats = LinearRegression.Describe([2, 3, 7, 6, 12, 9], design);
        Assert.True(stats.AdjustedRSquare < stats.RSquare);
        Assert.True(stats.RSquare is > 0 and <= 1);
    }

    [Fact]
    public void Leverage_OfAnIdenticalDesign_IsTheSameEverywhere()
    {
        double[] hat = LinearRegression.Leverage(new double[,] { { 1 }, { 1 }, { 1 }, { 1 } });
        Assert.All(hat, h => Assert.Equal(0.25, h, 9));
    }

    // --- ridge --------------------------------------------------------------------------------------

    [Fact]
    public void Ridge_WithNoPenalty_IsOrdinaryLeastSquares()
    {
        double[,] predictors = { { 1, 4 }, { 2, 1 }, { 3, 9 }, { 4, 2 }, { 5, 7 } };
        double[] y = [3, 1, 8, 4, 9];

        double[,] restored = LinearRegression.Ridge(y, predictors, [0], false);
        double[] ordinary = LeastSquares.Solve(LeastSquares.WithIntercept(predictors), y).Coefficients;
        for (int j = 0; j < 3; j++)
        {
            Assert.Equal(ordinary[j], restored[j, 0], 8);
        }
    }

    [Fact]
    public void Ridge_ShrinksTowardsZeroAsThePenaltyGrows()
    {
        double[,] predictors = { { 1, 4 }, { 2, 1 }, { 3, 9 }, { 4, 2 }, { 5, 7 } };
        double[] y = [3, 1, 8, 4, 9];
        double[,] path = LinearRegression.Ridge(y, predictors, [0, 1, 10, 1000], true);

        for (int j = 0; j < 2; j++)
        {
            for (int p = 1; p < 4; p++)
            {
                Assert.True(Math.Abs(path[j, p]) <= Math.Abs(path[j, p - 1]) + 1e-9);
            }

            Assert.True(Math.Abs(path[j, 3]) < 0.05, $"a penalty of 1000 left {path[j, 3]}.");
        }
    }

    [Fact]
    public void Ridge_ScaledAndRestored_PredictTheSameThing()
    {
        double[,] predictors = { { 1, 4 }, { 2, 1 }, { 3, 9 }, { 4, 2 }, { 5, 7 } };
        double[] y = [3, 1, 8, 4, 9];
        double[,] scaled = LinearRegression.Ridge(y, predictors, [2.5], true);
        double[,] restored = LinearRegression.Ridge(y, predictors, [2.5], false);

        for (int r = 0; r < 5; r++)
        {
            double fromRestored = restored[0, 0];
            for (int j = 0; j < 2; j++)
            {
                fromRestored += restored[j + 1, 0] * predictors[r, j];
            }

            // The standardized fit predicts the centred response from the standardized predictors.
            double fromScaled = y.Average();
            for (int j = 0; j < 2; j++)
            {
                double mean = 0, squares = 0;
                for (int i = 0; i < 5; i++)
                {
                    mean += predictors[i, j] / 5;
                }

                for (int i = 0; i < 5; i++)
                {
                    squares += (predictors[i, j] - mean) * (predictors[i, j] - mean);
                }

                fromScaled += scaled[j, 0] * (predictors[r, j] - mean) / Math.Sqrt(squares / 4);
            }

            Assert.Equal(fromRestored, fromScaled, 8);
        }
    }

    // --- invpred and polyconf -----------------------------------------------------------------------

    [Fact]
    public void Invpred_AnswersTheXWhoseFittedValueIsY0()
    {
        (double x0, double lower, double upper) =
            LinearRegression.InversePrediction(SimpleX, SimpleY, 10, 0.05, true);

        LeastSquares.Fit fit = LeastSquares.Solve(Intercept(SimpleX), SimpleY);
        Assert.Equal(10, fit.Coefficients[0] + (fit.Coefficients[1] * x0), 9);
        Assert.True(lower < x0 && x0 < upper);
    }

    [Fact]
    public void Invpred_TheObservationIntervalIsWiderThanTheCurveOne()
    {
        (_, double lowA, double upA) = LinearRegression.InversePrediction(SimpleX, SimpleY, 10, 0.05, true);
        (_, double lowB, double upB) = LinearRegression.InversePrediction(SimpleX, SimpleY, 10, 0.05, false);
        Assert.True(upA - lowA > upB - lowB);
    }

    [Fact]
    public void Invpred_RefusesAFlatLine() => Assert.Throws<ArgumentException>(
        () => LinearRegression.InversePrediction([1, 2, 3, 4], [5, 5, 5, 5], 5, 0.05, true));

    [Fact]
    public void Polyconf_TheSimultaneousBandIsWiderThanThePointwiseOne()
    {
        double[,] triangular = { { 2, 1 }, { 0, 3 } };
        double[,] rows = { { 1, 0.5 }, { 1, 2.0 } };

        double[] pointwise = LinearRegression.PolynomialInterval(triangular, 10, 3, rows, 0.05, false, false);
        double[] simultaneous = LinearRegression.PolynomialInterval(triangular, 10, 3, rows, 0.05, false, true);
        double[] observation = LinearRegression.PolynomialInterval(triangular, 10, 3, rows, 0.05, true, false);

        for (int i = 0; i < 2; i++)
        {
            Assert.True(simultaneous[i] > pointwise[i]);
            Assert.True(observation[i] > pointwise[i]);
        }
    }

    // --- robustfit ----------------------------------------------------------------------------------

    [Fact]
    public void Robustfit_WithEqualWeights_IsLeastSquares()
    {
        RobustRegression.RobustFit fit =
            RobustRegression.Fit(Intercept(SimpleX), SimpleY, RobustWeight.Ols, 0);
        double[] ordinary = LeastSquares.Solve(Intercept(SimpleX), SimpleY).Coefficients;
        Assert.Equal(ordinary[0], fit.Coefficients[0], 9);
        Assert.Equal(ordinary[1], fit.Coefficients[1], 9);
    }

    /// <summary>
    /// One point moved a long way off the line drags an ordinary fit with it; a robust one should
    /// barely notice, and should say so by giving that point almost no weight.
    /// </summary>
    [Fact]
    public void Robustfit_IgnoresASinglePointMovedFarOffTheLine()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var clean = new double[10];
        for (int i = 0; i < 10; i++)
        {
            clean[i] = 1 + (2 * x[i]);
        }

        // The point moved is away from the centre; one moved at the centre would tilt nothing and
        // would make this test pass for the wrong reason.
        var spoiled = (double[])clean.Clone();
        spoiled[8] += 30;

        double[] ordinary = LeastSquares.Solve(Intercept(x), spoiled).Coefficients;
        RobustRegression.RobustFit robust =
            RobustRegression.Fit(Intercept(x), spoiled, RobustWeight.Bisquare, 0);

        Assert.True(Math.Abs(ordinary[1] - 2) > 0.3, "an ordinary fit should have been dragged.");
        Assert.Equal(2, robust.Coefficients[1], 3);
        Assert.Equal(1, robust.Coefficients[0], 2);
        Assert.True(robust.Weights[8] < 1e-6, "the moved point should have been given up on.");
    }

    [Theory]
    [InlineData(RobustWeight.Andrews)]
    [InlineData(RobustWeight.Bisquare)]
    [InlineData(RobustWeight.Cauchy)]
    [InlineData(RobustWeight.Fair)]
    [InlineData(RobustWeight.Huber)]
    [InlineData(RobustWeight.Logistic)]
    [InlineData(RobustWeight.Talwar)]
    [InlineData(RobustWeight.Welsch)]
    public void Robustfit_EveryWeightFunctionIsFullAtZeroAndNeverRises(RobustWeight weight)
    {
        Assert.Equal(1, RobustRegression.Weigh(weight, 0), 6);
        double previous = 1;
        for (double r = 0; r <= 5; r += 0.05)
        {
            double value = RobustRegression.Weigh(weight, r);
            Assert.True(value <= previous + 1e-9, $"the weight rose again at {r}.");
            Assert.True(value >= 0);
            Assert.Equal(value, RobustRegression.Weigh(weight, -r), 9);
            previous = value;
        }
    }

    [Fact]
    public void Robustfit_EveryWeightFunctionRecoversTheLine()
    {
        double[] x = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var y = new double[12];
        for (int i = 0; i < 12; i++)
        {
            y[i] = 4 - (0.75 * x[i]);
        }

        y[8] += 25;

        foreach (RobustWeight weight in Enum.GetValues<RobustWeight>())
        {
            if (weight == RobustWeight.Ols)
            {
                continue;
            }

            RobustRegression.RobustFit fit = RobustRegression.Fit(Intercept(x), y, weight, 0);
            Assert.True(Math.Abs(fit.Coefficients[1] + 0.75) < 0.25,
                $"{weight} answered a slope of {fit.Coefficients[1]}.");
        }
    }

    [Fact]
    public void Robustfit_TheTuningConstantsAreTheDocumentedOnes()
    {
        Assert.Equal(4.685, RobustRegression.DefaultTuning(RobustWeight.Bisquare), 12);
        Assert.Equal(1.345, RobustRegression.DefaultTuning(RobustWeight.Huber), 12);
        Assert.Equal(1.339, RobustRegression.DefaultTuning(RobustWeight.Andrews), 12);
        Assert.Equal(2.985, RobustRegression.DefaultTuning(RobustWeight.Welsch), 12);
    }

    // --- glmfit and glmval --------------------------------------------------------------------------

    [Theory]
    [InlineData(GlmLink.Identity, 0.0)]
    [InlineData(GlmLink.Log, 0.0)]
    [InlineData(GlmLink.Logit, 0.0)]
    [InlineData(GlmLink.Probit, 0.0)]
    [InlineData(GlmLink.ComplementaryLogLog, 0.0)]
    [InlineData(GlmLink.LogLog, 0.0)]
    [InlineData(GlmLink.Reciprocal, 0.0)]
    [InlineData(GlmLink.Power, -2.0)]
    public void Glm_EveryLinkInvertsItself(GlmLink link, double power)
    {
        foreach (double mu in new[] { 0.05, 0.2, 0.5, 0.75, 0.95 })
        {
            double eta = GeneralizedLinear.Link(link, power, mu);
            Assert.Equal(mu, GeneralizedLinear.Inverse(link, power, eta), 9);

            // And its derivative agrees with a difference of itself.
            double step = 1e-6;
            double slope = (GeneralizedLinear.Link(link, power, mu + step)
                - GeneralizedLinear.Link(link, power, mu - step)) / (2 * step);
            Assert.Equal(slope, GeneralizedLinear.Derivative(link, power, mu), 4);
        }
    }

    [Fact]
    public void Glmfit_NormalWithTheIdentityLink_IsLeastSquares()
    {
        GeneralizedLinear.GlmFit fit = GeneralizedLinear.Fit(
            Intercept(SimpleX), SimpleY, GlmFamily.Normal, GlmLink.Identity, 0, null, null, null, true);
        double[] ordinary = LeastSquares.Solve(Intercept(SimpleX), SimpleY).Coefficients;

        Assert.Equal(ordinary[0], fit.Coefficients[0], 8);
        Assert.Equal(ordinary[1], fit.Coefficients[1], 8);
        Assert.Equal(LeastSquares.Solve(Intercept(SimpleX), SimpleY).ResidualSumOfSquares, fit.Deviance, 8);
    }

    [Fact]
    public void Glmfit_InterceptOnly_AnswersTheLinkOfTheMean()
    {
        var design = new double[6, 1];
        for (int i = 0; i < 6; i++)
        {
            design[i, 0] = 1;
        }

        double[] counts = [2, 5, 3, 8, 4, 6];
        GeneralizedLinear.GlmFit fit = GeneralizedLinear.Fit(
            design, counts, GlmFamily.Poisson, GlmLink.Log, 0, null, null, null, false);

        Assert.Equal(Math.Log(counts.Average()), fit.Coefficients[0], 8);
        Assert.All(fit.Fitted, mu => Assert.Equal(counts.Average(), mu, 8));
    }

    [Fact]
    public void Glmfit_BinomialLogit_RecoversAKnownLogOdds()
    {
        // Two groups, ten trials each: the log-odds ratio between them is the slope, exactly.
        double[,] design = { { 1, 0 }, { 1, 1 } };
        double[] proportions = [0.2, 0.8];
        double[] trials = [10, 10];

        GeneralizedLinear.GlmFit fit = GeneralizedLinear.Fit(
            design, proportions, GlmFamily.Binomial, GlmLink.Logit, 0, trials, null, null, false);

        Assert.Equal(Math.Log(0.2 / 0.8), fit.Coefficients[0], 6);
        Assert.Equal(Math.Log(0.8 / 0.2) - Math.Log(0.2 / 0.8), fit.Coefficients[1], 6);
        Assert.Equal(0, fit.Deviance, 8);
    }

    [Fact]
    public void Glmfit_TheOffsetShiftsTheLinearPredictorWithoutBeingFitted()
    {
        var design = new double[5, 1];
        double[] counts = [3, 7, 12, 20, 33];
        double[] exposure = new double[5];
        for (int i = 0; i < 5; i++)
        {
            design[i, 0] = 1;
            exposure[i] = Math.Log(i + 1.0);
        }

        GeneralizedLinear.GlmFit fit = GeneralizedLinear.Fit(
            design, counts, GlmFamily.Poisson, GlmLink.Log, 0, null, null, exposure, false);

        // With an exposure offset the fitted rate per unit exposure is constant, so the fitted counts
        // rise in proportion to the exposure.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(Math.Exp(fit.Coefficients[0]) * (i + 1.0), fit.Fitted[i], 8);
        }
    }

    [Fact]
    public void Glmval_ReproducesTheFittedValues()
    {
        double[,] design = { { 1, 0 }, { 1, 1 }, { 1, 2 }, { 1, 3 }, { 1, 4 } };
        double[] counts = [1, 3, 4, 9, 14];
        GeneralizedLinear.GlmFit fit = GeneralizedLinear.Fit(
            design, counts, GlmFamily.Poisson, GlmLink.Log, 0, null, null, null, false);

        (double[] predicted, double[] lower, double[] upper) = GeneralizedLinear.Evaluate(
            fit.Coefficients, design, GlmLink.Log, 0, fit.Covariance, fit.Df, 0.05, false, null);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(fit.Fitted[i], predicted[i], 8);
            Assert.True(lower[i] > 0 && upper[i] > 0);

            // The band is symmetric where it is drawn and bent by the link, so the two halves differ.
            Assert.NotEqual(lower[i], upper[i], 6);
        }
    }

    [Fact]
    public void Glmfit_RefusesAResponseTheFamilyCouldNotHaveProduced()
    {
        var design = new double[2, 1] { { 1 }, { 1 } };
        Assert.Throws<ArgumentException>(() => GeneralizedLinear.Fit(
            design, [0.5, 1.5], GlmFamily.Binomial, GlmLink.Logit, 0, null, null, null, false));
        Assert.Throws<ArgumentException>(() => GeneralizedLinear.Fit(
            design, [1, -1], GlmFamily.Poisson, GlmLink.Log, 0, null, null, null, false));
    }

    // --- lasso --------------------------------------------------------------------------------------

    private static (double[,] X, double[] Y) SparseProblem()
    {
        double[,] x =
        {
            { 1, 5, 2 }, { 2, 3, 9 }, { 3, 8, 4 }, { 4, 1, 7 }, { 5, 6, 1 },
            { 6, 2, 8 }, { 7, 9, 3 }, { 8, 4, 6 }, { 9, 7, 5 }, { 10, 0, 2 },
        };

        var y = new double[10];
        for (int i = 0; i < 10; i++)
        {
            y[i] = 2 + (3 * x[i, 0]);
        }

        return (x, y);
    }

    [Fact]
    public void Lasso_WithNoPenalty_IsLeastSquares()
    {
        (double[,] x, double[] y) = SparseProblem();
        PenalizedRegression.Path path = PenalizedRegression.Fit(
            x, y, 1, [0], default, true, null, 1e-12, 0);

        double[] ordinary = LeastSquares.Solve(LeastSquares.WithIntercept(x), y).Coefficients;
        Assert.Equal(ordinary[0], path.Intercepts[0], 5);
        for (int j = 0; j < 3; j++)
        {
            Assert.Equal(ordinary[j + 1], path.Coefficients[j, 0], 5);
        }
    }

    [Fact]
    public void Lasso_TheLargestPenaltyEmptiesTheModel()
    {
        (double[,] x, double[] y) = SparseProblem();
        PenalizedRegression.Path path = PenalizedRegression.Fit(
            x, y, 1, null, new PenalizedRegression.PathPlan(20, 1e-3), true, null, 1e-8, 0);

        Assert.Equal(20, path.Lambda.Length);
        Assert.Equal(0, path.Df[^1]);
        Assert.True(path.Df[0] > 0, "the smallest penalty should keep something.");
        Assert.Equal(y.Average(), path.Intercepts[^1], 6);

        // Non-increasing in the penalty, which is what makes the path readable as a selection order.
        for (int i = 1; i < path.Lambda.Length; i++)
        {
            Assert.True(path.Lambda[i] > path.Lambda[i - 1]);
            Assert.True(path.Df[i] <= path.Df[i - 1]);
        }
    }

    [Fact]
    public void Lasso_KeepsTheOnePredictorThatMatters()
    {
        (double[,] x, double[] y) = SparseProblem();
        PenalizedRegression.Path path = PenalizedRegression.Fit(
            x, y, 1, null, new PenalizedRegression.PathPlan(50, 1e-3), true, null, 1e-8, 0);

        int single = Array.FindLastIndex(path.Df, d => d == 1);
        Assert.True(single >= 0, "somewhere on the path exactly one predictor should survive.");
        Assert.NotEqual(0, path.Coefficients[0, single]);
        Assert.Equal(0, path.Coefficients[1, single]);
        Assert.Equal(0, path.Coefficients[2, single]);
    }

    [Fact]
    public void Lasso_ARidgeMixingShrinksButNeverDrops()
    {
        (double[,] x, double[] y) = SparseProblem();
        PenalizedRegression.Path path = PenalizedRegression.Fit(
            x, y, 0.001, [50], default, true, null, 1e-10, 0);

        for (int j = 0; j < 3; j++)
        {
            Assert.NotEqual(0, path.Coefficients[j, 0]);
        }
    }

    [Fact]
    public void Lassoglm_PoissonPathEndsAtTheInterceptOnlyModel()
    {
        double[,] x = { { 1, 2 }, { 2, 1 }, { 3, 5 }, { 4, 3 }, { 5, 4 }, { 6, 6 }, { 7, 2 }, { 8, 8 } };
        double[] counts = [1, 2, 4, 6, 9, 14, 20, 31];

        PenalizedRegression.Path path = PenalizedRegression.FitGeneralized(
            x, counts, GlmFamily.Poisson, GlmLink.Log, 0, 1, null,
            new PenalizedRegression.PathPlan(30, 1e-3), true, null, null, 1e-8, 0);

        Assert.Equal(0, path.Df[^1]);
        Assert.Equal(Math.Log(counts.Average()), path.Intercepts[^1], 4);
        Assert.True(path.Criterion[0] < path.Criterion[^1],
            "the least penalized fit should leave the least deviance.");
    }

    [Fact]
    public void Lasso_RefusesAMixingOutsideItsRange() => Assert.Throws<ArgumentException>(
        () => PenalizedRegression.Fit(SparseProblem().X, SparseProblem().Y, 0, [1], default, true, null, 1e-8, 0));

    // --- stepwisefit --------------------------------------------------------------------------------

    [Fact]
    public void Stepwisefit_TakesTheRealPredictorAndLeavesTheNoise()
    {
        double[,] predictors =
        {
            { 1, 7, 2 }, { 2, 3, 9 }, { 3, 8, 4 }, { 4, 1, 7 }, { 5, 6, 1 }, { 6, 2, 8 },
            { 7, 9, 3 }, { 8, 4, 6 }, { 9, 5, 5 }, { 10, 0, 2 }, { 11, 3, 7 }, { 12, 6, 4 },
        };

        var y = new double[12];
        for (int i = 0; i < 12; i++)
        {
            y[i] = 5 + (4 * predictors[i, 0]);
        }

        StepwiseSelection.Selection chosen =
            StepwiseSelection.Fit(predictors, y, 0.05, 0.10, null, null, 0);

        Assert.True(chosen.InModel[0]);
        Assert.False(chosen.InModel[1]);
        Assert.False(chosen.InModel[2]);
        Assert.Equal(4, chosen.Coefficients[0], 6);
        Assert.Equal(5, chosen.Intercept, 6);
        Assert.Equal(-1, chosen.NextTerm);
        Assert.Single(chosen.History);
        Assert.True(chosen.History[0].Added);
    }

    [Fact]
    public void Stepwisefit_AKeptTermIsNeverDropped()
    {
        double[,] predictors = { { 1, 3 }, { 2, 1 }, { 3, 4 }, { 4, 1 }, { 5, 5 }, { 6, 9 } };
        double[] y = [2, 4, 6, 8, 10, 12];

        StepwiseSelection.Selection chosen =
            StepwiseSelection.Fit(predictors, y, 0.05, 0.10, null, [false, true], 0);
        Assert.True(chosen.InModel[1]);
    }

    [Fact]
    public void Stepwisefit_RefusesARemovalRuleThatWouldCycle() => Assert.Throws<ArgumentException>(
        () => StepwiseSelection.Fit(new double[,] { { 1 }, { 2 }, { 3 } }, [1, 2, 3], 0.10, 0.05, null, null, 0));

    // --- plsregress ---------------------------------------------------------------------------------

    [Fact]
    public void Plsregress_WithEveryComponent_IsLeastSquares()
    {
        double[,] x =
        {
            { 1, 5, 2 }, { 2, 3, 9 }, { 3, 8, 4 }, { 4, 1, 7 }, { 5, 6, 1 },
            { 6, 2, 8 }, { 7, 9, 3 }, { 8, 4, 6 }, { 9, 7, 5 }, { 10, 0, 2 },
        };

        var y = new double[10, 1];
        for (int i = 0; i < 10; i++)
        {
            y[i, 0] = 3 + (2 * x[i, 0]) - x[i, 1] + (0.5 * x[i, 2]);
        }

        PartialLeastSquares.PlsFit fit = PartialLeastSquares.Fit(x, y, 3);
        var response = new double[10];
        for (int i = 0; i < 10; i++)
        {
            response[i] = y[i, 0];
        }

        double[] ordinary = LeastSquares.Solve(LeastSquares.WithIntercept(x), response).Coefficients;
        for (int j = 0; j < 4; j++)
        {
            Assert.Equal(ordinary[j], fit.Beta[j, 0], 6);
        }
    }

    [Fact]
    public void Plsregress_TheExplainedFractionsSumToNoMoreThanEverything()
    {
        double[,] x =
        {
            { 1, 5 }, { 2, 3 }, { 3, 8 }, { 4, 1 }, { 5, 6 }, { 6, 2 }, { 7, 9 }, { 8, 4 },
        };

        var y = new double[8, 2];
        for (int i = 0; i < 8; i++)
        {
            y[i, 0] = x[i, 0] + x[i, 1];
            y[i, 1] = x[i, 0] - x[i, 1];
        }

        PartialLeastSquares.PlsFit fit = PartialLeastSquares.Fit(x, y, 2);
        Assert.Equal(1, fit.ExplainedX.Sum(), 8);
        Assert.Equal(1, fit.ExplainedY.Sum(), 8);

        // The error table starts at the raw variance and ends at nothing.
        Assert.Equal(0, fit.YMeanSquaredError[2], 8);
        Assert.True(fit.YMeanSquaredError[0] > fit.YMeanSquaredError[1]);
    }

    [Fact]
    public void Plsregress_RefusesMoreComponentsThanThereAreDirections() =>
        Assert.Throws<ArgumentException>(() => PartialLeastSquares.Fit(
            new double[,] { { 1, 2 }, { 3, 4 }, { 5, 7 } }, new double[,] { { 1 }, { 2 }, { 3 } }, 3));

    // --- mnrfit and mnrval --------------------------------------------------------------------------

    /// <summary>
    /// Two categories make a multinomial fit an ordinary logistic regression, so the two routines must
    /// agree to the last digit that either of them is worth.
    /// </summary>
    [Fact]
    public void Mnrfit_WithTwoCategories_IsALogisticRegression()
    {
        double[,] predictors = { { 1 }, { 2 }, { 3 }, { 4 }, { 5 }, { 6 }, { 7 }, { 8 } };
        double[,] counts =
        {
            { 8, 2 }, { 7, 3 }, { 6, 4 }, { 5, 5 }, { 4, 6 }, { 3, 7 }, { 2, 8 }, { 1, 9 },
        };

        MultinomialRegression.MultinomialFit fit = MultinomialRegression.Fit(
            predictors, counts, MultinomialModel.Nominal, GlmLink.Logit, true);

        var design = new double[8, 2];
        var proportions = new double[8];
        var trials = new double[8];
        for (int i = 0; i < 8; i++)
        {
            design[i, 0] = 1;
            design[i, 1] = predictors[i, 0];
            trials[i] = counts[i, 0] + counts[i, 1];
            proportions[i] = counts[i, 0] / trials[i];
        }

        GeneralizedLinear.GlmFit logistic = GeneralizedLinear.Fit(
            design, proportions, GlmFamily.Binomial, GlmLink.Logit, 0, trials, null, null, false);

        Assert.True(fit.Converged);
        Assert.Equal(logistic.Coefficients[0], fit.Coefficients[0], 5);
        Assert.Equal(logistic.Coefficients[1], fit.Coefficients[1], 5);
        Assert.Equal(logistic.Deviance, fit.Deviance, 5);
    }

    [Fact]
    public void Mnrfit_TheFittedProbabilitiesAlwaysSumToOne()
    {
        double[,] predictors = { { 1 }, { 2 }, { 3 }, { 4 }, { 5 }, { 6 } };
        double[,] counts =
        {
            { 6, 3, 1 }, { 5, 4, 1 }, { 4, 4, 2 }, { 2, 5, 3 }, { 1, 4, 5 }, { 1, 2, 7 },
        };

        foreach (MultinomialModel model in Enum.GetValues<MultinomialModel>())
        {
            bool separate = model == MultinomialModel.Nominal;
            MultinomialRegression.MultinomialFit fit =
                MultinomialRegression.Fit(predictors, counts, model, GlmLink.Logit, separate);

            Assert.True(fit.Deviance >= -1e-8, $"{model} answered a negative deviance.");
            for (int i = 0; i < 6; i++)
            {
                double total = 0;
                for (int j = 0; j < 3; j++)
                {
                    total += fit.Probabilities[i, j];
                }

                Assert.Equal(1, total, 8);
            }
        }
    }

    [Fact]
    public void Mnrfit_TheOrdinalModelKeepsTheCategoriesInOrder()
    {
        double[,] predictors = { { 1 }, { 2 }, { 3 }, { 4 }, { 5 }, { 6 }, { 7 }, { 8 } };
        double[,] counts =
        {
            { 9, 1, 0 }, { 8, 2, 0 }, { 6, 3, 1 }, { 4, 5, 1 },
            { 2, 5, 3 }, { 1, 4, 5 }, { 0, 3, 7 }, { 0, 1, 9 },
        };

        MultinomialRegression.MultinomialFit fit = MultinomialRegression.Fit(
            predictors, counts, MultinomialModel.Ordinal, GlmLink.Logit, false);

        // One slope, two cut points, and the cut points must increase or the middle category would
        // have negative probability.
        Assert.Equal(3, fit.Coefficients.Length);
        Assert.True(fit.Coefficients[1] > fit.Coefficients[0]);

        double[,] cumulative = MultinomialRegression.Cumulative(fit.Probabilities);
        for (int i = 0; i < 8; i++)
        {
            Assert.True(cumulative[i, 1] >= cumulative[i, 0] - 1e-12);
        }
    }

    [Fact]
    public void Mnrval_ReproducesTheFittedProbabilities()
    {
        double[,] predictors = { { 1 }, { 2 }, { 3 }, { 4 }, { 5 } };
        double[,] counts = { { 5, 1 }, { 4, 2 }, { 3, 3 }, { 2, 4 }, { 1, 5 } };
        MultinomialRegression.MultinomialFit fit = MultinomialRegression.Fit(
            predictors, counts, MultinomialModel.Nominal, GlmLink.Logit, true);

        double[,] again = MultinomialRegression.Probabilities(
            predictors, fit.Coefficients, MultinomialModel.Nominal, GlmLink.Logit, true, 2, 1);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(fit.Probabilities[i, 0], again[i, 0], 10);
        }
    }

    [Fact]
    public void Mnrfit_RefusesAProbitNominalModel() => Assert.Throws<ArgumentException>(
        () => MultinomialRegression.Fit(
            new double[,] { { 1 }, { 2 } }, new double[,] { { 1, 1 }, { 2, 0 } },
            MultinomialModel.Nominal, GlmLink.Probit, true));

    // --- mvregress ----------------------------------------------------------------------------------

    [Fact]
    public void Mvregress_WithOneDesignForEveryResponse_IsEachResponseFittedAlone()
    {
        double[,] design = { { 1, 1 }, { 1, 2 }, { 1, 3 }, { 1, 4 }, { 1, 5 }, { 1, 6 } };
        double[,] responses =
        {
            { 2.0, 5.5 }, { 4.1, 5.0 }, { 5.9, 4.4 }, { 8.2, 4.1 }, { 9.8, 3.4 }, { 12.1, 3.0 },
        };

        MultivariateRegression.MultivariateFit fit = MultivariateRegression.Fit(
            MultivariateRegression.Expand(design, 2), responses, 0, 0);

        for (int d = 0; d < 2; d++)
        {
            var single = new double[6];
            for (int i = 0; i < 6; i++)
            {
                single[i] = responses[i, d];
            }

            double[] alone = LeastSquares.Solve(design, single).Coefficients;
            Assert.Equal(alone[0], fit.Coefficients[d * 2], 7);
            Assert.Equal(alone[1], fit.Coefficients[(d * 2) + 1], 7);
        }

        // The error covariance is the residual cross-product over the number of observations.
        double cross = 0;
        for (int i = 0; i < 6; i++)
        {
            cross += fit.Residuals[i, 0] * fit.Residuals[i, 1];
        }

        Assert.Equal(cross / 6, fit.Covariance[0, 1], 9);
    }

    [Fact]
    public void Mvregresslike_AgreesWithTheFitItDescribes()
    {
        double[,] design = { { 1, 1 }, { 1, 2 }, { 1, 4 }, { 1, 7 }, { 1, 9 } };
        double[,] responses = { { 1, 2 }, { 3, 1 }, { 6, 4 }, { 9, 3 }, { 13, 6 } };
        double[][,] expanded = MultivariateRegression.Expand(design, 2);

        MultivariateRegression.MultivariateFit fit =
            MultivariateRegression.Fit(expanded, responses, 0, 0);
        double again = MultivariateRegression.LogLikelihood(
            expanded, responses, fit.Coefficients, fit.Covariance);

        Assert.Equal(fit.LogLikelihood, again, 9);

        // Nothing else can do better, which is what maximum likelihood means.
        var nudged = (double[])fit.Coefficients.Clone();
        nudged[0] += 0.5;
        Assert.True(
            MultivariateRegression.LogLikelihood(expanded, responses, nudged, fit.Covariance)
            < fit.LogLikelihood);
    }

    [Fact]
    public void Mvregress_TheCovarianceOfTheCovarianceIsSymmetric()
    {
        double[,] sigma = { { 4, 1 }, { 1, 9 } };
        double[,] covariance = MultivariateRegression.CovarianceOfCovariance(sigma, 50);

        Assert.Equal(3, covariance.GetLength(0)); // σ₁₁, σ₂₁, σ₂₂
        Assert.Equal(2 * 16 / 50.0, covariance[0, 0], 12);
        for (int a = 0; a < 3; a++)
        {
            for (int b = 0; b < 3; b++)
            {
                Assert.Equal(covariance[a, b], covariance[b, a], 12);
            }
        }
    }

    // --- coxphfit -----------------------------------------------------------------------------------

    [Fact]
    public void Coxphfit_HigherRiskFailsFirstAndTheCoefficientSaysSo()
    {
        // Ten subjects, the five with the marker failing before any of the five without it.
        var x = new double[10, 1];
        var times = new double[10];
        for (int i = 0; i < 5; i++)
        {
            x[i, 0] = 1;
            times[i] = i + 1;
            x[i + 5, 0] = 0;
            times[i + 5] = i + 6;
        }

        ProportionalHazards.HazardFit fit = ProportionalHazards.Fit(
            x, times, null, null, null, TieHandling.Breslow, null);

        Assert.True(fit.Coefficients[0] > 1, $"the marker should raise the hazard, and it gave {fit.Coefficients[0]}.");
        Assert.Equal(10, fit.Times.Length);

        // Every marked subject failing before every unmarked one separates the two groups completely,
        // and the likelihood then has no maximum at all — the estimate runs off, and its standard
        // error runs off with it. A large coefficient with no precision behind it is the right answer
        // here, and pretending otherwise would be the bug.
        Assert.True(fit.StandardErrors[0] > 1);
        Assert.True(fit.P[0] > 0.05);

        // A cumulative hazard only ever climbs.
        for (int i = 1; i < fit.CumulativeHazard.Length; i++)
        {
            Assert.True(fit.CumulativeHazard[i] >= fit.CumulativeHazard[i - 1]);
        }
    }

    [Fact]
    public void Coxphfit_APredictorUnrelatedToTheOrderOfFailureIsNotSignificant()
    {
        var x = new double[8, 1];
        var times = new double[8];
        for (int i = 0; i < 8; i++)
        {
            x[i, 0] = i % 2;
            times[i] = i + 1;
        }

        ProportionalHazards.HazardFit fit = ProportionalHazards.Fit(
            x, times, null, null, null, TieHandling.Breslow, null);
        Assert.True(Math.Abs(fit.Coefficients[0]) < 1.5);
        Assert.True(fit.P[0] > 0.1);
    }

    /// <summary>
    /// With no ties the two tie rules describe exactly the same likelihood, so they must give exactly
    /// the same answer; with ties Efron shrinks the risk set and gives a larger coefficient.
    /// </summary>
    [Fact]
    public void Coxphfit_TheTwoTieRulesAgreeWhenNothingIsTied()
    {
        var x = new double[6, 1];
        var times = new double[6];
        for (int i = 0; i < 6; i++)
        {
            x[i, 0] = (i * 0.7) % 2;
            times[i] = i + 1;
        }

        double breslow = ProportionalHazards.Fit(x, times, null, null, null, TieHandling.Breslow, null)
            .Coefficients[0];
        double efron = ProportionalHazards.Fit(x, times, null, null, null, TieHandling.Efron, null)
            .Coefficients[0];
        Assert.Equal(breslow, efron, 7);
    }

    [Fact]
    public void Coxphfit_TiedFailuresMakeTheTwoRulesDisagree()
    {
        var x = new double[8, 1];
        var times = new double[8];
        for (int i = 0; i < 8; i++)
        {
            x[i, 0] = i < 4 ? 1 : 0;
            times[i] = (i / 2) + 1;
        }

        double breslow = ProportionalHazards.Fit(x, times, null, null, null, TieHandling.Breslow, null)
            .Coefficients[0];
        double efron = ProportionalHazards.Fit(x, times, null, null, null, TieHandling.Efron, null)
            .Coefficients[0];
        Assert.True(Math.Abs(efron) > Math.Abs(breslow),
            "Efron's rule should be the less shrunken of the two.");
    }

    [Fact]
    public void Coxphfit_CensoringKeepsAnObservationInTheRiskSetWithoutAFailure()
    {
        var x = new double[6, 1];
        var times = new double[6];
        var censored = new bool[6];
        for (int i = 0; i < 6; i++)
        {
            x[i, 0] = i % 2;
            times[i] = i + 1;
            censored[i] = i is 2 or 4;
        }

        ProportionalHazards.HazardFit fit = ProportionalHazards.Fit(
            x, times, censored, null, null, TieHandling.Breslow, null);

        Assert.Equal(4, fit.Times.Length);
        Assert.Equal(4, fit.Schoenfeld.GetLength(0));

        // A censored observation cannot have a positive martingale residual: it never failed.
        Assert.True(fit.Martingale[2] < 0);
        Assert.True(fit.Martingale[4] < 0);
    }

    // --- Levenberg–Marquardt and nlinfit --------------------------------------------------------------

    [Fact]
    public void LevenbergMarquardt_OnALinearProblem_LandsOnTheLeastSquaresAnswer()
    {
        double[] x = [1, 2, 3, 4, 5, 6];
        double[] y = [3.1, 5.2, 6.8, 9.1, 11.2, 12.8];

        LevenbergMarquardt.Result result = LevenbergMarquardt.Minimize(
            beta =>
            {
                var residuals = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    residuals[i] = y[i] - beta[0] - (beta[1] * x[i]);
                }

                return residuals;
            },
            [0, 0]);

        double[] ordinary = LeastSquares.Solve(Intercept(x), y).Coefficients;
        Assert.True(result.Converged);
        Assert.Equal(ordinary[0], result.Solution[0], 6);
        Assert.Equal(ordinary[1], result.Solution[1], 6);
    }

    [Fact]
    public void LevenbergMarquardt_SolvesTheRosenbrockResiduals()
    {
        LevenbergMarquardt.Result result = LevenbergMarquardt.Minimize(
            b => [10 * (b[1] - (b[0] * b[0])), 1 - b[0]],
            [-1.2, 1.0],
            new LevenbergMarquardt.Settings(MaxIterations: 500));

        Assert.Equal(1, result.Solution[0], 5);
        Assert.Equal(1, result.Solution[1], 5);
        Assert.True(result.SumOfSquares < 1e-10);
    }

    [Fact]
    public void Nlinfit_RecoversTheParametersOfAnExponential()
    {
        double[] x = [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4];
        var y = new double[9];
        for (int i = 0; i < 9; i++)
        {
            y[i] = 2.5 * Math.Exp(-0.7 * x[i]);
        }

        NonlinearRegression.NonlinearFit fit = NonlinearRegression.Fit(
            beta =>
            {
                var predicted = new double[9];
                for (int i = 0; i < 9; i++)
                {
                    predicted[i] = beta[0] * Math.Exp(beta[1] * x[i]);
                }

                return predicted;
            },
            y, [1, -0.1], null, null, 0, default);

        Assert.Equal(2.5, fit.Coefficients[0], 5);
        Assert.Equal(-0.7, fit.Coefficients[1], 5);
        Assert.Equal(7, fit.Df);
    }

    [Fact]
    public void Nlinfit_OnALinearModel_ReproducesTheLinearCovariance()
    {
        NonlinearRegression.NonlinearFit fit = NonlinearRegression.Fit(
            beta =>
            {
                var predicted = new double[8];
                for (int i = 0; i < 8; i++)
                {
                    predicted[i] = beta[0] + (beta[1] * SimpleX[i]);
                }

                return predicted;
            },
            SimpleY, [0, 1], null, null, 0, default);

        LeastSquares.Fit linear = LeastSquares.Solve(Intercept(SimpleX), SimpleY);
        Assert.Equal(linear.Coefficients[0], fit.Coefficients[0], 6);
        Assert.Equal(linear.MeanSquaredError, fit.MeanSquaredError, 8);

        // The Jacobian is taken by differencing, so the covariance agrees relatively rather than to a
        // fixed number of decimals — the quantity itself is a ten-thousandth.
        Assert.Equal(1, fit.Covariance[1, 1] / linear.Covariance[1, 1], 5);
    }

    [Fact]
    public void Nlparci_IsTheEstimatePlusOrMinusTTimesItsError()
    {
        double[,] covariance = { { 0.04, 0 }, { 0, 0.09 } };
        (double[] lower, double[] upper) =
            NonlinearRegression.ParameterInterval([2, 5], covariance, 10, 0.05);

        double critical = ContinuousDistributions.TInv(0.975, 10);
        Assert.Equal(2 - (critical * 0.2), lower[0], 10);
        Assert.Equal(5 + (critical * 0.3), upper[1], 10);
    }

    [Fact]
    public void Nlpredci_TheObservationBandIsWiderThanTheCurveBand()
    {
        double[,] jacobian = { { 1, 0.5 }, { 1, 2.0 }, { 1, 3.5 } };
        double[,] covariance = { { 0.01, 0 }, { 0, 0.004 } };

        double[] curve = NonlinearRegression.PredictionInterval(jacobian, covariance, 0.25, 12, 0.05, false, false);
        double[] observation = NonlinearRegression.PredictionInterval(jacobian, covariance, 0.25, 12, 0.05, true, false);
        double[] simultaneous = NonlinearRegression.PredictionInterval(jacobian, covariance, 0.25, 12, 0.05, false, true);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(observation[i] > curve[i]);
            Assert.True(simultaneous[i] > curve[i]);
        }
    }

    [Fact]
    public void Hougen_IsTheDocumentedRateExpression()
    {
        double[] beta = [1.25, 0.06, 0.04, 0.11, 1.19];
        double[] x = [470, 300, 10];
        double expected = ((1.25 * 300) - (10 / 1.19))
            / (1 + (0.06 * 470) + (0.04 * 300) + (0.11 * 10));
        Assert.Equal(expected, NonlinearRegression.Hougen(beta, x), 10);
    }

    [Fact]
    public void Hougen_RefusesTheWrongNumberOfParameters() =>
        Assert.Throws<ArgumentException>(() => NonlinearRegression.Hougen([1, 2, 3], [1, 2, 3]));
}
