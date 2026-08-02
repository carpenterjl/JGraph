namespace JGraph.Imaging;

/// <summary>
/// Contrast and illumination enhancement: contrast-limited adaptive histogram equalization,
/// histogram matching, flat-field correction, the decorrelation stretch, unsharp masking, and the
/// dark-channel haze pair.
/// </summary>
/// <remarks>
/// Everything here shares one habit: measure the picture, build a mapping, then apply it. The
/// mappings differ — a per-tile CDF for CLAHE, an inverse CDF for matching, a whitening matrix for
/// the decorrelation stretch — but keeping the measurement and the application apart is what lets
/// <c>histeq</c> hand its transformation back as a second output and lets CLAHE interpolate between
/// eight neighbouring mappings without recomputing any of them.
/// </remarks>
public static class Enhancement
{
    /// <summary>The shape CLAHE flattens each tile's histogram towards.</summary>
    public enum HistogramShape
    {
        /// <summary>A flat histogram — plain equalization within the tile.</summary>
        Uniform,

        /// <summary>A Rayleigh distribution, which keeps dark scenes from washing out.</summary>
        Rayleigh,

        /// <summary>An exponential distribution, the most aggressive of the three on shadows.</summary>
        Exponential,
    }

    /// <summary>Which second-order statistic the decorrelation stretch whitens.</summary>
    public enum StretchMode
    {
        /// <summary>The correlation matrix — bands are rescaled to unit variance first.</summary>
        Correlation,

        /// <summary>The covariance matrix, so a band's own spread carries into the transform.</summary>
        Covariance,
    }

    /// <summary>How <see cref="ReduceHaze"/> estimates the transmission map.</summary>
    public enum HazeMethod
    {
        /// <summary>The dark channel taken over a local window (He et al.).</summary>
        SimpleDarkChannel,

        /// <summary>A per-pixel dark channel, refined by a guided filter.</summary>
        ApproximateDarkChannel,
    }

    /// <summary>What <see cref="ReduceHaze"/> does to the contrast once the haze is removed.</summary>
    public enum HazeContrast
    {
        /// <summary>Stretch the result onto the full range.</summary>
        Global,

        /// <summary>Stretch, then push local contrast further by the boost amount.</summary>
        Boost,

        /// <summary>Leave the dehazed values alone.</summary>
        None,
    }

    // ---------------------------------------------------------------------------------------
    // Contrast-limited adaptive histogram equalization (adapthisteq)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Contrast-limited adaptive histogram equalization (MATLAB <c>adapthisteq</c>): equalize each
    /// tile of the picture separately, then blend the tiles' mappings so no seam shows.
    /// </summary>
    /// <remarks>
    /// Two details make this more than per-tile <c>histeq</c>. The clip limit caps how tall any one
    /// histogram bin may grow and spreads the excess over the rest, which is what stops a flat region
    /// from being amplified into noise. And every pixel's value is a bilinear blend of the four
    /// nearest tile mappings rather than its own tile's, which is what removes the block boundaries
    /// that plain tiled equalization leaves behind.
    /// </remarks>
    /// <param name="image">A grayscale image.</param>
    /// <param name="tileRows">Tiles down the picture; at least 2.</param>
    /// <param name="tileCols">Tiles across the picture; at least 2.</param>
    /// <param name="clipLimit">0–1. The bin ceiling, as a fraction of the tile's own pixel count.</param>
    /// <param name="bins">Histogram bins per tile.</param>
    /// <param name="distribution">The shape each tile is flattened towards.</param>
    /// <param name="alpha">The Rayleigh or exponential distribution's parameter.</param>
    /// <param name="range">The output range; null uses the full [0, 1].</param>
    public static ImageBuffer Clahe(
        ImageBuffer image,
        int tileRows = 8,
        int tileCols = 8,
        double clipLimit = 0.01,
        int bins = 256,
        HistogramShape distribution = HistogramShape.Uniform,
        double alpha = 0.4,
        (double Low, double High)? range = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels != 1)
        {
            throw new ArgumentException("adapthisteq expects a grayscale image; convert with rgb2gray first.");
        }

        if (tileRows < 2 || tileCols < 2)
        {
            throw new ArgumentException("adapthisteq needs at least two tiles in each direction.");
        }

        if (bins < 2)
        {
            throw new ArgumentException("adapthisteq needs at least two histogram bins.");
        }

        if (clipLimit is < 0 or > 1)
        {
            throw new ArgumentException("adapthisteq clip limit must be in [0, 1].");
        }

        if (alpha <= 0)
        {
            throw new ArgumentException("adapthisteq alpha must be positive.");
        }

        (double low, double high) = range ?? (0.0, 1.0);
        if (high <= low)
        {
            // A constant picture has nothing to equalize; hand it back untouched rather than divide
            // by a zero range.
            return image.Clone();
        }

        int height = image.Height;
        int width = image.Width;

