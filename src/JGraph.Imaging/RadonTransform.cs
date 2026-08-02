using System.Numerics;
using JGraph.Signal;

namespace JGraph.Imaging;

/// <summary>
/// The Radon transform and its filtered backprojection — <c>radon</c> and <c>iradon</c>, which
/// together are computed tomography: a stack of shadows taken from every angle, and the picture
/// they came from recovered out of them.
/// </summary>
/// <remarks>
/// <para>
/// The forward direction is a line integral: for each angle, every pixel is dropped into the
/// projection bin its coordinate falls in, split between the two nearest bins in proportion. That
/// splitting is what conserves mass — the sum of any column of the sinogram is the sum of the
/// picture, whatever the angle — and it is the property a script can check its own arithmetic
/// against.
/// </para>
/// <para>
/// The inverse direction cannot simply smear each projection back across the picture: doing that
/// counts the low frequencies once per angle and the high ones barely at all, and the result is a
/// blurred ghost. Multiplying each projection's spectrum by <c>|ω|</c> first — the ramp filter —
/// undoes exactly that oversampling. The ramp is unbounded at high frequency, where the data is
/// mostly noise, so every filter but Ram-Lak is the ramp with a window rolling it off: which window
/// you pick is the trade between a sharp reconstruction and a quiet one.
/// </para>
/// </remarks>
public static class RadonTransform
{
    /// <summary>How a backprojection reads a value between two projection bins.</summary>
    public enum Interpolation
    {
        /// <summary>The nearer bin, whole.</summary>
        Nearest,

        /// <summary>A straight-line blend of the two bins either side.</summary>
        Linear,
    }

    /// <summary>The window applied to the ramp before backprojection.</summary>
    public enum Filter
    {
        /// <summary>The bare ramp — sharpest, and noisiest.</summary>
        RamLak,

        /// <summary>The ramp times a sinc, rolling off gently.</summary>
        SheppLogan,

        /// <summary>The ramp times a cosine.</summary>
        Cosine,

        /// <summary>The ramp times a Hamming window.</summary>
        Hamming,

        /// <summary>The ramp times a Hann window.</summary>
        Hann,

        /// <summary>No filter at all — a plain backprojection, and so a blurred one.</summary>
        None,
    }

    /// <summary>
    /// How many projection bins a picture of this size needs: enough for the longest diagonal at any
    /// rotation, and always odd so that one bin sits exactly on the axis of rotation.
    /// </summary>
    /// <param name="rows">The picture's height.</param>
    /// <param name="cols">The picture's width.</param>
    /// <returns>The bin count.</returns>
    public static int ProjectionLength(int rows, int cols)
    {
        double halfRows = rows - Math.Floor((rows - 1) / 2.0) - 1;
        double halfCols = cols - Math.Floor((cols - 1) / 2.0) - 1;
        return (2 * (int)Math.Ceiling(Math.Sqrt((halfRows * halfRows) + (halfCols * halfCols)))) + 3;
    }

    /// <summary>
    /// Projects a picture at each angle.
    /// </summary>
    /// <param name="image">The picture, row-major.</param>
    /// <param name="anglesDegrees">The projection angles, measured from the x axis.</param>
    /// <returns>
    /// The sinogram — one column per angle — and the bin coordinates, which run symmetrically about
    /// zero in unit steps.
    /// </returns>
    public static (double[,] Sinogram, double[] Coordinates) Forward(
        double[,] image, IReadOnlyList<double> anglesDegrees)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(anglesDegrees);
        if (anglesDegrees.Count == 0)
        {
            throw new ArgumentException("radon needs at least one angle.", nameof(anglesDegrees));
        }

        int rows = image.GetLength(0);
        int cols = image.GetLength(1);
        int bins = ProjectionLength(rows, cols);
        double centre = (bins - 1) / 2.0;
        double rowCentre = (rows - 1) / 2.0;
        double colCentre = (cols - 1) / 2.0;

