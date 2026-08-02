namespace JGraph.Imaging;

/// <summary>
/// Active-contour segmentation (MATLAB <c>activecontour</c>): a mask is evolved as a level set until
/// it settles on the region the image says is there.
/// </summary>
/// <remarks>
/// <para>
/// The Chan–Vese method asks a question that has nothing to do with edges: given a contour, what are
/// the mean intensities inside and outside it, and would moving the contour make those two means
/// describe the picture better? A region with no visible boundary at all — a slow gradient, a blurred
/// join — still has two different means, which is why this finds objects the edge-based methods walk
/// straight through.
/// </para>
/// <para>
/// The evolution is written on a signed indicator rather than a true signed-distance function: the
/// interface is where the sign changes, and it is re-derived from the mask each step. That removes
/// the reinitialization pass a distance-function level set needs, at the cost of a step size that has
/// to stay small — which it does, because the smoothing term is applied by curvature rather than by
/// a large explicit step.
/// </para>
/// </remarks>
public static class ActiveContour
{
    /// <summary>Which force drives the contour.</summary>
    public enum Method
    {
        /// <summary>Region means inside and outside — Chan–Vese, MATLAB's default.</summary>
        ChanVese,

        /// <summary>The image gradient, so the contour is drawn to edges.</summary>
        Edge,
    }

    /// <summary>
    /// Evolves <paramref name="mask"/> over <paramref name="iterations"/> steps.
    /// </summary>
    /// <param name="image">The picture to segment.</param>
    /// <param name="mask">The starting region.</param>
    /// <param name="iterations">How many steps.</param>
    /// <param name="method">Which force drives the contour.</param>
    /// <param name="smoothFactor">
    /// How strongly curvature is penalized; larger values keep the boundary smoother and are what
    /// stop a noisy picture growing a ragged contour.
    /// </param>
    /// <param name="contractionBias">
    /// Positive shrinks the contour even where the image gives no reason to, negative grows it.
    /// </param>
    public static ImageBuffer Evolve(
        ImageBuffer image, ImageBuffer mask, int iterations = 100,
        Method method = Method.ChanVese, double smoothFactor = 0.0, double contractionBias = 0.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        if (image.Height != mask.Height || image.Width != mask.Width)
        {
            throw new ArgumentException(
                $"the mask is {mask.Height}x{mask.Width} but the image is {image.Height}x{image.Width}.",
                nameof(mask));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(iterations);
        int h = image.Height;
        int w = image.Width;

        // Colour is measured as the mean over the channels: Chan-Vese asks about one number per
        // pixel, and averaging is the reading that keeps a grey picture identical to itself.
        var intensity = new double[h * w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double total = 0;
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    total += image[r, c, ch];
                }

                intensity[(r * w) + c] = total / image.Channels;
            }
        }

        var inside = new bool[h * w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                inside[(r * w) + c] = mask[r, c, 0] != 0;
            }
        }

        GC.KeepAlive(image);
        GC.KeepAlive(mask);

        double[]? edgeStop = method == Method.Edge ? EdgeStop(image) : null;
        double smoothing = 0.2 * Math.Max(0.0, smoothFactor);

        for (int step = 0; step < iterations; step++)
        {
            (double meanIn, double meanOut) = Means(intensity, inside);
            var next = new bool[h * w];
            bool changed = false;

            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int p = (r * w) + c;
                    int neighbours = 0;
                    int insideNeighbours = 0;
                    foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, 8))
                    {
                        if ((uint)nr >= (uint)h || (uint)nc >= (uint)w)
                        {
                            continue;
                        }

                        neighbours++;
                        if (inside[(nr * w) + nc])
                        {
                            insideNeighbours++;
                        }
                    }

                    bool onBoundary = insideNeighbours != 0 && insideNeighbours != neighbours;
                    if (!onBoundary)
                    {
                        next[p] = inside[p];
                        continue;
                    }

                    double force;
                    if (method == Method.ChanVese)
                    {
                        // Positive means the pixel resembles the inside more than the outside, so
                        // the contour should swallow it.
                        double toIn = intensity[p] - meanIn;
                        double toOut = intensity[p] - meanOut;
                        force = (toOut * toOut) - (toIn * toIn);
                    }
                    else
                    {
                        // Edge mode: the contour advances freely over flat ground, where the stop
                        // function is near 1, and stalls at an edge, where it approaches 0. Half is
                        // the crossing point, so the contraction bias is what decides which way an
                        // ambiguous pixel goes.
                        force = edgeStop![p] - 0.5;
                    }

                    // Curvature: a pixel with few neighbours inside is a spike, and smoothing pulls
                    // it back regardless of what the image says.
                    double curvature = ((double)insideNeighbours / neighbours) - 0.5;
                    double total = force - contractionBias + (smoothing * curvature * 2.0);

                    next[p] = total > 0;
                    if (next[p] != inside[p])
                    {
                        changed = true;
                    }
                }
            }

            inside = next;
            if (!changed)
            {
                break;
            }
        }

        var result = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                result[r, c, 0] = inside[(r * w) + c] ? 1.0 : 0.0;
            }
        }

        return result;
    }

    private static (double Inside, double Outside) Means(double[] intensity, bool[] inside)
    {
        double sumIn = 0;
        double sumOut = 0;
        int countIn = 0;
        int countOut = 0;
        for (int i = 0; i < intensity.Length; i++)
        {
            if (inside[i])
            {
                sumIn += intensity[i];
                countIn++;
            }
            else
            {
                sumOut += intensity[i];
                countOut++;
            }
        }

        return (countIn > 0 ? sumIn / countIn : 0.0, countOut > 0 ? sumOut / countOut : 0.0);
    }

    /// <summary>The edge-stopping function: near 1 on flat ground, near 0 at a strong edge.</summary>
    private static double[] EdgeStop(ImageBuffer image)
    {
        using ImageBuffer smoothed = Filters.GaussianBlur(image, 1.0, 1.0);
        (ImageBuffer magnitude, ImageBuffer direction) = Gradients.Gradient(smoothed, Gradients.Operator.Sobel);
        direction.Dispose();
        using (magnitude)
        {
            double largest = 0;
            foreach (double value in magnitude.Pixels)
            {
                largest = Math.Max(largest, value);
            }

            var stop = new double[image.Height * image.Width];
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    double normalized = largest > 0 ? magnitude[r, c, 0] / largest : 0.0;
                    stop[(r * image.Width) + c] = 1.0 / (1.0 + (10.0 * normalized * normalized));
                }
            }

            GC.KeepAlive(magnitude);
            return stop;
        }
    }
}
