namespace JGraph.Imaging;

/// <summary>Histogram-based image operations: counts, equalization, Otsu thresholding, binarization.</summary>
public static class Histograms
{
    /// <summary>
    /// Counts grayscale samples into <paramref name="bins"/> equal bins over [0, 1] (MATLAB <c>imhist</c>).
    /// Bin <c>i</c> covers <c>[i/bins, (i+1)/bins)</c>; the last bin includes 1.0.
    /// </summary>
    public static double[] Histogram(ImageBuffer image, int bins = 256)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bins);
        if (image.Channels != 1)
        {
            throw new ArgumentException("imhist expects a grayscale image; convert with rgb2gray first.");
        }

        var counts = new double[bins];
        ReadOnlySpan<double> px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            int bin = (int)(Math.Clamp(px[i], 0, 1) * bins);
            if (bin >= bins)
            {
                bin = bins - 1;
            }

            counts[bin]++;
        }

        GC.KeepAlive(image);
        return counts;
    }

    /// <summary>Histogram equalization of a grayscale image over <paramref name="bins"/> levels (MATLAB <c>histeq</c>).</summary>
    public static ImageBuffer Equalize(ImageBuffer image, int bins = 64) => Equalize(image, Flat(bins)).Result;

    /// <summary>A flat target histogram of the given length.</summary>
    private static double[] Flat(int bins)
    {
        if (bins < 2)
        {
            throw new ArgumentException("histeq needs at least two levels.", nameof(bins));
        }

        var shape = new double[bins];
        Array.Fill(shape, 1.0);
        return shape;
    }

    /// <summary>
    /// Reshapes a grayscale image's histogram towards <paramref name="targetShape"/> and hands back
    /// the mapping it used (MATLAB <c>[J, T] = histeq(I, hgram)</c>).
    /// </summary>
    /// <remarks>
    /// Equalization is the special case where the target is flat, so the two share one routine: what
    /// MATLAB's <c>histeq(I, n)</c> does is match against <c>n</c> equal bins. The mapping is the
    /// second output because a transformation measured on one frame is often the one you want applied
    /// to the next, and recomputing it per frame is what makes a sequence flicker.
    /// </remarks>
    public static (ImageBuffer Result, double[] Transform) Equalize(
        ImageBuffer image, IReadOnlyList<double> targetShape)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(targetShape);
        if (image.Channels != 1)
        {
            throw new ArgumentException("histeq expects a grayscale image; convert with rgb2gray first.");
        }

        double[] transform = MatchingTransform(
            Histogram(image, 256), targetShape, (double)image.Height * image.Width);

        var result = new ImageBuffer(image.Height, image.Width, 1);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = transform[(int)Math.Round(Math.Clamp(src[i], 0, 1) * 255)];
        }

        GC.KeepAlive(image);
        return (result, transform);
    }

    /// <summary>
    /// The lookup table that carries a measured histogram onto a wanted one: for each of 256 input
    /// levels, the output level whose cumulative count comes closest to the input's.
    /// </summary>
    /// <remarks>
    /// A discrete histogram can rarely be reshaped exactly — a level holding a tenth of the pixels
    /// cannot be split — so this is stated as a minimization rather than an inversion, which is how
    /// MATLAB states it too. The half-bin tolerance added to each input level is what breaks the ties
    /// that a flat stretch of the cumulative curve otherwise produces, and levels that would have to
    /// map backwards are disqualified outright so the table stays monotone.
    /// </remarks>
    /// <param name="sourceCounts">The measured histogram, 256 bins.</param>
    /// <param name="targetShape">The wanted histogram, at any number of levels.</param>
    /// <param name="totalPixels">How many samples the source histogram counted.</param>
    public static double[] MatchingTransform(
        IReadOnlyList<double> sourceCounts, IReadOnlyList<double> targetShape, double totalPixels)
    {
        ArgumentNullException.ThrowIfNull(sourceCounts);
        ArgumentNullException.ThrowIfNull(targetShape);
        int levels = targetShape.Count;
        if (levels < 2)
        {
            throw new ArgumentException("histeq needs a target histogram with at least two levels.", nameof(targetShape));
        }

        int points = sourceCounts.Count;
        double targetTotal = 0;
        foreach (double value in targetShape)
        {
            if (value < 0)
            {
                throw new ArgumentException("histeq target histogram counts cannot be negative.", nameof(targetShape));
            }

            targetTotal += value;
        }

        if (targetTotal <= 0)
        {
            throw new ArgumentException("histeq target histogram is empty.", nameof(targetShape));
        }

        var sourceCumulative = new double[points];
        double running = 0;
        for (int j = 0; j < points; j++)
        {
            running += sourceCounts[j];
            sourceCumulative[j] = running;
        }

        var targetCumulative = new double[levels];
        running = 0;
        for (int k = 0; k < levels; k++)
        {
            running += targetShape[k] * totalPixels / targetTotal;
            targetCumulative[k] = running;
        }

        double disqualified = totalPixels;
        double slack = totalPixels * Math.Sqrt(2.220446049250313e-16);
        var transform = new double[points];
        for (int j = 0; j < points; j++)
        {
            // The ends get no tolerance: black and white have nowhere to spread to.
            double tolerance = j == 0 || j == points - 1 ? 0 : sourceCounts[j] / 2;
            double bestError = double.PositiveInfinity;
            int bestLevel = 0;
            for (int k = 0; k < levels; k++)
            {
                double error = targetCumulative[k] - sourceCumulative[j] + tolerance;
                if (error < -slack)
                {
                    error = disqualified;
                }

                if (error < bestError)
                {
                    bestError = error;
                    bestLevel = k;
                }
            }

            transform[j] = bestLevel / (double)(levels - 1);
        }

        return transform;
    }

    /// <summary>
    /// Otsu's threshold level in [0, 1] together with its effectiveness metric — the fraction of the
    /// image's total variance the split explains, which is MATLAB's second <c>graythresh</c> output.
    /// A metric near 1 means the histogram really is two clusters; near 0 means the level is arbitrary.
    /// </summary>
    public static (double Level, double Effectiveness) OtsuLevelAndMetric(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageBuffer gray = image.Channels == 1 ? image : PointOps.ToGray(image);
        double[] histogram = Histogram(gray, 256);
        if (!ReferenceEquals(gray, image))
        {
            gray.Dispose();
        }

        return OtsuFromCounts(histogram);
    }

    /// <summary>
    /// Otsu's threshold from histogram counts alone (MATLAB <c>otsuthresh</c>), as a level in [0, 1].
    /// Splitting this out is what lets a caller threshold a histogram it computed itself — the whole
    /// point of <c>otsuthresh</c> beside <c>graythresh</c>.
    /// </summary>
    public static (double Level, double Effectiveness) OtsuFromCounts(IReadOnlyList<double> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count < 2)
        {
            throw new ArgumentException("otsuthresh needs at least two histogram bins.", nameof(counts));
        }

        int bins = counts.Count;
        double total = 0;
        double sum = 0;
        for (int i = 0; i < bins; i++)
        {
            total += counts[i];
            sum += i * counts[i];
        }

        if (total == 0)
        {
            return (0.5, 0.0);
        }

        double mean = sum / total;
        double totalVariance = 0;
        for (int i = 0; i < bins; i++)
        {
            totalVariance += counts[i] * (i - mean) * (i - mean);
        }

        double weightBackground = 0;
        double sumBackground = 0;
        double bestBetween = -1;
        int firstBest = 0;
        int lastBest = 0;
        for (int t = 0; t < bins; t++)
        {
            weightBackground += counts[t];
            if (weightBackground == 0)
            {
                continue;
            }

            double weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += t * counts[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double diff = meanBackground - meanForeground;
            double between = weightBackground * weightForeground * diff * diff / total;
            if (between > bestBetween)
            {
                bestBetween = between;
                firstBest = lastBest = t;
            }
            else if (between == bestBetween)
            {
                lastBest = t; // extend the plateau (bimodal images have a flat valley of equal variance)
            }
        }

        double level = ((firstBest + lastBest) / 2.0) / (bins - 1);
        double effectiveness = totalVariance > 0 ? Math.Clamp(bestBetween / (totalVariance / total), 0, 1) : 0.0;
        return (level, effectiveness);
    }

    /// <summary>
    /// A locally adaptive threshold surface (MATLAB <c>adaptthresh</c>): one threshold per pixel from
    /// the statistics of its neighbourhood, so a picture with a brightness gradient binarizes evenly
    /// where one global level would black out half of it.
    /// </summary>
    /// <param name="image">The image to measure; colour input is converted to gray first.</param>
    /// <param name="sensitivity">0–1, default 0.5. Higher lets more pixels through as foreground.</param>
    /// <param name="neighborhood">Window size (rows, cols); null uses MATLAB's <c>2*floor(size/16)+1</c>.</param>
    /// <param name="statistic">Which local statistic to use.</param>
    /// <param name="darkForeground">True for MATLAB's <c>'ForegroundPolarity','dark'</c>.</param>
    public static ImageBuffer AdaptiveThreshold(
        ImageBuffer image,
        double sensitivity = 0.5,
        (int Height, int Width)? neighborhood = null,
        LocalStatistic statistic = LocalStatistic.Mean,
        bool darkForeground = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (sensitivity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sensitivity), sensitivity, "sensitivity must be in [0, 1].");
        }

        ImageBuffer gray = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
        ImageBuffer source = darkForeground ? PointOps.Complement(gray) : gray;
        if (!ReferenceEquals(source, gray))
        {
            gray.Dispose();
        }

        (int nh, int nw) = neighborhood ?? (OddWindow(image.Height), OddWindow(image.Width));
        ImageBuffer local = statistic switch
        {
            LocalStatistic.Median => Filters.Median(source, nh, nw),
            LocalStatistic.Gaussian => Filters.Correlate(
                source, Kernels.Gaussian(Math.Max(nh, nw), Math.Max(nh, nw) / 6.0), Filters.Boundary.Replicate),
            _ => Filters.BoxMean(source, nh, nw),
        };

        source.Dispose();

        // Bradley's rule, generalized over the sensitivity knob: at 0.5 the threshold is the local
        // statistic itself, and moving toward 1 lowers it so more pixels clear the bar.
        double bias = 1.0 - (0.6 * (sensitivity - 0.5));
        Span<double> px = local.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = Math.Clamp(px[i] * bias, 0, 1);
        }

        GC.KeepAlive(local);
        return local;
    }

    /// <summary>MATLAB's default <c>adaptthresh</c> window: an odd fraction of the image's own size.</summary>
    private static int OddWindow(int extent) => (2 * (extent / 16)) + 1;

    /// <summary>Which neighbourhood statistic <see cref="AdaptiveThreshold"/> compares against.</summary>
    public enum LocalStatistic
    {
        /// <summary>The local mean (MATLAB's default).</summary>
        Mean,

        /// <summary>The local median — slower, but immune to a bright speck in the window.</summary>
        Median,

        /// <summary>A Gaussian-weighted local mean.</summary>
        Gaussian,
    }

    /// <summary>Otsu's threshold level in [0, 1] over a 256-bin grayscale histogram (MATLAB <c>graythresh</c>).</summary>
    public static double OtsuLevel(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageBuffer gray = image.Channels == 1 ? image : PointOps.ToGray(image);
        double[] histogram = Histogram(gray, 256);
        if (!ReferenceEquals(gray, image))
        {
            gray.Dispose();
        }

        double total = 0;
        double sum = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            total += histogram[i];
            sum += i * histogram[i];
        }

        if (total == 0)
        {
            return 0.5;
        }

        double weightBackground = 0;
        double sumBackground = 0;
        double bestVariance = -1;
        int firstBest = 0;
        int lastBest = 0;
        for (int t = 0; t < histogram.Length; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
            {
                continue;
            }

            double weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += t * histogram[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double between = weightBackground * weightForeground * Math.Pow(meanBackground - meanForeground, 2);
            if (between > bestVariance)
            {
                bestVariance = between;
                firstBest = lastBest = t;
            }
            else if (between == bestVariance)
            {
                lastBest = t; // extend the plateau (bimodal images have a flat valley of equal variance)
            }
        }

        // The midpoint of the max-variance plateau sits in the valley between the two clusters.
        return ((firstBest + lastBest) / 2.0) / 255.0;
    }

    /// <summary>
    /// Thresholds against a per-pixel threshold surface — what <see cref="AdaptiveThreshold"/> produces
    /// and what MATLAB's <c>imbinarize(I, T)</c> takes.
    /// </summary>
    public static ImageBuffer Binarize(ImageBuffer image, ImageBuffer thresholds)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (thresholds.Height != image.Height || thresholds.Width != image.Width || thresholds.Channels != 1)
        {
            throw new ArgumentException(
                "imbinarize needs a one-channel threshold image the same size as the picture.", nameof(thresholds));
        }

        ImageBuffer gray = image.Channels == 1 ? image : PointOps.ToGray(image);
        var result = new ImageBuffer(image.Height, image.Width, 1);
        ReadOnlySpan<double> src = gray.Pixels;
        ReadOnlySpan<double> level = thresholds.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = src[i] > level[i] ? 1.0 : 0.0;
        }

        GC.KeepAlive(gray);
        GC.KeepAlive(thresholds);
        if (!ReferenceEquals(gray, image))
        {
            gray.Dispose();
        }

        return result;
    }

    /// <summary>Thresholds an image to a binary (0/1) image; the default level is Otsu's (MATLAB <c>imbinarize</c>).</summary>
    public static ImageBuffer Binarize(ImageBuffer image, double? level = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageBuffer gray = image.Channels == 1 ? image : PointOps.ToGray(image);
        double threshold = level ?? OtsuLevel(gray);

        var result = new ImageBuffer(image.Height, image.Width, 1);
        ReadOnlySpan<double> src = gray.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = src[i] > threshold ? 1.0 : 0.0;
        }

        GC.KeepAlive(gray);
        if (!ReferenceEquals(gray, image))
        {
            gray.Dispose();
        }

        return result;
    }
}
