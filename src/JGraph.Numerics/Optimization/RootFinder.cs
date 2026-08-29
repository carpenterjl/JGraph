namespace JGraph.Numerics.Optimization;

/// <summary>
/// Brent's zero finder, the routine behind MATLAB's <c>fzero</c>: an outward search for an interval
/// the function changes sign across, then bisection safeguarding a secant or inverse-quadratic step
/// inside it.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is Brent's <c>zeroin</c> as published in Forsythe, Malcolm and Moler,
/// <em>Computer Methods for Mathematical Computations</em> (1976). It keeps three points and takes
/// the interpolated step only when that step is well inside the bracket and less than half the one
/// before last; otherwise it bisects. Bisection alone would converge on any sign change but slowly,
/// and interpolation alone can leave the bracket entirely, so each covers what the other cannot.
/// </para>
/// <para>
/// A sign change is what the method needs and what it cannot manufacture. Given a single starting
/// guess it walks outwards in steps growing by a factor of sqrt 2 until the two ends disagree in
/// sign, which is why a function that never changes sign, or one whose zero is a touch rather than a
/// crossing, is reported as a failure to bracket rather than searched for indefinitely.
/// </para>
/// </remarks>
public static class RootFinder
{
    /// <summary>Why a root search ended, beyond the shared codes in <see cref="SearchExit"/>.</summary>
    public static class RootExit
    {
        /// <summary>The search met a NaN or an infinite function value and gave up.</summary>
        public const int NotFinite = -3;

        /// <summary>
        /// The search converged on a point where the function is larger than at either end of the
        /// bracket it started from, which is what a pole looks like from the inside.
        /// </summary>
        public const int NearSingularity = -5;

        /// <summary>The outward search ran to infinity without finding a sign change.</summary>
        public const int NoSignChange = -6;
    }

    /// <summary>One report from a running root search.</summary>
    /// <param name="Phase">Where in its life the search is.</param>
    /// <param name="Iteration">Zero-finding iterations taken so far.</param>
    /// <param name="IntervalIteration">Bracket-widening steps taken so far.</param>
    /// <param name="FunctionCount">Function evaluations spent so far.</param>
    /// <param name="Point">The current best estimate of the zero.</param>
    /// <param name="Value">The function there.</param>
    /// <param name="Procedure">What the step just taken was.</param>
    /// <param name="Low">The left end of the bracket.</param>
    /// <param name="LowValue">The function at <paramref name="Low"/>.</param>
    /// <param name="High">The right end of the bracket.</param>
    /// <param name="HighValue">The function at <paramref name="High"/>.</param>
    /// <param name="HasPoint">
    /// False on the opening report, where nothing has been evaluated and MATLAB hands a script's
    /// output function empty matrices rather than a made-up point.
    /// </param>
    public readonly record struct RootStep(
        SearchPhase Phase,
        int Iteration,
        int IntervalIteration,
        int FunctionCount,
        double Point,
        double Value,
        string Procedure,
        double Low,
        double LowValue,
        double High,
        double HighValue,
        bool HasPoint);

    /// <summary>Something watching a root search; returning true asks it to stop.</summary>
    public delegate bool RootWatcher(RootStep step);

    /// <summary>What the search found and why it stopped.</summary>
    /// <param name="Solution">The zero, or NaN when none was found.</param>
    /// <param name="Value">The function there.</param>
    /// <param name="ExitFlag">One of <see cref="SearchExit"/> or <see cref="RootExit"/>.</param>
    /// <param name="Iterations">Zero-finding iterations taken.</param>
    /// <param name="IntervalIterations">Bracket-widening steps taken.</param>
    /// <param name="FunctionCount">Function evaluations spent.</param>
    /// <param name="Low">The left end of the bracket the zero was sought in.</param>
    /// <param name="LowValue">The function at <paramref name="Low"/>.</param>
    /// <param name="High">The right end of the bracket.</param>
    /// <param name="HighValue">The function at <paramref name="High"/>.</param>
    /// <param name="FailedAt">
    /// Where the search met a value it could not use, for the message that reports it; NaN when it
    /// did not.
    /// </param>
    /// <param name="FailedValue">The unusable value itself.</param>
    public readonly record struct Result(
        double Solution,
        double Value,
        int ExitFlag,
        int Iterations,
        int IntervalIterations,
        int FunctionCount,
        double Low,
        double LowValue,
        double High,
        double HighValue,
        double FailedAt,
        double FailedValue);

