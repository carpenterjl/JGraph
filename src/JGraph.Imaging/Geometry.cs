namespace JGraph.Imaging;

/// <summary>Geometric image operations: resize, rotate, crop, pyramids, and the pattern generators.</summary>
public static class Geometry
{
    /// <summary>Resampling method for <see cref="Resize"/>, <see cref="Rotate"/> and warping.</summary>
    public enum Interpolation
    {
        /// <summary>Nearest-neighbour (crisp, blocky).</summary>
        Nearest,

        /// <summary>Bilinear (smooth).</summary>
        Bilinear,

        /// <summary>Keys cubic with a = −½, MATLAB's default for <c>imresize</c> and <c>imrotate</c>.</summary>
        Bicubic,

        /// <summary>Lanczos windowed sinc, two lobes.</summary>
        Lanczos2,

        /// <summary>Lanczos windowed sinc, three lobes — the sharpest of the set.</summary>
        Lanczos3,
    }

    /// <summary>
    /// Resizes an image to <paramref name="newHeight"/>×<paramref name="newWidth"/> (MATLAB <c>imresize</c>).
    /// </summary>
    /// <param name="image">The image to resample.</param>
    /// <param name="newHeight">Output row count.</param>
    /// <param name="newWidth">Output column count.</param>
    /// <param name="method">The interpolation kernel; MATLAB's default is <see cref="Interpolation.Bicubic"/>.</param>
    /// <param name="antialiasing">
    /// Whether to widen the kernel when shrinking so the discarded detail is averaged in rather than
    /// aliased. Null follows MATLAB: on for every method but <see cref="Interpolation.Nearest"/>.
    /// </param>
    /// <remarks>
    /// Output pixel <c>x</c> (one-based) samples the input at <c>x/scale + ½(1 − 1/scale)</c> — the
    /// half-pixel-centre mapping, which is what makes resizing by a factor and back land on the
    /// original grid. Shrinking scales the kernel itself by the resize factor, so a 10:1 reduction
    /// averages ten input pixels per output pixel instead of point-sampling one in ten. Both passes
    /// are separable, so the cost is <c>(kh + kw)</c> taps per pixel rather than <c>kh·kw</c>.
    /// </remarks>
    public static ImageBuffer Resize(
        ImageBuffer image,
        int newHeight,
        int newWidth,
        Interpolation method = Interpolation.Bicubic,
        bool? antialiasing = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);

        bool antialias = antialiasing ?? method != Interpolation.Nearest;
        double rowScale = newHeight / (double)image.Height;
        double colScale = newWidth / (double)image.Width;

        (double[][] rowWeights, int[][] rowIndices) =
            Contributions(image.Height, newHeight, rowScale, method, antialias);
        (double[][] colWeights, int[][] colIndices) =
            Contributions(image.Width, newWidth, colScale, method, antialias);

        // Rows first, then columns, over an intermediate that is already the output height.
        var vertical = new ImageBuffer(newHeight, image.Width, image.Channels);
        for (int r = 0; r < newHeight; r++)
        {
            double[] w = rowWeights[r];
            int[] idx = rowIndices[r];
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < w.Length; k++)
                    {
                        sum += w[k] * image[idx[k], c, ch];
                    }

