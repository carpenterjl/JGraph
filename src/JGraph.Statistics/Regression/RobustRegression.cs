using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Regression;

/// <summary>The nine weight functions <c>robustfit</c> names, each with the tuning constant it is used at.</summary>
public enum RobustWeight
{
    /// <summary>The sine of the scaled residual over itself, zero past π.</summary>
    Andrews,

    /// <summary>Tukey's biweight: <c>(1 − r²)²</c>, zero past one. MathWorks' default.</summary>
    Bisquare,

    /// <summary><c>1 / (1 + r²)</c>, which never quite reaches zero.</summary>
    Cauchy,

    /// <summary><c>1 / (1 + |r|)</c>.</summary>
    Fair,

    /// <summary>Full weight inside one, then <c>1 / |r|</c> — squared error near the middle, absolute outside.</summary>
    Huber,

    /// <summary><c>tanh(r) / r</c>.</summary>
    Logistic,

    /// <summary>Every observation weighted the same, which makes the fit ordinary least squares.</summary>
    Ols,

    /// <summary>Full weight inside one and none outside: a hard rejection rule.</summary>
    Talwar,

    /// <summary><c>exp(−r²)</c>.</summary>
    Welsch,
}

/// <summary>
/// <c>robustfit</c>: least squares run again and again, each time weighting an observation by how well
/// the previous fit already explained it.
/// </summary>
/// <remarks>
/// <para>
/// The nine weight functions differ only in how quickly they give up on a residual, so they are one
/// switch rather than nine fitters. What is not shared is the scale they are measured against: a
/// residual is large or small only relative to an estimate of the error's spread, and that estimate
/// must itself survive the outliers it is being used to find, so it comes from the median absolute
/// residual rather than the root mean square.
/// </para>
/// <para>
/// Two refinements matter enough to name. Residuals are divided by <c>√(1 − h)</c> before being
/// judged, because a high-leverage point drags the fit towards itself and so has an unfairly small
/// residual to begin with. And the scale finally reported is not the median-based one but Street,
/// Carroll and Ruppert's correction, which accounts for the weighting having been applied at all —
/// without it the standard errors of a heavily downweighted fit come out too small.
/// </para>
/// </remarks>
public static class RobustRegression
{
    /// <summary>What a robust fit produced.</summary>
    /// <param name="Coefficients">The fitted coefficients.</param>
    /// <param name="Residuals">Observed less fitted.</param>
    /// <param name="Weights">The weight each observation ended up with.</param>
    /// <param name="Leverage">The hat diagonal of the unweighted design.</param>
    /// <param name="OlsScale">The residual scale an ordinary fit would have reported.</param>
    /// <param name="RobustScale">The corrected robust scale.</param>
    /// <param name="MadScale">The median-absolute-residual scale the weighting used.</param>
    /// <param name="Scale">The scale the standard errors are built from.</param>
    /// <param name="StandardErrors">One per coefficient.</param>
    /// <param name="Covariance">The coefficients' covariance.</param>
    /// <param name="T">Each coefficient over its standard error.</param>
    /// <param name="P">The two-sided probability of each.</param>
    /// <param name="StudentizedResiduals">Residuals over the robust scale, corrected for leverage.</param>
    /// <param name="Df">Observations less coefficients.</param>
    /// <param name="Iterations">How many reweightings were taken.</param>
    /// <param name="Converged">Whether the coefficients settled before the budget ran out.</param>
    public readonly record struct RobustFit(
        double[] Coefficients,
        double[] Residuals,
        double[] Weights,
        double[] Leverage,
        double OlsScale,
        double RobustScale,
        double MadScale,
        double Scale,
        double[] StandardErrors,
        double[,] Covariance,
        double[] T,
        double[] P,
        double[] StudentizedResiduals,
        int Df,
        int Iterations,
        bool Converged);

    /// <summary>The tuning constant a weight function is used at unless the caller names another.</summary>
    public static double DefaultTuning(RobustWeight weight) => weight switch
    {
        RobustWeight.Andrews => 1.339,
        RobustWeight.Bisquare => 4.685,
        RobustWeight.Cauchy => 2.385,
        RobustWeight.Fair => 1.400,
        RobustWeight.Huber => 1.345,
        RobustWeight.Logistic => 1.205,
        RobustWeight.Ols => 1.0,
        RobustWeight.Talwar => 2.795,
        RobustWeight.Welsch => 2.985,
        _ => throw new ArgumentOutOfRangeException(nameof(weight)),
    };

