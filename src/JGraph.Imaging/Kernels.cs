namespace JGraph.Imaging;

/// <summary>Standard filter kernels (MATLAB <c>fspecial</c>).</summary>
public static class Kernels
{
    /// <summary>An <paramref name="size"/>×<paramref name="size"/> averaging kernel that sums to 1.</summary>
    public static double[,] Average(int size = 3) => Average(size, size);

    /// <summary>A rows×cols averaging kernel that sums to 1 (MATLAB <c>fspecial('average', [m n])</c>).</summary>
    public static double[,] Average(int rows, int cols)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        var kernel = new double[rows, cols];
        double value = 1.0 / ((double)rows * cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                kernel[r, c] = value;
            }
        }

        return kernel;
    }

    /// <summary>A rotationally-symmetric Gaussian kernel of the given size and standard deviation, normalized to sum 1.</summary>
    public static double[,] Gaussian(int size = 3, double sigma = 0.5) => Gaussian(size, size, sigma);

    /// <summary>A rows×cols Gaussian kernel (MATLAB <c>fspecial('gaussian', [m n], sigma)</c>), normalized to sum 1.</summary>
    public static double[,] Gaussian(int rows, int cols, double sigma)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        if (sigma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "gaussian sigma must be positive.");
        }

        var kernel = new double[rows, cols];
        double centerR = (rows - 1) / 2.0;
        double centerC = (cols - 1) / 2.0;
        double sum = 0;
        double twoSigmaSq = 2 * sigma * sigma;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double dy = r - centerR;
                double dx = c - centerC;
                double value = Math.Exp(-((dx * dx) + (dy * dy)) / twoSigmaSq);
                kernel[r, c] = value;
                sum += value;
            }
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                kernel[r, c] /= sum;
            }
        }

        return kernel;
    }

    /// <summary>
    /// The 3×3 unsharp contrast-enhancement kernel (MATLAB <c>fspecial('unsharp', alpha)</c>): the
    /// identity minus a Laplacian, so filtering with it sharpens in one pass.
    /// </summary>
    public static double[,] Unsharp(double alpha = 0.2)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        double scale = 1.0 / (alpha + 1.0);
        double corner = -alpha * scale;
        double edge = (alpha - 1.0) * scale;
        double centre = (alpha + 5.0) * scale;
        return new double[,]
        {
            { corner, edge, corner },
            { edge, centre, edge },
            { corner, edge, corner },
        };
    }

    /// <summary>
    /// A linear-motion blur kernel (MATLAB <c>fspecial('motion', len, theta)</c>): the point spread of
    /// a camera moved <paramref name="length"/> pixels at <paramref name="theta"/> degrees
    /// counter-clockwise, anti-aliased by how far each pixel centre lies from the swept line.
    /// </summary>
    /// <param name="length">Displacement in pixels.</param>
    /// <param name="theta">Direction in degrees, counter-clockwise from horizontal.</param>
    public static double[,] Motion(double length = 9, double theta = 0)
    {
        const double lineWidth = 1.0;
        const double eps = 2.220446049250313e-16;
        double len = Math.Max(1.0, length);
        double half = (len - 1) / 2.0;

        double phiDegrees = theta % 180.0;
        if (phiDegrees < 0)
        {
            phiDegrees += 180.0;
        }

        double phi = phiDegrees * Math.PI / 180.0;
        double cosphi = Math.Cos(phi);
        double sinphi = Math.Sin(phi);
        int xsign = cosphi < 0 ? -1 : 1;

        // The kernel is built one quadrant at a time and then unfolded, so only the sweep's positive
        // half is measured. The epsilon nudge is MATLAB's, and it is what keeps the exactly-horizontal
        // and exactly-vertical cases from picking up a spurious extra column.
        int sx = (int)Math.Truncate((half * cosphi) + (lineWidth * xsign) - (len * eps));
        int sy = (int)Math.Truncate((half * sinphi) + lineWidth - (len * eps));
        int nx = Math.Abs(sx) + 1;
        int ny = sy + 1;

        var distance = new double[ny, nx];
        for (int i = 0; i < ny; i++)
        {
            for (int j = 0; j < nx; j++)
            {
                double x = j * xsign;
                double y = i;
                double toLine = (y * cosphi) - (x * sinphi);
                double radius = Math.Sqrt((x * x) + (y * y));
                if (radius >= half && Math.Abs(toLine) <= lineWidth)
                {
                    // Past the end of the sweep the distance is measured to the end point itself, not
                    // to the infinite line, which is what rounds the kernel's ends off.
                    double alongLine = half - Math.Abs((x + (toLine * sinphi)) / cosphi);
                    toLine = Math.Sqrt((toLine * toLine) + (alongLine * alongLine));
                }

                distance[i, j] = Math.Max(0.0, lineWidth + eps - Math.Abs(toLine));
            }
        }

        int height = (2 * ny) - 1;
        int width = (2 * nx) - 1;
        var kernel = new double[height, width];
        double sum = 0;
        for (int i = 0; i < ny; i++)
        {
            for (int j = 0; j < nx; j++)
            {
                double value = distance[i, j];
                kernel[ny - 1 - i, nx - 1 - j] = value; // the rotated half
                kernel[ny - 1 + i, nx - 1 + j] = value; // the measured half
            }
        }

        foreach (double value in kernel)
        {
            sum += value;
        }

        sum += eps * len * len;
        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                // MATLAB flips the result when the sweep points rightwards, so that positive theta
                // reads counter-clockwise in image coordinates where rows increase downwards.
                int sourceRow = cosphi > 0 ? height - 1 - r : r;
                result[r, c] = kernel[sourceRow, c] / sum;
            }
        }

        return result;
    }

    /// <summary>The 3×3 Sobel horizontal-gradient kernel (transpose it for vertical).</summary>
    public static double[,] Sobel() => new double[,]
    {
        { 1, 2, 1 },
        { 0, 0, 0 },
        { -1, -2, -1 },
    };

    /// <summary>The 3×3 Prewitt horizontal-gradient kernel (transpose it for vertical).</summary>
    public static double[,] Prewitt() => new double[,]
    {
        { 1, 1, 1 },
        { 0, 0, 0 },
        { -1, -1, -1 },
    };

    /// <summary>The 2×2 Roberts cross kernel (pair it with <see cref="RobertsCounter"/> for the other diagonal).</summary>
    public static double[,] Roberts() => new double[,]
    {
        { 1, 0 },
        { 0, -1 },
    };

    /// <summary>The 2×2 Roberts kernel for the anti-diagonal.</summary>
    public static double[,] RobertsCounter() => new double[,]
    {
        { 0, 1 },
        { -1, 0 },
    };

    /// <summary>A 3×3 Laplacian kernel; <paramref name="alpha"/> in [0, 1] shapes the diagonal weighting (MATLAB default 0.2).</summary>
    public static double[,] Laplacian(double alpha = 0.2)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        double a = alpha / (alpha + 1);
        double b = (1 - alpha) / (alpha + 1);
        double center = -4 / (alpha + 1);
        return new double[,]
        {
            { a, b, a },
            { b, center, b },
            { a, b, a },
        };
    }

    /// <summary>A circular averaging (pillbox) kernel of the given radius, normalized to sum 1.</summary>
    public static double[,] Disk(int radius = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        int size = (2 * radius) + 1;
        var kernel = new double[size, size];
        double sum = 0;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double dy = r - radius;
                double dx = c - radius;
                if ((dx * dx) + (dy * dy) <= (double)radius * radius)
                {
                    kernel[r, c] = 1;
                    sum++;
                }
            }
        }

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                kernel[r, c] /= sum;
            }
        }

        return kernel;
    }

    /// <summary>A Laplacian-of-Gaussian kernel of the given size and sigma (zero-sum edge detector).</summary>
    public static double[,] LaplacianOfGaussian(int size = 5, double sigma = 0.5) =>
        LaplacianOfGaussian(size, size, sigma);

    /// <summary>A rows×cols Laplacian-of-Gaussian kernel (MATLAB <c>fspecial('log', [m n], sigma)</c>).</summary>
    public static double[,] LaplacianOfGaussian(int rows, int cols, double sigma)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        if (sigma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "log sigma must be positive.");
        }

        var kernel = new double[rows, cols];
        double centerR = (rows - 1) / 2.0;
        double centerC = (cols - 1) / 2.0;
        double sigma2 = sigma * sigma;
        double sum = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double dy = r - centerR;
                double dx = c - centerC;
                double rr = (dx * dx) + (dy * dy);
                double gauss = Math.Exp(-rr / (2 * sigma2));
                double value = (rr - (2 * sigma2)) / (sigma2 * sigma2) * gauss;
                kernel[r, c] = value;
                sum += value;
            }
        }

        // Remove the DC component so the kernel sums to zero (a pure second-derivative operator).
        double mean = sum / ((double)rows * cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                kernel[r, c] -= mean;
            }
        }

        return kernel;
    }
}
