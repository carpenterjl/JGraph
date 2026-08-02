namespace JGraph.Imaging;

/// <summary>
/// How close one picture is to another: squared error and peak signal-to-noise, structural similarity
/// at one scale and at several, and the three ways of scoring a segmentation against a truth.
/// </summary>
/// <remarks>
/// The family exists because the obvious answer is the wrong one. Mean squared error compares samples
/// at the same address and nothing else, so a picture shifted by one pixel scores as badly as a
/// picture of something else, and a mild blur — which anyone can see — scores better than a faint
/// noise nobody notices. Everything past <see cref="MeanSquaredError"/> is an attempt to score what a
/// viewer would say instead: structural similarity compares local statistics rather than samples, its
/// multiscale form admits that the right scale depends on how far away you sit, and the overlap
/// measures give up on intensity entirely and ask only whether the same pixels were chosen.
/// </remarks>
public static partial class QualityMetrics
{
    /// <summary>The weights MS-SSIM was published with, one per scale, already summing to one.</summary>
    public static double[] DefaultScaleWeights => [0.0448, 0.2856, 0.3001, 0.2363, 0.1333];

    /// <summary>The mean squared difference between two same-size pictures (MATLAB <c>immse</c>).</summary>
    public static double MeanSquaredError(double[,] a, double[,] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Statistics.RequireSameSize(a, b);

        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        if (rows == 0 || cols == 0)
        {
            return double.NaN;
        }

        double total = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double difference = a[r, c] - b[r, c];
                total += difference * difference;
            }
        }

        return total / ((long)rows * cols);
    }

    /// <summary>
    /// Peak signal-to-noise ratio in decibels (MATLAB <c>psnr</c>), against a peak value that is
    /// normally the largest sample the picture's class can hold.
    /// </summary>
    /// <remarks>
    /// This is the mean squared error again, turned upside down and put on a log scale so that a
    /// bigger number means a better picture. The peak is what makes it comparable across classes: an
    /// error of one grey level out of 255 and the same error out of 65535 are not the same error, and
    /// dividing by the peak is what says so. Two identical pictures give infinity, which is correct
    /// and is why the answer is not clamped.
    /// </remarks>
    public static double PeakSignalToNoise(double[,] a, double[,] b, double peak)
    {
        double error = MeanSquaredError(a, b);
        if (error == 0)
        {
            return double.PositiveInfinity;
        }

        return 10 * Math.Log10(peak * peak / error);
    }

    /// <summary>
    /// The knobs on <see cref="StructuralSimilarity(double[,], double[,], SsimOptions)"/>, defaulted to
    /// MATLAB's own.
    /// </summary>
    /// <param name="DynamicRange">The span of the sample values, which sets the two stabilizing constants.</param>
    /// <param name="Radius">The standard deviation of the Gaussian window the local statistics are taken through.</param>
    /// <param name="Exponents">The weights on luminance, contrast and structure.</param>
    /// <param name="RegularizationConstants">The two K values that keep the ratios finite where a region is flat.</param>
    public readonly record struct SsimOptions(
        double DynamicRange = 1.0,
        double Radius = 1.5,
        double[]? Exponents = null,
        double[]? RegularizationConstants = null)
    {
        /// <summary>The exponents to use, defaulted.</summary>
        public double[] Weights => Exponents ?? [1, 1, 1];

        /// <summary>The regularization constants to use, defaulted.</summary>
        public double[] Constants => RegularizationConstants ?? [0.01, 0.03];
    }

    /// <summary>
    /// Structural similarity between two same-size pictures (MATLAB <c>ssim</c>), as an overall score
    /// and the map it is the mean of.
    /// </summary>
    /// <remarks>
    /// The idea is that a viewer does not compare samples, they compare neighbourhoods, so the picture
    /// is read through a Gaussian window and three questions are asked of each position: is it as
    /// bright, does it vary as much, and does it vary the same way. The first two are ratios of means
    /// and of spreads, the third is the correlation between them, and each is written so that equal
    /// inputs give exactly one. The two constants exist for flat regions, where every one of those
    /// ratios is zero over zero.
    /// </remarks>
    public static (double Score, double[,] Map) StructuralSimilarity(
        double[,] a, double[,] b, SsimOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Statistics.RequireSameSize(a, b);

        double range = options.DynamicRange <= 0 ? 1.0 : options.DynamicRange;
        double radius = options.Radius <= 0 ? 1.5 : options.Radius;
        double[] exponents = options.Weights;
        double[] constants = options.Constants;
        if (exponents.Length != 3)
        {
            throw new ArgumentException("ssim takes three exponents: luminance, contrast and structure.");
        }

        if (constants.Length != 2)
        {
            throw new ArgumentException("ssim takes two regularization constants.");
        }

        double c1 = constants[0] * range * (constants[0] * range);
        double c2 = constants[1] * range * (constants[1] * range);
        double c3 = c2 / 2;

        double[] window = Filters.Gaussian1D(radius, (2 * (int)Math.Ceiling(3 * radius)) + 1);
        double[,] meanA = Smooth(a, window);
        double[,] meanB = Smooth(b, window);
        double[,] squareA = Smooth(Product(a, a), window);
        double[,] squareB = Smooth(Product(b, b), window);
        double[,] cross = Smooth(Product(a, b), window);

        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var map = new double[rows, cols];

        // All-ones exponents are the overwhelmingly common case and the two forms are algebraically
        // identical there, but the general form needs the square roots of the two variances, which
        // rounding can drive slightly below zero. Taking the short form when it applies keeps the
        // default free of that clamp.
        bool plain = exponents[0] == 1 && exponents[1] == 1 && exponents[2] == 1;
        double total = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double mA = meanA[r, c];
                double mB = meanB[r, c];
                double varA = squareA[r, c] - (mA * mA);
                double varB = squareB[r, c] - (mB * mB);
                double covariance = cross[r, c] - (mA * mB);

                double luminance = ((2 * mA * mB) + c1) / ((mA * mA) + (mB * mB) + c1);
                double value;
                if (plain)
                {
                    value = luminance * (((2 * covariance) + c2) / (varA + varB + c2));
                }
                else
                {
                    double sdA = Math.Sqrt(Math.Max(0, varA));
                    double sdB = Math.Sqrt(Math.Max(0, varB));
                    double contrast = ((2 * sdA * sdB) + c2) / (varA + varB + c2);
                    double structure = (covariance + c3) / ((sdA * sdB) + c3);
                    value = Math.Pow(luminance, exponents[0])
                        * Math.Pow(contrast, exponents[1])
                        * Math.Pow(structure, exponents[2]);
                }

                map[r, c] = value;
                total += value;
            }
        }

        return (total / ((long)rows * cols), map);
    }

    /// <summary>
    /// Multiscale structural similarity (MATLAB <c>multissim</c>): the contrast-and-structure terms
    /// from every scale, weighted together, with the luminance term taken from the coarsest alone.
    /// </summary>
    /// <remarks>
    /// One scale is a choice about viewing distance that nothing in the picture justifies, and a
    /// detail that matters held at arm's length may not matter across a room. So the comparison is run
    /// down a pyramid, each level half the last, and the scores are combined with the weights the
    /// method was published with. Only the coarsest level contributes a brightness term, because a
    /// difference in overall level is one fact about the pair and counting it once per scale would
    /// weight it five times over.
    /// </remarks>
    /// <returns>The overall score and the quality map from each scale, coarsest last.</returns>
    public static (double Score, double[][,] Maps) MultiScaleSimilarity(
        double[,] a, double[,] b, int scales, double[]? weights = null, SsimOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Statistics.RequireSameSize(a, b);
        ArgumentOutOfRangeException.ThrowIfLessThan(scales, 1);

        double[] scaleWeights = weights ?? DefaultScaleWeights;
        if (scaleWeights.Length < scales)
        {
            throw new ArgumentException(
                $"multissim was asked for {scales} scales but given {scaleWeights.Length} weights.");
        }

        double smallest = Math.Min(a.GetLength(0), a.GetLength(1));
        double needed = Math.Pow(2, scales - 1);
        if (smallest < needed * 2)
        {
            throw new ArgumentException(
                $"multissim over {scales} scales needs a picture at least {(int)needed * 2} pixels on a " +
                $"side; this one is {a.GetLength(0)}-by-{a.GetLength(1)}. Ask for fewer scales.");
        }

        double[,] currentA = a;
        double[,] currentB = b;
        var maps = new double[scales][,];
        double logScore = 0;
        double weightTotal = 0;
        for (int level = 0; level < scales; level++)
        {
            (double score, double[,] map) = StructuralSimilarity(currentA, currentB, options);
            maps[level] = map;

            // Every level contributes its contrast and structure; only the last contributes luminance,
            // which is why the finer levels are divided by their own brightness term rather than being
            // computed a second time without it.
            double contribution = level == scales - 1 ? score : score / LuminanceMean(currentA, currentB, options);
            double weight = scaleWeights[level];
            weightTotal += weight;
            logScore += weight * Math.Log(Math.Max(contribution, 1e-12));

            if (level < scales - 1)
            {
                currentA = Halve(currentA);
                currentB = Halve(currentB);
            }
        }

        return (Math.Exp(logScore / (weightTotal == 0 ? 1 : weightTotal)), maps);
    }

    /// <summary>
    /// The Sørensen–Dice overlap of two masks or two label maps (MATLAB <c>dice</c>): twice the shared
    /// area over the total area, one value per label.
    /// </summary>
    public static double[] Dice(double[,] a, double[,] b) => Overlap(a, b, dice: true);

    /// <summary>
    /// The Jaccard overlap of two masks or two label maps (MATLAB <c>jaccard</c>): the shared area over
    /// the area either one covers, one value per label.
    /// </summary>
    /// <remarks>
    /// Dice and Jaccard order every pair of segmentations identically — each is a monotone function of
    /// the other — so nothing is learned by computing both. They differ in what they say about a
    /// middling result: Dice is the kinder of the two, because the shared area is counted twice in its
    /// numerator and once in each of the two totals it is divided by.
    /// </remarks>
    public static double[] Jaccard(double[,] a, double[,] b) => Overlap(a, b, dice: false);

    /// <summary>
    /// The boundary F1 score of a segmentation against a truth (MATLAB <c>bfscore</c>): how much of
    /// each outline lies within <paramref name="threshold"/> pixels of the other.
    /// </summary>
    /// <remarks>
    /// Overlap measures are dominated by a region's interior, which is exactly the part nobody is
    /// unsure about; disagreement lives on the edge, and in a large region it can be invisible in a
    /// Dice score. Scoring the outlines instead makes the measure sensitive to the boundary and
    /// indifferent to the area behind it, and the tolerance is what makes that workable — an outline
    /// one pixel out is right, not wrong.
    /// </remarks>
    /// <returns>The F1 score, precision and recall, one entry per label.</returns>
    public static (double[] Score, double[] Precision, double[] Recall) BoundaryFScore(
        double[,] prediction, double[,] truth, double threshold)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(truth);
        Statistics.RequireSameSize(prediction, truth);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);

        (int[] labels, bool binary) = LabelsOf(prediction, truth);
        var scores = new double[labels.Length];
        var precisions = new double[labels.Length];
        var recalls = new double[labels.Length];
        int rows = prediction.GetLength(0);
        int cols = prediction.GetLength(1);

        for (int i = 0; i < labels.Length; i++)
        {
            bool[,] predictedMask = MaskOf(prediction, labels[i], binary);
            bool[,] truthMask = MaskOf(truth, labels[i], binary);
            bool[,] predictedEdge = Outline(predictedMask);
            bool[,] truthEdge = Outline(truthMask);

            double[] toTruth = EdgeDistance(truthEdge, rows, cols);
            double[] toPrediction = EdgeDistance(predictedEdge, rows, cols);

            precisions[i] = Within(predictedEdge, toTruth, rows, cols, threshold);
            recalls[i] = Within(truthEdge, toPrediction, rows, cols, threshold);
            double sum = precisions[i] + recalls[i];
            scores[i] = sum == 0 ? 0 : 2 * precisions[i] * recalls[i] / sum;
        }

        return (scores, precisions, recalls);
    }

    /// <summary>
    /// The default boundary tolerance MATLAB uses: three quarters of one percent of the picture's
    /// diagonal, so that the same script scores the same way on a picture of any size.
    /// </summary>
    public static double DefaultBoundaryTolerance(int rows, int cols) =>
        0.0075 * Math.Sqrt(((double)rows * rows) + ((double)cols * cols));

    /// <summary>The mean of the luminance term alone, which is what the finer scales divide out.</summary>
    private static double LuminanceMean(double[,] a, double[,] b, SsimOptions options)
    {
        double range = options.DynamicRange <= 0 ? 1.0 : options.DynamicRange;
        double radius = options.Radius <= 0 ? 1.5 : options.Radius;
        double c1 = options.Constants[0] * range * (options.Constants[0] * range);

        double[] window = Filters.Gaussian1D(radius, (2 * (int)Math.Ceiling(3 * radius)) + 1);
        double[,] meanA = Smooth(a, window);
        double[,] meanB = Smooth(b, window);

        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        double total = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double mA = meanA[r, c];
                double mB = meanB[r, c];
                total += ((2 * mA * mB) + c1) / ((mA * mA) + (mB * mB) + c1);
            }
        }

        double mean = total / ((long)rows * cols);
        return mean == 0 ? 1 : mean;
    }

    /// <summary>Separable Gaussian smoothing with a replicated border, which is what <c>ssim</c> reads through.</summary>
    private static double[,] Smooth(double[,] values, double[] window)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        int half = window.Length / 2;
        var horizontal = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double sum = 0;
                for (int k = 0; k < window.Length; k++)
                {
                    int at = Math.Clamp(c + k - half, 0, cols - 1);
                    sum += window[k] * values[r, at];
                }

                horizontal[r, c] = sum;
            }
        }

        var result = new double[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                double sum = 0;
                for (int k = 0; k < window.Length; k++)
                {
                    int at = Math.Clamp(r + k - half, 0, rows - 1);
                    sum += window[k] * horizontal[at, c];
                }

                result[r, c] = sum;
            }
        }

        return result;
    }

    private static double[,] Product(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var result = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                result[r, c] = a[r, c] * b[r, c];
            }
        }

        return result;
    }

    /// <summary>
    /// One step down the pyramid: a two-by-two average, then every other sample. That is the
    /// downsampling the multiscale method was published with, and it is deliberately cruder than
    /// <see cref="Geometry.Resize"/> — the point of the low-pass is to stop the next level seeing
    /// aliases of detail it is supposed to have left behind, not to look good.
    /// </summary>
    private static double[,] Halve(double[,] values)
    {
        int rows = values.GetLength(0) / 2;
        int cols = values.GetLength(1) / 2;
        var result = new double[Math.Max(1, rows), Math.Max(1, cols)];
        for (int r = 0; r < result.GetLength(0); r++)
        {
            for (int c = 0; c < result.GetLength(1); c++)
            {
                int r0 = Math.Min((2 * r) + 1, values.GetLength(0) - 1);
                int c0 = Math.Min((2 * c) + 1, values.GetLength(1) - 1);
                result[r, c] = (values[2 * r, 2 * c] + values[2 * r, c0]
                    + values[r0, 2 * c] + values[r0, c0]) / 4;
            }
        }

        return result;
    }

    private static double[] Overlap(double[,] a, double[,] b, bool dice)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Statistics.RequireSameSize(a, b);

        (int[] labels, bool binary) = LabelsOf(a, b);
        var result = new double[labels.Length];
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        for (int i = 0; i < labels.Length; i++)
        {
            long inA = 0;
            long inB = 0;
            long shared = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    bool here = Is(a[r, c], labels[i], binary);
                    bool there = Is(b[r, c], labels[i], binary);
                    if (here) { inA++; }
                    if (there) { inB++; }
                    if (here && there) { shared++; }
                }
            }

            // Two empty regions agree perfectly, which is the only reading that keeps a label neither
            // segmentation used from dragging the average down.
            long union = inA + inB - shared;
            result[i] = dice
                ? (inA + inB == 0 ? 1 : 2.0 * shared / (inA + inB))
                : (union == 0 ? 1 : (double)shared / union);
        }

        return result;
    }

    /// <summary>
    /// The labels two maps between them use, and whether the pair is to be read as masks. Nothing
    /// above one anywhere means two masks, and one score comes back; anything more is a pair of label
    /// maps, and a score comes back per label. That is how MATLAB tells the two cases apart, and it is
    /// why a mask and a one-region label map give the same answer — they are the same picture.
    /// </summary>
    private static (int[] Labels, bool Binary) LabelsOf(double[,] a, double[,] b)
    {
        double highest = 0;
        foreach (double[,] map in new[] { a, b })
        {
            foreach (double value in map)
            {
                if (value > highest)
                {
                    highest = value;
                }
            }
        }

        int top = (int)Math.Round(highest);
        if (top <= 1)
        {
            return ([1], true);
        }

        var labels = new int[top];
        for (int i = 0; i < top; i++)
        {
            labels[i] = i + 1;
        }

        return (labels, false);
    }

    private static bool Is(double value, int label, bool binary) =>
        binary ? value != 0 : (int)Math.Round(value) == label;

    private static bool[,] MaskOf(double[,] map, int label, bool binary)
    {
        int rows = map.GetLength(0);
        int cols = map.GetLength(1);
        var mask = new bool[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                mask[r, c] = Is(map[r, c], label, binary);
            }
        }

        return mask;
    }

    /// <summary>The pixels of a region that touch something outside it — its outline, one pixel thick.</summary>
    private static bool[,] Outline(bool[,] mask)
    {
        int rows = mask.GetLength(0);
        int cols = mask.GetLength(1);
        var edge = new bool[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (!mask[r, c])
                {
                    continue;
                }

                // The picture's own border counts as outside, so a region running off the edge still
                // has an outline there — otherwise a segmentation that reached the edge would score
                // against a truth that did the same.
                bool boundary = r == 0 || c == 0 || r == rows - 1 || c == cols - 1
                    || !mask[r - 1, c] || !mask[r + 1, c] || !mask[r, c - 1] || !mask[r, c + 1];
                edge[r, c] = boundary;
            }
        }

        return edge;
    }

    /// <summary>The distance from every pixel to the nearest outline pixel, row-major.</summary>
    private static double[] EdgeDistance(bool[,] edge, int rows, int cols)
    {
        using var buffer = new ImageBuffer(rows, cols, 1);
        bool any = false;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                buffer[r, c, 0] = edge[r, c] ? 1 : 0;
                any |= edge[r, c];
            }
        }

        if (!any)
        {
            var empty = new double[rows * cols];
            Array.Fill(empty, double.PositiveInfinity);
            return empty;
        }

        return DistanceTransforms.Transform(buffer).Distance;
    }

    private static double Within(bool[,] edge, double[] distance, int rows, int cols, double threshold)
    {
        long total = 0;
        long near = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (!edge[r, c])
                {
                    continue;
                }

                total++;
                if (distance[(r * cols) + c] <= threshold)
                {
                    near++;
                }
            }
        }

        return total == 0 ? 0 : (double)near / total;
    }
}