                    vertical[r, c, ch] = sum;
                }
            }
        }

        var result = new ImageBuffer(newHeight, newWidth, image.Channels);
        for (int c = 0; c < newWidth; c++)
        {
            double[] w = colWeights[c];
            int[] idx = colIndices[c];
            for (int r = 0; r < newHeight; r++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < w.Length; k++)
                    {
                        sum += w[k] * vertical[r, idx[k], ch];
                    }

                    result[r, c, ch] = sum;
                }
            }
        }

        vertical.Dispose();
        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Rotates an image counter-clockwise by <paramref name="degrees"/> about its centre (MATLAB <c>imrotate</c>).
    /// With <paramref name="loose"/> the output grows to fit the whole rotated image; otherwise it keeps the
    /// input size ('crop'). Pixels outside the source are filled with 0.
    /// </summary>
    public static ImageBuffer Rotate(
        ImageBuffer image, double degrees, Interpolation method = Interpolation.Nearest, bool loose = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        int outHeight = image.Height;
        int outWidth = image.Width;
        if (loose)
        {
            // Subtract a tiny epsilon so an exact integer extent (e.g. 2.0 + fp noise) doesn't ceil up.
            outWidth = (int)Math.Ceiling((Math.Abs(image.Width * cos)) + (Math.Abs(image.Height * sin)) - 1e-9);
            outHeight = (int)Math.Ceiling((Math.Abs(image.Width * sin)) + (Math.Abs(image.Height * cos)) - 1e-9);
            outWidth = Math.Max(1, outWidth);
            outHeight = Math.Max(1, outHeight);
        }

        double srcCenterX = (image.Width - 1) / 2.0;
        double srcCenterY = (image.Height - 1) / 2.0;
        double dstCenterX = (outWidth - 1) / 2.0;
        double dstCenterY = (outHeight - 1) / 2.0;

        var result = new ImageBuffer(outHeight, outWidth, image.Channels);
        for (int r = 0; r < outHeight; r++)
        {
            double dy = r - dstCenterY;
            for (int c = 0; c < outWidth; c++)
            {
                double dx = c - dstCenterX;
                // Map the destination pixel back into the source. Rows run downwards, so the sign
                // pattern that reads as a clockwise rotation on paper is the counter-clockwise one on
                // screen — which is the direction MATLAB's imrotate turns.
                double srcX = srcCenterX + (dx * cos) - (dy * sin);
                double srcY = srcCenterY + (dx * sin) + (dy * cos);
                if (srcX < -0.5 || srcX > image.Width - 0.5 || srcY < -0.5 || srcY > image.Height - 0.5)
                {
                    continue; // outside source → stays 0
                }

                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = Sample(image, srcY, srcX, ch, method);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Crops a rectangle from an image: <paramref name="x"/> is the 0-based left column,
    /// <paramref name="y"/> the 0-based top row, and width/height are in pixels. The rect is clamped
    /// to the image bounds.
    /// </summary>
    public static ImageBuffer Crop(ImageBuffer image, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        int col0 = Math.Clamp(x, 0, image.Width - 1);
        int row0 = Math.Clamp(y, 0, image.Height - 1);
        int col1 = Math.Clamp(col0 + width - 1, col0, image.Width - 1);
        int row1 = Math.Clamp(row0 + height - 1, row0, image.Height - 1);

        int outWidth = col1 - col0 + 1;
        int outHeight = row1 - row0 + 1;
        var result = new ImageBuffer(outHeight, outWidth, image.Channels);
        for (int r = 0; r < outHeight; r++)
        {
            for (int c = 0; c < outWidth; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = image[row0 + r, col0 + c, ch];
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// One level of a Gaussian pyramid (MATLAB <c>impyramid</c>): <paramref name="expand"/> false
    /// halves the image to <c>ceil(n/2)</c>, true doubles it to <c>2n − 1</c>.
    /// </summary>
    /// <remarks>
    /// Both directions use the Burt–Adelson five-tap kernel with a = 0.375. Expansion splits it into
    /// its two phases — <c>[⅛ ¾ ⅛]</c> for the samples that land on an input pixel and <c>[½ ½]</c>
    /// for the ones between two — which is the same filter as zero-stuffing and convolving, without
    /// the multiplications by zero.
    /// </remarks>
    public static ImageBuffer Pyramid(ImageBuffer image, bool expand)
    {
        ArgumentNullException.ThrowIfNull(image);
        double[] kernel = [0.0625, 0.25, 0.375, 0.25, 0.0625];

        if (!expand)
        {
            using ImageBuffer smoothed = SeparablePass(image, kernel, kernel);
            int outHeight = (image.Height + 1) / 2;
            int outWidth = (image.Width + 1) / 2;
            var reduced = new ImageBuffer(outHeight, outWidth, image.Channels);
            for (int r = 0; r < outHeight; r++)
            {
                for (int c = 0; c < outWidth; c++)
                {
                    for (int ch = 0; ch < image.Channels; ch++)
                    {
                        reduced[r, c, ch] = smoothed[r * 2, c * 2, ch];
                    }
                }
            }

            GC.KeepAlive(image);
            return reduced;
        }

        double[] onPixel = [0.125, 0.75, 0.125];
        double[] between = [0.5, 0.5];
        int height = (2 * image.Height) - 1;
        int width = (2 * image.Width) - 1;

        // Rows first into an intermediate, then columns — separable, as in Resize.
        var vertical = new ImageBuffer(height, image.Width, image.Channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    vertical[r, c, ch] = ExpandTap(image, r, c, ch, rows: true, onPixel, between);
                }
            }
        }

        var result = new ImageBuffer(height, width, image.Channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = ExpandTap(vertical, c, r, ch, rows: false, onPixel, between);
                }
            }
        }

        vertical.Dispose();
        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The MATLAB <c>checkerboard</c> test pattern: <paramref name="squareSize"/> pixels per square,
    /// <paramref name="rows"/>×<paramref name="cols"/> tiles of four squares each, so the image is
    /// <c>2·rows·n</c> by <c>2·cols·n</c>. The light squares in the right half are grey (0.7) rather
    /// than white, which is what makes a registration result's orientation readable at a glance.
    /// </summary>
    public static ImageBuffer Checkerboard(int squareSize, int rows, int cols)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(squareSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);

        int height = 2 * rows * squareSize;
        int width = 2 * cols * squareSize;
        var result = new ImageBuffer(height, width, 1);
        for (int r = 0; r < height; r++)
        {
            bool bottomOfTile = (r / squareSize) % 2 == 1;
            for (int c = 0; c < width; c++)
            {
                bool rightOfTile = (c / squareSize) % 2 == 1;
                if (bottomOfTile == rightOfTile)
                {
                    continue; // the dark squares of the tile
                }

                result[r, c, 0] = c < width / 2 ? 1.0 : 0.7;
            }
        }

        return result;
    }

    /// <summary>
    /// Samples an image at a fractional (row, column) with the given kernel. Taps that fall past the
    /// border are clamped to the edge pixel, or take <paramref name="outside"/> when one is given —
    /// which is how <c>imwarp</c>'s 'SmoothEdges' fades a warped border instead of smearing it.
    /// Shared by rotation and warping so every geometric operation interpolates identically.
    /// </summary>
    internal static double Sample(
        ImageBuffer image, double row, double col, int channel, Interpolation method, double? outside = null)
    {
        if (method == Interpolation.Nearest)
        {
            int nr = (int)Math.Round(row, MidpointRounding.AwayFromZero);
            int nc = (int)Math.Round(col, MidpointRounding.AwayFromZero);
            if (outside is { } beyond && (nr < 0 || nr >= image.Height || nc < 0 || nc >= image.Width))
            {
                return beyond;
            }

            return image[Clamp(nr, image.Height), Clamp(nc, image.Width), channel];
        }

        int half = (int)Math.Ceiling(KernelWidth(method) / 2.0);
        int r0 = (int)Math.Floor(row);
        int c0 = (int)Math.Floor(col);
        double sum = 0.0;
        double weight = 0.0;
        for (int dr = 1 - half; dr <= half; dr++)
        {
            double wr = Kernel(method, row - (r0 + dr));
            if (wr == 0.0)
            {
                continue;
            }

            for (int dc = 1 - half; dc <= half; dc++)
            {
                double w = wr * Kernel(method, col - (c0 + dc));
                if (w == 0.0)
                {
                    continue;
                }

                int r = r0 + dr;
                int c = c0 + dc;
                bool beyond = r < 0 || r >= image.Height || c < 0 || c >= image.Width;
                sum += w * (beyond && outside is { } value
                    ? value
                    : image[Clamp(r, image.Height), Clamp(c, image.Width), channel]);
                weight += w;
            }
        }

        return weight == 0.0 ? 0.0 : sum / weight;
    }

    /// <summary>The support width of an interpolation kernel, in input pixels.</summary>
    internal static double KernelWidth(Interpolation method) => method switch
    {
        Interpolation.Nearest => 1.0,
        Interpolation.Bilinear => 2.0,
        Interpolation.Bicubic or Interpolation.Lanczos2 => 4.0,
        _ => 6.0,
    };

    /// <summary>
    /// The resampling weights and source indices for one dimension, MATLAB's <c>contributions</c>.
    /// Indices past the edge fold back through the mirror <c>[1…n, n…1]</c>, so a border pixel is
    /// extended by reflection rather than by a hard clamp.
    /// </summary>
    private static (double[][] Weights, int[][] Indices) Contributions(
        int inLength, int outLength, double scale, Interpolation method, bool antialias)
    {
        double width = KernelWidth(method);
        bool shrinking = antialias && scale < 1.0;
        if (shrinking)
        {
            width /= scale;
        }

        int taps = (int)Math.Ceiling(width) + 2;
        int period = 2 * inLength;
        var weights = new double[outLength][];
        var indices = new int[outLength][];

        for (int x = 0; x < outLength; x++)
        {
            double u = ((x + 1) / scale) + (0.5 * (1.0 - (1.0 / scale)));
            int left = (int)Math.Floor(u - (width / 2.0));
            var w = new double[taps];
            var idx = new int[taps];
            double total = 0.0;
            for (int k = 0; k < taps; k++)
            {
                int oneBased = left + k;
                double delta = u - oneBased;
                w[k] = shrinking ? scale * Kernel(method, scale * delta) : Kernel(method, delta);
                total += w[k];

                int folded = Modulo(oneBased - 1, period);
                idx[k] = folded < inLength ? folded : period - 1 - folded;
            }

            if (total != 0.0)
            {
                for (int k = 0; k < taps; k++)
                {
                    w[k] /= total;
                }
            }

            weights[x] = w;
            indices[x] = idx;
        }

        return (weights, indices);
    }

    private static double Kernel(Interpolation method, double x) => method switch
    {
        Interpolation.Nearest => x >= -0.5 && x < 0.5 ? 1.0 : 0.0,
        Interpolation.Bilinear => Math.Abs(x) < 1.0 ? 1.0 - Math.Abs(x) : 0.0,
        Interpolation.Bicubic => Cubic(x),
        Interpolation.Lanczos2 => Lanczos(x, 2),
        _ => Lanczos(x, 3),
    };

    /// <summary>Keys' cubic convolution kernel with a = −½, which is MATLAB's 'bicubic'.</summary>
    private static double Cubic(double x)
    {
        double a = Math.Abs(x);
        double a2 = a * a;
        double a3 = a2 * a;
        if (a <= 1.0)
        {
            return (1.5 * a3) - (2.5 * a2) + 1.0;
        }

        return a <= 2.0 ? (-0.5 * a3) + (2.5 * a2) - (4.0 * a) + 2.0 : 0.0;
    }

    private static double Lanczos(double x, int lobes)
    {
        double a = Math.Abs(x);
        if (a >= lobes)
        {
            return 0.0;
        }

        if (a < 1e-12)
        {
            return 1.0;
        }

        return Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / lobes) / (Math.PI * Math.PI * x * x / lobes);
    }

    /// <summary>A separable pass with replicate borders, used by the pyramid's reduction step.</summary>
    private static ImageBuffer SeparablePass(ImageBuffer image, double[] rowKernel, double[] colKernel)
    {
        int rowAnchor = rowKernel.Length / 2;
        int colAnchor = colKernel.Length / 2;
        var vertical = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < rowKernel.Length; k++)
                    {
                        sum += rowKernel[k] * image[Clamp(r + k - rowAnchor, image.Height), c, ch];
                    }

                    vertical[r, c, ch] = sum;
                }
            }
        }

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < colKernel.Length; k++)
                    {
                        sum += colKernel[k] * vertical[r, Clamp(c + k - colAnchor, image.Width), ch];
                    }

                    result[r, c, ch] = sum;
                }
            }
        }

        vertical.Dispose();
        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// One expansion tap along a single dimension: <paramref name="along"/> is the output position in
    /// the dimension being doubled and <paramref name="across"/> the untouched one.
    /// </summary>
    private static double ExpandTap(
        ImageBuffer source, int along, int across, int channel, bool rows, double[] onPixel, double[] between)
    {
        int length = rows ? source.Height : source.Width;
        double sum = 0.0;
        if (along % 2 == 0)
        {
            int centre = along / 2;
            for (int k = 0; k < onPixel.Length; k++)
            {
                int index = Clamp(centre + k - 1, length);
                sum += onPixel[k] * (rows ? source[index, across, channel] : source[across, index, channel]);
            }

            return sum;
        }

        int lower = (along - 1) / 2;
        for (int k = 0; k < between.Length; k++)
        {
            int index = Clamp(lower + k, length);
            sum += between[k] * (rows ? source[index, across, channel] : source[across, index, channel]);
        }

        return sum;
    }

    private static int Modulo(int value, int period)
    {
        int rest = value % period;
        return rest < 0 ? rest + period : rest;
    }

    private static int Clamp(int index, int length) => Math.Clamp(index, 0, length - 1);
}
