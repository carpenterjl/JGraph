using JGraph.Statistics.Distributions;
using JGraph.Statistics.Optimize;

namespace JGraph.Statistics.Regression;

/// <summary>
/// <c>nlinfit</c> and the two intervals that follow it, <c>nlparci</c> and <c>nlpredci</c>, plus the
/// reaction-rate model <c>hougen</c> that the documentation fits as its example.
/// </summary>
/// <remarks>
/// A nonlinear fit has no closed form, but everything after it does: once the search has stopped, the
/// model is treated as if it were linear in its parameters at that point, and the Jacobian there plays
/// the part the design matrix plays in an ordinary fit. Every standard error, every confidence
/// interval and every prediction band below is the linear formula with the Jacobian substituted in —
/// which is also the approximation's limit, and why an interval from a badly curved model is
/// optimistic. That is recorded rather than corrected.
/// </remarks>
public static class NonlinearRegression
{
    /// <summary>What a nonlinear fit produced.</summary>
    /// <param name="Coefficients">The fitted parameters.</param>
    /// <param name="Residuals">Observed less fitted.</param>
    /// <param name="Jacobian">The model's derivatives at the answer, one row per observation.</param>
    /// <param name="Covariance">The parameters' covariance.</param>
    /// <param name="MeanSquaredError">The residual variance.</param>
    /// <param name="Df">Observations less parameters.</param>
    /// <param name="Weights">The weight each observation carried, robust reweighting included.</param>
    /// <param name="Converged">Whether the search settled before its budget ran out.</param>
    /// <param name="Iterations">Iterations taken.</param>
    public readonly record struct NonlinearFit(
        double[] Coefficients,
        double[] Residuals,
        double[,] Jacobian,
        double[,] Covariance,
        double MeanSquaredError,
        int Df,
        double[] Weights,
        bool Converged,
        int Iterations);

    /// <summary><c>nlinfit</c>: the parameters that bring a model closest to the data.</summary>
    /// <param name="model">The predicted response at a set of parameters.</param>
    /// <param name="y">The observed response.</param>
    /// <param name="start">The parameters to search from.</param>
    /// <param name="weights">A weight for each observation, or null for one each.</param>
    /// <param name="robust">A weight function to reject outliers with, or null to fit them all.</param>
    /// <param name="tuning">That function's tuning constant, or zero for its default.</param>
    /// <param name="settings">The search's budget and tolerances.</param>
    public static NonlinearFit Fit(
        Func<double[], double[]> model,
        double[] y,
        double[] start,
        double[]? weights,
        RobustWeight? robust,
        double tuning,
        LevenbergMarquardt.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(start);

        int n = y.Length;
        var carried = new double[n];
        for (int i = 0; i < n; i++)
        {
            carried[i] = weights is null ? 1 : weights[i];
            if (carried[i] < 0)
            {
                throw new ArgumentException("an observation's weight cannot be negative.");
            }
        }

        double[] Residuals(double[] beta)
        {
            double[] predicted = model(beta);
            if (predicted.Length != n)
            {
                throw new ArgumentException(
                    $"the model answered {predicted.Length} values for {n} observations.");
            }

            var gaps = new double[n];
            for (int i = 0; i < n; i++)
            {
                gaps[i] = Math.Sqrt(carried[i]) * (y[i] - predicted[i]);
            }

            return gaps;
        }

        // How far each observation really is from the fit, which is not what the search carries: the
        // search's residual is already multiplied by the square root of the weight, so an observation
        // the previous round gave up on would look perfectly fitted and get its weight straight back.
        double[] Raw(double[] beta)
        {
            double[] predicted = model(beta);
            var gaps = new double[n];
            for (int i = 0; i < n; i++)
            {
                gaps[i] = y[i] - predicted[i];
            }

            return gaps;
        }

        LevenbergMarquardt.Result result = LevenbergMarquardt.Minimize(Residuals, start, settings);
        int rounds = robust is null ? 1 : 25;
        for (int round = 1; round < rounds; round++)
        {
            // Robust fitting is the same search run again with each observation weighted by how well
            // the previous answer already explained it — the linear case's loop, around a nonlinear
            // solver instead of a linear one.
            double[] raw = Raw(result.Solution);
            double scale = MedianScale(raw, start.Length);
            if (scale <= 0)
            {
                break;
            }

            double constant = tuning > 0 ? tuning : RobustRegression.DefaultTuning(robust!.Value);
            var updated = new double[n];
            double movement = 0;
            for (int i = 0; i < n; i++)
            {
                updated[i] = RobustRegression.Weigh(robust!.Value, raw[i] / (scale * constant));
                movement = Math.Max(movement, Math.Abs(updated[i] - carried[i]));
            }

            for (int i = 0; i < n; i++)
            {
                carried[i] = (weights is null ? 1 : weights[i]) * updated[i];
            }

            result = LevenbergMarquardt.Minimize(Residuals, result.Solution, settings);
            if (movement <= 1e-8)
            {
                break;
            }
        }

        int k = start.Length;
        int df = Math.Max(0, n - k);
        double mse = df > 0 ? result.SumOfSquares / df : 0;

        // The Jacobian the search carries is of the weighted residual; the reported one is of the
        // model, which is what every interval below wants.
        var jacobian = new double[n, k];
        for (int i = 0; i < n; i++)
        {
            double root = Math.Sqrt(Math.Max(1e-300, carried[i]));
            for (int a = 0; a < k; a++)
            {
                jacobian[i, a] = -result.Jacobian[i, a] / root;
            }
        }

        double[,] covariance = Covariance(result.Jacobian, mse);
        return new NonlinearFit(
            result.Solution, Raw(result.Solution), jacobian, covariance, mse, df,
            carried, result.Converged, result.Iterations);
    }

