namespace JGraph.Imaging;

/// <summary>Standard filter kernels (MATLAB <c>fspecial</c>).</summary>
public static class Kernels
{
    /// <summary>An <paramref name="size"/>×<paramref name="size"/> averaging kernel that sums to 1.</summary>
    public static double[,] Average(int size = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var kernel = new double[size, size];
        double value = 1.0 / (size * size);
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                kernel[r, c] = value;
            }
        }

        return kernel;
    }

    /// <summary>A rotationally-symmetric Gaussian kernel of the given size and standard deviation, normalized to sum 1.</summary>
    public static double[,] Gaussian(int size = 3, double sigma = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (sigma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "gaussian sigma must be positive.");
        }

        var kernel = new double[size, size];
        double center = (size - 1) / 2.0;
        double sum = 0;
        double twoSigmaSq = 2 * sigma * sigma;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double dy = r - center;
                double dx = c - center;
                double value = Math.Exp(-((dx * dx) + (dy * dy)) / twoSigmaSq);
                kernel[r, c] = value;
                sum += value;
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
    public static double[,] LaplacianOfGaussian(int size = 5, double sigma = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (sigma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "log sigma must be positive.");
        }

        var kernel = new double[size, size];
        double center = (size - 1) / 2.0;
        double sigma2 = sigma * sigma;
        double sum = 0;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double dy = r - center;
                double dx = c - center;
                double rr = (dx * dx) + (dy * dy);
                double gauss = Math.Exp(-rr / (2 * sigma2));
                double value = (rr - (2 * sigma2)) / (sigma2 * sigma2) * gauss;
                kernel[r, c] = value;
                sum += value;
            }
        }

        // Remove the DC component so the kernel sums to zero (a pure second-derivative operator).
        double mean = sum / (size * size);
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                kernel[r, c] -= mean;
            }
        }

        return kernel;
    }
}
