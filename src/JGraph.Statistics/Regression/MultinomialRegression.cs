using JGraph.Numerics.LinearAlgebra;
using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Regression;

/// <summary>The three ways <c>mnrfit</c> can relate a response of several categories to the predictors.</summary>
public enum MultinomialModel
{
    /// <summary>The categories have no order; each is compared with the last one directly.</summary>
    Nominal,

    /// <summary>The categories are ordered, and each cut between them shares one set of slopes.</summary>
    Ordinal,

    /// <summary>Each category is reached only by not stopping at an earlier one.</summary>
    Hierarchical,
}

/// <summary>
/// <c>mnrfit</c> and <c>mnrval</c>: a logistic regression for a response with more than two categories.
/// </summary>
/// <remarks>
/// <para>
/// The three models differ only in what the linear predictors mean. Nominal compares each category
/// with a reference; ordinal cuts an ordered scale at each boundary and asks how far along the scale
/// an observation is; hierarchical asks, at each category in turn, whether an observation that got
/// this far stops here. So there is one likelihood, one gradient rule per model, and one Newton
/// search — not three fitters.
/// </para>
/// <para>
/// The Hessian is taken by differencing the analytic gradient rather than written out. The three
/// models' second derivatives are three different pieces of bookkeeping over the same small parameter
/// vector, and differencing an exact gradient is accurate to about half of working precision, which is
/// several digits more than the standard errors it feeds are worth.
/// </para>
/// <para>
/// Only the ordinal and hierarchical models take a link other than the logit. A nominal model has no
/// single probability to transform — it has one per category, tied together by having to sum to
/// one — so a probit nominal model is not a thing MathWorks offers either.
/// </para>
/// </remarks>
public static class MultinomialRegression
{
    /// <summary>What a multinomial fit produced.</summary>
    /// <param name="Coefficients">The intercepts, then the slopes; one set of slopes per category where they are separate.</param>
    /// <param name="Deviance">Twice the log-likelihood short of a perfect fit.</param>
    /// <param name="Covariance">The coefficients' covariance.</param>
    /// <param name="StandardErrors">One per coefficient.</param>
    /// <param name="T">Each coefficient over its standard error.</param>
    /// <param name="P">Its two-sided probability.</param>
    /// <param name="Probabilities">The fitted probability of every category for every observation.</param>
    /// <param name="Df">Observations times categories, less what was estimated.</param>
    /// <param name="Converged">Whether the search settled before its budget ran out.</param>
    /// <param name="Iterations">Iterations taken.</param>
    public readonly record struct MultinomialFit(
        double[] Coefficients,
        double Deviance,
        double[,] Covariance,
        double[] StandardErrors,
        double[] T,
        double[] P,
        double[,] Probabilities,
        int Df,
        bool Converged,
        int Iterations);

    /// <summary>How many coefficients a model of this shape has.</summary>
    public static int ParameterCount(int categories, int predictors, bool separateSlopes) =>
        categories - 1 + (separateSlopes ? (categories - 1) * predictors : predictors);

    /// <summary>Fits the model by Newton's method on the multinomial likelihood.</summary>
    /// <param name="predictors">One row per observation, one column per predictor; no intercept column.</param>
    /// <param name="counts">One row per observation, one column per category, holding how many fell there.</param>
    /// <param name="model">Which of the three relationships to fit.</param>
    /// <param name="link">The link, for the ordinal and hierarchical models.</param>
    /// <param name="separateSlopes">Whether each category gets its own slopes.</param>
    public static MultinomialFit Fit(
        double[,] predictors,
        double[,] counts,
        MultinomialModel model,
        GlmLink link,
        bool separateSlopes)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(counts);

        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        int k = counts.GetLength(1);
        if (counts.GetLength(0) != n)
        {
            throw new ArgumentException(
                $"the response has {counts.GetLength(0)} rows but the predictors have {n}.", nameof(counts));
        }

        if (k < 2)
        {
            throw new ArgumentException("a multinomial response needs at least two categories.", nameof(counts));
        }

        if (model == MultinomialModel.Nominal && link != GlmLink.Logit)
        {
            throw new ArgumentException(
                "a nominal model has one probability per category rather than a single one to transform, "
                + "so it is fitted with the logit and no other link.");
        }

        if (link is not (GlmLink.Logit or GlmLink.Probit or GlmLink.ComplementaryLogLog or GlmLink.LogLog))
        {
            throw new ArgumentException(
                "a multinomial model takes the logit, probit, complementary log-log or log-log link.");
        }

