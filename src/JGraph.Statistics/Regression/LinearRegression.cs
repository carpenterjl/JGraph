using JGraph.Statistics.Distributions;
using JGraph.Statistics.Hypothesis;

namespace JGraph.Statistics.Regression;

/// <summary>
/// The ordinary linear model and everything read off one fit of it: <c>regress</c>, the diagnostics
/// <c>regstats</c> reports, <c>leverage</c>, <c>ridge</c>, the interval around a polynomial that
/// <c>polyconf</c> draws, and the inverse prediction <c>invpred</c> makes.
/// </summary>
/// <remarks>
/// Almost none of this is a second fit. The delete-one quantities — what the coefficients would have
/// been without observation <c>i</c>, how far the fit would have moved, how much the covariance would
/// have shrunk — all follow in closed form from the single fit and its leverages, which is why
/// <c>regstats</c> answers <c>n</c> alternative models for the cost of one.
/// </remarks>
public static class LinearRegression
{
    /// <summary>What <c>regress</c> answers.</summary>
    /// <param name="Coefficients">The fitted coefficients.</param>
    /// <param name="Lower">The lower end of each coefficient's interval.</param>
    /// <param name="Upper">The upper end.</param>
    /// <param name="Residuals">Observed less fitted.</param>
    /// <param name="ResidualLower">The lower end of each residual's interval.</param>
    /// <param name="ResidualUpper">The upper end; an interval clear of zero marks an outlier.</param>
    /// <param name="RSquare">The fraction of variation the model accounts for.</param>
    /// <param name="F">The statistic for the whole model against a constant.</param>
    /// <param name="P">Its probability.</param>
    /// <param name="ErrorVariance">The estimate of the residual variance.</param>
    public readonly record struct Regression(
        double[] Coefficients,
        double[] Lower,
        double[] Upper,
        double[] Residuals,
        double[] ResidualLower,
        double[] ResidualUpper,
        double RSquare,
        double F,
        double P,
        double ErrorVariance);

    /// <summary>Everything <c>regstats</c> can be asked for, computed from one fit.</summary>
    /// <param name="Fit">The fit itself.</param>
    /// <param name="HatMatrix">The projection onto the model's column space.</param>
    /// <param name="DeletedVariance">The error variance re-estimated without each observation.</param>
    /// <param name="DeletedCoefficients">The coefficients re-fitted without each observation, one row each.</param>
    /// <param name="StandardizedResiduals">Residuals over their own standard error.</param>
    /// <param name="StudentizedResiduals">The same, using the variance estimated without the observation.</param>
    /// <param name="DfBetas">How far each coefficient moves when each observation is dropped, scaled.</param>
    /// <param name="DfFit">How far the fitted value at each observation moves when it is dropped.</param>
    /// <param name="DfFits">The same, scaled by the deleted standard error.</param>
    /// <param name="CovarianceRatio">How the covariance's size changes when each observation is dropped.</param>
    /// <param name="CooksDistance">How far the whole coefficient vector moves.</param>
    /// <param name="StandardErrors">One per coefficient.</param>
    /// <param name="T">Each coefficient over its standard error.</param>
    /// <param name="TProbability">The two-sided probability of each.</param>
    /// <param name="RegressionSumOfSquares">The variation the model accounts for.</param>
    /// <param name="ModelF">The statistic for the whole model.</param>
    /// <param name="ModelP">Its probability.</param>
    /// <param name="RSquare">The fraction of variation accounted for.</param>
    /// <param name="AdjustedRSquare">The same, penalized for the number of coefficients.</param>
    /// <param name="DurbinWatsonStatistic">How much each residual resembles the one before it.</param>
    /// <param name="DurbinWatsonProbability">Its probability.</param>
    public readonly record struct Diagnostics(
        LeastSquares.Fit Fit,
        double[,] HatMatrix,
        double[] DeletedVariance,
        double[,] DeletedCoefficients,
        double[] StandardizedResiduals,
        double[] StudentizedResiduals,
        double[,] DfBetas,
        double[] DfFit,
        double[] DfFits,
        double[] CovarianceRatio,
        double[] CooksDistance,
        double[] StandardErrors,
        double[] T,
        double[] TProbability,
        double RegressionSumOfSquares,
        double ModelF,
        double ModelP,
        double RSquare,
        double AdjustedRSquare,
        double DurbinWatsonStatistic,
        double DurbinWatsonProbability);

