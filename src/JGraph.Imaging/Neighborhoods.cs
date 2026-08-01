namespace JGraph.Imaging;

/// <summary>
/// Neighbourhood statistics and the padding they read through: MATLAB's <c>padarray</c>,
/// <c>ordfilt2</c>, the <c>stdfilt</c>/<c>rangefilt</c>/<c>entropyfilt</c>/<c>modefilt</c> family,
/// <c>wiener2</c>, and the integral images that make repeated box sums free.
/// </summary>
/// <remarks>
/// Every function here takes an arbitrary neighbourhood — a boolean domain rather than a rectangle —
/// because that is the shape MATLAB documents for <c>ordfilt2</c> and the <c>*filt</c> family, and a
/// rectangle is just the domain that is all true. The scan order within a neighbourhood is row-major
/// over the domain, which is the order the tie-breaking in <see cref="Mode"/> depends on.
/// </remarks>
public static class Neighborhoods
{
    /// <summary>Which sides of the image <see cref="Pad"/> extends.</summary>
    public enum PadDirection
    {
        /// <summary>Pad both sides of every dimension (MATLAB default).</summary>
        Both,

        /// <summary>Pad only before the first row and column.</summary>
        Pre,

        /// <summary>Pad only after the last row and column.</summary>
        Post,
    }

    /// <summary>
    /// Extends an image with padding (MATLAB <c>padarray</c>). <paramref name="boundary"/> selects the
    /// rule; <see cref="Filters.Boundary.Zero"/> pads with <paramref name="padValue"/>, which is how
    /// the constant-value form is expressed.
    /// </summary>
    /// <param name="image">The image to extend.</param>
    /// <param name="padRows">Rows added on each padded side.</param>
    /// <param name="padCols">Columns added on each padded side.</param>
    /// <param name="boundary">How the added samples are filled.</param>
    /// <param name="padValue">The constant for <see cref="Filters.Boundary.Zero"/>.</param>
    /// <param name="direction">Which sides are extended.</param>
    public static ImageBuffer Pad(
        ImageBuffer image,
        int padRows,
        int padCols,
        Filters.Boundary boundary = Filters.Boundary.Zero,
        double padValue = 0.0,
        PadDirection direction = PadDirection.Both)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(padRows);
        ArgumentOutOfRangeException.ThrowIfNegative(padCols);

        int preRows = direction == PadDirection.Post ? 0 : padRows;
        int postRows = direction == PadDirection.Pre ? 0 : padRows;
        int preCols = direction == PadDirection.Post ? 0 : padCols;
        int postCols = direction == PadDirection.Pre ? 0 : padCols;

        var result = new ImageBuffer(
            image.Height + preRows + postRows, image.Width + preCols + postCols, image.Channels)
        {
            Class = image.Class,
        };

        for (int r = 0; r < result.Height; r++)
        {
            for (int c = 0; c < result.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = Filters.Sample(image, r - preRows, c - preCols, ch, boundary, padValue);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The <paramref name="order"/>-th smallest value in each neighbourhood (MATLAB <c>ordfilt2</c>,
    /// 1-based order). <paramref name="offsets"/> optionally adds a per-position bias before the sort,
    /// which is what turns an order filter into grayscale morphology.
    /// </summary>
    /// <param name="image">The image to filter.</param>
    /// <param name="domain">The neighbourhood; true positions take part.</param>
    /// <param name="order">Rank to select, from 1 (minimum) to the number of true positions.</param>
    /// <param name="offsets">Additive bias per domain position, or null.</param>
    /// <param name="boundary">How samples beyond the edge are supplied.</param>
    public static ImageBuffer OrderFilter(
        ImageBuffer image,
        bool[,] domain,
        int order,
        double[,]? offsets = null,
        Filters.Boundary boundary = Filters.Boundary.Zero)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(domain);
        int members = CountMembers(domain);
        if (order < 1 || order > members)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order), order, $"order must be in 1..{members} for this neighbourhood.");
        }

