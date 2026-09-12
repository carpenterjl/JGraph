using System.Numerics;
using JGraph.Api;
using JGraph.Imaging;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// ADR 0153, stage 07a: the cosine transform's packed road and Bluestein's plan cache change no
/// bits. The boxed road they replace is kept here as <see cref="OldForward"/> and
/// <see cref="OldInverse"/> — the <see cref="Complex"/>-array code as it stood — so every claim of
/// "the same answer" is checked against it rather than assumed. Threading inside one transform is
/// checked against the serial road the same way, and the plan cache against its own budget.
/// </summary>
[Collection("JG facade")]
public class TransformRoadTests : IDisposable
{
    public TransformRoadTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    public static TheoryData<int> Lengths() => new() { 2, 3, 8, 33, 100, 1000, 4097, 32768, 65536, 100000 };

    [Theory]
    [MemberData(nameof(Lengths))]
    public void ThePlanarForwardAnswersTheBoxedRoadBitForBit(int n)
    {
        double[] x = Signal(n, seed: 3);
        double[] want = OldForward(x);
        double[] got = CosineTransforms.Forward(x);
        AssertSameBits(want, got);

        var threaded = new double[n];
        CosineTransforms.Forward(x, threaded, inside: true);
        AssertSameBits(want, threaded);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void ThePlanarInverseAnswersTheBoxedRoadBitForBit(int n)
    {
        double[] c = Signal(n, seed: 11);
        double[] want = OldInverse(c);
        double[] got = CosineTransforms.Inverse(c);
        AssertSameBits(want, got);

        var threaded = new double[n];
        CosineTransforms.Inverse(c, threaded, inside: true);
        AssertSameBits(want, threaded);
    }

    [Theory]
    [InlineData(100000)]
    [InlineData(200000)]
    [InlineData(65537)]
    public void ThreadingInsideBluesteinKeepsTheSerialBits(int n)
    {
        foreach (bool inverse in new[] { false, true })
        {
            double[] re = Signal(n, seed: 5);
            double[] im = Signal(n, seed: 7);
            double[] re2 = (double[])re.Clone();
            double[] im2 = (double[])im.Clone();
            FftKernels.Transform(re, im, n, inverse);
            FftKernels.Transform(re2, im2, n, inverse, inside: true);
            AssertSameBits(re, re2);
            AssertSameBits(im, im2);
        }
    }

    [Fact]
    public void ACachedPlanAnswersWhatAFreshOneDoes()
    {
        const int n = 12345;
        FftKernels.ClearPlanCache();
        double[] re = Signal(n, seed: 1);
        double[] im = Signal(n, seed: 2);
        double[] first = (double[])re.Clone();
        double[] firstIm = (double[])im.Clone();
        FftKernels.Transform(first, firstIm, n, inverse: false);
        Assert.Equal(1, FftKernels.PlanCacheCount);

        double[] again = (double[])re.Clone();
        double[] againIm = (double[])im.Clone();
        FftKernels.Transform(again, againIm, n, inverse: false);
        Assert.Equal(1, FftKernels.PlanCacheCount);
        AssertSameBits(first, again);
        AssertSameBits(firstIm, againIm);

        FftKernels.BluesteinPlan plan = FftKernels.BluesteinPlanFor(n, inverse: false);
        Assert.Equal(8L * ((2L * n) + (2L * plan.M)), plan.Bytes);
        Assert.Equal(plan.Bytes, FftKernels.PlanCacheBytes);
    }

    [Fact]
    public void ThePlanCacheKeepsToItsBudgetAndNeverHoldsAnOversizedPlan()
    {
        FftKernels.ClearPlanCache();

        // n ≈ 600k pads to m = 2^21, so a plan is 8·(1.2M + 4.2M) ≈ 43 MB; eight of them exceed the
        // 256 MB budget and the least recently used must go.
        var lengths = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            lengths.Add(600001 + i);
        }

        foreach (int n in lengths)
        {
            FftKernels.BluesteinPlan plan = FftKernels.BluesteinPlanFor(n, inverse: false);
            Assert.True(plan.Bytes <= FftKernels.PlanCacheEntryLimit, $"{n}: {plan.Bytes}");
            Assert.True(FftKernels.PlanCacheBytes <= FftKernels.PlanCacheBudget, $"{n}: {FftKernels.PlanCacheBytes} bytes cached");
        }

        Assert.True(FftKernels.PlanCacheCount < lengths.Count, "eviction never happened");
        Assert.True(FftKernels.PlanCacheCount >= 5, $"only {FftKernels.PlanCacheCount} plans kept");

        // A plan over 64 MB — n ≈ 1.3M pads to 4M — is built and dropped, never cached.
        int before = FftKernels.PlanCacheCount;
        long bytesBefore = FftKernels.PlanCacheBytes;
        FftKernels.BluesteinPlan big = FftKernels.BluesteinPlanFor(1_300_000, inverse: false);
        Assert.True(big.Bytes > FftKernels.PlanCacheEntryLimit);
        Assert.False(big.Cached);
        Assert.Equal(before, FftKernels.PlanCacheCount);
        Assert.Equal(bytesBefore, FftKernels.PlanCacheBytes);
        big.Release();

        // And a single-call plan answers what the cached road answers, pooled arrays and all.
        const int single = 1_100_000; // m = 2^22: 8·(2.2M + 8.4M) ≈ 85 MB, over the limit
        double[] sr = Signal(single, seed: 9);
        double[] si = Signal(single, seed: 10);
        double[] sr2 = (double[])sr.Clone();
        double[] si2 = (double[])si.Clone();
        FftKernels.Transform(sr, si, single, inverse: false);
        FftKernels.Transform(sr2, si2, single, inverse: false, inside: true);
        AssertSameBits(sr, sr2);
        AssertSameBits(si, si2);
        Assert.Equal(before, FftKernels.PlanCacheCount);

        FftKernels.ClearPlanCache();
        Assert.Equal(0, FftKernels.PlanCacheCount);
        Assert.Equal(0L, FftKernels.PlanCacheBytes);
    }