        var totals = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < k; j++)
            {
                if (counts[i, j] < 0)
                {
                    throw new ArgumentException("a category count cannot be negative.");
                }

                totals[i] += counts[i, j];
            }

            if (totals[i] <= 0)
            {
                throw new ArgumentException($"observation {i + 1} was never observed in any category.");
            }
        }

        int size = ParameterCount(k, p, separateSlopes);
        var theta = new double[size];
        for (int j = 0; j < k - 1; j++)
        {
            // Start from the model with no predictors at all, which has a closed form in every case
            // and is close enough that Newton needs only a handful of steps.
            double share = 0;
            for (int i = 0; i < n; i++)
            {
                share += counts[i, j];
            }

            double rest = 0;
            for (int i = 0; i < n; i++)
            {
                rest += totals[i];
            }

            double fraction = Math.Clamp(share / rest, 1e-4, 1 - 1e-4);
            theta[j] = model == MultinomialModel.Nominal
                ? Math.Log(fraction / Math.Max(1e-6, 1 - fraction))
                : GeneralizedLinear.Link(link, 0, Math.Clamp((j + 1.0) / k, 1e-4, 1 - 1e-4));
        }

        bool converged = false;
        int iteration = 0;
        double previous = LogLikelihood(predictors, counts, totals, theta, model, link, separateSlopes, k, p);
        for (iteration = 1; iteration <= 100; iteration++)
        {
            double[] gradient =
                Gradient(predictors, counts, totals, theta, model, link, separateSlopes, k, p);
            double[,] hessian =
                Hessian(predictors, counts, totals, theta, model, link, separateSlopes, k, p);

            double[] step;
            try
            {
                // Newton moves against the curvature; the likelihood is concave, so the Hessian is
                // negative definite and the step is minus its inverse times the gradient.
                var negated = new double[size, size];
                for (int a = 0; a < size; a++)
                {
                    for (int b = 0; b < size; b++)
                    {
                        negated[a, b] = -hessian[a, b];
                    }

                    negated[a, a] += 1e-10;
                }

                step = LuDecomposition.Factor(negated).Solve(gradient);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            double damping = 1;
            double value = previous;
            var candidate = theta;
            for (int back = 0; back < 30; back++)
            {
                var trial = new double[size];
                for (int a = 0; a < size; a++)
                {
                    trial[a] = theta[a] + (damping * step[a]);
                }

                double trialValue =
                    LogLikelihood(predictors, counts, totals, trial, model, link, separateSlopes, k, p);
                if (double.IsFinite(trialValue) && trialValue >= value)
                {
                    candidate = trial;
                    value = trialValue;
                    break;
                }

                damping /= 2;
            }

            double movement = 0;
            for (int a = 0; a < size; a++)
            {
                movement = Math.Max(movement, Math.Abs(candidate[a] - theta[a]));
            }

            theta = candidate;
            if (movement <= 1e-10 || Math.Abs(value - previous) <= 1e-12 * (1 + Math.Abs(value)))
            {
                previous = value;
                converged = true;
                break;
            }

            previous = value;
        }

        double[,] curvature = Hessian(predictors, counts, totals, theta, model, link, separateSlopes, k, p);
        var information = new double[size, size];
        for (int a = 0; a < size; a++)
        {
            for (int b = 0; b < size; b++)
            {
                information[a, b] = -curvature[a, b];
            }
        }

        double[,] covariance;
        try
        {
            covariance = LuDecomposition.Factor(information).Inverse();
        }
        catch (InvalidOperationException)
        {
            covariance = new double[size, size];
            for (int a = 0; a < size; a++)
            {
                covariance[a, a] = double.NaN;
            }
        }

        var probabilities = Probabilities(predictors, theta, model, link, separateSlopes, k, p);
        double saturated = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < k; j++)
            {
                if (counts[i, j] > 0)
                {
                    saturated += counts[i, j] * Math.Log(counts[i, j] / totals[i]);
                }
            }
        }

        int df = Math.Max(0, (n * (k - 1)) - size);
        var standardErrors = new double[size];
        var t = new double[size];
        var probability = new double[size];
        for (int a = 0; a < size; a++)
        {
            standardErrors[a] = Math.Sqrt(Math.Max(0, covariance[a, a]));
            t[a] = standardErrors[a] > 0 ? theta[a] / standardErrors[a] : double.NaN;
            probability[a] = df > 0 && double.IsFinite(t[a])
                ? 2 * ContinuousDistributions.TCdf(-Math.Abs(t[a]), df)
                : double.NaN;
        }

        return new MultinomialFit(
            theta, 2 * (saturated - previous), covariance, standardErrors, t, probability, probabilities,
            df, converged, Math.Min(iteration, 100));
    }

    /// <summary><c>mnrval</c>: the probability of every category at every row.</summary>
    public static double[,] Probabilities(
        double[,] predictors,
        double[] theta,
        MultinomialModel model,
        GlmLink link,
        bool separateSlopes,
        int categories,
        int predictorCount)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(theta);

        int n = predictors.GetLength(0);
        var probabilities = new double[n, categories];
        for (int i = 0; i < n; i++)
        {
            double[] eta = Linear(predictors, i, theta, separateSlopes, categories, predictorCount);
            double[] row = FromLinear(eta, model, link, categories);
            for (int j = 0; j < categories; j++)
            {
                probabilities[i, j] = row[j];
            }
        }

        return probabilities;
    }

    /// <summary>The cumulative probability up to and including each category but the last.</summary>
    public static double[,] Cumulative(double[,] categoryProbabilities)
    {
        ArgumentNullException.ThrowIfNull(categoryProbabilities);

        int n = categoryProbabilities.GetLength(0);
        int k = categoryProbabilities.GetLength(1);
        var cumulative = new double[n, k - 1];
        for (int i = 0; i < n; i++)
        {
            double running = 0;
            for (int j = 0; j < k - 1; j++)
            {
                running += categoryProbabilities[i, j];
                cumulative[i, j] = running;
            }
        }

        return cumulative;
    }

    /// <summary>The probability of stopping at each category given that an earlier one was not reached.</summary>
    public static double[,] Conditional(double[,] categoryProbabilities)
    {
        ArgumentNullException.ThrowIfNull(categoryProbabilities);

        int n = categoryProbabilities.GetLength(0);
        int k = categoryProbabilities.GetLength(1);
        var conditional = new double[n, k - 1];
        for (int i = 0; i < n; i++)
        {
            double left = 1;
            for (int j = 0; j < k - 1; j++)
            {
                conditional[i, j] = left > 0 ? categoryProbabilities[i, j] / left : 0;
                left -= categoryProbabilities[i, j];
            }
        }

        return conditional;
    }

    /// <summary>The linear predictors for one observation, one per category boundary.</summary>
    private static double[] Linear(
        double[,] predictors, int row, double[] theta, bool separateSlopes, int categories, int predictorCount)
    {
        var eta = new double[categories - 1];
        for (int j = 0; j < categories - 1; j++)
        {
            double value = theta[j];
            int offset = categories - 1 + (separateSlopes ? j * predictorCount : 0);
            for (int c = 0; c < predictorCount; c++)
            {
                value += theta[offset + c] * predictors[row, c];
            }

            eta[j] = value;
        }

        return eta;
    }

    /// <summary>The category probabilities the linear predictors imply, under one of the three models.</summary>
    private static double[] FromLinear(
        double[] eta, MultinomialModel model, GlmLink link, int categories)
    {
        var probabilities = new double[categories];
        switch (model)
        {
            case MultinomialModel.Nominal:
                {
                    double total = 1;
                    for (int j = 0; j < categories - 1; j++)
                    {
                        probabilities[j] = Math.Exp(Math.Clamp(eta[j], -700, 700));
                        total += probabilities[j];
                    }

                    for (int j = 0; j < categories - 1; j++)
                    {
                        probabilities[j] /= total;
                    }

                    probabilities[categories - 1] = 1 / total;
                    break;
                }

            case MultinomialModel.Ordinal:
                {
                    double previous = 0;
                    for (int j = 0; j < categories - 1; j++)
                    {
                        double cumulative = GeneralizedLinear.Inverse(link, 0, eta[j]);
                        probabilities[j] = cumulative - previous;
                        previous = cumulative;
                    }

                    probabilities[categories - 1] = 1 - previous;
                    break;
                }

            case MultinomialModel.Hierarchical:
                {
                    double left = 1;
                    for (int j = 0; j < categories - 1; j++)
                    {
                        double stop = GeneralizedLinear.Inverse(link, 0, eta[j]);
                        probabilities[j] = left * stop;
                        left *= 1 - stop;
                    }

                    probabilities[categories - 1] = left;
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(model));
        }

        for (int j = 0; j < categories; j++)
        {
            probabilities[j] = Math.Clamp(probabilities[j], 1e-12, 1);
        }

        return probabilities;
    }

    private static double LogLikelihood(
        double[,] predictors,
        double[,] counts,
        double[] totals,
        double[] theta,
        MultinomialModel model,
        GlmLink link,
        bool separateSlopes,
        int categories,
        int predictorCount)
    {
        int n = predictors.GetLength(0);
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            double[] eta = Linear(predictors, i, theta, separateSlopes, categories, predictorCount);
            double[] probabilities = FromLinear(eta, model, link, categories);
            for (int j = 0; j < categories; j++)
            {
                if (counts[i, j] > 0)
                {
                    total += counts[i, j] * Math.Log(probabilities[j]);
                }
            }
        }

        return total;
    }

    /// <summary>
    /// The likelihood's derivative with respect to each coefficient, by the chain rule through the
    /// linear predictors. Only the middle factor differs between the three models.
    /// </summary>
    private static double[] Gradient(
        double[,] predictors,
        double[,] counts,
        double[] totals,
        double[] theta,
        MultinomialModel model,
        GlmLink link,
        bool separateSlopes,
        int categories,
        int predictorCount)
    {
        int n = predictors.GetLength(0);
        int size = theta.Length;
        var gradient = new double[size];
        for (int i = 0; i < n; i++)
        {
            double[] eta = Linear(predictors, i, theta, separateSlopes, categories, predictorCount);
            double[] probabilities = FromLinear(eta, model, link, categories);
            double[] slope = new double[categories - 1];
            switch (model)
            {
                case MultinomialModel.Nominal:
                    for (int j = 0; j < categories - 1; j++)
                    {
                        slope[j] = counts[i, j] - (totals[i] * probabilities[j]);
                    }

                    break;

                case MultinomialModel.Ordinal:
                    for (int j = 0; j < categories - 1; j++)
                    {
                        double density = Density(link, eta[j]);
                        slope[j] = density
                            * ((counts[i, j] / probabilities[j]) - (counts[i, j + 1] / probabilities[j + 1]));
                    }

                    break;

                case MultinomialModel.Hierarchical:
                    {
                        double remaining = totals[i];
                        for (int j = 0; j < categories - 1; j++)
                        {
                            double stop = GeneralizedLinear.Inverse(link, 0, eta[j]);
                            stop = Math.Clamp(stop, 1e-12, 1 - 1e-12);
                            double density = Density(link, eta[j]);
                            slope[j] = density
                                * ((counts[i, j] / stop) - ((remaining - counts[i, j]) / (1 - stop)));
                            remaining -= counts[i, j];
                        }

                        break;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(model));
            }

            for (int j = 0; j < categories - 1; j++)
            {
                gradient[j] += slope[j];
                int offset = categories - 1 + (separateSlopes ? j * predictorCount : 0);
                for (int c = 0; c < predictorCount; c++)
                {
                    gradient[offset + c] += slope[j] * predictors[i, c];
                }
            }
        }

        return gradient;
    }

    /// <summary>The curvature, by differencing the exact gradient.</summary>
    private static double[,] Hessian(
        double[,] predictors,
        double[,] counts,
        double[] totals,
        double[] theta,
        MultinomialModel model,
        GlmLink link,
        bool separateSlopes,
        int categories,
        int predictorCount)
    {
        int size = theta.Length;
        var hessian = new double[size, size];
        for (int a = 0; a < size; a++)
        {
            double step = 1e-6 * Math.Max(1, Math.Abs(theta[a]));
            var up = (double[])theta.Clone();
            var down = (double[])theta.Clone();
            up[a] += step;
            down[a] -= step;
            double actual = up[a] - down[a];
            double[] above =
                Gradient(predictors, counts, totals, up, model, link, separateSlopes, categories, predictorCount);
            double[] below =
                Gradient(predictors, counts, totals, down, model, link, separateSlopes, categories, predictorCount);
            for (int b = 0; b < size; b++)
            {
                hessian[b, a] = (above[b] - below[b]) / actual;
            }
        }

        // The curvature of a likelihood is symmetric; differencing makes it only nearly so.
        for (int a = 0; a < size; a++)
        {
            for (int b = a + 1; b < size; b++)
            {
                double average = (hessian[a, b] + hessian[b, a]) / 2;
                hessian[a, b] = average;
                hessian[b, a] = average;
            }
        }

        return hessian;
    }

    /// <summary>The density of the distribution a link inverts.</summary>
    private static double Density(GlmLink link, double eta) => link switch
    {
        GlmLink.Logit => Logistic(eta),
        GlmLink.Probit => ContinuousDistributions.NormalPdf(eta, 0, 1),
        GlmLink.ComplementaryLogLog => Math.Exp(Math.Clamp(eta - Math.Exp(Math.Clamp(eta, -700, 700)), -700, 700)),
        GlmLink.LogLog => Math.Exp(Math.Clamp(-eta - Math.Exp(Math.Clamp(-eta, -700, 700)), -700, 700)),
        _ => throw new ArgumentOutOfRangeException(nameof(link)),
    };

    private static double Logistic(double eta)
    {
        double p = 1 / (1 + Math.Exp(-Math.Clamp(eta, -700, 700)));
        return p * (1 - p);
    }
}
