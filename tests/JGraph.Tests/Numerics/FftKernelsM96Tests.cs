using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M96a: the transform moves off boxed <see cref="Complex"/> arrays and onto planar storage, and the
/// claim that licenses it is that nothing about the answer moved with it. For every length the
/// direct road takes — which is every length under 32K points, and every awkward length whatever its
/// size — the new kernel is asserted <em>bit for bit</em> against the transform it replaces, which is
/// kept here as <see cref="Reference"/> so that it stays available to say so. Above 32K points the
/// kernel factors the sum a different way and rounds differently; that road is checked against the
/// reference within a tolerance, against itself at one thread and at sixteen, and against the
/// identity a transform and its inverse make.
/// </summary>
public class FftKernelsM96Tests
{
    /// <summary>Lengths the direct road takes: powers of two, tiny sums, and Bluestein's.</summary>
    public static TheoryData<int> DirectLengths() => new()
    {
        1, 2, 3, 4, 5, 7, 8, 15, 16, 31, 32, 33, 64, 100, 128, 256, 360, 512, 1000, 1024, 4096, 32768,
    };

    [Theory]
    [MemberData(nameof(DirectLengths))]
    public void EveryLengthTheDirectRoadTakesAnswersTheOldTransformBitForBit(int n)
    {
        foreach (bool inverse in new[] { false, true })
        {
            Complex[] input = Signal(n, seed: n * 7);
            Complex[] want = Reference(input, inverse);

            double[] re = new double[n];
            double[] im = new double[n];
            Split(input, re, im);
            FftKernels.Transform(re, im, n, inverse);

            for (int i = 0; i < n; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(want[i].Real),
                    BitConverter.DoubleToInt64Bits(re[i]));
                Assert.Equal(BitConverter.DoubleToInt64Bits(want[i].Imaginary),
                    BitConverter.DoubleToInt64Bits(im[i]));
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ABatchOfSignalsAnswersWhatEachOneAloneWouldHave(int lanes)
    {
        foreach (int n in new[] { 1, 2, 8, 64, 1024 })
        {
            foreach (bool inverse in new[] { false, true })
            {
                var singles = new Complex[lanes][];
                for (int l = 0; l < lanes; l++)
                {
                    singles[l] = Reference(Signal(n, seed: (n * 31) + l), inverse);
                }

                double[] re = new double[n * lanes];
                double[] im = new double[n * lanes];
                for (int l = 0; l < lanes; l++)
                {
                    Complex[] one = Signal(n, seed: (n * 31) + l);
                    for (int t = 0; t < n; t++)
                    {
                        re[(t * lanes) + l] = one[t].Real;
                        im[(t * lanes) + l] = one[t].Imaginary;
                    }
                }

                FftKernels.TransformBatch(re, im, n, lanes, inverse);

                for (int l = 0; l < lanes; l++)
                {
                    for (int t = 0; t < n; t++)
                    {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(singles[l][t].Real),
                            BitConverter.DoubleToInt64Bits(re[(t * lanes) + l]));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(singles[l][t].Imaginary),
                            BitConverter.DoubleToInt64Bits(im[(t * lanes) + l]));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(1 << 16)]
    [InlineData(1 << 17)]
    public void AFactoredLengthAgreesWithTheOldTransformToWithinItsOwnPrecision(int n)
    {
        Assert.True(FftKernels.IsFactored(n));
        Complex[] input = Signal(n, seed: 5);
        Complex[] want = Reference(input, inverse: false);

        double[] re = new double[n];
        double[] im = new double[n];
        Split(input, re, im);
        FftKernels.Transform(re, im, n, inverse: false);

        double scale = 0;
        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            scale = Math.Max(scale, want[i].Magnitude);
            worst = Math.Max(worst, Complex.Abs(new Complex(re[i], im[i]) - want[i]));
        }

        Assert.True(worst <= scale * 1e-13, $"factored answer drifted {worst / scale:E3} from the old one");
    }

    [Theory]
    [InlineData(1 << 16)]
    [InlineData(1 << 18)]
    public void AFactoredLengthAnswersTheSameBitsAtOneThreadAsAtSixteen(int n)
    {
        int was = ParallelKernels.MaxDegree;
        try
        {
            Complex[] input = Signal(n, seed: 11);
            ParallelKernels.MaxDegree = 1;
            (double[] oneRe, double[] oneIm) = Run(input, n);
            ParallelKernels.MaxDegree = 16;
            (double[] manyRe, double[] manyIm) = Run(input, n);

            for (int i = 0; i < n; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(oneRe[i]),
                    BitConverter.DoubleToInt64Bits(manyRe[i]));
                Assert.Equal(BitConverter.DoubleToInt64Bits(oneIm[i]),
                    BitConverter.DoubleToInt64Bits(manyIm[i]));
            }
        }
        finally
        {
            ParallelKernels.MaxDegree = was;
        }

        static (double[] Re, double[] Im) Run(Complex[] input, int n)
        {
            using var src = new ManagedBuffer(n);
            using var srcIm = new ManagedBuffer(n);
            using var dst = new ManagedBuffer(n);
            using var dstIm = new ManagedBuffer(n);
            Split(input, src.AsSpan(), srcIm.AsSpan());
            FftKernels.Transform(src, srcIm, 0, dst, dstIm, 0, n, inverse: false, inside: true);
            return (dst.AsSpan().ToArray(), dstIm.AsSpan().ToArray());
        }
    }

    [Theory]
    [InlineData(1 << 16)]
    [InlineData(1 << 17)]
    public void AFactoredLengthComesBackWhereItStartedThroughItsOwnInverse(int n)
    {
        Complex[] input = Signal(n, seed: 3);
        double[] re = new double[n];
        double[] im = new double[n];
        Split(input, re, im);
        FftKernels.Transform(re, im, n, inverse: false);
        FftKernels.Transform(re, im, n, inverse: true);

        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            worst = Math.Max(worst, Complex.Abs(new Complex(re[i], im[i]) - input[i]));
        }

        Assert.True(worst < 1e-12, $"round trip drifted by {worst:E3}");
    }

    /// <summary>Shapes that cover both layouts a slice can have, and both ways a length can move.</summary>
    public static TheoryData<int, int, int, int> Slices() => new()
    {
        { 1, 8, 5, 8 },     // contiguous slices, unchanged length
        { 1, 8, 5, 4 },     // contiguous slices, cut short
        { 1, 8, 5, 16 },    // contiguous slices, padded out
        { 3, 8, 2, 8 },     // interleaved, read with a stride
        { 3, 6, 2, 8 },     // interleaved and padded
        { 11, 4, 1, 4 },    // wider than one tile of lanes
        { 1, 12, 3, 12 },   // an awkward length, Bluestein's road
        { 5, 1, 3, 1 },     // slices of one element
        { 1, 1, 1, 1 },     // a scalar
        { 2, 5, 3, 7 },     // awkward, padded, interleaved
    };

    [Theory]
    [MemberData(nameof(Slices))]
    public void ATransformAlongADimensionAnswersWhatEachSliceAloneWouldHave(
        int inner, int count, int outer, int n)
    {
        var split = new ReduceKernels.Split(inner, count, outer);
        int total = (int)split.Total;
        foreach (bool inverse in new[] { false, true })
        {
            foreach (bool real in new[] { true, false })
            {
                Complex[] flat = Signal(total, seed: (inner * 17) + (count * 5) + outer + n);
                if (real)
                {
                    for (int i = 0; i < total; i++)
                    {
                        flat[i] = new Complex(flat[i].Real, 0);
                    }
                }

                using var srcRe = new ManagedBuffer(total);
                using var srcIm = new ManagedBuffer(total);
                Split(flat, srcRe.AsSpan(), srcIm.AsSpan());

                int outTotal = inner * n * outer;
                using var dstRe = new ManagedBuffer(outTotal);
                using var dstIm = new ManagedBuffer(outTotal);
                FftKernels.TransformAlong(
                    srcRe, real ? null : srcIm, dstRe, dstIm, split, n, inverse, symmetric: false);

                Span<double> gotRe = dstRe.AsSpan();
                Span<double> gotIm = dstIm.AsSpan();
                for (int s = 0; s < split.Slices; s++)
                {
                    var slice = new Complex[n];
                    int at = ((s / inner) * inner * count) + (s % inner);
                    for (int j = 0; j < Math.Min(n, count); j++)
                    {
                        slice[j] = flat[at + (j * inner)];
                    }

                    Complex[] want = Reference(slice, inverse);
                    int to = ((s / inner) * inner * n) + (s % inner);
                    for (int j = 0; j < n; j++)
                    {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(want[j].Real),
                            BitConverter.DoubleToInt64Bits(gotRe[to + (j * inner)]));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(want[j].Imaginary),
                            BitConverter.DoubleToInt64Bits(gotIm[to + (j * inner)]));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void TheSymmetryFlagForcesTheSpectrumHermitianAndTheAnswerReal(int n)
    {
        var split = new ReduceKernels.Split(1, n, 3);
        int total = n * 3;
        Complex[] flat = Signal(total, seed: 91);

        using var srcRe = new ManagedBuffer(total);
        using var srcIm = new ManagedBuffer(total);
        Split(flat, srcRe.AsSpan(), srcIm.AsSpan());
        using var dstRe = new ManagedBuffer(total);
        using var dstIm = new ManagedBuffer(total);
        FftKernels.TransformAlong(srcRe, srcIm, dstRe, dstIm, split, n, inverse: true, symmetric: true);

        Span<double> gotIm = dstIm.AsSpan();
        for (int i = 0; i < total; i++)
        {
            Assert.Equal(0.0, gotIm[i]);
        }

        for (int s = 0; s < 3; s++)
        {
            var slice = new Complex[n];
            Array.Copy(flat, s * n, slice, 0, n);
            Hermitian(slice);
            Complex[] want = Reference(slice, inverse: true);
            for (int j = 0; j < n; j++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(want[j].Real),
                    BitConverter.DoubleToInt64Bits(dstRe.AsSpan()[(s * n) + j]));
            }
        }
    }

    [Fact]
    public void TheBoxedDoorAndThePlanarKernelAreTheSameTransform()
    {
        foreach (int n in new[] { 6, 64, 100, 4096 })
        {
            Complex[] input = Signal(n, seed: 404);
            Complex[] boxed = (Complex[])input.Clone();
            JGraph.Signal.Fft.Transform(boxed, inverse: false);

            double[] re = new double[n];
            double[] im = new double[n];
            Split(input, re, im);
            FftKernels.Transform(re, im, n, inverse: false);

            for (int i = 0; i < n; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(boxed[i].Real),
                    BitConverter.DoubleToInt64Bits(re[i]));
                Assert.Equal(BitConverter.DoubleToInt64Bits(boxed[i].Imaginary),
                    BitConverter.DoubleToInt64Bits(im[i]));
            }
        }
    }

    // --- the oracle -------------------------------------------------------------------------------

    /// <summary>
    /// The transform as it stood before M96a — an in-place radix-2 over bit-reversed
    /// <see cref="Complex"/> data with Bluestein's for awkward lengths and a direct sum for tiny
    /// ones. Kept verbatim so that "the answers did not change" is something a test can check rather
    /// than something a commit message can claim.
    /// </summary>
    private static Complex[] Reference(Complex[] input, bool inverse)
    {
        var buffer = (Complex[])input.Clone();
        int n = buffer.Length;
        if (n <= 1)
        {
            return buffer;
        }

        if ((n & (n - 1)) == 0)
        {
            Radix2(buffer, n, inverse);
        }
        else if (n <= 32)
        {
            DirectDft(buffer, inverse);
        }
        else
        {
            Bluestein(buffer, inverse);
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                buffer[i] /= n;
            }
        }

        return buffer;
    }

    private static void Radix2(Complex[] buffer, int n, bool inverse)
    {
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        int half = n >> 1;
        var twiddles = new Complex[Math.Max(half, 1)];
        double step = (inverse ? 2.0 : -2.0) * Math.PI / n;
        for (int k = 0; k < half; k++)
        {
            double angle = step * k;
            twiddles[k] = new Complex(Math.Cos(angle), Math.Sin(angle));
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            int halfLen = len >> 1;
            int stride = n / len;
            for (int i = 0; i < n; i += len)
            {
                for (int k = 0; k < halfLen; k++)
                {
                    Complex u = buffer[i + k];
                    Complex v = buffer[i + k + halfLen] * twiddles[k * stride];
                    buffer[i + k] = u + v;
                    buffer[i + k + halfLen] = u - v;
                }
            }
        }
    }

    private static void Bluestein(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        double sign = inverse ? 1.0 : -1.0;
        int m = 1;
        while (m < (2 * n) - 1)
        {
            m <<= 1;
        }

        var chirp = new Complex[n];
        var a = new Complex[m];
        var b = new Complex[m];
        long modulus = 2L * n;
        for (int j = 0; j < n; j++)
        {
            long j2 = (long)j * j % modulus;
            double angle = sign * Math.PI * j2 / n;
            chirp[j] = new Complex(Math.Cos(angle), Math.Sin(angle));
        }

        for (int j = 0; j < n; j++)
        {
            a[j] = buffer[j] * chirp[j];
        }

        b[0] = Complex.Conjugate(chirp[0]);
        for (int j = 1; j < n; j++)
        {
            b[j] = b[m - j] = Complex.Conjugate(chirp[j]);
        }

        Radix2(a, m, inverse: false);
        Radix2(b, m, inverse: false);
        for (int j = 0; j < m; j++)
        {
            a[j] *= b[j];
        }

        Radix2(a, m, inverse: true);
        for (int k = 0; k < n; k++)
        {
            buffer[k] = a[k] / m * chirp[k];
        }
    }

    private static void DirectDft(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        var result = new Complex[n];
        double sign = inverse ? 1.0 : -1.0;
        double baseAngle = sign * 2.0 * Math.PI / n;
        for (int k = 0; k < n; k++)
        {
            Complex sum = Complex.Zero;
            for (int t = 0; t < n; t++)
            {
                double angle = baseAngle * k * t;
                sum += buffer[t] * new Complex(Math.Cos(angle), Math.Sin(angle));
            }

            result[k] = sum;
        }

        Array.Copy(result, buffer, n);
    }

    private static void Hermitian(Complex[] spectrum)
    {
        int n = spectrum.Length;
        spectrum[0] = new Complex(spectrum[0].Real, 0);
        if (n % 2 == 0)
        {
            spectrum[n / 2] = new Complex(spectrum[n / 2].Real, 0);
        }

        for (int i = 1; i < (n + 1) / 2; i++)
        {
            spectrum[n - i] = Complex.Conjugate(spectrum[i]);
        }
    }

    // --- fixtures ---------------------------------------------------------------------------------

    /// <summary>A deterministic broadband signal — no two samples alike, none of them round.</summary>
    private static Complex[] Signal(int n, int seed)
    {
        var signal = new Complex[n];
        double phi = 0.618033988749895;
        for (int i = 0; i < n; i++)
        {
            double t = ((i + seed) * phi) % 1.0;
            signal[i] = new Complex(
                Math.Sin(6.2831853 * 3 * t) + (0.25 * t) - 0.1,
                Math.Cos(6.2831853 * 5 * t) - (0.4 * t));
        }

        return signal;
    }

    private static void Split(IReadOnlyList<Complex> from, Span<double> re, Span<double> im)
    {
        for (int i = 0; i < from.Count; i++)
        {
            re[i] = from[i].Real;
            im[i] = from[i].Imaginary;
        }
    }
}
