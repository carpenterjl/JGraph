using System.Numerics;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Signal;

/// <summary>
/// Accuracy bounds at large n, pinning the M22 twiddle-table rework: the old per-butterfly
/// <c>w *= wLen</c> recurrence accumulated rounding across each stage; direct-sincos twiddles must
/// keep a quarter-million-sample Bluestein round-trip near machine precision.
/// </summary>
public class FftAccuracyTests
{
    [Fact]
    public void LargeNonPowerOfTwo_RoundTrip_StaysNearMachinePrecision()
    {
        const int n = 250_000; // Bluestein path (not a power of two)
        var signal = new double[n];
        var random = new Random(42);
        for (int i = 0; i < n; i++)
        {
            signal[i] = (random.NextDouble() * 2) - 1;
        }

        Complex[] spectrum = Fft.Forward(signal);
        Complex[] restored = Fft.Inverse(spectrum);

        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            worst = System.Math.Max(worst, Complex.Abs(restored[i] - signal[i]));
        }

        Assert.True(worst < 1e-9, $"round-trip error {worst:E2} exceeds 1e-9");
    }

    [Fact]
    public void LargeNonPowerOfTwo_PureTone_ConcentratesAtItsBin()
    {
        const int n = 100_001; // odd length: worst case for chirp-phase accuracy
        const int bin = 12_345;
        var signal = new double[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = System.Math.Sin(2 * System.Math.PI * bin * i / n);
        }

        Complex[] spectrum = Fft.Forward(signal);

        // A real sine of amplitude 1 puts magnitude n/2 at its bin (and the mirror bin).
        double peak = Complex.Abs(spectrum[bin]);
        Assert.True(System.Math.Abs(peak - (n / 2.0)) / (n / 2.0) < 1e-9,
            $"peak magnitude {peak} is not n/2 = {n / 2.0}");

        // Every other bin is numerical leakage only: at least ~10 orders of magnitude down.
        double worstLeak = 0;
        for (int k = 0; k < n / 2; k++)
        {
            if (k != bin)
            {
                worstLeak = System.Math.Max(worstLeak, Complex.Abs(spectrum[k]));
            }
        }

        Assert.True(worstLeak < peak * 1e-8, $"leakage {worstLeak:E2} vs peak {peak:E2}");
    }

    [Fact]
    public void Parseval_HoldsAtLargeN()
    {
        const int n = 65_536; // radix-2 path at scale
        var signal = new double[n];
        var random = new Random(7);
        double timeEnergy = 0;
        for (int i = 0; i < n; i++)
        {
            signal[i] = (random.NextDouble() * 2) - 1;
            timeEnergy += signal[i] * signal[i];
        }

        Complex[] spectrum = Fft.Forward(signal);
        double freqEnergy = 0;
        foreach (Complex bin in spectrum)
        {
            freqEnergy += bin.Magnitude * bin.Magnitude;
        }

        freqEnergy /= n;
        Assert.True(System.Math.Abs(freqEnergy - timeEnergy) / timeEnergy < 1e-12,
            $"Parseval mismatch: time {timeEnergy}, freq {freqEnergy}");
    }
}
