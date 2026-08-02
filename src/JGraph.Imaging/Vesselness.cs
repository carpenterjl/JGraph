namespace JGraph.Imaging;

/// <summary>
/// Ridge detection from the image Hessian — the Frangi vesselness measure behind
/// <c>fibermetric</c>, and the Hessian norm that calibrates it.
/// </summary>
/// <remarks>
/// A fibre, a vessel and a crack are all the same thing to second-order geometry: a place where the
/// intensity curves sharply across one direction and hardly at all along the other. The Hessian's
/// two eigenvalues measure exactly those two curvatures, so their ratio says how tube-like a point
/// is and their size says how much of anything is there.
/// </remarks>
public static class Vesselness
{
    /// <summary>Which way round the fibres are.</summary>
    public enum Polarity
    {
        /// <summary>Light fibres on a dark background.</summary>
        Bright,

        /// <summary>Dark fibres on a light background.</summary>
        Dark,
    }

    // Frangi's blob-suppression constant. Half is the published value and it is not exposed here for
    // the same reason MATLAB does not expose it: it separates "tube" from "blob", which is what the
    // measure means, rather than tuning how much of one to accept.
    private const double Beta = 0.5;

    /// <summary>
    /// The Frangi vesselness of a picture at each of several fibre widths, taking the strongest
    /// response (MATLAB <c>fibermetric</c>).
    /// </summary>
    /// <remarks>
    /// A single scale only finds fibres near its own width, so the measure is computed at every
    /// requested thickness and the largest kept — which is why the output is a fibre map and not a
    /// scale map. The Gaussian's standard deviation for a fibre of width <c>w</c> is <c>w/2</c>: that
    /// is where the scale-normalized second derivative of a bar that wide peaks, so a fibre is
    /// answered most strongly by the scale that was asked for it.
    /// </remarks>
    /// <param name="image">The picture to measure; colour is converted to gray first.</param>
    /// <param name="thicknesses">Fibre widths in pixels.</param>
    /// <param name="structureSensitivity">
    /// The threshold on how much structure counts. Below it a response is treated as background;
    /// <c>0.5 · maxhessiannorm</c> is the usual choice.
    /// </param>
    /// <param name="polarity">Whether the fibres are lighter or darker than what surrounds them.</param>
    public static ImageBuffer FiberMetric(
        ImageBuffer image,
        IReadOnlyList<double> thicknesses,
        double structureSensitivity = 0.01,
        Polarity polarity = Polarity.Bright)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(thicknesses);
        if (thicknesses.Count == 0)
        {
            throw new ArgumentException("fibermetric needs at least one thickness.", nameof(thicknesses));
        }

        foreach (double thickness in thicknesses)
        {
            if (thickness <= 0)
            {
                throw new ArgumentException("fibermetric thicknesses must be positive.", nameof(thicknesses));
            }
        }

        if (structureSensitivity <= 0)
        {
            throw new ArgumentException(
                "fibermetric structure sensitivity must be positive.", nameof(structureSensitivity));
        }

        using ImageBuffer gray = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
        int height = gray.Height;
        int width = gray.Width;
        var best = new ImageBuffer(height, width, 1);
        double sensitivitySquared = 2 * structureSensitivity * structureSensitivity;
        double betaSquared = 2 * Beta * Beta;

