namespace JGraph.Signal;

/// <summary>
/// The bilevel measurements: the two states a pulse waveform spends its time in, the transitions
/// between them, and everything measured off those transitions.
/// </summary>
/// <remarks>
/// One idea runs through all twelve names. The two state levels come from a histogram of the
/// samples, split at its own midpoint; the levels give a lower and an upper bound with a tolerance
/// either side; a transition is a pair of consecutive out-of-band samples that lie on opposite sides
/// of the band; and every measurement is a crossing time interpolated between two samples. Nothing
/// here searches for edges by differentiating, which is why a noisy waveform gives the same answer
/// as a clean one.
/// </remarks>
public static class BilevelWaveforms
{
    /// <summary>How the two state levels are read out of the histogram.</summary>
    public enum LevelMethod
    {
        /// <summary>The most populated bin of each half.</summary>
        Mode,

        /// <summary>The centre of mass of each half.</summary>
        Mean,
    }

    /// <summary>The histogram of a signal between two bounds, and the bin centres.</summary>
    public static (double[] Counts, double[] Bins) Histogram(
        double[] x, int bins, double lower, double upper)
    {
        var counts = new double[bins];
        double denominator = upper - lower;
        if (denominator == 0)
        {
            denominator = 2.220446049250313e-16;
        }

        foreach (double value in x)
        {
            double position = bins * (value - lower) / denominator;
            int index = (int)System.Math.Ceiling(position) + (position == 0 ? 1 : 0);
            if (index >= 1 && index <= bins)
            {
                counts[index - 1]++;
            }
        }

        var centres = new double[bins];
        for (int i = 0; i < bins; i++)
        {
            centres[i] = lower + ((i + 0.5) * (upper - lower) / bins);
        }

        return (counts, centres);
    }

