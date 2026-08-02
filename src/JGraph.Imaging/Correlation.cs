using System.Numerics;

namespace JGraph.Imaging;

/// <summary>
/// Finding one picture inside another — normalized cross-correlation (<c>normxcorr2</c>) and
/// phase-correlation registration (<c>imregcorr</c>).
/// </summary>
/// <remarks>
/// <para>
/// Plain correlation cannot answer "where is this template" because it is dominated by brightness:
/// a featureless bright patch outscores a perfect match in shadow. Normalizing removes both the
/// local mean and the local contrast, so what is left measures shape alone and its value is
/// bounded by one. Lewis's insight was that the local sums this needs are running sums, so the
/// normalization costs nothing while the numerator goes through the Fourier domain.
/// </para>
/// <para>
/// Registration asks a different question — not where a small thing sits in a big one, but how two
/// whole pictures line up. Phase correlation throws away all the magnitudes and keeps only the
/// phase difference, whose inverse transform is a single sharp spike at the offset. Discarding the
/// magnitudes is what makes it robust: two pictures of the same scene under different lighting have
/// quite different spectra but nearly the same phase structure.
/// </para>
/// </remarks>
public static class Correlation
{
    /// <summary>
    /// The normalized cross-correlation of a template against a picture, as MATLAB's
    /// <c>normxcorr2</c> defines it.
    /// </summary>
    /// <param name="template">The thing being looked for.</param>
    /// <param name="image">The picture being searched.</param>
    /// <returns>
    /// A correlation surface of size <c>size(image) + size(template) - 1</c>, valued in [-1, 1]. Its
    /// peak sits at the template's bottom-right corner, because offset zero is the position where
    /// only that one corner overlaps.
    /// </returns>
    public static double[,] Normalized(double[,] template, double[,] image)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(image);
        int m = template.GetLength(0);
        int n = template.GetLength(1);
        int rows = image.GetLength(0);
        int cols = image.GetLength(1);
        if (m > rows || n > cols)
        {
            throw new ArgumentException(
                $"normxcorr2 needs a template no larger than the picture, but a {m}-by-{n} template " +
                $"was given for a {rows}-by-{cols} picture.", nameof(template));
        }

        int outRows = rows + m - 1;
        int outCols = cols + n - 1;

