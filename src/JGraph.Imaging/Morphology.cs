namespace JGraph.Imaging;

/// <summary>Grayscale/binary morphology: erosion, dilation, opening, closing, and structuring elements.</summary>
public static class Morphology
{
    /// <summary>Erodes an image: each output sample is the minimum over the structuring-element neighbourhood (MATLAB <c>imerode</c>).</summary>
    public static ImageBuffer Erode(ImageBuffer image, bool[,] element) => Apply(image, element, erode: true);

    /// <summary>Dilates an image: each output sample is the maximum over the structuring-element neighbourhood (MATLAB <c>imdilate</c>).</summary>
    public static ImageBuffer Dilate(ImageBuffer image, bool[,] element) => Apply(image, element, erode: false);

    /// <summary>Opening: erosion followed by dilation (removes small bright specks) — MATLAB <c>imopen</c>.</summary>
    public static ImageBuffer Open(ImageBuffer image, bool[,] element)
    {
        using ImageBuffer eroded = Erode(image, element);
        return Dilate(eroded, element);
    }

    /// <summary>Closing: dilation followed by erosion (fills small dark holes) — MATLAB <c>imclose</c>.</summary>
    public static ImageBuffer Close(ImageBuffer image, bool[,] element)
    {
        using ImageBuffer dilated = Dilate(image, element);
        return Erode(dilated, element);
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

    private static ImageBuffer Apply(ImageBuffer image, bool[,] element, bool erode)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(element);
        int eh = element.GetLength(0);
        int ew = element.GetLength(1);
        int anchorR = eh / 2;
        int anchorC = ew / 2;

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
                            if (!element[er, ec])
                            {
                                continue;
                            }

                            int sc = c + ec - anchorC;
                            // Outside the image: erosion treats it as 1 (upper bound), dilation as 0.
                            double sample = (uint)sr < (uint)image.Height && (uint)sc < (uint)image.Width
                                ? image[sr, sc, ch]
                                : (erode ? 1.0 : 0.0);
                            extreme = erode ? Math.Min(extreme, sample) : Math.Max(extreme, sample);
                        }
                    }

                    result[r, c, ch] = extreme;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }
}
