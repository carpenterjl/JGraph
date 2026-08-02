namespace JGraph.Imaging;

/// <summary>
/// Turning labels and masks into something you can look at: <c>label2rgb</c>, <c>labeloverlay</c>
/// and <c>imoverlay</c>.
/// </summary>
/// <remarks>
/// All three bake to an ordinary RGB image rather than adding anything to the figure model, which is
/// why they cost nothing beyond the arithmetic: the result is a picture, and <c>imshow</c> already
/// knows how to show a picture.
/// </remarks>
public static class LabelDisplay
{
    /// <summary>
    /// A colour per label, spread round the hue circle. Adjacent labels are usually adjacent in
    /// space, so consecutive hues would make neighbouring regions look alike; the golden-ratio step
    /// puts consecutive labels as far apart in hue as it can while still visiting every hue.
    /// </summary>
    public static (double R, double G, double B)[] Palette(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var colors = new (double, double, double)[count];
        const double golden = 0.618033988749895;
        double hue = 0.15;
        for (int i = 0; i < count; i++)
        {
            hue = (hue + golden) % 1.0;
            colors[i] = HsvToRgb(hue, 0.75, 0.95);
        }

        return colors;
    }

    /// <summary>
    /// Colours a label map (MATLAB <c>label2rgb</c>). Label 0 takes
    /// <paramref name="background"/>; every other label takes a colour from
    /// <paramref name="colors"/>, cycling if there are more labels than colours.
    /// </summary>
    public static ImageBuffer LabelToRgb(
        int[,] labels, (double R, double G, double B)[]? colors = null,
        (double R, double G, double B)? background = null, bool shuffle = false, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
        int highest = 0;
        foreach (int label in labels)
        {
            highest = Math.Max(highest, label);
        }

        (double R, double G, double B)[] table = colors is { Length: > 0 } ? colors : Palette(Math.Max(1, highest));
        var order = new int[Math.Max(1, highest)];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        if (shuffle)
        {
            Random rng = random ?? new Random(0);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        (double R, double G, double B) empty = background ?? (1.0, 1.0, 1.0);
        var result = new ImageBuffer(h, w, 3);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int label = labels[r, c];
                (double red, double green, double blue) = label <= 0
                    ? empty
                    : table[order[(label - 1) % order.Length] % table.Length];
                result[r, c, 0] = red;
                result[r, c, 1] = green;
                result[r, c, 2] = blue;
            }
        }

        return result;
    }

    /// <summary>
    /// Blends a label map over a picture (MATLAB <c>labeloverlay</c>), leaving the background
    /// untouched so the picture still reads underneath.
    /// </summary>
    public static ImageBuffer LabelOverlay(
        ImageBuffer image, int[,] labels, (double R, double G, double B)[]? colors = null,
        double transparency = 0.65, IReadOnlyList<int>? included = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.GetLength(0) != image.Height || labels.GetLength(1) != image.Width)
        {
            throw new ArgumentException(
                $"the label map is {labels.GetLength(0)}x{labels.GetLength(1)} but the image is " +
                $"{image.Height}x{image.Width}.", nameof(labels));
        }

        using ImageBuffer painted = LabelToRgb(labels, colors);
        HashSet<int>? wanted = included is null ? null : [.. included];
        double alpha = 1.0 - Math.Clamp(transparency, 0.0, 1.0);

        var result = new ImageBuffer(image.Height, image.Width, 3);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                int label = labels[r, c];
                bool paint = label > 0 && (wanted is null || wanted.Contains(label));
                for (int ch = 0; ch < 3; ch++)
                {
                    double under = image[r, c, Math.Min(ch, image.Channels - 1)];
                    result[r, c, ch] = paint
                        ? (under * (1 - alpha)) + (painted[r, c, ch] * alpha)
                        : under;
                }
            }
        }

        GC.KeepAlive(image);
        GC.KeepAlive(painted);
        return result;
    }

    /// <summary>
    /// Burns a binary mask into a picture in one flat colour (MATLAB <c>imoverlay</c>).
    /// </summary>
    public static ImageBuffer Overlay(ImageBuffer image, ImageBuffer mask, (double R, double G, double B) color)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Height != image.Height || mask.Width != image.Width)
        {
            throw new ArgumentException(
                $"the mask is {mask.Height}x{mask.Width} but the image is {image.Height}x{image.Width}.",
                nameof(mask));
        }

        var result = new ImageBuffer(image.Height, image.Width, 3);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                bool on = mask[r, c, 0] != 0;
                result[r, c, 0] = on ? color.R : image[r, c, 0];
                result[r, c, 1] = on ? color.G : image[r, c, Math.Min(1, image.Channels - 1)];
                result[r, c, 2] = on ? color.B : image[r, c, Math.Min(2, image.Channels - 1)];
            }
        }

        GC.KeepAlive(image);
        GC.KeepAlive(mask);
        return result;
    }

    private static (double R, double G, double B) HsvToRgb(double h, double s, double v)
    {
        double sector = h * 6.0;
        int face = (int)Math.Floor(sector) % 6;
        double fraction = sector - Math.Floor(sector);
        double p = v * (1 - s);
        double q = v * (1 - (s * fraction));
        double t = v * (1 - (s * (1 - fraction)));
        return face switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }
}
