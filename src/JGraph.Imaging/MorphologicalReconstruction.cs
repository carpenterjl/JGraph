namespace JGraph.Imaging;

/// <summary>
/// Grayscale morphological reconstruction and the family of operations defined in terms of it:
/// hole filling, border clearing, the h-extrema, the regional and extended extrema, and minima
/// imposition.
/// </summary>
/// <remarks>
/// <para>
/// Reconstruction is repeated geodesic dilation of a marker under a mask until nothing changes.
/// Written literally that is a scan per pixel of propagation distance, which on a picture with one
/// long thin structure is thousands of passes. Vincent's hybrid algorithm instead makes one forward
/// raster scan and one backward scan — between them they carry a value as far as it can go in either
/// direction in a single pass — and queues only the pixels the backward scan proves are still able to
/// propagate. The queue then runs to exhaustion, touching each remaining pixel a bounded number of
/// times. What would be O(n · diameter) becomes O(n).
/// </para>
/// <para>
/// Everything here works on a flat row-major <c>double[]</c> plane rather than an
/// <see cref="ImageBuffer"/>, because <see cref="ImposeMin"/> genuinely needs ±∞ as sentinel values
/// and an image is a [0, 1] object. Multi-channel pictures are reconstructed one channel at a time,
/// which is what MATLAB's N-D reconstruction reduces to for an RGB image.
/// </para>
/// </remarks>
public static class MorphologicalReconstruction
{
    /// <summary>
    /// Reconstructs <paramref name="marker"/> under <paramref name="mask"/> (MATLAB
    /// <c>imreconstruct</c>): the marker grows by dilation but is never allowed above the mask, so
    /// what survives is every mask structure the marker reaches into.
    /// </summary>
    /// <param name="marker">The seed image; samples above the mask are clipped down to it first.</param>
    /// <param name="mask">The ceiling.</param>
    /// <param name="connectivity">4 or 8.</param>
    public static ImageBuffer Reconstruct(ImageBuffer marker, ImageBuffer mask, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(mask);
        if (marker.Height != mask.Height || marker.Width != mask.Width)
        {
            throw new ArgumentException(
                $"the marker is {marker.Height}x{marker.Width} but the mask is {mask.Height}x{mask.Width}.",
                nameof(marker));
        }

        if (marker.Channels != mask.Channels)
        {
            throw new ArgumentException("the marker and the mask must have the same number of channels.",
                nameof(marker));
        }

        var result = new ImageBuffer(mask.Height, mask.Width, mask.Channels);
        for (int ch = 0; ch < mask.Channels; ch++)
        {
            double[] plane = Plane(marker, ch);
            double[] ceiling = Plane(mask, ch);
            ReconstructPlane(plane, ceiling, mask.Height, mask.Width, connectivity);
            WritePlane(result, plane, ch);
        }

        return result;
    }

