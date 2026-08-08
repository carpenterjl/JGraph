namespace JGraph.Statistics.Multivariate;

/// <summary>
/// A sequence whose states are not observed: generating one, reading the state probabilities off an
/// observed sequence, finding the single most likely path through it, and estimating the two matrices
/// that describe it — from known states, or from the observations alone.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is written on the scaled forward-backward recursion rather than in logarithms. The
/// unscaled recursion underflows to zero after a few hundred observations, which is the length at
/// which these questions start being asked; scaling each step to sum to one keeps every quantity in
/// range, and the log-likelihood falls out as the sum of the logarithms of the scale factors, exactly.
/// </para>
/// <para>
/// States and symbols are numbered from zero throughout; the script layer adds the one. A model is
/// two matrices — a row per state of where it goes next, and a row per state of what it emits — and
/// each row is required to sum to one, because a row that does not is a modelling error rather than a
/// value to be renormalized silently.
/// </para>
/// </remarks>
public static class HiddenMarkov
{
    /// <summary>Draws a sequence from a model.</summary>
    /// <param name="length">How many observations to draw.</param>
    /// <param name="transition">Where each state goes next, a row per state summing to one.</param>
    /// <param name="emission">What each state emits, a row per state summing to one.</param>
    /// <param name="random">The stream to draw from.</param>
    /// <param name="start">Which state to begin in, or −1 to begin in the first as MathWorks does.</param>
    /// <returns>The symbols drawn, and the states they were drawn from.</returns>
    public static (int[] Sequence, int[] States) Generate(
        int length, double[,] transition, double[,] emission, Random random, int start = -1)
    {
        ArgumentNullException.ThrowIfNull(random);
        (int states, int symbols) = Check(transition, emission);
        if (length < 0)
        {
            throw new ArgumentException("A sequence cannot have a negative length.", nameof(length));
        }

        var sequence = new int[length];
        var path = new int[length];
        int state = start < 0 ? 0 : start;
        if (state >= states)
        {
            throw new ArgumentException("The starting state is not one the model has.", nameof(start));
        }

        for (int t = 0; t < length; t++)
        {
            // MathWorks moves first and then emits, so the state the model was given is where the walk
            // begins and never where an observation comes from. Emitting first would shift the whole
            // sequence by one step and quietly change every estimate downstream.
            state = Draw(transition, state, states, random);
            path[t] = state;
            sequence[t] = Draw(emission, state, symbols, random);
        }

        return (sequence, path);
    }

    /// <summary>What the forward-backward recursion found.</summary>
    /// <param name="Probabilities">The probability of each state at each step, a row per state.</param>
    /// <param name="LogLikelihood">The log-probability of the whole sequence under the model.</param>
    /// <param name="Forward">The scaled forward variables.</param>
    /// <param name="Backward">The scaled backward variables.</param>
    /// <param name="Scale">The factor each step was scaled by.</param>
    public readonly record struct Decoding(
        double[,] Probabilities, double LogLikelihood, double[,] Forward, double[,] Backward, double[] Scale);

    /// <summary>The probability of being in each state at each step of an observed sequence.</summary>
    public static Decoding Decode(int[] sequence, double[,] transition, double[,] emission)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        (int states, int symbols) = Check(transition, emission);
        int n = sequence.Length;

        // The recursion is written over n + 1 steps with a phantom step zero in the starting state,
        // which is what makes the answer agree with a generator that moves before it emits.
        var forward = new double[states, n + 1];
        var backward = new double[states, n + 1];
        var scale = new double[n + 1];
        forward[0, 0] = 1;
        scale[0] = 1;

        for (int t = 1; t <= n; t++)
        {
            int symbol = Symbol(sequence[t - 1], symbols);
            for (int j = 0; j < states; j++)
            {
                double total = 0;
                for (int i = 0; i < states; i++)
                {
                    total += forward[i, t - 1] * transition[i, j];
                }

                forward[j, t] = total * emission[j, symbol];
            }

            double sum = 0;
            for (int j = 0; j < states; j++)
            {
                sum += forward[j, t];
            }

            if (!(sum > 0))
            {
                throw new ArgumentException(
                    "The model gives that sequence no probability at all, so nothing can be inferred from it.",
                    nameof(sequence));
            }

            scale[t] = sum;
            for (int j = 0; j < states; j++)
            {
                forward[j, t] /= sum;
            }
        }

        for (int j = 0; j < states; j++)
        {
            backward[j, n] = 1;
        }

        for (int t = n; t >= 1; t--)
        {
            int symbol = Symbol(sequence[t - 1], symbols);
            for (int i = 0; i < states; i++)
            {
                double total = 0;
                for (int j = 0; j < states; j++)
                {
                    total += transition[i, j] * emission[j, symbol] * backward[j, t];
                }

                backward[i, t - 1] = total / scale[t];
            }
        }

        var posterior = new double[states, n + 1];
        for (int t = 0; t <= n; t++)
        {
            for (int j = 0; j < states; j++)
            {
                posterior[j, t] = forward[j, t] * backward[j, t];
            }
        }

