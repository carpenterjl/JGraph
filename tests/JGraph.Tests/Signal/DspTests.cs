using System.Numerics;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Signal;

/// <summary>
/// M21c: the DSP additions — Bluestein arbitrary-length FFT, Direct Form II transposed filtering,
/// freqz, Butterworth design, Parks–McClellan (firpm), and the WAV codec.
/// </summary>
public class DspTests
{
    // --- Bluestein FFT --------------------------------------------------------------------------

    [Fact]
    public void Bluestein_MatchesRadix2_OnZeroPaddedData()
    {
        // A 1000-point transform of a signal that is zero beyond 1000 samples must match the
        // first bins' values computed... more directly: compare against the analytic DFT.
        var random = new Random(42);
        double[] signal = Enumerable.Range(0, 1000).Select(_ => random.NextDouble() - 0.5).ToArray();

        Complex[] fast = Fft.Forward(signal);
        Complex[] slow = AnalyticDft(signal);

        for (int k = 0; k < signal.Length; k++)
        {
            Assert.True((fast[k] - slow[k]).Magnitude < 1e-7,
                $"bin {k}: bluestein {fast[k]} vs direct {slow[k]}");
        }
    }

    [Fact]
    public void Bluestein_OddSmallLengths_MatchTheAnalyticDft()
    {
        foreach (int n in new[] { 33, 97, 129 })
        {
            double[] signal = Enumerable.Range(0, n).Select(i => System.Math.Sin(0.7 * i) + 0.3).ToArray();
            Complex[] fast = Fft.Forward(signal);
            Complex[] slow = AnalyticDft(signal);
            for (int k = 0; k < n; k++)
            {
                Assert.True((fast[k] - slow[k]).Magnitude < 1e-8, $"n={n} bin {k}");
            }
        }
    }

    [Fact]
    public void Fft_RoundTrips_AtAnArbitraryLength()
    {
        double[] signal = Enumerable.Range(0, 1234).Select(i => System.Math.Cos(0.01 * i * i)).ToArray();

        Complex[] spectrum = Fft.Forward(signal);
        Complex[] restored = Fft.Inverse(spectrum);

        for (int i = 0; i < signal.Length; i++)
        {
            Assert.True(System.Math.Abs(restored[i].Real - signal[i]) < 1e-9);
            Assert.True(System.Math.Abs(restored[i].Imaginary) < 1e-9);
        }
    }

    [Fact]
    public void Fft_Parseval_HoldsAtNonPowerOfTwoLength()
    {
        double[] signal = Enumerable.Range(0, 777).Select(i => System.Math.Sin(0.3 * i)).ToArray();
        Complex[] spectrum = Fft.Forward(signal);

        double timeEnergy = signal.Sum(s => s * s);
        double frequencyEnergy = spectrum.Sum(s => s.Magnitude * s.Magnitude) / signal.Length;

        Assert.True(System.Math.Abs(timeEnergy - frequencyEnergy) < 1e-6);
    }

    // --- filter ---------------------------------------------------------------------------------

    [Fact]
    public void Filter_FirImpulseResponse_IsTheCoefficients()
    {
        double[] b = [0.5, 0.3, 0.2];
        double[] impulse = new double[6];
        impulse[0] = 1;

        double[] y = DigitalFilter.Filter(b, [1.0], impulse);

        Assert.Equal(new[] { 0.5, 0.3, 0.2, 0, 0, 0 }, y);
    }

    [Fact]
    public void Filter_OnePoleStepResponse_MatchesClosedForm()
    {
        // y[n] = x[n] + 0.5 y[n-1] on a unit step: y[n] = 2 (1 - 0.5^{n+1}).
        double[] step = Enumerable.Repeat(1.0, 10).ToArray();

        double[] y = DigitalFilter.Filter([1.0], [1.0, -0.5], step);

        for (int n = 0; n < y.Length; n++)
        {
            double expected = 2 * (1 - System.Math.Pow(0.5, n + 1));
            Assert.True(System.Math.Abs(y[n] - expected) < 1e-12, $"n={n}: {y[n]} vs {expected}");
        }
    }

    [Fact]
    public void Filter_NormalizesByA0()
    {
        double[] x = [1, 2, 3];
        double[] scaled = DigitalFilter.Filter([2.0], [2.0], x);
        Assert.Equal(x, scaled);
    }

    // --- freqz ----------------------------------------------------------------------------------

    [Fact]
    public void Freqz_MovingAverage_HasDcGainOne_AndKnownNull()
    {
        // A 4-tap moving average: H(0) = 1, null at fs/4.
        double[] b = [0.25, 0.25, 0.25, 0.25];

        (Complex[] h, double[] f) = DigitalFilter.Freqz(b, [1.0], 512, sampleRate: 1000);

        Assert.Equal(512, h.Length);
        Assert.Equal(0, f[0]);
        Assert.True(System.Math.Abs(h[0].Magnitude - 1) < 1e-12);
        int quarter = 256; // ω = π/2 → 250 Hz
        Assert.True(h[quarter].Magnitude < 1e-12, $"|H| at fs/4 was {h[quarter].Magnitude}");
        Assert.True(System.Math.Abs(f[256] - 250) < 1e-9);
    }

