namespace JGraph.Statistics;

/// <summary>
/// The enumerable designs of experiments: the ones whose run list follows from the number of factors
/// rather than from a search.
/// </summary>
/// <remarks>
/// <para>
/// Every design here is written down rather than optimized. A full factorial is a counter in mixed
/// radix; a two-level fraction is a set of columns each of which is a product of the basic ones; a
/// Box-Behnken design is a published list of factor blocks; a central composite design is a cube, a
/// star and a centre. That is what separates these from the exchange algorithms — <c>rowexch</c>,
/// <c>cordexch</c> — whose answer depends on where the search started, and which are excluded for
/// exactly that reason.
/// </para>
/// <para>
/// Two-level designs are in the -1/+1 coding throughout, which is the coding the confounding
/// arithmetic needs: a column is confounded with a product of columns precisely when multiplying them
/// elementwise gives a column of ones.
/// </para>
/// </remarks>
public static class DesignOfExperiments
{
    /// <summary>
    /// The full factorial over factors with the given numbers of levels, one run per row, levels
    /// numbered from one, and the first factor varying fastest.
    /// </summary>
    public static double[,] FullFactorial(IReadOnlyList<int> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0)
        {
            throw new ArgumentException("A full factorial needs at least one factor.", nameof(levels));
        }

        int runs = 1;
        foreach (int count in levels)
        {
            if (count < 1)
            {
                throw new ArgumentException("Every factor needs at least one level.", nameof(levels));
            }

            runs = checked(runs * count);
        }

        var design = new double[runs, levels.Count];
        int repeat = 1;
        for (int factor = 0; factor < levels.Count; factor++)
        {
            int count = levels[factor];
            for (int run = 0; run < runs; run++)
            {
                design[run, factor] = ((run / repeat) % count) + 1;
            }

            repeat *= count;
        }

