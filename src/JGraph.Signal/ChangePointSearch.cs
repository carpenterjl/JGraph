namespace JGraph.Signal;

/// <summary>Which statistic a change point is a change in.</summary>
public enum ChangeStatistic
{
    /// <summary>The mean, with the cost the residual sum of squares about it.</summary>
    Mean,

    /// <summary>The root mean square, with the cost a log likelihood about zero.</summary>
    RootMeanSquare,

    /// <summary>The standard deviation, with the cost a log likelihood about the segment's mean.</summary>
    StandardDeviation,

    /// <summary>The slope and intercept of a straight line.</summary>
    Linear,
}

/// <summary>
/// <c>findchangepts</c>: the points at which a signal's statistics change, found by minimising the
/// total residual plus a penalty for each change.
/// </summary>
/// <remarks>
/// The search is an exact dynamic program rather than MATLAB's pruned one. Both minimise the same
/// objective and both reach the same minimum; pruning only makes the search cheaper, so the answers
/// agree wherever the minimum is unique. What has to match exactly is the cost of a segment, the
/// minimum segment length, and the way the penalty is searched for when a number of changes is asked
/// for rather than a threshold — those are transcribed.
/// </remarks>
public static class ChangePointSearch
{
    private static readonly double LogRealMin = System.Math.Log(2.2250738585072014e-308);

    /// <summary>The cost of treating samples <paramref name="from"/> to <paramref name="to"/> as one segment.</summary>
    public static double SegmentCost(double[,] y, int from, int to, ChangeStatistic statistic)
    {
        int rows = y.GetLength(0);
        int points = to - from + 1;
        double total = 0;
        for (int r = 0; r < rows; r++)
        {
            double mean = 0;
            double squares = 0;
            double count = 0;
            double xMean = 0;
            double sxx = 0;
            double sxy = 0;
            for (int i = from; i <= to; i++)
            {
                count++;
                double deltaY = y[r, i] - mean;
                mean += deltaY / count;
                squares += deltaY * (y[r, i] - mean);
                double deltaX = i - xMean;
                xMean += deltaX / count;
                sxx += deltaX * (i - xMean);
                sxy += deltaX * deltaY * (count - 1) / count;
            }

            switch (statistic)
            {
                case ChangeStatistic.Mean:
                    total += squares;
                    break;
                case ChangeStatistic.RootMeanSquare:
                {
                    double energy = 0;
                    for (int i = from; i <= to; i++)
                    {
                        energy += y[r, i] * y[r, i];
                    }

                    total += System.Math.Max(
                        LogRealMin - System.Math.Log(points), System.Math.Log(energy / points));
                    break;
                }

                case ChangeStatistic.StandardDeviation:
                    total += System.Math.Max(
                        LogRealMin - System.Math.Log(points), System.Math.Log(squares / points));
                    break;
                default:
                    total += sxx == 0 ? squares : squares - (sxy * sxy / sxx);
                    break;
            }
        }

        return statistic is ChangeStatistic.RootMeanSquare or ChangeStatistic.StandardDeviation
            ? points * total
            : total;
    }

    /// <summary>The optimal segmentation for one penalty, and its residual without the penalty.</summary>
    public static (int[] Changes, double Residual) Segment(
        double[,] y, ChangeStatistic statistic, int minimum, double penalty)
    {
        int n = y.GetLength(1);
        var best = new double[n + 1];
        var previous = new int[n + 1];
        for (int t = 0; t <= n; t++)
        {
            best[t] = double.PositiveInfinity;
            previous[t] = -1;
        }

        best[0] = -penalty;
        for (int t = minimum; t <= n; t++)
        {
            for (int s = 0; s + minimum <= t; s++)
            {
                if (s != 0 && s < minimum)
                {
                    continue;
                }

                if (double.IsPositiveInfinity(best[s]))
                {
                    continue;
                }

                double candidate = best[s] + SegmentCost(y, s, t - 1, statistic) + penalty;
                if (candidate < best[t])
                {
                    best[t] = candidate;
                    previous[t] = s;
                }
            }
        }

        var changes = new List<int>();
        int at = n;
        while (at > 0 && previous[at] > 0)
        {
            changes.Add(previous[at]);
            at = previous[at];
        }

        changes.Reverse();
        double residual = best[n] - (changes.Count * penalty);
        return ([.. changes], residual);
    }

