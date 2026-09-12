using System.Globalization;
using System.Text.Json;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// ADR 0154: a 5-smooth length takes the Stockham mixed-radix road instead of Bluestein's, which
/// moves the last bits of every such transform. Forward and inverse are accepted independently
/// against the 30-digit reference <c>tools/transforms/fft_reference.py</c> wrote
/// (<c>fft_reference.json</c>: every bin at five small lengths that between them use every radix
/// and every mixture, two bins at 4,000,000) and against closed-form inputs at production lengths;
/// the Bluestein road it replaced is measured against the same reference so the ADR can say which
/// was the more accurate. Threaded and serial passes must answer the same bits, and a length that
/// is not smooth must still take Bluestein's road unchanged. In the facade collection because the
/// plan cache is one static, and <see cref="TransformRoadTests"/> asserts on its counts.
/// </summary>
[Collection("JG facade")]
public class MixedRadixReferenceTests
{
    private static readonly Lazy<JsonDocument> Reference = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Numerics", "fft_reference.json"))));

    public static TheoryData<int> FullLengths() => new() { 96, 100, 360, 1000, 1080 };

    [Fact]
    public void TheSmoothTestIsExact()
    {
        Assert.True(MixedRadixFft.IsSmooth(96));
        Assert.True(MixedRadixFft.IsSmooth(4_000_000));
        Assert.True(MixedRadixFft.IsSmooth(3_981_312));
        Assert.False(MixedRadixFft.IsSmooth(1));
        Assert.False(MixedRadixFft.IsSmooth(7 * 32));
        Assert.False(MixedRadixFft.IsSmooth(4_000_037));
        Assert.Equal(new[] { 4, 4, 4, 4, 5, 5, 5, 5, 5, 5 }, MixedRadixFft.Radices(4_000_000));
        Assert.Equal(new[] { 4, 2, 3, 5 }, MixedRadixFft.Radices(120));
    }

    [Theory]
    [MemberData(nameof(FullLengths))]
    public void EveryBinAgreesWithTheReference(int n)
    {
        JsonElement entry = Reference.Value.RootElement.GetProperty("full").GetProperty(n.ToString(CultureInfo.InvariantCulture));
        double[] x = CosineReferenceTests.Lcg(n);
        foreach ((string name, bool inverse) in new[] { ("forward", false), ("inverse", true) })
        {
            double[] wantRe = Doubles(entry.GetProperty(name).GetProperty("re"));
            double[] wantIm = Doubles(entry.GetProperty(name).GetProperty("im"));

            (double[] re, double[] im) = Unscaled(x, n, inverse, bluestein: false);
            (double[] oldRe, double[] oldIm) = Unscaled(x, n, inverse, bluestein: true);
            double fresh = WorstRelative(re, im, wantRe, wantIm);
            double old = WorstRelative(oldRe, oldIm, wantRe, wantIm);
            Report($"full {n} {name}: new {fresh:E2} bluestein {old:E2}");
            Assert.True(fresh <= 1e-12, $"n = {n} {name}: {fresh:E3} from the reference (Bluestein: {old:E3})");
        }
    }

    [Fact]
    public void SelectedBinsAgreeWithTheReferenceAtTheProductionLength()
    {
        const int n = 4_000_000;
        JsonElement entry = Reference.Value.RootElement.GetProperty("selected").GetProperty(n.ToString(CultureInfo.InvariantCulture));
        double[] x = CosineReferenceTests.Lcg(n);
        foreach ((string name, bool inverse) in new[] { ("forward", false), ("inverse", true) })
        {
            (double[] re, double[] im) = Unscaled(x, n, inverse, bluestein: false);
            (double[] oldRe, double[] oldIm) = Unscaled(x, n, inverse, bluestein: true);
            double scale = 0;
            for (int i = 0; i < n; i++)
            {
                scale = Math.Max(scale, Math.Abs(re[i]) + Math.Abs(im[i]));
            }

            foreach (JsonProperty p in entry.GetProperty(name).EnumerateObject())
            {
                int k = int.Parse(p.Name, CultureInfo.InvariantCulture);
                double wr = p.Value.GetProperty("re").GetDouble();
                double wi = p.Value.GetProperty("im").GetDouble();
                double fresh = Math.Max(Math.Abs(re[k] - wr), Math.Abs(im[k] - wi)) / scale;
                double old = Math.Max(Math.Abs(oldRe[k] - wr), Math.Abs(oldIm[k] - wi)) / scale;
                Report($"selected {n} {name} k={k}: new {fresh:E2} bluestein {old:E2}");
                Assert.True(fresh <= 1e-11, $"n = {n} {name} k = {k}: ({re[k]:R}, {im[k]:R}) is not ({wr:R}, {wi:R}); rel {fresh:E3}");
            }
        }
    }