        double logLikelihood = 0;
        for (int t = 1; t <= n; t++)
        {
            logLikelihood += Math.Log(scale[t]);
        }

        return new Decoding(posterior, logLikelihood, forward, backward, scale);
    }

    /// <summary>
    /// The single most likely sequence of states — which is not the same as the most likely state at
    /// each step taken separately, and can even be a path the posterior gives no weight to.
    /// </summary>
    public static (int[] States, double LogProbability) Viterbi(
        int[] sequence, double[,] transition, double[,] emission)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        (int states, int symbols) = Check(transition, emission);
        int n = sequence.Length;
        if (n == 0)
        {
            return ([], 0);
        }

        var best = new double[states, n];
        var came = new int[states, n];
        double negativeInfinity = double.NegativeInfinity;
        for (int j = 0; j < states; j++)
        {
            best[j, 0] = negativeInfinity;
        }

        int first = Symbol(sequence[0], symbols);
        for (int j = 0; j < states; j++)
        {
            best[j, 0] = Log(transition[0, j]) + Log(emission[j, first]);
            came[j, 0] = 0;
        }

        for (int t = 1; t < n; t++)
        {
            int symbol = Symbol(sequence[t], symbols);
            for (int j = 0; j < states; j++)
            {
                double top = negativeInfinity;
                int from = 0;
                for (int i = 0; i < states; i++)
                {
                    double candidate = best[i, t - 1] + Log(transition[i, j]);
                    if (candidate > top)
                    {
                        top = candidate;
                        from = i;
                    }
                }

                best[j, t] = top + Log(emission[j, symbol]);
                came[j, t] = from;
            }
        }

        int last = 0;
        double highest = negativeInfinity;
        for (int j = 0; j < states; j++)
        {
            if (best[j, n - 1] > highest)
            {
                highest = best[j, n - 1];
                last = j;
            }
        }

        var path = new int[n];
        path[n - 1] = last;
        for (int t = n - 1; t > 0; t--)
        {
            path[t - 1] = came[path[t], t];
        }

        return (path, highest);
    }

    /// <summary>
    /// The two matrices estimated by counting, when the states that produced the sequence are known.
    /// </summary>
    /// <param name="sequence">The symbols observed.</param>
    /// <param name="states">The state each symbol was emitted from.</param>
    /// <param name="stateCount">How many states the model has.</param>
    /// <param name="symbolCount">How many symbols it can emit.</param>
    /// <param name="pseudoTransitions">Counts to add to each transition before normalizing, or null.</param>
    /// <param name="pseudoEmissions">Counts to add to each emission before normalizing, or null.</param>
    public static (double[,] Transition, double[,] Emission) EstimateFromStates(
        int[] sequence,
        int[] states,
        int stateCount,
        int symbolCount,
        double[,]? pseudoTransitions = null,
        double[,]? pseudoEmissions = null)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(states);
        if (sequence.Length != states.Length)
        {
            throw new ArgumentException(
                "There must be one state for each observation.", nameof(states));
        }

        var transitionCounts = new double[stateCount, stateCount];
        var emissionCounts = new double[stateCount, symbolCount];
        if (pseudoTransitions is not null)
        {
            Add(transitionCounts, pseudoTransitions, nameof(pseudoTransitions));
        }

        if (pseudoEmissions is not null)
        {
            Add(emissionCounts, pseudoEmissions, nameof(pseudoEmissions));
        }

        for (int t = 0; t < sequence.Length; t++)
        {
            int state = states[t];
            if (state < 0 || state >= stateCount)
            {
                throw new ArgumentException("A state outside the model appears in the path.", nameof(states));
            }

            emissionCounts[state, Symbol(sequence[t], symbolCount)]++;
            if (t > 0)
            {
                transitionCounts[states[t - 1], state]++;
            }
        }

        return (Normalize(transitionCounts), Normalize(emissionCounts));
    }

    /// <summary>
    /// The two matrices estimated from the observations alone, by the Baum-Welch algorithm.
    /// </summary>
    /// <remarks>
    /// Each pass replaces the counts a known path would have given with the counts the current model
    /// expects, which is guaranteed never to lower the likelihood — so the search converges, to a local
    /// maximum that depends on where it started. That dependence is the reason the starting guess is
    /// the caller's rather than something chosen here.
    /// </remarks>
    /// <param name="sequences">One or more observed sequences.</param>
    /// <param name="guessTransition">Where the search starts for the transition matrix.</param>
    /// <param name="guessEmission">Where it starts for the emission matrix.</param>
    /// <param name="maxIterations">The most passes the search may take.</param>
    /// <param name="tolerance">How little the log-likelihood must move for it to stop.</param>
    /// <param name="pseudoTransitions">Counts to add to each transition before normalizing, or null.</param>
    /// <param name="pseudoEmissions">Counts to add to each emission before normalizing, or null.</param>
    /// <returns>The estimated matrices, the log-likelihood, and how the search ended.</returns>
    public static (double[,] Transition, double[,] Emission, double LogLikelihood, int Iterations, bool Converged)
        Train(
            IReadOnlyList<int[]> sequences,
            double[,] guessTransition,
            double[,] guessEmission,
            int maxIterations = 500,
            double tolerance = 1e-6,
            double[,]? pseudoTransitions = null,
            double[,]? pseudoEmissions = null)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        (int states, int symbols) = Check(guessTransition, guessEmission);

        double[,] transition = (double[,])guessTransition.Clone();
        double[,] emission = (double[,])guessEmission.Clone();
        double previous = double.NegativeInfinity;
        bool converged = false;
        int iteration = 0;

        for (; iteration < maxIterations; iteration++)
        {
            var transitionCounts = new double[states, states];
            var emissionCounts = new double[states, symbols];
            if (pseudoTransitions is not null)
            {
                Add(transitionCounts, pseudoTransitions, nameof(pseudoTransitions));
            }

            if (pseudoEmissions is not null)
            {
                Add(emissionCounts, pseudoEmissions, nameof(pseudoEmissions));
            }

            double likelihood = 0;
            foreach (int[] sequence in sequences)
            {
                if (sequence.Length == 0)
                {
                    continue;
                }

                Decoding decoded = Decode(sequence, transition, emission);
                likelihood += decoded.LogLikelihood;
                int n = sequence.Length;

                for (int t = 1; t <= n; t++)
                {
                    int symbol = Symbol(sequence[t - 1], symbols);
                    for (int i = 0; i < states; i++)
                    {
                        for (int j = 0; j < states; j++)
                        {
                            transitionCounts[i, j] +=
                                decoded.Forward[i, t - 1] * transition[i, j] * emission[j, symbol]
                                * decoded.Backward[j, t] / decoded.Scale[t];
                        }

                        emissionCounts[i, symbol] += decoded.Probabilities[i, t];
                    }
                }
            }

            transition = Normalize(transitionCounts);
            emission = Normalize(emissionCounts);

            if (Math.Abs(likelihood - previous) < tolerance * (1 + Math.Abs(previous)))
            {
                previous = likelihood;
                iteration++;
                converged = true;
                break;
            }

            previous = likelihood;
        }

        return (transition, emission, previous, iteration, converged);
    }

    private static (int States, int Symbols) Check(double[,] transition, double[,] emission)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(emission);
        int states = transition.GetLength(0);
        if (transition.GetLength(1) != states)
        {
            throw new ArgumentException(
                "The transition matrix must be square, one row and one column per state.", nameof(transition));
        }

        if (emission.GetLength(0) != states)
        {
            throw new ArgumentException(
                "The emission matrix must have one row for each state.", nameof(emission));
        }

        CheckRows(transition, nameof(transition));
        CheckRows(emission, nameof(emission));
        return (states, emission.GetLength(1));
    }

    private static void CheckRows(double[,] matrix, string name)
    {
        for (int r = 0; r < matrix.GetLength(0); r++)
        {
            double total = 0;
            for (int c = 0; c < matrix.GetLength(1); c++)
            {
                double value = matrix[r, c];
                if (!(value >= 0))
                {
                    throw new ArgumentException("A probability cannot be negative or missing.", name);
                }

                total += value;
            }

            if (Math.Abs(total - 1) > 1e-8)
            {
                throw new ArgumentException("Every row must sum to one.", name);
            }
        }
    }

    private static int Symbol(int value, int symbols)
    {
        if (value < 0 || value >= symbols)
        {
            throw new ArgumentException("The sequence holds a symbol the model cannot emit.", nameof(value));
        }

        return value;
    }

    private static int Draw(double[,] matrix, int row, int width, Random random)
    {
        double target = random.NextDouble();
        double running = 0;
        for (int c = 0; c < width; c++)
        {
            running += matrix[row, c];
            if (target < running)
            {
                return c;
            }
        }

        return width - 1;
    }

    private static double Log(double value) => value > 0 ? Math.Log(value) : double.NegativeInfinity;

    private static void Add(double[,] into, double[,] counts, string name)
    {
        if (counts.GetLength(0) != into.GetLength(0) || counts.GetLength(1) != into.GetLength(1))
        {
            throw new ArgumentException("The pseudocounts are not the shape of the matrix they add to.", name);
        }

        for (int r = 0; r < into.GetLength(0); r++)
        {
            for (int c = 0; c < into.GetLength(1); c++)
            {
                into[r, c] += counts[r, c];
            }
        }
    }

    private static double[,] Normalize(double[,] counts)
    {
        int rows = counts.GetLength(0);
        int columns = counts.GetLength(1);
        var probabilities = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            double total = 0;
            for (int c = 0; c < columns; c++)
            {
                total += counts[r, c];
            }

            for (int c = 0; c < columns; c++)
            {
                // A state that was never visited has no evidence about where it goes; leaving its row
                // at zero says so, rather than inventing a uniform distribution the data never showed.
                probabilities[r, c] = total > 0 ? counts[r, c] / total : 0;
            }
        }

        return probabilities;
    }
}
