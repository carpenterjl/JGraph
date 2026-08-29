using System.Numerics;
using System.Numerics.Tensors;

namespace JGraph.Numerics;

/// <summary>How much of a convolution the caller wants back.</summary>
public enum ConvolutionShape
{
    /// <summary>Everything: one shorter than the two lengths added, along every dimension.</summary>
    Full,

    /// <summary>The middle part, the same size as the first operand.</summary>
    Same,

    /// <summary>Only what no zero padding contributed to.</summary>
    Valid,
}

/// <summary>
/// Direct convolution of sequences and of N-dimensional arrays, with the three shapes MATLAB names.
/// </summary>
/// <remarks>
/// <para>
/// Direct, not transformed. A convolution can be done through an FFT and for long enough operands
/// that is far cheaper, but the two do not agree to the bit: the transform route rounds through the
/// frequency domain and leaves a floor of dust where the direct sum leaves exact zeros. MATLAB's own
/// <c>conv</c> is direct, so a transform here would show up as a divergence on every test that
/// convolves a short filter with anything — which is most of them.
/// </para>
/// <para>
/// The inner loop is written as one scaled accumulation per tap rather than one dot product per
/// output sample. Both compute the same sum, but the accumulation form walks both arrays forwards
/// and hands whole spans to the vector unit, where the dot-product form re-reads one operand
/// backwards for every output. The tap loop is over the shorter operand for the same reason.
/// </para>
/// </remarks>
public static class Convolution
{
    /// <summary>The full convolution of two sequences.</summary>
    /// <param name="u">One sequence.</param>
    /// <param name="v">The other.</param>
    /// <returns>
    /// A sequence one shorter than the two lengths added. Empty when either operand is empty.
    /// </returns>
    public static double[] Convolve(ReadOnlySpan<double> u, ReadOnlySpan<double> v)
    {
        if (u.Length == 0 || v.Length == 0)
        {
            return [];
        }

        var result = new double[u.Length + v.Length - 1];

        // Scale the longer operand by each tap of the shorter and add it in at the tap's offset.
        ReadOnlySpan<double> taps = u.Length <= v.Length ? u : v;
        ReadOnlySpan<double> signal = u.Length <= v.Length ? v : u;

        for (int t = 0; t < taps.Length; t++)
        {
            double tap = taps[t];
            if (tap == 0)
            {
                // Not merely a shortcut: skipping keeps a zero tap from turning an infinity in the
                // signal into a NaN, which is what 0·Inf would contribute.
                continue;
            }

            Span<double> into = result.AsSpan(t, signal.Length);
            if (tap == 1)
            {
                TensorPrimitives.Add<double>(into, signal, into);
                continue;
            }

            TensorPrimitives.MultiplyAdd<double>(signal, tap, into, into);
        }

        return result;
    }

    /// <summary>
    /// The full convolution of two complex sequences — the same sum, without the vector unit, which
    /// has no complex form to offer.
    /// </summary>
    /// <param name="u">One sequence.</param>
    /// <param name="v">The other.</param>
    /// <returns>A sequence one shorter than the two lengths added.</returns>
    public static Complex[] Convolve(ReadOnlySpan<Complex> u, ReadOnlySpan<Complex> v)
    {
        if (u.Length == 0 || v.Length == 0)
        {
            return [];
        }

        var result = new Complex[u.Length + v.Length - 1];
        for (int i = 0; i < u.Length; i++)
        {
            Complex left = u[i];
            if (left == Complex.Zero)
            {
                continue;
            }

            for (int j = 0; j < v.Length; j++)
            {
                result[i + j] += left * v[j];
            }
        }

        return result;
    }

    /// <summary>The convolution of two complex sequences, cut to the requested shape.</summary>
    /// <param name="u">The first sequence; <see cref="ConvolutionShape.Same"/> matches its length.</param>
    /// <param name="v">The second.</param>
    /// <param name="shape">How much to keep.</param>
    /// <returns>The requested part of the full convolution.</returns>
    public static Complex[] Convolve(
        ReadOnlySpan<Complex> u, ReadOnlySpan<Complex> v, ConvolutionShape shape)
    {
        Complex[] full = Convolve(u, v);
        if (shape == ConvolutionShape.Full)
        {
            return full;
        }

        (int offset, int length) = Window(u.Length, v.Length, shape);
        return length <= 0 ? [] : full.AsSpan(offset, length).ToArray();
    }

    /// <summary>The convolution of two sequences, cut to the requested shape.</summary>
    /// <param name="u">The first sequence; <see cref="ConvolutionShape.Same"/> matches its length.</param>
    /// <param name="v">The second.</param>
    /// <param name="shape">How much to keep.</param>
    /// <returns>The requested part of the full convolution.</returns>
    public static double[] Convolve(
        ReadOnlySpan<double> u, ReadOnlySpan<double> v, ConvolutionShape shape)
    {
        double[] full = Convolve(u, v);
        if (shape == ConvolutionShape.Full)
        {
            return full;
        }

        (int offset, int length) = Window(u.Length, v.Length, shape);
        return length <= 0 ? [] : full.AsSpan(offset, length).ToArray();
    }

