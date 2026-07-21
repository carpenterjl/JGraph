namespace JGraph.Imaging;

/// <summary>Connected-component labeling and region measurement for binary images.</summary>
public static class Regions
{
    /// <summary>
    /// Per-region measurements (MATLAB <c>regionprops</c> basics; bounding box uses the 0.5-pixel-offset
    /// convention). The three intensity fields are NaN unless the region was measured against an
    /// intensity image.
    /// </summary>
    public readonly record struct RegionProperty(
        int Label, int Area, double CentroidX, double CentroidY,
        double BoundingBoxX, double BoundingBoxY, double BoundingBoxWidth, double BoundingBoxHeight,
        double MeanIntensity = double.NaN,
        double WeightedCentroidX = double.NaN,
        double WeightedCentroidY = double.NaN);

    /// <summary>
    /// Labels connected foreground (nonzero) components of a binary image with a two-pass union-find
    /// (MATLAB <c>bwlabel</c>). Connectivity is 4 or 8 (default 8). Returns the label map (0 = background,
    /// 1..n = components) and the component count.
    /// </summary>
    public static (int[,] Labels, int Count) Label(ImageBuffer image, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (connectivity is not (4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(connectivity), connectivity, "connectivity must be 4 or 8.");
        }

        int h = image.Height;
        int w = image.Width;
        var labels = new int[h, w];
        var union = new UnionFind(((h * w) / 2) + 1);
        int next = 1;

        // First pass: provisional labels + equivalence recording over already-scanned neighbours.
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (image[r, c, 0] == 0)
                {
                    continue;
                }

                int best = 0;
                foreach ((int nr, int nc) in PriorNeighbours(r, c, connectivity))
                {
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w && labels[nr, nc] != 0)
                    {
                        int label = labels[nr, nc];
                        best = best == 0 ? label : union.Union(best, label);
                    }
                }

                if (best == 0)
                {
                    best = next++;
                    union.Ensure(best);
                }

