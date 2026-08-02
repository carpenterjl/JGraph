namespace JGraph.Imaging;

/// <summary>Which colour each of the four pixels in a Bayer sensor's 2×2 tile carries.</summary>
public enum SensorAlignment
{
    /// <summary>Green, blue on the first row; red, green on the second.</summary>
    Gbrg,

    /// <summary>Green, red on the first row; blue, green on the second.</summary>
    Grbg,

    /// <summary>Blue, green on the first row; green, red on the second.</summary>
    Bggr,

    /// <summary>Red, green on the first row; green, blue on the second.</summary>
    Rggb,
}

/// <summary>
/// Palettes: reducing a picture to a colormap and a table of indices, and reconstructing one from a
/// sensor's colour-filter array.
/// </summary>
public static class IndexedImages
{
    /// <summary>A grey colormap of <paramref name="levels"/> rows, black to white.</summary>
    public static double[,] GrayColormap(int levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        var map = new double[levels, 3];
        for (int i = 0; i < levels; i++)
        {
            double value = levels == 1 ? 0.0 : i / (double)(levels - 1);
            map[i, 0] = value;
            map[i, 1] = value;
            map[i, 2] = value;
        }

        return map;
    }

    /// <summary>The luminance of every colormap entry, written back as a grey colormap.</summary>
    public static double[,] ColormapToGray(double[,] map)
    {
        ArgumentNullException.ThrowIfNull(map);
        int n = map.GetLength(0);
        var gray = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double luma = (0.298936021293775 * map[i, 0])
                          + (0.587043074451121 * map[i, 1])
                          + (0.114020904255103 * map[i, 2]);
            gray[i, 0] = luma;
            gray[i, 1] = luma;
            gray[i, 2] = luma;
        }

