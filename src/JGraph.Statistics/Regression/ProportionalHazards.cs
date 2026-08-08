using JGraph.Numerics.LinearAlgebra;
using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Regression;

/// <summary>How a set of failures at the very same time is accounted for.</summary>
public enum TieHandling
{
    /// <summary>Every tied failure is compared against the same full risk set. Quick, and biased towards zero when ties are common.</summary>
    Breslow,

    /// <summary>The risk set is shrunk across the tied failures as if their order were unknown but real.</summary>
    Efron,
}

/// <summary>
/// <c>coxphfit</c>: how a set of predictors multiplies the rate at which something fails, without ever
/// saying what that rate is.
/// </summary>
/// <remarks>
/// <para>
/// The model's whole point is that the baseline hazard cancels. At each moment a failure occurs, the
/// question asked is only "of everyone still at risk, why this one?" — and the answer depends on the
/// predictors alone, because whatever the underlying rate was, it applied to all of them equally. That
/// product over failure times is the partial likelihood, and it is what is maximized here. The
/// baseline itself is recovered afterwards, once the coefficients are known.
/// </para>
/// <para>
/// Censoring costs nothing: an observation that was still going when it left the study contributes no
/// failure of its own but stays in every risk set up to the moment it left, which is exactly what
/// knowing "it had not failed by then" is worth.
/// </para>
/// </remarks>
public static class ProportionalHazards
{
    /// <summary>What a proportional-hazards fit produced.</summary>
    /// <param name="Coefficients">How each predictor multiplies the hazard, in logarithms.</param>
    /// <param name="LogLikelihood">The partial log-likelihood at the answer.</param>
    /// <param name="Covariance">The coefficients' covariance.</param>
    /// <param name="StandardErrors">One per coefficient.</param>
    /// <param name="Z">Each coefficient over its standard error.</param>
    /// <param name="P">Its two-sided probability.</param>
    /// <param name="Times">The distinct times at which something failed.</param>
    /// <param name="CumulativeHazard">The baseline cumulative hazard at each of those times.</param>
    /// <param name="Martingale">Each observation's failure less what the model expected of it.</param>
    /// <param name="Deviance">Those residuals made symmetric.</param>
    /// <param name="CoxSnell">What each observation's cumulative hazard came to.</param>
    /// <param name="Schoenfeld">At each failure, how far the predictors were from the risk set's average.</param>
    /// <param name="Scores">Each observation's contribution to the likelihood's derivative.</param>
    /// <param name="Converged">Whether the search settled before its budget ran out.</param>
    /// <param name="Iterations">Iterations taken.</param>
    public readonly record struct HazardFit(
        double[] Coefficients,
        double LogLikelihood,
        double[,] Covariance,
        double[] StandardErrors,
        double[] Z,
        double[] P,
        double[] Times,
        double[] CumulativeHazard,
        double[] Martingale,
        double[] Deviance,
        double[] CoxSnell,
        double[,] Schoenfeld,
        double[,] Scores,
        bool Converged,
        int Iterations);

    /// <summary>Fits the model by Newton's method on the partial likelihood.</summary>
    /// <param name="predictors">One row per observation, one column per predictor; no intercept, which would cancel.</param>
    /// <param name="times">When each observation failed or left.</param>
    /// <param name="censored">Whether each observation left rather than failed, or null if all failed.</param>
    /// <param name="frequency">How many identical observations each row stands for, or null for one each.</param>
    /// <param name="baseline">The predictor values the baseline hazard is reported at, or null for their mean.</param>
    /// <param name="ties">How to account for simultaneous failures.</param>
    /// <param name="start">Coefficients to search from, or null to start from zero.</param>
    public static HazardFit Fit(
        double[,] predictors,
        double[] times,
        bool[]? censored,
        double[]? frequency,
        double[]? baseline,
        TieHandling ties,
        double[]? start)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(times);

        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        if (times.Length != n)
        {
            throw new ArgumentException(
                $"there are {times.Length} times for {n} observations.", nameof(times));
        }

        var failed = new bool[n];
        var weight = new double[n];
        for (int i = 0; i < n; i++)
        {
            failed[i] = censored is null || !censored[i];
            weight[i] = frequency is null ? 1 : frequency[i];
            if (weight[i] < 0)
            {
                throw new ArgumentException("a frequency cannot be negative.");
            }

            if (!double.IsFinite(times[i]))
            {
                throw new ArgumentException("every observation needs a finite time.");
            }
        }