    /// <summary>How much weight an observation whose scaled residual is <paramref name="r"/> keeps.</summary>
    public static double Weigh(RobustWeight weight, double r)
    {
        double absolute = Math.Abs(r);
        return weight switch
        {
            RobustWeight.Andrews => absolute < Math.PI
                ? (absolute < 1e-12 ? 1 : Math.Sin(r) / r)
                : 0,
            RobustWeight.Bisquare => absolute < 1 ? (1 - (r * r)) * (1 - (r * r)) : 0,
            RobustWeight.Cauchy => 1 / (1 + (r * r)),
            RobustWeight.Fair => 1 / (1 + absolute),
            RobustWeight.Huber => 1 / Math.Max(1, absolute),
            RobustWeight.Logistic => absolute < 1e-12 ? 1 : Math.Tanh(r) / r,
            RobustWeight.Ols => 1,
            RobustWeight.Talwar => absolute < 1 ? 1 : 0,
            RobustWeight.Welsch => Math.Exp(-(r * r)),
            _ => throw new ArgumentOutOfRangeException(nameof(weight)),
        };
    }

    /// <summary>Fits <c>y = X·b</c>, downweighting whatever the fit cannot explain.</summary>
    /// <param name="design">The design matrix, intercept column included if one is wanted.</param>
    /// <param name="y">The response.</param>
    /// <param name="weight">Which weight function to reject outliers with.</param>
    /// <param name="tuning">Its tuning constant, or zero for the documented default.</param>
    public static RobustFit Fit(double[,] design, double[] y, RobustWeight weight, double tuning)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(y);

        if (tuning <= 0)
        {
            tuning = DefaultTuning(weight);
        }

        int n = design.GetLength(0);
        int k = design.GetLength(1);
        LeastSquares.Fit ordinary = LeastSquares.Solve(design, y);
        double olsScale = ordinary.Df > 0 ? Math.Sqrt(ordinary.MeanSquaredError) : 0;

        // What counts as no spread at all. A fit that reproduces most of the data exactly leaves
        // residuals of rounding error, and their median is a scale in name only — measured against it
        // every other rounding error looks like an outlier of moderate size, and the weights come out
        // as arbitrary fractions instead of the ones and zeros the situation actually calls for.
        double largest = 0;
        foreach (double value in y)
        {
            largest = Math.Max(largest, Math.Abs(value));
        }

        double floor = 1e-12 * (1 + largest);

        var weights = new double[n];
        Array.Fill(weights, 1.0);
        LeastSquares.Fit fit = ordinary;
        double mad = MedianScale(Adjusted(ordinary.Residuals, ordinary.Leverage), ordinary.Rank);

        int iterations = 0;
        bool converged = weight == RobustWeight.Ols;
        if (!converged)
        {
            for (iterations = 1; iterations <= 50; iterations++)
            {
                double[] adjusted = Adjusted(fit.Residuals, ordinary.Leverage);
                mad = MedianScale(adjusted, ordinary.Rank);
                if (mad <= floor)
                {
                    mad = 0;
                }

                // A fit passing exactly through more than half the data leaves no scale to measure
                // the rest against. That is the limit every weight function shares: an observation the
                // fit reaches keeps its full weight and one it does not is rejected outright. The
                // weights are still recorded, because they are what the caller is told about.
                double reference = 0;
                foreach (double value in adjusted)
                {
                    reference = Math.Max(reference, Math.Abs(value));
                }

                for (int i = 0; i < n; i++)
                {
                    weights[i] = mad > 0
                        ? Weigh(weight, adjusted[i] / (mad * tuning))
                        : Math.Abs(adjusted[i]) <= 1e-12 * (1 + reference) ? 1 : 0;
                }

                if (mad <= 0)
                {
                    fit = LeastSquares.Solve(design, y, weights);
                    converged = true;
                    break;
                }

                LeastSquares.Fit next = LeastSquares.Solve(design, y, weights);
                double movement = 0, size = 0;
                for (int j = 0; j < k; j++)
                {
                    movement = Math.Max(movement, Math.Abs(next.Coefficients[j] - fit.Coefficients[j]));
                    size = Math.Max(size, Math.Abs(next.Coefficients[j]));
                }

                fit = next;
                if (movement <= 1e-8 * (1 + size))
                {
                    converged = true;
                    break;
                }
            }

            iterations = Math.Min(iterations, 50);
        }

        int df = n - ordinary.Rank;
        double robustScale = weight == RobustWeight.Ols
            ? olsScale
            : CorrectedScale(weight, Adjusted(fit.Residuals, ordinary.Leverage), ordinary.Rank, mad, tuning);

        // A robust scale of zero means the weighting drove the median absolute residual to nothing:
        // the fit passes through everything it still trusts. Reporting the ordinary scale there would
        // charge the fit for the very observations it rejected, so what is reported instead is the
        // spread of what it kept — and only if that too is zero does the ordinary scale stand in.
        if (robustScale <= 0 && weight != RobustWeight.Ols)
        {
            robustScale = WeightedScale(fit.Residuals, weights, ordinary.Rank);
        }

        double scale = robustScale <= 0
            ? olsScale
            : Math.Max(
                robustScale,
                Math.Sqrt((((olsScale * olsScale) * ordinary.Rank * ordinary.Rank) + (robustScale * robustScale * n))
                    / ((ordinary.Rank * ordinary.Rank) + n)));