        return design;
    }

    /// <summary>
    /// The two-level full factorial over <paramref name="factors"/> factors, coded 0 and 1, with the
    /// <em>last</em> factor varying fastest — the opposite of the full factorial above, and the
    /// difference MathWorks documents between the two names.
    /// </summary>
    public static double[,] TwoLevelFullFactorial(int factors)
    {
        if (factors is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(factors), "Between 1 and 30 factors.");
        }

        int runs = 1 << factors;
        var design = new double[runs, factors];
        for (int run = 0; run < runs; run++)
        {
            for (int factor = 0; factor < factors; factor++)
            {
                design[run, factor] = (run >> (factors - 1 - factor)) & 1;
            }
        }

        return design;
    }

    /// <summary>One generator of a two-level fraction: the factors whose product makes a column.</summary>
    /// <param name="Word">The generator as it was written, for reporting.</param>
    /// <param name="Basic">The zero-based basic factors multiplied together.</param>
    public readonly record struct Generator(string Word, int[] Basic);

    /// <summary>
    /// A two-level fractional factorial from a list of generators. Each generator names the basic
    /// factors whose product forms its column, so a single letter is a basic factor itself and a word
    /// like <c>abc</c> is the three-way interaction of the first three.
    /// </summary>
    public static double[,] Fraction(IReadOnlyList<Generator> generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        if (generators.Count == 0)
        {
            throw new ArgumentException("A fraction needs at least one generator.", nameof(generators));
        }

        int basics = 0;
        foreach (Generator generator in generators)
        {
            foreach (int factor in generator.Basic)
            {
                basics = Math.Max(basics, factor + 1);
            }
        }

        double[,] cube = TwoLevelFullFactorial(basics);
        int runs = cube.GetLength(0);
        var design = new double[runs, generators.Count];
        for (int run = 0; run < runs; run++)
        {
            for (int column = 0; column < generators.Count; column++)
            {
                double product = 1;
                foreach (int factor in generators[column].Basic)
                {
                    // The cube arrives coded 0/1 and the fraction is written in -1/+1, because the
                    // product of two columns is only a column again in the symmetric coding.
                    product *= (2 * cube[run, factor]) - 1;
                }

                design[run, column] = product;
            }
        }

        return design;
    }

    /// <summary>
    /// The confounding pattern of a design: for every effect up to order <paramref name="order"/>, the
    /// group of effects that share its column and therefore cannot be told apart.
    /// </summary>
    /// <returns>
    /// One entry per group, each holding the effects in it as lists of zero-based column numbers. The
    /// first group is the one the constant sits in, and it holds an empty term for the constant itself.
    /// </returns>
    public static IReadOnlyList<int[][]> Confounding(double[,] design, int order)
    {
        ArgumentNullException.ThrowIfNull(design);
        int runs = design.GetLength(0);
        int factors = design.GetLength(1);

        var groups = new List<(double[] Column, List<int[]> Effects)>
        {
            (Ones(runs), [[]]),
        };

        foreach (int[] term in Terms(factors, Math.Min(order, factors)))
        {
            var column = new double[runs];
            for (int run = 0; run < runs; run++)
            {
                double product = 1;
                foreach (int factor in term)
                {
                    product *= design[run, factor];
                }

                column[run] = product;
            }

            bool placed = false;
            foreach ((double[] existing, List<int[]> effects) in groups)
            {
                if (SameOrOpposite(existing, column))
                {
                    effects.Add(term);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                groups.Add((column, [term]));
            }
        }

        return [.. groups.ConvertAll(static group => group.Effects.ToArray())];
    }

    /// <summary>
    /// Generators for a two-level fraction of <paramref name="factors"/> factors in
    /// <c>2^<paramref name="basics"/></c> runs, of at least the requested resolution.
    /// </summary>
    /// <remarks>
    /// The search is over which interaction of the basic factors each added factor is assigned to, and
    /// it is exhaustive rather than heuristic: the candidate set is every interaction of two or more
    /// basic factors, which for the sizes a two-level screening design is worth running at all is a few
    /// thousand combinations. Ties are broken toward the highest resolution, then toward the fewest
    /// words at that resolution, which is the same order of preference the minimum-aberration
    /// criterion states.
    /// </remarks>
    /// <returns>The generator words, or an empty list when nothing reaches the requested resolution.</returns>
    public static IReadOnlyList<string> FractionGenerators(int factors, int basics, int resolution)
    {
        if (factors < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(factors), "At least one factor.");
        }

        if (basics < 1 || basics > factors)
        {
            throw new ArgumentOutOfRangeException(nameof(basics), "Between one factor and all of them.");
        }

        var words = new List<string>();
        for (int i = 0; i < basics; i++)
        {
            words.Add(Letter(i).ToString());
        }

        int added = factors - basics;
        if (added == 0)
        {
            return words;
        }

        // Every interaction of two or more basic factors is a candidate column for an added factor.
        var candidates = new List<int[]>();
        foreach (int[] term in Terms(basics, basics))
        {
            if (term.Length >= 2)
            {
                candidates.Add(term);
            }
        }

        int[]? bestChoice = null;
        int bestResolution = 0;
        int bestCount = int.MaxValue;
        var choice = new int[added];

        void Search(int depth, int from)
        {
            if (depth == added)
            {
                (int found, int count) = Aberration(basics, choice, candidates);
                if (found > bestResolution || (found == bestResolution && count < bestCount))
                {
                    bestResolution = found;
                    bestCount = count;
                    bestChoice = (int[])choice.Clone();
                }

                return;
            }

            for (int i = from; i < candidates.Count; i++)
            {
                choice[depth] = i;
                Search(depth + 1, i + 1);
            }
        }

        Search(0, 0);

        if (bestChoice is null || bestResolution < resolution)
        {
            return [];
        }

        foreach (int index in bestChoice)
        {
            words.Add(string.Concat(Array.ConvertAll(candidates[index], static f => Letter(f).ToString())));
        }

        return words;
    }

    /// <summary>
    /// The resolution of a candidate assignment and how many defining words sit at it. The resolution
    /// is the length of the shortest word in the defining relation, and the defining relation is
    /// generated by the assignments themselves and every product of them.
    /// </summary>
    private static (int Resolution, int Count) Aberration(
        int basics, int[] choice, List<int[]> candidates)
    {
        int shortest = int.MaxValue;
        int count = 0;
        int subsets = 1 << choice.Length;
        for (int mask = 1; mask < subsets; mask++)
        {
            // A word is the symmetric difference of the chosen interactions, plus one letter for each
            // added factor in the subset, because a generator says "this factor equals that product".
            int letters = 0;
            int extra = 0;
            for (int i = 0; i < choice.Length; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                extra++;
                foreach (int factor in candidates[choice[i]])
                {
                    letters ^= 1 << factor;
                }
            }

            int length = System.Numerics.BitOperations.PopCount((uint)letters) + extra;
            if (length < shortest)
            {
                shortest = length;
                count = 1;
            }
            else if (length == shortest)
            {
                count++;
            }
        }

        _ = basics;
        return (shortest, count);
    }

    /// <summary>The two-level Box-Behnken blocks for three to seven factors, and the fallback above.</summary>
    private static IReadOnlyList<int[]> BehnkenBlocks(int factors) => factors switch
    {
        3 => [[0, 1], [0, 2], [1, 2]],
        4 => [[0, 1], [2, 3], [0, 2], [1, 3], [0, 3], [1, 2]],
        5 => [[0, 1], [2, 3], [0, 4], [1, 2], [3, 4], [0, 2], [1, 3], [2, 4], [0, 3], [1, 4]],
        6 => [[0, 1, 3], [1, 2, 4], [2, 3, 5], [3, 4, 0], [4, 5, 1], [5, 0, 2]],
        7 => [[3, 4, 5], [0, 5, 6], [1, 4, 6], [0, 1, 3], [2, 3, 6], [0, 2, 4], [1, 2, 5]],
        _ => AllPairs(factors),
    };

    private static IReadOnlyList<int[]> AllPairs(int factors)
    {
        var pairs = new List<int[]>();
        for (int i = 0; i < factors; i++)
        {
            for (int j = i + 1; j < factors; j++)
            {
                pairs.Add([i, j]);
            }
        }

        return pairs;
    }

    /// <summary>How many centre points a Box-Behnken design carries when the caller names none.</summary>
    public static int BehnkenCentrePoints(int factors) => factors <= 4 ? 3 : 6;

    /// <summary>
    /// A Box-Behnken design: for each published block of factors, the two-level factorial in those
    /// factors with every other factor held at its centre, followed by the centre runs.
    /// </summary>
    public static double[,] BoxBehnken(int factors, int centrePoints)
    {
        if (factors < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(factors), "A Box-Behnken design needs at least three factors.");
        }

        if (centrePoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(centrePoints), "The number of centre points cannot be negative.");
        }

        IReadOnlyList<int[]> blocks = BehnkenBlocks(factors);
        var rows = new List<double[]>();
        foreach (int[] block in blocks)
        {
            int corners = 1 << block.Length;
            for (int corner = 0; corner < corners; corner++)
            {
                var row = new double[factors];
                for (int i = 0; i < block.Length; i++)
                {
                    row[block[i]] = ((corner >> i) & 1) == 0 ? -1 : 1;
                }

                rows.Add(row);
            }
        }

        for (int i = 0; i < centrePoints; i++)
        {
            rows.Add(new double[factors]);
        }

        return Stack(rows, factors);
    }

    /// <summary>Where the star points of a central composite design sit relative to the cube.</summary>
    public enum CompositeKind
    {
        /// <summary>Star points outside the cube, at the rotatable distance.</summary>
        Circumscribed,

        /// <summary>The circumscribed design scaled so that nothing leaves the unit cube.</summary>
        Inscribed,

        /// <summary>Star points on the faces of the cube.</summary>
        Faced,
    }

    /// <summary>How many centre points a central composite design carries when the caller names none.</summary>
    /// <remarks>
    /// The published uniform-precision counts, which is what makes the variance of a prediction the same
    /// at the centre as one radius out. They are a table rather than a formula in every source that
    /// gives them.
    /// </remarks>
    public static int CompositeCentrePoints(int factors, int fraction) => (factors, fraction) switch
    {
        (2, _) => 5,
        (3, _) => 6,
        (4, _) => 7,
        (5, 0) => 10,
        (5, _) => 6,
        (6, 0) => 15,
        (6, _) => 9,
        (7, 0) => 21,
        (7, _) => 14,
        _ => 10,
    };

    /// <summary>
    /// A central composite design: a two-level cube (whole or fractioned), a star of two points per
    /// factor along each axis, and a run at the centre repeated.
    /// </summary>
    /// <param name="factors">How many factors.</param>
    /// <param name="fraction">How many times the cube is halved; zero is the full factorial.</param>
    /// <param name="kind">Where the star points sit.</param>
    /// <param name="centrePoints">How many runs at the centre.</param>
    public static double[,] CentralComposite(
        int factors, int fraction, CompositeKind kind, int centrePoints)
    {
        if (factors < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(factors), "A central composite design needs at least two factors.");
        }

        if (fraction < 0 || fraction >= factors)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), "The cube cannot be halved more times than there are factors.");
        }

        if (centrePoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(centrePoints), "The number of centre points cannot be negative.");
        }

        double[,] cube = CubePortion(factors, fraction);
        int cubeRuns = cube.GetLength(0);

        // The rotatable distance: the star points sit where the cube corners are, measured from the
        // centre, which is what makes the design's precision depend on distance alone.
        double alpha = kind == CompositeKind.Faced ? 1 : Math.Pow(cubeRuns, 0.25);
        double scale = kind == CompositeKind.Inscribed ? 1 / alpha : 1;

        var rows = new List<double[]>();
        for (int run = 0; run < cubeRuns; run++)
        {
            var row = new double[factors];
            for (int factor = 0; factor < factors; factor++)
            {
                row[factor] = cube[run, factor] * scale;
            }

            rows.Add(row);
        }

        for (int factor = 0; factor < factors; factor++)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                var row = new double[factors];
                row[factor] = sign * alpha * scale;
                rows.Add(row);
            }
        }

        for (int i = 0; i < centrePoints; i++)
        {
            rows.Add(new double[factors]);
        }

        return Stack(rows, factors);
    }

    /// <summary>
    /// The cube of a central composite design: the whole two-level factorial, or the fraction of it
    /// generated by assigning the last factors to the highest interactions of the first ones.
    /// </summary>
    private static double[,] CubePortion(int factors, int fraction)
    {
        int basics = factors - fraction;
        var generators = new List<Generator>();
        for (int i = 0; i < basics; i++)
        {
            generators.Add(new Generator(Letter(i).ToString(), [i]));
        }

        for (int i = 0; i < fraction; i++)
        {
            // Each added factor takes the longest interaction available, which is the assignment that
            // keeps the shortest defining word as long as it can be.
            var term = new int[Math.Min(basics, basics - i)];
            for (int j = 0; j < term.Length; j++)
            {
                term[j] = j;
            }

            if (i > 0 && term.Length > 1)
            {
                term = term[..^1];
                term[^1] = basics - 1;
            }

            generators.Add(new Generator(
                string.Concat(Array.ConvertAll(term, static f => Letter(f).ToString())), term));
        }

        return Fraction(generators);
    }

    // --- Shared small pieces ----------------------------------------------------------------------

    /// <summary>The letter a factor is written with: a, b, c… and A, B, C… past the twenty-sixth.</summary>
    public static char Letter(int factor) =>
        factor < 26 ? (char)('a' + factor) : (char)('A' + factor - 26);

    /// <summary>Every combination of factors up to the given order, shortest first.</summary>
    private static IEnumerable<int[]> Terms(int factors, int order)
    {
        for (int length = 1; length <= order; length++)
        {
            var term = new int[length];
            foreach (int[] combination in Combinations(term, 0, 0, factors))
            {
                yield return combination;
            }
        }
    }

    private static IEnumerable<int[]> Combinations(int[] term, int depth, int from, int factors)
    {
        if (depth == term.Length)
        {
            yield return (int[])term.Clone();
            yield break;
        }

        for (int i = from; i <= factors - (term.Length - depth); i++)
        {
            term[depth] = i;
            foreach (int[] combination in Combinations(term, depth + 1, i + 1, factors))
            {
                yield return combination;
            }
        }
    }

    private static double[] Ones(int length)
    {
        var column = new double[length];
        Array.Fill(column, 1);
        return column;
    }

    /// <summary>Whether two columns carry the same information, which includes carrying the negative of it.</summary>
    private static bool SameOrOpposite(double[] left, double[] right)
    {
        bool same = true;
        bool opposite = true;
        for (int i = 0; i < left.Length; i++)
        {
            same &= Math.Abs(left[i] - right[i]) < 1e-9;
            opposite &= Math.Abs(left[i] + right[i]) < 1e-9;
        }

        return same || opposite;
    }

    private static double[,] Stack(List<double[]> rows, int columns)
    {
        var design = new double[rows.Count, columns];
        for (int run = 0; run < rows.Count; run++)
        {
            for (int factor = 0; factor < columns; factor++)
            {
                design[run, factor] = rows[run][factor];
            }
        }

        return design;
    }
}
