using System.Numerics;

namespace JGraph.Imaging;

/// <summary>One Gabor filter's parameters, and the envelope they imply.</summary>
/// <param name="Wavelength">The wavelength of the sinusoid, in pixels per cycle.</param>
/// <param name="OrientationDegrees">
/// The direction the sinusoid runs in, measured from the column axis and turning towards increasing
/// rows.
/// </param>
/// <param name="Bandwidth">The half-response bandwidth in octaves, which sets how many cycles fit under the envelope.</param>
/// <param name="AspectRatio">The envelope's width across the sinusoid relative to along it.</param>
public readonly record struct GaborParameters(
    double Wavelength,
    double OrientationDegrees,
    double Bandwidth = 1.0,
    double AspectRatio = 0.5)
{
    /// <summary>The envelope's standard deviation along the sinusoid.</summary>
    /// <remarks>
    /// Bandwidth and envelope size are the same statement made twice: a filter that answers over a
    /// narrow band of frequencies must be broad in space, because it needs to see many cycles before
    /// it can tell one frequency from a neighbouring one. This is that relation solved for the
    /// envelope, so a script names the bandwidth it cares about and the size follows.
    /// </remarks>
    public double Sigma
    {
        get
        {
            double octaves = Math.Pow(2, Bandwidth);
            return Wavelength / Math.PI * Math.Sqrt(Math.Log(2) / 2) * ((octaves + 1) / (octaves - 1));
        }
    }

    /// <summary>The envelope's standard deviation across the sinusoid.</summary>
    public double SigmaAcross => AspectRatio == 0 ? Sigma : Sigma / AspectRatio;

    /// <summary>Throws when any parameter is outside the range that describes a filter.</summary>
    public void Validate()
    {
        if (!(Wavelength >= 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Wavelength),
                "the wavelength must be at least two pixels per cycle; a shorter one is not sampled.");
        }

        if (!(Bandwidth > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(Bandwidth), "the bandwidth must be positive.");
        }

        if (!(AspectRatio > 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AspectRatio), "the aspect ratio must be positive.");
        }
    }
}

/// <summary>
/// Gabor filters: a sinusoid of one wavelength and direction, seen through a Gaussian window.
/// </summary>
/// <remarks>
/// A Fourier transform says which frequencies a picture contains and nothing about where they are; a
/// filter kernel says where things are and little about their frequency. A Gabor filter is the
/// compromise that is provably as good as the compromise gets — no other shape localizes as tightly
/// in both at once — which is why a bank of them, spread over a few wavelengths and directions, is the
/// standard way to describe texture. The answer at each pixel is complex: the magnitude says how much
/// of that wavelength and direction is present, and the phase says where in its cycle it is.
/// </remarks>
public static class GaborFilters
{
    /// <summary>The complex kernel one set of parameters describes.</summary>
    /// <param name="parameters">The filter's wavelength, direction, bandwidth and aspect.</param>
    /// <returns>The real and imaginary parts, the same size, centred on their middle tap.</returns>
    public static (double[,] Real, double[,] Imaginary) Kernel(GaborParameters parameters)
    {
        parameters.Validate();
        double sigmaAlong = parameters.Sigma;
        double sigmaAcross = parameters.SigmaAcross;
        double radians = parameters.OrientationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // Three standard deviations of the envelope, measured along the picture's own axes rather than
        // the filter's: a turned ellipse needs a bounding box, not a square.
        int halfCols = (int)Math.Ceiling(3 * Math.Sqrt(
            (sigmaAlong * cos * (sigmaAlong * cos)) + (sigmaAcross * sin * (sigmaAcross * sin))));
        int halfRows = (int)Math.Ceiling(3 * Math.Sqrt(
            (sigmaAlong * sin * (sigmaAlong * sin)) + (sigmaAcross * cos * (sigmaAcross * cos))));
        halfCols = Math.Max(1, halfCols);
        halfRows = Math.Max(1, halfRows);

        int rows = (2 * halfRows) + 1;
        int cols = (2 * halfCols) + 1;
        var real = new double[rows, cols];
        var imaginary = new double[rows, cols];
        double scale = 1.0 / (2 * Math.PI * sigmaAlong * sigmaAcross);

        for (int r = 0; r < rows; r++)
        {
            double y = r - halfRows;
            for (int c = 0; c < cols; c++)
            {
                double x = c - halfCols;
                double along = (x * cos) + (y * sin);
                double across = (-x * sin) + (y * cos);
                double envelope = scale * Math.Exp(-0.5 * (
                    (along * along / (sigmaAlong * sigmaAlong)) +
                    (across * across / (sigmaAcross * sigmaAcross))));
                double phase = 2 * Math.PI * along / parameters.Wavelength;
                real[r, c] = envelope * Math.Cos(phase);
                imaginary[r, c] = envelope * Math.Sin(phase);
            }
        }

        return (real, imaginary);
    }