        // The covariance is the weighted cross-product's inverse scaled by the robust variance, not by
        // the weighted residual sum: the weights were chosen to ignore observations, and the residual
        // sum they leave behind would understate the error.
        var covariance = new double[k, k];
        for (int a = 0; a < k; a++)
        {
            for (int b = 0; b < k; b++)
            {
                covariance[a, b] = scale * scale * fit.CrossInverse[a, b];
            }
        }

        var standardErrors = new double[k];
        var t = new double[k];
        var probability = new double[k];
        for (int j = 0; j < k; j++)
        {
            standardErrors[j] = Math.Sqrt(Math.Max(0, covariance[j, j]));
            t[j] = standardErrors[j] > 0 ? fit.Coefficients[j] / standardErrors[j] : double.NaN;
            probability[j] = df > 0 && double.IsFinite(t[j])
                ? 2 * ContinuousDistributions.TCdf(-Math.Abs(t[j]), df)
                : double.NaN;
        }

        var studentized = new double[n];
        for (int i = 0; i < n; i++)
        {
            double room = Math.Sqrt(Math.Max(1e-300, 1 - ordinary.Leverage[i]));
            studentized[i] = scale > 0 ? fit.Residuals[i] / (scale * room) : double.NaN;
        }

        return new RobustFit(
            fit.Coefficients, fit.Residuals, weights, ordinary.Leverage, olsScale, robustScale, mad, scale,
            standardErrors, covariance, t, probability, studentized, df, iterations, converged);
    }

    /// <summary>The residual spread of the observations the weighting kept, over their own degrees of freedom.</summary>
    private static double WeightedScale(double[] residuals, double[] weights, int rank)
    {
        double squares = 0, total = 0;
        for (int i = 0; i < residuals.Length; i++)
        {
            squares += weights[i] * residuals[i] * residuals[i];
            total += weights[i];
        }

        return total > rank ? Math.Sqrt(squares / (total - rank)) : 0;
    }

    /// <summary>Residuals divided by how much room the fit left them, which a high-leverage point has little of.</summary>
    private static double[] Adjusted(double[] residuals, double[] leverage)
    {
        var adjusted = new double[residuals.Length];
        for (int i = 0; i < residuals.Length; i++)
        {
            adjusted[i] = residuals[i] / Math.Sqrt(Math.Max(1e-12, 1 - leverage[i]));
        }

        return adjusted;
    }

    /// <summary>
    /// The median absolute residual over 0.6745, which is what that ratio equals for normal errors. The
    /// smallest few residuals are dropped first, because a fit with <c>p</c> coefficients passes exactly
    /// through <c>p</c> points and those zeros would drag the median down.
    /// </summary>
    private static double MedianScale(double[] residuals, int rank)
    {
        var sorted = new double[residuals.Length];
        for (int i = 0; i < residuals.Length; i++)
        {
            sorted[i] = Math.Abs(residuals[i]);
        }

        Array.Sort(sorted);
        int from = Math.Min(Math.Max(0, rank - 1), sorted.Length - 1);
        int count = sorted.Length - from;
        double median = count % 2 == 1
            ? sorted[from + (count / 2)]
            : (sorted[from + (count / 2) - 1] + sorted[from + (count / 2)]) / 2;
        return median / 0.6745;
    }

    /// <summary>
    /// Street, Carroll and Ruppert's scale: the weighted residuals corrected by how steeply the weight
    /// function was falling where they landed, which is what the weighting cost in efficiency.
    /// </summary>
    private static double CorrectedScale(
        RobustWeight weight, double[] residuals, int rank, double mad, double tuning)
    {
        int n = residuals.Length;
        if (mad <= 0 || n <= rank)
        {
            return 0;
        }

        double unit = mad * tuning;
        const double Step = 1e-4;
        var influence = new double[n];
        var slope = new double[n];
        for (int i = 0; i < n; i++)
        {
            double u = residuals[i] / unit;
            influence[i] = u * Weigh(weight, u);
            double below = (u - Step) * Weigh(weight, u - Step);
            double above = (u + Step) * Weigh(weight, u + Step);
            slope[i] = (above - below) / (2 * Step);
        }

        double meanSlope = 0;
        foreach (double value in slope)
        {
            meanSlope += value;
        }

        meanSlope /= n;
        if (Math.Abs(meanSlope) < 1e-12)
        {
            return 0;
        }

        double spread = 0;
        foreach (double value in slope)
        {
            spread += (value - meanSlope) * (value - meanSlope);
        }

        spread /= n - 1;

        double squares = 0;
        foreach (double value in influence)
        {
            squares += value * value;
        }

        double correction = 1 + (rank * spread / (n * meanSlope * meanSlope));
        return correction * unit * Math.Sqrt(squares / (n - rank)) / Math.Abs(meanSlope);
    }
}