    /// <summary>
    /// Finds a zero of <paramref name="f"/> near <paramref name="guess"/>, first searching outwards
    /// for a sign change.
    /// </summary>
    /// <param name="f">The function whose zero is wanted.</param>
    /// <param name="guess">Where to start looking.</param>
    /// <param name="tolerance">The relative tolerance on the answer; zero for the double epsilon.</param>
    /// <param name="watcher">Optional; asked at each step.</param>
    /// <exception cref="ArgumentException">
    /// The function at <paramref name="guess"/> is not finite, so there is nothing to search from.
    /// </exception>
    public static Result Solve(
        Func<double, double> f, double guess, double tolerance = 0, RootWatcher? watcher = null)
    {
        ArgumentNullException.ThrowIfNull(f);

        int evaluations = 0;
        int intervalIterations = 0;

        if (Report(watcher, SearchPhase.Init, 0, 0, 0, double.NaN, double.NaN, " ",
            double.NaN, double.NaN, double.NaN, double.NaN, hasPoint: false) is { } atInit)
        {
            return atInit;
        }

        double atGuess = f(guess);
        evaluations++;

        if (atGuess == 0.0)
        {
            watcher?.Invoke(new RootStep(SearchPhase.Done, 0, 0, evaluations, guess, atGuess,
                " ", double.NaN, double.NaN, double.NaN, double.NaN, HasPoint: true));
            return new Result(guess, atGuess, SearchExit.Converged, 0, 0, evaluations,
                guess, atGuess, guess, atGuess, double.NaN, double.NaN);
        }

        if (!double.IsFinite(atGuess))
        {
            throw new ArgumentException(
                "The function value at the starting guess must be a finite real number.", nameof(guess));
        }

        double low = guess;
        double high = guess;
        double lowValue = atGuess;
        double highValue = atGuess;
        double width = guess != 0 ? guess / 50 : 1.0 / 50;
        double growth = Math.Sqrt(2);

        if (Report(watcher, SearchPhase.Iterate, 0, intervalIterations, evaluations, guess, atGuess,
            "initial interval", low, lowValue, high, highValue, hasPoint: true) is { } atOpening)
        {
            return atOpening;
        }

        // Walk outwards, alternating sides, until the two ends disagree in sign. Widening by a
        // constant factor rather than a constant step is what lets a zero many orders of magnitude
        // away from the guess still be reached in a handful of evaluations.
        while ((lowValue > 0) == (highValue > 0))
        {
            intervalIterations++;
            width *= growth;

            low = guess - width;
            lowValue = f(low);
            evaluations++;
            if (!double.IsFinite(lowValue) || !double.IsFinite(low))
            {
                return GaveUp(watcher, low, lowValue, 0, intervalIterations, evaluations);
            }

            if ((lowValue > 0) != (highValue > 0))
            {
                if (Report(watcher, SearchPhase.Iterate, 0, intervalIterations, evaluations,
                    guess, atGuess, "search", low, lowValue, high, highValue, hasPoint: true)
                    is { } atLeft)
                {
                    return atLeft;
                }

                break;
            }

            high = guess + width;
            highValue = f(high);
            if (!double.IsFinite(highValue) || !double.IsFinite(high))
            {
                return GaveUp(watcher, high, highValue, 0, intervalIterations, evaluations);
            }

            evaluations++;
            if (Report(watcher, SearchPhase.Iterate, 0, intervalIterations, evaluations,
                guess, atGuess, "search", low, lowValue, high, highValue, hasPoint: true)
                is { } atRight)
            {
                return atRight;
            }
        }

        return SolveBracketed(
            f, low, lowValue, high, highValue, tolerance, watcher, intervalIterations, evaluations);
    }

