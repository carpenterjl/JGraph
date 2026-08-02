namespace JGraph.Imaging;

/// <summary>
/// Regions of interest: turning polygons into masks (<c>poly2mask</c>, <c>poly2label</c>,
/// <c>roipoly</c>), selecting by intensity (<c>roicolor</c>), filtering inside a mask
/// (<c>roifilt2</c>), and smoothly filling one in (<c>regionfill</c>).
/// </summary>
public static class RoiOps
{
    /// <summary>
    /// Rasterizes a polygon (MATLAB <c>poly2mask</c>). A pixel belongs to the mask when its centre
    /// falls inside the polygon, by the even–odd rule.
    /// </summary>
    /// <param name="xs">Vertex x coordinates, 0-based pixel units.</param>
    /// <param name="ys">Vertex y coordinates.</param>
    /// <param name="height">Mask height.</param>
    /// <param name="width">Mask width.</param>
    public static ImageBuffer PolygonMask(
        IReadOnlyList<double> xs, IReadOnlyList<double> ys, int height, int width)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (xs.Count != ys.Count)
        {
            throw new ArgumentException("a polygon needs as many x coordinates as y coordinates.", nameof(xs));
        }

        var polygon = new (double X, double Y)[xs.Count];
        for (int i = 0; i < xs.Count; i++)
        {
            polygon[i] = (xs[i], ys[i]);
        }

