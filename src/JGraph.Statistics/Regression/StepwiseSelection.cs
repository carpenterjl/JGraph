using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Regression;

/// <summary>
/// <c>stepwisefit</c>: choosing which predictors belong in a linear model by adding and removing them
/// one at a time.
/// </summary>
/// <remarks>
/// <para>
/// Removals are considered before additions at every step. That ordering is not cosmetic: a predictor
/// that earned its place early can be made redundant by two added later, and a rule that only ever
/// adds would keep it forever. The search stops when no term in the model is weak enough to drop and
/// no term outside it is strong enough to take.
/// </para>
/// <para>
/// The coefficients reported for terms that are not in the model are the ones they would take if they
/// were added next, which is what makes the answer readable as advice rather than only as a decision.
/// </para>
/// </remarks>
public static class StepwiseSelection
{
    /// <summary>One move the search made, and the model it left behind.</summary>
    /// <param name="Term">The zero-based predictor that moved.</param>
    /// <param name="Added">Whether it entered the model rather than left it.</param>
    /// <param name="P">The probability that justified the move.</param>
    /// <param name="InModel">Which predictors were in the model after the move.</param>
    /// <param name="Rmse">The root mean squared error of that model.</param>
    /// <param name="ModelDf">How many predictors it used.</param>
    public readonly record struct Move(
        int Term, bool Added, double P, bool[] InModel, double Rmse, int ModelDf);

    /// <summary>What the search settled on.</summary>
    /// <param name="InModel">Which predictors ended up in the model.</param>
    /// <param name="Coefficients">Each predictor's coefficient, fitted or prospective.</param>
    /// <param name="StandardErrors">Its standard error.</param>
    /// <param name="P">Its two-sided probability.</param>
    /// <param name="Intercept">The fitted intercept.</param>
    /// <param name="ResidualSumOfSquares">What the chosen model leaves unexplained.</param>
    /// <param name="TotalSumOfSquares">The variation about the mean.</param>
    /// <param name="Df">The chosen model's residual degrees of freedom.</param>
    /// <param name="ModelDf">How many predictors it uses.</param>
    /// <param name="F">The statistic for the whole model.</param>
    /// <param name="ModelP">Its probability.</param>
    /// <param name="Rmse">The root mean squared error.</param>
    /// <param name="Covariance">The covariance of the intercept and the chosen coefficients.</param>
    /// <param name="XResiduals">Each predictor with the chosen model's predictors projected out.</param>
    /// <param name="YResiduals">The response with them projected out.</param>
    /// <param name="NextTerm">The predictor that would move next, or −1 where the search is finished.</param>
    /// <param name="NextP">Its probability.</param>
    /// <param name="History">Every move, in order.</param>
    public readonly record struct Selection(
        bool[] InModel,
        double[] Coefficients,
        double[] StandardErrors,
        double[] P,
        double Intercept,
        double ResidualSumOfSquares,
        double TotalSumOfSquares,
        int Df,
        int ModelDf,
        double F,
        double ModelP,
        double Rmse,
        double[,] Covariance,
        double[,] XResiduals,
        double[] YResiduals,
        int NextTerm,
        double NextP,
        IReadOnlyList<Move> History);

    /// <summary>Runs the search.</summary>
    /// <param name="predictors">One row per observation, one column per candidate predictor.</param>
    /// <param name="y">The response.</param>
    /// <param name="enter">How improbable a term must be under the null to be taken in.</param>
    /// <param name="remove">How probable it must become to be dropped again.</param>
    /// <param name="start">Which terms to start with, or null to start with none.</param>
    /// <param name="keep">Terms that may never be removed, or null for none.</param>
    /// <param name="maxIterations">How many moves to allow, or zero for one per predictor plus ten.</param>
    public static Selection Fit(
        double[,] predictors,
        double[] y,
        double enter,
        double remove,
        bool[]? start,
        bool[]? keep,
        int maxIterations)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(y);

        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        if (y.Length != n)
        {
            throw new ArgumentException(
                $"the response has {y.Length} values but the predictors have {n} rows.", nameof(y));
        }

        if (!(enter > 0 && enter < 1) || !(remove > 0 && remove < 1))
        {
            throw new ArgumentException("the entry and removal probabilities must be between 0 and 1.");
        }

