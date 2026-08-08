namespace JGraph.Statistics.Regression;

/// <summary>The four model shapes <c>x2fx</c>, <c>regstats</c> and <c>leverage</c> all name by word.</summary>
public enum ModelShape
{
    /// <summary>An intercept and each predictor once.</summary>
    Linear,

    /// <summary>The linear terms and every product of two different predictors.</summary>
    Interaction,

    /// <summary>The interaction terms and each predictor squared.</summary>
    Quadratic,

    /// <summary>The linear terms and each predictor squared, with no products.</summary>
    PureQuadratic,
}

/// <summary>
/// Turning predictors into the columns a linear model is actually fitted against: <c>x2fx</c> and
/// <c>dummyvar</c>.
/// </summary>
/// <remarks>
/// A model term is a list of exponents, one per predictor, so the whole surface is one loop over a
/// list of those: a word like <c>'quadratic'</c> only chooses which list, and a caller who writes the
/// exponents out by hand gets exactly the same treatment. That is also what makes a categorical
/// predictor expressible — its level indicators are ordinary columns once <c>dummyvar</c> has made
/// them, and an interaction with a categorical predictor is the product of a term with each indicator.
/// </remarks>
public static class DesignMatrix
{
    /// <summary>The exponent rows a named model shape stands for, over <paramref name="predictors"/> predictors.</summary>
    public static List<int[]> Terms(ModelShape shape, int predictors)
    {
        if (predictors < 1)
        {
            throw new ArgumentException("a model needs at least one predictor.", nameof(predictors));
        }

        var terms = new List<int[]> { new int[predictors] };
        for (int j = 0; j < predictors; j++)
        {
            var linear = new int[predictors];
            linear[j] = 1;
            terms.Add(linear);
        }

        if (shape is ModelShape.Interaction or ModelShape.Quadratic)
        {
            for (int a = 0; a < predictors - 1; a++)
            {
                for (int b = a + 1; b < predictors; b++)
                {
                    var product = new int[predictors];
                    product[a] = 1;
                    product[b] = 1;
                    terms.Add(product);
                }
            }
        }

        if (shape is ModelShape.Quadratic or ModelShape.PureQuadratic)
        {
            for (int j = 0; j < predictors; j++)
            {
                var square = new int[predictors];
                square[j] = 2;
                terms.Add(square);
            }
        }

        return terms;
    }

    /// <summary><c>x2fx</c>: the design matrix a list of exponent rows describes.</summary>
    /// <param name="predictors">One row per observation, one column per predictor.</param>
    /// <param name="terms">One row per model term, holding the power each predictor is raised to.</param>
    public static double[,] Expand(double[,] predictors, IReadOnlyList<int[]> terms)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(terms);

        int n = predictors.GetLength(0);
        int k = predictors.GetLength(1);
        var design = new double[n, terms.Count];
        for (int t = 0; t < terms.Count; t++)
        {
            int[] powers = terms[t];
            if (powers.Length != k)
            {
                throw new ArgumentException(
                    $"term {t + 1} names {powers.Length} predictors but there are {k}.", nameof(terms));
            }

            for (int r = 0; r < n; r++)
            {
                double value = 1;
                for (int c = 0; c < k; c++)
                {
                    for (int power = 0; power < powers[c]; power++)
                    {
                        value *= predictors[r, c];
                    }
                }

                design[r, t] = value;
            }
        }