        // Every tile must hold the same number of pixels for one clip limit to mean the same thing
        // everywhere, so the picture is grown to a whole number of tiles by mirroring its last rows
        // and columns. The extra strip is cropped off again at the end.
        int tileHeight = (height + tileRows - 1) / tileRows;
        int tileWidth = (width + tileCols - 1) / tileCols;
        int paddedHeight = tileHeight * tileRows;
        int paddedWidth = tileWidth * tileCols;

        using ImageBuffer padded = Neighborhoods.Pad(
            image, paddedHeight - height, paddedWidth - width,
            Filters.Boundary.Symmetric, 0.0, Neighborhoods.PadDirection.Post);

        double perTile = (double)tileHeight * tileWidth;
        double binScale = (bins - 1) / (high - low);

        // One mapping per tile: bins entries, each an output level in [low, high].
        var maps = new double[tileRows * tileCols][];
        var counts = new double[bins];
        for (int ty = 0; ty < tileRows; ty++)
        {
            for (int tx = 0; tx < tileCols; tx++)
            {
                Array.Clear(counts);
                for (int r = ty * tileHeight; r < (ty + 1) * tileHeight; r++)
                {
                    for (int c = tx * tileWidth; c < (tx + 1) * tileWidth; c++)
                    {
                        counts[BinOf(padded[r, c, 0], low, binScale, bins)]++;
                    }
                }

                ClipHistogram(counts, clipLimit, perTile);
                maps[(ty * tileCols) + tx] = BuildMapping(counts, perTile, low, high, distribution, alpha);
            }
        }

        var result = new ImageBuffer(height, width, 1);
        for (int r = 0; r < height; r++)
        {
            // Tile centres sit at (k + ½)·tileHeight, so a pixel's position between them is its own
            // centre measured in tiles, less a half. Clamping the two ends is what makes the border
            // half-tile use its own mapping alone instead of blending with nothing.
            double ry = ((r + 0.5) / tileHeight) - 0.5;
            int ry0 = (int)Math.Floor(ry);
            double fy = ry - ry0;
            int rowA = Math.Clamp(ry0, 0, tileRows - 1);
            int rowB = Math.Clamp(ry0 + 1, 0, tileRows - 1);

            for (int c = 0; c < width; c++)
            {
                double rx = ((c + 0.5) / tileWidth) - 0.5;
                int rx0 = (int)Math.Floor(rx);
                double fx = rx - rx0;
                int colA = Math.Clamp(rx0, 0, tileCols - 1);
                int colB = Math.Clamp(rx0 + 1, 0, tileCols - 1);

                int bin = BinOf(padded[r, c, 0], low, binScale, bins);
                double top = Lerp(maps[(rowA * tileCols) + colA][bin], maps[(rowA * tileCols) + colB][bin], fx);
                double bottom = Lerp(maps[(rowB * tileCols) + colA][bin], maps[(rowB * tileCols) + colB][bin], fx);
                result[r, c, 0] = Lerp(top, bottom, fy);
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static int BinOf(double value, double low, double binScale, int bins) =>
        Math.Clamp((int)Math.Floor(((value - low) * binScale) + 0.5), 0, bins - 1);

    /// <summary>
    /// Caps every bin at the clip limit and hands the excess back to the histogram, which is the
    /// "contrast-limited" half of CLAHE.
    /// </summary>
    /// <remarks>
    /// The excess goes back in two passes. The first raises every bin by the average share, stopping
    /// at the ceiling; the second walks the histogram at a stride and adds one count at a time until
    /// the remainder is gone. Two passes rather than one because after the flat share some bins have
    /// hit the ceiling and cannot take their portion, and a histogram that has quietly lost counts no
    /// longer equalizes to the right range.
    /// </remarks>
    private static void ClipHistogram(double[] counts, double clipLimit, double pixelsInTile)
    {
        int bins = counts.Length;
        if (clipLimit <= 0)
        {
            return;
        }

        // MATLAB's floor: below one count per bin the limit would clip everything flat.
        double limit = Math.Max(1.0, clipLimit * pixelsInTile);
        double excess = 0;
        foreach (double count in counts)
        {
            if (count > limit)
            {
                excess += count - limit;
            }
        }

        if (excess <= 0)
        {
            return;
        }

        double share = Math.Floor(excess / bins);
        double ceiling = limit - share;
        for (int i = 0; i < bins; i++)
        {
            if (counts[i] > limit)
            {
                counts[i] = limit;
            }
            else if (counts[i] > ceiling)
            {
                excess -= limit - counts[i];
                counts[i] = limit;
            }
            else
            {
                excess -= share;
                counts[i] += share;
            }
        }

        int start = 0;
        while (excess > 0 && start < bins)
        {
            int stride = Math.Max(1, (int)Math.Floor(bins / excess));
            for (int i = start; i < bins && excess > 0; i += stride)
            {
                if (counts[i] < limit)
                {
                    counts[i]++;
                    excess--;
                }
            }

            start++;
        }
    }

    /// <summary>
    /// A tile's mapping: the cumulative histogram, reshaped by the requested output distribution and
    /// scaled onto <c>[low, high]</c>.
    /// </summary>
    /// <remarks>
    /// Each shape is its own inverse CDF applied to the tile's cumulative fraction, normalized so a
    /// cumulative fraction of one lands exactly on <paramref name="high"/>. Without that
    /// normalization the Rayleigh and exponential shapes would stop short of white by an amount that
    /// depends on alpha, and the tiles would not agree with each other about what white is.
    /// </remarks>
    private static double[] BuildMapping(
        double[] counts, double pixelsInTile, double low, double high, HistogramShape shape, double alpha)
    {
        int bins = counts.Length;
        var map = new double[bins];
        double span = high - low;
        double running = 0;

        double hconst = 2 * alpha * alpha;
        double rayleighMax = 1.0 - Math.Exp(-1.0 / hconst);
        double exponentialMax = 1.0 - Math.Exp(-alpha);

        for (int i = 0; i < bins; i++)
        {
            running += counts[i];
            double fraction = Math.Clamp(running / pixelsInTile, 0, 1);
            double shaped = shape switch
            {
                HistogramShape.Rayleigh => Math.Sqrt(-hconst * Math.Log(1.0 - Math.Min(fraction * rayleighMax, 1.0 - 1e-15))),
                HistogramShape.Exponential => -Math.Log(1.0 - Math.Min(fraction * exponentialMax, 1.0 - 1e-15)) / alpha,
                _ => fraction,
            };

            map[i] = Math.Min(low + (Math.Clamp(shaped, 0, 1) * span), high);
        }

        return map;
    }

    // ---------------------------------------------------------------------------------------
    // Histogram matching (imhistmatch)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reshapes <paramref name="image"/> so its histogram resembles <paramref name="reference"/>'s
    /// (MATLAB <c>imhistmatch</c>), returning the target histogram alongside.
    /// </summary>
    /// <param name="image">The picture to adjust.</param>
    /// <param name="reference">The picture whose distribution is wanted.</param>
    /// <param name="bins">How many levels the target histogram is measured at.</param>
    /// <param name="smooth">
    /// True for MATLAB's <c>'polynomial'</c> method: a monotone cubic through the same mapping,
    /// which trades exactness for a curve without steps in it.
    /// </param>
    public static (ImageBuffer Result, double[] Histogram) MatchHistogram(
        ImageBuffer image, ImageBuffer reference, int bins = 64, bool smooth = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reference);
        if (bins < 2)
        {
            throw new ArgumentException("imhistmatch needs at least two bins.", nameof(bins));
        }

        if (reference.Channels != 1 && reference.Channels != image.Channels)
        {
            throw new ArgumentException(
                "imhistmatch needs the reference to have the same number of channels as the image, or one.");
        }

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        double[]? firstHistogram = null;
        for (int ch = 0; ch < image.Channels; ch++)
        {
            int refChannel = reference.Channels == 1 ? 0 : ch;
            double[] target = ChannelHistogram(reference, refChannel, bins);
            firstHistogram ??= target;

            double[] transform = Histograms.MatchingTransform(
                ChannelHistogram(image, ch, 256), target, (double)image.Height * image.Width);
            if (smooth)
            {
                transform = MonotoneSmooth(transform);
            }

            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    result[r, c, ch] = transform[
                        Math.Clamp((int)Math.Round(Math.Clamp(image[r, c, ch], 0, 1) * 255), 0, 255)];
                }
            }
        }