        double[] centre = baseline ?? ColumnMeans(predictors, weight);
        if (centre.Length != p)
        {
            throw new ArgumentException(
                $"the baseline names {centre.Length} predictors for {p} columns.", nameof(baseline));
        }

        var centred = new double[n, p];
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < p; c++)
            {
                centred[i, c] = predictors[i, c] - centre[c];
            }
        }

        // Observations sorted by time, so that a risk set is a suffix of the ordering rather than a
        // search: everything from the first observation at this time onwards is still at risk.
        int[] order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => times[a].CompareTo(times[b]));

        var beta = new double[p];
        if (start is not null)
        {
            if (start.Length != p)
            {
                throw new ArgumentException(
                    $"the starting point names {start.Length} coefficients for {p} predictors.", nameof(start));
            }

            Array.Copy(start, beta, p);
        }

        double value = PartialLogLikelihood(centred, times, failed, weight, order, beta, ties);
        bool converged = false;
        int iteration = 0;
        for (iteration = 1; iteration <= 100; iteration++)
        {
            double[] gradient = Score(centred, times, failed, weight, order, beta, ties);
            double[,] curvature = Curvature(centred, times, failed, weight, order, beta, ties);

            double[] step;
            try
            {
                var information = new double[p, p];
                for (int a = 0; a < p; a++)
                {
                    for (int b = 0; b < p; b++)
                    {
                        information[a, b] = -curvature[a, b];
                    }

                    information[a, a] += 1e-12;
                }

                step = LuDecomposition.Factor(information).Solve(gradient);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            double damping = 1;
            var candidate = beta;
            double best = value;
            for (int back = 0; back < 30; back++)
            {
                var trial = new double[p];
                for (int a = 0; a < p; a++)
                {
                    trial[a] = beta[a] + (damping * step[a]);
                }

                double trialValue = PartialLogLikelihood(centred, times, failed, weight, order, trial, ties);
                if (double.IsFinite(trialValue) && trialValue >= best)
                {
                    candidate = trial;
                    best = trialValue;
                    break;
                }

                damping /= 2;
            }

            double movement = 0;
            for (int a = 0; a < p; a++)
            {
                movement = Math.Max(movement, Math.Abs(candidate[a] - beta[a]));
            }

            beta = candidate;
            if (movement <= 1e-10 || Math.Abs(best - value) <= 1e-12 * (1 + Math.Abs(best)))
            {
                value = best;
                converged = true;
                break;
            }

            value = best;
        }

        double[,] finalCurvature = Curvature(centred, times, failed, weight, order, beta, ties);
        var negated = new double[p, p];
        for (int a = 0; a < p; a++)
        {
            for (int b = 0; b < p; b++)
            {
                negated[a, b] = -finalCurvature[a, b];
            }
        }

        double[,] covariance;
        try
        {
            covariance = LuDecomposition.Factor(negated).Inverse();
        }
        catch (InvalidOperationException)
        {
            covariance = new double[p, p];
            for (int a = 0; a < p; a++)
            {
                covariance[a, a] = double.NaN;
            }
        }

        var standardErrors = new double[p];
        var z = new double[p];
        var probability = new double[p];
        for (int a = 0; a < p; a++)
        {
            standardErrors[a] = Math.Sqrt(Math.Max(0, covariance[a, a]));
            z[a] = standardErrors[a] > 0 ? beta[a] / standardErrors[a] : double.NaN;
            probability[a] = double.IsFinite(z[a])
                ? 2 * ContinuousDistributions.NormalCdf(-Math.Abs(z[a]), 0, 1)
                : double.NaN;
        }

        (double[] eventTimes, double[] hazard) =
            Baseline(centred, times, failed, weight, order, beta);

        var coxSnell = new double[n];
        var martingale = new double[n];
        var deviance = new double[n];
        for (int i = 0; i < n; i++)
        {
            double at = 0;
            for (int e = 0; e < eventTimes.Length && eventTimes[e] <= times[i]; e++)
            {
                at = hazard[e];
            }

            coxSnell[i] = at * Math.Exp(Dot(centred, i, beta));
            martingale[i] = (failed[i] ? 1 : 0) - coxSnell[i];
            double indicator = failed[i] ? 1 : 0;
            double inside = martingale[i]
                + (indicator > 0 ? indicator * Math.Log(Math.Max(1e-300, indicator - martingale[i])) : 0);
            deviance[i] = Math.Sign(martingale[i]) * Math.Sqrt(Math.Max(0, -2 * inside));
        }

        double[,] schoenfeld = Schoenfeld(centred, times, failed, weight, order, beta);
        var scores = new double[n, p];
        for (int i = 0; i < n; i++)
        {
            for (int a = 0; a < p; a++)
            {
                scores[i, a] = centred[i, a] * martingale[i];
            }
        }

        return new HazardFit(
            beta, value, covariance, standardErrors, z, probability, eventTimes, hazard, martingale,
            deviance, coxSnell, schoenfeld, scores, converged, Math.Min(iteration, 100));
    }

    /// <summary>The partial log-likelihood at a set of coefficients.</summary>
    public static double PartialLogLikelihood(
        double[,] x,
        double[] times,
        bool[] failed,
        double[] weight,
        int[] order,
        double[] beta,
        TieHandling ties)
    {
        double total = 0;
        Walk(x, times, failed, weight, order, beta, ties, (group, riskSum, groupSum, _, _, _, _) =>
        {
            foreach (int i in group)
            {
                total += weight[i] * Dot(x, i, beta);
            }

            double count = 0;
            foreach (int i in group)
            {
                count += weight[i];
            }

            if (ties == TieHandling.Breslow)
            {
                total -= count * Math.Log(riskSum);
                return;
            }

            for (int l = 0; l < (int)Math.Round(count); l++)
            {
                total -= Math.Log(riskSum - (l / Math.Max(1, count) * groupSum));
            }
        });

        return total;
    }

    /// <summary>The partial likelihood's derivative with respect to each coefficient.</summary>
    private static double[] Score(
        double[,] x,
        double[] times,
        bool[] failed,
        double[] weight,
        int[] order,
        double[] beta,
        TieHandling ties)
    {
        int p = x.GetLength(1);
        var gradient = new double[p];
        Walk(x, times, failed, weight, order, beta, ties,
            (group, riskSum, groupSum, riskFirst, groupFirst, _, _) =>
        {
            double count = 0;
            foreach (int i in group)
            {
                count += weight[i];
                for (int a = 0; a < p; a++)
                {
                    gradient[a] += weight[i] * x[i, a];
                }
            }

            if (ties == TieHandling.Breslow)
            {
                for (int a = 0; a < p; a++)
                {
                    gradient[a] -= count * riskFirst[a] / riskSum;
                }

                return;
            }

            int whole = (int)Math.Round(count);
            for (int l = 0; l < whole; l++)
            {
                double fraction = l / (double)whole;
                double denominator = riskSum - (fraction * groupSum);
                for (int a = 0; a < p; a++)
                {
                    gradient[a] -= (riskFirst[a] - (fraction * groupFirst[a])) / denominator;
                }
            }
        });

        return gradient;
    }

    /// <summary>The curvature, by differencing the exact score.</summary>
    private static double[,] Curvature(
        double[,] x,
        double[] times,
        bool[] failed,
        double[] weight,
        int[] order,
        double[] beta,
        TieHandling ties)
    {
        int p = beta.Length;
        var curvature = new double[p, p];
        for (int a = 0; a < p; a++)
        {
            double step = 1e-6 * Math.Max(1, Math.Abs(beta[a]));
            var up = (double[])beta.Clone();
            var down = (double[])beta.Clone();
            up[a] += step;
            down[a] -= step;
            double actual = up[a] - down[a];
            double[] above = Score(x, times, failed, weight, order, up, ties);
            double[] below = Score(x, times, failed, weight, order, down, ties);
            for (int b = 0; b < p; b++)
            {
                curvature[b, a] = (above[b] - below[b]) / actual;
            }
        }

        for (int a = 0; a < p; a++)
        {
            for (int b = a + 1; b < p; b++)
            {
                double average = (curvature[a, b] + curvature[b, a]) / 2;
                curvature[a, b] = average;
                curvature[b, a] = average;
            }
        }

        return curvature;
    }

    /// <summary>Breslow's baseline cumulative hazard, at the times something failed.</summary>
    private static (double[] Times, double[] Hazard) Baseline(
        double[,] x, double[] times, bool[] failed, double[] weight, int[] order, double[] beta)
    {
        // The walk goes backwards through time because that is how a risk set grows; a cumulative
        // hazard accumulates forwards, so the increments are collected first and summed afterwards.
        var eventTimes = new List<double>();
        var increments = new List<double>();
        Walk(x, times, failed, weight, order, beta, TieHandling.Breslow,
            (group, riskSum, _, _, _, time, _) =>
        {
            double count = 0;
            foreach (int i in group)
            {
                count += weight[i];
            }

            eventTimes.Add(time);
            increments.Add(count / riskSum);
        });

        eventTimes.Reverse();
        increments.Reverse();
        var hazard = new double[increments.Count];
        double running = 0;
        for (int e = 0; e < increments.Count; e++)
        {
            running += increments[e];
            hazard[e] = running;
        }

        return ([.. eventTimes], hazard);
    }

    /// <summary>How far each failing observation's predictors were from the risk set's weighted average.</summary>
    private static double[,] Schoenfeld(
        double[,] x, double[] times, bool[] failed, double[] weight, int[] order, double[] beta)
    {
        int p = x.GetLength(1);
        var rows = new List<double[]>();
        Walk(x, times, failed, weight, order, beta, TieHandling.Breslow,
            (group, riskSum, _, riskFirst, _, _, _) =>
        {
            foreach (int i in group)
            {
                var row = new double[p];
                for (int a = 0; a < p; a++)
                {
                    row[a] = x[i, a] - (riskFirst[a] / riskSum);
                }

                rows.Add(row);
            }
        });

        // Reported in time order, though the walk that produced them went the other way.
        rows.Reverse();
        var schoenfeld = new double[rows.Count, p];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int a = 0; a < p; a++)
            {
                schoenfeld[r, a] = rows[r][a];
            }
        }

        return schoenfeld;
    }

    /// <summary>
    /// Walks the distinct failure times from the last backwards, handing each visitor the tied failures
    /// there and the risk set's weighted sums.
    /// </summary>
    /// <remarks>
    /// Walking backwards is what makes this one pass: the risk set at an earlier time contains the risk
    /// set at a later one, so each step only adds the observations that left in between.
    /// </remarks>
    private static void Walk(
        double[,] x,
        double[] times,
        bool[] failed,
        double[] weight,
        int[] order,
        double[] beta,
        TieHandling ties,
        Action<List<int>, double, double, double[], double[], double, double[,]> visit)
    {
        int n = order.Length;
        int p = x.GetLength(1);
        double riskSum = 0;
        var riskFirst = new double[p];
        var riskSecond = new double[p, p];

        int index = n - 1;
        while (index >= 0)
        {
            double time = times[order[index]];
            var group = new List<int>();
            double groupSum = 0;
            var groupFirst = new double[p];
            while (index >= 0 && times[order[index]] == time)
            {
                int i = order[index];
                double hazard = weight[i] * Math.Exp(Dot(x, i, beta));
                riskSum += hazard;
                for (int a = 0; a < p; a++)
                {
                    riskFirst[a] += hazard * x[i, a];
                    for (int b = 0; b < p; b++)
                    {
                        riskSecond[a, b] += hazard * x[i, a] * x[i, b];
                    }
                }

                if (failed[i])
                {
                    group.Add(i);
                    groupSum += hazard;
                    for (int a = 0; a < p; a++)
                    {
                        groupFirst[a] += hazard * x[i, a];
                    }
                }

                index--;
            }

            if (group.Count > 0)
            {
                visit(group, riskSum, groupSum, riskFirst, groupFirst, time, riskSecond);
            }
        }
    }

    private static double Dot(double[,] x, int row, double[] beta)
    {
        double value = 0;
        for (int a = 0; a < beta.Length; a++)
        {
            value += x[row, a] * beta[a];
        }

        return value;
    }

    private static double[] ColumnMeans(double[,] matrix, double[] weight)
    {
        int n = matrix.GetLength(0);
        int p = matrix.GetLength(1);
        var means = new double[p];
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            total += weight[i];
        }

        for (int c = 0; c < p; c++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += weight[i] * matrix[i, c];
            }

            means[c] = total > 0 ? sum / total : 0;
        }

        return means;
    }
}
