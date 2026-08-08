namespace JGraph.Statistics.Regression;

/// <summary>
/// <c>lasso</c> and <c>lassoglm</c>: least squares — or a generalized linear fit — with the size of the
/// coefficients themselves added to what is being minimized.
/// </summary>
/// <remarks>
/// <para>
/// The penalty is what makes this worth a solver of its own. Penalizing the sum of squared
/// coefficients only shrinks them; penalizing the sum of their absolute values drives most of them to
/// exactly zero, so the fit chooses its own predictors. The elastic net mixes the two, and the mixing
/// fraction is the one option that changes the character of the answer rather than its accuracy.
/// </para>
/// <para>
/// Because the penalty is not differentiable at zero there is no normal equation to solve, but there
/// is something better: with every other coefficient held still, the one being updated has a closed
/// form — soft-thresholding — so a sweep of coordinate updates costs one pass over the data. The whole
/// path of penalties is fitted from the largest down, each one starting from the answer before it, and
/// that warm start is what makes a hundred fits cost barely more than a few.
/// </para>
/// <para>
/// The generalized fit is the same routine wrapped in the reweighting loop from
/// <see cref="GeneralizedLinear"/>: the response is replaced by its value on the linear predictor's
/// scale and the penalized least squares is run against that. Nothing about the penalty changes.
/// </para>
/// </remarks>
public static class PenalizedRegression
{
    /// <summary>A whole path of penalized fits.</summary>
    /// <param name="Coefficients">One column per penalty, one row per predictor.</param>
    /// <param name="Intercepts">The intercept at each penalty.</param>
    /// <param name="Lambda">The penalties, ascending.</param>
    /// <param name="Df">How many coefficients are non-zero at each.</param>
    /// <param name="Criterion">The mean squared error, or the deviance, at each.</param>
    public readonly record struct Path(
        double[,] Coefficients,
        double[] Intercepts,
        double[] Lambda,
        int[] Df,
        double[] Criterion);

    /// <summary>How the penalties are chosen when the caller does not name them.</summary>
    /// <param name="Count">How many to fit at.</param>
    /// <param name="Ratio">The smallest as a fraction of the largest.</param>
    public readonly record struct PathPlan(int Count = 100, double Ratio = 1e-4);

    /// <summary><c>lasso</c>: the elastic-net path for a squared-error loss.</summary>
    /// <param name="predictors">One row per observation, one column per predictor; no intercept column.</param>
    /// <param name="y">The response.</param>
    /// <param name="mixing">One for the lasso, zero for a ridge, in between for the elastic net.</param>
    /// <param name="lambda">The penalties to fit at, or null to choose them.</param>
    /// <param name="plan">How to choose them, where they were not given.</param>
    /// <param name="standardize">Whether to put every predictor on the same scale before penalizing.</param>
    /// <param name="weights">A weight for each observation, or null for equal weights.</param>
    /// <param name="tolerance">How small a sweep's largest change must be to stop.</param>
    /// <param name="maximumDf">Stop once this many coefficients are non-zero, or zero for no limit.</param>
    public static Path Fit(
        double[,] predictors,
        double[] y,
        double mixing,
        IReadOnlyList<double>? lambda,
        PathPlan plan,
        bool standardize,
        double[]? weights,
        double tolerance,
        int maximumDf)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(y);
        CheckMixing(mixing);

        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        double[] w = NormalizedWeights(weights, n);
        (double[,] scaled, double[] centres, double[] scales) = Standardize(predictors, w, standardize);

        double responseMean = 0;
        for (int i = 0; i < n; i++)
        {
            responseMean += w[i] * y[i];
        }

        var centred = new double[n];
        for (int i = 0; i < n; i++)
        {
            centred[i] = y[i] - responseMean;
        }

        double[] penalties = lambda is not null
            ? Ascending(lambda)
            : Sequence(LargestPenalty(scaled, centred, w, mixing), plan);