        var mask = new ImageBuffer(height, width, 1);
        if (polygon.Length < 3)
        {
            return mask;
        }

        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                mask[r, c, 0] = Boundaries.InsidePolygon(polygon, c, r) ? 1.0 : 0.0;
            }
        }

        return mask;
    }

    /// <summary>
    /// Labels a picture by which of several polygons each pixel falls in (MATLAB <c>poly2label</c>).
    /// Later polygons win where they overlap, which is MATLAB's own rule.
    /// </summary>
    public static int[,] PolygonLabels(
        IReadOnlyList<(double[] X, double[] Y)> polygons, IReadOnlyList<int> ids, int height, int width)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        ArgumentNullException.ThrowIfNull(ids);
        if (polygons.Count != ids.Count)
        {
            throw new ArgumentException("each polygon needs a label.", nameof(ids));
        }

        var labels = new int[height, width];
        for (int i = 0; i < polygons.Count; i++)
        {
            (double[] xs, double[] ys) = polygons[i];
            if (xs.Length != ys.Length || xs.Length < 3)
            {
                continue;
            }

            var polygon = new (double X, double Y)[xs.Length];
            for (int k = 0; k < xs.Length; k++)
            {
                polygon[k] = (xs[k], ys[k]);
            }

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    if (Boundaries.InsidePolygon(polygon, c, r))
                    {
                        labels[r, c] = ids[i];
                    }
                }
            }
        }

        return labels;
    }

    /// <summary>
    /// Selects samples in an intensity range (MATLAB <c>roicolor</c>). With one bound the selection is
    /// everything at or below it — MATLAB reads a single number that way too.
    /// </summary>
    public static ImageBuffer SelectByColor(ImageBuffer image, double low, double high)
    {
        ArgumentNullException.ThrowIfNull(image);
        var mask = new ImageBuffer(image.Height, image.Width, 1);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                double value = image[r, c, 0];
                mask[r, c, 0] = value >= low && value <= high ? 1.0 : 0.0;
            }
        }

        GC.KeepAlive(image);
        return mask;
    }

    /// <summary>
    /// Selects samples matching any of a set of values (MATLAB's <c>roicolor(A, v)</c> form).
    /// </summary>
    public static ImageBuffer SelectByValues(ImageBuffer image, IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(values);
        var wanted = new HashSet<double>(values);
        var mask = new ImageBuffer(image.Height, image.Width, 1);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                mask[r, c, 0] = wanted.Contains(image[r, c, 0]) ? 1.0 : 0.0;
            }
        }

        GC.KeepAlive(image);
        return mask;
    }

    /// <summary>
    /// Puts a filtered version of a picture back only where a mask allows (MATLAB <c>roifilt2</c>).
    /// </summary>
    public static ImageBuffer FilterInMask(ImageBuffer original, ImageBuffer filtered, ImageBuffer mask)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(filtered);
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Height != original.Height || mask.Width != original.Width)
        {
            throw new ArgumentException(
                $"the mask is {mask.Height}x{mask.Width} but the image is {original.Height}x{original.Width}.",
                nameof(mask));
        }

        var result = original.Clone();
        for (int r = 0; r < original.Height; r++)
        {
            for (int c = 0; c < original.Width; c++)
            {
                if (mask[r, c, 0] == 0)
                {
                    continue;
                }

                for (int ch = 0; ch < original.Channels; ch++)
                {
                    result[r, c, ch] = filtered[r, c, Math.Min(ch, filtered.Channels - 1)];
                }
            }
        }

        GC.KeepAlive(mask);
        GC.KeepAlive(filtered);
        return result;
    }

    /// <summary>
    /// Fills a region smoothly from its own boundary (MATLAB <c>regionfill</c>) by solving Laplace's
    /// equation inside it: every filled sample becomes the average of its four neighbours, which is
    /// the surface with no interior structure of its own and the boundary values it was given.
    /// </summary>
    /// <remarks>
    /// Gauss–Seidel with successive over-relaxation, rather than assembling and factoring the
    /// Laplacian. The matrix would be enormous for a large hole and the iteration converges in a few
    /// hundred sweeps for the sizes this is used on — a scratch, a timestamp, a dead pixel.
    /// </remarks>
    public static ImageBuffer FillRegion(ImageBuffer image, ImageBuffer mask, int iterations = 500)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Height != image.Height || mask.Width != image.Width)
        {
            throw new ArgumentException(
                $"the mask is {mask.Height}x{mask.Width} but the image is {image.Height}x{image.Width}.",
                nameof(mask));
        }

        int h = image.Height;
        int w = image.Width;
        ImageBuffer result = image.Clone();

        var fill = new bool[h, w];
        bool any = false;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                fill[r, c] = mask[r, c, 0] != 0;
                any |= fill[r, c];
            }
        }

        GC.KeepAlive(mask);
        if (!any)
        {
            return result;
        }

        // Start from the mean of the boundary so the first sweep is already close; from zero the
        // relaxation spends its early passes doing nothing but undoing the black.
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double edgeTotal = 0;
            int edgeCount = 0;
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (fill[r, c])
                    {
                        continue;
                    }

                    bool beside =
                        (r > 0 && fill[r - 1, c]) || (r + 1 < h && fill[r + 1, c]) ||
                        (c > 0 && fill[r, c - 1]) || (c + 1 < w && fill[r, c + 1]);
                    if (beside)
                    {
                        edgeTotal += result[r, c, ch];
                        edgeCount++;
                    }
                }
            }

            double seed = edgeCount > 0 ? edgeTotal / edgeCount : 0.0;
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (fill[r, c])
                    {
                        result[r, c, ch] = seed;
                    }
                }
            }

            const double relaxation = 1.8;
            for (int pass = 0; pass < iterations; pass++)
            {
                double worst = 0;
                for (int r = 0; r < h; r++)
                {
                    for (int c = 0; c < w; c++)
                    {
                        if (!fill[r, c])
                        {
                            continue;
                        }

                        double total = 0;
                        int count = 0;
                        if (r > 0)
                        {
                            total += result[r - 1, c, ch];
                            count++;
                        }

                        if (r + 1 < h)
                        {
                            total += result[r + 1, c, ch];
                            count++;
                        }

                        if (c > 0)
                        {
                            total += result[r, c - 1, ch];
                            count++;
                        }

                        if (c + 1 < w)
                        {
                            total += result[r, c + 1, ch];
                            count++;
                        }

                        if (count == 0)
                        {
                            continue;
                        }

                        double target = total / count;
                        double before = result[r, c, ch];
                        double after = before + (relaxation * (target - before));
                        result[r, c, ch] = after;
                        worst = Math.Max(worst, Math.Abs(after - before));
                    }
                }

                if (worst < 1e-7)
                {
                    break;
                }
            }
        }

        return result;
    }
}