    // --- butter ---------------------------------------------------------------------------------

    [Fact]
    public void Butterworth_SecondOrderLowPass_MatchesTheAnalyticBiquad()
    {
        // butter(2, 0.5) has the well-known coefficients b = [1, 2, 1]/(2+√2)... verify against
        // the direct bilinear evaluation instead of magic numbers: |H(0)| = 1, |H(Wn)| = 1/√2.
        (double[] b, double[] a) = IirDesign.Butterworth(2, [0.5], FilterBandType.LowPass);

        (Complex[] h, _) = DigitalFilter.Freqz(b, a, 1024, sampleRate: 2);
        Assert.True(System.Math.Abs(h[0].Magnitude - 1) < 1e-9, $"DC gain {h[0].Magnitude}");
        Assert.True(System.Math.Abs(h[512].Magnitude - (1 / System.Math.Sqrt(2))) < 1e-3,
            $"cutoff gain {h[512].Magnitude}");
        Assert.True(h[1023].Magnitude < 0.01, "response must fall toward Nyquist");
    }

    [Fact]
    public void Butterworth_LowPassMagnitude_IsMonotone()
    {
        (double[] b, double[] a) = IirDesign.Butterworth(4, [0.3], FilterBandType.LowPass);
        (Complex[] h, _) = DigitalFilter.Freqz(b, a, 256, sampleRate: 2);

        for (int k = 1; k < h.Length; k++)
        {
            Assert.True(h[k].Magnitude <= h[k - 1].Magnitude + 1e-9, $"not monotone at bin {k}");
        }
    }

    [Fact]
    public void Butterworth_HighPass_MirrorsTheGains()
    {
        (double[] b, double[] a) = IirDesign.Butterworth(3, [0.4], FilterBandType.HighPass);
        (Complex[] h, _) = DigitalFilter.Freqz(b, a, 1000, sampleRate: 2);

        Assert.True(h[0].Magnitude < 1e-6, "DC must be blocked");
        Assert.True(System.Math.Abs(h[^1].Magnitude - 1) < 1e-3, "Nyquist gain must be ~1");
        Assert.True(System.Math.Abs(h[400].Magnitude - (1 / System.Math.Sqrt(2))) < 1e-2,
            $"cutoff gain {h[400].Magnitude}"); // f[k] = k/1000 → the 0.4 cutoff is bin 400
    }

    [Fact]
    public void Butterworth_BandPass_PeaksInsideTheBand()
    {
        (double[] b, double[] a) = IirDesign.Butterworth(2, [0.2, 0.4], FilterBandType.BandPass);
        (Complex[] h, _) = DigitalFilter.Freqz(b, a, 1000, sampleRate: 2);

        Assert.True(h[0].Magnitude < 1e-6);
        Assert.True(h[^1].Magnitude < 1e-3);
        double peak = h.Max(v => v.Magnitude);
        Assert.True(System.Math.Abs(peak - 1) < 1e-2, $"peak {peak}");
        Assert.True(h[300].Magnitude > 0.7, "the band's center region must pass");
    }

    [Fact]
    public void Butterworth_FilteredSines_ShowTheExpectedAttenuation()
    {
        // Push a low and a high tone through a lowpass and compare output powers.
        (double[] b, double[] a) = IirDesign.Butterworth(4, [0.25], FilterBandType.LowPass);
        double[] low = Tone(0.05, 4096);
        double[] high = Tone(0.8, 4096);

        double lowGain = Rms(DigitalFilter.Filter(b, a, low)[1000..]) / Rms(low[1000..]);
        double highGain = Rms(DigitalFilter.Filter(b, a, high)[1000..]) / Rms(high[1000..]);

        Assert.True(lowGain > 0.99, $"passband gain {lowGain}");
        Assert.True(highGain < 0.01, $"stopband gain {highGain}");
    }

    // --- firpm ----------------------------------------------------------------------------------

    [Fact]
    public void Remez_DemoLowPass_MeetsItsSpec()
    {
        // The FM-demod demo call: firpm(127, [0 20 30 500]/500, [1 1 0 0]).
        double[] h = FirDesign.Remez(127, [0, 0.04, 0.06, 1], [1, 1, 0, 0], out bool converged);

        Assert.True(converged, "the demo design must converge");
        Assert.Equal(128, h.Length);

        // Exact even symmetry (linear phase).
        for (int i = 0; i < h.Length / 2; i++)
        {
            Assert.Equal(h[i], h[^(i + 1)]);
        }

        (Complex[] response, double[] f) = DigitalFilter.Freqz(h, [1.0], 2048, sampleRate: 1000);
        double passRipple = 0;
        double stopPeak = 0;
        for (int k = 0; k < response.Length; k++)
        {
            double magnitude = response[k].Magnitude;
            if (f[k] <= 20)
            {
                passRipple = System.Math.Max(passRipple, System.Math.Abs(magnitude - 1));
            }
            else if (f[k] >= 30)
            {
                stopPeak = System.Math.Max(stopPeak, magnitude);
            }
        }

        // With a transition of only 2% of Nyquist, 128 taps buy ~26 dB (Kaiser estimate): the
        // optimal δ is ≈ 0.04. Equiripple means pass and stop levels come out equal (weights are 1).
        Assert.True(passRipple < 0.06, $"passband ripple {passRipple}");
        Assert.True(stopPeak < 0.06, $"stopband peak {stopPeak}");
        Assert.True(System.Math.Abs(passRipple - stopPeak) < 0.01,
            $"equiripple levels should match: pass {passRipple} vs stop {stopPeak}");
    }