        // A window at output (0,0) covers only the picture's first pixel, so the picture is embedded
        // in a field padded by a whole template on every side and every window is full-sized.
        int height = rows + (2 * m) - 2;
        int width = cols + (2 * n) - 2;
        var field = new double[height * width];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                field[((r + m - 1) * width) + (c + n - 1)] = image[r, c];
            }
        }

        double templateMean = 0;
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                templateMean += template[r, c];
            }
        }

        templateMean /= m * (double)n;

        double templateEnergy = 0;
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double centred = template[r, c] - templateMean;
                templateEnergy += centred * centred;
            }
        }

        double[] raw = FourierGrid.Correlate(field, FourierGrid.Embed(template, height, width), height, width);
        double[] sum = Integral(field, height, width, square: false);
        double[] sumSquares = Integral(field, height, width, square: true);

        var result = new double[outRows, outCols];
        if (templateEnergy <= 0)
        {
            // A flat template has no shape to match, so every position correlates with it equally
            // badly. MATLAB answers zero rather than dividing by nothing.
            return result;
        }

        double count = m * (double)n;
        for (int u = 0; u < outRows; u++)
        {
            for (int v = 0; v < outCols; v++)
            {
                double window = Box(sum, width, u, v, m, n);
                double windowSquares = Box(sumSquares, width, u, v, m, n);
                double variance = windowSquares - (window * window / count);
                if (variance <= 0)
                {
                    continue;
                }

                double numerator = raw[(u * width) + v] - (templateMean * window);
                double value = numerator / Math.Sqrt(variance * templateEnergy);
                result[u, v] = Math.Clamp(value, -1.0, 1.0);
            }
        }

        return result;
    }

    /// <summary>
    /// The offset that carries <paramref name="moving"/> onto <paramref name="fixedImage"/>, found by
    /// phase correlation.
    /// </summary>
    /// <param name="moving">The picture to be moved, row-major.</param>
    /// <param name="fixedImage">The picture to line up with, row-major.</param>
    /// <param name="height">The rows in both.</param>
    /// <param name="width">The columns in both.</param>
    /// <returns>The shift in columns and rows, and the height of the peak that decided it.</returns>
    public static (double DeltaX, double DeltaY, double Peak) PhaseShift(
        ReadOnlySpan<double> moving, ReadOnlySpan<double> fixedImage, int height, int width)
    {
        Complex[] a = FourierGrid.Forward(fixedImage, height, width);
        Complex[] b = FourierGrid.Forward(moving, height, width);
        for (int i = 0; i < a.Length; i++)
        {
            Complex cross = a[i] * Complex.Conjugate(b[i]);
            double magnitude = cross.Magnitude;

            // Only the phase carries the offset; keeping the magnitude would let one strong feature
            // outvote the agreement of everything else.
            a[i] = magnitude > 1e-12 ? cross / magnitude : Complex.Zero;
        }

        FourierGrid.Transform(a, height, width, inverse: true);

        int peak = 0;
        double best = double.NegativeInfinity;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Real > best)
            {
                best = a[i].Real;
                peak = i;
            }
        }

        int peakRow = peak / width;
        int peakCol = peak % width;

        // A shift of more than half the picture is really a smaller shift the other way: the
        // transform wraps, and the shorter reading is the one that means anything.
        double dy = peakRow > height / 2 ? peakRow - height : peakRow;
        double dx = peakCol > width / 2 ? peakCol - width : peakCol;
        return (dx, dy, best);
    }

    /// <summary>
    /// Estimates the similarity transform carrying <paramref name="moving"/> onto
    /// <paramref name="fixedImage"/>.
    /// </summary>
    /// <param name="moving">The picture to be registered.</param>
    /// <param name="fixedImage">The reference picture.</param>
    /// <param name="allowRotation">Whether a rotation may be recovered as well as a translation.</param>
    /// <param name="allowScale">Whether a uniform scale may be recovered too.</param>
    /// <returns>The scale, the rotation in degrees, the translation, and the peak that chose them.</returns>
    public static (double Scale, double RotationDegrees, double DeltaX, double DeltaY, double Peak) Register(
        double[,] moving, double[,] fixedImage, bool allowRotation, bool allowScale)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(fixedImage);
        int height = Math.Max(moving.GetLength(0), fixedImage.GetLength(0));
        int width = Math.Max(moving.GetLength(1), fixedImage.GetLength(1));

        double[] movingField = FourierGrid.Embed(moving, height, width);
        double[] fixedField = FourierGrid.Embed(fixedImage, height, width);

        if (!allowRotation && !allowScale)
        {
            (double dx, double dy, double peak) = PhaseShift(movingField, fixedField, height, width);
            return (1.0, 0.0, dx, dy, peak);
        }

        (double angle, double scale) = PolarMatch(movingField, fixedField, height, width, allowScale);

        // The magnitude spectrum is symmetric through the origin, so the log-polar match cannot tell
        // a rotation from the same rotation turned half a circle further. Try both and keep whichever
        // actually lines the pictures up.
        (double Scale, double RotationDegrees, double DeltaX, double DeltaY, double Peak) best = default;
        double bestPeak = double.NegativeInfinity;
        foreach (double candidate in new[] { angle, angle + 180.0 })
        {
            double[] adjusted = Resample(moving, height, width, scale, candidate);
            (double dx, double dy, double peak) = PhaseShift(adjusted, fixedField, height, width);
            if (peak > bestPeak)
            {
                bestPeak = peak;
                best = (scale, Normalize(candidate), dx, dy, peak);
            }
        }

        return best;
    }

    /// <summary>
    /// Matches the two pictures' spectra in log-polar coordinates, where a rotation becomes a shift
    /// along one axis and a scale becomes a shift along the other.
    /// </summary>
    private static (double AngleDegrees, double Scale) PolarMatch(
        ReadOnlySpan<double> moving, ReadOnlySpan<double> fixedImage, int height, int width, bool allowScale)
    {
        double[] movingSpectrum = LogPolarSpectrum(moving, height, width, out int angleBins, out int radiusBins);
        double[] fixedSpectrum = LogPolarSpectrum(fixedImage, height, width, out _, out _);

        (double dx, double dy, _) = PhaseShift(movingSpectrum, fixedSpectrum, angleBins, radiusBins);

        // Rows are angle, columns are log radius.
        double angle = dy * 180.0 / angleBins;
        if (!allowScale)
        {
            return (angle, 1.0);
        }

        double maxRadius = Math.Min(height, width) / 2.0;
        double logBase = Math.Log(maxRadius) / radiusBins;
        return (angle, Math.Exp(-dx * logBase));
    }

    private static double[] LogPolarSpectrum(
        ReadOnlySpan<double> values, int height, int width, out int angleBins, out int radiusBins)
    {
        // A rectangular window leaks a bright cross along both axes of the spectrum, which would
        // dominate any polar match; tapering the edges to zero removes it.
        var windowed = new double[height * width];
        for (int r = 0; r < height; r++)
        {
            double rowTaper = Hann(r, height);
            for (int c = 0; c < width; c++)
            {
                windowed[(r * width) + c] = values[(r * width) + c] * rowTaper * Hann(c, width);
            }
        }

        Complex[] spectrum = FourierGrid.Forward(windowed, height, width);
        var magnitude = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; i++)
        {
            // The log compresses a range that spans several decades into one a correlation can see
            // past the DC term.
            magnitude[i] = Math.Log(1 + spectrum[i].Magnitude);
        }

        double[] centred = FourierGrid.Shift(magnitude, height, width);

        // Even after the log, a natural picture's spectrum is a mound around the origin, and a mound
        // looks the same whichever way you turn it. Reddy and Chatterji's emphasis filter tilts the
        // weight towards the higher frequencies, where the orientation actually lives.
        for (int r = 0; r < height; r++)
        {
            double eta = ((r / (double)height) - 0.5) * Math.PI;
            for (int c = 0; c < width; c++)
            {
                double xi = ((c / (double)width) - 0.5) * Math.PI;
                double cross = Math.Cos(eta) * Math.Cos(xi);
                centred[(r * width) + c] *= (1 - cross) * (2 - cross);
            }
        }

        angleBins = 180;
        radiusBins = 128;
        double centreRow = height / 2.0;
        double centreCol = width / 2.0;
        double maxRadius = Math.Min(height, width) / 2.0;
        double logBase = Math.Log(maxRadius) / radiusBins;

        var polar = new double[angleBins * radiusBins];
        for (int a = 0; a < angleBins; a++)
        {
            double theta = a * Math.PI / angleBins;
            double cos = Math.Cos(theta);
            double sin = Math.Sin(theta);
            for (int k = 0; k < radiusBins; k++)
            {
                double radius = Math.Exp(k * logBase);
                double row = centreRow + (radius * sin);
                double col = centreCol + (radius * cos);
                polar[(a * radiusBins) + k] = Sample(centred, height, width, row, col);
            }
        }

        return polar;
    }

    private static double[] Resample(double[,] image, int height, int width, double scale, double angleDegrees)
    {
        int rows = image.GetLength(0);
        int cols = image.GetLength(1);
        double radians = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double centreRow = (rows - 1) / 2.0;
        double centreCol = (cols - 1) / 2.0;

        var flat = new double[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                flat[(r * cols) + c] = image[r, c];
            }
        }

        // Pull each output pixel from where it came from, which is the forward map run backwards.
        var result = new double[height * width];
        for (int r = 0; r < rows; r++)
        {
            double y = r - centreRow;
            for (int c = 0; c < cols; c++)
            {
                double x = c - centreCol;
                double sourceX = ((x * cos) + (y * sin)) / scale;
                double sourceY = ((-x * sin) + (y * cos)) / scale;
                result[(r * width) + c] =
                    Sample(flat, rows, cols, sourceY + centreRow, sourceX + centreCol);
            }
        }

        return result;
    }

    private static double Hann(int i, int n) =>
        n <= 1 ? 1.0 : 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));

    private static double Normalize(double degrees)
    {
        double angle = degrees % 360.0;
        if (angle > 180.0)
        {
            angle -= 360.0;
        }
        else if (angle <= -180.0)
        {
            angle += 360.0;
        }

        return angle;
    }

    private static double Sample(ReadOnlySpan<double> grid, int height, int width, double row, double col)
    {
        int r0 = (int)Math.Floor(row);
        int c0 = (int)Math.Floor(col);
        double fr = row - r0;
        double fc = col - c0;
        double total = 0;
        for (int dr = 0; dr <= 1; dr++)
        {
            for (int dc = 0; dc <= 1; dc++)
            {
                int r = r0 + dr;
                int c = c0 + dc;
                if (r < 0 || r >= height || c < 0 || c >= width)
                {
                    continue;
                }

                double weight = (dr == 0 ? 1 - fr : fr) * (dc == 0 ? 1 - fc : fc);
                total += weight * grid[(r * width) + c];
            }
        }

        return total;
    }

    /// <summary>
    /// The summed-area table: entry (r, c) holds the total of everything above and to the left, so
    /// any rectangle's sum is four lookups whatever its size.
    /// </summary>
    private static double[] Integral(ReadOnlySpan<double> values, int height, int width, bool square)
    {
        var table = new double[(height + 1) * (width + 1)];
        for (int r = 0; r < height; r++)
        {
            double running = 0;
            for (int c = 0; c < width; c++)
            {
                double value = values[(r * width) + c];
                running += square ? value * value : value;
                table[((r + 1) * (width + 1)) + c + 1] = table[(r * (width + 1)) + c + 1] + running;
            }
        }

        return table;
    }

    private static double Box(double[] table, int width, int row, int col, int m, int n)
    {
        int stride = width + 1;
        return table[((row + m) * stride) + col + n]
             - table[(row * stride) + col + n]
             - table[((row + m) * stride) + col]
             + table[(row * stride) + col];
    }
}
