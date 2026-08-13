namespace JGraph.Maths.Sampling;

/// <summary>
/// Chooses where to read a curve so that the straight lines drawn between the readings look like the
/// curve. Every function plotter is this sampler plus a drawing.
/// </summary>
/// <remarks>
/// <para>
/// The rule is one sentence: <b>a straight line between two readings is accepted when the curve does
/// not depart from it</b>. The sampler takes an even set of readings, then repeatedly probes inside
/// every interval it has not yet accepted and splits the ones whose probes miss the chord. Curvature
/// therefore buys points and flatness does not, which is the whole reason to sample adaptively — a
/// uniform grid dense enough for the sharp part of a curve is wasted everywhere else on it.
/// </para>
/// <para>
/// The probes sit at a third and two thirds of the way across rather than at the middle. A single
/// midpoint probe can be fooled by a curve that happens to cross its own chord there, which is not a
/// rare accident but exactly what a periodic function does when the grid lands on its zeros.
/// </para>
/// <para>
/// The function is asked for a whole round of probes at once so that a caller who can evaluate an
/// array in one go — which a script's function handle usually can — is not made to answer one
/// parameter at a time.
/// </para>
/// <para>
/// Two kinds of reading become gaps rather than points. One the function gives directly: an infinite
/// or undefined value is a gap where it stands. The other the sampler decides: a value that has run
/// away from the middle of the readings by more than <see cref="AdaptiveSamplerOptions.PoleFactor"/>
/// spreads is the curve leaving rather than a reading of it, and drawing it would put a wall across
/// the picture where a break belongs. Refinement is what makes such a gap narrow, so the deciding
/// happens after the refinement rather than during it.
/// </para>
/// </remarks>
public static class AdaptiveSampler1D
{
    /// <summary>Samples a single-valued function of one parameter over <c>[a, b]</c>.</summary>
    public static AdaptiveSamples Sample(
        Func<double, double> f,
        double a,
        double b,
        AdaptiveSamplerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(f);
        return Sample(
            parameters =>
            {
                var row = new double[parameters.Count];
                for (int i = 0; i < parameters.Count; i++)
                {
                    row[i] = f(parameters[i]);
                }

                return [row];
            },
            1,
            a,
            b,
            options);
    }

    /// <summary>
    /// Samples a curve of <paramref name="components"/> components over <c>[a, b]</c>. The evaluator
    /// is handed every parameter of a round at once and answers with one row per component.
    /// </summary>
    public static AdaptiveSamples Sample(
        Func<IReadOnlyList<double>, double[][]> evaluate,
        int components,
        double a,
        double b,
        AdaptiveSamplerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentOutOfRangeException.ThrowIfLessThan(components, 1);

        AdaptiveSamplerOptions settings = options ?? new AdaptiveSamplerOptions();
        if (!double.IsFinite(a) || !double.IsFinite(b) || a >= b)
        {
            throw new ArgumentException("The domain must run from a finite value to a larger one.", nameof(a));
        }

        int seedCount = System.Math.Max(3, settings.SeedCount);
        var seeds = new double[seedCount];
        for (int i = 0; i < seedCount; i++)
        {
            seeds[i] = a + ((b - a) * i / (seedCount - 1));
        }

        seeds[^1] = b;

        var nodes = new List<Node>(seedCount);
        double[][] seedValues = Evaluate(evaluate, seeds, components);
        for (int i = 0; i < seedCount; i++)
        {
            nodes.Add(new Node(seeds[i], Column(seedValues, i, components)));
        }

        // The spread and the middle are read from the even pass and then held fixed. Letting them
        // follow the refinement would move the target: every probe taken next to a pole would widen
        // the spread that decides whether the probe after it is a pole.
        double[] spread = new double[components];
        double[] centre = new double[components];
        for (int k = 0; k < components; k++)
        {
            (spread[k], centre[k]) = ReadingSpread.Of(seedValues[k]);
        }

        Refine(evaluate, nodes, components, spread, settings);

        var parameters = new List<double>(nodes.Count);
        var rows = new List<double>[components];
        for (int k = 0; k < components; k++)
        {
            rows[k] = new List<double>(nodes.Count);
        }

        int poles = 0;
        bool previousWasGap = false;
        var reading = new double[components];
        foreach (Node node in nodes)
        {
            bool ranAway = false;
            bool gap = true;
            for (int k = 0; k < components; k++)
            {
                double value = node.Values[k];
                if (ReadingSpread.RanAway(value, centre[k], spread[k], settings.PoleFactor))
                {
                    value = double.NaN;
                    ranAway = true;
                }

                reading[k] = double.IsFinite(value) ? value : double.NaN;
                gap &= double.IsNaN(reading[k]);
            }

            // Refinement crowds readings against a pole, and every one of them is the same break.
            // Keeping the first and dropping the rest is what makes the gap one gap.
            if (gap && previousWasGap)
            {
                continue;
            }

            previousWasGap = gap;
            parameters.Add(node.Parameter);
            for (int k = 0; k < components; k++)
            {
                rows[k].Add(reading[k]);
            }

            if (ranAway)
            {
                poles++;
            }
        }

        return new AdaptiveSamples(
            [.. parameters], [.. rows.Select(row => row.ToArray())], poles);
    }

