namespace JGraph.Imaging;

/// <summary>One connected region of a volume, measured the way MATLAB's <c>regionprops3</c> measures it.</summary>
public sealed class VolumeMeasurement
{
    /// <summary>The region's label in the map it was measured from.</summary>
    public int Label { get; init; }

    /// <summary>Voxel count.</summary>
    public int Volume { get; init; }

    /// <summary>Centre of mass as (x, y, z) — column, row, plane — zero-based.</summary>
    public (double X, double Y, double Z) Centroid { get; init; }

    /// <summary>The smallest box holding the region: an origin half a voxel before the first voxel, then extents.</summary>
    public (double X, double Y, double Z, double Width, double Height, double Depth) BoundingBox { get; init; }

    /// <summary>The diameter of the sphere with the region's volume.</summary>
    public double EquivDiameter { get; init; }

    /// <summary>Volume divided by the bounding box's volume.</summary>
    public double Extent { get; init; }

    /// <summary>Surface area, counted as the faces of the region's voxels that face outwards.</summary>
    public double SurfaceArea { get; init; }

    /// <summary>The three axis lengths of the ellipsoid with the region's second moments, longest first.</summary>
    public double[] PrincipalAxisLength { get; init; } = [0, 0, 0];

    /// <summary>The second-moment eigenvalues, largest first.</summary>
    public double[] EigenValues { get; init; } = [0, 0, 0];

    /// <summary>The matching eigenvectors as columns, in (x, y, z).</summary>
    public double[,] EigenVectors { get; init; } = new double[3, 3];

    /// <summary>The Euler angles in degrees that turn the axes onto the region's own, in Z-Y-X order.</summary>
    public double[] Orientation { get; init; } = [0, 0, 0];

    /// <summary>Every voxel in the region, zero-based (row, column, plane).</summary>
    public (int Row, int Col, int Plane)[] Voxels { get; init; } = [];

    /// <summary>The intensity at each of those voxels; empty without an intensity volume.</summary>
    public double[] VoxelValues { get; init; } = [];

    /// <summary>Mean of the intensity volume over the region; NaN without one.</summary>
    public double MeanIntensity { get; init; } = double.NaN;

    /// <summary>Smallest intensity in the region; NaN without an intensity volume.</summary>
    public double MinIntensity { get; init; } = double.NaN;

    /// <summary>Largest intensity in the region; NaN without an intensity volume.</summary>
    public double MaxIntensity { get; init; } = double.NaN;

    /// <summary>Intensity-weighted centre as (x, y, z); NaN without an intensity volume.</summary>
    public (double X, double Y, double Z) WeightedCentroid { get; init; } =
        (double.NaN, double.NaN, double.NaN);
}

/// <summary>
/// Connectivity, labelling, measurement and clustering on a <see cref="Volume"/> — the counterparts of
/// <see cref="Regions"/>, <see cref="RegionProperties"/> and <see cref="Segmentation"/>.
/// </summary>
/// <remarks>
/// Connectivity in three dimensions is a real choice rather than a detail. Six neighbours share a
/// face, eighteen share at least an edge, twenty-six share at least a corner — and two blobs that meet
/// only at a corner are one object under 26-connectivity and two under 6. Every function here takes
/// the choice explicitly and none of them guesses.
/// </remarks>
public static class VolumeRegions
{
    /// <summary>The operations <c>bwmorph3</c> documents.</summary>
    public enum MorphOperation
    {
        /// <summary>Voxels where a skeleton divides.</summary>
        BranchPoints,

        /// <summary>Isolated voxels, removed.</summary>
        Clean,

        /// <summary>Voxels where a skeleton stops.</summary>
        EndPoints,

        /// <summary>Interior holes, filled.</summary>
        Fill,

        /// <summary>Voxels kept only where most of the neighbourhood agrees.</summary>
        Majority,

        /// <summary>Interior voxels removed, leaving the surface.</summary>
        Remove,
    }

