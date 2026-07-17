namespace JGraph.Signal;

/// <summary>The named tapering windows available for spectral analysis.</summary>
public enum WindowType
{
    /// <summary>No taper (a boxcar); best frequency resolution, worst spectral leakage.</summary>
    Rectangular,

    /// <summary>The raised-cosine (Hann) window; a good general-purpose default.</summary>
    Hann,

    /// <summary>The Hamming window; lower first side-lobe than Hann.</summary>
    Hamming,

    /// <summary>The Blackman window; strong side-lobe suppression.</summary>
    Blackman,

    /// <summary>The 4-term Blackman–Harris window; very low side-lobes.</summary>
    BlackmanHarris,

    /// <summary>The flat-top window; excellent amplitude accuracy, wide main lobe.</summary>
    FlatTop,
}

/// <summary>
/// Generates and applies tapering windows used before an <see cref="Fft"/> to reduce spectral leakage.
/// Windows are symmetric (the classic "periodic = false" form, denominator N−1).
/// </summary>
public static class Window
{
    /// <summary>Builds the coefficients of a length-<paramref name="length"/> window of the given type.</summary>
    public static double[] Create(WindowType type, int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Window length must be positive.");
        }

        var w = new double[length];
        if (length == 1)
        {
            w[0] = 1.0;
            return w;
        }

        double denom = length - 1;
        for (int n = 0; n < length; n++)
        {
            double t = n / denom; // 0..1
            w[n] = type switch
            {
                WindowType.Rectangular => 1.0,
                WindowType.Hann => 0.5 - (0.5 * Cos(1, t)),
                WindowType.Hamming => 0.54 - (0.46 * Cos(1, t)),
                WindowType.Blackman => 0.42 - (0.5 * Cos(1, t)) + (0.08 * Cos(2, t)),
                WindowType.BlackmanHarris =>
                    0.35875 - (0.48829 * Cos(1, t)) + (0.14128 * Cos(2, t)) - (0.01168 * Cos(3, t)),
                WindowType.FlatTop =>
                    0.21557895 - (0.41663158 * Cos(1, t)) + (0.277263158 * Cos(2, t))
                        - (0.083578947 * Cos(3, t)) + (0.006947368 * Cos(4, t)),
                _ => 1.0,
            };
        }

        return w;
    }

    /// <summary>Multiplies <paramref name="frame"/> in place by a window of the given type.</summary>
    public static void ApplyInPlace(WindowType type, double[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double[] w = Create(type, frame.Length);
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] *= w[i];
        }
    }

    /// <summary>
    /// The coherent gain (mean of the window coefficients). Amplitude spectra are divided by this to
    /// recover the true amplitude of a windowed sinusoid.
    /// </summary>
    public static double CoherentGain(ReadOnlySpan<double> window)
    {
        if (window.Length == 0)
        {
            return 1.0;
        }

        double sum = 0;
        foreach (double v in window)
        {
            sum += v;
        }

        return sum / window.Length;
    }

    private static double Cos(int harmonic, double t) =>
        System.Math.Cos(harmonic * 2.0 * System.Math.PI * t);
}
