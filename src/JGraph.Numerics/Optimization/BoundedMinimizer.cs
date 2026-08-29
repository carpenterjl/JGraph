namespace JGraph.Numerics.Optimization;

/// <summary>
/// Brent's minimizer on a closed interval, the routine behind MATLAB's <c>fminbnd</c>: golden
/// section search with a parabolic interpolation taken whenever the parabola through the three best
/// points lands somewhere the search is willing to go.
/// </summary>
/// <remarks>
/// The algorithm is Brent's <c>fmin</c> as published in Forsythe, Malcolm and Moler,
/// <em>Computer Methods for Mathematical Computations</em> (1976). The golden step alone converges
/// on any continuous function but slowly; the parabolic step is fast near a smooth minimum but can
/// be wild, so it is accepted only when it is smaller than half the step before last and lands
/// strictly inside the bracket. That guard is what makes the pair reliable, and it is why the
/// procedure name the search reports alternates between <c>parabolic</c> and <c>golden</c>.
/// </remarks>
public static class BoundedMinimizer
{
    /// <summary>The golden-section fraction, (3 - sqrt 5) / 2.</summary>
    private static readonly double GoldenFraction = 0.5 * (3.0 - Math.Sqrt(5.0));

    /// <summary>The square root of the double epsilon, the relative floor on the tolerance.</summary>
    private static readonly double SquareRootEpsilon = Math.Sqrt(Math.Pow(2, -52));

    /// <summary>How the search is allowed to run and when it should stop.</summary>
    /// <param name="MaxIterations">Iterations allowed, or zero for 500.</param>
    /// <param name="MaxFunctionEvaluations">Evaluations allowed, or zero for 500.</param>
    /// <param name="ToleranceX">How close the bracket must close; zero for 1e-4.</param>
    public readonly record struct Settings(
        int MaxIterations = 0,
        int MaxFunctionEvaluations = 0,
        double ToleranceX = 0);

    /// <summary>What the search found and why it stopped.</summary>
    /// <param name="Solution">The best point seen.</param>
    /// <param name="Value">The objective there.</param>
    /// <param name="ExitFlag">One of <see cref="SearchExit"/>.</param>
    /// <param name="Iterations">Iterations taken.</param>
    /// <param name="FunctionCount">Objective evaluations spent.</param>
    public readonly record struct Result(
        double Solution, double Value, int ExitFlag, int Iterations, int FunctionCount);

    /// <summary>
    /// Minimizes <paramref name="objective"/> over the closed interval from <paramref name="lower"/>
    /// to <paramref name="upper"/>.
    /// </summary>
    /// <param name="objective">The function to minimize.</param>
    /// <param name="lower">The left end of the interval.</param>
    /// <param name="upper">The right end; must not be to the left of <paramref name="lower"/>.</param>
    /// <param name="settings">Budget and tolerance.</param>
    /// <param name="watcher">Optional; asked before the first step and after every iteration.</param>
    /// <exception cref="ArgumentException">The interval runs backwards.</exception>
    public static Result Minimize(
        Func<double, double> objective,
        double lower,
        double upper,
        Settings settings = default,
        SearchWatcher? watcher = null)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (lower > upper)
        {
            throw new ArgumentException(
                "The lower bound must not be above the upper bound.", nameof(lower));
        }

        int maxIterations = settings.MaxIterations > 0 ? settings.MaxIterations : 500;
        int maxEvaluations = settings.MaxFunctionEvaluations > 0 ? settings.MaxFunctionEvaluations : 500;
        double tolerance = settings.ToleranceX > 0 ? settings.ToleranceX : 1e-4;

        int evaluations = 0;
        int iteration = 0;
        string procedure = "initial";

        // Nothing has been evaluated yet, and MATLAB says so rather than inventing a point: the
        // opening report carries an empty point and a NaN value, which the caller turns back into
        // the empty matrices a script's output function is handed.
        if (Report(watcher, SearchPhase.Init, iteration, evaluations, double.NaN, procedure, [])
            is { } atInit)
        {
            return atInit;
        }

        double a = lower;
        double b = upper;

        // The first probe sits a golden fraction in from the left, and becomes all three of the
        // best, second-best and third-best points until there is anything else to compare it with.
        double best = a + (GoldenFraction * (b - a));
        double second = best;
        double third = best;
        double bestValue = objective(best);
        evaluations++;
        double secondValue = bestValue;
        double thirdValue = bestValue;

        double step = 0.0;
        double stepBeforeLast = 0.0;

