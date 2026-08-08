using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Regression;

/// <summary>The five error distributions <c>glmfit</c> takes.</summary>
public enum GlmFamily
{
    /// <summary>Constant variance — an ordinary linear model, reached the long way round.</summary>
    Normal,

    /// <summary>A count of successes out of a known number of trials.</summary>
    Binomial,

    /// <summary>A count, whose variance equals its mean.</summary>
    Poisson,

    /// <summary>A positive quantity whose spread is proportional to its size.</summary>
    Gamma,

    /// <summary>A positive quantity whose variance grows as the cube of its mean.</summary>
    InverseGaussian,
}

/// <summary>How the mean is related to the linear predictor.</summary>
public enum GlmLink
{
    /// <summary>The mean itself.</summary>
    Identity,

    /// <summary>Its logarithm.</summary>
    Log,

    /// <summary>Its log-odds — the canonical link for a proportion.</summary>
    Logit,

    /// <summary>The normal quantile of the proportion.</summary>
    Probit,

    /// <summary>The complementary log-log, which is asymmetric about a half.</summary>
    ComplementaryLogLog,

    /// <summary>The log-log, asymmetric the other way.</summary>
    LogLog,

    /// <summary>Its reciprocal — the canonical link for a gamma.</summary>
    Reciprocal,

    /// <summary>The mean raised to a stated power.</summary>
    Power,
}

/// <summary>
/// <c>glmfit</c> and <c>glmval</c>: a linear model whose response need not be normal and need not be
/// related to the predictors directly.
/// </summary>
/// <remarks>
/// <para>
/// The whole family is one fit. At each step the response is replaced by the value it would have had
/// on the linear predictor's scale, each observation is weighted by how precisely it pins that value
/// down, and ordinary weighted least squares is run — which is why this file contains no solver, only
/// a description of five variance functions and eight links. Where the link is the family's canonical
/// one this is exactly Newton's method on the likelihood; where it is not, it is Fisher scoring, which
/// converges to the same place.
/// </para>
/// <para>
/// A binomial response is carried as a proportion beside its number of trials rather than as a count,
/// because that is the form the variance function needs and it is what makes a single observation of
/// forty trials weigh forty times one observation of one.
/// </para>
/// </remarks>
public static class GeneralizedLinear
{
    /// <summary>What a generalized fit produced.</summary>
    /// <param name="Coefficients">The fitted coefficients.</param>
    /// <param name="Deviance">Twice the log-likelihood short of a perfect fit.</param>
    /// <param name="Fitted">The fitted mean for each observation.</param>
    /// <param name="LinearPredictor">The design times the coefficients, plus any offset.</param>
    /// <param name="Residuals">Observed less fitted, on the response's own scale.</param>
    /// <param name="PearsonResiduals">Those divided by the standard deviation the model predicts.</param>
    /// <param name="DevianceResiduals">The signed square root of each observation's contribution to the deviance.</param>
    /// <param name="AnscombeResiduals">The transformation that makes them closest to normal.</param>
    /// <param name="Covariance">The coefficients' covariance.</param>
    /// <param name="StandardErrors">One per coefficient.</param>
    /// <param name="T">Each coefficient over its standard error.</param>
    /// <param name="P">Its two-sided probability.</param>
    /// <param name="Dispersion">The scale the standard errors were multiplied by, one where it was not estimated.</param>
    /// <param name="FittedDispersion">The dispersion the data implies, estimated or not.</param>
    /// <param name="Df">Observations less coefficients.</param>
    /// <param name="Iterations">How many reweightings were taken.</param>
    /// <param name="Converged">Whether the coefficients settled before the budget ran out.</param>
    public readonly record struct GlmFit(
        double[] Coefficients,
        double Deviance,
        double[] Fitted,
        double[] LinearPredictor,
        double[] Residuals,
        double[] PearsonResiduals,
        double[] DevianceResiduals,
        double[] AnscombeResiduals,
        double[,] Covariance,
        double[] StandardErrors,
        double[] T,
        double[] P,
        double Dispersion,
        double FittedDispersion,
        int Df,
        int Iterations,
        bool Converged);