    /// <summary>Applies one filter to a picture.</summary>
    /// <param name="image">The picture.</param>
    /// <param name="parameters">The filter.</param>
    /// <returns>The response magnitude and phase, both the size of the picture.</returns>
    /// <remarks>
    /// The border is extended by repeating the edge pixels, so the filter never sees a step that is
    /// not there. Both parts of the kernel are applied through one padded transform, because a Gabor
    /// envelope wide enough to be useful is usually wide enough that convolving directly costs more
    /// than transforming twice.
    /// </remarks>
    public static (double[,] Magnitude, double[,] Phase) Apply(double[,] image, GaborParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(image);
        (double[,] real, double[,] imaginary) = Kernel(parameters);

        double[,] evenPart = ConvolveReplicated(image, real);
        double[,] oddPart = ConvolveReplicated(image, imaginary);

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var magnitude = new double[height, width];
        var phase = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                magnitude[r, c] = Math.Sqrt(
                    (evenPart[r, c] * evenPart[r, c]) + (oddPart[r, c] * oddPart[r, c]));
                phase[r, c] = Math.Atan2(oddPart[r, c], evenPart[r, c]);
            }
        }

        return (magnitude, phase);
    }

    /// <summary>
    /// Convolution with the border extended by repetition, done through the frequency domain.
    /// </summary>
    /// <remarks>
    /// The picture is grown by one kernel less one on each axis, filled by repeating its edge, and
    /// then wrapped convolution over that larger grid is read back from the part the wrap cannot
    /// reach. The extension is exactly as wide as the aliasing, so the answer is the linear
    /// convolution with a repeated border and not an approximation of it.
    /// </remarks>
    private static double[,] ConvolveReplicated(double[,] image, double[,] kernel)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        int kernelRows = kernel.GetLength(0);
        int kernelCols = kernel.GetLength(1);
        int paddedRows = height + kernelRows - 1;
        int paddedCols = width + kernelCols - 1;
        int topPad = kernelRows / 2;
        int leftPad = kernelCols / 2;

        var padded = new Complex[paddedRows * paddedCols];
        for (int r = 0; r < paddedRows; r++)
        {
            int source = Math.Clamp(r - topPad, 0, height - 1);
            for (int c = 0; c < paddedCols; c++)
            {
                padded[(r * paddedCols) + c] = image[source, Math.Clamp(c - leftPad, 0, width - 1)];
            }
        }

        Complex[] taps = FilterDesign.PsfToOtf(kernel, paddedRows, paddedCols);
        FourierGrid.Transform(padded, paddedRows, paddedCols, inverse: false);
        for (int i = 0; i < padded.Length; i++)
        {
            padded[i] *= taps[i];
        }

        FourierGrid.Transform(padded, paddedRows, paddedCols, inverse: true);

        // The wrap can only reach as far in as the kernel's own half-width, and the picture was
        // inset by exactly that much, so what sits where the picture sat is untouched by it.
        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                result[r, c] = padded[((r + topPad) * paddedCols) + c + leftPad].Real;
            }
        }

        return result;
    }
}
