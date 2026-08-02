namespace JGraph.Imaging;

/// <summary>
/// Texture by co-occurrence: how often one grey level sits a given distance and direction from
/// another (MATLAB <c>graycomatrix</c>), and the four numbers that summarize such a table
/// (<c>graycoprops</c>).
/// </summary>
/// <remarks>
/// A histogram says which grey levels a picture contains and nothing about how they are arranged, so
/// it cannot tell sand from stripes. The co-occurrence matrix restores the arrangement in the
/// cheapest possible way: instead of one count per level it keeps one count per ordered pair of
/// levels seen at a fixed displacement. Fine texture puts weight far from the diagonal at short
/// displacements; coarse texture keeps it near the diagonal until the displacement grows. Everything
/// <see cref="Properties"/> reports is a different way of asking how far from the diagonal the weight
/// sits.
/// </remarks>
public static class TextureAnalysis
{
    /// <summary>MATLAB's default displacement: one pixel to the right.</summary>
    public static (int Row, int Col)[] DefaultOffsets => [(0, 1)];

    /// <summary>
    /// The co-occurrence matrices of a picture, one per displacement, together with the picture
    /// quantized to the level range they were counted on.
    /// </summary>
    /// <param name="values">The picture.</param>
    /// <param name="levels">How many grey levels to quantize to.</param>
    /// <param name="limits">The sample range the levels span; samples outside it are clamped in.</param>
    /// <param name="offsets">The displacements, each a (row, column) step.</param>
    /// <param name="symmetric">Whether a pair counts in both directions.</param>
    /// <remarks>
    /// Quantizing first is not a shortcut but the point: a 256-level table has 65,536 cells and a
    /// picture rarely has enough pixels to fill them, so the counts would be noise. Eight levels is
    /// MATLAB's default for the same reason.
    /// </remarks>
    public static (double[][,] Matrices, double[,] Scaled) Comatrix(
        double[,] values,
        int levels,
        (double Low, double High) limits,
        IReadOnlyList<(int Row, int Col)> offsets,
        bool symmetric)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentOutOfRangeException.ThrowIfLessThan(levels, 2);
        if (offsets.Count == 0)
        {
            throw new ArgumentException("graycomatrix needs at least one offset.", nameof(offsets));
        }

        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        double span = limits.High - limits.Low;

        // A flat picture has no range to divide by, so every sample lands on the first level — which
        // is right: one level is all the information there is.
        var quantized = new int[rows, cols];
        var scaled = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double sample = values[r, c];
                int level;
                if (double.IsNaN(sample))
                {
                    level = -1;
                }
                else if (span <= 0)
                {
                    level = 0;
                }
                else
                {
                    double position = (sample - limits.Low) / span * levels;
                    level = Math.Clamp((int)Math.Floor(position), 0, levels - 1);
                }

                quantized[r, c] = level;
                scaled[r, c] = level < 0 ? double.NaN : level + 1;
            }
        }

        var matrices = new double[offsets.Count][,];
        for (int k = 0; k < offsets.Count; k++)
        {
            (int dr, int dc) = offsets[k];
            var counts = new double[levels, levels];
            for (int r = 0; r < rows; r++)
            {
                int nr = r + dr;
                if ((uint)nr >= (uint)rows)
                {
                    continue;
                }

                for (int c = 0; c < cols; c++)
                {
                    int nc = c + dc;
                    if ((uint)nc >= (uint)cols)
                    {
                        continue;
                    }

                    int from = quantized[r, c];
                    int to = quantized[nr, nc];
                    if (from < 0 || to < 0)
                    {
                        continue;
                    }

                    counts[from, to]++;
                    if (symmetric)
                    {
                        counts[to, from]++;
                    }
                }
            }

            matrices[k] = counts;
        }

        return (matrices, scaled);
    }

    /// <summary>
    /// The four statistics MATLAB's <c>graycoprops</c> reads off a co-occurrence matrix, computed on
    /// the matrix normalized to a probability.
    /// </summary>
    /// <remarks>
    /// Contrast and homogeneity are each other's opposites — one weights a pair by the square of the
    /// level difference, the other by its reciprocal — so a picture cannot score highly on both.
    /// Energy is the probability that two independent draws give the same pair, which is largest when
    /// only a few pairs ever occur, so it measures uniformity rather than brightness. Correlation is
    /// the ordinary correlation coefficient between the two ends of the displacement, and it is the
    /// one that is undefined for a flat picture: with no variation there is nothing to correlate, and
    /// the answer is NaN rather than a number pretending otherwise.
    /// </remarks>
    public static (double Contrast, double Correlation, double Energy, double Homogeneity) Properties(
        double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int levels = matrix.GetLength(0);
        if (levels != matrix.GetLength(1))
        {
            throw new ArgumentException("a co-occurrence matrix is square.", nameof(matrix));
        }

        double total = 0;
        foreach (double count in matrix)
        {
            total += count;
        }

        if (total <= 0)
        {
            return (0, double.NaN, 0, 0);
        }

        double contrast = 0;
        double energy = 0;
        double homogeneity = 0;
        double meanRow = 0;
        double meanCol = 0;
        for (int i = 0; i < levels; i++)
        {
            for (int j = 0; j < levels; j++)
            {
                double p = matrix[i, j] / total;
                double difference = i - j;
                contrast += difference * difference * p;
                energy += p * p;
                homogeneity += p / (1 + Math.Abs(difference));
                meanRow += (i + 1) * p;
                meanCol += (j + 1) * p;
            }
        }

        double varianceRow = 0;
        double varianceCol = 0;
        double covariance = 0;
        for (int i = 0; i < levels; i++)
        {
            for (int j = 0; j < levels; j++)
            {
                double p = matrix[i, j] / total;
                double di = i + 1 - meanRow;
                double dj = j + 1 - meanCol;
                varianceRow += di * di * p;
                varianceCol += dj * dj * p;
                covariance += di * dj * p;
            }
        }

        double spread = Math.Sqrt(varianceRow * varianceCol);
        double correlation = spread <= 0 ? double.NaN : covariance / spread;
        return (contrast, correlation, energy, homogeneity);
    }
}