        return gray;
    }

    /// <summary>
    /// Chooses a palette of at most <paramref name="colors"/> entries by median cut: repeatedly split
    /// the box of colours that is longest along one axis, at its own median, and average what is left
    /// in each box.
    /// </summary>
    /// <remarks>
    /// Splitting at the median rather than the midpoint is what makes it adaptive — a box holding
    /// thousands of near-identical greens is cut where the greens actually are, so the palette spends
    /// its entries where the picture does.
    /// </remarks>
    public static double[,] MedianCut(double[,] pixels, int colors)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(colors);

        int n = pixels.GetLength(0);
        var indices = new int[n];
        for (int i = 0; i < n; i++)
        {
            indices[i] = i;
        }

        var boxes = new List<(int Start, int Count)> { (0, n) };
        while (boxes.Count < colors)
        {
            int widest = -1;
            int widestAxis = 0;
            double widestSpan = 0;
            for (int b = 0; b < boxes.Count; b++)
            {
                (int start, int count) = boxes[b];
                if (count < 2)
                {
                    continue;
                }

                for (int axis = 0; axis < 3; axis++)
                {
                    double low = double.PositiveInfinity;
                    double high = double.NegativeInfinity;
                    for (int k = start; k < start + count; k++)
                    {
                        double v = pixels[indices[k], axis];
                        low = Math.Min(low, v);
                        high = Math.Max(high, v);
                    }

                    if (high - low > widestSpan)
                    {
                        widestSpan = high - low;
                        widest = b;
                        widestAxis = axis;
                    }
                }
            }

            if (widest < 0 || widestSpan <= 0)
            {
                break; // every box holds one colour; there is nothing left to split
            }

            (int boxStart, int boxCount) = boxes[widest];
            Array.Sort(indices, boxStart, boxCount, Comparer<int>.Create(
                (a, b) => pixels[a, widestAxis].CompareTo(pixels[b, widestAxis])));

            int half = boxCount / 2;
            boxes[widest] = (boxStart, half);
            boxes.Insert(widest + 1, (boxStart + half, boxCount - half));
        }

        var map = new double[boxes.Count, 3];
        for (int b = 0; b < boxes.Count; b++)
        {
            (int start, int count) = boxes[b];
            for (int k = start; k < start + count; k++)
            {
                for (int c = 0; c < 3; c++)
                {
                    map[b, c] += pixels[indices[k], c];
                }
            }

            for (int c = 0; c < 3; c++)
            {
                map[b, c] /= count;
            }
        }

        return map;
    }

    /// <summary>
    /// Maps every pixel of an image to its nearest palette entry, returning one-based indices.
    /// </summary>
    /// <param name="pixels">The picture, row-major, as <c>h·w</c> RGB triples.</param>
    /// <param name="height">Row count, needed only when dithering.</param>
    /// <param name="width">Column count, needed only when dithering.</param>
    /// <param name="map">The palette.</param>
    /// <param name="dither">
    /// Whether to spread each pixel's rounding error onto its unvisited neighbours (Floyd–Steinberg),
    /// which trades speckle for the banding a flat gradient would otherwise show.
    /// </param>
    public static double[] Quantize(double[,] pixels, int height, int width, double[,] map, bool dither)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(map);

        int n = pixels.GetLength(0);
        var indices = new double[n];
        if (!dither)
        {
            for (int i = 0; i < n; i++)
            {
                indices[i] = Nearest(map, pixels[i, 0], pixels[i, 1], pixels[i, 2]) + 1;
            }

            return indices;
        }

        // A working copy, because the error diffusion changes pixels that have not been visited yet.
        var working = new double[n, 3];
        Array.Copy(pixels, working, pixels.Length);

        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int i = (r * width) + c;
                int chosen = Nearest(map, working[i, 0], working[i, 1], working[i, 2]);
                indices[i] = chosen + 1;

                for (int ch = 0; ch < 3; ch++)
                {
                    double error = working[i, ch] - map[chosen, ch];
                    Spread(working, height, width, r, c + 1, ch, error * 7.0 / 16.0);
                    Spread(working, height, width, r + 1, c - 1, ch, error * 3.0 / 16.0);
                    Spread(working, height, width, r + 1, c, ch, error * 5.0 / 16.0);
                    Spread(working, height, width, r + 1, c + 1, ch, error * 1.0 / 16.0);
                }
            }
        }

        return indices;
    }

    /// <summary>Expands one-based indices through a colormap into RGB triples.</summary>
    public static double[,] Expand(double[] indices, double[,] map)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(map);
        int rows = map.GetLength(0);
        var rgb = new double[indices.Length, 3];
        for (int i = 0; i < indices.Length; i++)
        {
            int row = Math.Clamp((int)Math.Round(indices[i]) - 1, 0, rows - 1);
            for (int c = 0; c < 3; c++)
            {
                rgb[i, c] = map[row, c];
            }
        }

        return rgb;
    }

    /// <summary>
    /// Reconstructs full colour from a Bayer colour-filter array (MATLAB <c>demosaic</c>), by
    /// Malvar, He and Cutler's gradient-corrected linear interpolation.
    /// </summary>
    /// <remarks>
    /// Plain bilinear interpolation of each colour plane smears edges into rainbows, because a step
    /// in luminance lands on the three planes at different pixels. Malvar's scheme borrows the
    /// second difference of the channel that <em>was</em> measured at a pixel to correct the two that
    /// were not, which costs one extra 5×5 kernel and removes most of the fringing.
    /// </remarks>
    public static double[,] Demosaic(double[] cfa, int height, int width, SensorAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(cfa);
        if (height < 2 || width < 2 || height % 2 != 0 || width % 2 != 0)
        {
            throw new ArgumentException("a colour-filter array has an even number of rows and columns");
        }

        var rgb = new double[height * width, 3];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int i = (r * width) + c;
                int channel = ChannelAt(alignment, r, c);
                double measured = cfa[i];
                rgb[i, channel] = measured;

                for (int target = 0; target < 3; target++)
                {
                    if (target == channel)
                    {
                        continue;
                    }

                    rgb[i, target] = Estimate(cfa, height, width, r, c, channel, target, alignment);
                }
            }
        }

        return rgb;
    }

    /// <summary>Which channel the sensor measured at a given pixel.</summary>
    private static int ChannelAt(SensorAlignment alignment, int row, int col)
    {
        // The 2×2 tile, written as the channel index of (0,0), (0,1), (1,0), (1,1).
        int[] tile = alignment switch
        {
            SensorAlignment.Gbrg => [1, 2, 0, 1],
            SensorAlignment.Grbg => [1, 0, 2, 1],
            SensorAlignment.Bggr => [2, 1, 1, 0],
            _ => [0, 1, 1, 2],
        };

        return tile[((row % 2) * 2) + (col % 2)];
    }

    private static double Estimate(
        double[] cfa, int height, int width, int row, int col, int measured, int target, SensorAlignment alignment)
    {
        // Malvar's weights, expressed as the bilinear estimate of the target channel plus a gain
        // times the Laplacian of the measured one.
        double neighbours = 0;
        int count = 0;
        for (int dr = -2; dr <= 2; dr++)
        {
            for (int dc = -2; dc <= 2; dc++)
            {
                int r = row + dr;
                int c = col + dc;
                if (r < 0 || r >= height || c < 0 || c >= width || Math.Abs(dr) + Math.Abs(dc) > 2)
                {
                    continue;
                }

                if (ChannelAt(alignment, r, c) != target || (dr == 0 && dc == 0))
                {
                    continue;
                }

                neighbours += cfa[(r * width) + c];
                count++;
            }
        }

        if (count == 0)
        {
            return cfa[(row * width) + col];
        }

        double bilinear = neighbours / count;
        double gain = measured == 1 || target == 1 ? 0.5 : 0.75;
        return bilinear + (gain * Laplacian(cfa, height, width, row, col, measured, alignment));
    }

    /// <summary>The second difference of the measured channel about a pixel, used as the correction.</summary>
    private static double Laplacian(
        double[] cfa, int height, int width, int row, int col, int measured, SensorAlignment alignment)
    {
        double sum = 0;
        int count = 0;
        foreach ((int dr, int dc) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2) })
        {
            int r = row + dr;
            int c = col + dc;
            if (r < 0 || r >= height || c < 0 || c >= width || ChannelAt(alignment, r, c) != measured)
            {
                continue;
            }

            sum += cfa[(r * width) + c];
            count++;
        }

        return count == 0 ? 0.0 : cfa[(row * width) + col] - (sum / count);
    }

    private static int Nearest(double[,] map, double r, double g, double b)
    {
        int best = 0;
        double bestDistance = double.PositiveInfinity;
        for (int i = 0; i < map.GetLength(0); i++)
        {
            double dr = r - map[i, 0];
            double dg = g - map[i, 1];
            double db = b - map[i, 2];
            double distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static void Spread(double[,] working, int height, int width, int row, int col, int channel, double amount)
    {
        if (row < 0 || row >= height || col < 0 || col >= width)
        {
            return;
        }

        working[(row * width) + col, channel] += amount;
    }
}