    /// <summary>The link a family is fitted with unless the caller names another.</summary>
    public static (GlmLink Link, double Power) CanonicalLink(GlmFamily family) => family switch
    {
        GlmFamily.Normal => (GlmLink.Identity, 0),
        GlmFamily.Binomial => (GlmLink.Logit, 0),
        GlmFamily.Poisson => (GlmLink.Log, 0),
        GlmFamily.Gamma => (GlmLink.Reciprocal, 0),
        GlmFamily.InverseGaussian => (GlmLink.Power, -2),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>Whether a family's dispersion is estimated from the data rather than fixed at one.</summary>
    public static bool EstimatesDispersionByDefault(GlmFamily family) =>
        family is GlmFamily.Normal or GlmFamily.Gamma or GlmFamily.InverseGaussian;

    /// <summary>The linear predictor a mean corresponds to.</summary>
    public static double Link(GlmLink link, double power, double mu) => link switch
    {
        GlmLink.Identity => mu,
        GlmLink.Log => Math.Log(mu),
        GlmLink.Logit => Math.Log(mu / (1 - mu)),
        GlmLink.Probit => ContinuousDistributions.NormalInv(mu, 0, 1),
        GlmLink.ComplementaryLogLog => Math.Log(-Math.Log(1 - mu)),
        GlmLink.LogLog => Math.Log(-Math.Log(mu)),
        GlmLink.Reciprocal => 1 / mu,
        GlmLink.Power => Math.Pow(mu, power),
        _ => throw new ArgumentOutOfRangeException(nameof(link)),
    };

    /// <summary>The mean a linear predictor corresponds to.</summary>
    public static double Inverse(GlmLink link, double power, double eta) => link switch
    {
        GlmLink.Identity => eta,
        GlmLink.Log => Math.Exp(eta),
        GlmLink.Logit => 1 / (1 + Math.Exp(-eta)),
        GlmLink.Probit => ContinuousDistributions.NormalCdf(eta, 0, 1),
        GlmLink.ComplementaryLogLog => 1 - Math.Exp(-Math.Exp(eta)),
        GlmLink.LogLog => Math.Exp(-Math.Exp(eta)),
        GlmLink.Reciprocal => 1 / eta,
        GlmLink.Power => Math.Pow(eta, 1 / power),
        _ => throw new ArgumentOutOfRangeException(nameof(link)),
    };

    /// <summary>How fast the linear predictor moves as the mean does.</summary>
    public static double Derivative(GlmLink link, double power, double mu) => link switch
    {
        GlmLink.Identity => 1,
        GlmLink.Log => 1 / mu,
        GlmLink.Logit => 1 / (mu * (1 - mu)),
        GlmLink.Probit => 1 / Math.Max(
            1e-300, ContinuousDistributions.NormalPdf(ContinuousDistributions.NormalInv(mu, 0, 1), 0, 1)),
        GlmLink.ComplementaryLogLog => 1 / ((mu - 1) * Math.Log(1 - mu)),
        GlmLink.LogLog => 1 / (mu * Math.Log(mu)),
        GlmLink.Reciprocal => -1 / (mu * mu),
        GlmLink.Power => power * Math.Pow(mu, power - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(link)),
    };

    /// <summary>The variance the family predicts at a given mean, for one trial's worth of observation.</summary>
    public static double Variance(GlmFamily family, double mu) => family switch
    {
        GlmFamily.Normal => 1,
        GlmFamily.Binomial => mu * (1 - mu),
        GlmFamily.Poisson => mu,
        GlmFamily.Gamma => mu * mu,
        GlmFamily.InverseGaussian => mu * mu * mu,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>Fits the model by iteratively reweighted least squares.</summary>
    /// <param name="design">The design matrix, intercept column included if one is wanted.</param>
    /// <param name="y">The response — a proportion, where the family is binomial.</param>
    /// <param name="family">The error distribution.</param>
    /// <param name="link">Which function relates the mean to the linear predictor.</param>
    /// <param name="power">The exponent, where that function is a power.</param>
    /// <param name="trials">Trials behind each binomial proportion, or null for one each.</param>
    /// <param name="priorWeights">A weight for each observation, or null for one each.</param>
    /// <param name="offset">A term added to the linear predictor and not fitted, or null for none.</param>
    /// <param name="estimateDispersion">Whether to scale the standard errors by the observed dispersion.</param>
    public static GlmFit Fit(
        double[,] design,
        double[] y,
        GlmFamily family,
        GlmLink link,
        double power,
        double[]? trials,
        double[]? priorWeights,
        double[]? offset,
        bool estimateDispersion)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(y);

        int n = design.GetLength(0);
        int k = design.GetLength(1);
        if (y.Length != n)
        {
            throw new ArgumentException(
                $"the response has {y.Length} values but the design has {n} rows.", nameof(y));
        }

        if (link == GlmLink.Power && power == 0)
        {
            throw new ArgumentException("a power link needs a non-zero exponent.", nameof(power));
        }

        var counts = new double[n];
        var prior = new double[n];
        var shift = new double[n];
        for (int i = 0; i < n; i++)
        {
            counts[i] = trials is null ? 1 : trials[i];
            prior[i] = priorWeights is null ? 1 : priorWeights[i];
            shift[i] = offset is null ? 0 : offset[i];
            if (counts[i] <= 0)
            {
                throw new ArgumentException("every binomial observation needs at least one trial.");
            }

            if (prior[i] < 0)
            {
                throw new ArgumentException("an observation's weight cannot be negative.");
            }

            CheckResponse(family, y[i]);
        }

        var mu = new double[n];
        for (int i = 0; i < n; i++)
        {
            mu[i] = Start(family, y[i], counts[i]);
        }

        LeastSquares.Fit fit = default;
        var working = new double[n];
        var weights = new double[n];
        bool converged = false;
        int iterations = 0;
        double previous = double.PositiveInfinity;
        for (iterations = 1; iterations <= 100; iterations++)
        {
            for (int i = 0; i < n; i++)
            {
                double slope = Derivative(link, power, mu[i]);
                working[i] = Link(link, power, mu[i]) - shift[i] + ((y[i] - mu[i]) * slope);
                double variance = Variance(family, mu[i]) / counts[i];
                weights[i] = prior[i] / Math.Max(1e-300, variance * slope * slope);
            }

            fit = LeastSquares.Solve(design, working, weights);
            for (int i = 0; i < n; i++)
            {
                double eta = shift[i];
                for (int c = 0; c < k; c++)
                {
                    eta += design[i, c] * fit.Coefficients[c];
                }

                mu[i] = Clamp(family, Inverse(link, power, eta));
            }

            double deviance = Deviance(family, y, mu, counts, prior);
            if (Math.Abs(deviance - previous) <= 1e-10 * (1 + Math.Abs(deviance)))
            {
                converged = true;
                break;
            }

            previous = deviance;
        }

        iterations = Math.Min(iterations, 100);
        var linear = new double[n];
        var residuals = new double[n];
        var pearson = new double[n];
        var devianceResiduals = new double[n];
        var anscombe = new double[n];
        for (int i = 0; i < n; i++)
        {
            linear[i] = Link(link, power, mu[i]);
            residuals[i] = y[i] - mu[i];
            double spread = Math.Sqrt(Math.Max(1e-300, Variance(family, mu[i]) / counts[i]));
            pearson[i] = residuals[i] / spread;
            double part = UnitDeviance(family, y[i], mu[i]) * counts[i] * prior[i];
            devianceResiduals[i] = Math.Sign(residuals[i]) * Math.Sqrt(Math.Max(0, part));
            anscombe[i] = Anscombe(family, y[i], mu[i], counts[i]);
        }

        double total = Deviance(family, y, mu, counts, prior);
        int df = n - fit.Rank;
        double pearsonSum = 0;
        for (int i = 0; i < n; i++)
        {
            pearsonSum += prior[i] * pearson[i] * pearson[i];
        }

        double fitted = df > 0 ? Math.Sqrt(pearsonSum / df) : 0;
        double dispersion = estimateDispersion ? fitted : 1;

        var covariance = new double[k, k];
        for (int a = 0; a < k; a++)
        {
            for (int b = 0; b < k; b++)
            {
                covariance[a, b] = dispersion * dispersion * fit.CrossInverse[a, b];
            }
        }

        var standardErrors = new double[k];
        var t = new double[k];
        var probability = new double[k];
        for (int j = 0; j < k; j++)
        {
            standardErrors[j] = Math.Sqrt(Math.Max(0, covariance[j, j]));
            t[j] = standardErrors[j] > 0 ? fit.Coefficients[j] / standardErrors[j] : double.NaN;

            // Where the dispersion was not estimated the statistic is a normal deviate, not a t: there
            // is no estimated scale in the denominator for the extra spread to come from.
            probability[j] = !double.IsFinite(t[j])
                ? double.NaN
                : estimateDispersion && df > 0
                    ? 2 * ContinuousDistributions.TCdf(-Math.Abs(t[j]), df)
                    : 2 * ContinuousDistributions.NormalCdf(-Math.Abs(t[j]), 0, 1);
        }

        return new GlmFit(
            fit.Coefficients, total, mu, linear, residuals, pearson, devianceResiduals, anscombe,
            covariance, standardErrors, t, probability, dispersion, fitted, df, iterations, converged);
    }

    /// <summary><c>glmval</c>: the mean the model predicts at each row, and how far its interval reaches.</summary>
    /// <param name="coefficients">The fitted coefficients.</param>
    /// <param name="design">The rows to predict at, intercept column included if the fit had one.</param>
    /// <param name="link">The link the fit used.</param>
    /// <param name="power">Its exponent, where the link is a power.</param>
    /// <param name="covariance">The coefficients' covariance, or null to skip the interval.</param>
    /// <param name="df">The fit's residual degrees of freedom.</param>
    /// <param name="alpha">One less the confidence level.</param>
    /// <param name="simultaneous">Whether the interval must hold at every row at once.</param>
    /// <param name="offset">A term added to the linear predictor, or null for none.</param>
    /// <returns>
    /// The predicted means and the two half-widths. They differ because the interval is symmetric on
    /// the linear predictor's scale and the link then bends it, which is what keeps a predicted
    /// proportion's interval inside zero and one.
    /// </returns>
    public static (double[] Predicted, double[] Lower, double[] Upper) Evaluate(
        double[] coefficients,
        double[,] design,
        GlmLink link,
        double power,
        double[,]? covariance,
        int df,
        double alpha,
        bool simultaneous,
        double[]? offset)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentNullException.ThrowIfNull(design);

        int n = design.GetLength(0);
        int k = design.GetLength(1);
        if (k != coefficients.Length)
        {
            throw new ArgumentException(
                $"the design has {k} columns for {coefficients.Length} coefficients.", nameof(design));
        }

        var predicted = new double[n];
        var lower = new double[n];
        var upper = new double[n];
        double multiplier = 0;
        if (covariance is not null)
        {
            LinearRegression.CheckLevel(alpha);
            multiplier = simultaneous
                ? Math.Sqrt(k * (df > 0
                    ? ContinuousDistributions.FInv(1 - alpha, k, df)
                    : ContinuousDistributions.Chi2Inv(1 - alpha, k) / k))
                : df > 0
                    ? ContinuousDistributions.TInv(1 - (alpha / 2), df)
                    : ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1);
        }

