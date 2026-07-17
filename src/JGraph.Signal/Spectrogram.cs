using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The result of a short-time Fourier transform: a magnitude (dB) grid indexed
/// <c>[frequencyBin, timeFrame]</c>, plus the data-space extents that place it on time and frequency
/// axes. Frequency bin 0 is DC; a heatmap should draw bin 0 at the bottom (low frequency) so the
/// image reads the usual way.
/// </summary>
/// <param name="MagnitudeDb">Magnitude in dB, [frequencyBins, timeFrames]; row 0 is DC.</param>
/// <param name="TimeMin">The start time (seconds) of the first frame's left edge.</param>
/// <param name="TimeMax">The end time (seconds) covered by the signal.</param>
/// <param name="FrequencyMin">The lowest frequency shown (0 Hz).</param>
/// <param name="FrequencyMax">The Nyquist frequency (sampleRate/2).</param>
public sealed record SpectrogramResult(
    double[,] MagnitudeDb,
    double TimeMin,
    double TimeMax,
    double FrequencyMin,
    double FrequencyMax)
{
    /// <summary>The number of frequency bins (windowSize/2 + 1).</summary>
    public int FrequencyBins => MagnitudeDb.GetLength(0);

    /// <summary>The number of time frames.</summary>
    public int TimeFrames => MagnitudeDb.GetLength(1);
}

/// <summary>
/// Computes a spectrogram (short-time Fourier transform magnitude) of a real signal: the signal is
/// split into overlapping windowed frames, each transformed by the <see cref="Fft"/>, and the
/// per-frame magnitudes stacked into a time–frequency grid.
/// </summary>
public static class Spectrogram
{
    /// <summary>
    /// Computes the spectrogram of <paramref name="signal"/> (sampled at <paramref name="sampleRate"/> Hz)
    /// using frames of <paramref name="windowSize"/> samples that overlap by <paramref name="overlap"/>
    /// samples. Keep <paramref name="windowSize"/> a power of two for the fast FFT path.
    /// </summary>
    public static SpectrogramResult Compute(
        ReadOnlySpan<double> signal,
        double sampleRate,
        int windowSize = 256,
        int overlap = 128,
        WindowType window = WindowType.Hann)
    {
        if (windowSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be at least 2.");
        }

        if (overlap < 0 || overlap >= windowSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be in [0, windowSize).");
        }

        int hop = windowSize - overlap;
        int bins = (windowSize / 2) + 1;
        int frames = signal.Length >= windowSize ? 1 + ((signal.Length - windowSize) / hop) : 0;

        var mag = new double[bins, System.Math.Max(frames, 0)];
        if (frames == 0)
        {
            return new SpectrogramResult(mag, 0, signal.Length / sampleRate, 0, sampleRate / 2);
        }

        double[] w = Window.Create(window, windowSize);
        var buffer = new Complex[windowSize];
        for (int f = 0; f < frames; f++)
        {
            int start = f * hop;
            for (int i = 0; i < windowSize; i++)
            {
                buffer[i] = new Complex(signal[start + i] * w[i], 0);
            }

            Fft.Transform(buffer, inverse: false);

            for (int k = 0; k < bins; k++)
            {
                double m = buffer[k].Magnitude;
                mag[k, f] = 20.0 * System.Math.Log10(m + 1e-12);
            }
        }

        double timeMax = signal.Length / sampleRate;
        return new SpectrogramResult(mag, 0, timeMax, 0, sampleRate / 2);
    }
}
