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
/// Windows are symmetric (the classic "periodic = false" form, denominator N-1).
/// </summary>
/// <remarks>
/// The coefficients come from <see cref="SignalWindows"/>, which is where every window MATLAB
/// documents lives (M132). Before that this class carried its own copy of the raised-cosine table
/// and evaluated it at every index; the table agreed with MATLAB's and the evaluation did not,
/// because MATLAB computes one half of a window and reflects it. Two tables of the same numbers is
/// how one of them drifts, so there is now one.
/// </remarks>
public static class Window
{
    /// <summary>Builds the coefficients of a length-<paramref name="length"/> window of the given type.</summary>
    public static double[] Create(WindowType type, int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Window length must be positive.");
        }

        return type switch
        {
            WindowType.Hann => SignalWindows.Hann(length),
            WindowType.Hamming => SignalWindows.Hamming(length),
            WindowType.Blackman => SignalWindows.Blackman(length),
            WindowType.BlackmanHarris => SignalWindows.BlackmanHarris(length),
            WindowType.FlatTop => SignalWindows.FlatTop(length),
            _ => SignalWindows.Rectangular(length),
        };
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
}
