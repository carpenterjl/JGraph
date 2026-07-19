namespace JGraph.Imaging;

/// <summary>Spatial-filtering operations: 2-D correlation/convolution and median filtering.</summary>
public static class Filters
{
    /// <summary>How samples beyond the image edge are supplied to a filter.</summary>
    public enum Boundary
    {
        /// <summary>Out-of-range samples are 0 (MATLAB default).</summary>
        Zero,

        /// <summary>Out-of-range samples replicate the nearest edge pixel.</summary>
        Replicate,

        /// <summary>Out-of-range samples mirror across the edge.</summary>
        Symmetric,
    }

    /// <summary>
    /// Correlates an image with a kernel (MATLAB <c>imfilter</c> default), producing a same-size result.
    /// Each output channel is filtered independently. The kernel is applied as-is (no flip).
    /// </summary>
    public static ImageBuffer Correlate(ImageBuffer image, double[,] kernel, Boundary boundary = Boundary.Zero)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(kernel);
        int kh = kernel.GetLength(0);
        int kw = kernel.GetLength(1);
        int anchorR = kh / 2;
        int anchorC = kw / 2;

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double acc = 0;
                    for (int kr = 0; kr < kh; kr++)
                    {
                        int sr = r + kr - anchorR;
                        for (int kc = 0; kc < kw; kc++)
                        {
                            int sc = c + kc - anchorC;
                            acc += kernel[kr, kc] * Sample(image, sr, sc, ch, boundary);
                        }
                    }

                    result[r, c, ch] = acc;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>2-D convolution of two matrices (MATLAB <c>conv2</c>), with 'full', 'same', or 'valid' shape.</summary>
    public static double[,] Convolve2(double[,] a, double[,] b, Conv2Shape shape = Conv2Shape.Full)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int ah = a.GetLength(0);
        int aw = a.GetLength(1);
        int bh = b.GetLength(0);
        int bw = b.GetLength(1);
        int fullH = ah + bh - 1;
        int fullW = aw + bw - 1;

        var full = new double[fullH, fullW];
        for (int i = 0; i < ah; i++)
        {
            for (int j = 0; j < aw; j++)
            {
                double av = a[i, j];
                if (av == 0)
                {
                    continue;
                }

                for (int m = 0; m < bh; m++)
                {
                    for (int n = 0; n < bw; n++)
                    {
                        full[i + m, j + n] += av * b[m, n];
                    }
                }
            }
        }

        return shape switch
        {
            Conv2Shape.Full => full,
            Conv2Shape.Same => Crop(full, (bh - 1) / 2, (bw - 1) / 2, ah, aw),
            Conv2Shape.Valid => ah >= bh && aw >= bw
                ? Crop(full, bh - 1, bw - 1, ah - bh + 1, aw - bw + 1)
                : new double[0, 0],
            _ => full,
        };
    }

    /// <summary>Median filter over an m×n window (MATLAB <c>medfilt2</c>), zero-padded at the edges.</summary>
    public static ImageBuffer Median(ImageBuffer image, int windowHeight = 3, int windowWidth = 3)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
        int anchorR = windowHeight / 2;
        int anchorC = windowWidth / 2;
        var window = new double[windowHeight * windowWidth];

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    int count = 0;
                    for (int wr = 0; wr < windowHeight; wr++)
                    {
                        int sr = r + wr - anchorR;
                        for (int wc = 0; wc < windowWidth; wc++)
                        {
                            int sc = c + wc - anchorC;
                            window[count++] = Sample(image, sr, sc, ch, Boundary.Zero);
                        }
                    }

                    Array.Sort(window);
                    result[r, c, ch] = window[window.Length / 2];
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static double Sample(ImageBuffer image, int r, int c, int channel, Boundary boundary)
    {
        switch (boundary)
        {
            case Boundary.Replicate:
                r = Math.Clamp(r, 0, image.Height - 1);
                c = Math.Clamp(c, 0, image.Width - 1);
                return image[r, c, channel];
            case Boundary.Symmetric:
                r = Mirror(r, image.Height);
                c = Mirror(c, image.Width);
                return image[r, c, channel];
            default:
                return (uint)r < (uint)image.Height && (uint)c < (uint)image.Width ? image[r, c, channel] : 0.0;
        }
    }

    private static int Mirror(int index, int length)
    {
        if (length == 1)
        {
            return 0;
        }

        while (index < 0 || index >= length)
        {
            if (index < 0)
            {
                index = -index - 1; // reflect across the leading edge (symmetric: abcba)
            }
            else if (index >= length)
            {
                index = (2 * length) - index - 1;
            }
        }

        return index;
    }

    private static double[,] Crop(double[,] source, int rowOffset, int colOffset, int height, int width)
    {
        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                result[r, c] = source[rowOffset + r, colOffset + c];
            }
        }

        return result;
    }
}

/// <summary>Output-size convention for <see cref="Filters.Convolve2"/>.</summary>
public enum Conv2Shape
{
    /// <summary>The full (ah+bh-1)×(aw+bw-1) convolution.</summary>
    Full,

    /// <summary>The central part the same size as the first operand.</summary>
    Same,

    /// <summary>Only the region computed without zero-padding.</summary>
    Valid,
}