    /// <summary>
    /// Splits every interval whose probes miss the chord, a whole round at a time, until each has been
    /// accepted or the budget runs out.
    /// </summary>
    private static void Refine(
        Func<IReadOnlyList<double>, double[][]> evaluate,
        List<Node> nodes,
        int components,
        double[] spread,
        AdaptiveSamplerOptions settings)
    {
        for (int round = 0; round < settings.MaxRounds; round++)
        {
            var probes = new List<double>();
            var open = new List<int>();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                if (nodes[i].AcceptedToNext)
                {
                    continue;
                }

                double left = nodes[i].Parameter;
                double step = nodes[i + 1].Parameter - left;
                if (step <= 0 || !double.IsFinite(step))
                {
                    continue;
                }

                open.Add(i);
                probes.Add(left + (step / 3));
                probes.Add(left + (2 * step / 3));
            }

            if (open.Count == 0 || nodes.Count + probes.Count > settings.MaxPoints)
            {
                return;
            }

            double[][] values = Evaluate(evaluate, probes, components);
            var next = new List<Node>(nodes.Count + probes.Count);
            int probeIndex = 0;
            int openIndex = 0;

            for (int i = 0; i < nodes.Count - 1; i++)
            {
                next.Add(nodes[i]);
                if (openIndex >= open.Count || open[openIndex] != i)
                {
                    continue;
                }

                openIndex++;
                double[] first = Column(values, probeIndex, components);
                double[] second = Column(values, probeIndex + 1, components);
                probeIndex += 2;

                if (OnTheChord(nodes[i].Values, nodes[i + 1].Values, first, second, components, spread, settings.Tolerance))
                {
                    next[^1] = nodes[i] with { AcceptedToNext = true };
                    continue;
                }

                next.Add(new Node(probes[probeIndex - 2], first));
                next.Add(new Node(probes[probeIndex - 1], second));
            }

            next.Add(nodes[^1]);
            nodes.Clear();
            nodes.AddRange(next);
        }
    }

    /// <summary>
    /// Whether both probes sit close enough to the straight line between the interval's ends, in every
    /// component. A probe the function could not answer for never counts as close: the interval is
    /// split again so that the gap it leaves is a narrow one.
    /// </summary>
    private static bool OnTheChord(
        double[] left,
        double[] right,
        double[] first,
        double[] second,
        int components,
        double[] spread,
        double tolerance)
    {
        for (int k = 0; k < components; k++)
        {
            if (!double.IsFinite(first[k]) || !double.IsFinite(second[k])
                || !double.IsFinite(left[k]) || !double.IsFinite(right[k]))
            {
                return false;
            }

            double limit = tolerance * spread[k];
            double chordAtThird = left[k] + ((right[k] - left[k]) / 3);
            double chordAtTwoThirds = left[k] + (2 * (right[k] - left[k]) / 3);
            if (System.Math.Abs(first[k] - chordAtThird) > limit
                || System.Math.Abs(second[k] - chordAtTwoThirds) > limit)
            {
                return false;
            }
        }

        return true;
    }

    private static double[][] Evaluate(
        Func<IReadOnlyList<double>, double[][]> evaluate,
        IReadOnlyList<double> parameters,
        int components)
    {
        double[][] values = evaluate(parameters)
            ?? throw new InvalidOperationException("The sampled function answered with nothing.");
        if (values.Length != components)
        {
            throw new InvalidOperationException(
                $"The sampled function answered with {values.Length} components where {components} were expected.");
        }

        foreach (double[] row in values)
        {
            if (row.Length != parameters.Count)
            {
                throw new InvalidOperationException(
                    "The sampled function answered with a different number of readings than it was asked for.");
            }
        }

        return values;
    }

    private static double[] Column(double[][] values, int index, int components)
    {
        var column = new double[components];
        for (int k = 0; k < components; k++)
        {
            column[k] = values[k][index];
        }

        return column;
    }

    /// <summary>
    /// One reading, and whether the straight line from it to the next reading has been accepted as a
    /// fair drawing of the curve between them.
    /// </summary>
    private readonly record struct Node(double Parameter, double[] Values, bool AcceptedToNext = false);
}