    /// <summary><c>[b, bint, r, rint, stats] = regress(y, X, alpha)</c>.</summary>
    /// <param name="y">The response.</param>
    /// <param name="design">The design matrix, intercept column included if one is wanted.</param>
    /// <param name="alpha">One less the confidence level.</param>
    public static Regression Regress(double[] y, double[,] design, double alpha)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(design);
        CheckLevel(alpha);

        LeastSquares.Fit fit = LeastSquares.Solve(design, y);
        int n = y.Length;
        int p = fit.Rank;
        int dfe = fit.Df;

        var lower = new double[fit.Coefficients.Length];
        var upper = new double[fit.Coefficients.Length];
        double critical = dfe > 0 ? ContinuousDistributions.TInv(1 - (alpha / 2), dfe) : double.PositiveInfinity;
        for (int j = 0; j < fit.Coefficients.Length; j++)
        {
            double spread = critical * Math.Sqrt(Math.Max(0, fit.Covariance[j, j]));
            lower[j] = fit.Coefficients[j] - spread;
            upper[j] = fit.Coefficients[j] + spread;
        }

        // The residual intervals diagnose outliers, so each is built from the error variance estimated
        // without the observation it belongs to — otherwise a single wild point inflates the very
        // spread that is supposed to reveal it.
        var residualLower = new double[n];
        var residualUpper = new double[n];
        double[] deleted = DeletedVariances(fit);
        for (int i = 0; i < n; i++)
        {
            double room = 1 - fit.Leverage[i];
            if (room <= 1e-12 || dfe <= 1)
            {
                residualLower[i] = double.NaN;
                residualUpper[i] = double.NaN;
                continue;
            }

            double spread = critical * Math.Sqrt(Math.Max(0, deleted[i]) * room);
            residualLower[i] = fit.Residuals[i] - spread;
            residualUpper[i] = fit.Residuals[i] + spread;
        }