        if (remove <= enter)
        {
            throw new ArgumentException(
                "the removal probability must exceed the entry one, or a term could enter and leave for ever.");
        }

        var inModel = new bool[p];
        if (start is not null)
        {
            if (start.Length != p)
            {
                throw new ArgumentException(
                    $"the starting model names {start.Length} terms for {p} predictors.", nameof(start));
            }

            Array.Copy(start, inModel, p);
        }

        var locked = new bool[p];
        if (keep is not null)
        {
            if (keep.Length != p)
            {
                throw new ArgumentException(
                    $"the kept terms name {keep.Length} of {p} predictors.", nameof(keep));
            }

            Array.Copy(keep, locked, p);
            for (int j = 0; j < p; j++)
            {
                inModel[j] |= locked[j];
            }
        }

        // What counts as nothing left to explain. Without this a response that some predictor already
        // reproduces exactly goes on taking more of them: the residuals are rounding error, but their
        // ratio to a standard error that is also rounding error is a perfectly ordinary-looking
        // statistic, and the search would read it as evidence.
        double centre = 0;
        foreach (double value in y)
        {
            centre += value;
        }

        centre /= n;
        double spread = 0;
        foreach (double value in y)
        {
            spread += (value - centre) * (value - centre);
        }

        double floor = 1e-20 * Math.Max(spread, 1e-300);

        int budget = maxIterations > 0 ? maxIterations : p + 10;
        var history = new List<Move>();
        for (int step = 0; step < budget; step++)
        {
            (int term, bool add, double probability) =
                Candidate(predictors, y, inModel, locked, enter, remove, floor);
            if (term < 0)
            {
                break;
            }

            inModel[term] = add;
            LeastSquares.Fit reached = FitSubset(predictors, y, inModel, out int[] kept);
            history.Add(new Move(
                term, add, probability, (bool[])inModel.Clone(),
                Math.Sqrt(reached.MeanSquaredError), kept.Length));
        }