    /// <summary>MATLAB's <c>statelevels</c>: the low and high states of a bilevel waveform.</summary>
    public static double[] StateLevels(double[] counts, double lower, double upper, LevelMethod method)
    {
        double step = (upper - lower) / counts.Length;
        int first = -1;
        int last = -1;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0)
            {
                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }
        }

        if (first < 0)
        {
            return [double.NaN, double.NaN];
        }

        int lowLow = first + 1;
        int lowHigh = first + 1 + (int)System.Math.Floor((last - first) / 2.0);
        int highLow = lowHigh;
        int highHigh = last + 1;
        if (method == LevelMethod.Mode)
        {
            int lowPeak = lowLow;
            for (int i = lowLow; i <= lowHigh; i++)
            {
                if (counts[i - 1] > counts[lowPeak - 1])
                {
                    lowPeak = i;
                }
            }

            int highPeak = highLow;
            for (int i = highLow; i <= highHigh; i++)
            {
                if (counts[i - 1] > counts[highPeak - 1])
                {
                    highPeak = i;
                }
            }

            return
            [
                lower + (step * (lowPeak - 0.5)),
                lower + (step * (highPeak - 0.5)),
            ];
        }

        double lowMoment = 0;
        double lowTotal = 0;
        for (int i = lowLow; i <= lowHigh; i++)
        {
            lowMoment += (i - 0.5) * counts[i - 1];
            lowTotal += counts[i - 1];
        }

        double highMoment = 0;
        double highTotal = 0;
        for (int i = highLow; i <= highHigh; i++)
        {
            highMoment += (i - 0.5) * counts[i - 1];
            highTotal += counts[i - 1];
        }

        return [lower + (step * lowMoment / lowTotal), lower + (step * highMoment / highTotal)];
    }

    /// <summary>The reference level a percentage of the way between the two states.</summary>
    public static double Reference(double[] levels, double percent) =>
        levels[0] + (percent * (levels[1] - levels[0]) / 100);

    /// <summary>Straight-line interpolation between two samples.</summary>
    public static double Interpolate(double ya, double yb, double xa, double xb, double x) =>
        ya + ((yb - ya) * (x - xa) / (xb - xa));

    /// <summary>The crossings of the mid reference level, with the direction of each.</summary>
    public readonly record struct Crossings(
        double[] Times, int[] Polarity, int[] Pre, int[] Post, int Last);

    /// <summary>MATLAB's <c>getMidCross</c>.</summary>
    public static Crossings MidCrossings(
        double[] x, double[] t, double upperBound, double lowerBound, double midRef)
    {
        var state = new List<int>();
        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] < lowerBound || x[i] > upperBound)
            {
                state.Add(i);
            }
        }

        var pre = new List<int>();
        var post = new List<int>();
        for (int i = 0; i + 1 < state.Count; i++)
        {
            int a = state[i];
            int b = state[i + 1];
            bool rising = x[a] < lowerBound && upperBound < x[b];
            bool falling = x[a] > upperBound && lowerBound > x[b];
            if (rising || falling)
            {
                pre.Add(a);
                post.Add(b);
            }
        }

        var polarity = new int[pre.Count];
        var times = new double[pre.Count];
        for (int i = 0; i < pre.Count; i++)
        {
            polarity[i] = x[pre[i]] < lowerBound ? 1 : -1;
            int mid = MidIndex(x, pre[i], post[i], midRef, polarity[i]);
            times[i] = Interpolate(t[mid], t[mid + 1], x[mid], x[mid + 1], midRef);
        }

        return new Crossings(times, polarity, [.. pre], [.. post], state.Count > 0 ? state[^1] : 0);
    }

    private static int MidIndex(double[] x, int a, int b, double reference, int polarity)
    {
        for (int i = a; i < b; i++)
        {
            bool hit = polarity > 0
                ? x[i] <= reference && reference < x[i + 1]
                : x[i] >= reference && reference > x[i + 1];
            if (hit)
            {
                return i;
            }
        }

        return a;
    }

    /// <summary>Everything a transition is measured by.</summary>
    public sealed class Transitions
    {
        /// <summary>How long each transition took between the two percent reference levels.</summary>
        public double[] Duration { get; init; } = [];

        /// <summary>Whether each transition rose or fell.</summary>
        public int[] Polarity { get; init; } = [];

        /// <summary>The slope of each transition between its two reference levels.</summary>
        public double[] SlewRate { get; init; } = [];

        /// <summary>When each transition crossed the mid reference level.</summary>
        public double[] MiddleCross { get; init; } = [];

        /// <summary>When each transition crossed the lower reference level.</summary>
        public double[] LowerCross { get; init; } = [];

        /// <summary>When each transition crossed the upper reference level.</summary>
        public double[] UpperCross { get; init; } = [];

        /// <summary>The last sample before each transition began.</summary>
        public int[] Pre { get; init; } = [];

        /// <summary>The first sample after each transition ended.</summary>
        public int[] Post { get; init; } = [];
    }

    /// <summary>MATLAB's <c>getTransitions</c>.</summary>
    public static Transitions FindTransitions(
        double[] x, double[] t, double upperBound, double lowerBound,
        double upperRef, double midRef, double lowerRef)
    {
        Crossings crossings = MidCrossings(x, t, upperBound, lowerBound, midRef);
        int n = crossings.Polarity.Length;
        var duration = new double[n];
        var slew = new double[n];
        var middle = new double[n];
        var lower = new double[n];
        var upper = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = crossings.Pre[i];
            int b = crossings.Post[i];
            int polarity = crossings.Polarity[i];
            double preRef = polarity > 0 ? lowerRef : upperRef;
            double postRef = polarity > 0 ? upperRef : lowerRef;
            int mid = MidIndex(x, a, b, midRef, polarity);

            int preIndex = a;
            for (int i2 = a; i2 <= System.Math.Min(mid + 1, x.Length - 1); i2++)
            {
                bool hit = polarity > 0 ? x[i2] < preRef : x[i2] >= preRef;
                if (hit)
                {
                    preIndex = i2;
                }
            }

            int postIndex = mid;
            for (int i2 = mid; i2 <= b; i2++)
            {
                bool hit = polarity > 0 ? postRef < x[i2] : postRef > x[i2];
                if (hit)
                {
                    postIndex = i2 - 1;
                    break;
                }
            }

            middle[i] = Interpolate(t[mid], t[mid + 1], x[mid], x[mid + 1], midRef);
            double tPre = Interpolate(
                t[preIndex], t[preIndex + 1], x[preIndex], x[preIndex + 1], preRef);
            double tPost = Interpolate(
                t[postIndex], t[postIndex + 1], x[postIndex], x[postIndex + 1], postRef);
            duration[i] = tPost - tPre;
            slew[i] = (postRef - preRef) / duration[i];
            upper[i] = polarity > 0 ? tPost : tPre;
            lower[i] = polarity > 0 ? tPre : tPost;
        }

        return new Transitions
        {
            Duration = duration,
            Polarity = crossings.Polarity,
            SlewRate = slew,
            MiddleCross = middle,
            LowerCross = lower,
            UpperCross = upper,
            Pre = crossings.Pre,
            Post = crossings.Post,
        };
    }

    /// <summary>What a shoot measurement reports.</summary>
    public readonly record struct Shoots(
        double[] Overshoot, double[] Undershoot, double[] OvershootLevel, double[] UndershootLevel,
        double[] OvershootInstant, double[] UndershootInstant);

    /// <summary>MATLAB's <c>getPostshoots</c>: the excursion after each transition settles.</summary>
    public static Shoots Postshoots(
        double[] x, double[] t, double upperState, double lowerState,
        double upperBound, double lowerBound, double seekFactor, Transitions transitions)
    {
        int n = transitions.Polarity.Length;
        if (n == 0)
        {
            return new Shoots([], [], [], [], [], []);
        }

        var above = new double[n];
        var below = new double[n];
        var aboveAt = new int[n];
        var belowAt = new int[n];
        for (int i = 0; i < n; i++)
        {
            int polarity = transitions.Polarity[i];
            double bound = polarity > 0 ? upperBound : lowerBound;
            int post = transitions.Post[i];
            double tPost = Interpolate(t[post - 1], t[post], x[post - 1], x[post], bound);
            double seek = tPost + (seekFactor * transitions.Duration[i]);
            int stop = -1;
            for (int k = post; k < t.Length; k++)
            {
                if (t[k] > seek)
                {
                    stop = k;
                    break;
                }
            }

            if (i < n - 1 && (stop < 0 || stop > transitions.Pre[i + 1]))
            {
                stop = transitions.Pre[i + 1];
            }
            else if (stop < 0)
            {
                stop = x.Length - 1;
            }

            if (stop > post)
            {
                stop--;
            }

            aboveAt[i] = post;
            belowAt[i] = post;
            above[i] = x[post];
            below[i] = x[post];
            for (int k = post; k <= stop && k < x.Length; k++)
            {
                if (x[k] > above[i])
                {
                    above[i] = x[k];
                    aboveAt[i] = k;
                }

                if (x[k] < below[i])
                {
                    below[i] = x[k];
                    belowAt[i] = k;
                }
            }
        }

        return Report(x, t, transitions, above, below, aboveAt, belowAt, upperState, lowerState, after: true);
    }

    /// <summary>MATLAB's <c>getPreshoots</c>: the excursion before each transition begins.</summary>
    public static Shoots Preshoots(
        double[] x, double[] t, double upperState, double lowerState,
        double upperBound, double lowerBound, double seekFactor, Transitions transitions)
    {
        int n = transitions.Polarity.Length;
        if (n == 0)
        {
            return new Shoots([], [], [], [], [], []);
        }

        var above = new double[n];
        var below = new double[n];
        var aboveAt = new int[n];
        var belowAt = new int[n];
        for (int i = 0; i < n; i++)
        {
            int polarity = transitions.Polarity[i];
            double bound = polarity > 0 ? lowerBound : upperBound;
            int pre = transitions.Pre[i];
            double tPre = Interpolate(t[pre], t[pre + 1], x[pre], x[pre + 1], bound);
            double seek = tPre - (seekFactor * transitions.Duration[i]);
            int start = -1;
            for (int k = 0; k <= pre; k++)
            {
                if (t[k] > seek)
                {
                    start = k;
                    break;
                }
            }

            if (start < 0)
            {
                start = pre;
            }
            else if (i >= 2 && start < transitions.Post[i - 1])
            {
                start = transitions.Post[i - 1];
            }

            aboveAt[i] = start;
            belowAt[i] = start;
            above[i] = x[start];
            below[i] = x[start];
            for (int k = start; k <= pre; k++)
            {
                if (x[k] > above[i])
                {
                    above[i] = x[k];
                    aboveAt[i] = k;
                }

                if (x[k] < below[i])
                {
                    below[i] = x[k];
                    belowAt[i] = k;
                }
            }
        }

        return Report(x, t, transitions, above, below, aboveAt, belowAt, upperState, lowerState, after: false);
    }

    private static Shoots Report(
        double[] x, double[] t, Transitions transitions, double[] above, double[] below,
        int[] aboveAt, int[] belowAt, double upperState, double lowerState, bool after)
    {
        int n = transitions.Polarity.Length;
        var overshoot = new double[n];
        var undershoot = new double[n];
        var overLevel = new double[n];
        var underLevel = new double[n];
        var overInstant = new double[n];
        var underInstant = new double[n];
        double amplitude = upperState - lowerState;
        for (int i = 0; i < n; i++)
        {
            int polarity = transitions.Polarity[i];
            double state = after
                ? (polarity > 0 ? upperState : lowerState)
                : (polarity > 0 ? lowerState : upperState);
            overshoot[i] = (above[i] - state) / amplitude * 100;
            undershoot[i] = (state - below[i]) / amplitude * 100;
            overLevel[i] = x[aboveAt[i]];
            underLevel[i] = x[belowAt[i]];
            overInstant[i] = t[aboveAt[i]];
            underInstant[i] = t[belowAt[i]];
        }

        return new Shoots(overshoot, undershoot, overLevel, underLevel, overInstant, underInstant);
    }

    /// <summary>MATLAB's <c>getSettling</c>: when each transition last left the tolerance band.</summary>
    public static (double[] Duration, double[] Level, double[] Instant) Settling(
        double[] x, double[] t, double upperLevel, double lowerLevel, double tolerance,
        double seek, Transitions transitions)
    {
        int n = transitions.Polarity.Length;
        var duration = new double[n];
        var level = new double[n];
        var instant = new double[n];
        double amplitude = upperLevel - lowerLevel;
        for (int i = 0; i < n; i++)
        {
            double reference = transitions.Polarity[i] > 0 ? upperLevel : lowerLevel;
            double final = transitions.MiddleCross[i] + seek;
            int post = transitions.Post[i];
            int stop = -1;
            for (int k = post; k < t.Length; k++)
            {
                if (t[k] > final)
                {
                    stop = k;
                    break;
                }
            }

            bool unusable = stop < 0
                || final < t[post]
                || (i < n - 1 && stop > transitions.Pre[i + 1]);
            if (unusable)
            {
                instant[i] = double.NaN;
                level[i] = double.NaN;
                duration[i] = double.NaN;
                continue;
            }

            int pre = transitions.Pre[i];
            int last = -1;
            for (int k = pre; k <= stop; k++)
            {
                if (System.Math.Abs(x[k] - reference) > amplitude * tolerance / 100)
                {
                    last = k;
                }
            }

            if (last < 0 || last == stop)
            {
                instant[i] = double.NaN;
                level[i] = double.NaN;
                duration[i] = double.NaN;
                continue;
            }

            double intercept = System.Math.Sign(x[last] - reference) * amplitude * tolerance / 100;
            instant[i] = Interpolate(
                t[last], t[last + 1], x[last] - reference, x[last + 1] - reference, intercept);
            level[i] = Interpolate(x[last], x[last + 1], t[last], t[last + 1], instant[i]);
            duration[i] = instant[i] - transitions.MiddleCross[i];
        }

        return (duration, level, instant);
    }
}