    [Fact]
    public void ThePackedDctAnswersTheSlicedRoadBitForBit()
    {
        // One contiguous packed column takes the new road; the same column as half of an n-by-2
        // matrix is two slices and takes the road of before. Both must print the same bits, as must
        // a row vector, and idct must come back through the same pair.
        const string script = """
            x = mod((1:5000)' * 0.618033988749895, 1) - 0.5;
            a = dct(x);
            b = dct([x, 2*x]);
            r = dct(x');
            fprintf('%d %d %d\n', isequal(a, b(:, 1)), isequal(a', r), isequal(idct(a), idct(b(:, 1))));
            fprintf('%s %s\n', num2hex(a(2)), num2hex(b(2, 1)));
            fprintf('%s %s\n', mat2str(size(a)), mat2str(size(r)));
            fprintf('%d\n', max(abs(idct(a) - x)) < 1e-12);
            """;
        string printed = RunMatlabDialect(script).Trim();
        string[] lines = printed.Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Equal("1 1 1", lines[0]);
        string[] hex = lines[1].Split(' ');
        Assert.Equal(hex[0], hex[1]);
        Assert.Equal("[5000 1] [1 5000]", lines[2]);
        Assert.Equal("1", lines[3]);
    }

    [Fact]
    public void ThePackedRoadLeavesPaddingCroppingAndOtherTypesToTheRoadOfBefore()
    {
        const string script = """
            x = mod((1:100)' * 0.618033988749895, 1);
            fprintf('%s %s %s %s\n', mat2str(size(dct(x, 64))), mat2str(size(dct(x, 128))), ...
                mat2str(size(dct(x, [], 2))), class(dct(single(x))));
            d1 = dct(x, 'Type', 1); d4 = dct(x, 'Type', 4);
            fprintf('%d %d\n', max(abs(dct(d1, 'Type', 1) - x)) < 1e-12, max(abs(dct(d4, 'Type', 4) - x)) < 1e-12);
            fprintf('%d\n', isequal(dct(x, 100), dct(x)));
            """;
        string[] lines = RunMatlabDialect(script).Trim().Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Equal("[64 1] [128 1] [100 1] single", lines[0]);
        Assert.Equal("1 1", lines[1]);
        Assert.Equal("1", lines[2]);
    }

    // --- the boxed road as it was, kept as the oracle ------------------------------------------

    private static double[] OldForward(ReadOnlySpan<double> values)
    {
        int n = values.Length;
        var extended = new Complex[2 * n];
        for (int i = 0; i < n; i++)
        {
            extended[i] = new Complex(values[i], 0);
            extended[(2 * n) - 1 - i] = new Complex(values[i], 0);
        }

        BoxedTransform(extended, inverse: false);

        var result = new double[n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double angle = -Math.PI * k / (2.0 * n);
            double half = (extended[k] * Complex.FromPolarCoordinates(1, angle)).Real / 2.0;
            result[k] = half * (k == 0 ? first : rest);
        }

        return result;
    }

    private static double[] OldInverse(ReadOnlySpan<double> coefficients)
    {
        int n = coefficients.Length;
        var spectrum = new Complex[2 * n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double weight = coefficients[k] * (k == 0 ? first : rest);
            spectrum[k] = Complex.FromPolarCoordinates(weight, Math.PI * k / (2.0 * n));
        }

        BoxedTransform(spectrum, inverse: true);

        var result = new double[n];
        for (int j = 0; j < n; j++)
        {
            result[j] = spectrum[j].Real * 2 * n;
        }

        return result;
    }

    /// <summary>What <c>Fft.Transform(Complex[])</c> does: split, the serial planar kernel, join.</summary>
    private static void BoxedTransform(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = buffer[i].Real;
            im[i] = buffer[i].Imaginary;
        }

        FftKernels.Transform(re, im, n, inverse);
        for (int i = 0; i < n; i++)
        {
            buffer[i] = new Complex(re[i], im[i]);
        }
    }

    private static double[] Signal(int n, int seed)
    {
        var x = new double[n];
        double phi = 0.618033988749895;
        for (int i = 0; i < n; i++)
        {
            double t = ((i + 1) * phi * (seed + 1)) % 1.0;
            x[i] = Math.Sin(2 * Math.PI * 0.01 * i) + (t - 0.5);
        }

        return x;
    }

    private static void AssertSameBits(double[] want, double[] got)
    {
        Assert.Equal(want.Length, got.Length);
        for (int i = 0; i < want.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(want[i]) != BitConverter.DoubleToInt64Bits(got[i]))
            {
                Assert.Fail($"element {i}: {got[i]:R} is not {want[i]:R} to the bit");
            }
        }
    }

    private static string RunMatlabDialect(string code)
    {
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
