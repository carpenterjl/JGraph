namespace JGraph.Statistics.Sampling;

/// <summary>
/// Drawing from a distribution known only by a formula for its density, by walking a chain whose
/// long-run behaviour is that distribution.
/// </summary>
/// <remarks>
/// <para>
/// Both methods here need the density only up to a constant, which is the whole reason to use them: a
/// posterior is usually easy to write down and impossible to normalize. Both work in the logarithm,
/// because a density that a script can write is a density that can underflow, and comparing two
/// underflowed numbers compares nothing.
/// </para>
/// <para>
/// Both are deterministic under a seeded generator, and neither adapts: the proposal of the first and
/// the initial width of the second are the caller's, not tuned from the run. Tuning them from the run
/// would break the chain's own guarantee, which is the one thing that makes the answer mean anything.
/// </para>
/// </remarks>
public static class MarkovChain
{
    /// <summary>What a chain produced.</summary>
    /// <param name="Samples">One draw per row.</param>
    /// <param name="Accepted">The proportion of proposals the chain moved to.</param>
    /// <param name="Evaluations">How many times the density was asked.</param>
    public readonly record struct Chain(double[][] Samples, double Accepted, int Evaluations);

    /// <summary>
    /// Metropolis-Hastings: propose a move, and take it with the probability that keeps the chain
    /// reversible with respect to the target.
    /// </summary>
    /// <param name="start">Where the chain starts.</param>
    /// <param name="count">How many draws to keep.</param>
    /// <param name="logTarget">The logarithm of the target density, up to a constant.</param>
    /// <param name="propose">A proposal drawn from the current point.</param>
    /// <param name="logProposal">
    /// The logarithm of the proposal density of the first point given the second, or null when the
    /// proposal is symmetric and therefore cancels.
    /// </param>
    /// <param name="burnIn">How many draws to discard before keeping any.</param>
    /// <param name="thin">Keep one draw in this many.</param>
    public static Chain Metropolis(
        double[] start,
        int count,
        Func<double[], double> logTarget,
        Func<double[], double[]> propose,
        Func<double[], double[], double>? logProposal,
        int burnIn,
        int thin)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(logTarget);
        ArgumentNullException.ThrowIfNull(propose);
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A chain cannot keep a negative number of draws.");
        }

        if (burnIn < 0 || thin < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(thin), "The burn-in cannot be negative and the thinning must be at least one.");
        }

        var kept = new List<double[]>(count);
        double[] current = (double[])start.Clone();
        double currentLog = logTarget(current);
        int evaluations = 1;
        int proposed = 0;
        int accepted = 0;

        int total = burnIn + (count * thin);
        for (int step = 0; step < total; step++)
        {
            double[] candidate = propose(current);
            double candidateLog = logTarget(candidate);
            evaluations++;
            proposed++;

            // The proposal terms cancel when the proposal is symmetric, which is why a caller who says
            // so is not asked for a proposal density at all.
            double ratio = candidateLog - currentLog;
            if (logProposal is not null)
            {
                ratio += logProposal(current, candidate) - logProposal(candidate, current);
            }

            if (double.IsNaN(ratio))
            {
                ratio = double.NegativeInfinity;
            }

            if (ratio >= 0 || Math.Log(NextUniform()) < ratio)
            {
                current = candidate;
                currentLog = candidateLog;
                accepted++;
            }

            if (step >= burnIn && (step - burnIn) % thin == 0 && kept.Count < count)
            {
                kept.Add((double[])current.Clone());
            }
        }

        return new Chain([.. kept], proposed == 0 ? 0 : (double)accepted / proposed, evaluations);

        double NextUniform() => Uniform();
    }

    /// <summary>The uniform draw a chain uses; set once per run by <see cref="Using"/>.</summary>
    [ThreadStatic]
    private static Random? _random;

    private static double Uniform() => (_random ??= new Random(0)).NextDouble();

    /// <summary>
    /// Runs <paramref name="body"/> with <paramref name="random"/> as the source of every uniform the
    /// chains draw, so that a seeded run is reproducible without the generator being threaded through
    /// every callback the caller supplies.
    /// </summary>
    public static T Using<T>(Random random, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(body);
        Random? previous = _random;
        _random = random;
        try
        {
            return body();
        }
        finally
        {
            _random = previous;
        }
    }

    /// <summary>
    /// Slice sampling: draw a height under the density, then a point uniformly from the slice of the
    /// density above that height.
    /// </summary>
    /// <remarks>
    /// The slice is not known in closed form, so it is stepped out from the current point in the given
    /// width until both ends are below the height, and then shrunk toward the current point on every
    /// rejection. That shrinking is what makes the method correct without a proposal to tune: a bad
    /// width costs evaluations rather than correctness.
    /// </remarks>
    /// <param name="start">Where the chain starts.</param>
    /// <param name="count">How many draws to keep.</param>
    /// <param name="logTarget">The logarithm of the target density, up to a constant.</param>
    /// <param name="width">The first guess at how wide the slice is, per dimension.</param>
    /// <param name="burnIn">How many draws to discard before keeping any.</param>
    /// <param name="thin">Keep one draw in this many.</param>
    public static Chain Slice(
        double[] start,
        int count,
        Func<double[], double> logTarget,
        double[] width,
        int burnIn,
        int thin)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(logTarget);
        ArgumentNullException.ThrowIfNull(width);
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A chain cannot keep a negative number of draws.");
        }

        if (burnIn < 0 || thin < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(thin), "The burn-in cannot be negative and the thinning must be at least one.");
        }

        const int MaximumSteps = 200;
        int dimensions = start.Length;
        var kept = new List<double[]>(count);
        double[] current = (double[])start.Clone();
        double currentLog = logTarget(current);
        int evaluations = 1;

        int total = burnIn + (count * thin);
        for (int step = 0; step < total; step++)
        {
            for (int d = 0; d < dimensions; d++)
            {
                // The height is drawn below the density in the logarithm, which is the same thing as
                // multiplying the density by a uniform and avoids the underflow that would be.
                double height = currentLog + Math.Log(Uniform());
                double size = width[d % width.Length];
                double left = current[d] - (size * Uniform());
                double right = left + size;

                var probe = (double[])current.Clone();
                for (int i = 0; i < MaximumSteps; i++)
                {
                    probe[d] = left;
                    evaluations++;
                    if (logTarget(probe) <= height)
                    {
                        break;
                    }

                    left -= size;
                }

                for (int i = 0; i < MaximumSteps; i++)
                {
                    probe[d] = right;
                    evaluations++;
                    if (logTarget(probe) <= height)
                    {
                        break;
                    }

                    right += size;
                }

                for (int i = 0; i < MaximumSteps; i++)
                {
                    double candidate = left + ((right - left) * Uniform());
                    probe[d] = candidate;
                    double candidateLog = logTarget(probe);
                    evaluations++;
                    if (candidateLog > height)
                    {
                        current[d] = candidate;
                        currentLog = candidateLog;
                        break;
                    }

                    if (candidate < current[d])
                    {
                        left = candidate;
                    }
                    else
                    {
                        right = candidate;
                    }
                }

                probe[d] = current[d];
            }

            if (step >= burnIn && (step - burnIn) % thin == 0 && kept.Count < count)
            {
                kept.Add((double[])current.Clone());
            }
        }

        return new Chain([.. kept], 1, evaluations);
    }
}