        (double rSquare, double f, double probability) = ModelTest(y, fit, design);
        return new Regression(
            fit.Coefficients, lower, upper, fit.Residuals, residualLower, residualUpper,
            rSquare, f, probability, fit.MeanSquaredError);
    }

    /// <summary>Every quantity <c>regstats</c> reports, from one fit of <paramref name="design"/>.</summary>
    public static Diagnostics Describe(double[] y, double[,] design)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(design);

        LeastSquares.Fit fit = LeastSquares.Solve(design, y);
        int n = y.Length;
        int k = design.GetLength(1);
        int dfe = fit.Df;
        double[] deleted = DeletedVariances(fit);

        var hat = new double[n, n];
        for (int a = 0; a < n; a++)
        {
            double[] rowA = LeastSquares.Row(design, a);
            for (int b = 0; b < n; b++)
            {
                double[] rowB = LeastSquares.Row(design, b);
                double value = 0;
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        value += rowA[i] * fit.CrossInverse[i, j] * rowB[j];
                    }
                }

                hat[a, b] = value;
            }
        }

        var deletedCoefficients = new double[n, k];
        var dfBetas = new double[n, k];
        var standardized = new double[n];
        var studentized = new double[n];
        var dfFit = new double[n];
        var dfFits = new double[n];
        var covarianceRatio = new double[n];
        var cook = new double[n];
        double s = Math.Sqrt(fit.MeanSquaredError);
        for (int i = 0; i < n; i++)
        {
            double room = Math.Max(1e-300, 1 - fit.Leverage[i]);
            double scaled = fit.Residuals[i] / room;
            for (int j = 0; j < k; j++)
            {
                double shift = 0;
                for (int c = 0; c < k; c++)
                {
                    shift += fit.CrossInverse[j, c] * design[i, c];
                }

                deletedCoefficients[i, j] = fit.Coefficients[j] - (shift * scaled);
                dfBetas[i, j] = (fit.Coefficients[j] - deletedCoefficients[i, j])
                    / Math.Max(1e-300, Math.Sqrt(deleted[i] * fit.CrossInverse[j, j]));
            }

            standardized[i] = fit.Residuals[i] / Math.Max(1e-300, s * Math.Sqrt(room));
            studentized[i] = fit.Residuals[i] / Math.Max(1e-300, Math.Sqrt(deleted[i] * room));
            dfFit[i] = fit.Leverage[i] * scaled;
            dfFits[i] = studentized[i] * Math.Sqrt(fit.Leverage[i] / room);
            covarianceRatio[i] = Math.Pow(deleted[i] / Math.Max(1e-300, fit.MeanSquaredError), k) / room;
            cook[i] = fit.Residuals[i] * fit.Residuals[i] * fit.Leverage[i]
                / Math.Max(1e-300, k * fit.MeanSquaredError * room * room);
        }

        var standardErrors = new double[k];
        var t = new double[k];
        var tProbability = new double[k];
        for (int j = 0; j < k; j++)
        {
            standardErrors[j] = Math.Sqrt(Math.Max(0, fit.Covariance[j, j]));
            t[j] = standardErrors[j] > 0 ? fit.Coefficients[j] / standardErrors[j] : double.NaN;
            tProbability[j] = dfe > 0 && double.IsFinite(t[j])
                ? 2 * ContinuousDistributions.TCdf(-Math.Abs(t[j]), dfe)
                : double.NaN;
        }

        (double rSquare, double f, double probability) = ModelTest(y, fit, design);
        double regressionSum = TotalSumOfSquares(y, design) - fit.ResidualSumOfSquares;
        double adjusted = dfe > 0 && n > 1
            ? 1 - ((1 - rSquare) * (n - (LeastSquares.HasConstantColumn(design) ? 1 : 0)) / dfe)
            : double.NaN;

        LinearModelTests.SerialCorrelation serial =
            LinearModelTests.DurbinWatson(fit.Residuals, design, false, Tail.Both);

        return new Diagnostics(
            fit, hat, deleted, deletedCoefficients, standardized, studentized, dfBetas, dfFit, dfFits,
            covarianceRatio, cook, standardErrors, t, tProbability, regressionSum, f, probability,
            rSquare, adjusted, serial.D, serial.P);
    }

    /// <summary><c>leverage</c>: the hat matrix's diagonal, how much each observation pulls its own fit.</summary>
    public static double[] Leverage(double[,] design)
    {
        ArgumentNullException.ThrowIfNull(design);

        var zero = new double[design.GetLength(0)];
        return LeastSquares.Solve(design, zero).Leverage;
    }

    /// <summary>
    /// <c>ridge</c>: the coefficients that least squares would give if the sum of their squares were
    /// penalized. Each ridge parameter gets its own column of coefficients.
    /// </summary>
    /// <param name="y">The response.</param>
    /// <param name="predictors">The predictors, with no intercept column.</param>
    /// <param name="parameters">The ridge parameters to fit at.</param>
    /// <param name="scaled">
    /// Whether to leave the coefficients on the standardized scale, which is what makes them
    /// comparable across predictors, or restore them to the original scale with an intercept in front.
    /// </param>
    public static double[,] Ridge(
        double[] y, double[,] predictors, IReadOnlyList<double> parameters, bool scaled)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(parameters);

        int n = predictors.GetLength(0);
        int k = predictors.GetLength(1);
        if (y.Length != n)
        {
            throw new ArgumentException(
                $"the response has {y.Length} values but the predictors have {n} rows.", nameof(y));
        }

        if (n <= 1)
        {
            throw new ArgumentException("a ridge fit needs more than one observation.");
        }

        var means = new double[k];
        var deviations = new double[k];
        for (int c = 0; c < k; c++)
        {
            double sum = 0;
            for (int r = 0; r < n; r++)
            {
                sum += predictors[r, c];
            }

            means[c] = sum / n;
            double squares = 0;
            for (int r = 0; r < n; r++)
            {
                double gap = predictors[r, c] - means[c];
                squares += gap * gap;
            }

            deviations[c] = Math.Sqrt(squares / (n - 1));
            if (deviations[c] < 1.4901161193847656e-08)
            {
                deviations[c] = 1;
            }
        }

        double responseMean = 0;
        foreach (double value in y)
        {
            responseMean += value;
        }

        responseMean /= n;

        // The penalty is imposed by appending pseudo-observations whose design is √k·I and whose
        // response is zero: least squares over the enlarged problem is exactly the ridge solution, so
        // there is no second solver here.
        var answer = new double[scaled ? k : k + 1, parameters.Count];
        for (int p = 0; p < parameters.Count; p++)
        {
            double lambda = parameters[p];
            if (lambda < 0)
            {
                throw new ArgumentException("a ridge parameter cannot be negative.");
            }

            var augmented = new double[n + k, k];
            var response = new double[n + k];
            for (int r = 0; r < n; r++)
            {
                response[r] = y[r] - responseMean;
                for (int c = 0; c < k; c++)
                {
                    augmented[r, c] = (predictors[r, c] - means[c]) / deviations[c];
                }
            }

            for (int c = 0; c < k; c++)
            {
                augmented[n + c, c] = Math.Sqrt(lambda);
            }

            double[] coefficients = LeastSquares.Solve(augmented, response).Coefficients;
            if (scaled)
            {
                for (int c = 0; c < k; c++)
                {
                    answer[c, p] = coefficients[c];
                }

                continue;
            }

            double intercept = responseMean;
            for (int c = 0; c < k; c++)
            {
                double original = coefficients[c] / deviations[c];
                answer[c + 1, p] = original;
                intercept -= means[c] * original;
            }

            answer[0, p] = intercept;
        }

        return answer;
    }

    /// <summary>
    /// <c>invpred</c>: the value of <c>x</c> at which a straight-line fit predicts <c>y0</c>, and the
    /// interval around it.
    /// </summary>
    /// <param name="x">The predictor.</param>
    /// <param name="y">The response.</param>
    /// <param name="y0">The response to invert.</param>
    /// <param name="alpha">One less the confidence level.</param>
    /// <param name="observation">
    /// Whether the interval covers a new observation at <c>x0</c> rather than the line's own position
    /// there. It is the wider of the two, and it is MathWorks' default.
    /// </param>
    /// <returns>The estimate and the two ends of its interval, either of which may be infinite.</returns>
    public static (double X0, double Lower, double Upper) InversePrediction(
        IReadOnlyList<double> x, IReadOnlyList<double> y, double y0, double alpha, bool observation)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        CheckLevel(alpha);

        int n = x.Count;
        if (n != y.Count)
        {
            throw new ArgumentException("the predictor and the response must be the same length.");
        }

        if (n < 3)
        {
            throw new ArgumentException("an inverse prediction needs at least three observations.");
        }

        double meanX = 0, meanY = 0;
        for (int i = 0; i < n; i++)
        {
            meanX += x[i];
            meanY += y[i];
        }

        meanX /= n;
        meanY /= n;

        double sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            sxx += (x[i] - meanX) * (x[i] - meanX);
            sxy += (x[i] - meanX) * (y[i] - meanY);
        }

        if (sxx <= 0)
        {
            throw new ArgumentException("the predictor never varies, so nothing can be inverted.");
        }

        double slope = sxy / sxx;
        double intercept = meanY - (slope * meanX);
        if (slope == 0)
        {
            throw new ArgumentException("the fitted line is flat, so no value of x predicts y0.");
        }

        double residual = 0;
        for (int i = 0; i < n; i++)
        {
            double gap = y[i] - intercept - (slope * x[i]);
            residual += gap * gap;
        }

        int dfe = n - 2;
        double variance = residual / dfe;
        double x0 = (y0 - intercept) / slope;

        // Fieller's interval. When the slope is not clearly non-zero the ratio's interval is the whole
        // line with a hole in it, which reports as unbounded rather than as a narrow wrong answer.
        double critical = ContinuousDistributions.TInv(1 - (alpha / 2), dfe);
        double g = critical * critical * variance / (slope * slope * sxx);
        double extra = observation ? 1 : 0;
        if (g >= 1)
        {
            return (x0, double.NegativeInfinity, double.PositiveInfinity);
        }

        double gap0 = x0 - meanX;
        double inside = ((1 - g) * (extra + (1.0 / n))) + (gap0 * gap0 / sxx);
        double centre = meanX + (gap0 / (1 - g));
        double half = critical * Math.Sqrt(variance) * Math.Sqrt(Math.Max(0, inside))
            / (Math.Abs(slope) * (1 - g));
        return (x0, centre - half, centre + half);
    }

    /// <summary>
    /// <c>polyconf</c>: how far the interval around a polynomial reaches at each point, given the
    /// triangular factor and residual a fit recorded.
    /// </summary>
    /// <param name="triangular">The fit's upper triangular factor, one row per coefficient.</param>
    /// <param name="df">The fit's residual degrees of freedom.</param>
    /// <param name="residualNorm">The length of its residual vector.</param>
    /// <param name="rows">The design row at each point the interval is wanted at.</param>
    /// <param name="alpha">One less the confidence level.</param>
    /// <param name="observation">
    /// Whether the interval covers a new observation, which adds the observation's own variance, or
    /// only the fitted curve.
    /// </param>
    /// <param name="simultaneous">
    /// Whether the interval must hold at every point at once. That is Scheffé's multiplier, which
    /// grows with the number of coefficients rather than staying at a single point's <c>t</c>.
    /// </param>
    public static double[] PolynomialInterval(
        double[,] triangular,
        double df,
        double residualNorm,
        double[,] rows,
        double alpha,
        bool observation,
        bool simultaneous)
    {
        ArgumentNullException.ThrowIfNull(triangular);
        ArgumentNullException.ThrowIfNull(rows);
        CheckLevel(alpha);

        int terms = triangular.GetLength(0);
        if (triangular.GetLength(1) != terms || rows.GetLength(1) != terms)
        {
            throw new ArgumentException("the triangular factor does not match the number of coefficients.");
        }

        int count = rows.GetLength(0);
        var spread = new double[count];
        if (df <= 0)
        {
            Array.Fill(spread, double.PositiveInfinity);
            return spread;
        }

        double s = residualNorm / Math.Sqrt(df);
        double multiplier = simultaneous
            ? Math.Sqrt(terms * ContinuousDistributions.FInv(1 - alpha, terms, df))
            : ContinuousDistributions.TInv(1 - (alpha / 2), df);

        for (int r = 0; r < count; r++)
        {
            // Solve eᵀ·R = xᵀ by forward substitution; ‖e‖² is then xᵀ(XᵀX)⁻¹x without ever forming
            // the cross-product.
            var e = new double[terms];
            double sum = observation ? 1 : 0;
            for (int i = 0; i < terms; i++)
            {
                double known = 0;
                for (int j = 0; j < i; j++)
                {
                    known += triangular[j, i] * e[j];
                }

                e[i] = (rows[r, i] - known) / triangular[i, i];
                sum += e[i] * e[i];
            }

            spread[r] = multiplier * s * Math.Sqrt(Math.Max(0, sum));
        }

        return spread;
    }

    /// <summary>The error variance re-estimated with each observation left out in turn.</summary>
    public static double[] DeletedVariances(LeastSquares.Fit fit)
    {
        int n = fit.Residuals.Length;
        var deleted = new double[n];
        int dfe = fit.Df;
        for (int i = 0; i < n; i++)
        {
            double room = 1 - fit.Leverage[i];
            if (dfe <= 1 || room <= 1e-12)
            {
                deleted[i] = double.NaN;
                continue;
            }

            deleted[i] = Math.Max(
                0,
                ((dfe * fit.MeanSquaredError) - (fit.Residuals[i] * fit.Residuals[i] / room)) / (dfe - 1));
        }

        return deleted;
    }

    /// <summary>The whole model against a constant one — or against nothing, where it has no intercept.</summary>
    private static (double RSquare, double F, double P) ModelTest(
        double[] y, LeastSquares.Fit fit, double[,] design)
    {
        bool constant = LeastSquares.HasConstantColumn(design);
        double total = TotalSumOfSquares(y, design);
        double rSquare = total > 0 ? 1 - (fit.ResidualSumOfSquares / total) : double.NaN;

        int numerator = fit.Rank - (constant ? 1 : 0);
        if (numerator <= 0 || fit.Df <= 0 || fit.MeanSquaredError <= 0)
        {
            return (rSquare, double.NaN, double.NaN);
        }

        double f = (total - fit.ResidualSumOfSquares) / numerator / fit.MeanSquaredError;
        return (rSquare, f, 1 - ContinuousDistributions.FCdf(f, numerator, fit.Df));
    }

    /// <summary>
    /// The variation to be accounted for: about the mean where the model has an intercept, about zero
    /// where it does not, because a model with no intercept is not entitled to claim the mean.
    /// </summary>
    private static double TotalSumOfSquares(double[] y, double[,] design)
    {
        bool constant = LeastSquares.HasConstantColumn(design);
        double centre = 0;
        if (constant)
        {
            foreach (double value in y)
            {
                centre += value;
            }

            centre /= y.Length;
        }

        double total = 0;
        foreach (double value in y)
        {
            total += (value - centre) * (value - centre);
        }

        return total;
    }

    /// <summary>Refuses a confidence level outside the open unit interval.</summary>
    internal static void CheckLevel(double alpha)
    {
        if (!(alpha > 0 && alpha < 1))
        {
            throw new ArgumentException($"the significance level must be between 0 and 1, and {alpha} is not.");
        }
    }
}
