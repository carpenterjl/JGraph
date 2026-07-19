namespace JGraph.Imaging;

/// <summary>Connected-component labeling and region measurement for binary images.</summary>
public static class Regions
{
    /// <summary>Per-region measurements (MATLAB <c>regionprops</c> basics; bounding box uses the 0.5-pixel-offset convention).</summary>
    public readonly record struct RegionProperty(
        int Label, int Area, double CentroidX, double CentroidY,
        double BoundingBoxX, double BoundingBoxY, double BoundingBoxWidth, double BoundingBoxHeight);

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

    /// <summary>Measures Area, Centroid, and BoundingBox for each labeled region.</summary>
    public static RegionProperty[] Measure(int[,] labels, int count)
    {
        ArgumentNullException.ThrowIfNull(labels);
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
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
            }
        }

        var result = new RegionProperty[count];
        for (int i = 1; i <= count; i++)
        {
            // Centroid is 1-based (MATLAB); bounding box starts half a pixel before the first pixel.
            result[i - 1] = new RegionProperty(
                i,
                area[i],
                (sumX[i] / area[i]) + 1,
                (sumY[i] / area[i]) + 1,
                minX[i] + 0.5,
                minY[i] + 0.5,
                maxX[i] - minX[i] + 1,
                maxY[i] - minY[i] + 1);
        }

        return result;
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