        if (offsets is not null &&
            (offsets.GetLength(0) != domain.GetLength(0) || offsets.GetLength(1) != domain.GetLength(1)))
        {
            throw new ArgumentException("ordfilt2 offsets must be the same size as the neighbourhood.", nameof(offsets));
        }

        var window = new double[members];
        return Apply(image, domain, boundary, (values, count) =>
        {
            values.CopyTo(window.AsSpan(0, count));
            Array.Sort(window, 0, count);
            return window[order - 1];
        }, offsets);
    }

    /// <summary>The local standard deviation over a neighbourhood (MATLAB <c>stdfilt</c>, normalized by n−1).</summary>
    public static ImageBuffer StandardDeviation(ImageBuffer image, bool[,] domain) =>
        Apply(image, domain, Filters.Boundary.Symmetric, static (values, count) =>
        {
            if (count < 2)
            {
                return 0.0;
            }

            double mean = 0;
            for (int i = 0; i < count; i++)
            {
                mean += values[i];
            }

            mean /= count;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                double d = values[i] - mean;
                sum += d * d;
            }

            return Math.Sqrt(sum / (count - 1));
        });

    /// <summary>The local max−min over a neighbourhood (MATLAB <c>rangefilt</c>).</summary>
    public static ImageBuffer Range(ImageBuffer image, bool[,] domain) =>
        Apply(image, domain, Filters.Boundary.Symmetric, static (values, count) =>
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (values[i] < min) { min = values[i]; }
                if (values[i] > max) { max = values[i]; }
            }

            return max - min;
        });

    /// <summary>
    /// The local entropy over a neighbourhood (MATLAB <c>entropyfilt</c>), in bits, measured on the
    /// 256-bin histogram MATLAB uses for non-logical input.
    /// </summary>
    public static ImageBuffer Entropy(ImageBuffer image, bool[,] domain, int bins = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        var counts = new int[bins];
        return Apply(image, domain, Filters.Boundary.Symmetric, (values, count) =>
        {
            Array.Clear(counts);
            for (int i = 0; i < count; i++)
            {
                int bin = (int)Math.Clamp(Math.Floor(values[i] * bins), 0, bins - 1);
                counts[bin]++;
            }

            double entropy = 0;
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] == 0)
                {
                    continue;
                }

                double p = (double)counts[b] / count;
                entropy -= p * Math.Log2(p);
            }

            return entropy;
        });
    }

    /// <summary>
    /// The most frequent value in each neighbourhood (MATLAB <c>modefilt</c>). Ties go to the smallest
    /// value, which is MATLAB's documented rule.
    /// </summary>
    public static ImageBuffer Mode(ImageBuffer image, bool[,] domain, Filters.Boundary boundary = Filters.Boundary.Symmetric)
    {
        int members = CountMembers(domain);
        var window = new double[members];
        return Apply(image, domain, boundary, (values, count) =>
        {
            values.CopyTo(window.AsSpan(0, count));
            Array.Sort(window, 0, count);

            double best = window[0];
            int bestRun = 0;
            int run = 0;
            for (int i = 0; i < count; i++)
            {
                run = i > 0 && window[i] == window[i - 1] ? run + 1 : 1;
                if (run > bestRun)
                {
                    // Strictly greater keeps the first — smallest — value of a tied run.
                    bestRun = run;
                    best = window[i];
                }
            }

            return best;
        });
    }

    /// <summary>
    /// Adaptive Wiener smoothing (MATLAB <c>wiener2</c>): each pixel is pulled towards its local mean
    /// by how much of the local variance the noise accounts for, so flat regions smooth hard and
    /// detailed ones are left alone. Returns the filtered image and the noise variance used.
    /// </summary>
    /// <param name="image">The image to filter.</param>
    /// <param name="windowHeight">Neighbourhood rows.</param>
    /// <param name="windowWidth">Neighbourhood columns.</param>
    /// <param name="noiseVariance">The noise power, or null to estimate it as the mean local variance.</param>
    public static (ImageBuffer Result, double NoiseVariance) Wiener(
        ImageBuffer image, int windowHeight = 3, int windowWidth = 3, double? noiseVariance = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);

        using ImageBuffer mean = Filters.BoxMean(image, windowHeight, windowWidth, Filters.Boundary.Zero);
        using ImageBuffer squared = Square(image);
        using ImageBuffer meanSquare = Filters.BoxMean(squared, windowHeight, windowWidth, Filters.Boundary.Zero);

        int count = image.Height * image.Width * image.Channels;
        var variance = new double[count];
        double total = 0;
        int k = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double m = mean[r, c, ch];
                    double v = Math.Max(0.0, meanSquare[r, c, ch] - (m * m));
                    variance[k++] = v;
                    total += v;
                }
            }
        }

        double noise = noiseVariance ?? (count == 0 ? 0.0 : total / count);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        k = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double m = mean[r, c, ch];
                    double v = variance[k++];
                    // Where the local variance is at or below the noise floor the pixel becomes its
                    // local mean; where it is far above, the pixel is kept as it is.
                    double keep = v <= noise ? 0.0 : (v - noise) / v;
                    result[r, c, ch] = m + (keep * (image[r, c, ch] - m));
                }
            }
        }

        GC.KeepAlive(image);
        return (result, noise);
    }

    /// <summary>
    /// The summed-area table of a single channel (MATLAB <c>integralImage</c>): an
    /// (H+1)×(W+1) array whose entry <c>(r, c)</c> is the sum of every sample above and left of it, so
    /// any rectangle's sum is four lookups.
    /// </summary>
    public static double[,] IntegralImage(ImageBuffer image, int channel = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        var integral = new double[image.Height + 1, image.Width + 1];
        for (int r = 0; r < image.Height; r++)
        {
            double rowSum = 0;
            for (int c = 0; c < image.Width; c++)
            {
                rowSum += image[r, c, channel];
                integral[r + 1, c + 1] = integral[r, c + 1] + rowSum;
            }
        }

        GC.KeepAlive(image);
        return integral;
    }

    /// <summary>
    /// The rotated (45°) summed-area table (MATLAB <c>integralImage(I, 'rotated')</c>), an
    /// (H+1)×(W+2) array whose entry <c>(i, j)</c> is the sum over the upward triangle with its apex
    /// at pixel <c>(i-1, j-1)</c>, so any diamond-oriented rectangle's sum is four lookups.
    /// </summary>
    /// <remarks>
    /// The recurrence needs apex columns to either side of the stored table — a triangle H rows deep
    /// reaches H columns outwards — so it runs over a scratch table widened by the image height and
    /// the documented window is copied out at the end. That costs H·(W+2H) doubles for the duration of
    /// the call, which is the price of the rotated table being exact at its left and right edges
    /// rather than quietly truncated there.
    /// </remarks>
    public static double[,] RotatedIntegralImage(ImageBuffer image, int channel = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        int h = image.Height;
        int w = image.Width;
        int margin = h + 1;
        int span = w + (2 * margin);

        // scratch[i, a + margin] is the triangle whose apex sits at image column a, row i-1.
        var scratch = new double[h + 1, span];
        for (int i = 1; i <= h; i++)
        {
            for (int k = 0; k < span; k++)
            {
                int a = k - margin;
                double left = k > 0 ? scratch[i - 1, k - 1] : 0.0;
                double right = k + 1 < span ? scratch[i - 1, k + 1] : 0.0;
                double above = i >= 2 ? scratch[i - 2, k] : 0.0;
                scratch[i, k] = left + right - above + Pixel(image, i - 1, a, channel) + Pixel(image, i - 2, a, channel);
            }
        }

        var integral = new double[h + 1, w + 2];
        for (int i = 0; i <= h; i++)
        {
            for (int j = 0; j < w + 2; j++)
            {
                integral[i, j] = scratch[i, j - 1 + margin];
            }
        }

        GC.KeepAlive(image);
        return integral;
    }

    /// <summary>
    /// Box filtering straight off a summed-area table (MATLAB <c>integralBoxFilter</c>). The result is
    /// only defined where the whole window fits, so it is smaller than the source image by the filter
    /// extent minus one — MATLAB's 'valid' region.
    /// </summary>
    /// <param name="integral">A table from <see cref="IntegralImage"/>.</param>
    /// <param name="windowHeight">Window rows.</param>
    /// <param name="windowWidth">Window columns.</param>
    /// <param name="normalizationFactor">
    /// Multiplier applied to each window sum, matching MATLAB's <c>'NormalizationFactor'</c>; the
    /// reciprocal of the window area by default, which makes the filter an average.
    /// </param>
    public static double[,] IntegralBoxFilter(
        double[,] integral, int windowHeight = 3, int windowWidth = 3, double? normalizationFactor = null)
    {
        ArgumentNullException.ThrowIfNull(integral);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
        int h = integral.GetLength(0) - 1;
        int w = integral.GetLength(1) - 1;
        if (windowHeight > h || windowWidth > w)
        {
            throw new ArgumentException("integralBoxFilter window is larger than the integral image.");
        }

        double factor = normalizationFactor ?? (1.0 / ((double)windowHeight * windowWidth));
        var result = new double[h - windowHeight + 1, w - windowWidth + 1];
        for (int r = 0; r < result.GetLength(0); r++)
        {
            for (int c = 0; c < result.GetLength(1); c++)
            {
                double sum = integral[r + windowHeight, c + windowWidth]
                    - integral[r, c + windowWidth]
                    - integral[r + windowHeight, c]
                    + integral[r, c];
                result[r, c] = sum * factor;
            }
        }

        return result;
    }

    private static double Pixel(ImageBuffer image, int r, int c, int channel) =>
        (uint)r < (uint)image.Height && (uint)c < (uint)image.Width ? image[r, c, channel] : 0.0;

    /// <summary>A rows×cols neighbourhood with every position taking part.</summary>
    public static bool[,] Rectangle(int rows, int cols)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        var domain = new bool[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                domain[r, c] = true;
            }
        }

        return domain;
    }

    /// <summary>How many positions of a neighbourhood take part.</summary>
    public static int CountMembers(bool[,] domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        int count = 0;
        foreach (bool member in domain)
        {
            if (member)
            {
                count++;
            }
        }

        if (count == 0)
        {
            throw new ArgumentException("the neighbourhood has no positions in it.", nameof(domain));
        }

        return count;
    }

    /// <summary>
    /// Gathers each pixel's neighbourhood and reduces it. The gathered span is reused between pixels,
    /// so a reducer must not hold on to it.
    /// </summary>
    private static ImageBuffer Apply(
        ImageBuffer image,
        bool[,] domain,
        Filters.Boundary boundary,
        Func<double[], int, double> reduce,
        double[,]? offsets = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        int dh = domain.GetLength(0);
        int dw = domain.GetLength(1);
        int anchorR = (dh - 1) / 2;
        int anchorC = (dw - 1) / 2;
        var gathered = new double[CountMembers(domain)];

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    int count = 0;
                    for (int dr = 0; dr < dh; dr++)
                    {
                        for (int dc = 0; dc < dw; dc++)
                        {
                            if (!domain[dr, dc])
                            {
                                continue;
                            }

                            double value = Filters.Sample(
                                image, r + dr - anchorR, c + dc - anchorC, ch, boundary);
                            gathered[count++] = offsets is null ? value : value + offsets[dr, dc];
                        }
                    }

                    result[r, c, ch] = reduce(gathered, count);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static ImageBuffer Square(ImageBuffer image)
    {
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = src[i] * src[i];
        }

        GC.KeepAlive(image);
        return result;
    }
}
