namespace JGraph.Imaging;

/// <summary>
/// The standard (rho, theta) Hough transform for straight lines in a binary image, plus peak finding
/// and segment extraction — MATLAB's <c>hough</c>, <c>houghpeaks</c>, and <c>houghlines</c>.
/// </summary>
/// <remarks>
/// A pixel at 0-based (x, y) votes for every line satisfying <c>rho = x·cos θ + y·sin θ</c>. Theta runs
/// over −90°…89° in 1° steps and rho over −D…D in 1-pixel steps, where D is the image diagonal, so the
/// accumulator is indexed [rho, theta] exactly as MATLAB's is.
/// </remarks>
public static class HoughTransform
{
    /// <summary>One extracted line segment (MATLAB <c>houghlines</c>), with 0-based endpoints.</summary>
    public readonly record struct LineSegment(
        double Point1X, double Point1Y, double Point2X, double Point2Y, double Theta, double Rho);

    /// <summary>
    /// Accumulates votes from every nonzero pixel of a binary image. The counts come back as an image
    /// (rows = rho, columns = theta) so large accumulators stay on the tiered buffers; normalize with
    /// <see cref="PointOps.Normalize"/> before displaying one.
    /// </summary>
    public static (ImageBuffer Accumulator, double[] Theta, double[] Rho) Accumulate(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels != 1)
        {
            throw new ArgumentException("hough needs a binary (single-channel) image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        int diagonal = (int)Math.Ceiling(Math.Sqrt(((double)h * h) + ((double)w * w)));

        var theta = new double[180];
        var cos = new double[180];
        var sin = new double[180];
        for (int t = 0; t < 180; t++)
        {
            theta[t] = t - 90;
            double radians = theta[t] * Math.PI / 180.0;
            cos[t] = Math.Cos(radians);
            sin[t] = Math.Sin(radians);
        }

        var rho = new double[(2 * diagonal) + 1];
        for (int i = 0; i < rho.Length; i++)
        {
            rho[i] = i - diagonal;
        }

        var accumulator = new ImageBuffer(rho.Length, theta.Length, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (image[r, c, 0] == 0)
                {
                    continue;
                }

                double x = c;
                double y = r;
                for (int t = 0; t < 180; t++)
                {
                    int index = (int)Math.Round((x * cos[t]) + (y * sin[t])) + diagonal;
                    if ((uint)index < (uint)rho.Length)
                    {
                        accumulator[index, t, 0] += 1.0;
                    }
                }
            }
        }

        return (accumulator, theta, rho);
    }

    /// <summary>
    /// Finds up to <paramref name="count"/> accumulator peaks (MATLAB <c>houghpeaks</c>): the largest
    /// cells at or above <paramref name="threshold"/> (default half the maximum), suppressing a
    /// neighbourhood around each one as it is taken. Returns 0-based (rho, theta) indices, strongest first.
    /// </summary>
    public static (int RhoIndex, int ThetaIndex)[] Peaks(
        ImageBuffer accumulator, int count = 1, double? threshold = null, int? neighbourhood = null)
    {
        ArgumentNullException.ThrowIfNull(accumulator);
        int rows = accumulator.Height;
        int cols = accumulator.Width;

        double max = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                max = Math.Max(max, accumulator[r, c, 0]);
            }
        }

        double level = threshold ?? (0.5 * max);
        // MATLAB's default neighbourhood is about 1/50 of the accumulator, rounded up to an odd size.
        int halfRho = (neighbourhood ?? OddAtLeastThree(rows / 50)) / 2;
        int halfTheta = (neighbourhood ?? OddAtLeastThree(cols / 50)) / 2;

        var taken = new bool[rows, cols];
        var peaks = new List<(int RhoIndex, int ThetaIndex)>();
        for (int n = 0; n < count; n++)
        {
            double best = 0;
            int bestR = -1;
            int bestC = -1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double v = accumulator[r, c, 0];
                    if (!taken[r, c] && v > best && v >= level)
                    {
                        best = v;
                        bestR = r;
                        bestC = c;
                    }
                }
            }

            if (bestR < 0)
            {
                break;
            }

            peaks.Add((bestR, bestC));
            for (int dr = -halfRho; dr <= halfRho; dr++)
            {
                for (int dt = -halfTheta; dt <= halfTheta; dt++)
                {
                    int r = bestR + dr;
                    int c = bestC + dt;

                    // Theta wraps at ±90°: (rho, theta) and (-rho, theta ± 180°) name the same line, so
                    // a neighbourhood running off one end continues at the other with rho reflected.
                    if (c < 0)
                    {
                        c += cols;
                        r = rows - 1 - r;
                    }
                    else if (c >= cols)
                    {
                        c -= cols;
                        r = rows - 1 - r;
                    }

                    if ((uint)r < (uint)rows)
                    {
                        taken[r, c] = true;
                    }
                }
            }
        }

        return peaks.ToArray();
    }

    /// <summary>
    /// Extracts line segments for the given peaks (MATLAB <c>houghlines</c>): the image pixels voting for
    /// each peak are walked in order along the line, split wherever the gap exceeds
    /// <paramref name="fillGap"/> pixels, and runs shorter than <paramref name="minLength"/> are dropped.
    /// </summary>
    public static LineSegment[] Lines(
        ImageBuffer image, double[] theta, double[] rho, (int RhoIndex, int ThetaIndex)[] peaks,
        double fillGap = 20, double minLength = 40)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(theta);
        ArgumentNullException.ThrowIfNull(rho);
        ArgumentNullException.ThrowIfNull(peaks);

        var segments = new List<LineSegment>();
        foreach ((int rhoIndex, int thetaIndex) in peaks)
        {
            if ((uint)rhoIndex >= (uint)rho.Length || (uint)thetaIndex >= (uint)theta.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(peaks), "a peak index is outside the accumulator.");
            }

            double angle = theta[thetaIndex] * Math.PI / 180.0;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double target = rho[rhoIndex];

            // Collect the voting pixels, ordered by their distance along the line's own direction.
            var points = new List<(double Along, double X, double Y)>();
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    if (image[r, c, 0] == 0)
                    {
                        continue;
                    }

                    double x = c;
                    double y = r;
                    if (Math.Round((x * cos) + (y * sin)) == target)
                    {
                        points.Add(((x * -sin) + (y * cos), x, y));
                    }
                }
            }

            if (points.Count == 0)
            {
                continue;
            }

            points.Sort((a, b) => a.Along.CompareTo(b.Along));

            int start = 0;
            for (int i = 1; i <= points.Count; i++)
            {
                bool broken = i == points.Count || points[i].Along - points[i - 1].Along > fillGap;
                if (!broken)
                {
                    continue;
                }

                (double _, double x1, double y1) = points[start];
                (double _, double x2, double y2) = points[i - 1];
                double length = Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));
                if (length >= minLength)
                {
                    segments.Add(new LineSegment(x1, y1, x2, y2, theta[thetaIndex], target));
                }

                start = i;
            }
        }

        return segments.ToArray();
    }

    private static int OddAtLeastThree(int value)
    {
        int size = Math.Max(3, value);
        return size % 2 == 0 ? size + 1 : size;
    }
}