    [Theory]
    [InlineData(4_000_000)]
    [InlineData(3_981_312)]
    [InlineData(1_000_000)]
    public void ClosedFormInputsAnswerTheirClosedFormsAtProductionLength(int n)
    {
        var re = new double[n];
        var im = new double[n];

        // One complex exponential at k0 transforms to n at bin k0 and nothing elsewhere.
        foreach (int k0 in new[] { 1, 7, n / 2 })
        {
            for (int j = 0; j < n; j++)
            {
                (re[j], im[j]) = Turn((long)j * k0, n);
            }

            FftKernels.Transform(re, im, n, inverse: false, inside: true);
            Assert.True(Math.Abs(re[k0] - n) <= 1e-10 * n && Math.Abs(im[k0]) <= 1e-10 * n, $"exp k0 = {k0}: bin is ({re[k0]:R}, {im[k0]:R})");
            AssertRestSmall(re, im, k0, 1e-10 * n, $"exp k0 = {k0}");

            // The mirror: one bin in, the exponential out (the inverse is scaled by 1/n).
            Array.Clear(re);
            Array.Clear(im);
            re[k0] = n;
            FftKernels.Transform(re, im, n, inverse: true, inside: true);
            double worst = 0;
            for (int j = 0; j < n; j++)
            {
                (double wr, double wi) = Turn((long)j * k0, n);
                worst = Math.Max(worst, Math.Max(Math.Abs(re[j] - wr), Math.Abs(im[j] - wi)));
            }

            Assert.True(worst <= 1e-10, $"inverse of one bin k0 = {k0}: off by {worst:E3}");
        }

        // An impulse at j0: bin k is e^{−2πi·k·j0/n}, magnitude one everywhere.
        Array.Clear(re);
        Array.Clear(im);
        const int j0 = 12345;
        re[j0] = 1;
        FftKernels.Transform(re, im, n, inverse: false, inside: true);
        double worstImpulse = 0;
        for (int k = 0; k < n; k++)
        {
            (double wr, double wi) = Turn(-(long)k * j0, n);
            worstImpulse = Math.Max(worstImpulse, Math.Max(Math.Abs(re[k] - wr), Math.Abs(im[k] - wi)));
        }

        Assert.True(worstImpulse <= 1e-10, $"impulse: off by {worstImpulse:E3}");
    }

    [Theory]
    [InlineData(4_000_000)]
    [InlineData(3_981_312)]
    [InlineData(1_000_000)]
    [InlineData(1080)]
    public void TheRoundTripHoldsAndThreadsChangeNoBits(int n)
    {
        double[] x = CosineReferenceTests.Lcg(n);
        var re = (double[])x.Clone();
        var im = new double[n];
        FftKernels.Transform(re, im, n, inverse: false, inside: true);

        var sr = (double[])x.Clone();
        var si = new double[n];
        FftKernels.Transform(sr, si, n, inverse: false, inside: false);
        Assert.True(re.AsSpan().SequenceEqual(sr) && im.AsSpan().SequenceEqual(si), "threaded and serial passes answered different bits");

        FftKernels.Transform(re, im, n, inverse: true, inside: true);
        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            worst = Math.Max(worst, Math.Max(Math.Abs(re[i] - x[i]), Math.Abs(im[i])));
        }