                labels[r, c] = best;
            }
        }

        // Second pass: flatten to canonical roots and renumber them 1..count densely.
        var remap = new Dictionary<int, int>();
        int count = 0;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (labels[r, c] == 0)
                {
                    continue;
                }

                int root = union.Find(labels[r, c]);
                if (!remap.TryGetValue(root, out int dense))
                {
                    dense = ++count;
                    remap[root] = dense;
                }

                labels[r, c] = dense;
            }
        }

        return (labels, count);
    }

    /// <summary>
    /// Measures Area, Centroid, and BoundingBox for each labeled region. When an
    /// <paramref name="intensity"/> image is supplied (grayscale, same height and width as the label
    /// map) each region also gets its MeanIntensity and its intensity-weighted centroid
    /// (MATLAB <c>WeightedCentroid</c>): the sample-weighted mean of the 1-based pixel coordinates.
    /// </summary>
    /// <param name="labels">The label map, 0 = background.</param>
    /// <param name="count">The number of labeled regions.</param>
    /// <param name="intensity">The optional grayscale image the weights are read from.</param>
    public static RegionProperty[] Measure(int[,] labels, int count, ImageBuffer? intensity = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
        if (intensity is not null)
        {
            if (intensity.Channels != 1)
            {
                throw new ArgumentException("The intensity image must be grayscale.", nameof(intensity));
            }

            if (intensity.Height != h || intensity.Width != w)
            {
                throw new ArgumentException(
                    $"The intensity image is {intensity.Height}x{intensity.Width} but the labels are {h}x{w}.",
                    nameof(intensity));
            }
        }

        var weight = new double[count + 1];
        var weightedX = new double[count + 1];
        var weightedY = new double[count + 1];
        var area = new int[count + 1];
        var sumX = new double[count + 1];
        var sumY = new double[count + 1];
        var minX = new int[count + 1];
        var minY = new int[count + 1];
        var maxX = new int[count + 1];
        var maxY = new int[count + 1];
        for (int i = 1; i <= count; i++)
        {
            minX[i] = int.MaxValue;
            minY[i] = int.MaxValue;
            maxX[i] = int.MinValue;
            maxY[i] = int.MinValue;
        }

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int label = labels[r, c];
                if (label == 0)
                {
                    continue;
                }

                area[label]++;
                sumX[label] += c;
                sumY[label] += r;
                minX[label] = Math.Min(minX[label], c);
                minY[label] = Math.Min(minY[label], r);
                maxX[label] = Math.Max(maxX[label], c);
                maxY[label] = Math.Max(maxY[label], r);

                if (intensity is not null)
                {
                    double value = intensity[r, c, 0];
                    weight[label] += value;
                    weightedX[label] += value * c;
                    weightedY[label] += value * r;
                }
            }
        }

        var result = new RegionProperty[count];
        for (int i = 1; i <= count; i++)
        {
            // A region whose samples are all zero has no weighted centre; NaN says so rather than
            // silently reporting the geometric one.
            (double mean, double wx, double wy) = intensity is null || weight[i] == 0
                ? (double.NaN, double.NaN, double.NaN)
                : (weight[i] / area[i], (weightedX[i] / weight[i]) + 1, (weightedY[i] / weight[i]) + 1);

            // Centroid is 1-based (MATLAB); bounding box starts half a pixel before the first pixel.
            result[i - 1] = new RegionProperty(
                i,
                area[i],
                (sumX[i] / area[i]) + 1,
                (sumY[i] / area[i]) + 1,
                minX[i] + 0.5,
                minY[i] + 0.5,
                maxX[i] - minX[i] + 1,
                maxY[i] - minY[i] + 1,
                mean,
                wx,
                wy);
        }

        return result;
    }

    /// <summary>
    /// The intensity-weighted centre of a whole grayscale image: the sample-weighted mean of the
    /// 1-based pixel coordinates, <c>Σ(x·w) / Σw</c>. Unlike <see cref="Measure"/> this ignores
    /// connectivity entirely — every nonzero sample pulls on the result — which is what a
    /// centre-of-mass estimate over a thresholded (masked) image means. Zero out the background
    /// first; a sample of 0 contributes nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The image is not grayscale, or every sample is zero.</exception>
    public static (double X, double Y) WeightedCentroid(ImageBuffer weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Channels != 1)
        {
            throw new ArgumentException("The weight image must be grayscale.", nameof(weights));
        }

        double total = 0;
        double sumX = 0;
        double sumY = 0;
        for (int r = 0; r < weights.Height; r++)
        {
            for (int c = 0; c < weights.Width; c++)
            {
                double value = weights[r, c, 0];
                total += value;
                sumX += value * c;
                sumY += value * r;
            }
        }

        if (total <= 0)
        {
            throw new ArgumentException("The weight image has no positive samples.", nameof(weights));
        }

        return ((sumX / total) + 1, (sumY / total) + 1);
    }

    /// <summary>
    /// Fills holes in a binary image (MATLAB <c>imfill(bw, 'holes')</c>): background pixels that cannot
    /// be reached from the image border become foreground. Reachability uses 4-connectivity on the
    /// background, the complement of <c>bwlabel</c>'s default 8-connected foreground.
    /// </summary>
    public static ImageBuffer FillHoles(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels != 1)
        {
            throw new ArgumentException("imfill needs a binary (single-channel) image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        var reachable = new bool[h, w];
        var queue = new Queue<(int R, int C)>();

        void Seed(int r, int c)
        {
            if (image[r, c, 0] == 0 && !reachable[r, c])
            {
                reachable[r, c] = true;
                queue.Enqueue((r, c));
            }
        }

        for (int c = 0; c < w; c++)
        {
            Seed(0, c);
            Seed(h - 1, c);
        }

        for (int r = 0; r < h; r++)
        {
            Seed(r, 0);
            Seed(r, w - 1);
        }

        while (queue.Count > 0)
        {
            (int r, int c) = queue.Dequeue();
            Seed(r - 1 < 0 ? r : r - 1, c);
            Seed(r + 1 >= h ? r : r + 1, c);
            Seed(r, c - 1 < 0 ? c : c - 1);
            Seed(r, c + 1 >= w ? c : c + 1);
        }

        var filled = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                filled[r, c, 0] = image[r, c, 0] != 0 || !reachable[r, c] ? 1.0 : 0.0;
            }
        }

        return filled;
    }

    /// <summary>
    /// Removes connected components smaller than <paramref name="minArea"/> pixels from a binary image
    /// (MATLAB <c>bwareaopen</c>). Connectivity is 4 or 8 (default 8).
    /// </summary>
    public static ImageBuffer AreaOpen(ImageBuffer image, int minArea, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        (int[,] labels, int count) = Label(image, connectivity);
        var area = new int[count + 1];
        foreach (int label in labels)
        {
            area[label]++;
        }

        int h = image.Height;
        int w = image.Width;
        var kept = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int label = labels[r, c];
                kept[r, c, 0] = label != 0 && area[label] >= minArea ? 1.0 : 0.0;
            }
        }

        return kept;
    }

    /// <summary>Wraps an integer label map as a grayscale image (label values stored as-is).</summary>
    public static ImageBuffer LabelsToImage(int[,] labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
        var image = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                image[r, c, 0] = labels[r, c];
            }
        }

        return image;
    }

    /// <summary>Reads an integer label map back out of an image (rounding samples to labels).</summary>
    public static int[,] ImageToLabels(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var labels = new int[image.Height, image.Width];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                labels[r, c] = (int)Math.Round(image[r, c, 0]);
            }
        }

        return labels;
    }

    private static IEnumerable<(int R, int C)> PriorNeighbours(int r, int c, int connectivity)
    {
        yield return (r, c - 1);
        yield return (r - 1, c);
        if (connectivity == 8)
        {
            yield return (r - 1, c - 1);
            yield return (r - 1, c + 1);
        }
    }

    private sealed class UnionFind
    {
        private int[] _parent;

        public UnionFind(int capacity) => _parent = new int[Math.Max(2, capacity)];

        public void Ensure(int label)
        {
            if (label >= _parent.Length)
            {
                Array.Resize(ref _parent, Math.Max(label + 1, _parent.Length * 2));
            }

            if (_parent[label] == 0)
            {
                _parent[label] = label;
            }
        }

        public int Find(int label)
        {
            Ensure(label);
            while (_parent[label] != label)
            {
                _parent[label] = _parent[_parent[label]];
                label = _parent[label];
            }

            return label;
        }

        public int Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra == rb)
            {
                return ra;
            }

            int root = Math.Min(ra, rb);
            int other = Math.Max(ra, rb);
            _parent[other] = root;
            return root;
        }
    }
}
