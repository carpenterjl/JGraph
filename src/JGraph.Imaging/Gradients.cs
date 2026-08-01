namespace JGraph.Imaging;

/// <summary>
/// Directional gradients of a grayscale image (MATLAB <c>imgradientxy</c> and <c>imgradient</c>) — the
/// support functions around <see cref="EdgeDetection"/>. Results are signed, so they are not clamped to
/// [0, 1]; normalize with <see cref="PointOps.Normalize(double[,])"/> before displaying one.
/// </summary>
public static class Gradients
{
    /// <summary>The gradient operator a gradient is measured with.</summary>
    public enum Operator
    {
        /// <summary>The 3×3 Sobel kernels (MATLAB default).</summary>
        Sobel,

        /// <summary>The 3×3 Prewitt kernels.</summary>
        Prewitt,

        /// <summary>The 2×2 Roberts cross kernels.</summary>
        Roberts,

        /// <summary>The central difference, <c>(I(k+1) − I(k−1)) / 2</c>.</summary>
        Central,

        /// <summary>The intermediate (forward) difference, <c>I(k+1) − I(k)</c>.</summary>
        Intermediate,
    }

    /// <summary>
    /// The horizontal and vertical gradient components (MATLAB <c>imgradientxy</c>). RGB input is
    /// converted to grayscale first.
    /// </summary>
    public static (ImageBuffer Gx, ImageBuffer Gy) GradientXY(ImageBuffer image, Operator op = Operator.Sobel)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageBuffer gray = image.Channels == 1 ? image : PointOps.ToGray(image);
        try
        {
            (double[,] kx, double[,] ky) = KernelsFor(op);
            return (Filters.Correlate(gray, kx, Filters.Boundary.Replicate),
                    Filters.Correlate(gray, ky, Filters.Boundary.Replicate));
        }
        finally
        {
            if (!ReferenceEquals(gray, image))
            {
                gray.Dispose();
            }
        }
    }

    /// <summary>
    /// The gradient magnitude and direction (MATLAB <c>imgradient</c>). Direction is in degrees,
    /// measured counter-clockwise from the positive x axis with y pointing up, so a bright-to-dark
    /// step from left to right reads as 0°.
    /// </summary>
    public static (ImageBuffer Magnitude, ImageBuffer Direction) Gradient(ImageBuffer image, Operator op = Operator.Sobel)
    {
        (ImageBuffer gx, ImageBuffer gy) = GradientXY(image, op);
        using (gx)
        using (gy)
        {
            return FromComponents(gx, gy);
        }
    }

    /// <summary>
    /// Magnitude and direction from gradient components already in hand (MATLAB
    /// <c>imgradient(Gx, Gy)</c>) — the form that lets a script substitute its own derivative filter.
    /// </summary>
    public static (ImageBuffer Magnitude, ImageBuffer Direction) FromComponents(ImageBuffer gx, ImageBuffer gy)
    {
        ArgumentNullException.ThrowIfNull(gx);
        ArgumentNullException.ThrowIfNull(gy);
        if (gx.Height != gy.Height || gx.Width != gy.Width)
        {
            throw new ArgumentException("imgradient needs Gx and Gy to be the same size.");
        }

        var magnitude = new ImageBuffer(gx.Height, gx.Width, 1);
        var direction = new ImageBuffer(gx.Height, gx.Width, 1);
        for (int r = 0; r < gx.Height; r++)
        {
            for (int c = 0; c < gx.Width; c++)
            {
                double x = gx[r, c, 0];
                double y = gy[r, c, 0];
                magnitude[r, c, 0] = Math.Sqrt((x * x) + (y * y));
                // Rows increase downwards, so the row gradient is negated to give MATLAB's
                // y-up convention.
                direction[r, c, 0] = Math.Atan2(-y, x) * 180.0 / Math.PI;
            }
        }

        GC.KeepAlive(gx);
        GC.KeepAlive(gy);
        return (magnitude, direction);
    }

    /// <summary>
    /// The (x, y) kernel pair. <see cref="Kernels.Sobel"/> and <see cref="Kernels.Prewitt"/> difference
    /// down the rows with the top row positive, so the x kernel is the negated transpose and the y
    /// kernel is the negated original — that way a positive result means "brighter as the coordinate
    /// increases", the MATLAB <c>imgradientxy</c> convention. The Roberts pair stays as-is: its two
    /// components run along the diagonals rather than the axes.
    /// </summary>
    private static (double[,] Kx, double[,] Ky) KernelsFor(Operator op) => op switch
    {
        Operator.Prewitt => (Negate(Transpose(Kernels.Prewitt())), Negate(Kernels.Prewitt())),
        Operator.Roberts => (Kernels.Roberts(), Kernels.RobertsCounter()),

        // The difference operators are 1-D on purpose: they are what a script reaches for when a 3×3
        // operator's built-in smoothing would blur the very step it is trying to locate.
        Operator.Central => (
            new double[,] { { -0.5, 0, 0.5 } },
            new double[,] { { -0.5 }, { 0 }, { 0.5 } }),
        Operator.Intermediate => (
            new double[,] { { 0, -1, 1 } },
            new double[,] { { 0 }, { -1 }, { 1 } }),
        _ => (Negate(Transpose(Kernels.Sobel())), Negate(Kernels.Sobel())),
    };

    private static double[,] Negate(double[,] kernel)
    {
        int h = kernel.GetLength(0);
        int w = kernel.GetLength(1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                kernel[r, c] = -kernel[r, c];
            }
        }

        return kernel;
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