        return design;
    }

    /// <summary><c>x2fx</c> with the model named by word.</summary>
    public static double[,] Expand(double[,] predictors, ModelShape shape)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        return Expand(predictors, Terms(shape, predictors.GetLength(1)));
    }

    /// <summary>
    /// <c>x2fx</c> where some predictors are categorical: each such column is replaced by an indicator
    /// for every level but the last, and every term that used it becomes one term per indicator.
    /// </summary>
    /// <param name="predictors">One row per observation, one column per predictor.</param>
    /// <param name="terms">The model terms over the original predictors.</param>
    /// <param name="categorical">The zero-based indices of the categorical predictors.</param>
    public static double[,] Expand(
        double[,] predictors, IReadOnlyList<int[]> terms, IReadOnlyList<int> categorical)
    {
        ArgumentNullException.ThrowIfNull(predictors);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(categorical);

        if (categorical.Count == 0)
        {
            return Expand(predictors, terms);
        }

        int n = predictors.GetLength(0);
        int k = predictors.GetLength(1);

        // Each predictor becomes a list of columns: one for a continuous predictor, one per level but
        // the last for a categorical one. A term is then the product of one choice from each list.
        var columns = new List<double[]>[k];
        for (int c = 0; c < k; c++)
        {
            var built = new List<double[]>();
            if (categorical.Contains(c))
            {
                double[] levels = DistinctLevels(predictors, c);
                for (int level = 0; level + 1 < levels.Length; level++)
                {
                    var indicator = new double[n];
                    for (int r = 0; r < n; r++)
                    {
                        indicator[r] = predictors[r, c] == levels[level] ? 1 : 0;
                    }

                    built.Add(indicator);
                }

                if (built.Count == 0)
                {
                    // A predictor with a single level carries no information; it contributes nothing but
                    // must still contribute a column, or every term through it would vanish.
                    var ones = new double[n];
                    Array.Fill(ones, 1);
                    built.Add(ones);
                }
            }
            else
            {
                var raw = new double[n];
                for (int r = 0; r < n; r++)
                {
                    raw[r] = predictors[r, c];
                }

                built.Add(raw);
            }

            columns[c] = built;
        }

        var expanded = new List<double[]>();
        foreach (int[] powers in terms)
        {
            foreach (double[] column in TermColumns(powers, columns, n, 0))
            {
                expanded.Add(column);
            }
        }

        var design = new double[n, expanded.Count];
        for (int t = 0; t < expanded.Count; t++)
        {
            for (int r = 0; r < n; r++)
            {
                design[r, t] = expanded[t][r];
            }
        }

        return design;
    }

    /// <summary><c>dummyvar</c>: an indicator column for every level of every grouping column.</summary>
    /// <param name="groups">One row per observation; each column is a grouping variable of positive whole numbers.</param>
    public static double[,] Indicators(double[,] groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        int n = groups.GetLength(0);
        int k = groups.GetLength(1);
        var counts = new int[k];
        for (int c = 0; c < k; c++)
        {
            int most = 0;
            for (int r = 0; r < n; r++)
            {
                double value = groups[r, c];
                if (value != Math.Floor(value) || value < 1)
                {
                    throw new ArgumentException(
                        "a grouping variable holds the whole numbers 1, 2, … naming each observation's group; "
                        + $"column {c + 1} holds {value}.");
                }

                most = Math.Max(most, (int)value);
            }

            counts[c] = most;
        }

        int width = 0;
        foreach (int count in counts)
        {
            width += count;
        }

        var indicators = new double[n, width];
        int offset = 0;
        for (int c = 0; c < k; c++)
        {
            for (int r = 0; r < n; r++)
            {
                indicators[r, offset + (int)groups[r, c] - 1] = 1;
            }

            offset += counts[c];
        }

        return indicators;
    }

    /// <summary>The distinct values in one column, in ascending order.</summary>
    public static double[] DistinctLevels(double[,] matrix, int column)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var seen = new SortedSet<double>();
        for (int r = 0; r < matrix.GetLength(0); r++)
        {
            seen.Add(matrix[r, column]);
        }

        return [.. seen];
    }

    /// <summary>
    /// Every column one term expands to: the product of one chosen column from each predictor it uses,
    /// raised to that predictor's power.
    /// </summary>
    private static IEnumerable<double[]> TermColumns(
        int[] powers, List<double[]>[] columns, int n, int predictor)
    {
        if (predictor == powers.Length)
        {
            var ones = new double[n];
            Array.Fill(ones, 1);
            yield return ones;
            yield break;
        }

        foreach (double[] rest in TermColumns(powers, columns, n, predictor + 1))
        {
            if (powers[predictor] == 0)
            {
                yield return rest;
                continue;
            }

            foreach (double[] choice in columns[predictor])
            {
                var product = new double[n];
                for (int r = 0; r < n; r++)
                {
                    double value = rest[r];
                    for (int power = 0; power < powers[predictor]; power++)
                    {
                        value *= choice[r];
                    }

                    product[r] = value;
                }

                yield return product;
            }
        }
    }
}
