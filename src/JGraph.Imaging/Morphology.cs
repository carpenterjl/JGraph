namespace JGraph.Imaging;

/// <summary>Grayscale/binary morphology: erosion, dilation, opening, closing, and the two hat transforms.</summary>
public static class Morphology
{
    /// <summary>Erodes an image: each output sample is the minimum over the structuring-element neighbourhood (MATLAB <c>imerode</c>).</summary>
    public static ImageBuffer Erode(ImageBuffer image, bool[,] element) =>
        Erode(image, StructuringElement.Arbitrary(element));

    /// <summary>Dilates an image: each output sample is the maximum over the structuring-element neighbourhood (MATLAB <c>imdilate</c>).</summary>
    public static ImageBuffer Dilate(ImageBuffer image, bool[,] element) =>
        Dilate(image, StructuringElement.Arbitrary(element));

    /// <summary>Opening: erosion followed by dilation (removes small bright specks) — MATLAB <c>imopen</c>.</summary>
    public static ImageBuffer Open(ImageBuffer image, bool[,] element) =>
        Open(image, StructuringElement.Arbitrary(element));

    /// <summary>Closing: dilation followed by erosion (fills small dark holes) — MATLAB <c>imclose</c>.</summary>
    public static ImageBuffer Close(ImageBuffer image, bool[,] element) =>
        Close(image, StructuringElement.Arbitrary(element));

    /// <summary>
    /// Erodes an image over a structuring element: <c>min over members of f(x + b) − h(b)</c>. Samples
    /// outside the picture read as 1, the top of the range, so the border is not eaten away.
    /// </summary>
    public static ImageBuffer Erode(ImageBuffer image, StructuringElement element) =>
        Apply(image, element, erode: true);

    /// <summary>
    /// Dilates an image over a structuring element: <c>max over members of f(x − b) + h(b)</c>.
    /// The element is reflected first, which is what makes dilation the exact dual of erosion for an
    /// asymmetric shape; samples outside the picture read as 0.
    /// </summary>
    public static ImageBuffer Dilate(ImageBuffer image, StructuringElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Apply(image, element.Reflect(), erode: false);
    }

    /// <summary>Opening: erosion followed by dilation — MATLAB <c>imopen</c>.</summary>
    public static ImageBuffer Open(ImageBuffer image, StructuringElement element)
    {
        using ImageBuffer eroded = Erode(image, element);
        return Dilate(eroded, element);
    }

    /// <summary>Closing: dilation followed by erosion — MATLAB <c>imclose</c>.</summary>
    public static ImageBuffer Close(ImageBuffer image, StructuringElement element)
    {
        using ImageBuffer dilated = Dilate(image, element);
        return Erode(dilated, element);
    }

    /// <summary>
    /// The top-hat transform, <c>I − imopen(I)</c>: what the opening could not fit, which is the small
    /// bright detail sitting on whatever background the element is large enough to follow.
    /// </summary>
    public static ImageBuffer TopHat(ImageBuffer image, StructuringElement element)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ImageBuffer opened = Open(image, element);
        return Difference(image, opened);
    }

    /// <summary>The bottom-hat transform, <c>imclose(I) − I</c>: the small dark detail.</summary>
    public static ImageBuffer BottomHat(ImageBuffer image, StructuringElement element)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ImageBuffer closed = Close(image, element);
        return Difference(closed, image);
    }

    /// <summary>
    /// The hit-or-miss transform: the pixels whose neighbourhood matches <paramref name="foreground"/>
    /// where it demands foreground and background where <paramref name="background"/> demands it
    /// (MATLAB <c>bwhitmiss</c>). Both elements must be the same size.
    /// </summary>
    public static ImageBuffer HitMiss(
        ImageBuffer image, StructuringElement foreground, StructuringElement background)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(background);

        using ImageBuffer complement = PointOps.Complement(image);
        using ImageBuffer hits = Erode(image, foreground);
        using ImageBuffer misses = Erode(complement, background);
        var result = new ImageBuffer(image.Height, image.Width, 1);
        Span<double> output = result.Pixels;
        ReadOnlySpan<double> a = hits.Pixels;
        ReadOnlySpan<double> b = misses.Pixels;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = a[i] != 0 && b[i] != 0 ? 1.0 : 0.0;
        }

        GC.KeepAlive(hits);
        GC.KeepAlive(misses);
        return result;
    }

    /// <summary>A square structuring element of the given side length (all true).</summary>
    public static bool[,] Square(int size = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var element = new bool[size, size];
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                element[r, c] = true;
            }
        }

        return element;
    }

    /// <summary>A disk structuring element of the given radius (true inside the circle).</summary>
    public static bool[,] Disk(int radius = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        int size = (2 * radius) + 1;
        var element = new bool[size, size];
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                double dy = r - radius;
                double dx = c - radius;
                element[r, c] = (dx * dx) + (dy * dy) <= (double)radius * radius;
            }
        }

        return element;
    }

    /// <summary>Converts a numeric structuring element (nonzero = member) to a boolean mask.</summary>
    public static bool[,] ToElement(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int h = values.GetLength(0);
        int w = values.GetLength(1);
        var element = new bool[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                element[r, c] = values[r, c] != 0;
            }
        }

        return element;
    }

    private static ImageBuffer Difference(ImageBuffer left, ImageBuffer right)
    {
        var result = new ImageBuffer(left.Height, left.Width, left.Channels);
        Span<double> output = result.Pixels;
        ReadOnlySpan<double> a = left.Pixels;
        ReadOnlySpan<double> b = right.Pixels;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = Math.Max(0.0, a[i] - b[i]);
        }

        GC.KeepAlive(left);
        GC.KeepAlive(right);
        return result;
    }

    private static ImageBuffer Apply(ImageBuffer image, StructuringElement element, bool erode)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(element);
        if (element.Is3D)
        {
            throw new ArgumentException(
                "a three-dimensional structuring element needs a volume, not a picture.", nameof(element));
        }

        int eh = element.Rows;
        int ew = element.Cols;
        int anchorR = element.OriginRow;
        int anchorC = element.OriginCol;

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double extreme = erode ? double.PositiveInfinity : double.NegativeInfinity;
                    for (int er = 0; er < eh; er++)
                    {
                        int sr = r + er - anchorR;
                        for (int ec = 0; ec < ew; ec++)
                        {
                            if (!element.Member(er, ec))
                            {
                                continue;
                            }

                            int sc = c + ec - anchorC;
                            // Outside the image: erosion treats it as 1 (upper bound), dilation as 0.
                            double sample = (uint)sr < (uint)image.Height && (uint)sc < (uint)image.Width
                                ? image[sr, sc, ch]
                                : (erode ? 1.0 : 0.0);

                            // A non-flat element shifts each sample by its own height before the
                            // extreme is taken — down for erosion, up for dilation.
                            double height = element.HeightAt(er, ec);
                            sample = erode ? sample - height : sample + height;
                            extreme = erode ? Math.Min(extreme, sample) : Math.Max(extreme, sample);
                        }
                    }

                    // An element with no members leaves the sample alone rather than writing ±Inf.
                    // A flat element always picks a sample that was already in range; a non-flat one
                    // can push past either end, and saturating there is what MATLAB does for every
                    // class but double — which is the only range an image carries here.
                    result[r, c, ch] = double.IsInfinity(extreme)
                        ? image[r, c, ch]
                        : Math.Clamp(extreme, 0.0, 1.0);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }
}