        for (int r = 0; r < n; r++)
        {
            double[] row = LeastSquares.Row(design, r);
            double eta = offset is null ? 0 : offset[r];
            for (int c = 0; c < k; c++)
            {
                eta += row[c] * coefficients[c];
            }

            predicted[r] = Inverse(link, power, eta);
            if (covariance is null)
            {
                continue;
            }

            double spread = multiplier * Math.Sqrt(LeastSquares.PredictionVariance(covariance, row));
            double low = Inverse(link, power, eta - spread);
            double high = Inverse(link, power, eta + spread);
            lower[r] = Math.Abs(predicted[r] - Math.Min(low, high));
            upper[r] = Math.Abs(Math.Max(low, high) - predicted[r]);
        }

        return (predicted, lower, upper);
    }

    /// <summary>Twice the gap between this model's log-likelihood and a model that fits every point exactly.</summary>
    public static double Deviance(
        GlmFamily family, double[] y, double[] mu, double[] trials, double[] weights)
    {
        double total = 0;
        for (int i = 0; i < y.Length; i++)
        {
            total += weights[i] * trials[i] * UnitDeviance(family, y[i], mu[i]);
        }

        return total;
    }

    /// <summary>One observation's contribution to the deviance, before its weight and trial count.</summary>
    private static double UnitDeviance(GlmFamily family, double y, double mu) => family switch
    {
        GlmFamily.Normal => (y - mu) * (y - mu),
        GlmFamily.Binomial => 2 * (Xlog(y, mu) + Xlog(1 - y, 1 - mu)),
        GlmFamily.Poisson => 2 * (Xlog(y, mu) - (y - mu)),
        GlmFamily.Gamma => 2 * (-Math.Log(Math.Max(1e-300, y / mu)) + ((y - mu) / mu)),
        GlmFamily.InverseGaussian => (y - mu) * (y - mu) / (mu * mu * Math.Max(1e-300, y)),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>The residual transformed to be as close to normal as the family allows.</summary>
    private static double Anscombe(GlmFamily family, double y, double mu, double trials)
    {
        switch (family)
        {
            case GlmFamily.Normal:
                return y - mu;
            case GlmFamily.Poisson:
                return 1.5 * (Math.Pow(Math.Max(0, y), 2.0 / 3) - Math.Pow(mu, 2.0 / 3))
                    / Math.Pow(mu, 1.0 / 6);
            case GlmFamily.Gamma:
                return 3 * (Math.Pow(Math.Max(1e-300, y), 1.0 / 3) - Math.Pow(mu, 1.0 / 3))
                    / Math.Pow(mu, 1.0 / 3);
            case GlmFamily.InverseGaussian:
                return (Math.Log(Math.Max(1e-300, y)) - Math.Log(mu)) / Math.Sqrt(mu);
            case GlmFamily.Binomial:
                {
                    // The incomplete beta at ⅔, ⅔, which is the transformation that flattens a
                    // proportion's variance; the difference is then scaled by what is left of it.
                    double numerator = IncompleteTwoThirds(y) - IncompleteTwoThirds(mu);
                    double denominator = Math.Pow(Math.Max(1e-300, mu * (1 - mu)), 1.0 / 6)
                        / Math.Sqrt(trials);
                    return numerator / denominator;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }
    }

    /// <summary>The regularized incomplete beta at two thirds, times its complete value.</summary>
    private static double IncompleteTwoThirds(double p) =>
        ContinuousDistributions.BetaCdf(Math.Clamp(p, 0, 1), 2.0 / 3, 2.0 / 3)
        * Math.Exp(JGraph.Numerics.SpecialFunctions.LogBeta(2.0 / 3, 2.0 / 3));

    /// <summary><c>y·log(y/mu)</c>, which is zero rather than undefined where <c>y</c> is.</summary>
    private static double Xlog(double y, double mu) =>
        y <= 0 ? 0 : y * Math.Log(y / Math.Max(1e-300, mu));

    /// <summary>The mean each family starts from, nudged off the boundary where the response sits on one.</summary>
    private static double Start(GlmFamily family, double y, double trials) => family switch
    {
        GlmFamily.Binomial => ((trials * y) + 0.5) / (trials + 1),
        GlmFamily.Poisson => y + 0.25,
        GlmFamily.Gamma or GlmFamily.InverseGaussian => Math.Max(y, 1e-6),
        _ => y,
    };

    /// <summary>Keeps a fitted mean inside the family's domain.</summary>
    private static double Clamp(GlmFamily family, double mu) => family switch
    {
        GlmFamily.Binomial => Math.Clamp(mu, 1e-10, 1 - 1e-10),
        GlmFamily.Poisson or GlmFamily.Gamma or GlmFamily.InverseGaussian => Math.Max(mu, 1e-10),
        _ => mu,
    };

    /// <summary>Refuses a response the family cannot have produced.</summary>
    private static void CheckResponse(GlmFamily family, double y)
    {
        bool ok = family switch
        {
            GlmFamily.Binomial => y is >= 0 and <= 1,
            GlmFamily.Poisson => y >= 0,
            GlmFamily.Gamma or GlmFamily.InverseGaussian => y > 0,
            _ => true,
        };

        if (!ok)
        {
            throw new ArgumentException(family switch
            {
                GlmFamily.Binomial => $"a binomial response is a proportion between 0 and 1, and {y} is not.",
                GlmFamily.Poisson => $"a Poisson response is a count, and {y} is negative.",
                _ => $"a {family.ToString().ToLowerInvariant()} response must be positive, and {y} is not.",
            });
        }
    }
}
