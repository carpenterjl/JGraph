using JGraph.Imaging;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The every-row plan's I12 in the numerics layer: the IIR recurrence in registers, the threaded
/// inverse scale, the cosine pair's threaded passes and half-length road, and the real matrix's
/// two-dimensional transform. Where a road keeps the old arithmetic it is held to the old code,
/// kept verbatim here, to the bit; where it changes the rounding it is held to the old answer
/// within rounding and to the symmetry it promises exactly.
/// </summary>
public class SignalTransformsI12Tests
{
    private const double Phi = 0.618033988749895;

    private static double[] Golden(int n, double scale = 1, double shift = -0.5)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = (((i + 1) * Phi % 1) * scale) + shift;
        }

        return values;
    }

    private static void AssertBits(ReadOnlySpan<double> expected, ReadOnlySpan<double> actual, string what)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(expected[i]) != BitConverter.DoubleToInt64Bits(actual[i]))
            {
                Assert.Fail($"{what}: element {i} is {actual[i]:R}, the old road gave {expected[i]:R}");
            }
        }
    }

    // --- the recurrence ------------------------------------------------------------------------------

    /// <summary>DigitalFilter.Filter's loop as it stood before I12, over normalized, padded coefficients.</summary>
    private static double[] RecurrenceAsItWas(double[] bn, double[] an, double[] x, double[] delays)
    {
        int stateLength = delays.Length;
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double input = x[i];
            double output = (bn[0] * input) + (stateLength > 0 ? delays[0] : 0);
            for (int j = 0; j < stateLength; j++)
            {
                double next = j + 1 < stateLength ? delays[j + 1] : 0;
                delays[j] = (bn[j + 1] * input) + next - (an[j + 1] * output);
            }

            y[i] = output;
        }

        return y;
    }

    private static (double[] B, double[] A) Coefficients(int order)
    {
        // A stable all-pole part (poles at radius 0.6 and below) and an arbitrary numerator.
        var a = new double[order + 1];
        a[0] = 1;
        for (int k = 1; k <= order; k++)
        {
            a[k] = (k % 2 == 0 ? 1 : -1) * Math.Pow(0.6, k) / k;
        }

        double[] b = Golden(order + 1, 0.4, 0.05);
        return (b, a);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void TheRecurrenceIsTheOldLoopToTheBit(int order)
    {
        (double[] b, double[] a) = Coefficients(order);
        double[] x = Golden(20000, 2, -1);
        x[17] = -0.0;
        x[18] = -0.0;
        foreach (bool seeded in new[] { false, true })
        {
            double[] start = seeded ? Golden(order, 0.3, 0.1) : new double[order];
            double[] oldDelays = (double[])start.Clone();
            double[] expected = RecurrenceAsItWas(b, a, x, oldDelays);

            double[] newDelays = (double[])start.Clone();
            var actual = new double[x.Length];
            FilterKernels.Recurrence(b, a, x, actual, newDelays);
            AssertBits(expected, actual, $"order {order}, seeded {seeded}");
            AssertBits(oldDelays, newDelays, $"final delays, order {order}");
        }
    }

    [Fact]
    public void TheRecurrenceCarriesANonFiniteSampleAsTheLoopDid()
    {
        (double[] b, double[] a) = Coefficients(4);
        a[2] = 0; // a zero feedback coefficient still multiplies, as the loop's did
        double[] x = Golden(200);
        x[50] = double.PositiveInfinity;
        x[120] = double.NaN;
        double[] expected = RecurrenceAsItWas(b, a, x, new double[4]);
        var actual = new double[x.Length];
        FilterKernels.Recurrence(b, a, x, actual, new double[4]);
        AssertBits(expected, actual, "non-finite");
    }

    [Fact]
    public void DigitalFilterIsTheOldLoopWithUnpaddedCoefficients()
    {
        double[] b = [0.0201, 0.0402, 0.0201];
        double[] a = [2.0, -3.1220, 1.2828, 0.01, -0.002]; // a(1) ≠ 1 and longer than b
        double[] x = Golden(5000);
        double[] state = [0.1, -0.2, 0.05, 0.0];
        var bn = new double[5];
        var an = new double[5];
        for (int i = 0; i < b.Length; i++)
        {
            bn[i] = b[i] / a[0];
        }

        for (int i = 0; i < a.Length; i++)
        {
            an[i] = a[i] / a[0];
        }

        double[] oldDelays = (double[])state.Clone();
        double[] expected = RecurrenceAsItWas(bn, an, x, oldDelays);
        double[] newState = (double[])state.Clone();
        double[] actual = JGraph.Signal.DigitalFilter.Filter(b, a, x, newState);
        AssertBits(expected, actual, "DigitalFilter");
        AssertBits(oldDelays, newState, "DigitalFilter final state");
    }

    [Theory]
    [InlineData(1)]   // along the columns of a 300-by-7 matrix
    [InlineData(2)]   // along its rows
    public void TheRecurrenceAlongADimensionIsTheRecurrencePerSlice(int dim)
    {
        const int rows = 300, cols = 7;
        (double[] b, double[] a) = Coefficients(3);
        double[] data = Golden(rows * cols);
        double[] initial = [0.25, -0.125, 0.5];
        var split = dim == 1 ? new ReduceKernels.Split(1, rows, cols) : new ReduceKernels.Split(rows, cols, 1);
        using var src = ManagedBuffer.Adopt((double[])data.Clone());
        using var dst = ManagedBuffer.Adopt(new double[data.Length]);
        using var finals = ManagedBuffer.Adopt(new double[split.Slices * 3]);
        FilterKernels.RecurrenceAlong(src, dst, finals, split, b, a, initial);

        int count = split.Count;
        for (int s = 0; s < split.Slices; s++)
        {
            int at = ((s / split.Inner) * split.Inner * count) + (s % split.Inner);
            var slice = new double[count];
            for (int j = 0; j < count; j++)
            {
                slice[j] = data[at + (j * split.Inner)];
            }

            double[] delays = (double[])initial.Clone();
            double[] expected = RecurrenceAsItWas(b, a, slice, delays);
            var actual = new double[count];
            var finalActual = new double[3];
            int place = ((s / split.Inner) * split.Inner * 3) + (s % split.Inner);
            for (int j = 0; j < count; j++)
            {
                actual[j] = dst.AsSpan()[at + (j * split.Inner)];
            }

            for (int j = 0; j < 3; j++)
            {
                finalActual[j] = finals.AsSpan()[place + (j * split.Inner)];
            }

            AssertBits(expected, actual, $"slice {s}");
            AssertBits(delays, finalActual, $"slice {s} final");
        }
    }

    // --- the inverse scale ---------------------------------------------------------------------------

    [Theory]
    [InlineData(3 << 17, true)]   // a long mixed-radix length, threaded
    [InlineData(3 << 17, false)]
    [InlineData(1000, true)]      // short: the vector loop alone
    public void TheThreadedInverseScaleIsTheDivisionLoop(int n, bool inside)
    {
        double[] re = Golden(n);
        double[] im = Golden(n, 1, -0.25);
        double[] expectedRe = (double[])re.Clone();
        double[] expectedIm = (double[])im.Clone();
        MixedRadixFft.Transform(expectedRe, expectedIm, n, inverse: true, inside: false);
        for (int i = 0; i < n; i++)
        {
            expectedRe[i] /= n;
            expectedIm[i] /= n;
        }

        FftKernels.Transform(re, im, n, inverse: true, inside);
        AssertBits(expectedRe, re, "re");
        AssertBits(expectedIm, im, "im");
    }

    // --- the cosine pair -----------------------------------------------------------------------------

    /// <summary>CosineTransforms.Forward as it stood before I12: serial passes, a length-n transform.</summary>
    private static double[] ForwardAsItWas(double[] values)
    {
        int n = values.Length;
        var re = new double[n];
        var im = new double[n];
        int half = (n + 1) / 2;
        for (int j = 0; j < half; j++)
        {
            re[j] = values[2 * j];
        }

        for (int j = 0; j < n / 2; j++)
        {
            re[n - 1 - j] = values[(2 * j) + 1];
        }

        FftKernels.Transform(re, im, n, inverse: false, inside: false);
        var result = new double[n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double angle = -Math.PI * k / (2.0 * n);
            double unnormalized = (re[k] * Math.Cos(angle)) - (im[k] * Math.Sin(angle));
            result[k] = unnormalized * (k == 0 ? first : rest);
        }

        return result;
    }

    /// <summary>CosineTransforms.Inverse as it stood before I12.</summary>
    private static double[] InverseAsItWas(double[] coefficients)
    {
        int n = coefficients.Length;
        var re = new double[n];
        var im = new double[n];
        double first = Math.Sqrt(n);
        double rest = Math.Sqrt(n / 2.0);
        re[0] = coefficients[0] * first;
        for (int k = 1; k < n; k++)
        {
            double a = coefficients[k] * rest;
            double b = coefficients[n - k] * rest;
            double angle = Math.PI * k / (2.0 * n);
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            re[k] = (a * c) + (b * s);
            im[k] = (a * s) - (b * c);
        }

        FftKernels.Transform(re, im, n, inverse: true, inside: false);
        var result = new double[n];
        int half = (n + 1) / 2;
        for (int j = 0; j < half; j++)
        {
            result[2 * j] = re[j];
        }

        for (int j = 0; j < n / 2; j++)
        {
            result[(2 * j) + 1] = re[n - 1 - j];
        }

        return result;
    }

    [Theory]
    [InlineData(7)]
    [InlineData(4095)]               // even lengths below the half road's floor keep the full road
    [InlineData(4094)]
    [InlineData((3 << 17) + 1)]      // odd and long: the full road, its passes threaded
    public void TheFullRoadIsTheOldTransformToTheBit(int n)
    {
        double[] x = Golden(n);
        foreach (bool inside in new[] { false, true })
        {
            var forward = new double[n];
            CosineTransforms.Forward(x, forward, inside);
            AssertBits(ForwardAsItWas(x), forward, $"dct n={n} inside={inside}");

            var back = new double[n];
            CosineTransforms.Inverse(forward, back, inside);
            AssertBits(InverseAsItWas(forward), back, $"idct n={n} inside={inside}");
        }
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4098)]
    [InlineData(6000)]              // even, not smooth to a power of two
    [InlineData(1 << 19)]
    [InlineData(4000000)]
    public void TheHalfRoadIsTheTransformToRoundingAndUndoesItself(int n)
    {
        double[] x = Golden(n, 2, -1);
        double[] expected = ForwardAsItWas(x);
        double scale = 0;
        foreach (double v in expected)
        {
            scale = Math.Max(scale, Math.Abs(v));
        }

        var serial = new double[n];
        var threaded = new double[n];
        CosineTransforms.Forward(x, serial, inside: false);
        CosineTransforms.Forward(x, threaded, inside: true);
        AssertBits(serial, threaded, "the half road does not depend on threads");
        for (int k = 0; k < n; k++)
        {
            Assert.True(Math.Abs(serial[k] - expected[k]) <= 64 * Math.Sqrt(Math.Log2(n)) * 1e-16 * scale,
                $"dct coefficient {k}: {serial[k]:R} against {expected[k]:R}");
        }

        var back = new double[n];
        var backThreaded = new double[n];
        CosineTransforms.Inverse(serial, back, inside: false);
        CosineTransforms.Inverse(serial, backThreaded, inside: true);
        AssertBits(back, backThreaded, "the half inverse does not depend on threads");
        double[] oldBack = InverseAsItWas(serial);
        for (int i = 0; i < n; i++)
        {
            Assert.True(Math.Abs(back[i] - oldBack[i]) <= 1e-13, $"idct sample {i}: {back[i]:R} against {oldBack[i]:R}");
            Assert.True(Math.Abs(back[i] - x[i]) <= 1e-13, $"round trip sample {i}");
        }
    }

    // --- the real matrix's fft2 ----------------------------------------------------------------------

    [Theory]
    [InlineData(16, 16, false)]
    [InlineData(256, 256, true)]
    [InlineData(64, 1024, true)]
    [InlineData(2048, 32, true)]
    [InlineData(48, 2048, false)]    // a side that is not a power of two
    [InlineData(65536, 16, false)]   // a side the tile kernel factors
    public void TheRealRoadTakesTheShapesItSays(int rows, int cols, bool fits) =>
        Assert.Equal(fits, FftKernels.FitsReal2(rows, cols));

    [Theory]
    [InlineData(256, 256)]
    [InlineData(64, 1024)]
    [InlineData(2048, 32)]
    [InlineData(512, 512)]
    public void TheRealMatrixTransformIsTheGeneralOneToRoundingAndExactlyHermitian(int m, int n)
    {
        double[] x = Golden(m * n, 2, -1);
        using var src = ManagedBuffer.Adopt(x);
        using var re = ManagedBuffer.Adopt(new double[m * n]);
        using var im = ManagedBuffer.Adopt(new double[m * n]);
        FftKernels.RealForward2(src, re, im, m, n);

        // The general transform: every column, then every row, as complex sequences.
        var gr = (double[])x.Clone();
        var gi = new double[m * n];
        var colRe = new double[m];
        var colIm = new double[m];
        for (int c = 0; c < n; c++)
        {
            Array.Copy(gr, c * m, colRe, 0, m);
            Array.Clear(colIm);
            FftKernels.Transform(colRe, colIm, m, inverse: false);
            Array.Copy(colRe, 0, gr, c * m, m);
            Array.Copy(colIm, 0, gi, c * m, m);
        }

        var rowRe = new double[n];
        var rowIm = new double[n];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                rowRe[c] = gr[(c * m) + r];
                rowIm[c] = gi[(c * m) + r];
            }

            FftKernels.Transform(rowRe, rowIm, n, inverse: false);
            for (int c = 0; c < n; c++)
            {
                gr[(c * m) + r] = rowRe[c];
                gi[(c * m) + r] = rowIm[c];
            }
        }

        double scale = 0;
        for (int i = 0; i < m * n; i++)
        {
            scale = Math.Max(scale, Math.Abs(gr[i]) + Math.Abs(gi[i]));
        }

        Span<double> ar = re.AsSpan();
        Span<double> ai = im.AsSpan();
        for (int i = 0; i < m * n; i++)
        {
            Assert.True(Math.Abs(ar[i] - gr[i]) <= 1e-14 * scale && Math.Abs(ai[i] - gi[i]) <= 1e-14 * scale,
                $"element {i}: ({ar[i]:R}, {ai[i]:R}) against ({gr[i]:R}, {gi[i]:R})");
        }

        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                int mirror = ((((n - c) % n) * m) + ((m - r) % m));
                int here = (c * m) + r;
                Assert.Equal(BitConverter.DoubleToInt64Bits(ar[here]), BitConverter.DoubleToInt64Bits(ar[mirror]));
                Assert.Equal(ai[here], -ai[mirror]);
            }
        }

        Assert.Equal(0.0, ai[0]);
        Assert.Equal(0.0, ai[m / 2]);
        Assert.Equal(0.0, ai[(n / 2) * m]);
        Assert.Equal(0.0, ai[((n / 2) * m) + (m / 2)]);
    }
}