    [Fact]
    public void Remez_TypeI_HighPassBand_Works()
    {
        // Even order → odd length (Type I): a simple two-band design with a passband at the top.
        double[] h = FirDesign.Remez(64, [0, 0.3, 0.5, 1], [0, 0, 1, 1], out bool converged);

        Assert.True(converged);
        Assert.Equal(65, h.Length);
        (Complex[] response, double[] f) = DigitalFilter.Freqz(h, [1.0], 1024, sampleRate: 2);
        for (int k = 0; k < response.Length; k++)
        {
            if (f[k] <= 0.3)
            {
                Assert.True(response[k].Magnitude < 0.02, $"stopband leak {response[k].Magnitude} at {f[k]}");
            }
            else if (f[k] >= 0.5)
            {
                Assert.True(System.Math.Abs(response[k].Magnitude - 1) < 0.02,
                    $"passband error {response[k].Magnitude} at {f[k]}");
            }
        }
    }

    [Fact]
    public void Remez_ErrorRipples_Alternate()
    {
        double[] h = FirDesign.Remez(31, [0, 0.35, 0.5, 1], [1, 1, 0, 0], out bool converged);

        Assert.True(converged);
        (Complex[] response, double[] f) = DigitalFilter.Freqz(h, [1.0], 4096, sampleRate: 2);

        // Equiripple: the passband error's extreme magnitudes should all be close to one level.
        var passbandError = new List<double>();
        for (int k = 0; k < response.Length; k++)
        {
            if (f[k] <= 0.35)
            {
                passbandError.Add(response[k].Magnitude - 1);
            }
        }

        double ripple = passbandError.Max(System.Math.Abs);
        int signChanges = 0;
        for (int i = 1; i < passbandError.Count; i++)
        {
            if (System.Math.Sign(passbandError[i]) != System.Math.Sign(passbandError[i - 1]))
            {
                signChanges++;
            }
        }

        Assert.True(ripple is > 0 and < 0.1, $"ripple {ripple}");
        Assert.True(signChanges >= 3, $"expected alternating ripple, saw {signChanges} sign changes");
    }

    // --- WAV ------------------------------------------------------------------------------------

    [Fact]
    public void Wave_16BitPcm_RoundTrips()
    {
        double[] samples = Enumerable.Range(0, 480).Select(i => 0.8 * System.Math.Sin(2 * System.Math.PI * 440 * i / 48000.0)).ToArray();
        using var stream = new MemoryStream();

        WaveFile.Write16BitPcm(stream, samples, 48000);
        stream.Position = 0;
        (double[] restored, int rate) = WaveFile.Read(stream);

        Assert.Equal(48000, rate);
        Assert.Equal(samples.Length, restored.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.True(System.Math.Abs(restored[i] - samples[i]) < 1e-4);
        }
    }

    [Fact]
    public void Wave_Float32Stereo_AveragesToMono()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            const int frames = 4;
            writer.Write("RIFF"u8);
            writer.Write(36 + (frames * 8));
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((ushort)0x0003); // IEEE float
            writer.Write((ushort)2);      // stereo
            writer.Write(8000);
            writer.Write(8000 * 8);
            writer.Write((ushort)8);
            writer.Write((ushort)32);
            writer.Write("data"u8);
            writer.Write(frames * 8);
            for (int i = 0; i < frames; i++)
            {
                writer.Write((float)(0.5 * i)); // left
                writer.Write((float)(0.1 * i)); // right
            }
        }

        stream.Position = 0;
        (double[] samples, int rate) = WaveFile.Read(stream);

        Assert.Equal(8000, rate);
        Assert.Equal(4, samples.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.True(System.Math.Abs(samples[i] - (0.3 * i)) < 1e-6);
        }
    }

    [Fact]
    public void Wave_NonWavBytes_FailClearly()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => WaveFile.Read(stream));
        Assert.Contains("RIFF", ex.Message);
    }

    // --- helpers --------------------------------------------------------------------------------

    private static Complex[] AnalyticDft(double[] signal)
    {
        int n = signal.Length;
        var result = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            Complex sum = Complex.Zero;
            for (int t = 0; t < n; t++)
            {
                double angle = -2 * System.Math.PI * k * t / n;
                sum += signal[t] * new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            result[k] = sum;
        }

        return result;
    }

    private static double[] Tone(double normalizedFrequency, int length) =>
        Enumerable.Range(0, length)
            .Select(i => System.Math.Sin(System.Math.PI * normalizedFrequency * i))
            .ToArray();

    private static double Rms(double[] samples) =>
        System.Math.Sqrt(samples.Sum(s => s * s) / samples.Length);
}