        if (Report(watcher, SearchPhase.Iterate, iteration, evaluations, bestValue, procedure, [best])
            is { } afterFirst)
        {
            return afterFirst;
        }

        double middle = 0.5 * (a + b);
        double closeEnough = (SquareRootEpsilon * Math.Abs(best)) + (tolerance / 3.0);
        double twiceCloseEnough = 2.0 * closeEnough;
        int exit = SearchExit.Converged;

        while (Math.Abs(best - middle) > twiceCloseEnough - (0.5 * (b - a)))
        {
            bool golden = true;

            // A parabola needs three distinct points and a step before last big enough to be worth
            // measuring against; without one the search has nothing to be cautious about yet.
            if (Math.Abs(stepBeforeLast) > closeEnough)
            {
                golden = false;
                double r = (best - second) * (bestValue - thirdValue);
                double q = (best - third) * (bestValue - secondValue);
                double p = ((best - third) * q) - ((best - second) * r);
                q = 2.0 * (q - r);
                if (q > 0.0)
                {
                    p = -p;
                }

                q = Math.Abs(q);
                double lastSpan = stepBeforeLast;
                stepBeforeLast = step;

                if (Math.Abs(p) < Math.Abs(0.5 * q * lastSpan)
                    && p > q * (a - best)
                    && p < q * (b - best))
                {
                    step = p / q;
                    double candidate = best + step;
                    procedure = "parabolic";

                    // The objective must not be sampled on top of either end of the bracket.
                    if (candidate - a < twiceCloseEnough || b - candidate < twiceCloseEnough)
                    {
                        step = closeEnough * SignOrPositive(middle - best);
                    }
                }
                else
                {
                    golden = true;
                }
            }

            if (golden)
            {
                stepBeforeLast = best >= middle ? a - best : b - best;
                step = GoldenFraction * stepBeforeLast;
                procedure = "golden";
            }

            // Nor may it be sampled on top of the best point itself, however small the step wanted
            // to be.
            double at = best + (SignOrPositive(step) * Math.Max(Math.Abs(step), closeEnough));
            double value = objective(at);
            evaluations++;
            iteration++;

            if (Report(watcher, SearchPhase.Iterate, iteration, evaluations, value, procedure, [at])
                is { } midway)
            {
                return midway;
            }

            if (value <= bestValue)
            {
                // A new best: the bracket closes on the side the old best was on, and the three
                // remembered points all shift down one.
                if (at >= best)
                {
                    a = best;
                }
                else
                {
                    b = best;
                }

                third = second;
                thirdValue = secondValue;
                second = best;
                secondValue = bestValue;
                best = at;
                bestValue = value;
            }
            else
            {
                if (at < best)
                {
                    a = at;
                }
                else
                {
                    b = at;
                }

                if (value <= secondValue || second == best)
                {
                    third = second;
                    thirdValue = secondValue;
                    second = at;
                    secondValue = value;
                }
                else if (value <= thirdValue || third == best || third == second)
                {
                    third = at;
                    thirdValue = value;
                }
            }

            middle = 0.5 * (a + b);
            closeEnough = (SquareRootEpsilon * Math.Abs(best)) + (tolerance / 3.0);
            twiceCloseEnough = 2.0 * closeEnough;

            if (evaluations >= maxEvaluations || iteration >= maxIterations)
            {
                exit = SearchExit.BudgetExhausted;
                break;
            }
        }

        watcher?.Invoke(new SearchStep(
            SearchPhase.Done, iteration, evaluations, bestValue, procedure, [best]));
        return new Result(best, bestValue, exit, iteration, evaluations);
    }

    /// <summary>
    /// The sign of <paramref name="value"/>, counting zero as positive, which is what makes a step
    /// of exactly zero still move the search to the right rather than nowhere.
    /// </summary>
    private static double SignOrPositive(double value) => value < 0 ? -1.0 : 1.0;

    /// <summary>
    /// Hands a step to <paramref name="watcher"/> and, when it asks to stop, the result the search
    /// should give back.
    /// </summary>
    private static Result? Report(
        SearchWatcher? watcher, SearchPhase phase, int iteration, int evaluations,
        double value, string procedure, double[] point)
    {
        if (watcher is null
            || !watcher(new SearchStep(phase, iteration, evaluations, value, procedure, point)))
        {
            return null;
        }

        return new Result(
            point.Length > 0 ? point[0] : double.NaN,
            value,
            SearchExit.StoppedByWatcher,
            iteration,
            evaluations);
    }
}