    /// <summary>The neighbour offsets a connectivity of 6, 18 or 26 stands for.</summary>
    public static (int R, int C, int P)[] Neighbours(int connectivity)
    {
        if (connectivity is not (6 or 18 or 26))
        {
            throw new ArgumentOutOfRangeException(nameof(connectivity), connectivity,
                "3-D connectivity is 6 (faces), 18 (faces and edges) or 26 (faces, edges and corners).");
        }

        var offsets = new List<(int R, int C, int P)>();
        for (int p = -1; p <= 1; p++)
        {
            for (int c = -1; c <= 1; c++)
            {
                for (int r = -1; r <= 1; r++)
                {
                    int steps = Math.Abs(r) + Math.Abs(c) + Math.Abs(p);
                    if (steps == 0)
                    {
                        continue;
                    }

                    if (steps <= (connectivity == 6 ? 1 : connectivity == 18 ? 2 : 3))
                    {
                        offsets.Add((r, c, p));
                    }
                }
            }
        }

        return [.. offsets];
    }

    /// <summary>
    /// Labels the connected regions of a binary volume (MATLAB <c>bwlabeln</c>), numbering them from 1
    /// in the order their first voxel is reached scanning column-major.
    /// </summary>
    public static (int[] Labels, int Count) Label(Volume mask, int connectivity = 26)
    {
        ArgumentNullException.ThrowIfNull(mask);
        (int R, int C, int P)[] offsets = Neighbours(connectivity);
        int height = mask.Height;
        int width = mask.Width;
        int depth = mask.Depth;
        var labels = new int[height * width * depth];
        ReadOnlySpan<double> samples = mask.Samples;
        var queue = new Queue<(int R, int C, int P)>();
        int next = 0;

        for (int p = 0; p < depth; p++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    int index = r + (c * height) + (p * height * width);
                    if (samples[index] == 0 || labels[index] != 0)
                    {
                        continue;
                    }

                    next++;
                    labels[index] = next;
                    queue.Enqueue((r, c, p));
                    while (queue.Count > 0)
                    {
                        (int qr, int qc, int qp) = queue.Dequeue();
                        foreach ((int dr, int dc, int dp) in offsets)
                        {
                            int nr = qr + dr;
                            int nc = qc + dc;
                            int np = qp + dp;
                            if (nr < 0 || nr >= height || nc < 0 || nc >= width || np < 0 || np >= depth)
                            {
                                continue;
                            }

                            int neighbour = nr + (nc * height) + (np * height * width);
                            if (samples[neighbour] == 0 || labels[neighbour] != 0)
                            {
                                continue;
                            }

                            labels[neighbour] = next;
                            queue.Enqueue((nr, nc, np));
                        }
                    }
                }
            }
        }

        GC.KeepAlive(mask);
        return (labels, next);
    }

    /// <summary>
    /// Removes connected regions smaller than <paramref name="minVoxels"/> (the volume form of
    /// <c>bwareaopen</c>).
    /// </summary>
    public static Volume AreaOpen(Volume mask, int minVoxels, int connectivity = 26)
    {
        ArgumentNullException.ThrowIfNull(mask);
        (int[] labels, int count) = Label(mask, connectivity);
        var sizes = new int[count + 1];
        foreach (int label in labels)
        {
            if (label > 0)
            {
                sizes[label]++;
            }
        }

        var result = Volume.Like(mask);
        Span<double> target = result.Samples;
        for (int i = 0; i < labels.Length; i++)
        {
            target[i] = labels[i] > 0 && sizes[labels[i]] >= minVoxels ? 1 : 0;
        }

        return result;
    }

    /// <summary>
    /// The regions containing the given seeds (MATLAB <c>bwselect3</c>). Seeds are zero-based
    /// (row, column, plane); a seed on a background voxel selects nothing.
    /// </summary>
    public static Volume Select(
        Volume mask, IReadOnlyList<(int Row, int Col, int Plane)> seeds, int connectivity = 26)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(seeds);
        (int[] labels, int count) = Label(mask, connectivity);
        var wanted = new bool[count + 1];
        foreach ((int r, int c, int p) in seeds)
        {
            if (r < 0 || r >= mask.Height || c < 0 || c >= mask.Width || p < 0 || p >= mask.Depth)
            {
                continue;
            }

            int label = labels[r + (c * mask.Height) + (p * mask.Height * mask.Width)];
            if (label > 0)
            {
                wanted[label] = true;
            }
        }

        var result = Volume.Like(mask);
        Span<double> target = result.Samples;
        for (int i = 0; i < labels.Length; i++)
        {
            target[i] = labels[i] > 0 && wanted[labels[i]] ? 1 : 0;
        }

        return result;
    }

    /// <summary>
    /// The neighbourhood operations of <c>bwmorph3</c>. Each looks at one voxel's 26-neighbourhood and
    /// decides on that alone, so all of them are one pass.
    /// </summary>
    public static Volume Morph(Volume mask, MorphOperation operation)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var result = Volume.Like(mask);
        (int R, int C, int P)[] faces = Neighbours(6);
        (int R, int C, int P)[] all = Neighbours(26);
        for (int p = 0; p < mask.Depth; p++)
        {
            for (int c = 0; c < mask.Width; c++)
            {
                for (int r = 0; r < mask.Height; r++)
                {
                    bool here = mask[r, c, p] != 0;
                    int neighbours = 0;
                    foreach ((int dr, int dc, int dp) in all)
                    {
                        if (Foreground(mask, r + dr, c + dc, p + dp))
                        {
                            neighbours++;
                        }
                    }

                    int faceNeighbours = 0;
                    foreach ((int dr, int dc, int dp) in faces)
                    {
                        if (Foreground(mask, r + dr, c + dc, p + dp))
                        {
                            faceNeighbours++;
                        }
                    }

                    bool keep = operation switch
                    {
                        MorphOperation.Clean => here && neighbours > 0,
                        MorphOperation.Fill => here || faceNeighbours == 6,
                        MorphOperation.Majority => neighbours + (here ? 1 : 0) >= 14,
                        MorphOperation.Remove => here && faceNeighbours < 6,
                        MorphOperation.EndPoints => here && neighbours == 1,
                        MorphOperation.BranchPoints => here && Branches(mask, r, c, p) >= 3,
                        _ => here,
                    };

                    result[r, c, p] = keep ? 1 : 0;
                }
            }
        }

        GC.KeepAlive(mask);
        return result;
    }

    /// <summary>
    /// Measures every labelled region of a volume (MATLAB <c>regionprops3</c>). Labels run
    /// 1…<paramref name="count"/>, and a label with no voxels still produces an entry, so row
    /// <c>k</c> is always region <c>k</c>.
    /// </summary>
    public static VolumeMeasurement[] Measure(
        int[] labels, int count, (int Height, int Width, int Depth) size, Volume? intensity = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (intensity is not null &&
            (intensity.Height != size.Height || intensity.Width != size.Width || intensity.Depth != size.Depth))
        {
            throw new ArgumentException("the intensity volume is not the same size as the labels.", nameof(intensity));
        }

        var voxels = new List<(int Row, int Col, int Plane)>[count + 1];
        for (int i = 1; i <= count; i++)
        {
            voxels[i] = [];
        }

        for (int p = 0; p < size.Depth; p++)
        {
            for (int c = 0; c < size.Width; c++)
            {
                for (int r = 0; r < size.Height; r++)
                {
                    int label = labels[r + (c * size.Height) + (p * size.Height * size.Width)];
                    if (label >= 1 && label <= count)
                    {
                        voxels[label].Add((r, c, p));
                    }
                }
            }
        }

        var results = new VolumeMeasurement[count];
        for (int label = 1; label <= count; label++)
        {
            results[label - 1] = MeasureOne(label, voxels[label], size, intensity);
        }

        return results;
    }

    /// <summary>
    /// k-means over voxel values (MATLAB <c>imsegkmeans3</c>), seeded by spreading the initial centres
    /// evenly through the value range.
    /// </summary>
    /// <remarks>
    /// The seeding is deliberately deterministic rather than random. A volume has enough samples that
    /// k-means++ buys nothing over an even spread of the range, and a segmentation that came back
    /// different every time it was run would make the same script unreproducible.
    /// </remarks>
    public static (int[] Labels, double[] Centers) KMeans(
        Volume volume, int clusters, int iterations = 100)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clusters);
        ReadOnlySpan<double> samples = volume.Samples;
        var centers = new double[clusters];
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        for (int i = 0; i < samples.Length; i++)
        {
            low = Math.Min(low, samples[i]);
            high = Math.Max(high, samples[i]);
        }

        for (int k = 0; k < clusters; k++)
        {
            centers[k] = clusters == 1
                ? 0.5 * (low + high)
                : low + ((high - low) * ((k + 0.5) / clusters));
        }

        var labels = new int[samples.Length];
        var sums = new double[clusters];
        var counts = new int[clusters];
        for (int pass = 0; pass < iterations; pass++)
        {
            bool changed = false;
            Array.Clear(sums);
            Array.Clear(counts);
            for (int i = 0; i < samples.Length; i++)
            {
                int best = 0;
                double bestDistance = double.PositiveInfinity;
                for (int k = 0; k < clusters; k++)
                {
                    double d = samples[i] - centers[k];
                    double distance = d * d;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = k;
                    }
                }

                if (labels[i] != best + 1)
                {
                    labels[i] = best + 1;
                    changed = true;
                }

                counts[best]++;
                sums[best] += samples[i];
            }

            for (int k = 0; k < clusters; k++)
            {
                if (counts[k] > 0)
                {
                    centers[k] = sums[k] / counts[k];
                }
            }

            if (!changed)
            {
                break;
            }
        }

        GC.KeepAlive(volume);
        return (labels, centers);
    }

    /// <summary>
    /// SLIC supervoxels (MATLAB <c>superpixels3</c>): k-means in a four-dimensional space of value and
    /// position, searched only within twice the expected supervoxel spacing.
    /// </summary>
    public static (int[] Labels, int Count) Superpixels(
        Volume volume, int wanted, double compactness = 0.001, int iterations = 10)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wanted);
        int height = volume.Height;
        int width = volume.Width;
        int depth = volume.Depth;
        double spacing = Math.Cbrt((double)height * width * depth / wanted);
        int step = Math.Max(1, (int)Math.Round(spacing));

        var centers = new List<(double R, double C, double P, double Value)>();
        for (int p = step / 2; p < depth; p += step)
        {
            for (int c = step / 2; c < width; c += step)
            {
                for (int r = step / 2; r < height; r += step)
                {
                    centers.Add((r, c, p, volume[r, c, p]));
                }
            }
        }

        if (centers.Count == 0)
        {
            centers.Add((height / 2.0, width / 2.0, depth / 2.0, 0));
        }

        var labels = new int[height * width * depth];
        var distance = new double[labels.Length];
        for (int pass = 0; pass < iterations; pass++)
        {
            Array.Clear(labels);
            Array.Fill(distance, double.PositiveInfinity);
            for (int k = 0; k < centers.Count; k++)
            {
                (double cr, double cc, double cp, double value) = centers[k];
                int fromR = Math.Max(0, (int)(cr - (2 * step)));
                int toR = Math.Min(height - 1, (int)(cr + (2 * step)));
                int fromC = Math.Max(0, (int)(cc - (2 * step)));
                int toC = Math.Min(width - 1, (int)(cc + (2 * step)));
                int fromP = Math.Max(0, (int)(cp - (2 * step)));
                int toP = Math.Min(depth - 1, (int)(cp + (2 * step)));
                for (int p = fromP; p <= toP; p++)
                {
                    for (int c = fromC; c <= toC; c++)
                    {
                        for (int r = fromR; r <= toR; r++)
                        {
                            double dv = volume[r, c, p] - value;
                            double spatial = (((r - cr) * (r - cr)) + ((c - cc) * (c - cc))
                                + ((p - cp) * (p - cp))) / (step * (double)step);
                            double total = (dv * dv) + (compactness * spatial);
                            int index = r + (c * height) + (p * height * width);
                            if (total < distance[index])
                            {
                                distance[index] = total;
                                labels[index] = k + 1;
                            }
                        }
                    }
                }
            }

            var sumR = new double[centers.Count];
            var sumC = new double[centers.Count];
            var sumP = new double[centers.Count];
            var sumV = new double[centers.Count];
            var counts = new int[centers.Count];
            for (int p = 0; p < depth; p++)
            {
                for (int c = 0; c < width; c++)
                {
                    for (int r = 0; r < height; r++)
                    {
                        int label = labels[r + (c * height) + (p * height * width)];
                        if (label == 0)
                        {
                            continue;
                        }

                        int k = label - 1;
                        sumR[k] += r;
                        sumC[k] += c;
                        sumP[k] += p;
                        sumV[k] += volume[r, c, p];
                        counts[k]++;
                    }
                }
            }

            for (int k = 0; k < centers.Count; k++)
            {
                if (counts[k] > 0)
                {
                    centers[k] = (sumR[k] / counts[k], sumC[k] / counts[k], sumP[k] / counts[k],
                        sumV[k] / counts[k]);
                }
            }
        }

        // A supervoxel that lost every voxel would leave a gap in the numbering, and MATLAB's second
        // output is the count a script indexes with — so the labels are renumbered to be contiguous.
        var mapping = new int[centers.Count + 1];
        int used = 0;
        for (int i = 0; i < labels.Length; i++)
        {
            int label = labels[i];
            if (label == 0)
            {
                continue;
            }

            if (mapping[label] == 0)
            {
                mapping[label] = ++used;
            }

            labels[i] = mapping[label];
        }

        GC.KeepAlive(volume);
        return (labels, used);
    }

    private static bool Foreground(Volume mask, int r, int c, int p) =>
        r >= 0 && r < mask.Height && c >= 0 && c < mask.Width && p >= 0 && p < mask.Depth
        && mask[r, c, p] != 0;

    // How many separate pieces the foreground in a voxel's 26-neighbourhood falls into. A skeleton
    // voxel with three or more is where the skeleton divides; counting neighbours instead would call
    // every fat spot a branch.
    private static int Branches(Volume mask, int r, int c, int p)
    {
        Span<bool> occupied = stackalloc bool[27];
        for (int i = 0, dp = -1; dp <= 1; dp++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                for (int dr = -1; dr <= 1; dr++, i++)
                {
                    occupied[i] = (dr != 0 || dc != 0 || dp != 0) && Foreground(mask, r + dr, c + dc, p + dp);
                }
            }
        }

        Span<bool> seen = stackalloc bool[27];
        int pieces = 0;
        Span<int> stack = stackalloc int[27];
        for (int start = 0; start < 27; start++)
        {
            if (!occupied[start] || seen[start])
            {
                continue;
            }

            pieces++;
            int top = 0;
            stack[top++] = start;
            seen[start] = true;
            while (top > 0)
            {
                int index = stack[--top];
                int ir = (index % 3) - 1;
                int ic = ((index / 3) % 3) - 1;
                int ip = (index / 9) - 1;
                for (int dp = -1; dp <= 1; dp++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        for (int dr = -1; dr <= 1; dr++)
                        {
                            int nr = ir + dr;
                            int nc = ic + dc;
                            int np = ip + dp;
                            if (nr is < -1 or > 1 || nc is < -1 or > 1 || np is < -1 or > 1)
                            {
                                continue;
                            }

                            int neighbour = (nr + 1) + ((nc + 1) * 3) + ((np + 1) * 9);
                            if (occupied[neighbour] && !seen[neighbour])
                            {
                                seen[neighbour] = true;
                                stack[top++] = neighbour;
                            }
                        }
                    }
                }
            }
        }

        return pieces;
    }

    private static VolumeMeasurement MeasureOne(
        int label,
        List<(int Row, int Col, int Plane)> voxels,
        (int Height, int Width, int Depth) size,
        Volume? intensity)
    {
        if (voxels.Count == 0)
        {
            return new VolumeMeasurement { Label = label };
        }

        double sumR = 0;
        double sumC = 0;
        double sumP = 0;
        int minR = int.MaxValue;
        int maxR = int.MinValue;
        int minC = int.MaxValue;
        int maxC = int.MinValue;
        int minP = int.MaxValue;
        int maxP = int.MinValue;
        foreach ((int r, int c, int p) in voxels)
        {
            sumR += r;
            sumC += c;
            sumP += p;
            minR = Math.Min(minR, r);
            maxR = Math.Max(maxR, r);
            minC = Math.Min(minC, c);
            maxC = Math.Max(maxC, c);
            minP = Math.Min(minP, p);
            maxP = Math.Max(maxP, p);
        }

        int n = voxels.Count;
        double meanR = sumR / n;
        double meanC = sumC / n;
        double meanP = sumP / n;

        // The second-moment matrix in (x, y, z). The 1/12 on the diagonal is the moment of a voxel
        // about its own centre: without it a single voxel would have no extent at all, and its
        // principal axes would be zero rather than one voxel across.
        double xx = 1.0 / 12;
        double yy = 1.0 / 12;
        double zz = 1.0 / 12;
        double xy = 0;
        double xz = 0;
        double yz = 0;
        foreach ((int r, int c, int p) in voxels)
        {
            double dx = c - meanC;
            double dy = r - meanR;
            double dz = p - meanP;
            xx += dx * dx / n;
            yy += dy * dy / n;
            zz += dz * dz / n;
            xy += dx * dy / n;
            xz += dx * dz / n;
            yz += dy * dz / n;
        }

        (double[] values, double[,] vectors) = SymmetricEigen(new[,]
        {
            { xx, xy, xz },
            { xy, yy, yz },
            { xz, yz, zz },
        });

        var axes = new double[3];
        for (int i = 0; i < 3; i++)
        {
            // A solid ellipsoid's second moment about its centre along a principal axis is a²/5, so
            // the full axis length is 2·sqrt(5λ) — the three-dimensional reading of the 4·sqrt(λ) that
            // regionprops uses for an ellipse.
            axes[i] = 2 * Math.Sqrt(5 * Math.Max(values[i], 0));
        }

        double boxWidth = maxC - minC + 1;
        double boxHeight = maxR - minR + 1;
        double boxDepth = maxP - minP + 1;

        var occupied = new HashSet<(int, int, int)>(voxels);
        double surface = 0;
        foreach ((int r, int c, int p) in voxels)
        {
            foreach ((int dr, int dc, int dp) in Neighbours(6))
            {
                if (!occupied.Contains((r + dr, c + dc, p + dp)))
                {
                    surface++;
                }
            }
        }

        double[] intensities = [];
        double mean = double.NaN;
        double low = double.NaN;
        double high = double.NaN;
        (double X, double Y, double Z) weighted = (double.NaN, double.NaN, double.NaN);
        if (intensity is not null)
        {
            intensities = new double[n];
            double total = 0;
            double weightedR = 0;
            double weightedC = 0;
            double weightedP = 0;
            low = double.PositiveInfinity;
            high = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                (int r, int c, int p) = voxels[i];
                double value = intensity[r, c, p];
                intensities[i] = value;
                total += value;
                low = Math.Min(low, value);
                high = Math.Max(high, value);
                weightedR += value * r;
                weightedC += value * c;
                weightedP += value * p;
            }

            mean = total / n;
            weighted = total > 0
                ? (weightedC / total, weightedR / total, weightedP / total)
                : (meanC, meanR, meanP);
        }

        return new VolumeMeasurement
        {
            Label = label,
            Volume = n,
            Centroid = (meanC, meanR, meanP),
            BoundingBox = (minC - 0.5, minR - 0.5, minP - 0.5, boxWidth, boxHeight, boxDepth),
            EquivDiameter = 2 * Math.Cbrt(3 * n / (4 * Math.PI)),
            Extent = n / (boxWidth * boxHeight * boxDepth),
            SurfaceArea = surface,
            PrincipalAxisLength = axes,
            EigenValues = values,
            EigenVectors = vectors,
            Orientation = EulerAngles(vectors),
            Voxels = [.. voxels],
            VoxelValues = intensities,
            MeanIntensity = mean,
            MinIntensity = low,
            MaxIntensity = high,
            WeightedCentroid = weighted,
        };
    }

    // Jacobi rotations on a 3×3 symmetric matrix. Three dimensions is small enough that the sweep
    // converges in a handful of passes and the eigenvectors come out orthogonal to machine precision,
    // which matters because they are read as a rotation.
    private static (double[] Values, double[,] Vectors) SymmetricEigen(double[,] matrix)
    {
        var a = (double[,])matrix.Clone();
        var v = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            v[i, i] = 1;
        }

        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = (a[0, 1] * a[0, 1]) + (a[0, 2] * a[0, 2]) + (a[1, 2] * a[1, 2]);
            if (off < 1e-30)
            {
                break;
            }

            for (int p = 0; p < 2; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300)
                    {
                        continue;
                    }

                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
                    if (theta == 0)
                    {
                        t = 1;
                    }

                    double cos = 1 / Math.Sqrt((t * t) + 1);
                    double sin = t * cos;
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k, p];
                        double akq = a[k, q];
                        a[k, p] = (cos * akp) - (sin * akq);
                        a[k, q] = (sin * akp) + (cos * akq);
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p, k];
                        double aqk = a[q, k];
                        a[p, k] = (cos * apk) - (sin * aqk);
                        a[q, k] = (sin * apk) + (cos * aqk);
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p];
                        double vkq = v[k, q];
                        v[k, p] = (cos * vkp) - (sin * vkq);
                        v[k, q] = (sin * vkp) + (cos * vkq);
                    }
                }
            }
        }

        int[] order = [0, 1, 2];
        Array.Sort(order, (x, y) => a[y, y].CompareTo(a[x, x]));
        var values = new double[3];
        var vectors = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            values[i] = a[order[i], order[i]];
            for (int k = 0; k < 3; k++)
            {
                vectors[k, i] = v[k, order[i]];
            }
        }

        return (values, vectors);
    }

    // The Z-Y-X Euler angles of the rotation whose columns are the region's own axes, in degrees.
    private static double[] EulerAngles(double[,] r)
    {
        double pitch = Math.Asin(Math.Clamp(-r[2, 0], -1, 1));
        double yaw;
        double roll;
        if (Math.Abs(r[2, 0]) < 1 - 1e-12)
        {
            yaw = Math.Atan2(r[1, 0], r[0, 0]);
            roll = Math.Atan2(r[2, 1], r[2, 2]);
        }
        else
        {
            // Straight up or straight down: yaw and roll turn about the same line, so the split
            // between them is arbitrary and all of it is given to yaw.
            yaw = Math.Atan2(-r[0, 1], r[1, 1]);
            roll = 0;
        }

        const double ToDegrees = 180.0 / Math.PI;
        return [yaw * ToDegrees, pitch * ToDegrees, roll * ToDegrees];
    }
}