    /// <summary>The single best change point, MATLAB's <c>cpsingle</c>.</summary>
    public static (int[] Changes, double Residual) Single(
        double[,] y, ChangeStatistic statistic, int minimum)
    {
        int n = y.GetLength(1);
        if (n < 2 * minimum)
        {
            return ([], double.NaN);
        }

        int bestAt = -1;
        double bestCost = double.PositiveInfinity;
        for (int cut = minimum; cut <= n - minimum; cut++)
        {
            double cost = SegmentCost(y, 0, cut - 1, statistic) + SegmentCost(y, cut, n - 1, statistic);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestAt = cut;
            }
        }

        return bestAt < 0 ? ([], double.NaN) : ([bestAt], bestCost);
    }

    /// <summary>The residual when nothing changes, MATLAB's <c>cpnochange</c>.</summary>
    public static double NoChange(double[,] y, ChangeStatistic statistic)
    {
        int n = y.GetLength(1);
        return n == 0 ? double.NaN : SegmentCost(y, 0, n - 1, statistic);
    }

    /// <summary>
    /// MATLAB's <c>cpmulti</c>: the penalty is searched for until the segmentation has exactly the
    /// number of changes asked for, by halving, doubling and then bisecting.
    /// </summary>
    public static (int[] Changes, double Residual) AtMost(
        double[,] y, ChangeStatistic statistic, int minimum, int most)
    {
        double unsegmented = NoChange(y, statistic);
        if (most == 0)
        {
            return ([], unsegmented);
        }

        (int[] finest, double finestResidual) = Segment(y, statistic, minimum, 0);
        if (most >= finest.Length)
        {
            return (finest, finestResidual);
        }

        (int[] single, double singleResidual) = Single(y, statistic, minimum);
        if (single.Length == 0)
        {
            return ([], unsegmented);
        }

        double penalty = unsegmented - singleResidual;
        (int[] changes, double residual) = Segment(y, statistic, minimum, penalty);
        double upper = double.PositiveInfinity;
        double lower = 0;
        int[] coarsest = changes;
        double coarsestResidual = residual;
        double residueMax = unsegmented;
        double minimumResidual = finestResidual;

        while (changes.Length < most && residual >= minimumResidual)
        {
            upper = penalty;
            residueMax = residual;
            coarsest = changes;
            coarsestResidual = residual;
            penalty *= 0.5;
            (changes, residual) = Segment(y, statistic, minimum, penalty);
            if (changes.Length > most)
            {
                lower = penalty;
                minimumResidual = residual;
            }
        }

        while (changes.Length > most && residual <= residueMax && double.IsPositiveInfinity(upper))
        {
            lower = penalty;
            penalty *= 2;
            (changes, residual) = Segment(y, statistic, minimum, penalty);
            if (changes.Length < most)
            {
                upper = penalty;
                coarsest = changes;
                coarsestResidual = residual;
                residueMax = residual;
            }
        }

        if (changes.Length == most)
        {
            return (changes, residual);
        }

        penalty = (upper + lower) / 2;
        while (changes.Length != most && lower < penalty && penalty < upper)
        {
            (changes, residual) = Segment(y, statistic, minimum, penalty);
            if (changes.Length < most)
            {
                coarsest = changes;
                coarsestResidual = residual;
                upper = penalty;
            }
            else
            {
                lower = penalty;
            }

            penalty = (upper + lower) / 2;
        }

        return changes.Length == most ? (changes, residual) : (coarsest, coarsestResidual);
    }

    /// <summary>MATLAB's <c>cpmanual</c>: a segmentation only when one change beats the threshold.</summary>
    public static (int[] Changes, double Residual) AboveThreshold(
        double[,] y, ChangeStatistic statistic, int minimum, double threshold)
    {
        double unsegmented = NoChange(y, statistic);
        (int[] single, double singleResidual) = Single(y, statistic, minimum);
        if (single.Length == 0 || threshold > unsegmented - singleResidual)
        {
            return ([], unsegmented);
        }

        return Segment(y, statistic, minimum, threshold);
    }
}
