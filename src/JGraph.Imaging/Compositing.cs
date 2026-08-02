namespace JGraph.Imaging;

/// <summary>
/// Putting pictures next to each other and reading values out of one: the montage grid, the two-image
/// composites behind <c>imfuse</c> and <c>imshowpair</c>, the samples along a line, and the samples at
/// named points.
/// </summary>
/// <remarks>
/// These exist because looking at two pictures one after the other is a poor way to compare them —
/// the eye is very good at judging alignment and very bad at remembering brightness — so the toolbox
/// puts the comparison in one frame. A montage does it by tiling, a false-colour fuse by giving each
/// picture its own colour channels so anywhere they agree comes out grey, and a difference image by
/// subtracting. None of the three is more correct than the others; each makes a different kind of
/// disagreement obvious.
/// </remarks>
public static class Compositing
{
    /// <summary>How <see cref="Fuse"/> combines two pictures.</summary>
    public enum FuseMethod
    {
        /// <summary>Each picture drives its own colour channels, so agreement is grey.</summary>
        FalseColor,

        /// <summary>An even alpha blend, in grey.</summary>
        Blend,

        /// <summary>The absolute difference, in grey.</summary>
        Difference,

        /// <summary>The two pictures side by side.</summary>
        Montage,
    }

    /// <summary>How <see cref="Fuse"/> maps sample values onto the display range first.</summary>
    public enum FuseScaling
    {
        /// <summary>Each picture is stretched to its own extremes.</summary>
        Independent,

        /// <summary>Both are stretched to the range the two share.</summary>
        Joint,

        /// <summary>Neither is stretched.</summary>
        None,
    }

    /// <summary>
    /// Tiles pictures into one, at a common size, with a border between them.
    /// </summary>
    /// <param name="tiles">The pictures, in reading order.</param>
    /// <param name="rows">Grid rows, or zero to choose a near-square grid.</param>
    /// <param name="cols">Grid columns, or zero to choose a near-square grid.</param>
    /// <param name="border">Blank pixels between and around the tiles.</param>
    /// <param name="background">The colour of the border and of any empty cell, one value per channel.</param>
    /// <param name="thumbnail">The size to bring every tile to, or null to use the first tile's size.</param>
    public static ImageBuffer Montage(
        IReadOnlyList<ImageBuffer> tiles,
        int rows,
        int cols,
        int border,
        double[] background,
        (int Height, int Width)? thumbnail = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentOutOfRangeException.ThrowIfNegative(border);
        if (tiles.Count == 0)
        {
            throw new ArgumentException("montage needs at least one picture.", nameof(tiles));
        }

        int channels = 1;
        foreach (ImageBuffer tile in tiles)
        {
            channels = Math.Max(channels, tile.Channels);
        }

        (int tileHeight, int tileWidth) = thumbnail ?? (tiles[0].Height, tiles[0].Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);

        // A grid nobody specified is made as square as it can be, with the spare cells in the last
        // row rather than the last column — which is what reading order implies.
        if (cols <= 0 && rows <= 0)
        {
            cols = (int)Math.Ceiling(Math.Sqrt(tiles.Count));
            rows = (int)Math.Ceiling(tiles.Count / (double)cols);
        }
        else if (cols <= 0)
        {
            cols = (int)Math.Ceiling(tiles.Count / (double)rows);
        }
        else if (rows <= 0)
        {
            rows = (int)Math.Ceiling(tiles.Count / (double)cols);
        }

        if ((long)rows * cols < tiles.Count)
        {
            throw new ArgumentException(
                $"montage was given {tiles.Count} pictures but a {rows}-by-{cols} grid holds {rows * cols}.");
        }

        int height = (rows * tileHeight) + ((rows + 1) * border);
        int width = (cols * tileWidth) + ((cols + 1) * border);
        var result = new ImageBuffer(height, width, channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    result[r, c, ch] = background[Math.Min(ch, background.Length - 1)];
                }
            }
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            int gridRow = i / cols;
            int gridCol = i % cols;
            int top = border + (gridRow * (tileHeight + border));
            int left = border + (gridCol * (tileWidth + border));