    /// <summary>
    /// Finds the zero inside a bracket whose ends already disagree in sign, which is the half of the
    /// work MATLAB does when <c>fzero</c> is handed a two-element interval.
    /// </summary>
    /// <param name="f">The function whose zero is wanted.</param>
    /// <param name="low">The left end.</param>
    /// <param name="lowValue">The function there.</param>
    /// <param name="high">The right end.</param>
    /// <param name="highValue">The function there.</param>
    /// <param name="tolerance">The relative tolerance on the answer; zero for the double epsilon.</param>
    /// <param name="watcher">Optional; asked at each step.</param>
    /// <param name="intervalIterations">Bracket-widening steps already spent.</param>
    /// <param name="evaluations">Function evaluations already spent.</param>
    public static Result SolveBracketed(
        Func<double, double> f,
        double low,
        double lowValue,
        double high,
        double highValue,
        double tolerance = 0,
        RootWatcher? watcher = null,
        int intervalIterations = 0,
        int evaluations = 0)
    {
        ArgumentNullException.ThrowIfNull(f);
        double toler = tolerance > 0 ? tolerance : Math.Pow(2, -52);

        double savedLow = low;
        double savedLowValue = lowValue;
        double savedHigh = high;
        double savedHighValue = highValue;

        // a is the previous iterate, b the current best, c the contrapoint: the end of the bracket
        // whose sign differs from b's.
        double a = low;
        double fa = lowValue;
        double b = high;
        double fb = highValue;
        double c = b;
        double fc = fb;
        double step = 0;
        double stepBeforeLast = 0;
        int iteration = 0;
        string procedure = "initial";

        while (fb != 0 && a != b)
        {
            if ((fb > 0) == (fc > 0))
            {
                c = a;
                fc = fa;
                step = b - a;
                stepBeforeLast = step;
            }

            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b;
                b = c;
                c = a;
                fa = fb;
                fb = fc;
                fc = fa;
            }

            double half = 0.5 * (c - b);
            double close = 2.0 * toler * Math.Max(Math.Abs(b), 1.0);
            if (Math.Abs(half) <= close || fb == 0.0)
            {
                break;
            }

            if (Report(watcher, SearchPhase.Iterate, iteration, intervalIterations, evaluations,
                b, fb, procedure, savedLow, savedLowValue, savedHigh, savedHighValue, hasPoint: true)
                is { } midway)
            {
                return midway;
            }

            if (Math.Abs(stepBeforeLast) < close || Math.Abs(fa) <= Math.Abs(fb))
            {
                // The last step was already down at the tolerance, or the previous iterate is no
                // worse than the current one: interpolation has nothing to offer, so bisect.
                step = half;
                stepBeforeLast = half;
                procedure = "bisection";
            }
            else
            {
                double p;
                double q;
                double s = fb / fa;
                if (a == c)
                {
                    // Only two distinct points: the secant through them.
                    p = 2.0 * half * s;
                    q = 1.0 - s;
                }
                else
                {
                    // Three distinct points: x as a quadratic in f, evaluated at f = 0.
                    q = fa / fc;
                    double r = fb / fc;
                    p = s * ((2.0 * half * q * (q - r)) - ((b - a) * (r - 1.0)));
                    q = (q - 1.0) * (r - 1.0) * (s - 1.0);
                }

                if (p > 0)
                {
                    q = -q;
                }
                else
                {
                    p = -p;
                }

                if (2.0 * p < (3.0 * half * q) - Math.Abs(close * q) && p < Math.Abs(0.5 * stepBeforeLast * q))
                {
                    stepBeforeLast = step;
                    step = p / q;
                    procedure = "interpolation";
                }
                else
                {
                    step = half;
                    stepBeforeLast = half;
                    procedure = "bisection";
                }
            }

            a = b;
            fa = fb;
            if (Math.Abs(step) > close)
            {
                b += step;
            }
            else if (b > c)
            {
                b -= close;
            }
            else
            {
                b += close;
            }

            fb = f(b);
            evaluations++;
            iteration++;
        }

        if (Report(watcher, SearchPhase.Iterate, iteration, intervalIterations, evaluations,
            b, fb, procedure, savedLow, savedLowValue, savedHigh, savedHighValue, hasPoint: true)
            is { } atEnd)
        {
            return atEnd;
        }

        // A zero the search walked to whose function value exceeds both ends of the bracket it
        // started from is not a zero at all: it is a pole, and the sign change either side of it is
        // the sign change the bracket found.
        int exit = Math.Abs(fb) > Math.Max(Math.Abs(savedLowValue), Math.Abs(savedHighValue))
            ? RootExit.NearSingularity
            : SearchExit.Converged;

        watcher?.Invoke(new RootStep(SearchPhase.Done, iteration, intervalIterations, evaluations,
            b, fb, procedure, savedLow, savedLowValue, savedHigh, savedHighValue, HasPoint: true));

        return new Result(b, fb, exit, iteration, intervalIterations, evaluations,
            savedLow, savedLowValue, savedHigh, savedHighValue, double.NaN, double.NaN);
    }

    /// <summary>
    /// The result of meeting a value the search cannot use: a NaN or infinite function value stops
    /// it where it stands, and an infinite abscissa means the outward walk ran off the line without
    /// ever finding a sign change.
    /// </summary>
    private static Result GaveUp(
        RootWatcher? watcher, double at, double value, int iteration, int intervalIterations, int evaluations)
    {
        int exit = double.IsFinite(at) ? RootExit.NotFinite : RootExit.NoSignChange;
        watcher?.Invoke(new RootStep(SearchPhase.Done, iteration, intervalIterations, evaluations,
            double.NaN, double.NaN, " ", double.NaN, double.NaN, double.NaN, double.NaN,
            HasPoint: false));

        return new Result(double.NaN, double.NaN, exit, iteration, intervalIterations, evaluations,
            double.NaN, double.NaN, double.NaN, double.NaN, at, value);
    }

    /// <summary>
    /// Hands a step to <paramref name="watcher"/> and, when it asks to stop, the result the search
    /// should give back.
    /// </summary>
    private static Result? Report(
        RootWatcher? watcher,
        SearchPhase phase,
        int iteration,
        int intervalIterations,
        int evaluations,
        double point,
        double value,
        string procedure,
        double low,
        double lowValue,
        double high,
        double highValue,
        bool hasPoint)
    {
        if (watcher is null
            || !watcher(new RootStep(phase, iteration, intervalIterations, evaluations, point, value,
                procedure, low, lowValue, high, highValue, hasPoint)))
        {
            return null;
        }

        return new Result(point, value, SearchExit.StoppedByWatcher, iteration, intervalIterations,
            evaluations, low, lowValue, high, highValue, double.NaN, double.NaN);
    }
}