        (int nextTerm, _, double nextP) =
            Candidate(predictors, y, inModel, locked, enter, remove, floor);
        return Describe(predictors, y, inModel, nextTerm, nextP, history);
    }

    /// <summary>The move the search would make next, or a term of −1 where there is none.</summary>
    private static (int Term, bool Add, double P) Candidate(
        double[,] predictors,
        double[] y,
        bool[] inModel,
        bool[] locked,
        double enter,
        double remove,
        double floor)
    {
        int p = inModel.Length;
        LeastSquares.Fit fit = FitSubset(predictors, y, inModel, out int[] chosen);
        double[] probabilities = Probabilities(fit);

        // A term already in the model earns its place afresh at every step, so a removal is looked for
        // first: the model should shed what has become redundant before it takes on anything new.
        int worst = -1;
        double worstP = remove;
        for (int slot = 0; slot < chosen.Length; slot++)
        {
            int term = chosen[slot];
            if (locked[term])
            {
                continue;
            }

            double probability = probabilities[slot + 1];
            if (double.IsFinite(probability) && probability > worstP)
            {
                worst = term;
                worstP = probability;
            }
        }

        if (worst >= 0)
        {
            return (worst, false, worstP);
        }

        if (fit.ResidualSumOfSquares <= floor)
        {
            return (-1, false, double.NaN);
        }

        int best = -1;
        double bestP = enter;
        for (int term = 0; term < p; term++)
        {
            if (inModel[term])
            {
                continue;
            }

            var trial = (bool[])inModel.Clone();
            trial[term] = true;
            LeastSquares.Fit candidate = FitSubset(predictors, y, trial, out int[] order);
            double[] candidateP = Probabilities(candidate);
            int slot = Array.IndexOf(order, term);
            double probability = candidateP[slot + 1];
            if (double.IsFinite(probability) && probability < bestP)
            {
                best = term;
                bestP = probability;
            }
        }

        return best >= 0 ? (best, true, bestP) : (-1, false, double.NaN);
    }

    /// <summary>Everything the caller is told about the model the search stopped at.</summary>
    private static Selection Describe(
        double[,] predictors,
        double[] y,
        bool[] inModel,
        int nextTerm,
        double nextP,
        List<Move> history)
    {
        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        LeastSquares.Fit fit = FitSubset(predictors, y, inModel, out int[] chosen);
        double[] probabilities = Probabilities(fit);

        var coefficients = new double[p];
        var standardErrors = new double[p];
        var reported = new double[p];
        Array.Fill(reported, double.NaN);
        for (int slot = 0; slot < chosen.Length; slot++)
        {
            int term = chosen[slot];
            coefficients[term] = fit.Coefficients[slot + 1];
            standardErrors[term] = Math.Sqrt(Math.Max(0, fit.Covariance[slot + 1, slot + 1]));
            reported[term] = probabilities[slot + 1];
        }

        // A term outside the model is reported with the coefficient it would take if it were added,
        // which is what makes the answer advice rather than only a verdict.
        for (int term = 0; term < p; term++)
        {
            if (inModel[term])
            {
                continue;
            }

            var trial = (bool[])inModel.Clone();
            trial[term] = true;
            LeastSquares.Fit candidate = FitSubset(predictors, y, trial, out int[] order);
            double[] candidateP = Probabilities(candidate);
            int slot = Array.IndexOf(order, term);
            coefficients[term] = candidate.Coefficients[slot + 1];
            standardErrors[term] = Math.Sqrt(Math.Max(0, candidate.Covariance[slot + 1, slot + 1]));
            reported[term] = candidateP[slot + 1];
        }

        double mean = 0;
        foreach (double value in y)
        {
            mean += value;
        }

        mean /= n;
        double total = 0;
        foreach (double value in y)
        {
            total += (value - mean) * (value - mean);
        }

        double f = double.NaN, modelP = double.NaN;
        if (chosen.Length > 0 && fit.Df > 0 && fit.MeanSquaredError > 0)
        {
            f = (total - fit.ResidualSumOfSquares) / chosen.Length / fit.MeanSquaredError;
            modelP = 1 - ContinuousDistributions.FCdf(f, chosen.Length, fit.Df);
        }

        // The residuals of every predictor on the chosen model: what is left of each candidate once
        // the terms already in the model have taken what they can.
        var xResiduals = new double[n, p];
        var design = Design(predictors, chosen);
        for (int term = 0; term < p; term++)
        {
            var column = new double[n];
            for (int i = 0; i < n; i++)
            {
                column[i] = predictors[i, term];
            }

            double[] left = LeastSquares.Solve(design, column).Residuals;
            for (int i = 0; i < n; i++)
            {
                xResiduals[i, term] = left[i];
            }
        }

        return new Selection(
            inModel, coefficients, standardErrors, reported, fit.Coefficients[0], fit.ResidualSumOfSquares,
            total, fit.Df, chosen.Length, f, modelP, Math.Sqrt(fit.MeanSquaredError), fit.Covariance,
            xResiduals, fit.Residuals, nextTerm, nextP, history);
    }

    /// <summary>Fits the intercept and the chosen predictors, reporting which those were.</summary>
    private static LeastSquares.Fit FitSubset(
        double[,] predictors, double[] y, bool[] inModel, out int[] chosen)
    {
        var terms = new List<int>();
        for (int j = 0; j < inModel.Length; j++)
        {
            if (inModel[j])
            {
                terms.Add(j);
            }
        }

        chosen = [.. terms];
        return LeastSquares.Solve(Design(predictors, chosen), y);
    }

    /// <summary>A design matrix of an intercept followed by the chosen predictors.</summary>
    private static double[,] Design(double[,] predictors, int[] chosen)
    {
        int n = predictors.GetLength(0);
        var design = new double[n, chosen.Length + 1];
        for (int i = 0; i < n; i++)
        {
            design[i, 0] = 1;
            for (int slot = 0; slot < chosen.Length; slot++)
            {
                design[i, slot + 1] = predictors[i, chosen[slot]];
            }
        }

        return design;
    }

    /// <summary>The two-sided probability of each coefficient in a fit.</summary>
    private static double[] Probabilities(LeastSquares.Fit fit)
    {
        var probabilities = new double[fit.Coefficients.Length];
        for (int j = 0; j < probabilities.Length; j++)
        {
            double error = Math.Sqrt(Math.Max(0, fit.Covariance[j, j]));
            probabilities[j] = error > 0 && fit.Df > 0
                ? 2 * ContinuousDistributions.TCdf(-Math.Abs(fit.Coefficients[j] / error), fit.Df)
                : double.NaN;
        }

        return probabilities;
    }
}