        foreach (double thickness in thicknesses)
        {
            (double[,] xx, double[,] xy, double[,] yy) = Hessian(gray, thickness / 2.0);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    (double small, double large) = Eigenvalues(xx[r, c], xy[r, c], yy[r, c]);

                    // A bright fibre curves downwards across itself, so its larger-magnitude
                    // eigenvalue is negative. The wrong sign is a fibre of the other polarity and
                    // gets nothing, which is what keeps the two answers apart.
                    bool wrongWay = polarity == Polarity.Bright ? large > 0 : large < 0;
                    if (wrongWay || large == 0)
                    {
                        continue;
                    }

                    double ratio = small / large;
                    double strength = (small * small) + (large * large);
                    double response =
                        Math.Exp(-(ratio * ratio) / betaSquared) *
                        (1.0 - Math.Exp(-strength / sensitivitySquared));

                    if (response > best[r, c, 0])
                    {
                        best[r, c, 0] = response;
                    }
                }
            }
        }

        GC.KeepAlive(image);
        return best;
    }

    /// <summary>
    /// The largest Frobenius norm of the image Hessian at one scale (MATLAB <c>maxhessiannorm</c>) —
    /// the size of the strongest piece of structure in the picture.
    /// </summary>
    /// <remarks>
    /// This exists to calibrate <see cref="FiberMetric"/>. Its structure sensitivity is an absolute
    /// threshold, so a number that works on one picture is meaningless on another with a different
    /// contrast; half of this norm is a threshold expressed in the picture's own terms.
    /// </remarks>
    public static double MaxHessianNorm(ImageBuffer image, double thickness = 4)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (thickness <= 0)
        {
            throw new ArgumentException("maxhessiannorm thickness must be positive.", nameof(thickness));
        }

        using ImageBuffer gray = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
        (double[,] xx, double[,] xy, double[,] yy) = Hessian(gray, thickness / 2.0);

        double largest = 0;
        for (int r = 0; r < gray.Height; r++)
        {
            for (int c = 0; c < gray.Width; c++)
            {
                // ‖H‖_F over a symmetric 2×2: the off-diagonal appears twice.
                double norm = Math.Sqrt((xx[r, c] * xx[r, c]) + (2 * xy[r, c] * xy[r, c]) + (yy[r, c] * yy[r, c]));
                largest = Math.Max(largest, norm);
            }
        }

        GC.KeepAlive(image);
        return largest;
    }

    /// <summary>
    /// The three distinct entries of the scale-normalized Hessian, each a separable pass of Gaussian
    /// derivatives.
    /// </summary>
    /// <remarks>
    /// Differentiating a Gaussian analytically and convolving once is both faster and better behaved
    /// than blurring and then differencing: a finite difference of a blurred picture amplifies
    /// whatever the blur left behind, and at small sigma there is a good deal of it. The σ² factor is
    /// what makes responses at different scales comparable, so the maximum over scales means
    /// something.
    /// </remarks>
    private static (double[,] Xx, double[,] Xy, double[,] Yy) Hessian(ImageBuffer image, double sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        int size = (2 * radius) + 1;
        var g = new double[size];
        var g1 = new double[size];
        var g2 = new double[size];
        double variance = sigma * sigma;
        double total = 0;
        for (int i = 0; i < size; i++)
        {
            double x = i - radius;
            g[i] = Math.Exp(-(x * x) / (2 * variance));
            total += g[i];
        }

        double sum1 = 0;
        double sum2 = 0;
        for (int i = 0; i < size; i++)
        {
            double x = i - radius;
            g[i] /= total;
            g1[i] = -x / variance * g[i];
            g2[i] = ((x * x) - variance) / (variance * variance) * g[i];
            sum1 += g1[i];
            sum2 += g2[i];
        }

        // A derivative kernel has to sum to zero or it responds to a constant, and the truncated
        // analytic one does not: cutting the tails off at 3σ leaves a small residue that shows up as
        // structure in a picture that has none. Taking the residue back out is what makes
        // maxhessiannorm of a flat picture exactly zero.
        for (int i = 0; i < size; i++)
        {
            g1[i] -= sum1 / size;
            g2[i] -= sum2 / size;
        }

        double scale = variance;
        return (
            Separable(image, g2, g, scale),
            Separable(image, g1, g1, scale),
            Separable(image, g, g2, scale));
    }

    /// <summary>Convolves with <paramref name="alongCols"/> across and <paramref name="alongRows"/> down.</summary>
    private static double[,] Separable(ImageBuffer image, double[] alongCols, double[] alongRows, double scale)
    {
        int height = image.Height;
        int width = image.Width;
        int radius = alongCols.Length / 2;

        var horizontal = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double sum = 0;
                for (int k = 0; k < alongCols.Length; k++)
                {
                    sum += alongCols[k] * image[r, Math.Clamp(c + k - radius, 0, width - 1), 0];
                }

                horizontal[r, c] = sum;
            }
        }

        int down = alongRows.Length / 2;
        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double sum = 0;
                for (int k = 0; k < alongRows.Length; k++)
                {
                    sum += alongRows[k] * horizontal[Math.Clamp(r + k - down, 0, height - 1), c];
                }

                result[r, c] = scale * sum;
            }
        }

        return result;
    }

    /// <summary>The eigenvalues of a symmetric 2×2, ordered by magnitude.</summary>
    private static (double Small, double Large) Eigenvalues(double xx, double xy, double yy)
    {
        double half = (xx + yy) / 2;
        double difference = (xx - yy) / 2;
        double root = Math.Sqrt((difference * difference) + (xy * xy));
        double a = half + root;
        double b = half - root;
        return Math.Abs(a) <= Math.Abs(b) ? (a, b) : (b, a);
    }
}