        Report($"roundtrip {n}: {worst:E2}");
        Assert.True(worst <= 1e-12, $"n = {n}: round trip off by {worst:E3}");
    }

    [Fact]
    public void ALengthWithALargerPrimeFactorStillTakesBluesteinsRoadUnchanged()
    {
        const int n = 7 * 1024;
        double[] x = CosineReferenceTests.Lcg(n);
        (double[] re, double[] im) = Unscaled(x, n, inverse: false, bluestein: false);
        (double[] oldRe, double[] oldIm) = Unscaled(x, n, inverse: false, bluestein: true);
        Assert.True(re.AsSpan().SequenceEqual(oldRe) && im.AsSpan().SequenceEqual(oldIm), "7·1024 left Bluestein's road");
    }

    [Fact]
    public void AMixedRadixPlanIsCachedUnderTheSameBudgetAsBluesteins()
    {
        FftKernels.ClearPlanCache();
        double[] x = CosineReferenceTests.Lcg(1080);
        Unscaled(x, 1080, inverse: false, bluestein: false);
        Assert.Equal(1, FftKernels.PlanCacheCount);
        Assert.Equal(MixedRadixFft.Plan.BytesFor(1080), FftKernels.PlanCacheBytes);
        Unscaled(x, 1080, inverse: false, bluestein: false);
        Assert.Equal(1, FftKernels.PlanCacheCount);
        FftKernels.ClearPlanCache();
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>The unscaled transform of a real input on the road asked for: the inverse's 1/n undone.</summary>
    private static (double[] Re, double[] Im) Unscaled(double[] x, int n, bool inverse, bool bluestein)
    {
        var re = (double[])x.Clone();
        var im = new double[n];
        if (bluestein)
        {
            FftKernels.BluesteinTransform(re, im, n, inverse, inside: false);
        }
        else
        {
            FftKernels.Transform(re, im, n, inverse, inside: false);
            if (inverse)
            {
                for (int i = 0; i < n; i++)
                {
                    re[i] *= n;
                    im[i] *= n;
                }
            }
        }

        return (re, im);
    }

    /// <summary>e^{2πi·numerator/n} with the numerator reduced modulo n first, so the angle is one turn at most.</summary>
    private static (double Re, double Im) Turn(long numerator, int n)
    {
        long reduced = ((numerator % n) + n) % n;
        double angle = 2.0 * Math.PI * reduced / n;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    private static double[] Doubles(JsonElement array)
    {
        var values = new double[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            values[i++] = e.GetDouble();
        }

        return values;
    }

    /// <summary>The largest |got − want| over both planes, relative to the largest |want| over both.</summary>
    private static double WorstRelative(double[] re, double[] im, double[] wantRe, double[] wantIm)
    {
        double scale = 0;
        double worst = 0;
        for (int i = 0; i < wantRe.Length; i++)
        {
            scale = Math.Max(scale, Math.Max(Math.Abs(wantRe[i]), Math.Abs(wantIm[i])));
            worst = Math.Max(worst, Math.Max(Math.Abs(re[i] - wantRe[i]), Math.Abs(im[i] - wantIm[i])));
        }

        return worst / scale;
    }

    private static void AssertRestSmall(double[] re, double[] im, int except, double bound, string what)
    {
        double worst = 0;
        int at = -1;
        for (int k = 0; k < re.Length; k++)
        {
            double v = Math.Max(Math.Abs(re[k]), Math.Abs(im[k]));
            if (k != except && v > worst)
            {
                worst = v;
                at = k;
            }
        }

        Assert.True(worst <= bound, $"{what}: bin {at} is {worst:E3}, bound {bound:E3}");
    }

    /// <summary>With JGRAPH_FFT_REPORT set to a path, one line per measurement for the ADR.</summary>
    private static void Report(string line)
    {
        if (Environment.GetEnvironmentVariable("JGRAPH_FFT_REPORT") is { Length: > 0 } path)
        {
            lock (Reference)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }
}
