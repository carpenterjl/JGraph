using JGraph.Statistics.Cluster;

namespace JGraph.Statistics.Multivariate;

/// <summary>
/// t-distributed stochastic neighbour embedding: a low-dimensional picture in which points that were
/// near each other in the original space are near each other again.
/// </summary>
/// <remarks>
/// <para>
/// The method turns distances into probabilities twice and then makes the two agree. In the original
/// space, the chance of picking point j as a neighbour of point i falls off like a normal whose width
/// is set per point so that the number of neighbours it effectively has matches the requested
/// perplexity. In the picture, the same chance falls off like a t with one degree of freedom, whose
/// heavy tail is what stops distant points from being crushed together. Gradient descent on the
/// divergence between the two is the whole algorithm.
/// </para>
/// <para>
/// This is the exact form, over all pairs, not the tree-approximated one: the cost is quadratic in the
/// number of points, which is honest for the sizes a script hands to a function like this and avoids a
/// second approximation on top of the one the method already is.
/// </para>
/// </remarks>
public static class StochasticEmbedding
{
    /// <summary>What an embedding produced.</summary>
    /// <param name="Coordinates">One point per row, in the requested number of dimensions.</param>
    /// <param name="Loss">The divergence at the end of each iteration.</param>
    public readonly record struct Embedding(double[][] Coordinates, double[] Loss);

    /// <summary>Embeds the rows of <paramref name="data"/>.</summary>
    /// <param name="random">Where the starting positions come from.</param>
    /// <param name="data">One observation per row.</param>
    /// <param name="dimensions">How many dimensions the picture has.</param>
    /// <param name="perplexity">How many neighbours each point should effectively have.</param>
    /// <param name="exaggeration">How much the early iterations overstate the original probabilities.</param>
    /// <param name="rate">The gradient descent step.</param>
    /// <param name="iterations">How many steps to take.</param>
    /// <param name="measure">How distance is measured in the original space.</param>
    /// <param name="start">Starting coordinates, or null to draw them.</param>
    public static Embedding Embed(
        Random random,
        double[][] data,
        int dimensions,
        double perplexity,
        double exaggeration,
        double rate,
        int iterations,
        DistanceMeasure measure,
        double[][]? start)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(data);
        int n = data.Length;
        if (n < 3)
        {
            throw new ArgumentException("An embedding needs at least three observations.", nameof(data));
        }