    /// <summary>
    /// The convolution of two arrays of any rank, in column-major order with explicit dimensions.
    /// </summary>
    /// <param name="a">The first array's elements, column-major.</param>
    /// <param name="aDims">Its dimensions.</param>
    /// <param name="b">The second array's elements, column-major.</param>
    /// <param name="bDims">Its dimensions.</param>
    /// <param name="shape">How much of the answer to keep.</param>
    /// <returns>The answer and its dimensions, column-major.</returns>
    public static (double[] Values, int[] Dims) ConvolveN(
        ReadOnlySpan<double> a, int[] aDims,
        ReadOnlySpan<double> b, int[] bDims,
        ConvolutionShape shape)
    {
        int rank = Math.Max(aDims.Length, bDims.Length);
        int[] an = Padded(aDims, rank);
        int[] bn = Padded(bDims, rank);

        var fullDims = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            fullDims[d] = Math.Max(Math.Max(an[d] + bn[d] - 1, an[d]), bn[d]);
        }

        var keepDims = new int[rank];
        var offsets = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            (offsets[d], keepDims[d]) = shape switch
            {
                ConvolutionShape.Full => (0, fullDims[d]),
                _ => Window(an[d], bn[d], shape),
            };

            if (keepDims[d] < 0)
            {
                keepDims[d] = 0;
            }
        }

        long kept = 1;
        foreach (int n in keepDims)
        {
            kept *= n;
        }

        if (kept == 0 || a.Length == 0 || b.Length == 0)
        {
            return (new double[Math.Max(kept, 0)], keepDims);
        }

        var full = new double[Product(fullDims)];
        int[] aStride = Strides(an);
        int[] fullStride = Strides(fullDims);

        // One pass over B: each of its elements scales the whole of A into the answer at that
        // element's offset. Same shape as the 1-D tap loop, one dimension at a time.
        var bIndex = new int[rank];
        for (int j = 0; j < b.Length; j++)
        {
            double tap = b[j];
            if (tap != 0)
            {
                int baseOffset = 0;
                for (int d = 0; d < rank; d++)
                {
                    baseOffset += bIndex[d] * fullStride[d];
                }

                Scatter(a, an, aStride, full, fullStride, rank, baseOffset, tap);
            }

            Advance(bIndex, bn);
        }

        if (shape == ConvolutionShape.Full)
        {
            return (full, fullDims);
        }

        var result = new double[kept];
        var at = new int[rank];
        for (int i = 0; i < result.Length; i++)
        {
            int from = 0;
            for (int d = 0; d < rank; d++)
            {
                from += (at[d] + offsets[d]) * fullStride[d];
            }

            result[i] = full[from];
            Advance(at, keepDims);
        }

        return (result, keepDims);
    }

    /// <summary>
    /// Where a cut shape starts in the full convolution, and how long it is, along one dimension.
    /// </summary>
    private static (int Offset, int Length) Window(int na, int nb, ConvolutionShape shape) =>
        shape == ConvolutionShape.Same
            ? (nb / 2, na)
            : (Math.Max(nb - 1, 0), Math.Max(na - Math.Max(nb - 1, 0), 0));

    /// <summary>Adds A, scaled by one tap, into the answer at a fixed offset.</summary>
    private static void Scatter(
        ReadOnlySpan<double> a, int[] an, int[] aStride,
        double[] full, int[] fullStride, int rank, int baseOffset, double tap)
    {
        // The first dimension is contiguous in both, so it runs as one span; the rest walk.
        int run = an[0];
        var index = new int[rank];
        int columns = a.Length / Math.Max(run, 1);

        for (int c = 0; c < columns; c++)
        {
            int from = 0;
            int to = baseOffset;
            for (int d = 1; d < rank; d++)
            {
                from += index[d] * aStride[d];
                to += index[d] * fullStride[d];
            }

            ReadOnlySpan<double> source = a.Slice(from, run);
            Span<double> into = full.AsSpan(to, run);
            if (tap == 1)
            {
                TensorPrimitives.Add<double>(into, source, into);
            }
            else
            {
                TensorPrimitives.MultiplyAdd<double>(source, tap, into, into);
            }

            // Advance every dimension but the first, which the span above consumed whole.
            for (int d = 1; d < rank; d++)
            {
                if (++index[d] < an[d])
                {
                    break;
                }

                index[d] = 0;
            }
        }
    }

    private static void Advance(int[] index, int[] dims)
    {
        for (int d = 0; d < dims.Length; d++)
        {
            if (++index[d] < dims[d])
            {
                return;
            }

            index[d] = 0;
        }
    }

    private static int[] Strides(int[] dims)
    {
        var strides = new int[dims.Length];
        int running = 1;
        for (int d = 0; d < dims.Length; d++)
        {
            strides[d] = running;
            running *= dims[d];
        }

        return strides;
    }

    private static int Product(int[] dims)
    {
        int product = 1;
        foreach (int n in dims)
        {
            product *= n;
        }

        return product;
    }

    private static int[] Padded(int[] dims, int rank)
    {
        if (dims.Length == rank)
        {
            return dims;
        }

        var padded = new int[rank];
        Array.Fill(padded, 1);
        dims.CopyTo(padded, 0);
        return padded;
    }
}
