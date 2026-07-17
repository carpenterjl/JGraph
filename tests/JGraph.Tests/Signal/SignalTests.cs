using System;
using System.Numerics;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Signal;

public class SignalTests
{
    // ---- FFT ----

    [Fact]
    public void Fft_PowerOfTwoRoundTrips()
    {
        var rng = new Random(1);
        var x = new Complex[16];
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = new Complex(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
        }

        Complex[] restored = Fft.Inverse(Fft.Forward(x));
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal(x[i].Real, restored[i].Real, 9);
            Assert.Equal(x[i].Imaginary, restored[i].Imaginary, 9);
        }
    }

    [Fact]
    public void Fft_NonPowerOfTwoRoundTrips()
    {
        var rng = new Random(2);
        var x = new Complex[6];
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = new Complex(rng.NextDouble(), 0);
        }

        Complex[] restored = Fft.Inverse(Fft.Forward(x));
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal(x[i].Real, restored[i].Real, 9);
            Assert.Equal(x[i].Imaginary, restored[i].Imaginary, 9);
        }
    }

    [Fact]
    public void Fft_ImpulseHasFlatSpectrum()
    {
        var x = new Complex[8];
        x[0] = Complex.One;
        Complex[] spectrum = Fft.Forward(x);
        foreach (Complex c in spectrum)
        {
            Assert.Equal(1.0, c.Magnitude, 9);
        }
    }

    [Fact]
    public void Fft_CosinePeaksAtItsBin()
    {
        const int n = 8;
        const int k = 1;
        var x = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = new Complex(System.Math.Cos(2 * System.Math.PI * k * i / n), 0);
        }

        Complex[] spectrum = Fft.Forward(x);
        Assert.Equal(n / 2.0, spectrum[k].Magnitude, 6);       // amplitude N/2 at bin k
        Assert.Equal(n / 2.0, spectrum[n - k].Magnitude, 6);   // and its mirror
        Assert.True(spectrum[3].Magnitude < 1e-9);             // nothing elsewhere
    }

    [Fact]
    public void Fft_NonPowerOfTwoMatchesNaiveDft()
    {
        var rng = new Random(5);
        const int n = 6;
        var x = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = new Complex(rng.NextDouble(), rng.NextDouble());
        }

        Complex[] fast = Fft.Forward(x);
        for (int kk = 0; kk < n; kk++)
        {
            Complex sum = Complex.Zero;
            for (int t = 0; t < n; t++)
            {
                double angle = -2 * System.Math.PI * kk * t / n;
                sum += x[t] * new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            }

            Assert.Equal(sum.Real, fast[kk].Real, 9);
            Assert.Equal(sum.Imaginary, fast[kk].Imaginary, 9);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(1024, true)]
    [InlineData(3, false)]
    [InlineData(6, false)]
    public void Fft_IsPowerOfTwo(int n, bool expected) => Assert.Equal(expected, Fft.IsPowerOfTwo(n));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(256, 256)]
    [InlineData(257, 512)]
    public void Fft_NextPowerOfTwo(int n, int expected) => Assert.Equal(expected, Fft.NextPowerOfTwo(n));

    // ---- Windows ----

    [Fact]
    public void Window_HannEndpointsAreZeroAndSymmetric()
    {
        double[] w = Window.Create(WindowType.Hann, 9);
        Assert.Equal(9, w.Length);
        Assert.Equal(0.0, w[0], 12);
        Assert.Equal(0.0, w[^1], 12);
        Assert.Equal(1.0, w[4], 12); // center of an odd-length Hann
        for (int i = 0; i < w.Length; i++)
        {
            Assert.Equal(w[i], w[^ (i + 1)], 12); // symmetric
        }
    }

    [Fact]
    public void Window_RectangularIsAllOnes()
    {
        double[] w = Window.Create(WindowType.Rectangular, 5);
        Assert.All(w, v => Assert.Equal(1.0, v, 12));
        Assert.Equal(1.0, Window.CoherentGain(w), 12);
    }

    [Fact]
    public void Window_LengthOneIsUnity() => Assert.Equal(new[] { 1.0 }, Window.Create(WindowType.Blackman, 1));

    // ---- Spectrum ----

    [Fact]
    public void Spectrum_RecoversSinusoidAmplitude()
    {
        const int n = 64;
        const double fs = 64.0;    // 1 Hz per bin
        const int bin = 8;
        const double amplitude = 2.0;
        var signal = new double[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = amplitude * System.Math.Cos(2 * System.Math.PI * bin * i / n);
        }

        SpectrumResult result = Spectrum.Compute(signal, fs, WindowType.Rectangular);
        Assert.Equal(bin, result.Frequencies[bin]); // 8 Hz
        Assert.Equal(amplitude, result.Magnitude[bin], 6);
    }

    // ---- Transfer function ----

    [Fact]
    public void TransferFunction_RcLowpassIsMinus3dbAtCutoff()
    {
        // H(s) = 1 / (s + 1): first-order low-pass with cutoff at omega = 1 rad/s.
        var tf = new TransferFunction(new[] { 1.0 }, new[] { 1.0, 1.0 });

        Complex h = tf.Evaluate(1.0);
        Assert.Equal(1.0 / System.Math.Sqrt(2), h.Magnitude, 9);

        FrequencyResponse response = tf.Response(new[] { 1.0 });
        Assert.Equal(-3.0103, response.MagnitudeDb[0], 3);
        Assert.Equal(-45.0, response.PhaseDegrees[0], 6);
    }

    [Fact]
    public void TransferFunction_DcGainIsUnity()
    {
        var tf = new TransferFunction(new[] { 1.0 }, new[] { 1.0, 1.0 });
        Complex h = tf.Evaluate(1e-6);
        Assert.Equal(1.0, h.Magnitude, 6);
    }

    [Fact]
    public void TransferFunction_LogSpaceSpansEndpoints()
    {
        double[] w = TransferFunction.LogSpace(0.1, 1000, 5);
        Assert.Equal(5, w.Length);
        Assert.Equal(0.1, w[0], 9);
        Assert.Equal(1000, w[^1], 6);
        Assert.Equal(10.0, w[2], 6); // geometric midpoint of 0.1..1000
    }

    [Fact]
    public void TransferFunction_RejectsAllZeroDenominator() =>
        Assert.Throws<ArgumentException>(() => new TransferFunction(new[] { 1.0 }, new[] { 0.0, 0.0 }));

    // ---- Spectrogram ----

    [Fact]
    public void Spectrogram_HasExpectedDimensions()
    {
        var signal = new double[1000];
        SpectrogramResult result = Spectrogram.Compute(signal, 1000, windowSize: 128, overlap: 64);

        Assert.Equal((128 / 2) + 1, result.FrequencyBins);
        int hop = 128 - 64;
        int expectedFrames = 1 + ((1000 - 128) / hop);
        Assert.Equal(expectedFrames, result.TimeFrames);
        Assert.Equal(500.0, result.FrequencyMax, 6); // Nyquist = fs/2
        Assert.Equal(1.0, result.TimeMax, 6);          // 1000 samples at 1000 Hz
    }

    [Fact]
    public void Spectrogram_ConcentratesEnergyAtToneFrequency()
    {
        const double fs = 1024.0;
        const int windowSize = 256;
        const double toneHz = 128.0;
        var signal = new double[2048];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = System.Math.Sin(2 * System.Math.PI * toneHz * i / fs);
        }

        SpectrogramResult result = Spectrogram.Compute(signal, fs, windowSize, overlap: 128);

        // Bin for the tone: freq = bin * fs / windowSize → bin = tone * windowSize / fs.
        int toneBin = (int)System.Math.Round(toneHz * windowSize / fs);
        int frame = result.TimeFrames / 2;

        int peakBin = 0;
        double peak = double.NegativeInfinity;
        for (int k = 0; k < result.FrequencyBins; k++)
        {
            if (result.MagnitudeDb[k, frame] > peak)
            {
                peak = result.MagnitudeDb[k, frame];
                peakBin = k;
            }
        }

        Assert.Equal(toneBin, peakBin);
    }
}
