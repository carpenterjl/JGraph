namespace JGraph.Imaging;

/// <summary>
/// Filtering, gradients and point operations on a <see cref="Volume"/> — the three-dimensional
/// counterparts of <see cref="Filters"/>, <see cref="Gradients"/> and <see cref="PointOps"/>.
/// </summary>
/// <remarks>
/// A volume filter is not a slice filter run in a loop. Filtering each plane separately smooths within
/// a slice and leaves the direction the slices were stacked in untouched, which is precisely the
/// direction a scan is usually coarsest in; it produces a result that looks right on screen and is
/// wrong the moment anything measures across planes. Everything here reaches through the stack.
/// </remarks>
public static class VolumeFilters
{
    /// <summary>The two edge finders <c>edge3</c> documents.</summary>
    public enum EdgeMethod
    {
        /// <summary>Threshold the 3-D Sobel gradient magnitude.</summary>
        Sobel,

        /// <summary>Smooth, suppress non-maxima along the gradient, then link by hysteresis.</summary>
        ApproxCanny,
    }

    /// <summary>Extends a volume on every side (the N-D form of <c>padarray</c>).</summary>
    /// <param name="volume">The volume to extend.</param>
    /// <param name="pre">Rows, columns and planes to add before the data.</param>
    /// <param name="post">Rows, columns and planes to add after it.</param>
    /// <param name="boundary">Where the added samples come from.</param>
    /// <param name="padValue">The constant used when <paramref name="boundary"/> is <see cref="Filters.Boundary.Zero"/>.</param>
    public static Volume Pad(
        Volume volume,
        (int Rows, int Cols, int Planes) pre,
        (int Rows, int Cols, int Planes) post,
        Filters.Boundary boundary = Filters.Boundary.Zero,
        double padValue = 0.0)
    {
        ArgumentNullException.ThrowIfNull(volume);
        var result = new Volume(
            volume.Height + pre.Rows + post.Rows,
            volume.Width + pre.Cols + post.Cols,
            volume.Depth + pre.Planes + post.Planes);
        for (int p = 0; p < result.Depth; p++)
        {
            for (int c = 0; c < result.Width; c++)
            {
                for (int r = 0; r < result.Height; r++)
                {
                    result[r, c, p] = volume.At(
                        r - pre.Rows, c - pre.Cols, p - pre.Planes, boundary, padValue);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Correlates a volume with a 3-D kernel, same-size, each output sample the weighted sum of the
    /// neighbourhood the kernel covers. The kernel origin sits at <c>(k−1)/2</c> per axis, MATLAB's
    /// anchor written zero-based.
    /// </summary>
    public static Volume Correlate(
        Volume volume,
        Volume kernel,
        Filters.Boundary boundary = Filters.Boundary.Replicate,
        double padValue = 0.0)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(kernel);
        int ar = (kernel.Height - 1) / 2;
        int ac = (kernel.Width - 1) / 2;
        int ap = (kernel.Depth - 1) / 2;
        var result = Volume.Like(volume);
        for (int p = 0; p < volume.Depth; p++)
        {
            for (int c = 0; c < volume.Width; c++)
            {
                for (int r = 0; r < volume.Height; r++)
                {
                    double sum = 0;
                    for (int kp = 0; kp < kernel.Depth; kp++)
                    {
                        for (int kc = 0; kc < kernel.Width; kc++)
                        {
                            for (int kr = 0; kr < kernel.Height; kr++)
                            {
                                double weight = kernel[kr, kc, kp];
                                if (weight == 0)
                                {
                                    continue;
                                }

                                sum += weight * volume.At(
                                    r + kr - ar, c + kc - ac, p + kp - ap, boundary, padValue);
                            }
                        }
                    }

                    result[r, c, p] = sum;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Filters along one axis at a time with three 1-D kernels. A separable kernel costs
    /// <c>kr + kc + kp</c> multiplies per sample instead of their product — the difference between a
    /// 9×9×9 Gaussian being 27 operations and being 729, which is what makes a volume blur affordable
    /// at all.
    /// </summary>
    public static Volume Separable(
        Volume volume,
        double[] down,
        double[] across,
        double[] through,
        Filters.Boundary boundary = Filters.Boundary.Replicate,
        double padValue = 0.0)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(down);
        ArgumentNullException.ThrowIfNull(across);
        ArgumentNullException.ThrowIfNull(through);
        Volume rows = Along(volume, 0, down, boundary, padValue);
        Volume cols = Along(rows, 1, across, boundary, padValue);
        rows.Dispose();
        Volume planes = Along(cols, 2, through, boundary, padValue);
        cols.Dispose();
        return planes;
    }

    /// <summary>A separable Gaussian blur (MATLAB <c>imgaussfilt3</c>), one sigma per axis.</summary>
    /// <param name="volume">The volume to blur.</param>
    /// <param name="sigma">Standard deviation per axis; each must be positive.</param>
    /// <param name="size">Kernel extent per axis, or 0 for MATLAB's <c>2·ceil(2σ)+1</c>.</param>
    /// <param name="boundary">How samples beyond the edge are supplied.</param>
    public static Volume GaussianBlur(
        Volume volume,
        (double Rows, double Cols, double Planes) sigma,
        (int Rows, int Cols, int Planes) size = default,
        Filters.Boundary boundary = Filters.Boundary.Replicate)
    {
        ArgumentNullException.ThrowIfNull(volume);
        return Separable(
            volume,
            Filters.Gaussian1D(sigma.Rows, size.Rows),
            Filters.Gaussian1D(sigma.Cols, size.Cols),
            Filters.Gaussian1D(sigma.Planes, size.Planes),
            boundary);
    }

    /// <summary>
    /// A box mean (MATLAB <c>imboxfilt3</c>). The default normalization divides by the neighbourhood
    /// size, which makes the filter an average; passing a different one turns it into a plain sum, or
    /// any other scaling a script wants.
    /// </summary>
    public static Volume BoxMean(
        Volume volume,
        (int Rows, int Cols, int Planes) size,
        Filters.Boundary boundary = Filters.Boundary.Replicate,
        double? normalization = null)
    {
        ArgumentNullException.ThrowIfNull(volume);
        RequireOdd(size, "imboxfilt3");
        double factor = normalization ?? 1.0 / ((double)size.Rows * size.Cols * size.Planes);
        double[] ones(int n)
        {
            var k = new double[n];
            Array.Fill(k, 1.0);
            return k;
        }

        Volume summed = Separable(volume, ones(size.Rows), ones(size.Cols), ones(size.Planes), boundary);
        Span<double> samples = summed.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= factor;
        }

        return summed;
    }

    /// <summary>
    /// A median filter (MATLAB <c>medfilt3</c>). The median is the one neighbourhood statistic that
    /// cannot be built from separable passes — it is not linear — so this pays the full window cost,
    /// which is exactly why the default window is 3×3×3.
    /// </summary>
    public static Volume Median(
        Volume volume,
        (int Rows, int Cols, int Planes) window = default,
        Filters.Boundary boundary = Filters.Boundary.Symmetric)
    {
        ArgumentNullException.ThrowIfNull(volume);
        if (window == default)
        {
            window = (3, 3, 3);
        }

        RequireOdd(window, "medfilt3");
        int ar = window.Rows / 2;
        int ac = window.Cols / 2;
        int ap = window.Planes / 2;
        var buffer = new double[window.Rows * window.Cols * window.Planes];
        var result = Volume.Like(volume);
        for (int p = 0; p < volume.Depth; p++)
        {
            for (int c = 0; c < volume.Width; c++)
            {
                for (int r = 0; r < volume.Height; r++)
                {
                    int n = 0;
                    for (int kp = -ap; kp <= ap; kp++)
                    {
                        for (int kc = -ac; kc <= ac; kc++)
                        {
                            for (int kr = -ar; kr <= ar; kr++)
                            {
                                buffer[n++] = volume.At(r + kr, c + kc, p + kp, boundary);
                            }
                        }
                    }

                    Array.Sort(buffer, 0, n);
                    result[r, c, p] = (n % 2) == 1
                        ? buffer[n / 2]
                        : 0.5 * (buffer[(n / 2) - 1] + buffer[n / 2]);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The integral (summed-area) volume, one sample larger per axis than its input, holding the sum
    /// of everything above, left of and before each corner. Any box sum then costs eight lookups
    /// whatever its size — the reason a box filter's cost stops depending on its window.
    /// </summary>
    public static Volume Integral(Volume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        var result = new Volume(volume.Height + 1, volume.Width + 1, volume.Depth + 1);
        for (int p = 1; p <= volume.Depth; p++)
        {
            for (int c = 1; c <= volume.Width; c++)
            {
                for (int r = 1; r <= volume.Height; r++)
                {
                    // Inclusion-exclusion over the three faces that meet at this corner.
                    result[r, c, p] = volume[r - 1, c - 1, p - 1]
                        + result[r - 1, c, p] + result[r, c - 1, p] + result[r, c, p - 1]
                        - result[r - 1, c - 1, p] - result[r - 1, c, p - 1] - result[r, c - 1, p - 1]
                        + result[r - 1, c - 1, p - 1];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A box filter read off an integral volume (MATLAB <c>integralBoxFilter3</c>). The result covers
    /// only the positions where the whole window fits, so it is <c>size(intV) − filterSize</c> — there
    /// is no boundary rule here because an integral volume carries no information about what lies
    /// outside it.
    /// </summary>
    public static Volume IntegralBoxFilter(
        Volume integral,
        (int Rows, int Cols, int Planes) size = default,
        double? normalization = null)
    {
        ArgumentNullException.ThrowIfNull(integral);
        if (size == default)
        {
            size = (3, 3, 3);
        }

        int height = integral.Height - size.Rows;
        int width = integral.Width - size.Cols;
        int depth = integral.Depth - size.Planes;
        if (height < 1 || width < 1 || depth < 1)
        {
            throw new ArgumentException(
                "integralBoxFilter3: the window is larger than the integral volume can cover.");
        }

        double factor = normalization ?? 1.0 / ((double)size.Rows * size.Cols * size.Planes);
        var result = new Volume(height, width, depth);
        for (int p = 0; p < depth; p++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    int r1 = r + size.Rows;
                    int c1 = c + size.Cols;
                    int p1 = p + size.Planes;
                    double sum = integral[r1, c1, p1]
                        - integral[r, c1, p1] - integral[r1, c, p1] - integral[r1, c1, p]
                        + integral[r, c, p1] + integral[r, c1, p] + integral[r1, c, p]
                        - integral[r, c, p];
                    result[r, c, p] = sum * factor;
                }
            }
        }

        return result;
    }

    /// <summary>An averaging kernel (<c>fspecial3('average')</c>).</summary>
    public static Volume Average((int Rows, int Cols, int Planes) size)
    {
        var kernel = new Volume(size.Rows, size.Cols, size.Planes);
        Span<double> samples = kernel.Samples;
        double weight = 1.0 / samples.Length;
        samples.Fill(weight);
        return kernel;
    }

    /// <summary>A Gaussian kernel (<c>fspecial3('gaussian')</c>), separable but materialized in full.</summary>
    public static Volume Gaussian((int Rows, int Cols, int Planes) size, double sigma)
    {
        double[] down = Filters.Gaussian1D(sigma, size.Rows);
        double[] across = Filters.Gaussian1D(sigma, size.Cols);
        double[] through = Filters.Gaussian1D(sigma, size.Planes);
        var kernel = new Volume(size.Rows, size.Cols, size.Planes);
        for (int p = 0; p < size.Planes; p++)
        {
            for (int c = 0; c < size.Cols; c++)
            {
                for (int r = 0; r < size.Rows; r++)
                {
                    kernel[r, c, p] = down[r] * across[c] * through[p];
                }
            }
        }

        return kernel;
    }

    /// <summary>
    /// A 3-D Laplacian (<c>fspecial3('laplacian')</c>). The two shape parameters weight the twelve
    /// edge neighbours and the eight corner neighbours; at zero the kernel is the plain six-neighbour
    /// second difference, and raising them trades directional bias for noise sensitivity.
    /// </summary>
    public static Volume Laplacian(double gamma1 = 0, double gamma2 = 0)
    {
        if (gamma1 is < 0 or > 1 || gamma2 is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(gamma1), "fspecial3 laplacian shapes lie in [0, 1].");
        }

        var kernel = new Volume(3, 3, 3);
        double scale = 1.0 / (1 + gamma1 + gamma2);
        for (int p = 0; p < 3; p++)
        {
            for (int c = 0; c < 3; c++)
            {
                for (int r = 0; r < 3; r++)
                {
                    int distance = Math.Abs(r - 1) + Math.Abs(c - 1) + Math.Abs(p - 1);
                    double weight = distance switch
                    {
                        0 => -(6 + (12 * gamma1) + (8 * gamma2)),
                        1 => 1,
                        2 => gamma1,
                        _ => gamma2,
                    };
                    kernel[r, c, p] = scale * weight;
                }
            }
        }

        return kernel;
    }

    /// <summary>A Laplacian-of-Gaussian kernel (<c>fspecial3('log')</c>), zero-summed so a flat field answers zero.</summary>
    public static Volume LaplacianOfGaussian((int Rows, int Cols, int Planes) size, double sigma)
    {
        var kernel = new Volume(size.Rows, size.Cols, size.Planes);
        double cr = (size.Rows - 1) / 2.0;
        double cc = (size.Cols - 1) / 2.0;
        double cp = (size.Planes - 1) / 2.0;
        double twoSigmaSq = 2 * sigma * sigma;
        double sum = 0;
        for (int p = 0; p < size.Planes; p++)
        {
            for (int c = 0; c < size.Cols; c++)
            {
                for (int r = 0; r < size.Rows; r++)
                {
                    double dr = r - cr;
                    double dc = c - cc;
                    double dp = p - cp;
                    double rSq = (dr * dr) + (dc * dc) + (dp * dp);
                    double gaussian = Math.Exp(-rSq / twoSigmaSq);
                    kernel[r, c, p] = gaussian * ((rSq / (sigma * sigma)) - 3) / (sigma * sigma);
                    sum += kernel[r, c, p];
                }
            }
        }

        // The analytic form integrates to zero; a sampled one does not, and the residual is a constant
        // response to a constant field — the one thing a second-derivative filter must not have.
        Span<double> samples = kernel.Samples;
        double correction = sum / samples.Length;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] -= correction;
        }

        return kernel;
    }

    /// <summary>
    /// A Sobel or Prewitt derivative kernel (<c>fspecial3('sobel')</c>, <c>fspecial3('prewitt')</c>),
    /// differencing down the rows and smoothing across the other two axes.
    /// </summary>
    public static Volume Derivative(bool sobel)
    {
        double[] smooth = sobel ? [1, 2, 1] : [1, 1, 1];
        double[] difference = [1, 0, -1];
        var kernel = new Volume(3, 3, 3);
        for (int p = 0; p < 3; p++)
        {
            for (int c = 0; c < 3; c++)
            {
                for (int r = 0; r < 3; r++)
                {
                    kernel[r, c, p] = difference[r] * smooth[c] * smooth[p];
                }
            }
        }

        return kernel;
    }

    /// <summary>
    /// An ellipsoidal averaging kernel (<c>fspecial3('ellipsoid')</c>). Each voxel's weight is the
    /// share of it that falls inside the ellipsoid, estimated by supersampling, so the boundary is
    /// graded rather than stepped and the filter does not favour the axes.
    /// </summary>
    public static Volume Ellipsoid((double Rows, double Cols, double Planes) semiAxes)
    {
        if (semiAxes.Rows <= 0 || semiAxes.Cols <= 0 || semiAxes.Planes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(semiAxes), "fspecial3 ellipsoid semi-axes must be positive.");
        }

        int hr = (int)Math.Ceiling(semiAxes.Rows);
        int hc = (int)Math.Ceiling(semiAxes.Cols);
        int hp = (int)Math.Ceiling(semiAxes.Planes);
        var kernel = new Volume((2 * hr) + 1, (2 * hc) + 1, (2 * hp) + 1);
        const int Steps = 3;
        double total = 0;
        for (int p = -hp; p <= hp; p++)
        {
            for (int c = -hc; c <= hc; c++)
            {
                for (int r = -hr; r <= hr; r++)
                {
                    int inside = 0;
                    for (int sp = 0; sp < Steps; sp++)
                    {
                        for (int sc = 0; sc < Steps; sc++)
                        {
                            for (int sr = 0; sr < Steps; sr++)
                            {
                                double y = r + ((sr + 0.5) / Steps) - 0.5;
                                double x = c + ((sc + 0.5) / Steps) - 0.5;
                                double z = p + ((sp + 0.5) / Steps) - 0.5;
                                double norm = (y * y / (semiAxes.Rows * semiAxes.Rows))
                                    + (x * x / (semiAxes.Cols * semiAxes.Cols))
                                    + (z * z / (semiAxes.Planes * semiAxes.Planes));
                                if (norm <= 1)
                                {
                                    inside++;
                                }
                            }
                        }
                    }

                    double weight = inside / (double)(Steps * Steps * Steps);
                    kernel[r + hr, c + hc, p + hp] = weight;
                    total += weight;
                }
            }
        }

        Span<double> samples = kernel.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] /= total;
        }

        return kernel;
    }

    /// <summary>
    /// Remaps sample values (MATLAB <c>imadjustn</c>), the volume form of
    /// <see cref="PointOps.Adjust"/>: clip to the input window, apply the gamma, then stretch onto the
    /// output window.
    /// </summary>
    public static Volume Adjust(
        Volume volume, double lowIn, double highIn, double lowOut, double highOut, double gamma)
    {
        ArgumentNullException.ThrowIfNull(volume);
        if (highIn <= lowIn)
        {
            throw new ArgumentException("imadjustn needs the input window to have a positive width.");
        }

        var result = Volume.Like(volume);
        ReadOnlySpan<double> source = volume.Samples;
        Span<double> target = result.Samples;
        double span = highIn - lowIn;
        for (int i = 0; i < source.Length; i++)
        {
            double t = Math.Clamp((source[i] - lowIn) / span, 0, 1);
            if (gamma != 1)
            {
                t = Math.Pow(t, gamma);
            }

            target[i] = lowOut + (t * (highOut - lowOut));
        }

        GC.KeepAlive(volume);
        return result;
    }

    /// <summary>
    /// The lowest and highest sample values a fraction of the volume falls outside, the volume form of
    /// <see cref="PointOps.StretchLimits"/> — what <c>imadjustn</c> uses when the caller passes no
    /// input window.
    /// </summary>
    public static (double Low, double High) StretchLimits(
        Volume volume, double lowFraction = 0.01, double highFraction = 0.99, int bins = 256)
    {
        ArgumentNullException.ThrowIfNull(volume);
        double[] counts = Histogram(volume, bins);
        double total = 0;
        foreach (double count in counts)
        {
            total += count;
        }

        double low = 0;
        double high = 1;
        double running = 0;
        for (int i = 0; i < bins; i++)
        {
            running += counts[i];
            if (running > lowFraction * total)
            {
                low = i / (double)(bins - 1);
                break;
            }
        }

        running = 0;
        for (int i = bins - 1; i >= 0; i--)
        {
            running += counts[i];
            if (running > (1 - highFraction) * total)
            {
                high = i / (double)(bins - 1);
                break;
            }
        }

        return low < high ? (low, high) : (0, 1);
    }

    /// <summary>A sample histogram of the whole volume, on <paramref name="bins"/> even bins over [0, 1].</summary>
    public static double[] Histogram(Volume volume, int bins = 256)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        var counts = new double[bins];
        ReadOnlySpan<double> samples = volume.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            double sample = samples[i];
            if (double.IsNaN(sample))
            {
                continue;
            }

            counts[(int)Math.Round(Math.Clamp(sample, 0, 1) * (bins - 1))]++;
        }

        GC.KeepAlive(volume);
        return counts;
    }

    /// <summary>
    /// Remaps a volume so its histogram matches a reference volume's (MATLAB <c>imhistmatchn</c>).
    /// The mapping is built once from the two cumulative histograms and applied everywhere, so the
    /// answer does not depend on where in the volume a sample sits.
    /// </summary>
    public static Volume MatchHistogram(Volume volume, Volume reference, int bins = 64)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(reference);
        double[] target = Histogram(reference, bins);
        double[] transform = Histograms.MatchingTransform(
            Histogram(volume, bins), target, volume.SampleCount);
        var result = Volume.Like(volume);
        ReadOnlySpan<double> source = volume.Samples;
        Span<double> output = result.Samples;
        for (int i = 0; i < source.Length; i++)
        {
            int bin = (int)Math.Round(Math.Clamp(source[i], 0, 1) * (bins - 1));
            output[i] = transform[bin];
        }

        GC.KeepAlive(volume);
        return result;
    }

    /// <summary>
    /// The three directional gradients (MATLAB <c>imgradientxyz</c>). A positive component means the
    /// samples grow as that coordinate grows, matching <see cref="Gradients.GradientXY"/>.
    /// </summary>
    public static (Volume Gx, Volume Gy, Volume Gz) GradientXYZ(
        Volume volume, Gradients.Operator op = Gradients.Operator.Sobel)
    {
        ArgumentNullException.ThrowIfNull(volume);
        (double[] difference, double[] smooth) = DerivativePair(op);
        Volume gy = Separable(volume, difference, smooth, smooth);
        Volume gx = Separable(volume, smooth, difference, smooth);
        Volume gz = Separable(volume, smooth, smooth, difference);
        return (gx, gy, gz);
    }

    /// <summary>
    /// Gradient magnitude with the direction split into an azimuth in the xy-plane and an elevation
    /// out of it (MATLAB <c>imgradient3</c>), both in degrees. Two angles are the honest way to name a
    /// direction in three dimensions; one would have to throw a whole degree of freedom away.
    /// </summary>
    public static (Volume Magnitude, Volume Azimuth, Volume Elevation) Gradient(
        Volume volume, Gradients.Operator op = Gradients.Operator.Sobel)
    {
        (Volume gx, Volume gy, Volume gz) = GradientXYZ(volume, op);
        using (gx)
        using (gy)
        using (gz)
        {
            var magnitude = Volume.Like(volume);
            var azimuth = Volume.Like(volume);
            var elevation = Volume.Like(volume);
            ReadOnlySpan<double> x = gx.Samples;
            ReadOnlySpan<double> y = gy.Samples;
            ReadOnlySpan<double> z = gz.Samples;
            Span<double> m = magnitude.Samples;
            Span<double> a = azimuth.Samples;
            Span<double> e = elevation.Samples;
            for (int i = 0; i < m.Length; i++)
            {
                double flat = Math.Sqrt((x[i] * x[i]) + (y[i] * y[i]));
                m[i] = Math.Sqrt((flat * flat) + (z[i] * z[i]));

                // Rows increase downwards, so the row component is negated for the same y-up reading
                // imgradient gives.
                a[i] = Math.Atan2(-y[i], x[i]) * 180.0 / Math.PI;
                e[i] = Math.Atan2(z[i], flat) * 180.0 / Math.PI;
            }

            return (magnitude, azimuth, elevation);
        }
    }

    /// <summary>
    /// Finds surfaces in a volume (MATLAB <c>edge3</c>). The Sobel method thresholds the gradient
    /// magnitude; the approximate Canny method smooths first, keeps only samples that are a maximum
    /// along their own gradient direction, and then links weak edges to strong ones — the same three
    /// steps the 2-D detector takes, with the neighbour lookup interpolated in three dimensions
    /// instead of two.
    /// </summary>
    /// <param name="volume">The volume to search.</param>
    /// <param name="method">Which detector to run.</param>
    /// <param name="threshold">
    /// The gradient level an edge must reach, relative to the strongest gradient found. A pair sets
    /// the hysteresis window directly; a single number takes 40% of it as the lower bound.
    /// </param>
    /// <param name="sigma">The smoothing applied before the gradient, for the Canny method.</param>
    public static Volume Edge(
        Volume volume,
        EdgeMethod method,
        (double Low, double High) threshold,
        double sigma = 1.4142135623730951)
    {
        ArgumentNullException.ThrowIfNull(volume);
        Volume source = method == EdgeMethod.ApproxCanny
            ? GaussianBlur(volume, (sigma, sigma, sigma))
            : volume;
        try
        {
            (Volume gx, Volume gy, Volume gz) = GradientXYZ(source, Gradients.Operator.Sobel);
            using (gx)
            using (gy)
            using (gz)
            {
                var magnitude = Volume.Like(volume);
                Span<double> m = magnitude.Samples;
                ReadOnlySpan<double> x = gx.Samples;
                ReadOnlySpan<double> y = gy.Samples;
                ReadOnlySpan<double> z = gz.Samples;
                double peak = 0;
                for (int i = 0; i < m.Length; i++)
                {
                    m[i] = Math.Sqrt((x[i] * x[i]) + (y[i] * y[i]) + (z[i] * z[i]));
                    peak = Math.Max(peak, m[i]);
                }

                if (peak <= 0)
                {
                    return magnitude;
                }

                for (int i = 0; i < m.Length; i++)
                {
                    m[i] /= peak;
                }

                if (method == EdgeMethod.Sobel)
                {
                    using (magnitude)
                    {
                        return Threshold(magnitude, threshold.High);
                    }
                }

                using (magnitude)
                {
                    using Volume thin = Suppress(magnitude, gx, gy, gz);
                    return Hysteresis(thin, threshold.Low, threshold.High);
                }
            }
        }
        finally
        {
            if (!ReferenceEquals(source, volume))
            {
                source.Dispose();
            }
        }
    }

    // A 1-D filter run along one axis of the volume, which is where all the separable work happens.
    private static Volume Along(
        Volume volume, int axis, double[] kernel, Filters.Boundary boundary, double padValue)
    {
        int anchor = (kernel.Length - 1) / 2;
        var result = Volume.Like(volume);
        for (int p = 0; p < volume.Depth; p++)
        {
            for (int c = 0; c < volume.Width; c++)
            {
                for (int r = 0; r < volume.Height; r++)
                {
                    double sum = 0;
                    for (int k = 0; k < kernel.Length; k++)
                    {
                        int step = k - anchor;
                        double sample = axis switch
                        {
                            0 => volume.At(r + step, c, p, boundary, padValue),
                            1 => volume.At(r, c + step, p, boundary, padValue),
                            _ => volume.At(r, c, p + step, boundary, padValue),
                        };
                        sum += kernel[k] * sample;
                    }

                    result[r, c, p] = sum;
                }
            }
        }

        return result;
    }

    // The 1-D difference and smoothing pair a 3-D operator is the outer product of. Sobel and Prewitt
    // differ only in how hard they smooth; the two difference operators do not smooth at all, which is
    // what a script asks for when the smoothing would blur the step it is looking for.
    private static (double[] Difference, double[] Smooth) DerivativePair(Gradients.Operator op) => op switch
    {
        Gradients.Operator.Prewitt => ([-1, 0, 1], [1, 1, 1]),
        Gradients.Operator.Central => ([-0.5, 0, 0.5], [1]),
        Gradients.Operator.Intermediate => ([0, -1, 1], [1]),
        Gradients.Operator.Roberts => throw new ArgumentException(
            "the Roberts cross is a 2-D operator; imgradientxyz takes sobel, prewitt, central or intermediate."),
        _ => ([-1, 0, 1], [1, 2, 1]),
    };

    private static Volume Threshold(Volume magnitude, double level)
    {
        var result = Volume.Like(magnitude);
        ReadOnlySpan<double> source = magnitude.Samples;
        Span<double> target = result.Samples;
        for (int i = 0; i < source.Length; i++)
        {
            target[i] = source[i] >= level ? 1 : 0;
        }

        return result;
    }

    // Keeps only samples that are at least as large as the two neighbours either side of them along
    // their own gradient, sampled by trilinear interpolation because the gradient rarely points at a
    // neighbour exactly.
    private static Volume Suppress(Volume magnitude, Volume gx, Volume gy, Volume gz)
    {
        var result = Volume.Like(magnitude);
        for (int p = 0; p < magnitude.Depth; p++)
        {
            for (int c = 0; c < magnitude.Width; c++)
            {
                for (int r = 0; r < magnitude.Height; r++)
                {
                    double x = gx[r, c, p];
                    double y = gy[r, c, p];
                    double z = gz[r, c, p];
                    double length = Math.Sqrt((x * x) + (y * y) + (z * z));
                    if (length <= 0)
                    {
                        continue;
                    }

                    double here = magnitude[r, c, p];
                    double dr = y / length;
                    double dc = x / length;
                    double dp = z / length;
                    double ahead = Sample(magnitude, r + dr, c + dc, p + dp);
                    double behind = Sample(magnitude, r - dr, c - dc, p - dp);
                    if (here >= ahead && here >= behind)
                    {
                        result[r, c, p] = here;
                    }
                }
            }
        }

        return result;
    }

    private static double Sample(Volume volume, double r, double c, double p)
    {
        int r0 = (int)Math.Floor(r);
        int c0 = (int)Math.Floor(c);
        int p0 = (int)Math.Floor(p);
        double fr = r - r0;
        double fc = c - c0;
        double fp = p - p0;
        double total = 0;
        for (int dp = 0; dp <= 1; dp++)
        {
            for (int dc = 0; dc <= 1; dc++)
            {
                for (int dr = 0; dr <= 1; dr++)
                {
                    double weight = (dr == 0 ? 1 - fr : fr) * (dc == 0 ? 1 - fc : fc) * (dp == 0 ? 1 - fp : fp);
                    if (weight == 0)
                    {
                        continue;
                    }

                    total += weight * volume.At(
                        r0 + dr, c0 + dc, p0 + dp, Filters.Boundary.Replicate);
                }
            }
        }

        return total;
    }

    // Everything above the high level is an edge; everything above the low level that touches one
    // becomes an edge too. Without the second pass a single noisy sample can break an otherwise
    // continuous surface in half.
    private static Volume Hysteresis(Volume magnitude, double low, double high)
    {
        var result = Volume.Like(magnitude);
        var stack = new Stack<(int R, int C, int P)>();
        for (int p = 0; p < magnitude.Depth; p++)
        {
            for (int c = 0; c < magnitude.Width; c++)
            {
                for (int r = 0; r < magnitude.Height; r++)
                {
                    if (magnitude[r, c, p] >= high && result[r, c, p] == 0)
                    {
                        result[r, c, p] = 1;
                        stack.Push((r, c, p));
                    }
                }
            }
        }

        while (stack.Count > 0)
        {
            (int r, int c, int p) = stack.Pop();
            for (int dp = -1; dp <= 1; dp++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int nr = r + dr;
                        int nc = c + dc;
                        int np = p + dp;
                        if (nr < 0 || nr >= magnitude.Height || nc < 0 || nc >= magnitude.Width
                            || np < 0 || np >= magnitude.Depth)
                        {
                            continue;
                        }

                        if (result[nr, nc, np] == 0 && magnitude[nr, nc, np] >= low)
                        {
                            result[nr, nc, np] = 1;
                            stack.Push((nr, nc, np));
                        }
                    }
                }
            }
        }

        return result;
    }

    private static void RequireOdd((int Rows, int Cols, int Planes) size, string what)
    {
        if (size.Rows < 1 || size.Cols < 1 || size.Planes < 1)
        {
            throw new ArgumentException($"{what} needs a positive window size.");
        }

        if ((size.Rows % 2) == 0 || (size.Cols % 2) == 0 || (size.Planes % 2) == 0)
        {
            throw new ArgumentException($"{what} needs an odd window size, so the window has a centre.");
        }
    }
}