    /// <summary>
    /// Reconstructs one plane in place. <paramref name="marker"/> is clipped to
    /// <paramref name="mask"/> on entry, so a caller may hand in a marker that overshoots.
    /// </summary>
    public static void ReconstructPlane(
        double[] marker, double[] mask, int height, int width, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(mask);
        CheckConnectivity(connectivity);

        for (int i = 0; i < marker.Length; i++)
        {
            marker[i] = Math.Min(marker[i], mask[i]);
        }

        // Forward raster scan: each pixel takes the largest value among itself and the neighbours
        // already visited, capped by the mask.
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int p = (r * width) + c;
                double best = marker[p];
                best = Math.Max(best, Neighbour(marker, height, width, r, c - 1));
                best = Math.Max(best, Neighbour(marker, height, width, r - 1, c));
                if (connectivity == 8)
                {
                    best = Math.Max(best, Neighbour(marker, height, width, r - 1, c - 1));
                    best = Math.Max(best, Neighbour(marker, height, width, r - 1, c + 1));
                }

                marker[p] = Math.Min(best, mask[p]);
            }
        }

        // Backward scan: the same in the other direction, and any pixel whose still-unvisited
        // neighbour could take more from it goes on the queue.
        var queue = new Queue<int>();
        for (int r = height - 1; r >= 0; r--)
        {
            for (int c = width - 1; c >= 0; c--)
            {
                int p = (r * width) + c;
                double best = marker[p];
                best = Math.Max(best, Neighbour(marker, height, width, r, c + 1));
                best = Math.Max(best, Neighbour(marker, height, width, r + 1, c));
                if (connectivity == 8)
                {
                    best = Math.Max(best, Neighbour(marker, height, width, r + 1, c - 1));
                    best = Math.Max(best, Neighbour(marker, height, width, r + 1, c + 1));
                }

                marker[p] = Math.Min(best, mask[p]);

                double value = marker[p];
                if (Pending(marker, mask, height, width, r, c + 1, value) ||
                    Pending(marker, mask, height, width, r + 1, c, value) ||
                    (connectivity == 8 &&
                     (Pending(marker, mask, height, width, r + 1, c - 1, value) ||
                      Pending(marker, mask, height, width, r + 1, c + 1, value))))
                {
                    queue.Enqueue(p);
                }
            }
        }

        // Propagation: raise each under-filled neighbour to whatever the mask lets through, and put it
        // back on the queue so its own neighbours get the same chance.
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            int r = p / width;
            int c = p % width;
            double value = marker[p];
            foreach ((int nr, int nc) in Neighbours(r, c, connectivity))
            {
                if ((uint)nr >= (uint)height || (uint)nc >= (uint)width)
                {
                    continue;
                }

                int q = (nr * width) + nc;
                if (marker[q] < value && mask[q] != marker[q])
                {
                    marker[q] = Math.Min(value, mask[q]);
                    queue.Enqueue(q);
                }
            }
        }
    }

    /// <summary>
    /// Fills holes (MATLAB <c>imfill(I, 'holes')</c>): reconstructing the complement from its border
    /// recovers everything the background can reach, and what is left over is a hole. On a binary
    /// picture this is exactly "background not connected to the border"; on a grayscale one it raises
    /// each enclosed dark basin to the lowest rim that surrounds it.
    /// </summary>
    public static ImageBuffer FillHoles(ImageBuffer image, int connectivity = 4)
    {
        ArgumentNullException.ThrowIfNull(image);
        CheckConnectivity(connectivity);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[] mask = Plane(image, ch);
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = -mask[i];
            }

            double[] marker = BorderMarker(mask, image.Height, image.Width);
            ReconstructPlane(marker, mask, image.Height, image.Width, connectivity);
            for (int i = 0; i < marker.Length; i++)
            {
                marker[i] = -marker[i];
            }

            WritePlane(result, marker, ch);
        }

        return result;
    }

    /// <summary>
    /// Fills the background regions containing the given seed pixels (MATLAB
    /// <c>imfill(BW, locations)</c>). Seeds are 0-based (row, column) pairs.
    /// </summary>
    public static ImageBuffer FillFrom(
        ImageBuffer image, IReadOnlyList<(int Row, int Col)> seeds, int connectivity = 4)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(seeds);
        CheckConnectivity(connectivity);
        if (image.Channels != 1)
        {
            throw new ArgumentException("seeded filling needs a binary (single-channel) image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        double[] background = Plane(image, 0);
        var mask = new double[background.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = background[i] != 0 ? 0.0 : 1.0;
        }

        var marker = new double[mask.Length];
        foreach ((int row, int col) in seeds)
        {
            if ((uint)row >= (uint)h || (uint)col >= (uint)w)
            {
                throw new ArgumentOutOfRangeException(nameof(seeds),
                    $"seed ({row}, {col}) is outside the {h}x{w} image.");
            }

            marker[(row * w) + col] = 1.0;
        }

        ReconstructPlane(marker, mask, h, w, connectivity);
        var result = new ImageBuffer(h, w, 1);
        Span<double> output = result.Pixels;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = background[i] != 0 || marker[i] != 0 ? 1.0 : 0.0;
        }

        return result;
    }

    /// <summary>
    /// Removes whatever touches the border (MATLAB <c>imclearborder</c>): reconstruct from the border
    /// and subtract, which on a grayscale picture lowers each border-connected structure to the level
    /// its surroundings reach rather than deleting it outright.
    /// </summary>
    public static ImageBuffer ClearBorder(ImageBuffer image, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        CheckConnectivity(connectivity);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[] mask = Plane(image, ch);
            double[] marker = BorderMarker(mask, image.Height, image.Width);
            ReconstructPlane(marker, mask, image.Height, image.Width, connectivity);
            for (int i = 0; i < marker.Length; i++)
            {
                marker[i] = mask[i] - marker[i];
            }

            WritePlane(result, marker, ch);
        }

        return result;
    }

    /// <summary>
    /// Suppresses maxima shallower than <paramref name="h"/> (MATLAB <c>imhmax</c>): reconstruct the
    /// picture from a copy of itself lowered by h, so any peak that does not rise h above its
    /// surroundings is flattened into them.
    /// </summary>
    public static ImageBuffer HMax(ImageBuffer image, double h, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(h);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[] mask = Plane(image, ch);
            var marker = new double[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                marker[i] = mask[i] - h;
            }

            ReconstructPlane(marker, mask, image.Height, image.Width, connectivity);
            WritePlane(result, marker, ch);
        }

        return result;
    }

    /// <summary>Suppresses minima shallower than <paramref name="h"/> (MATLAB <c>imhmin</c>).</summary>
    public static ImageBuffer HMin(ImageBuffer image, double h, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ImageBuffer inverted = PointOps.Complement(image);
        using ImageBuffer suppressed = HMax(inverted, h, connectivity);
        return PointOps.Complement(suppressed);
    }

    /// <summary>
    /// The regional maxima (MATLAB <c>imregionalmax</c>): connected plateaux of equal value from which
    /// no neighbour is higher.
    /// </summary>
    /// <remarks>
    /// Written as <c>I &gt; imhmax(I, h)</c> for an infinitesimal h this would need a tolerance, and
    /// picking one is guesswork on floating-point samples. Flooding each plateau and asking whether
    /// anything outside it is higher answers the question exactly instead.
    /// </remarks>
    public static ImageBuffer RegionalMax(ImageBuffer image, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        CheckConnectivity(connectivity);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[] plane = Plane(image, ch);
            double[] flags = Extrema(plane, image.Height, image.Width, connectivity, maxima: true);
            WritePlane(result, flags, ch);
        }

        return result;
    }

    /// <summary>The regional minima (MATLAB <c>imregionalmin</c>).</summary>
    public static ImageBuffer RegionalMin(ImageBuffer image, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        CheckConnectivity(connectivity);
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[] plane = Plane(image, ch);
            double[] flags = Extrema(plane, image.Height, image.Width, connectivity, maxima: false);
            WritePlane(result, flags, ch);
        }

        return result;
    }

    /// <summary>The extended maxima: the regional maxima of the h-maxima transform (MATLAB <c>imextendedmax</c>).</summary>
    public static ImageBuffer ExtendedMax(ImageBuffer image, double h, int connectivity = 8)
    {
        using ImageBuffer suppressed = HMax(image, h, connectivity);
        return RegionalMax(suppressed, connectivity);
    }

    /// <summary>The extended minima (MATLAB <c>imextendedmin</c>).</summary>
    public static ImageBuffer ExtendedMin(ImageBuffer image, double h, int connectivity = 8)
    {
        using ImageBuffer suppressed = HMin(image, h, connectivity);
        return RegionalMin(suppressed, connectivity);
    }

    /// <summary>
    /// Forces the regional minima to sit exactly where <paramref name="marker"/> is true and nowhere
    /// else (MATLAB <c>imimposemin</c>) — the standard way to stop a watershed over-segmenting.
    /// </summary>
    /// <remarks>
    /// The marked pixels are pushed to −∞ and everything else to +∞, then that is reconstructed under
    /// the picture raised by one grey step. Raising by a step is what removes the picture's own
    /// shallow minima; the infinities are why this works on a plane of doubles rather than on an
    /// image, which would clamp them back into range.
    /// </remarks>
    public static ImageBuffer ImposeMin(ImageBuffer image, ImageBuffer marker, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(marker);
        if (image.Height != marker.Height || image.Width != marker.Width)
        {
            throw new ArgumentException(
                $"the marker is {marker.Height}x{marker.Width} but the image is {image.Height}x{image.Width}.",
                nameof(marker));
        }

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[] plane = Plane(image, ch);
            double[] flags = Plane(marker, Math.Min(ch, marker.Channels - 1));

            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            foreach (double value in plane)
            {
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }

            double step = high > low ? (high - low) / 1000.0 : 0.1;

            // Reconstruction climbs, so both sides are negated: the minima to be imposed become the
            // maxima it can find. The mask is the picture raised by one grey step, which is what
            // removes the shallow minima the picture already had.
            var mask = new double[plane.Length];
            var seed = new double[plane.Length];
            for (int i = 0; i < plane.Length; i++)
            {
                bool marked = flags[i] != 0;
                seed[i] = marked ? double.PositiveInfinity : double.NegativeInfinity;
                mask[i] = marked ? double.PositiveInfinity : -(plane[i] + step);
            }

            ReconstructPlane(seed, mask, image.Height, image.Width, connectivity);
            for (int i = 0; i < seed.Length; i++)
            {
                // MATLAB leaves the marked pixels at -Inf, the literal global minimum. An image here
                // carries [0, 1], so they land on 0 instead — still strictly below everything else,
                // because the reconstruction raised the rest by a step.
                seed[i] = double.IsPositiveInfinity(seed[i]) ? 0.0 : Math.Clamp(-seed[i], 0.0, 1.0);
            }

            WritePlane(result, seed, ch);
        }

        return result;
    }

    /// <summary>One channel of an image as a flat row-major plane.</summary>
    internal static double[] Plane(ImageBuffer image, int channel)
    {
        var plane = new double[image.Height * image.Width];
        ReadOnlySpan<double> pixels = image.Pixels;
        int channels = image.Channels;
        for (int i = 0; i < plane.Length; i++)
        {
            plane[i] = pixels[(i * channels) + channel];
        }

        GC.KeepAlive(image);
        return plane;
    }

    /// <summary>Writes a flat plane back into one channel of an image.</summary>
    internal static void WritePlane(ImageBuffer image, double[] plane, int channel)
    {
        Span<double> pixels = image.Pixels;
        int channels = image.Channels;
        for (int i = 0; i < plane.Length; i++)
        {
            pixels[(i * channels) + channel] = plane[i];
        }

        GC.KeepAlive(image);
    }

    /// <summary>The neighbour offsets for 4- or 8-connectivity.</summary>
    internal static (int R, int C)[] Neighbours(int r, int c, int connectivity) =>
        connectivity == 4
            ? [(r - 1, c), (r + 1, c), (r, c - 1), (r, c + 1)]
            :
            [
                (r - 1, c - 1), (r - 1, c), (r - 1, c + 1),
                (r, c - 1), (r, c + 1),
                (r + 1, c - 1), (r + 1, c), (r + 1, c + 1),
            ];

    internal static void CheckConnectivity(int connectivity)
    {
        if (connectivity is not (4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectivity), connectivity, "connectivity must be 4 or 8.");
        }
    }

    private static double[] Extrema(double[] plane, int height, int width, int connectivity, bool maxima)
    {
        var flags = new double[plane.Length];
        var visited = new bool[plane.Length];
        var plateau = new List<int>();
        var queue = new Queue<int>();

        for (int start = 0; start < plane.Length; start++)
        {
            if (visited[start])
            {
                continue;
            }

            // Flood the plateau of equal-valued pixels containing this one, watching for any
            // neighbour outside it that beats the plateau's level.
            double level = plane[start];
            bool extreme = true;
            plateau.Clear();
            queue.Clear();
            queue.Enqueue(start);
            visited[start] = true;
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                plateau.Add(p);
                int r = p / width;
                int c = p % width;
                foreach ((int nr, int nc) in Neighbours(r, c, connectivity))
                {
                    if ((uint)nr >= (uint)height || (uint)nc >= (uint)width)
                    {
                        continue;
                    }

                    int q = (nr * width) + nc;
                    double value = plane[q];
                    if (value == level)
                    {
                        if (!visited[q])
                        {
                            visited[q] = true;
                            queue.Enqueue(q);
                        }
                    }
                    else if (maxima ? value > level : value < level)
                    {
                        extreme = false;
                    }
                }
            }

            if (extreme)
            {
                foreach (int p in plateau)
                {
                    flags[p] = 1.0;
                }
            }
        }

        return flags;
    }

    private static double[] BorderMarker(double[] plane, int height, int width)
    {
        var marker = new double[plane.Length];
        Array.Fill(marker, double.NegativeInfinity);
        for (int c = 0; c < width; c++)
        {
            marker[c] = plane[c];
            marker[((height - 1) * width) + c] = plane[((height - 1) * width) + c];
        }

        for (int r = 0; r < height; r++)
        {
            marker[r * width] = plane[r * width];
            marker[(r * width) + width - 1] = plane[(r * width) + width - 1];
        }

        return marker;
    }

    private static double Neighbour(double[] plane, int height, int width, int r, int c) =>
        (uint)r < (uint)height && (uint)c < (uint)width
            ? plane[(r * width) + c]
            : double.NegativeInfinity;

    private static bool Pending(
        double[] marker, double[] mask, int height, int width, int r, int c, double value)
    {
        if ((uint)r >= (uint)height || (uint)c >= (uint)width)
        {
            return false;
        }

        int q = (r * width) + c;
        return marker[q] < value && marker[q] < mask[q];
    }
}
