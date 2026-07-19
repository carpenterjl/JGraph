namespace JGraph.Imaging;

/// <summary>Edge detectors producing binary (0/1) images: Sobel, Prewitt, and Canny.</summary>
public static class EdgeDetection
{
    /// <summary>The available edge-detection methods.</summary>
    public enum Method
    {
        /// <summary>Sobel gradient magnitude thresholding.</summary>
        Sobel,

        /// <summary>Prewitt gradient magnitude thresholding.</summary>
        Prewitt,

        /// <summary>Canny (Gaussian smoothing, non-max suppression, hysteresis).</summary>
        Canny,
    }

    /// <summary>
    /// Detects edges in a grayscale image, returning a binary image (MATLAB <c>edge</c>). For Sobel and
    /// Prewitt, <paramref name="threshold"/> overrides the automatic 4×mean-magnitude heuristic; for
    /// Canny it is the high hysteresis threshold (low = 0.4×high).
    /// </summary>
    public static ImageBuffer Detect(ImageBuffer image, Method method, double? threshold = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageBuffer gray = image.Channels == 1 ? image : PointOps.ToGray(image);
        try
        {
            return method == Method.Canny
                ? Canny(gray, threshold)
                : Gradient(gray, method, threshold);
        }
        finally
        {
            if (!ReferenceEquals(gray, image))
            {
                gray.Dispose();
            }
        }
    }

    private static ImageBuffer Gradient(ImageBuffer gray, Method method, double? threshold)
    {
        double[,] kx = method == Method.Prewitt ? Kernels.Prewitt() : Kernels.Sobel();
        double[,] ky = Transpose(kx);
        (double[,] magnitude, _) = GradientMagnitude(gray, kx, ky);

        double level = threshold ?? (4.0 * Mean(magnitude));
        return Threshold(gray.Height, gray.Width, magnitude, level);
    }

    private static ImageBuffer Canny(ImageBuffer gray, double? highThreshold)
    {
        using ImageBuffer smoothed = Filters.Correlate(gray, Kernels.Gaussian(5, Math.Sqrt(2)), Filters.Boundary.Replicate);
        double[,] kx = Kernels.Sobel();
        double[,] ky = Transpose(kx);
        (double[,] magnitude, double[,] direction) = GradientMagnitude(smoothed, kx, ky);

        int h = gray.Height;
        int w = gray.Width;
        var suppressed = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double angle = direction[r, c];
                (int dr, int dc) = QuantizeDirection(angle);
                double m = magnitude[r, c];
                double a = Neighbour(magnitude, r + dr, c + dc, h, w);
                double b = Neighbour(magnitude, r - dr, c - dc, h, w);
                suppressed[r, c] = (m >= a && m >= b) ? m : 0.0;
            }
        }

        double high = highThreshold ?? PercentileThreshold(suppressed, 0.7);
        if (high <= 0)
        {
            return new ImageBuffer(h, w, 1); // no gradient anywhere → no edges
        }

        double low = 0.4 * high;
        return Hysteresis(suppressed, high, low);
    }

    private static (double[,] Magnitude, double[,] Direction) GradientMagnitude(ImageBuffer gray, double[,] kx, double[,] ky)
    {
        using ImageBuffer gxImage = Filters.Correlate(gray, kx, Filters.Boundary.Replicate);
        using ImageBuffer gyImage = Filters.Correlate(gray, ky, Filters.Boundary.Replicate);
        int h = gray.Height;
        int w = gray.Width;
        var magnitude = new double[h, w];
        var direction = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double gx = gxImage[r, c, 0];
                double gy = gyImage[r, c, 0];
                magnitude[r, c] = Math.Sqrt((gx * gx) + (gy * gy));
                direction[r, c] = Math.Atan2(gy, gx);
            }
        }

        return (magnitude, direction);
    }

    private static ImageBuffer Threshold(int h, int w, double[,] magnitude, double level)
    {
        var edges = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                edges[r, c, 0] = magnitude[r, c] >= level ? 1.0 : 0.0;
            }
        }

        return edges;
    }

    private static ImageBuffer Hysteresis(double[,] suppressed, double high, double low)
    {
        int h = suppressed.GetLength(0);
        int w = suppressed.GetLength(1);
        var edges = new ImageBuffer(h, w, 1);
        var queue = new Queue<(int R, int C)>();

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (suppressed[r, c] >= high)
                {
                    edges[r, c, 0] = 1.0;
                    queue.Enqueue((r, c));
                }
            }
        }

        while (queue.Count > 0)
        {
            (int r, int c) = queue.Dequeue();
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int nr = r + dr;
                    int nc = c + dc;
                    if ((uint)nr >= (uint)h || (uint)nc >= (uint)w || edges[nr, nc, 0] == 1.0)
                    {
                        continue;
                    }

                    if (suppressed[nr, nc] >= low)
                    {
                        edges[nr, nc, 0] = 1.0;
                        queue.Enqueue((nr, nc));
                    }
                }
            }
        }

        return edges;
    }

    private static (int Dr, int Dc) QuantizeDirection(double angle)
    {
        double degrees = angle * 180.0 / Math.PI;
        if (degrees < 0)
        {
            degrees += 180;
        }

        // Four sectors: 0° (horizontal gradient → compare left/right), 45°, 90°, 135°.
        if (degrees < 22.5 || degrees >= 157.5)
        {
            return (0, 1);
        }

        if (degrees < 67.5)
        {
            return (1, 1);
        }

        if (degrees < 112.5)
        {
            return (1, 0);
        }

        return (1, -1);
    }

    private static double Neighbour(double[,] values, int r, int c, int h, int w) =>
        (uint)r < (uint)h && (uint)c < (uint)w ? values[r, c] : 0.0;

    private static double Mean(double[,] values)
    {
        double sum = 0;
        foreach (double v in values)
        {
            sum += v;
        }

        return sum / values.Length;
    }

    private static double PercentileThreshold(double[,] values, double fraction)
    {
        var flat = new List<double>();
        foreach (double v in values)
        {
            if (v > 0)
            {
                flat.Add(v);
            }
        }

        if (flat.Count == 0)
        {
            return 0;
        }

        flat.Sort();
        int index = Math.Clamp((int)(fraction * flat.Count), 0, flat.Count - 1);
        return flat[index];
    }

    private static double[,] Transpose(double[,] kernel)
    {
        int h = kernel.GetLength(0);
        int w = kernel.GetLength(1);
        var result = new double[w, h];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                result[c, r] = kernel[r, c];
            }
        }

        return result;
    }
}