        var sinogram = new double[bins, anglesDegrees.Count];
        for (int a = 0; a < anglesDegrees.Count; a++)
        {
            double radians = anglesDegrees[a] * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            for (int r = 0; r < rows; r++)
            {
                // y counts up the picture where the row index counts down it.
                double y = rowCentre - r;
                for (int c = 0; c < cols; c++)
                {
                    double value = image[r, c];
                    if (value == 0)
                    {
                        continue;
                    }

                    double position = (((c - colCentre) * cos) + (y * sin)) + centre;
                    int low = (int)Math.Floor(position);
                    double fraction = position - low;

                    // Splitting the pixel between the two bins it straddles is what keeps the total
                    // constant: every projection of the same picture sums to the same number.
                    if (low >= 0 && low < bins)
                    {
                        sinogram[low, a] += value * (1 - fraction);
                    }

                    if (low + 1 >= 0 && low + 1 < bins)
                    {
                        sinogram[low + 1, a] += value * fraction;
                    }
                }
            }
        }

        var coordinates = new double[bins];
        for (int i = 0; i < bins; i++)
        {
            coordinates[i] = i - centre;
        }

        return (sinogram, coordinates);
    }

    /// <summary>The side length <c>iradon</c> reconstructs at when none is asked for.</summary>
    /// <param name="projectionLength">The number of bins per projection.</param>
    /// <returns>The default output size.</returns>
    public static int DefaultOutputSize(int projectionLength) =>
        2 * (int)Math.Floor(projectionLength / (2 * Math.Sqrt(2)));

    /// <summary>
    /// Reconstructs a picture from its projections by filtered backprojection.
    /// </summary>
    /// <param name="sinogram">One column per angle, as <see cref="Forward"/> returns.</param>
    /// <param name="anglesDegrees">The angle each column was taken at.</param>
    /// <param name="interpolation">How a backprojection reads between bins.</param>
    /// <param name="filter">The window on the ramp.</param>
    /// <param name="frequencyScaling">
    /// The fraction of the spectrum to keep, in (0, 1]. Below 1 the filter is compressed into the
    /// low frequencies and everything above is zeroed — a blunter but much quieter reconstruction.
    /// </param>
    /// <param name="outputSize">The side of the square to reconstruct.</param>
    /// <returns>The picture, and the filter's frequency response.</returns>
    public static (double[,] Image, double[] FilterResponse) Inverse(
        double[,] sinogram,
        IReadOnlyList<double> anglesDegrees,
        Interpolation interpolation,
        Filter filter,
        double frequencyScaling,
        int outputSize)
    {
        ArgumentNullException.ThrowIfNull(sinogram);
        ArgumentNullException.ThrowIfNull(anglesDegrees);
        int bins = sinogram.GetLength(0);
        int angles = sinogram.GetLength(1);
        if (angles != anglesDegrees.Count)
        {
            throw new ArgumentException(
                "iradon needs one angle per column of the sinogram.", nameof(anglesDegrees));
        }

        if (frequencyScaling is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frequencyScaling), "the frequency scaling must be greater than 0 and at most 1.");
        }

        if (outputSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(outputSize), "the output size must be positive.");
        }

        int order = Math.Max(64, Fft.NextPowerOfTwo(2 * bins));
        double[] response = DesignFilter(filter, order, frequencyScaling);
        double[,] filtered = ApplyFilter(sinogram, response, order);

        // The same geometric centre the forward transform projects about, rather than MATLAB's
        // ceil(n/2) grid: sharing one convention is what makes the pair invert each other exactly,
        // and MATLAB's own grid sits half a pixel off centre for an even size.
        var image = new double[outputSize, outputSize];
        double centre = (outputSize - 1) / 2.0;
        double binCentre = (bins - 1) / 2.0;

        for (int a = 0; a < angles; a++)
        {
            double radians = anglesDegrees[a] * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            for (int r = 0; r < outputSize; r++)
            {
                double y = centre - r;
                for (int c = 0; c < outputSize; c++)
                {
                    double x = c - centre;
                    double position = (x * cos) + (y * sin) + binCentre;
                    if (interpolation == Interpolation.Nearest)
                    {
                        int nearest = (int)Math.Round(position, MidpointRounding.AwayFromZero);
                        if (nearest >= 0 && nearest < bins)
                        {
                            image[r, c] += filtered[nearest, a];
                        }

                        continue;
                    }

                    int low = (int)Math.Floor(position);
                    double fraction = position - low;
                    if (low >= 0 && low < bins)
                    {
                        image[r, c] += filtered[low, a] * (1 - fraction);
                    }

                    if (low + 1 >= 0 && low + 1 < bins)
                    {
                        image[r, c] += filtered[low + 1, a] * fraction;
                    }
                }
            }
        }

        // Each of the n angles contributed a full backprojection, and π/2 of them is what the
        // inversion formula asks for.
        double scale = Math.PI / (2.0 * angles);
        for (int r = 0; r < outputSize; r++)
        {
            for (int c = 0; c < outputSize; c++)
            {
                image[r, c] *= scale;
            }
        }

        return (image, response);
    }

    /// <summary>Reads a filter name the way MATLAB spells it.</summary>
    /// <param name="name">The word given to <c>iradon</c>.</param>
    /// <returns>The matching filter.</returns>
    public static Filter ParseFilter(string name) => name?.ToLowerInvariant() switch
    {
        "ram-lak" => Filter.RamLak,
        "shepp-logan" => Filter.SheppLogan,
        "cosine" => Filter.Cosine,
        "hamming" => Filter.Hamming,
        "hann" => Filter.Hann,
        "none" => Filter.None,
        _ => throw new ArgumentException(
            $"unknown filter '{name}' (use 'Ram-Lak', 'Shepp-Logan', 'Cosine', 'Hamming', 'Hann', or 'none').",
            nameof(name)),
    };

    /// <summary>
    /// The ramp filter's frequency response, windowed and band-limited.
    /// </summary>
    /// <remarks>
    /// The ramp is built from its impulse response rather than written down as <c>|ω|</c> directly.
    /// Sampling the ramp in frequency gives it a spurious DC term — the continuous ramp is zero at
    /// zero, but its sampled inverse is not — and the visible result is a reconstruction sitting on
    /// a constant offset. Transforming the exact impulse response instead puts the zero where it
    /// belongs.
    /// </remarks>
    private static double[] DesignFilter(Filter filter, int order, double scaling)
    {
        int half = order / 2;
        if (filter == Filter.None)
        {
            var flat = new double[order];
            Array.Fill(flat, 1.0);
            return flat;
        }

        var impulse = new double[order];
        impulse[0] = 0.25;
        for (int n = 1; n <= half; n += 2)
        {
            double value = -1.0 / ((Math.PI * n) * (Math.PI * n));
            impulse[n] = value;
            impulse[order - n] = value;
        }

        Complex[] spectrum = Fft.Forward(impulse);
        var response = new double[order];
        for (int k = 0; k <= half; k++)
        {
            response[k] = 2 * spectrum[k].Real;
        }

        for (int k = 1; k <= half; k++)
        {
            double w = 2 * Math.PI * k / order;
            response[k] *= filter switch
            {
                Filter.RamLak => 1.0,
                Filter.SheppLogan => Math.Sin(w / (2 * scaling)) / (w / (2 * scaling)),
                Filter.Cosine => Math.Cos(w / (2 * scaling)),
                Filter.Hamming => 0.54 + (0.46 * Math.Cos(w / scaling)),
                Filter.Hann => (1 + Math.Cos(w / scaling)) / 2,
                _ => 1.0,
            };

            if (w > Math.PI * scaling)
            {
                response[k] = 0;
            }
        }

        // The response is real and even, so the upper half mirrors the lower.
        for (int k = 1; k < half; k++)
        {
            response[order - k] = response[k];
        }

        return response;
    }

    private static double[,] ApplyFilter(double[,] sinogram, double[] response, int order)
    {
        int bins = sinogram.GetLength(0);
        int angles = sinogram.GetLength(1);
        var filtered = new double[bins, angles];
        var column = new Complex[order];

        for (int a = 0; a < angles; a++)
        {
            Array.Clear(column);
            for (int i = 0; i < bins; i++)
            {
                column[i] = new Complex(sinogram[i, a], 0);
            }

            Fft.Transform(column, inverse: false);
            for (int k = 0; k < order; k++)
            {
                column[k] *= response[k];
            }

            Fft.Transform(column, inverse: true);
            for (int i = 0; i < bins; i++)
            {
                filtered[i, a] = column[i].Real;
            }
        }

        return filtered;
    }
}
