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
    public static ImageBuffer Equalize(ImageBuffer image, int bins = 64)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels != 1)
        {
            throw new ArgumentException("histeq expects a grayscale image; convert with rgb2gray first.");
        }

        double[] histogram = Histogram(image, bins);
        int total = image.Height * image.Width;

        // Cumulative distribution, mapped so the darkest occupied level goes to 0 and the brightest to 1.
        var cdf = new double[bins];
        double running = 0;
        for (int i = 0; i < bins; i++)
        {
            running += histogram[i];
            cdf[i] = running / total;
        }

        var result = new ImageBuffer(image.Height, image.Width, 1);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            int bin = (int)(Math.Clamp(src[i], 0, 1) * bins);
            if (bin >= bins)
            {
                bin = bins - 1;
            }

            dst[i] = cdf[bin];
        }

        GC.KeepAlive(image);
        return result;
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
