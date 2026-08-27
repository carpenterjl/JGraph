using System;
using System.Collections.Generic;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M96b: a filter with nothing in its denominator past <c>a(1)</c> leaves the transposed recurrence
/// and becomes a sum of taps per output. The claim is that the sum is the recurrence's own — same
/// operands, same order, same rounding — so the answers are the same bits, and the tests here say so
/// against <see cref="Recurrence"/>, which is the loop as it stood. What they also pin down is the
/// one place the answers do change: an infinity or a NaN in the input used to poison every later
/// sample, because the recurrence multiplied the output by a zero coefficient and zero times an
/// infinity is not zero.
/// </summary>
public class FilterKernelsM96Tests
{
    public static TheoryData<int, int> Shapes() => new()
    {
        { 1, 1 },     // one tap, one sample
        { 1, 40 },    // one tap
        { 2, 40 },    // a difference
        { 5, 4 },     // more taps than samples
        { 5, 5 },     // exactly as many
        { 8, 64 },
        { 21, 300 },
        { 64, 1000 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AFeedForwardFilterAnswersTheRecurrenceBitForBit(int taps, int samples)
    {
        foreach (double[] a in Denominators())
        {
            double[] b = Coefficients(taps, seed: taps + samples);
            double[] x = Signal(samples, seed: taps * 3);
            foreach (bool carried in new[] { false, true })
            {
                int order = Math.Max(a.Length, b.Length);
                double[] zi = carried ? Coefficients(order - 1, seed: 77) : new double[order - 1];

                var wantState = (double[])zi.Clone();
                double[] want = Recurrence(b, a, x, wantState);

                var gotState = (double[])zi.Clone();
                double[] got = JGraph.Signal.DigitalFilter.Filter(b, a, x, gotState);

                AssertSameBits(want, got, $"taps {taps} samples {samples} carried {carried}");
                AssertSameBits(wantState, gotState, $"final state, taps {taps} samples {samples}");
            }
        }
    }

    [Fact]
    public void TheKernelAnswersTheSameWhicheverRangeOfOutputsItIsAskedFor()
    {
        double[] b = Coefficients(11, seed: 5);
        double[] x = Signal(200, seed: 9);
        double[] zi = Coefficients(10, seed: 3);

        var whole = new double[x.Length];
        FilterKernels.FeedForward(b, x, zi, whole, 0, x.Length);

        foreach (int cut in new[] { 1, 7, 10, 11, 64, 199 })
        {
            var piecemeal = new double[x.Length];
            FilterKernels.FeedForward(b, x, zi, piecemeal, 0, cut);
            FilterKernels.FeedForward(b, x, zi, piecemeal, cut, x.Length);
            AssertSameBits(whole, piecemeal, $"cut at {cut}");
        }
    }

    [Fact]
    public void ASliceOfAPackedArrayFiltersLikeTheSliceOnItsOwn()
    {
        foreach ((int inner, int n, int outer) in new[] { (1, 40, 3), (4, 25, 2), (7, 9, 1), (1, 300, 1) })
        {
            var split = new ReduceKernels.Split(inner, n, outer);
            int total = (int)split.Total;
            double[] b = Coefficients(6, seed: inner + n);
            double[] a = [2.0];
            double[] zi = Coefficients(5, seed: 21);
            double[] flat = Signal(total, seed: outer + 4);

            int order = Math.Max(a.Length, b.Length);
            var taps = new double[order];
            for (int i = 0; i < b.Length; i++)
            {
                taps[i] = b[i] / a[0];
            }

            using var src = ManagedBuffer.Adopt((double[])flat.Clone());
            using var dst = new ManagedBuffer(total);
            using var finals = new ManagedBuffer((order - 1) * split.Slices);
            FilterKernels.FeedForwardAlong(src, dst, finals, split, taps, zi);

            Span<double> got = dst.AsSpan();
            for (int s = 0; s < split.Slices; s++)
            {
                int at = ((s / inner) * inner * n) + (s % inner);
                var slice = new double[n];
                for (int j = 0; j < n; j++)
                {
                    slice[j] = flat[at + (j * inner)];
                }

                var wantState = (double[])zi.Clone();
                double[] want = Recurrence(b, a, slice, wantState);
                for (int j = 0; j < n; j++)
                {
                    Assert.Equal(
                        BitConverter.DoubleToInt64Bits(want[j]),
                        BitConverter.DoubleToInt64Bits(got[at + (j * inner)]));
                }

                int state = inner == 1
                    ? s * (order - 1)
                    : ((s / inner) * inner * (order - 1)) + (s % inner);
                for (int j = 0; j < order - 1; j++)
                {
                    int where = inner == 1 ? state + j : state + (j * inner);
                    Assert.Equal(
                        BitConverter.DoubleToInt64Bits(wantState[j]),
                        BitConverter.DoubleToInt64Bits(finals.AsSpan()[where]));
                }
            }
        }
    }

    [Fact]
    public void AFeedForwardFilterAnswersTheSameBitsAtOneThreadAsAtSixteen()
    {
        int was = ParallelKernels.MaxDegree;
        try
        {
            double[] b = Coefficients(33, seed: 12);
            double[] x = Signal(400_000, seed: 6);
            var split = new ReduceKernels.Split(1, x.Length, 1);

            ParallelKernels.MaxDegree = 1;
            double[] one = Run(x, split, b);
            ParallelKernels.MaxDegree = 16;
            double[] many = Run(x, split, b);
            AssertSameBits(one, many, "thread count");
        }
        finally
        {
            ParallelKernels.MaxDegree = was;
        }

        static double[] Run(double[] x, ReduceKernels.Split split, double[] taps)
        {
            using var src = ManagedBuffer.Adopt((double[])x.Clone());
            using var dst = new ManagedBuffer(x.Length);
            FilterKernels.FeedForwardAlong(src, dst, null, split, taps, new double[taps.Length - 1]);
            return dst.AsSpan().ToArray();
        }
    }

    /// <summary>
    /// The one deliberate change. A filter with no feedback cannot carry a value further than its
    /// own length; the recurrence carried it forever, because it formed <c>0 · y</c> for every
    /// coefficient it had been given as zero and that product is NaN when the output is not finite.
    /// </summary>
    [Fact]
    public void ANaNReachesExactlyAsFarAsTheFilterIsLong()
    {
        double[] b = [0.5, 0.25, 0.25];
        double[] x = [1, 2, double.NaN, 4, 5, 6, 7, 8];
        double[] got = JGraph.Signal.DigitalFilter.Filter(b, [1.0], x);

        Assert.False(double.IsNaN(got[1]));
        Assert.True(double.IsNaN(got[2]));
        Assert.True(double.IsNaN(got[3]));
        Assert.True(double.IsNaN(got[4]));
        Assert.False(double.IsNaN(got[5]));
        Assert.Equal((0.5 * 6) + (0.25 * 5) + (0.25 * 4), got[5]);

        double[] boxed = Recurrence(b, [1.0], x, new double[2]);
        Assert.True(double.IsNaN(boxed[7])); // what it used to do, kept here so the change is visible
    }

    [Fact]
    public void ADenominatorWithRealFeedbackStillWalksTheRecurrence()
    {
        double[] b = [0.0201, 0.0402, 0.0201];
        double[] a = [1.0, -1.5610, 0.6414];
        Assert.False(FilterKernels.IsFeedForward(a));
        Assert.True(FilterKernels.IsFeedForward([1.0]));
        Assert.True(FilterKernels.IsFeedForward([2.5, 0, 0]));
        Assert.False(FilterKernels.IsFeedForward([1.0, 0, -0.5]));

        double[] x = Signal(500, seed: 2);
        var wantState = new double[2];
        double[] want = Recurrence(b, a, x, wantState);
        var gotState = new double[2];
        double[] got = JGraph.Signal.DigitalFilter.Filter(b, a, x, gotState);
        AssertSameBits(want, got, "IIR untouched");
        AssertSameBits(wantState, gotState, "IIR state untouched");
    }

    // --- the oracle -------------------------------------------------------------------------------

    /// <summary>The direct-form-II-transposed recurrence as it stood before M96b.</summary>
    private static double[] Recurrence(double[] b, double[] a, double[] x, double[] state)
    {
        double a0 = a[0];
        int order = Math.Max(a.Length, b.Length);
        int stateLength = order - 1;
        var bn = new double[order];
        var an = new double[order];
        var delays = new double[Math.Max(stateLength, 1)];
        for (int i = 0; i < stateLength && i < state.Length; i++)
        {
            delays[i] = state[i];
        }

        for (int i = 0; i < b.Length; i++)
        {
            bn[i] = b[i] / a0;
        }

        for (int i = 0; i < a.Length; i++)
        {
            an[i] = a[i] / a0;
        }

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

        for (int i = 0; i < stateLength && i < state.Length; i++)
        {
            state[i] = delays[i];
        }

        return y;
    }

    private static IEnumerable<double[]> Denominators() => [[1.0], [2.5], [1.0, 0.0], [-0.75, 0, 0]];

    private static double[] Coefficients(int n, int seed)
    {
        var taps = new double[Math.Max(n, 0)];
        for (int i = 0; i < taps.Length; i++)
        {
            taps[i] = Math.Sin(((i + seed) * 0.7) + 0.3) / (1 + i);
        }

        return taps;
    }

    private static double[] Signal(int n, int seed)
    {
        var x = new double[n];
        double phi = 0.618033988749895;
        for (int i = 0; i < n; i++)
        {
            x[i] = (((i + seed) * phi) % 1.0) - 0.5 + Math.Sin(i * 0.05);
        }

        return x;
    }

    private static void AssertSameBits(double[] want, double[] got, string what)
    {
        Assert.Equal(want.Length, got.Length);
        for (int i = 0; i < want.Length; i++)
        {
            Assert.True(
                BitConverter.DoubleToInt64Bits(want[i]) == BitConverter.DoubleToInt64Bits(got[i]),
                $"{what}: element {i} was {got[i]:R}, wanted {want[i]:R}");
        }
    }
}
