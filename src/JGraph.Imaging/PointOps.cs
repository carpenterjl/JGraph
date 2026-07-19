namespace JGraph.Imaging;

/// <summary>Per-pixel (point) image operations: colour conversion, intensity mapping, arithmetic, noise.</summary>
public static class PointOps
{
    // Rec.601 luma weights (MATLAB rgb2gray).
    private const double RedWeight = 0.2989;
    private const double GreenWeight = 0.5870;
    private const double BlueWeight = 0.1140;

    /// <summary>Converts an RGB image to grayscale (Rec.601); a grayscale image is cloned.</summary>
    public static ImageBuffer ToGray(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels == 1)
        {
            return image.Clone();
        }

        var gray = new ImageBuffer(image.Height, image.Width, 1);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = gray.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            int b = i * 3;
            dst[i] = (RedWeight * src[b]) + (GreenWeight * src[b + 1]) + (BlueWeight * src[b + 2]);
        }

        GC.KeepAlive(image);
        return gray;
    }

    /// <summary>Scales a scalar field to a grayscale image with min→0 and max→1 (MATLAB <c>mat2gray</c>).</summary>
    public static ImageBuffer Normalize(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int h = values.GetLength(0);
        int w = values.GetLength(1);
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (double v in values)
        {
            if (v < min) { min = v; }
            if (v > max) { max = v; }
        }

        double range = max - min;
        var image = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                image[r, c, 0] = range > 0 ? Math.Clamp((values[r, c] - min) / range, 0, 1) : 0.0;
            }
        }

        return image;
    }

    /// <summary>Wraps a scalar field as a grayscale image with the values clamped to [0, 1] (MATLAB <c>mat2gray</c> of already-scaled data).</summary>
    public static ImageBuffer FromMatrix(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int h = values.GetLength(0);
        int w = values.GetLength(1);
        var image = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                image[r, c, 0] = Math.Clamp(values[r, c], 0, 1);
            }
        }

        return image;
    }

    /// <summary>Copies an image channel to a scalar field (0-based channel).</summary>
    public static double[,] ToMatrix(ImageBuffer image, int channel)
    {
        ArgumentNullException.ThrowIfNull(image);
        if ((uint)channel >= (uint)image.Channels)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, $"channel out of range [0, {image.Channels - 1}]");
        }

        var values = new double[image.Height, image.Width];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                values[r, c] = image[r, c, channel];
            }
        }

        return values;
    }

    /// <summary>
    /// Maps intensities: values in <c>[lowIn, highIn]</c> stretch onto <c>[lowOut, highOut]</c> with the
    /// given gamma (MATLAB <c>imadjust</c>). Values outside the input range clamp to the output ends.
    /// </summary>
    public static ImageBuffer Adjust(ImageBuffer image, double lowIn, double highIn, double lowOut, double highOut, double gamma)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (highIn <= lowIn)
        {
            throw new ArgumentException("imadjust input range requires highIn > lowIn.");
        }

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        double span = highIn - lowIn;
        for (int i = 0; i < dst.Length; i++)
        {
            double t = Math.Clamp((src[i] - lowIn) / span, 0, 1);
            if (gamma != 1.0)
            {
                t = Math.Pow(t, gamma);
            }

            dst[i] = lowOut + ((highOut - lowOut) * t);
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>The <paramref name="lowFraction"/>/<paramref name="highFraction"/> intensity limits over the grayscale histogram (MATLAB <c>stretchlim</c>).</summary>
    public static (double Low, double High) StretchLimits(ImageBuffer image, double lowFraction = 0.01, double highFraction = 0.99)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageBuffer gray = image.Channels == 1 ? image : ToGray(image);
        double[] histogram = Histograms.Histogram(gray, 256);
        if (!ReferenceEquals(gray, image))
        {
            gray.Dispose();
        }

        double total = 0;
        foreach (double count in histogram)
        {
            total += count;
        }

        double low = FindFraction(histogram, total, lowFraction);
        double high = FindFraction(histogram, total, highFraction);
        if (high <= low)
        {
            high = Math.Min(1.0, low + (1.0 / 256));
        }

        return (low, high);
    }

    private static double FindFraction(double[] histogram, double total, double fraction)
    {
        double target = fraction * total;
        double cumulative = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target)
            {
                return i / (double)(histogram.Length - 1);
            }
        }

        return 1.0;
    }

    /// <summary>Adds two images (or an image and a scalar), clamping to [0, 1] (MATLAB <c>imadd</c>).</summary>
    public static ImageBuffer Add(ImageBuffer a, ImageBuffer b) => Combine(a, b, static (x, y) => x + y);

    /// <summary>Subtracts <paramref name="b"/> from <paramref name="a"/>, clamping to [0, 1] (MATLAB <c>imsubtract</c>).</summary>
    public static ImageBuffer Subtract(ImageBuffer a, ImageBuffer b) => Combine(a, b, static (x, y) => x - y);

    /// <summary>Adds a scalar to every sample, clamping to [0, 1].</summary>
    public static ImageBuffer AddScalar(ImageBuffer image, double value) => Map(image, v => v + value);

    /// <summary>Subtracts a scalar from every sample, clamping to [0, 1].</summary>
    public static ImageBuffer SubtractScalar(ImageBuffer image, double value) => Map(image, v => v - value);

    /// <summary>Inverts intensities: <c>1 - v</c> (MATLAB <c>imcomplement</c>).</summary>
    public static ImageBuffer Complement(ImageBuffer image) => Map(image, static v => 1.0 - v);

    private static ImageBuffer Map(ImageBuffer image, Func<double, double> f)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = Math.Clamp(f(src[i]), 0, 1);
        }

        GC.KeepAlive(image);
        return result;
    }

    private static ImageBuffer Combine(ImageBuffer a, ImageBuffer b, Func<double, double, double> f)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Height != b.Height || a.Width != b.Width || a.Channels != b.Channels)
        {
            throw new ArgumentException("image arithmetic requires images of matching size and channel count.");
        }

        var result = new ImageBuffer(a.Height, a.Width, a.Channels);
        ReadOnlySpan<double> pa = a.Pixels;
        ReadOnlySpan<double> pb = b.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = Math.Clamp(f(pa[i], pb[i]), 0, 1);
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        return result;
    }

    /// <summary>Adds zero-mean Gaussian noise of the given variance, clamping to [0, 1] (MATLAB <c>imnoise</c> 'gaussian').</summary>
    public static ImageBuffer GaussianNoise(ImageBuffer image, double mean, double variance, Random random)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(random);
        double sigma = Math.Sqrt(Math.Max(0, variance));
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            dst[i] = Math.Clamp(src[i] + mean + (sigma * gauss), 0, 1);
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>Adds salt &amp; pepper noise at the given density (MATLAB <c>imnoise</c> 'salt &amp; pepper').</summary>
    public static ImageBuffer SaltPepperNoise(ImageBuffer image, double density, Random random)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(random);
        ImageBuffer result = image.Clone();
        Span<double> px = result.Pixels;
        int pixelCount = image.Height * image.Width;
        for (int p = 0; p < pixelCount; p++)
        {
            if (random.NextDouble() >= density)
            {
                continue;
            }

            double value = random.NextDouble() < 0.5 ? 0.0 : 1.0; // pepper or salt
            int baseIndex = p * image.Channels;
            for (int ch = 0; ch < image.Channels; ch++)
            {
                px[baseIndex + ch] = value;
            }
        }

        return result;
    }
}