        GC.KeepAlive(image);
        GC.KeepAlive(reference);
        return (result, firstHistogram ?? []);
    }

    private static double[] ChannelHistogram(ImageBuffer image, int channel, int bins)
    {
        var counts = new double[bins];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                int bin = (int)(Math.Clamp(image[r, c, channel], 0, 1) * bins);
                counts[Math.Min(bin, bins - 1)]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// Smooths a lookup table with a monotone cubic (Fritsch–Carlson), which is what turns the
    /// staircase an exact histogram match produces into a continuous curve.
    /// </summary>
    /// <remarks>
    /// Monotone is the whole point: an ordinary spline through the same points overshoots at every
    /// step and would map a lighter input to a darker output somewhere along the curve, which reads
    /// as a posterized band rather than a smoother one.
    /// </remarks>
    private static double[] MonotoneSmooth(double[] table)
    {
        int n = table.Length;
        if (n < 3)
        {
            return table;
        }

        // Knots every eighth entry: enough to follow the shape of the mapping, sparse enough that the
        // steps between them are what gets smoothed away.
        const int Stride = 8;
        var xs = new List<double>();
        var ys = new List<double>();
        for (int i = 0; i < n; i += Stride)
        {
            xs.Add(i);
            ys.Add(table[i]);
        }

        if (xs[^1] != n - 1)
        {
            xs.Add(n - 1);
            ys.Add(table[n - 1]);
        }

        int k = xs.Count;
        var slopes = new double[k - 1];
        for (int i = 0; i < k - 1; i++)
        {
            slopes[i] = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
        }

        var tangents = new double[k];
        tangents[0] = slopes[0];
        tangents[k - 1] = slopes[k - 2];
        for (int i = 1; i < k - 1; i++)
        {
            tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2;
        }

        for (int i = 0; i < k - 1; i++)
        {
            if (slopes[i] == 0)
            {
                tangents[i] = 0;
                tangents[i + 1] = 0;
                continue;
            }

            double a = tangents[i] / slopes[i];
            double b = tangents[i + 1] / slopes[i];
            double magnitude = (a * a) + (b * b);
            if (magnitude > 9)
            {
                double scale = 3.0 / Math.Sqrt(magnitude);
                tangents[i] = scale * a * slopes[i];
                tangents[i + 1] = scale * b * slopes[i];
            }
        }

        var smoothed = new double[n];
        int segment = 0;
        for (int i = 0; i < n; i++)
        {
            while (segment < k - 2 && i > xs[segment + 1])
            {
                segment++;
            }

            double h = xs[segment + 1] - xs[segment];
            double t = (i - xs[segment]) / h;
            double t2 = t * t;
            double t3 = t2 * t;
            smoothed[i] =
                (((2 * t3) - (3 * t2) + 1) * ys[segment]) +
                ((t3 - (2 * t2) + t) * h * tangents[segment]) +
                (((-2 * t3) + (3 * t2)) * ys[segment + 1]) +
                ((t3 - t2) * h * tangents[segment + 1]);
        }

        return smoothed;
    }

    // ---------------------------------------------------------------------------------------
    // Flat-field correction (imflatfield)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Removes a smooth illumination gradient by dividing the picture by a heavily blurred copy of
    /// itself and restoring the original mean (MATLAB <c>imflatfield</c>).
    /// </summary>
    /// <remarks>
    /// The blur has to be wide enough that it carries the lighting and none of the subject; that is
    /// what <paramref name="sigma"/> sets, and it is why a sigma chosen too small erases the picture
    /// instead of its shading. Colour is corrected on lightness alone so the hue does not shift with
    /// the illumination.
    /// </remarks>
    /// <param name="image">The picture to correct.</param>
    /// <param name="sigma">The Gaussian's standard deviation, in pixels.</param>
    /// <param name="filterSize">The kernel size; zero picks <c>2·ceil(2σ)+1</c>.</param>
    /// <param name="mask">Optional: only pixels inside the mask are measured and corrected.</param>
    public static ImageBuffer FlatField(
        ImageBuffer image, double sigma, int filterSize = 0, ImageBuffer? mask = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (sigma <= 0)
        {
            throw new ArgumentException("imflatfield sigma must be positive.", nameof(sigma));
        }

        if (mask is not null && (mask.Height != image.Height || mask.Width != image.Width))
        {
            throw new ArgumentException("imflatfield mask must be the same size as the image.", nameof(mask));
        }

        int size = filterSize > 0 ? filterSize : (2 * (int)Math.Ceiling(2 * sigma)) + 1;
        using ImageBuffer working = image.Channels == 3 ? LightnessOf(image) : image.Clone();
        using ImageBuffer blurred = Filters.GaussianBlur(
            working, sigma, sigma, size, size, Filters.Boundary.Symmetric);

        double sum = 0;
        int counted = 0;
        for (int r = 0; r < working.Height; r++)
        {
            for (int c = 0; c < working.Width; c++)
            {
                if (mask is not null && mask[r, c, 0] == 0)
                {
                    continue;
                }

                sum += working[r, c, 0];
                counted++;
            }
        }

        double mean = counted > 0 ? sum / counted : 0;
        var corrected = new ImageBuffer(working.Height, working.Width, 1);
        for (int r = 0; r < working.Height; r++)
        {
            for (int c = 0; c < working.Width; c++)
            {
                if (mask is not null && mask[r, c, 0] == 0)
                {
                    corrected[r, c, 0] = working[r, c, 0];
                    continue;
                }

                double denominator = blurred[r, c, 0];
                corrected[r, c, 0] = denominator <= double.Epsilon
                    ? working[r, c, 0]
                    : Math.Clamp(working[r, c, 0] * mean / denominator, 0, 1);
            }
        }

        if (image.Channels != 3)
        {
            return corrected;
        }

        using (corrected)
        {
            return WithLightness(image, corrected);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Decorrelation stretch (decorrstretch)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Removes the correlation between an image's bands and rescales each one, which is what pulls
    /// colour out of a picture whose channels nearly agree (MATLAB <c>decorrstretch</c>).
    /// </summary>
    /// <remarks>
    /// The transform is the symmetric whitening <c>V·Λ^(−½)·Vᵀ</c> of the band covariance, scaled to
    /// the target spreads. Symmetric rather than any other square root because it is the whitening
    /// closest to doing nothing, which is what keeps the result recognizable as the same scene
    /// instead of a differently-coloured one.
    /// </remarks>
    /// <param name="image">A multi-band image.</param>
    /// <param name="mode">Whether to whiten the covariance or the correlation.</param>
    /// <param name="targetMean">Per-band output means; null keeps the input's.</param>
    /// <param name="targetSigma">Per-band output spreads; null keeps the input's.</param>
    /// <param name="tolerance">
    /// Optional linear stretch afterwards, as <c>stretchlim</c> fractions; null skips it.
    /// </param>
    /// <param name="sample">
    /// Which pixels the statistics are measured from; null uses all of them. A subset is what lets a
    /// scene's own subject decide the stretch rather than a border of black sky.
    /// </param>
    public static ImageBuffer DecorrelationStretch(
        ImageBuffer image,
        StretchMode mode = StretchMode.Correlation,
        double[]? targetMean = null,
        double[]? targetSigma = null,
        (double Low, double High)? tolerance = null,
        IReadOnlyList<(int Row, int Col)>? sample = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        int bands = image.Channels;
        if (sample is { Count: 0 })
        {
            throw new ArgumentException("decorrstretch sample is empty.", nameof(sample));
        }

        int pixels = sample?.Count ?? (image.Height * image.Width);
        if (pixels < 2)
        {
            return image.Clone();
        }

        (int Row, int Col) At(int index) =>
            sample is not null ? sample[index] : (index / image.Width, index % image.Width);

        var means = new double[bands];
        for (int i = 0; i < pixels; i++)
        {
            (int r, int c) = At(i);
            for (int ch = 0; ch < bands; ch++)
            {
                means[ch] += image[r, c, ch];
            }
        }

        for (int ch = 0; ch < bands; ch++)
        {
            means[ch] /= pixels;
        }

        var covariance = new double[bands, bands];
        for (int p = 0; p < pixels; p++)
        {
            (int r, int c) = At(p);
            for (int i = 0; i < bands; i++)
            {
                double di = image[r, c, i] - means[i];
                for (int j = i; j < bands; j++)
                {
                    covariance[i, j] += di * (image[r, c, j] - means[j]);
                }
            }
        }

        for (int i = 0; i < bands; i++)
        {
            for (int j = i; j < bands; j++)
            {
                covariance[i, j] /= pixels - 1;
                covariance[j, i] = covariance[i, j];
            }
        }

        var deviations = new double[bands];
        for (int i = 0; i < bands; i++)
        {
            deviations[i] = Math.Sqrt(Math.Max(covariance[i, i], 0));
        }

        double[] sigma = targetSigma ?? deviations;
        double[] centre = targetMean ?? means;
        if (sigma.Length != bands || centre.Length != bands)
        {
            throw new ArgumentException("decorrstretch targets need one value per band.");
        }

        // In correlation mode the bands are rescaled to unit variance before whitening and scaled
        // back afterwards, so a band that happens to be dim does not dominate the rotation.
        var work = (double[,])covariance.Clone();
        if (mode == StretchMode.Correlation)
        {
            for (int i = 0; i < bands; i++)
            {
                for (int j = 0; j < bands; j++)
                {
                    double scale = deviations[i] * deviations[j];
                    work[i, j] = scale > 0 ? covariance[i, j] / scale : (i == j ? 1 : 0);
                }
            }
        }

        (double[] values, double[,] vectors) = SymmetricEigen(work);
        var transform = new double[bands, bands];
        for (int i = 0; i < bands; i++)
        {
            for (int j = 0; j < bands; j++)
            {
                double sum = 0;
                for (int k = 0; k < bands; k++)
                {
                    double lambda = values[k];
                    // A degenerate direction carries no information; leaving it at zero drops it
                    // rather than amplifying rounding noise by one over the square root of nothing.
                    if (lambda > 1e-12)
                    {
                        sum += vectors[i, k] * vectors[j, k] / Math.Sqrt(lambda);
                    }
                }

                transform[i, j] = sum;
            }
        }

        if (mode == StretchMode.Correlation)
        {
            for (int i = 0; i < bands; i++)
            {
                for (int j = 0; j < bands; j++)
                {
                    double scale = deviations[i];
                    transform[i, j] = scale > 0 ? transform[i, j] / scale : 0;
                }
            }
        }

        for (int i = 0; i < bands; i++)
        {
            for (int j = 0; j < bands; j++)
            {
                transform[i, j] *= sigma[j];
            }
        }

        var result = new ImageBuffer(image.Height, image.Width, bands);
        var offsets = new double[bands];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int i = 0; i < bands; i++)
                {
                    offsets[i] = image[r, c, i] - means[i];
                }

                for (int j = 0; j < bands; j++)
                {
                    double sum = centre[j];
                    for (int i = 0; i < bands; i++)
                    {
                        sum += offsets[i] * transform[i, j];
                    }

                    result[r, c, j] = Math.Clamp(sum, 0, 1);
                }
            }
        }

        GC.KeepAlive(image);
        if (tolerance is not { } tol)
        {
            return result;
        }

        using (result)
        {
            return StretchBands(result, tol.Low, tol.High);
        }
    }

    /// <summary>Rescales each band onto [0, 1] between its own <c>stretchlim</c> percentiles.</summary>
    private static ImageBuffer StretchBands(ImageBuffer image, double low, double high)
    {
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            var samples = new double[image.Height * image.Width];
            int n = 0;
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    samples[n++] = image[r, c, ch];
                }
            }

            Array.Sort(samples);
            double lowValue = samples[Math.Clamp((int)(low * (n - 1)), 0, n - 1)];
            double highValue = samples[Math.Clamp((int)(high * (n - 1)), 0, n - 1)];
            double span = highValue - lowValue;
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    result[r, c, ch] = span > 0
                        ? Math.Clamp((image[r, c, ch] - lowValue) / span, 0, 1)
                        : image[r, c, ch];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Eigenvalues and eigenvectors of a small symmetric matrix by the cyclic Jacobi rotation.
    /// </summary>
    /// <remarks>
    /// Jacobi rather than anything from the linear-algebra library because
    /// <c>JGraph.Imaging</c> depends only on <c>JGraph.Numerics</c>, and a band covariance is at most
    /// a handful of rows: the rotation converges in a few sweeps and is exact for the symmetric case,
    /// which is the only case here.
    /// </remarks>
    private static (double[] Values, double[,] Vectors) SymmetricEigen(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        var vectors = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            vectors[i, i] = 1;
        }

        for (int sweep = 0; sweep < 60; sweep++)
        {
            double offDiagonal = 0;
            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    offDiagonal += a[p, q] * a[p, q];
                }
            }

            if (offDiagonal < 1e-30)
            {
                break;
            }

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300)
                    {
                        continue;
                    }

                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
                    if (theta == 0)
                    {
                        t = 1;
                    }

                    double cos = 1 / Math.Sqrt((t * t) + 1);
                    double sin = t * cos;
                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k, p];
                        double akq = a[k, q];
                        a[k, p] = (cos * akp) - (sin * akq);
                        a[k, q] = (sin * akp) + (cos * akq);
                    }

                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[p, k];
                        double aqk = a[q, k];
                        a[p, k] = (cos * apk) - (sin * aqk);
                        a[q, k] = (sin * apk) + (cos * aqk);
                    }

                    for (int k = 0; k < n; k++)
                    {
                        double vkp = vectors[k, p];
                        double vkq = vectors[k, q];
                        vectors[k, p] = (cos * vkp) - (sin * vkq);
                        vectors[k, q] = (sin * vkp) + (cos * vkq);
                    }
                }
            }
        }

        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = a[i, i];
        }

        return (values, vectors);
    }

    // ---------------------------------------------------------------------------------------
    // Unsharp masking (imsharpen)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sharpens by adding back what a blur removed (MATLAB <c>imsharpen</c>).
    /// </summary>
    /// <param name="image">The picture to sharpen.</param>
    /// <param name="radius">The blur's standard deviation — how wide an edge counts as an edge.</param>
    /// <param name="amount">How much of the difference to add back.</param>
    /// <param name="threshold">
    /// 0–1. Differences below this fraction of the largest one are left alone, so flat regions are
    /// not sharpened into noise.
    /// </param>
    public static ImageBuffer Sharpen(
        ImageBuffer image, double radius = 1.0, double amount = 0.8, double threshold = 0.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (radius <= 0)
        {
            throw new ArgumentException("imsharpen radius must be positive.", nameof(radius));
        }

        if (threshold is < 0 or > 1)
        {
            throw new ArgumentException("imsharpen threshold must be in [0, 1].", nameof(threshold));
        }

        // Colour is sharpened on lightness alone: sharpening the three channels apart from each other
        // moves them by different amounts at an edge, which shows up as coloured fringing.
        using ImageBuffer working = image.Channels == 3 ? LightnessOf(image) : image.Clone();
        int size = (2 * (int)Math.Ceiling(2 * radius)) + 1;
        using ImageBuffer blurred = Filters.GaussianBlur(
            working, radius, radius, size, size, Filters.Boundary.Replicate);

        int height = working.Height;
        int width = working.Width;
        var mask = new double[height, width];
        double largest = 0;
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double difference = working[r, c, 0] - blurred[r, c, 0];
                mask[r, c] = difference;
                largest = Math.Max(largest, Math.Abs(difference));
            }
        }

        double floor = threshold * largest;
        var sharpened = new ImageBuffer(height, width, 1);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double difference = Math.Abs(mask[r, c]) >= floor ? mask[r, c] : 0;
                sharpened[r, c, 0] = Math.Clamp(working[r, c, 0] + (amount * difference), 0, 1);
            }
        }

        if (image.Channels != 3)
        {
            return sharpened;
        }

        using (sharpened)
        {
            return WithLightness(image, sharpened);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Haze (imreducehaze, imlocalbrighten)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Removes atmospheric haze using the dark-channel prior (MATLAB <c>imreducehaze</c>), returning
    /// the transmission map alongside the cleared picture.
    /// </summary>
    /// <remarks>
    /// The prior is an observation about photographs of real scenes: in almost every small patch of a
    /// haze-free outdoor picture, at least one colour channel is nearly black. Where that is not true,
    /// the brightness is the haze, and how much of it there is at each pixel is the transmission map.
    /// </remarks>
    /// <param name="image">The hazy picture.</param>
    /// <param name="amount">0–1. How much of the estimated haze to take away.</param>
    /// <param name="method">Whether the dark channel is taken over a window or per pixel.</param>
    /// <param name="atmosphericLight">The haze colour; null estimates it from the picture.</param>
    /// <param name="contrast">What to do with the contrast afterwards.</param>
    /// <param name="boostAmount">0–1. How much further to push contrast under <see cref="HazeContrast.Boost"/>.</param>
    public static (ImageBuffer Result, ImageBuffer Transmission) ReduceHaze(
        ImageBuffer image,
        double amount = 0.9,
        HazeMethod method = HazeMethod.SimpleDarkChannel,
        double[]? atmosphericLight = null,
        HazeContrast contrast = HazeContrast.Global,
        double boostAmount = 0.1)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (amount is < 0 or > 1)
        {
            throw new ArgumentException("imreducehaze amount must be in [0, 1].", nameof(amount));
        }

        int height = image.Height;
        int width = image.Width;
        int channels = image.Channels;
        if (atmosphericLight is not null && atmosphericLight.Length != channels)
        {
            throw new ArgumentException(
                "imreducehaze atmospheric light needs one value per channel.", nameof(atmosphericLight));
        }

        // A window wide enough to hold a whole object, so the darkest sample in it belongs to the
        // object and not to a stray dark pixel — the usual choice is a fifteenth of the picture.
        int patch = method == HazeMethod.SimpleDarkChannel
            ? Math.Max(3, (2 * (Math.Min(height, width) / 30)) + 1)
            : 1;

        var dark = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double smallest = double.PositiveInfinity;
                for (int ch = 0; ch < channels; ch++)
                {
                    smallest = Math.Min(smallest, image[r, c, ch]);
                }

                dark[r, c] = smallest;
            }
        }

        if (patch > 1)
        {
            dark = MinimumFilter(dark, patch);
        }

        double[] light = atmosphericLight ?? EstimateAtmosphere(image, dark);
        for (int ch = 0; ch < channels; ch++)
        {
            // A channel estimated at zero would divide the transmission by nothing.
            light[ch] = Math.Max(light[ch], 1e-6);
        }

        var transmission = new ImageBuffer(height, width, 1);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double normalized = double.PositiveInfinity;
                for (int ch = 0; ch < channels; ch++)
                {
                    normalized = Math.Min(normalized, image[r, c, ch] / light[ch]);
                }

                transmission[r, c, 0] = Math.Clamp(1.0 - (amount * normalized), 0.0, 1.0);
            }
        }

        if (method == HazeMethod.ApproximateDarkChannel)
        {
            // A per-pixel dark channel follows every edge, including the ones that are texture rather
            // than depth. The guided filter puts the map back onto the picture's own edges.
            using ImageBuffer guide = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
            using ImageBuffer refined = Denoising.GuidedFilter(transmission, guide, 15, 15, 1e-3);
            transmission.Dispose();
            transmission = refined.Clone();
        }

        var result = new ImageBuffer(height, width, channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                // Below about a tenth of the light getting through, the division amplifies whatever
                // noise is left far more than it recovers scene.
                double t = Math.Max(transmission[r, c, 0], 0.1);
                for (int ch = 0; ch < channels; ch++)
                {
                    result[r, c, ch] = Math.Clamp(
                        ((image[r, c, ch] - light[ch]) / t) + light[ch], 0, 1);
                }
            }
        }

        GC.KeepAlive(image);
        if (contrast == HazeContrast.None)
        {
            return (result, transmission);
        }

        using (result)
        {
            ImageBuffer stretched = StretchBands(result, 0.01, 0.99);
            if (contrast != HazeContrast.Boost)
            {
                return (stretched, transmission);
            }

            using (stretched)
            {
                return (Boost(stretched, Math.Clamp(boostAmount, 0, 1)), transmission);
            }
        }
    }

    /// <summary>
    /// Brightens the dark parts of a picture (MATLAB <c>imlocalbrighten</c>), returning the
    /// transmission map that decided where.
    /// </summary>
    /// <remarks>
    /// This is haze removal run on the negative. A dark region in a photograph behaves, once
    /// inverted, exactly like a hazy one — low contrast riding on a bright floor — so the same
    /// dark-channel estimate finds it, and inverting the result puts the brightening back where the
    /// shadows were.
    /// </remarks>
    /// <param name="image">The picture to brighten.</param>
    /// <param name="amount">0–1. How much brightening to apply.</param>
    /// <param name="alphaBlend">
    /// True to blend the brightened picture back over the original using the transmission map, which
    /// leaves the already-bright regions as they were.
    /// </param>
    public static (ImageBuffer Result, ImageBuffer Transmission) LocalBrighten(
        ImageBuffer image, double amount = 1.0, bool alphaBlend = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (amount is < 0 or > 1)
        {
            throw new ArgumentException("imlocalbrighten amount must be in [0, 1].", nameof(amount));
        }

        using ImageBuffer inverted = PointOps.Complement(image);
        (ImageBuffer dehazed, ImageBuffer transmission) = ReduceHaze(
            inverted, amount, HazeMethod.SimpleDarkChannel, null, HazeContrast.None);

        ImageBuffer brightened;
        using (dehazed)
        {
            brightened = PointOps.Complement(dehazed);
        }

        if (!alphaBlend)
        {
            return (brightened, transmission);
        }

        using (brightened)
        {
            var blended = new ImageBuffer(image.Height, image.Width, image.Channels);
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    double t = transmission[r, c, 0];
                    for (int ch = 0; ch < image.Channels; ch++)
                    {
                        blended[r, c, ch] = (image[r, c, ch] * t) + (brightened[r, c, ch] * (1 - t));
                    }
                }
            }

            GC.KeepAlive(image);
            return (blended, transmission);
        }
    }

    /// <summary>The haze colour: the brightest pixel among the haziest tenth of a percent.</summary>
    private static double[] EstimateAtmosphere(ImageBuffer image, double[,] dark)
    {
        int height = image.Height;
        int width = image.Width;
        int pixels = height * width;
        int take = Math.Max(1, pixels / 1000);

        var order = new int[pixels];
        var keys = new double[pixels];
        for (int i = 0; i < pixels; i++)
        {
            order[i] = i;
            keys[i] = dark[i / width, i % width];
        }

        Array.Sort(keys, order);

        var light = new double[image.Channels];
        double brightest = -1;
        for (int i = pixels - take; i < pixels; i++)
        {
            int r = order[i] / width;
            int c = order[i] % width;
            double intensity = 0;
            for (int ch = 0; ch < image.Channels; ch++)
            {
                intensity += image[r, c, ch];
            }

            if (intensity <= brightest)
            {
                continue;
            }

            brightest = intensity;
            for (int ch = 0; ch < image.Channels; ch++)
            {
                light[ch] = image[r, c, ch];
            }
        }

        return light;
    }

    /// <summary>The smallest value in each pixel's square window.</summary>
    private static double[,] MinimumFilter(double[,] values, int size)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);
        int radius = size / 2;

        // Separable: the minimum over a square is the minimum of the row minima.
        var rows = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double smallest = double.PositiveInfinity;
                for (int k = -radius; k <= radius; k++)
                {
                    smallest = Math.Min(smallest, values[r, Math.Clamp(c + k, 0, width - 1)]);
                }

                rows[r, c] = smallest;
            }
        }

        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double smallest = double.PositiveInfinity;
                for (int k = -radius; k <= radius; k++)
                {
                    smallest = Math.Min(smallest, rows[Math.Clamp(r + k, 0, height - 1), c]);
                }

                result[r, c] = smallest;
            }
        }

        return result;
    }

    /// <summary>Pushes local contrast further by adding back a share of what a wide blur removed.</summary>
    private static ImageBuffer Boost(ImageBuffer image, double amount)
    {
        if (amount <= 0)
        {
            return image.Clone();
        }

        double sigma = Math.Max(1.0, Math.Min(image.Height, image.Width) / 32.0);
        int size = (2 * (int)Math.Ceiling(2 * sigma)) + 1;
        using ImageBuffer blurred = Filters.GaussianBlur(
            image, sigma, sigma, size, size, Filters.Boundary.Replicate);

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        Span<double> dst = result.Pixels;
        ReadOnlySpan<double> src = image.Pixels;
        ReadOnlySpan<double> low = blurred.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = Math.Clamp(src[i] + (amount * (src[i] - low[i]) * 4.0), 0, 1);
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Lightness helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>The L* channel of an RGB image, scaled onto [0, 1].</summary>
    internal static ImageBuffer LightnessOf(ImageBuffer image)
    {
        int pixels = image.Height * image.Width;
        var rgb = new double[pixels, 3];
        int n = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++, n++)
            {
                rgb[n, 0] = image[r, c, 0];
                rgb[n, 1] = image[r, c, 1];
                rgb[n, 2] = image[r, c, 2];
            }
        }

        double[,] lab = ColorSpaces.RgbToLab(rgb, RgbColorSpace.Srgb, ColorSpaces.WhitePoint("d65"));
        var lightness = new ImageBuffer(image.Height, image.Width, 1);
        n = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++, n++)
            {
                lightness[r, c, 0] = Math.Clamp(lab[n, 0] / 100.0, 0, 1);
            }
        }

        return lightness;
    }

    /// <summary>An RGB image with its lightness replaced and its colour left alone.</summary>
    internal static ImageBuffer WithLightness(ImageBuffer image, ImageBuffer lightness)
    {
        int pixels = image.Height * image.Width;
        var rgb = new double[pixels, 3];
        int n = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++, n++)
            {
                rgb[n, 0] = image[r, c, 0];
                rgb[n, 1] = image[r, c, 1];
                rgb[n, 2] = image[r, c, 2];
            }
        }

        double[] white = ColorSpaces.WhitePoint("d65");
        double[,] lab = ColorSpaces.RgbToLab(rgb, RgbColorSpace.Srgb, white);
        n = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++, n++)
            {
                lab[n, 0] = lightness[r, c, 0] * 100.0;
            }
        }

        double[,] back = ColorSpaces.LabToRgb(lab, RgbColorSpace.Srgb, white);
        var result = new ImageBuffer(image.Height, image.Width, 3);
        n = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++, n++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    result[r, c, ch] = Math.Clamp(back[n, ch], 0, 1);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }
}
