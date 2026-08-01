namespace JGraph.Imaging;

/// <summary>
/// Image arithmetic beyond the add/subtract/multiply trio in <see cref="PointOps"/>: division,
/// absolute difference, weighted combinations, and colour-channel mixing. Every result is clamped to
/// the [0, 1] sample range, which is the normalized form of MATLAB's saturating integer arithmetic —
/// the scripting layer stamps the output class and snaps the samples to its grid.
/// </summary>
public static class Arithmetic
{
    /// <summary>Divides <paramref name="a"/> by <paramref name="b"/> sample by sample (MATLAB <c>imdivide</c>).</summary>
    /// <remarks>Division by zero saturates rather than producing infinity, matching integer-class MATLAB.</remarks>
    public static ImageBuffer Divide(ImageBuffer a, ImageBuffer b) =>
        Combine(a, b, "imdivide", static (x, y) => y == 0 ? (x == 0 ? 0.0 : double.PositiveInfinity) : x / y);

    /// <summary>The absolute difference of two images (MATLAB <c>imabsdiff</c>).</summary>
    public static ImageBuffer AbsoluteDifference(ImageBuffer a, ImageBuffer b) =>
        Combine(a, b, "imabsdiff", static (x, y) => Math.Abs(x - y));

    /// <summary>Divides every sample by a constant.</summary>
    public static ImageBuffer DivideScalar(ImageBuffer image, double value)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (value == 0)
        {
            throw new ArgumentException("imdivide by zero.", nameof(value));
        }

        return Map(image, v => v / value);
    }

    /// <summary>
    /// The weighted sum <c>k1*A1 + k2*A2 + …</c>, with an optional trailing constant (MATLAB
    /// <c>imlincomb</c>). All images must match in size and channel count.
    /// </summary>
    /// <param name="weights">One weight per image, optionally with one extra trailing constant.</param>
    /// <param name="images">The images to combine; at least one.</param>
    public static ImageBuffer LinearCombination(IReadOnlyList<double> weights, IReadOnlyList<ImageBuffer> images)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("imlincomb needs at least one image.", nameof(images));
        }

        if (weights.Count != images.Count && weights.Count != images.Count + 1)
        {
            throw new ArgumentException(
                "imlincomb takes one weight per image, plus an optional trailing constant.", nameof(weights));
        }

        ImageBuffer first = images[0];
        for (int i = 1; i < images.Count; i++)
        {
            if (images[i].Height != first.Height || images[i].Width != first.Width ||
                images[i].Channels != first.Channels)
            {
                throw new ArgumentException("imlincomb requires images of matching size and channel count.");
            }
        }

        double constant = weights.Count > images.Count ? weights[images.Count] : 0.0;
        var result = new ImageBuffer(first.Height, first.Width, first.Channels);
        Span<double> dst = result.Pixels;
        dst.Fill(constant);
        for (int i = 0; i < images.Count; i++)
        {
            double weight = weights[i];
            ReadOnlySpan<double> src = images[i].Pixels;
            for (int s = 0; s < dst.Length; s++)
            {
                dst[s] += weight * src[s];
            }

            GC.KeepAlive(images[i]);
        }

        for (int s = 0; s < dst.Length; s++)
        {
            dst[s] = Math.Clamp(dst[s], 0, 1);
        }

        GC.KeepAlive(result);
        return result;
    }

    /// <summary>
    /// Mixes colour channels through a matrix (MATLAB <c>imapplymatrix</c>): output channel <c>i</c> is
    /// <c>sum_j M[i, j] * input_j</c>, plus an optional per-channel constant.
    /// </summary>
    /// <param name="matrix">An <c>outputChannels</c>-by-<c>inputChannels</c> mixing matrix.</param>
    /// <param name="image">The image whose channels are mixed.</param>
    /// <param name="offsets">One constant per output channel, or null.</param>
    public static ImageBuffer ApplyMatrix(double[,] matrix, ImageBuffer image, double[]? offsets = null)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(image);
        int outChannels = matrix.GetLength(0);
        int inChannels = matrix.GetLength(1);
        if (inChannels != image.Channels)
        {
            throw new ArgumentException(
                $"imapplymatrix needs a matrix with {image.Channels} columns for a {image.Channels}-channel image.",
                nameof(matrix));
        }

        if (outChannels is not (1 or 3))
        {
            throw new ArgumentException("imapplymatrix can produce 1 or 3 output channels.", nameof(matrix));
        }

        if (offsets is not null && offsets.Length != outChannels)
        {
            throw new ArgumentException("imapplymatrix needs one offset per output channel.", nameof(offsets));
        }

        var result = new ImageBuffer(image.Height, image.Width, outChannels);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        int pixels = image.Height * image.Width;
        for (int p = 0; p < pixels; p++)
        {
            int srcBase = p * inChannels;
            int dstBase = p * outChannels;
            for (int o = 0; o < outChannels; o++)
            {
                double sum = offsets?[o] ?? 0.0;
                for (int j = 0; j < inChannels; j++)
                {
                    sum += matrix[o, j] * src[srcBase + j];
                }

                dst[dstBase + o] = Math.Clamp(sum, 0, 1);
            }
        }

        GC.KeepAlive(image);
        GC.KeepAlive(result);
        return result;
    }

    private static ImageBuffer Map(ImageBuffer image, Func<double, double> f)
    {
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        ReadOnlySpan<double> src = image.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = Math.Clamp(f(src[i]), 0, 1);
        }

        GC.KeepAlive(image);
        return result;
    }

    private static ImageBuffer Combine(ImageBuffer a, ImageBuffer b, string name, Func<double, double, double> f)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Height != b.Height || a.Width != b.Width || a.Channels != b.Channels)
        {
            throw new ArgumentException($"{name} requires images of matching size and channel count.");
        }

        var result = new ImageBuffer(a.Height, a.Width, a.Channels);
        ReadOnlySpan<double> pa = a.Pixels;
        ReadOnlySpan<double> pb = b.Pixels;
        Span<double> dst = result.Pixels;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = Math.Clamp(f(pa[i], pb[i]), 0, 1);
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        return result;
    }
}