        return Walk(
            scaled, centred, w, penalties, mixing, tolerance, maximumDf, centres, scales, responseMean,
            (fitted, intercept) =>
            {
                double total = 0;
                for (int i = 0; i < n; i++)
                {
                    double gap = centred[i] - intercept - fitted[i];
                    total += w[i] * gap * gap;
                }

                return total;
            },
            p);
    }

    /// <summary><c>lassoglm</c>: the same path, for a response the squared-error loss does not describe.</summary>
    /// <param name="predictors">One row per observation, one column per predictor; no intercept column.</param>
    /// <param name="y">The response — a proportion, where the family is binomial.</param>
    /// <param name="family">The error distribution.</param>
    /// <param name="link">Which function relates the mean to the linear predictor.</param>
    /// <param name="power">Its exponent, where the link is a power.</param>
    /// <param name="mixing">One for the lasso, zero for a ridge, in between for the elastic net.</param>
    /// <param name="lambda">The penalties to fit at, or null to choose them.</param>
    /// <param name="plan">How to choose them, where they were not given.</param>
    /// <param name="standardize">Whether to put every predictor on the same scale before penalizing.</param>
    /// <param name="trials">Trials behind each binomial proportion, or null for one each.</param>
    /// <param name="offset">A term added to the linear predictor and not fitted, or null for none.</param>
    /// <param name="tolerance">How small a sweep's largest change must be to stop.</param>
    /// <param name="maximumDf">Stop once this many coefficients are non-zero, or zero for no limit.</param>
    public static Path FitGeneralized(
        double[,] predictors,
        double[] y,
        GlmFamily family,
        GlmLink link,
        double power,
        double mixing,
        IReadOnlyList<double>? lambda,
        PathPlan plan,
        bool standardize,
        double[]? trials,
        double[]? offset,
        double tolerance,
        int maximumDf)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(y);
        CheckMixing(mixing);

        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        var equal = new double[n];
        Array.Fill(equal, 1.0 / n);
        (double[,] scaled, double[] centres, double[] scales) = Standardize(predictors, equal, standardize);

        var counts = new double[n];
        var shift = new double[n];
        for (int i = 0; i < n; i++)
        {
            counts[i] = trials is null ? 1 : trials[i];
            shift[i] = offset is null ? 0 : offset[i];
        }

        // The null model, which is where the largest useful penalty and every warm start come from.
        double nullMean = 0;
        double weightSum = 0;
        for (int i = 0; i < n; i++)
        {
            nullMean += counts[i] * y[i];
            weightSum += counts[i];
        }

        nullMean /= weightSum;
        double nullEta = GeneralizedLinear.Link(link, power, Bounded(family, nullMean));

        var working = new double[n];
        var reweights = new double[n];
        Reweight(family, link, power, n, counts, shift, y, _ => nullEta, working, reweights);
        double[] penalties = lambda is not null
            ? Ascending(lambda)
            : Sequence(LargestPenalty(scaled, Recentred(working, reweights), reweights, mixing), plan);

        var coefficients = new double[p, penalties.Length];
        var intercepts = new double[penalties.Length];
        var degrees = new int[penalties.Length];
        var criterion = new double[penalties.Length];

        var beta = new double[p];
        double b0 = nullEta;
        for (int index = penalties.Length - 1; index >= 0; index--)
        {
            double penalty = penalties[index];
            for (int outer = 0; outer < 100; outer++)
            {
                double[] carried = beta;
                double carriedIntercept = b0;
                Reweight(
                    family, link, power, n, counts, shift, y,
                    i => carriedIntercept + Dot(scaled, i, carried), working, reweights);

                double total = 0;
                for (int i = 0; i < n; i++)
                {
                    total += reweights[i];
                }

                if (total <= 0)
                {
                    break;
                }

                var normalized = new double[n];
                for (int i = 0; i < n; i++)
                {
                    normalized[i] = reweights[i] / total;
                }

                double centre = 0;
                for (int i = 0; i < n; i++)
                {
                    centre += normalized[i] * working[i];
                }

                var target = new double[n];
                for (int i = 0; i < n; i++)
                {
                    target[i] = working[i] - centre;
                }

                var next = (double[])beta.Clone();
                double nextIntercept = Descend(scaled, target, normalized, next, penalty, mixing, tolerance);
                double movement = Math.Abs(nextIntercept + centre - b0);
                for (int j = 0; j < p; j++)
                {
                    movement = Math.Max(movement, Math.Abs(next[j] - beta[j]));
                }

                beta = next;
                b0 = nextIntercept + centre;
                if (movement <= tolerance)
                {
                    break;
                }
            }

            var mu = new double[n];
            for (int i = 0; i < n; i++)
            {
                mu[i] = Bounded(family, GeneralizedLinear.Inverse(link, power, shift[i] + b0 + Dot(scaled, i, beta)));
            }

            var ones = new double[n];
            Array.Fill(ones, 1.0);
            criterion[index] = GeneralizedLinear.Deviance(family, y, mu, counts, ones);
            Record(coefficients, intercepts, degrees, index, beta, b0, centres, scales, 0);
            if (maximumDf > 0 && degrees[index] > maximumDf)
            {
                break;
            }
        }

        return new Path(coefficients, intercepts, penalties, degrees, criterion);
    }

    /// <summary>Walks a path of penalties from the largest down, each fit starting from the one before.</summary>
    private static Path Walk(
        double[,] scaled,
        double[] centred,
        double[] w,
        double[] penalties,
        double mixing,
        double tolerance,
        int maximumDf,
        double[] centres,
        double[] scales,
        double responseMean,
        Func<double[], double, double> loss,
        int p)
    {
        int n = scaled.GetLength(0);
        var coefficients = new double[p, penalties.Length];
        var intercepts = new double[penalties.Length];
        var degrees = new int[penalties.Length];
        var criterion = new double[penalties.Length];

        var beta = new double[p];
        for (int index = penalties.Length - 1; index >= 0; index--)
        {
            double intercept = Descend(scaled, centred, w, beta, penalties[index], mixing, tolerance);
            var fitted = new double[n];
            for (int i = 0; i < n; i++)
            {
                fitted[i] = Dot(scaled, i, beta);
            }

            criterion[index] = loss(fitted, intercept);
            Record(coefficients, intercepts, degrees, index, beta, intercept + responseMean, centres, scales, 0);
            if (maximumDf > 0 && degrees[index] > maximumDf)
            {
                break;
            }
        }

        return new Path(coefficients, intercepts, penalties, degrees, criterion);
    }

    /// <summary>
    /// Cyclic coordinate descent on the standardized problem. Each coefficient in turn is given the
    /// value it would take alone, soft-thresholded by the penalty, until a whole sweep barely moves
    /// anything.
    /// </summary>
    /// <returns>The intercept on the centred response's scale.</returns>
    private static double Descend(
        double[,] x,
        double[] y,
        double[] w,
        double[] beta,
        double penalty,
        double mixing,
        double tolerance)
    {
        int n = x.GetLength(0);
        int p = x.GetLength(1);
        var residual = new double[n];
        double intercept = 0;
        for (int i = 0; i < n; i++)
        {
            residual[i] = y[i] - Dot(x, i, beta);
            intercept += w[i] * residual[i];
        }

        for (int i = 0; i < n; i++)
        {
            residual[i] -= intercept;
        }

        for (int sweep = 0; sweep < 1000; sweep++)
        {
            double largest = 0;
            for (int j = 0; j < p; j++)
            {
                double gradient = 0, curvature = 0;
                for (int i = 0; i < n; i++)
                {
                    gradient += w[i] * x[i, j] * residual[i];
                    curvature += w[i] * x[i, j] * x[i, j];
                }

                if (curvature <= 0)
                {
                    continue;
                }

                double whole = gradient + (curvature * beta[j]);
                double threshold = penalty * mixing;
                double magnitude = Math.Abs(whole) - threshold;
                double updated = magnitude <= 0
                    ? 0
                    : Math.Sign(whole) * magnitude / (curvature + (penalty * (1 - mixing)));

                double change = updated - beta[j];
                if (change == 0)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    residual[i] -= change * x[i, j];
                }

                beta[j] = updated;
                largest = Math.Max(largest, Math.Abs(change) * Math.Sqrt(curvature));
            }

            // The intercept is not penalized, so it is simply the weighted mean of what is left.
            double drift = 0;
            for (int i = 0; i < n; i++)
            {
                drift += w[i] * residual[i];
            }

            if (drift != 0)
            {
                for (int i = 0; i < n; i++)
                {
                    residual[i] -= drift;
                }

                intercept += drift;
                largest = Math.Max(largest, Math.Abs(drift));
            }

            if (largest <= tolerance)
            {
                break;
            }
        }

        return intercept;
    }

    /// <summary>The smallest penalty at which every coefficient is still zero.</summary>
    private static double LargestPenalty(double[,] x, double[] y, double[] w, double mixing)
    {
        int n = x.GetLength(0);
        int p = x.GetLength(1);
        double centre = 0;
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            centre += w[i] * y[i];
            total += w[i];
        }

        centre = total > 0 ? centre / total : 0;

        double largest = 0;
        for (int j = 0; j < p; j++)
        {
            double gradient = 0;
            for (int i = 0; i < n; i++)
            {
                gradient += w[i] * x[i, j] * (y[i] - centre);
            }

            largest = Math.Max(largest, Math.Abs(gradient));
        }

        double bound = largest / Math.Max(mixing, 1e-3);
        return bound > 0 ? bound : 1;
    }

    /// <summary>Penalties spaced evenly in the logarithm, from a fraction of the largest up to it.</summary>
    private static double[] Sequence(double largest, PathPlan plan)
    {
        int count = plan.Count > 0 ? plan.Count : 100;
        double ratio = plan.Ratio > 0 && plan.Ratio < 1 ? plan.Ratio : 1e-4;
        if (count == 1)
        {
            return [largest];
        }

        var penalties = new double[count];
        double top = Math.Log(largest);
        double bottom = Math.Log(largest * ratio);
        for (int i = 0; i < count; i++)
        {
            penalties[i] = Math.Exp(bottom + ((top - bottom) * i / (count - 1)));
        }

        return penalties;
    }

    /// <summary>Undoes the standardization and records one column of the path.</summary>
    private static void Record(
        double[,] coefficients,
        double[] intercepts,
        int[] degrees,
        int index,
        double[] beta,
        double intercept,
        double[] centres,
        double[] scales,
        double baseline)
    {
        int p = beta.Length;
        double shift = intercept + baseline;
        int nonZero = 0;
        for (int j = 0; j < p; j++)
        {
            double original = beta[j] / scales[j];
            coefficients[j, index] = original;
            shift -= centres[j] * original;
            if (original != 0)
            {
                nonZero++;
            }
        }

        intercepts[index] = shift;
        degrees[index] = nonZero;
    }

    /// <summary>The working response and weight one reweighting step produces.</summary>
    private static void Reweight(
        GlmFamily family,
        GlmLink link,
        double power,
        int n,
        double[] trials,
        double[] offset,
        double[] y,
        Func<int, double> linear,
        double[] working,
        double[] weights)
    {
        for (int i = 0; i < n; i++)
        {
            double eta = offset[i] + linear(i);
            double mu = Bounded(family, GeneralizedLinear.Inverse(link, power, eta));
            double slope = GeneralizedLinear.Derivative(link, power, mu);
            working[i] = eta - offset[i] + ((y[i] - mu) * slope);
            double variance = GeneralizedLinear.Variance(family, mu) / trials[i];
            weights[i] = 1 / Math.Max(1e-300, variance * slope * slope);
        }
    }

    /// <summary>A working response with its own weighted mean taken out.</summary>
    private static double[] Recentred(double[] working, double[] weights)
    {
        double centre = 0, total = 0;
        for (int i = 0; i < working.Length; i++)
        {
            centre += weights[i] * working[i];
            total += weights[i];
        }

        centre = total > 0 ? centre / total : 0;
        var centred = new double[working.Length];
        for (int i = 0; i < working.Length; i++)
        {
            centred[i] = working[i] - centre;
        }

        return centred;
    }

    /// <summary>One row of the design times the coefficients.</summary>
    private static double Dot(double[,] x, int row, double[] beta)
    {
        double value = 0;
        for (int j = 0; j < beta.Length; j++)
        {
            value += x[row, j] * beta[j];
        }

        return value;
    }

    /// <summary>Predictors with their weighted mean removed and, where asked, their spread divided out.</summary>
    private static (double[,] Scaled, double[] Centres, double[] Scales) Standardize(
        double[,] predictors, double[] w, bool standardize)
    {
        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        var centres = new double[p];
        var scales = new double[p];
        var scaled = new double[n, p];
        for (int j = 0; j < p; j++)
        {
            double mean = 0;
            for (int i = 0; i < n; i++)
            {
                mean += w[i] * predictors[i, j];
            }

            centres[j] = mean;
            double squares = 0;
            for (int i = 0; i < n; i++)
            {
                double gap = predictors[i, j] - mean;
                squares += w[i] * gap * gap;
            }

            scales[j] = standardize && squares > 0 ? Math.Sqrt(squares) : 1;
            for (int i = 0; i < n; i++)
            {
                scaled[i, j] = (predictors[i, j] - mean) / scales[j];
            }
        }

        return (scaled, centres, scales);
    }

    /// <summary>Weights that sum to one, so the loss does not change size with the sample.</summary>
    private static double[] NormalizedWeights(double[]? weights, int n)
    {
        var w = new double[n];
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            w[i] = weights is null ? 1 : weights[i];
            if (w[i] < 0)
            {
                throw new ArgumentException("an observation's weight cannot be negative.");
            }

            total += w[i];
        }

        if (total <= 0)
        {
            throw new ArgumentException("the weights are all zero, so there is nothing to fit.");
        }

        for (int i = 0; i < n; i++)
        {
            w[i] /= total;
        }

        return w;
    }

    /// <summary>Penalties in ascending order, which is the order MathWorks reports them in.</summary>
    private static double[] Ascending(IReadOnlyList<double> lambda)
    {
        var penalties = new double[lambda.Count];
        for (int i = 0; i < lambda.Count; i++)
        {
            if (lambda[i] < 0)
            {
                throw new ArgumentException("a penalty cannot be negative.");
            }

            penalties[i] = lambda[i];
        }

        Array.Sort(penalties);
        return penalties;
    }

    /// <summary>Keeps a mean inside its family's domain.</summary>
    private static double Bounded(GlmFamily family, double mu) => family switch
    {
        GlmFamily.Binomial => Math.Clamp(mu, 1e-10, 1 - 1e-10),
        GlmFamily.Poisson or GlmFamily.Gamma or GlmFamily.InverseGaussian => Math.Max(mu, 1e-10),
        _ => mu,
    };

    private static void CheckMixing(double mixing)
    {
        if (!(mixing > 0 && mixing <= 1))
        {
            throw new ArgumentException(
                $"the elastic-net mixing must be above 0 and at most 1, and {mixing} is not.");
        }
    }
}