    /// <summary><c>nlparci</c>: an interval around each parameter, from the fit's covariance.</summary>
    public static (double[] Lower, double[] Upper) ParameterInterval(
        double[] beta, double[,] covariance, int df, double alpha)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(covariance);
        LinearRegression.CheckLevel(alpha);

        double critical = df > 0
            ? ContinuousDistributions.TInv(1 - (alpha / 2), df)
            : ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1);

        var lower = new double[beta.Length];
        var upper = new double[beta.Length];
        for (int a = 0; a < beta.Length; a++)
        {
            double spread = critical * Math.Sqrt(Math.Max(0, covariance[a, a]));
            lower[a] = beta[a] - spread;
            upper[a] = beta[a] + spread;
        }

        return (lower, upper);
    }

    /// <summary><c>nlpredci</c>: how far the interval around the fitted curve reaches at each new row.</summary>
    /// <param name="jacobian">The model's derivatives at the rows the prediction is wanted at.</param>
    /// <param name="covariance">The parameters' covariance from the fit.</param>
    /// <param name="mse">The fit's residual variance.</param>
    /// <param name="df">Its residual degrees of freedom.</param>
    /// <param name="alpha">One less the confidence level.</param>
    /// <param name="observation">Whether the interval covers a new observation rather than the curve.</param>
    /// <param name="simultaneous">Whether it must hold at every row at once.</param>
    public static double[] PredictionInterval(
        double[,] jacobian,
        double[,] covariance,
        double mse,
        int df,
        double alpha,
        bool observation,
        bool simultaneous)
    {
        ArgumentNullException.ThrowIfNull(jacobian);
        ArgumentNullException.ThrowIfNull(covariance);
        LinearRegression.CheckLevel(alpha);

        int n = jacobian.GetLength(0);
        int k = jacobian.GetLength(1);
        double critical = simultaneous
            ? Math.Sqrt(k * (df > 0
                ? ContinuousDistributions.FInv(1 - alpha, k, df)
                : ContinuousDistributions.Chi2Inv(1 - alpha, k) / k))
            : df > 0
                ? ContinuousDistributions.TInv(1 - (alpha / 2), df)
                : ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1);

        var delta = new double[n];
        for (int r = 0; r < n; r++)
        {
            double[] row = LeastSquares.Row(jacobian, r);
            double variance = LeastSquares.PredictionVariance(covariance, row);
            delta[r] = critical * Math.Sqrt(Math.Max(0, variance + (observation ? mse : 0)));
        }

        return delta;
    }

    /// <summary>The Jacobian of a model at a set of parameters, by forward differences.</summary>
    public static double[,] Jacobian(Func<double[], double[]> model, double[] beta)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(beta);

        double[] at = model(beta);
        int n = at.Length;
        int k = beta.Length;
        var jacobian = new double[n, k];
        for (int a = 0; a < k; a++)
        {
            double step = 1.4901161193847656e-08 * Math.Max(Math.Abs(beta[a]), 1e-3);
            var moved = (double[])beta.Clone();
            moved[a] = beta[a] + step;
            double actual = moved[a] - beta[a];
            double[] shifted = model(moved);
            for (int i = 0; i < n; i++)
            {
                jacobian[i, a] = (shifted[i] - at[i]) / actual;
            }
        }

        return jacobian;
    }

    /// <summary>
    /// <c>hougen</c>: the Hougen–Watson reaction rate, in the parameters and the three partial
    /// pressures the documentation names.
    /// </summary>
    public static double Hougen(IReadOnlyList<double> beta, IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(beta);
        ArgumentNullException.ThrowIfNull(x);
        if (beta.Count != 5)
        {
            throw new ArgumentException($"the model takes five parameters, and {beta.Count} were given.");
        }

        if (x.Count != 3)
        {
            throw new ArgumentException($"the model takes three predictors, and {x.Count} were given.");
        }

        return ((beta[0] * x[1]) - (x[2] / beta[4]))
            / (1 + (beta[1] * x[0]) + (beta[2] * x[1]) + (beta[3] * x[2]));
    }

    /// <summary>The covariance the linearization implies: the residual variance times <c>(JᵀJ)⁻¹</c>.</summary>
    private static double[,] Covariance(double[,] jacobian, double mse)
    {
        int n = jacobian.GetLength(0);
        int k = jacobian.GetLength(1);
        var zero = new double[n];
        LeastSquares.Fit fit = LeastSquares.Solve(jacobian, zero);
        var covariance = new double[k, k];
        for (int a = 0; a < k; a++)
        {
            for (int b = 0; b < k; b++)
            {
                covariance[a, b] = mse * fit.CrossInverse[a, b];
            }
        }

        return covariance;
    }

    /// <summary>The median absolute residual over 0.6745, skipping the exact fits the parameters buy.</summary>
    private static double MedianScale(double[] residuals, int parameters)
    {
        var sorted = new double[residuals.Length];
        for (int i = 0; i < residuals.Length; i++)
        {
            sorted[i] = Math.Abs(residuals[i]);
        }

        Array.Sort(sorted);
        int from = Math.Min(Math.Max(0, parameters - 1), sorted.Length - 1);
        int count = sorted.Length - from;
        double median = count % 2 == 1
            ? sorted[from + (count / 2)]
            : (sorted[from + (count / 2) - 1] + sorted[from + (count / 2)]) / 2;
        return median / 0.6745;
    }
}