        if (dimensions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "At least one dimension.");
        }

        if (!(perplexity > 0) || perplexity >= n)
        {
            throw new ArgumentOutOfRangeException(
                nameof(perplexity), "The perplexity must be above zero and below the number of observations.");
        }

        double[,] squared = SquaredDistances(data, measure);
        double[,] affinity = Affinities(squared, perplexity);

        var y = new double[n][];
        for (int i = 0; i < n; i++)
        {
            y[i] = new double[dimensions];
            for (int d = 0; d < dimensions; d++)
            {
                y[i][d] = start is not null ? start[i][d] : 1e-4 * Gaussian(random);
            }
        }

        var velocity = new double[n][];
        var gains = new double[n][];
        for (int i = 0; i < n; i++)
        {
            velocity[i] = new double[dimensions];
            gains[i] = new double[dimensions];
            Array.Fill(gains[i], 1);
        }

        var loss = new double[iterations];
        var lowAffinity = new double[n, n];

        for (int step = 0; step < iterations; step++)
        {
            // The early iterations exaggerate the original probabilities, which opens gaps between the
            // clusters before the picture has to settle inside them.
            double factor = step < iterations / 4 ? exaggeration : 1;
            double momentum = step < 20 ? 0.5 : 0.8;

            // How far a point may move in one step. Nothing in the published method bounds the step,
            // and with few points it does not need bounding — but the step is proportional to the
            // learning rate times a gain that grows while the gradient keeps its sign, and the product
            // of those two can throw a point far enough that every force on it is negligible and it
            // never comes back. Tying the bound to how large the picture already is lets it grow from
            // nothing at the start and still never leave in one jump.
            double limit = Math.Max(RootMeanSquare(y, dimensions), 0.5);

            double total = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double distance = 0;
                    for (int d = 0; d < dimensions; d++)
                    {
                        double delta = y[i][d] - y[j][d];
                        distance += delta * delta;
                    }

                    double q = 1 / (1 + distance);
                    lowAffinity[i, j] = q;
                    lowAffinity[j, i] = q;
                    total += 2 * q;
                }
            }

            double divergence = 0;
            for (int i = 0; i < n; i++)
            {
                var gradient = new double[dimensions];
                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    double q = lowAffinity[i, j] / total;
                    double p = factor * affinity[i, j];
                    double weight = (p - q) * lowAffinity[i, j];
                    for (int d = 0; d < dimensions; d++)
                    {
                        gradient[d] += 4 * weight * (y[i][d] - y[j][d]);
                    }

                    if (affinity[i, j] > 0 && q > 0)
                    {
                        divergence += affinity[i, j] * Math.Log(affinity[i, j] / q);
                    }
                }

                for (int d = 0; d < dimensions; d++)
                {
                    // The gain rises while the gradient keeps its sign and is cut when it flips, which
                    // is the step-size rule the method is published with. The ceiling is not in that
                    // rule and is here because without one a gain that never flips grows without
                    // bound, and a step proportional to it eventually leaves the picture entirely.
                    gains[i][d] = Math.Sign(gradient[d]) != Math.Sign(velocity[i][d])
                        ? Math.Min(gains[i][d] + 0.2, 5)
                        : Math.Max(gains[i][d] * 0.8, 0.01);

                    velocity[i][d] = Math.Clamp(
                        (momentum * velocity[i][d]) - (rate * gains[i][d] * gradient[d]), -limit, limit);
                    y[i][d] += velocity[i][d];
                }
            }

            Centre(y, dimensions);
            loss[step] = divergence;
        }

        return new Embedding(y, loss);
    }

    /// <summary>
    /// The original-space probabilities, symmetrized. Each point's own width is found by bisection so
    /// that its neighbourhood has the requested perplexity, because one width for all points would
    /// make a dense region and a sparse one incomparable.
    /// </summary>
    private static double[,] Affinities(double[,] squared, double perplexity)
    {
        int n = squared.GetLength(0);
        var p = new double[n, n];
        double target = Math.Log(perplexity);

        for (int i = 0; i < n; i++)
        {
            double low = 0;
            double high = double.PositiveInfinity;
            double beta = 1;
            var row = new double[n];

            for (int attempt = 0; attempt < 60; attempt++)
            {
                double sum = 0;
                for (int j = 0; j < n; j++)
                {
                    row[j] = i == j ? 0 : Math.Exp(-beta * squared[i, j]);
                    sum += row[j];
                }

                if (sum <= 0)
                {
                    sum = 1e-300;
                }

                double entropy = 0;
                for (int j = 0; j < n; j++)
                {
                    if (row[j] > 0)
                    {
                        entropy += beta * squared[i, j] * row[j] / sum;
                    }
                }

                entropy += Math.Log(sum);

                double error = entropy - target;
                if (Math.Abs(error) < 1e-6)
                {
                    break;
                }

                if (error > 0)
                {
                    low = beta;
                    beta = double.IsPositiveInfinity(high) ? beta * 2 : (beta + high) / 2;
                }
                else
                {
                    high = beta;
                    beta = (beta + low) / 2;
                }
            }

            double normalizer = 0;
            for (int j = 0; j < n; j++)
            {
                normalizer += row[j];
            }

            for (int j = 0; j < n; j++)
            {
                p[i, j] = normalizer > 0 ? row[j] / normalizer : 0;
            }
        }

        var symmetric = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                symmetric[i, j] = (p[i, j] + p[j, i]) / (2 * n);
            }
        }

        return symmetric;
    }

    private static double[,] SquaredDistances(double[][] data, DistanceMeasure measure)
    {
        int n = data.Length;
        double[,] between = Distances.Between(data, data, measure);
        var squared = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                squared[i, j] = between[i, j] * between[i, j];
                squared[j, i] = squared[i, j];
            }
        }

        return squared;
    }

    /// <summary>How far the picture reaches from its own centre, in the root-mean-square sense.</summary>
    private static double RootMeanSquare(double[][] y, int dimensions)
    {
        double total = 0;
        foreach (double[] point in y)
        {
            for (int d = 0; d < dimensions; d++)
            {
                total += point[d] * point[d];
            }
        }

        return Math.Sqrt(total / y.Length);
    }

    private static void Centre(double[][] y, int dimensions)
    {
        for (int d = 0; d < dimensions; d++)
        {
            double mean = 0;
            foreach (double[] point in y)
            {
                mean += point[d];
            }

            mean /= y.Length;
            foreach (double[] point in y)
            {
                point[d] -= mean;
            }
        }
    }

    private static double Gaussian(Random random)
    {
        double u = 1 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }
}
