namespace JGraph.Imaging;

/// <summary>
/// Edge-preserving smoothing: the bilateral and guided filters, anisotropic diffusion, and
/// non-local means.
/// </summary>
/// <remarks>
/// A plain blur cannot tell noise from an edge, because at the scale of a few pixels they look the
/// same. Every filter here answers that with a second measurement taken alongside distance —
/// intensity difference for the bilateral filter, a local linear fit for the guided filter, the
/// gradient for diffusion, patch similarity for non-local means — and lets that measurement decide
/// how much of each neighbour is allowed through.
/// </remarks>
public static class Denoising
{
    /// <summary>Which conduction function anisotropic diffusion uses.</summary>
    public enum Conduction
    {
        /// <summary>Favours high-contrast edges over wide ones (Perona–Malik's first function).</summary>
        Exponential,

        /// <summary>Favours wide regions over high-contrast ones (their second).</summary>
        Quadratic,
    }

    // ---------------------------------------------------------------------------------------
    // Bilateral filter (imbilatfilt)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The bilateral filter (MATLAB <c>imbilatfilt</c>): a Gaussian blur in which each neighbour is
    /// also weighted by how close its colour is to the centre pixel's.
    /// </summary>
    /// <remarks>
    /// Because a neighbour on the far side of an edge contributes almost nothing, the filter smooths
    /// within a region without carrying anything across its boundary. Colour is measured in L*a*b*,
    /// where equal distances look equally different, so the filter does not preserve an edge in one
    /// hue while smoothing an equally visible one in another.
    /// </remarks>
    /// <param name="image">The picture to filter.</param>
    /// <param name="degreeOfSmoothing">
    /// The intensity term's variance. Larger lets more dissimilar neighbours in, so the filter
    /// approaches a plain Gaussian blur.
    /// </param>
    /// <param name="spatialSigma">The distance term's standard deviation, in pixels.</param>
    /// <param name="neighborhoodSize">The window's side; zero picks <c>2·ceil(2σ)+1</c>.</param>
    /// <param name="boundary">How the window is filled at the picture's edge.</param>
    /// <param name="padValue">The constant for <see cref="Filters.Boundary.Zero"/>.</param>
    public static ImageBuffer Bilateral(
        ImageBuffer image,
        double degreeOfSmoothing = 0.01,
        double spatialSigma = 1.0,
        int neighborhoodSize = 0,
        Filters.Boundary boundary = Filters.Boundary.Symmetric,
        double padValue = 0.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (degreeOfSmoothing <= 0)
        {
            throw new ArgumentException("imbilatfilt degree of smoothing must be positive.", nameof(degreeOfSmoothing));
        }

        if (spatialSigma <= 0)
        {
            throw new ArgumentException("imbilatfilt spatial sigma must be positive.", nameof(spatialSigma));
        }

        int size = neighborhoodSize > 0 ? neighborhoodSize : (2 * (int)Math.Ceiling(2 * spatialSigma)) + 1;
        int radius = size / 2;
        int height = image.Height;
        int width = image.Width;
        int channels = image.Channels;

        // The colour distance is measured in L*a*b* but divided by 100, the span of L*, so that one
        // unit of it means what one unit of intensity means for a grayscale picture. That is what
        // lets a single degree-of-smoothing default cover both.
        using ImageBuffer measured = channels == 3 ? LabOf(image) : image.Clone();

        var spatial = new double[(2 * radius) + 1, (2 * radius) + 1];
        double spatialDenominator = 2 * spatialSigma * spatialSigma;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                spatial[dy + radius, dx + radius] = Math.Exp(-((dy * dy) + (dx * dx)) / spatialDenominator);
            }
        }

        double rangeDenominator = 2 * degreeOfSmoothing;
        var result = new ImageBuffer(height, width, channels);
        var accumulator = new double[channels];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double weightSum = 0;
                Array.Clear(accumulator);
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        double distance = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            double d = Filters.Sample(measured, r + dy, c + dx, ch, boundary, padValue)
                                - measured[r, c, ch];
                            distance += d * d;
                        }

                        double weight = spatial[dy + radius, dx + radius] * Math.Exp(-distance / rangeDenominator);
                        weightSum += weight;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            accumulator[ch] += weight * Filters.Sample(image, r + dy, c + dx, ch, boundary, padValue);
                        }
                    }
                }

                for (int ch = 0; ch < channels; ch++)
                {
                    result[r, c, ch] = weightSum > 0 ? accumulator[ch] / weightSum : image[r, c, ch];
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>An RGB image in L*a*b*, every channel divided by 100 so distances read as intensities.</summary>
    private static ImageBuffer LabOf(ImageBuffer image)
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
        var result = new ImageBuffer(image.Height, image.Width, 3);
        n = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++, n++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    result[r, c, ch] = lab[n, ch] / 100.0;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Guided filter (imguidedfilter)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The guided filter (MATLAB <c>imguidedfilter</c>): smooths one picture while borrowing another
    /// picture's edges.
    /// </summary>
    /// <remarks>
    /// Within each window the output is fitted as a straight line in the guide, which is exactly why
    /// it keeps edges: a line through a step is a step. Where the guide is flat the fitted slope
    /// collapses towards zero and the window averages, and the changeover between the two is set by
    /// <paramref name="degreeOfSmoothing"/> — a variance the guide's own local variance is compared
    /// against. Filtering a picture with itself as the guide is the ordinary denoising use.
    /// </remarks>
    /// <param name="image">The picture to filter.</param>
    /// <param name="guide">The picture whose edges to keep; must be the same size.</param>
    /// <param name="neighborhoodRows">Window height.</param>
    /// <param name="neighborhoodCols">Window width.</param>
    /// <param name="degreeOfSmoothing">The guide variance below which a window is treated as flat.</param>
    public static ImageBuffer GuidedFilter(
        ImageBuffer image,
        ImageBuffer guide,
        int neighborhoodRows = 5,
        int neighborhoodCols = 5,
        double degreeOfSmoothing = 0.01)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(guide);
        if (guide.Height != image.Height || guide.Width != image.Width)
        {
            throw new ArgumentException("imguidedfilter needs the guide to be the same size as the image.");
        }

        if (guide.Channels != 1 && guide.Channels != image.Channels)
        {
            throw new ArgumentException(
                "imguidedfilter needs the guide to have one channel or as many as the image.");
        }

        if (neighborhoodRows < 1 || neighborhoodCols < 1)
        {
            throw new ArgumentException("imguidedfilter neighbourhood must be at least one pixel.");
        }

        int height = image.Height;
        int width = image.Width;
        var result = new ImageBuffer(height, width, image.Channels);

        for (int ch = 0; ch < image.Channels; ch++)
        {
            int guideChannel = guide.Channels == 1 ? 0 : ch;
            var target = new double[height, width];
            var source = new double[height, width];
            var product = new double[height, width];
            var square = new double[height, width];
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    double p = image[r, c, ch];
                    double g = guide[r, c, guideChannel];
                    target[r, c] = p;
                    source[r, c] = g;
                    product[r, c] = p * g;
                    square[r, c] = g * g;
                }
            }

            double[,] meanP = BoxMean(target, neighborhoodRows, neighborhoodCols);
            double[,] meanG = BoxMean(source, neighborhoodRows, neighborhoodCols);
            double[,] meanPg = BoxMean(product, neighborhoodRows, neighborhoodCols);
            double[,] meanGg = BoxMean(square, neighborhoodRows, neighborhoodCols);

            var slope = new double[height, width];
            var intercept = new double[height, width];
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    double variance = meanGg[r, c] - (meanG[r, c] * meanG[r, c]);
                    double covariance = meanPg[r, c] - (meanG[r, c] * meanP[r, c]);
                    double a = covariance / (variance + degreeOfSmoothing);
                    slope[r, c] = a;
                    intercept[r, c] = meanP[r, c] - (a * meanG[r, c]);
                }
            }

            // Every pixel sits in many windows, each with its own line. Averaging the lines rather
            // than picking one is what makes the output continuous across window boundaries.
            double[,] meanA = BoxMean(slope, neighborhoodRows, neighborhoodCols);
            double[,] meanB = BoxMean(intercept, neighborhoodRows, neighborhoodCols);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    result[r, c, ch] = (meanA[r, c] * guide[r, c, guideChannel]) + meanB[r, c];
                }
            }
        }

        GC.KeepAlive(image);
        GC.KeepAlive(guide);
        return result;
    }

    /// <summary>A box mean over a scalar field, with the window clipped at the border.</summary>
    private static double[,] BoxMean(double[,] values, int rows, int cols)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);
        int radiusRows = rows / 2;
        int radiusCols = cols / 2;

        // Summed-area table, so a window of any size costs four lookups.
        var integral = new double[height + 1, width + 1];
        for (int r = 0; r < height; r++)
        {
            double running = 0;
            for (int c = 0; c < width; c++)
            {
                running += values[r, c];
                integral[r + 1, c + 1] = integral[r, c + 1] + running;
            }
        }

        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            int top = Math.Max(0, r - radiusRows);
            int bottom = Math.Min(height, r + radiusRows + 1);
            for (int c = 0; c < width; c++)
            {
                int left = Math.Max(0, c - radiusCols);
                int right = Math.Min(width, c + radiusCols + 1);
                double sum = integral[bottom, right] - integral[top, right]
                    - integral[bottom, left] + integral[top, left];
                result[r, c] = sum / ((bottom - top) * (right - left));
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Anisotropic diffusion (imdiffusefilt, imdiffuseest)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Perona–Malik anisotropic diffusion (MATLAB <c>imdiffusefilt</c>): heat flow through the
    /// picture whose conductivity falls off wherever the gradient is steep.
    /// </summary>
    /// <remarks>
    /// Ordinary diffusion is a Gaussian blur — it conducts equally everywhere and edges dissolve.
    /// Making conductivity a falling function of the gradient turns each edge into an insulator, so
    /// the picture smooths within regions and stops at their boundaries. Iterating is the point: each
    /// pass moves heat one pixel, so the smoothing widens with the iteration count while the edges
    /// stay where they were.
    /// </remarks>
    /// <param name="image">The picture to filter.</param>
    /// <param name="gradientThresholds">
    /// One threshold per iteration. A gradient at the threshold conducts at about a third; well above
    /// it, almost not at all.
    /// </param>
    /// <param name="eightConnected">True for MATLAB's <c>'maximal'</c> connectivity.</param>
    /// <param name="conduction">Which conduction function to use.</param>
    public static ImageBuffer AnisotropicDiffusion(
        ImageBuffer image,
        IReadOnlyList<double> gradientThresholds,
        bool eightConnected = true,
        Conduction conduction = Conduction.Exponential)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(gradientThresholds);
        if (gradientThresholds.Count == 0)
        {
            throw new ArgumentException("imdiffusefilt needs at least one iteration.", nameof(gradientThresholds));
        }

        foreach (double threshold in gradientThresholds)
        {
            if (threshold <= 0)
            {
                throw new ArgumentException("imdiffusefilt gradient thresholds must be positive.");
            }
        }

        int height = image.Height;
        int width = image.Width;
        int channels = image.Channels;

        // The four axis neighbours are one pixel away; the four diagonals are √2 away, so their
        // gradients are divided by that distance and their contribution weighted by one over the
        // square of it. Skipping that would let the diagonals diffuse faster than the axes and turn
        // a round blob square.
        (int Dy, int Dx, double Weight)[] neighbours = eightConnected
            ?
            [
                (-1, 0, 1.0), (1, 0, 1.0), (0, -1, 1.0), (0, 1, 1.0),
                (-1, -1, 0.5), (-1, 1, 0.5), (1, -1, 0.5), (1, 1, 0.5),
            ]
            : [(-1, 0, 1.0), (1, 0, 1.0), (0, -1, 1.0), (0, 1, 1.0)];

        double totalWeight = 0;
        foreach ((_, _, double weight) in neighbours)
        {
            totalWeight += weight;
        }

        ImageBuffer current = image.Clone();
        foreach (double threshold in gradientThresholds)
        {
            var next = new ImageBuffer(height, width, channels);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        double centre = current[r, c, ch];
                        double flow = 0;
                        foreach ((int dy, int dx, double weight) in neighbours)
                        {
                            int sr = Math.Clamp(r + dy, 0, height - 1);
                            int sc = Math.Clamp(c + dx, 0, width - 1);
                            double gradient = current[sr, sc, ch] - centre;
                            double ratio = gradient * Math.Sqrt(weight) / threshold;
                            double conductivity = conduction == Conduction.Quadratic
                                ? 1.0 / (1.0 + (ratio * ratio))
                                : Math.Exp(-ratio * ratio);
                            flow += weight * conductivity * gradient;
                        }

                        next[r, c, ch] = centre + (flow / totalWeight);
                    }
                }
            }

            current.Dispose();
            current = next;
        }

        GC.KeepAlive(image);
        return current;
    }

    /// <summary>
    /// Suggests diffusion settings for a picture (MATLAB <c>imdiffuseest</c>): a threshold for each
    /// of a number of iterations.
    /// </summary>
    /// <remarks>
    /// The first threshold is the ninetieth percentile of the gradient magnitude, which lets the pass
    /// smooth everything except the strongest tenth of the edges. Later thresholds fall linearly, so
    /// each pass conducts across less than the last and the surviving edges sharpen rather than
    /// creep. MathWorks documents what the estimate is for, not how it is computed, so this rule is
    /// stated rather than matched.
    /// </remarks>
    public static (double[] Thresholds, int Iterations) EstimateDiffusion(ImageBuffer image, int iterations = 5)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (iterations < 1)
        {
            throw new ArgumentException("imdiffuseest needs at least one iteration.", nameof(iterations));
        }

        using ImageBuffer gray = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
        var magnitudes = new List<double>(gray.Height * gray.Width);
        for (int r = 0; r < gray.Height; r++)
        {
            for (int c = 0; c < gray.Width; c++)
            {
                double gx = gray[r, Math.Min(c + 1, gray.Width - 1), 0] - gray[r, Math.Max(c - 1, 0), 0];
                double gy = gray[Math.Min(r + 1, gray.Height - 1), c, 0] - gray[Math.Max(r - 1, 0), c, 0];
                magnitudes.Add(Math.Sqrt((gx * gx) + (gy * gy)) / 2.0);
            }
        }

        magnitudes.Sort();
        double ninetieth = magnitudes[Math.Clamp((int)(0.9 * (magnitudes.Count - 1)), 0, magnitudes.Count - 1)];
        if (ninetieth <= 0)
        {
            // A flat picture has no edges to preserve; any positive threshold diffuses it evenly.
            ninetieth = 1e-6;
        }

        var thresholds = new double[iterations];
        for (int k = 0; k < iterations; k++)
        {
            thresholds[k] = ninetieth * (iterations - k) / iterations;
        }

        return (thresholds, iterations);
    }

    // ---------------------------------------------------------------------------------------
    // Non-local means (imnlmfilt)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Non-local means (MATLAB <c>imnlmfilt</c>): average each pixel with every other pixel in a
    /// search window whose surroundings look like its own.
    /// </summary>
    /// <remarks>
    /// The insight is that a photograph repeats itself — the same edge, the same texture, the same
    /// flat patch occur many times over — so a pixel has far more genuinely comparable samples than
    /// the ones touching it. Comparing whole patches rather than single pixels is what makes the
    /// similarity judgement survive the noise it is trying to remove.
    /// </remarks>
    /// <param name="image">The picture to filter.</param>
    /// <param name="degreeOfSmoothing">
    /// How unlike two patches may be and still count as similar; the noise standard deviation is the
    /// usual choice.
    /// </param>
    /// <param name="searchSize">The side of the window searched for similar patches (odd).</param>
    /// <param name="comparisonSize">The side of the patch compared (odd).</param>
    public static ImageBuffer NonLocalMeans(
        ImageBuffer image, double degreeOfSmoothing, int searchSize = 21, int comparisonSize = 5)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (degreeOfSmoothing <= 0)
        {
            throw new ArgumentException("imnlmfilt degree of smoothing must be positive.", nameof(degreeOfSmoothing));
        }

        if (searchSize < 1 || comparisonSize < 1)
        {
            throw new ArgumentException("imnlmfilt window sizes must be positive.");
        }

        int height = image.Height;
        int width = image.Width;
        int channels = image.Channels;
        int searchRadius = searchSize / 2;
        int compareRadius = comparisonSize / 2;
        int pad = searchRadius + compareRadius;

        int paddedHeight = height + (2 * pad);
        int paddedWidth = width + (2 * pad);
        var padded = new double[channels][];
        for (int ch = 0; ch < channels; ch++)
        {
            var plane = new double[paddedHeight * paddedWidth];
            for (int r = 0; r < paddedHeight; r++)
            {
                int sr = Filters.MapIndex(r - pad, height, Filters.Boundary.Symmetric);
                for (int c = 0; c < paddedWidth; c++)
                {
                    int sc = Filters.MapIndex(c - pad, width, Filters.Boundary.Symmetric);
                    plane[(r * paddedWidth) + c] = image[sr, sc, ch];
                }
            }

            padded[ch] = plane;
        }

        var weights = new double[height * width];
        var totals = new double[height * width * channels];
        var maxima = new double[height * width];

        // One pass per displacement rather than per pixel pair: for a fixed offset every patch
        // distance in the picture is a box sum over the same squared-difference image, so a
        // summed-area table turns each of them into four lookups. Without it the cost is the search
        // window times the comparison window per pixel, which is thousands of multiplies each.
        double patchArea = (double)comparisonSize * comparisonSize * channels;
        double denominator = degreeOfSmoothing * degreeOfSmoothing;
        var difference = new double[paddedHeight * paddedWidth];
        var integral = new double[(paddedHeight + 1) * (paddedWidth + 1)];

        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                if (dy == 0 && dx == 0)
                {
                    continue;
                }

                Array.Clear(difference);
                for (int ch = 0; ch < channels; ch++)
                {
                    double[] plane = padded[ch];
                    for (int r = 0; r < paddedHeight; r++)
                    {
                        int sr = Math.Clamp(r + dy, 0, paddedHeight - 1);
                        int rowBase = r * paddedWidth;
                        int shiftedBase = sr * paddedWidth;
                        for (int c = 0; c < paddedWidth; c++)
                        {
                            int sc = Math.Clamp(c + dx, 0, paddedWidth - 1);
                            double d = plane[rowBase + c] - plane[shiftedBase + sc];
                            difference[rowBase + c] += d * d;
                        }
                    }
                }

                Array.Clear(integral);
                for (int r = 0; r < paddedHeight; r++)
                {
                    double running = 0;
                    int rowBase = r * paddedWidth;
                    int aboveBase = r * (paddedWidth + 1);
                    int hereBase = (r + 1) * (paddedWidth + 1);
                    for (int c = 0; c < paddedWidth; c++)
                    {
                        running += difference[rowBase + c];
                        integral[hereBase + c + 1] = integral[aboveBase + c + 1] + running;
                    }
                }

                for (int r = 0; r < height; r++)
                {
                    int top = r + pad - compareRadius;
                    int bottom = r + pad + compareRadius + 1;
                    for (int c = 0; c < width; c++)
                    {
                        int left = c + pad - compareRadius;
                        int right = c + pad + compareRadius + 1;
                        double sum =
                            integral[(bottom * (paddedWidth + 1)) + right]
                            - integral[(top * (paddedWidth + 1)) + right]
                            - integral[(bottom * (paddedWidth + 1)) + left]
                            + integral[(top * (paddedWidth + 1)) + left];

                        double weight = Math.Exp(-sum / patchArea / denominator);
                        int here = (r * width) + c;
                        weights[here] += weight;
                        maxima[here] = Math.Max(maxima[here], weight);
                        for (int ch = 0; ch < channels; ch++)
                        {
                            totals[(here * channels) + ch] +=
                                weight * padded[ch][((r + pad + dy) * paddedWidth) + c + pad + dx];
                        }
                    }
                }
            }
        }

        var result = new ImageBuffer(height, width, channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int here = (r * width) + c;

                // The pixel's own weight would be one, which for a lonely patch would swamp every
                // other sample and leave the noise untouched. Giving it the largest weight any
                // neighbour earned is the standard remedy.
                double self = maxima[here] > 0 ? maxima[here] : 1.0;
                double total = weights[here] + self;
                for (int ch = 0; ch < channels; ch++)
                {
                    double sum = totals[(here * channels) + ch] + (self * image[r, c, ch]);
                    result[r, c, ch] = Math.Clamp(sum / total, 0, 1);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The noise standard deviation, by Immerkær's estimator — the mean absolute response of a
    /// Laplacian-like mask that a smooth picture is invisible to.
    /// </summary>
    /// <remarks>
    /// This is what <c>imnlmfilt</c> returns as its estimated degree of smoothing when none was
    /// given. The mask has zero response to any linear ramp, so what it measures is what is left over
    /// after the picture itself: the noise.
    /// </remarks>
    public static double EstimateNoise(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ImageBuffer gray = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
        int height = gray.Height;
        int width = gray.Width;
        if (height < 3 || width < 3)
        {
            return 0;
        }

        double sum = 0;
        for (int r = 1; r < height - 1; r++)
        {
            for (int c = 1; c < width - 1; c++)
            {
                double response =
                    (4 * gray[r, c, 0])
                    - (2 * (gray[r - 1, c, 0] + gray[r + 1, c, 0] + gray[r, c - 1, 0] + gray[r, c + 1, 0]))
                    + gray[r - 1, c - 1, 0] + gray[r - 1, c + 1, 0]
                    + gray[r + 1, c - 1, 0] + gray[r + 1, c + 1, 0];
                sum += Math.Abs(response);
            }
        }

        // √(π/2) converts a mean absolute value to a standard deviation; 6 is the mask's own norm.
        return sum * Math.Sqrt(0.5 * Math.PI) / (6.0 * (width - 2) * (height - 2));
    }
}