            ImageBuffer source = tiles[i];
            bool resized = source.Height != tileHeight || source.Width != tileWidth;
            ImageBuffer sized = resized
                ? Geometry.Resize(source, tileHeight, tileWidth)
                : source;
            try
            {
                for (int r = 0; r < tileHeight; r++)
                {
                    for (int c = 0; c < tileWidth; c++)
                    {
                        for (int ch = 0; ch < channels; ch++)
                        {
                            result[top + r, left + c, ch] = sized[r, c, Math.Min(ch, sized.Channels - 1)];
                        }
                    }
                }
            }
            finally
            {
                if (resized)
                {
                    sized.Dispose();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Combines two pictures into one (MATLAB <c>imfuse</c>). The two need not be the same size; the
    /// result is large enough for both, and each is placed at the top-left corner.
    /// </summary>
    /// <param name="a">The first picture.</param>
    /// <param name="b">The second.</param>
    /// <param name="method">How to combine them.</param>
    /// <param name="scaling">How to map sample values onto the display range first.</param>
    /// <param name="channels">
    /// For <see cref="FuseMethod.FalseColor"/>, which picture drives each of red, green and blue:
    /// 1 for the first, 2 for the second, 0 for nothing.
    /// </param>
    /// <remarks>
    /// False colour is the default because it answers the question that is usually being asked. Give
    /// one picture the green channel and the other the red and blue, and everywhere the two agree
    /// comes out grey, while every disagreement shows as either magenta or green depending on which
    /// picture is brighter. Registration errors that are invisible in a difference image are obvious
    /// as a coloured fringe.
    /// </remarks>
    public static ImageBuffer Fuse(
        ImageBuffer a,
        ImageBuffer b,
        FuseMethod method,
        FuseScaling scaling = FuseScaling.Independent,
        int[]? channels = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (method == FuseMethod.Montage)
        {
            // Side by side, each on a canvas big enough for the larger of the two, so neither is
            // resampled: the point of the montage form is to show the pictures as they are.
            int tall = Math.Max(a.Height, b.Height);
            int wide = Math.Max(a.Width, b.Width);
            int planes = Math.Max(a.Channels, b.Channels);
            using ImageBuffer leftTile = Placed(a, tall, wide, planes);
            using ImageBuffer rightTile = Placed(b, tall, wide, planes);
            return Montage([leftTile, rightTile], 1, 2, 0, [0], (tall, wide));
        }

        int height = Math.Max(a.Height, b.Height);
        int width = Math.Max(a.Width, b.Width);
        double[,] first = Scaled(a, height, width);
        double[,] second = Scaled(b, height, width);
        if (scaling != FuseScaling.None)
        {
            if (scaling == FuseScaling.Joint)
            {
                (double low, double high) = Extremes(first, second);
                Stretch(first, low, high);
                Stretch(second, low, high);
            }
            else
            {
                (double lowA, double highA) = Extremes(first, first);
                (double lowB, double highB) = Extremes(second, second);
                Stretch(first, lowA, highA);
                Stretch(second, lowB, highB);
            }
        }

        if (method == FuseMethod.Blend || method == FuseMethod.Difference)
        {
            var grey = new ImageBuffer(height, width, 1);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    grey[r, c, 0] = method == FuseMethod.Blend
                        ? (first[r, c] + second[r, c]) / 2
                        : Math.Abs(first[r, c] - second[r, c]);
                }
            }

            return grey;
        }

        int[] map = channels ?? [2, 1, 2];
        if (map.Length != 3)
        {
            throw new ArgumentException(
                "imfuse's colour channels are three entries, one per output channel.", nameof(channels));
        }

        var fused = new ImageBuffer(height, width, 3);
        for (int ch = 0; ch < 3; ch++)
        {
            double[,]? source = map[ch] switch
            {
                0 => null,
                1 => first,
                2 => second,
                _ => throw new ArgumentException(
                    "imfuse's colour channels are 1 for the first picture, 2 for the second, or 0 for none.",
                    nameof(channels)),
            };

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    fused[r, c, ch] = source is null ? 0 : source[r, c];
                }
            }
        }

        return fused;
    }

    /// <summary>How <see cref="Profile"/> and <see cref="PixelValues"/> read a sample between pixels.</summary>
    public enum Sampling
    {
        /// <summary>The nearest pixel, which is MATLAB's default for <c>improfile</c>.</summary>
        Nearest,

        /// <summary>The weighted average of the four surrounding pixels.</summary>
        Bilinear,

        /// <summary>Keys cubic over sixteen pixels.</summary>
        Bicubic,
    }

    /// <summary>
    /// The samples along a path through a picture (MATLAB <c>improfile</c>), together with the
    /// coordinates each was taken at.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <param name="xs">The path's column coordinates, zero-based.</param>
    /// <param name="ys">The path's row coordinates, zero-based.</param>
    /// <param name="samples">How many points to take, or zero for one per pixel of path length.</param>
    /// <param name="method">How to read a sample between pixels.</param>
    /// <remarks>
    /// The points are spread evenly along the whole path rather than evenly within each segment, so a
    /// long leg gets proportionally more of them — otherwise a path made of one long and one short
    /// segment would be sampled twice as densely on the short one.
    /// </remarks>
    public static (double[,] Values, double[] X, double[] Y) Profile(
        ImageBuffer image,
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        int samples,
        Sampling method = Sampling.Nearest)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (xs.Count != ys.Count)
        {
            throw new ArgumentException("improfile needs the same number of x and y coordinates.");
        }

        if (xs.Count < 2)
        {
            throw new ArgumentException("improfile needs at least two points to draw a path between.");
        }

        var lengths = new double[xs.Count];
        double total = 0;
        for (int i = 1; i < xs.Count; i++)
        {
            total += Math.Sqrt(((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]))
                + ((ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1])));
            lengths[i] = total;
        }

        int count = samples > 0 ? samples : Math.Max(2, (int)Math.Ceiling(total) + 1);
        var sampleX = new double[count];
        var sampleY = new double[count];
        var values = new double[count, image.Channels];

        for (int k = 0; k < count; k++)
        {
            double along = count == 1 ? 0 : total * k / (count - 1);
            int segment = 1;
            while (segment < lengths.Length - 1 && lengths[segment] < along)
            {
                segment++;
            }

            double start = lengths[segment - 1];
            double stretch = lengths[segment] - start;
            double t = stretch <= 0 ? 0 : (along - start) / stretch;
            double x = xs[segment - 1] + (t * (xs[segment] - xs[segment - 1]));
            double y = ys[segment - 1] + (t * (ys[segment] - ys[segment - 1]));
            sampleX[k] = x;
            sampleY[k] = y;
            for (int ch = 0; ch < image.Channels; ch++)
            {
                values[k, ch] = Sample(image, y, x, ch, method);
            }
        }

        return (values, sampleX, sampleY);
    }

    /// <summary>
    /// The colour at each of a list of points (MATLAB <c>impixel</c>), always three columns wide —
    /// a grey picture answers with the same value three times, which is what makes the result
    /// something a script can treat uniformly as a colour.
    /// </summary>
    public static double[,] PixelValues(
        ImageBuffer image, IReadOnlyList<double> columns, IReadOnlyList<double> rows)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count != rows.Count)
        {
            throw new ArgumentException("impixel needs the same number of column and row coordinates.");
        }

        var result = new double[columns.Count, 3];
        for (int i = 0; i < columns.Count; i++)
        {
            int c = (int)Math.Round(columns[i]);
            int r = (int)Math.Round(rows[i]);
            for (int ch = 0; ch < 3; ch++)
            {
                if ((uint)r >= (uint)image.Height || (uint)c >= (uint)image.Width)
                {
                    // A point off the picture has no colour; MATLAB answers zero rather than refusing,
                    // because a click near the edge is a normal thing for a caller to hand over.
                    result[i, ch] = 0;
                    continue;
                }

                result[i, ch] = image[r, c, Math.Min(ch, image.Channels - 1)];
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static double Sample(ImageBuffer image, double y, double x, int channel, Sampling method)
    {
        switch (method)
        {
            case Sampling.Nearest:
                return At(image, (int)Math.Round(y), (int)Math.Round(x), channel);

            case Sampling.Bilinear:
            {
                int r0 = (int)Math.Floor(y);
                int c0 = (int)Math.Floor(x);
                double fr = y - r0;
                double fc = x - c0;
                double top = (At(image, r0, c0, channel) * (1 - fc)) + (At(image, r0, c0 + 1, channel) * fc);
                double bottom = (At(image, r0 + 1, c0, channel) * (1 - fc)) + (At(image, r0 + 1, c0 + 1, channel) * fc);
                return (top * (1 - fr)) + (bottom * fr);
            }

            default:
            {
                int r0 = (int)Math.Floor(y);
                int c0 = (int)Math.Floor(x);
                double fr = y - r0;
                double fc = x - c0;
                double result = 0;
                for (int m = -1; m <= 2; m++)
                {
                    double rowWeight = Keys(m - fr);
                    if (rowWeight == 0)
                    {
                        continue;
                    }

                    double row = 0;
                    for (int n = -1; n <= 2; n++)
                    {
                        row += Keys(n - fc) * At(image, r0 + m, c0 + n, channel);
                    }

                    result += rowWeight * row;
                }

                return result;
            }
        }
    }

    /// <summary>The Keys cubic with a = −½, the same kernel <see cref="Geometry"/> resamples with.</summary>
    private static double Keys(double t)
    {
        double x = Math.Abs(t);
        if (x < 1)
        {
            return (1.5 * x * x * x) - (2.5 * x * x) + 1;
        }

        if (x < 2)
        {
            return (-0.5 * x * x * x) + (2.5 * x * x) - (4 * x) + 2;
        }

        return 0;
    }

    /// <summary>A sample with the edge replicated, so a path that leaves the picture still reads.</summary>
    private static double At(ImageBuffer image, int r, int c, int channel) =>
        image[Math.Clamp(r, 0, image.Height - 1), Math.Clamp(c, 0, image.Width - 1), channel];

    /// <summary>A picture as one grey plane on a canvas of the given size, top-left aligned.</summary>
    private static double[,] Scaled(ImageBuffer image, int height, int width)
    {
        var plane = new double[height, width];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                double sum = 0;
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    sum += image[r, c, ch];
                }

                plane[r, c] = sum / image.Channels;
            }
        }

        GC.KeepAlive(image);
        return plane;
    }

    private static ImageBuffer Placed(ImageBuffer image, int height, int width, int channels)
    {
        var result = new ImageBuffer(height, width, channels);
        for (int r = 0; r < Math.Min(height, image.Height); r++)
        {
            for (int c = 0; c < Math.Min(width, image.Width); c++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    result[r, c, ch] = image[r, c, Math.Min(ch, image.Channels - 1)];
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static (double Low, double High) Extremes(double[,] a, double[,] b)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double[,] plane in new[] { a, b })
        {
            foreach (double value in plane)
            {
                if (value < low) { low = value; }
                if (value > high) { high = value; }
            }
        }

        return (low, high);
    }

    private static void Stretch(double[,] plane, double low, double high)
    {
        double span = high - low;
        if (span <= 0)
        {
            return;
        }

        int rows = plane.GetLength(0);
        int cols = plane.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                plane[r, c] = (plane[r, c] - low) / span;
            }
        }
    }
}
