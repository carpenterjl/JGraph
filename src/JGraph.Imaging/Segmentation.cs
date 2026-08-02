namespace JGraph.Imaging;

/// <summary>
/// Segmentation that does not need a contour: multilevel thresholding and quantizing, watershed
/// flooding, the two weight functions and the fast-marching front they feed, seeded region growing,
/// k-means over colour, and SLIC superpixels.
/// </summary>
public static class Segmentation
{
    /// <summary>
    /// Otsu's method carried to <paramref name="levels"/> thresholds (MATLAB <c>multithresh</c>):
    /// the split of the histogram that maximizes the variance between the classes it makes.
    /// </summary>
    /// <remarks>
    /// One threshold is a scan; two is a double loop; beyond that the search is exponential written
    /// naively, so this recurses over the cumulative sums instead, which reduces every candidate
    /// class to two lookups and makes five levels over 256 bins ordinary rather than hopeless.
    /// </remarks>
    public static double[] MultiThreshold(ImageBuffer image, int levels = 1, int bins = 256)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        if (levels > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(levels), levels,
                "multithresh is defined for up to 20 levels.");
        }

        double[] counts = Histograms.Histogram(image, bins);
        double total = 0;
        foreach (double count in counts)
        {
            total += count;
        }

        // Cumulative count and cumulative first moment: the mean of any bin range is one subtraction
        // apiece, which is what makes the search over class boundaries affordable.
        var weight = new double[bins + 1];
        var moment = new double[bins + 1];
        for (int i = 0; i < bins; i++)
        {
            weight[i + 1] = weight[i] + counts[i];
            moment[i + 1] = moment[i] + (counts[i] * i);
        }

        double Between(int from, int to)
        {
            double w = weight[to] - weight[from];
            if (w <= 0)
            {
                return 0;
            }

            double mean = (moment[to] - moment[from]) / w;
            return w * mean * mean;
        }

        var best = new int[levels];
        var current = new int[levels];
        double bestScore = double.NegativeInfinity;

        void Search(int depth, int from)
        {
            if (depth == levels)
            {
                double score = Between(from, bins);
                for (int i = 0; i < levels; i++)
                {
                    score += Between(i == 0 ? 0 : current[i - 1], current[i]);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    Array.Copy(current, best, levels);
                }

                return;
            }

            for (int cut = from + 1; cut <= bins - (levels - depth); cut++)
            {
                current[depth] = cut;
                Search(depth + 1, cut);
            }
        }

        Search(0, 0);

        var thresholds = new double[levels];
        for (int i = 0; i < levels; i++)
        {
            // The threshold sits at the boundary between bins, quoted on the [0, 1] sample scale.
            thresholds[i] = (best[i] - 0.5) / (bins - 1);
        }

        _ = total;
        return thresholds;
    }

    /// <summary>
    /// Assigns each sample to the interval its value falls in (MATLAB <c>imquantize</c>). Levels
    /// number from 1, as MATLAB's do, so a single threshold produces 1s and 2s.
    /// </summary>
    public static int[,] Quantize(ImageBuffer image, IReadOnlyList<double> thresholds)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(thresholds);
        int h = image.Height;
        int w = image.Width;
        var result = new int[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double value = image[r, c, 0];
                int level = 1;
                foreach (double threshold in thresholds)
                {
                    if (value >= threshold)
                    {
                        level++;
                    }
                }

                result[r, c] = level;
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Slices an intensity image into <paramref name="levels"/> equal bands (MATLAB
    /// <c>grayslice</c>), numbering the bands from 0 as MATLAB's indexed images do.
    /// </summary>
    public static int[,] Slice(ImageBuffer image, int levels = 10)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        int h = image.Height;
        int w = image.Width;
        var result = new int[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                result[r, c] = Math.Clamp((int)(image[r, c, 0] * levels), 0, levels - 1);
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Watershed segmentation by Meyer's flooding (MATLAB <c>watershed</c>): every regional minimum
    /// becomes a catchment basin, and the picture is flooded from all of them at once, lowest level
    /// first. Where two floods meet, a ridge is left — a pixel labelled 0.
    /// </summary>
    /// <remarks>
    /// The priority queue is what makes this a flood rather than a race: pixels are always taken in
    /// order of the level at which the water would reach them, so a basin cannot run ahead of a
    /// deeper one and claim territory that belongs to it.
    /// </remarks>
    public static int[,] Watershed(ImageBuffer image, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        MorphologicalReconstruction.CheckConnectivity(connectivity);
        int h = image.Height;
        int w = image.Width;

        using ImageBuffer minima = MorphologicalReconstruction.RegionalMin(image, connectivity);
        (int[,] seedLabels, int basins) = Regions.Label(minima, connectivity);

        var labels = new int[h, w];
        var queue = new PriorityQueue<(int R, int C), (double Level, long Order)>();
        long order = 0;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                labels[r, c] = seedLabels[r, c];
                if (seedLabels[r, c] == 0)
                {
                    continue;
                }

                foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, connectivity))
                {
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w && seedLabels[nr, nc] == 0)
                    {
                        queue.Enqueue((r, c), (image[r, c, 0], order++));
                        break;
                    }
                }
            }
        }

        const int ridge = -1;
        while (queue.TryDequeue(out (int R, int C) here, out (double Level, long Order) _))
        {
            int mine = labels[here.R, here.C];
            if (mine <= 0)
            {
                continue;
            }

            foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(here.R, here.C, connectivity))
            {
                if ((uint)nr >= (uint)h || (uint)nc >= (uint)w)
                {
                    continue;
                }

                int theirs = labels[nr, nc];
                if (theirs == 0)
                {
                    labels[nr, nc] = mine;
                    queue.Enqueue((nr, nc), (image[nr, nc, 0], order++));
                }
                else if (theirs > 0 && theirs != mine)
                {
                    // Two floods have met. Neither pixel changes hands; the meeting itself is the
                    // ridge, and it is drawn on whichever side arrived second.
                    labels[here.R, here.C] = ridge;
                    break;
                }
            }
        }

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (labels[r, c] < 0)
                {
                    labels[r, c] = 0;
                }
            }
        }

        GC.KeepAlive(image);
        _ = basins;
        return labels;
    }

    /// <summary>
    /// Grows a region from seeds while the intensity stays within <paramref name="tolerance"/> of the
    /// seed value (MATLAB <c>grayconnected</c>).
    /// </summary>
    public static ImageBuffer GrayConnected(
        ImageBuffer image, IReadOnlyList<(int Row, int Col)> seeds, double tolerance, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(seeds);
        MorphologicalReconstruction.CheckConnectivity(connectivity);
        int h = image.Height;
        int w = image.Width;
        var result = new ImageBuffer(h, w, 1);
        var visited = new bool[h, w];
        var queue = new Queue<(int R, int C)>();

        foreach ((int row, int col) in seeds)
        {
            if ((uint)row >= (uint)h || (uint)col >= (uint)w)
            {
                throw new ArgumentOutOfRangeException(nameof(seeds),
                    $"seed ({row}, {col}) is outside the {h}x{w} image.");
            }

            double level = image[row, col, 0];
            double low = level - tolerance;
            double high = level + tolerance;
            if (visited[row, col])
            {
                continue;
            }

            visited[row, col] = true;
            result[row, col, 0] = 1.0;
            queue.Enqueue((row, col));
            while (queue.Count > 0)
            {
                (int r, int c) = queue.Dequeue();
                foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, connectivity))
                {
                    if ((uint)nr >= (uint)h || (uint)nc >= (uint)w || visited[nr, nc])
                    {
                        continue;
                    }

                    double value = image[nr, nc, 0];
                    if (value < low || value > high)
                    {
                        continue;
                    }

                    visited[nr, nc] = true;
                    result[nr, nc, 0] = 1.0;
                    queue.Enqueue((nr, nc));
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// A weight image that is small where the gradient is large (MATLAB <c>gradientweight</c>) — the
    /// cost surface a front should find expensive to cross at an edge.
    /// </summary>
    public static ImageBuffer GradientWeight(ImageBuffer image, double sigma = 1.5, double rolloff = 3.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ImageBuffer smoothed = Filters.GaussianBlur(image, sigma, sigma);
        (ImageBuffer magnitude, ImageBuffer direction) = Gradients.Gradient(smoothed, Gradients.Operator.Sobel);
        direction.Dispose();
        using (magnitude)
        {
            double largest = 0;
            foreach (double value in magnitude.Pixels)
            {
                largest = Math.Max(largest, value);
            }

            GC.KeepAlive(magnitude);
            var weight = new ImageBuffer(image.Height, image.Width, 1);
            Span<double> output = weight.Pixels;
            ReadOnlySpan<double> gradient = magnitude.Pixels;
            for (int i = 0; i < output.Length; i++)
            {
                double normalized = largest > 0 ? gradient[i] / largest : 0.0;
                output[i] = 1.0 / (1.0 + Math.Pow(normalized, rolloff) * 1000.0);
            }

            GC.KeepAlive(magnitude);
            return weight;
        }
    }

    /// <summary>
    /// A weight image that is small where the intensity is far from the seeds' own (MATLAB
    /// <c>graydiffweight</c>).
    /// </summary>
    public static ImageBuffer GrayDifferenceWeight(
        ImageBuffer image, IReadOnlyList<(int Row, int Col)> seeds, double rolloff = 500.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(seeds);
        if (seeds.Count == 0)
        {
            throw new ArgumentException("graydiffweight needs at least one seed.", nameof(seeds));
        }

        double reference = 0;
        foreach ((int row, int col) in seeds)
        {
            if ((uint)row >= (uint)image.Height || (uint)col >= (uint)image.Width)
            {
                throw new ArgumentOutOfRangeException(nameof(seeds),
                    $"seed ({row}, {col}) is outside the {image.Height}x{image.Width} image.");
            }

            reference += image[row, col, 0];
        }

        reference /= seeds.Count;

        double largest = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                largest = Math.Max(largest, Math.Abs(image[r, c, 0] - reference));
            }
        }

        var weight = new ImageBuffer(image.Height, image.Width, 1);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                double difference = largest > 0 ? Math.Abs(image[r, c, 0] - reference) / largest : 0.0;
                weight[r, c, 0] = Math.Exp(-rolloff * difference * difference / 100.0);
            }
        }

        GC.KeepAlive(image);
        return weight;
    }

    /// <summary>
    /// Fast marching from seeds over a weight image (MATLAB <c>imsegfmm</c>): returns the mask of
    /// everything the front reached before the threshold, and the arrival-time map itself.
    /// </summary>
    /// <remarks>
    /// This is Dijkstra over the weight's reciprocal rather than a true Eikonal solver: the front
    /// travels along the grid rather than across it, which costs a little accuracy in the diagonal
    /// direction and buys the shortest-path guarantee outright. The threshold is on the normalized
    /// arrival time, as MATLAB's is.
    /// </remarks>
    public static (ImageBuffer Mask, ImageBuffer Time) FastMarch(
        ImageBuffer weight, IReadOnlyList<(int Row, int Col)> seeds, double threshold, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentNullException.ThrowIfNull(seeds);
        MorphologicalReconstruction.CheckConnectivity(connectivity);
        int h = weight.Height;
        int w = weight.Width;

        var time = new double[h * w];
        Array.Fill(time, double.PositiveInfinity);
        var settled = new bool[h * w];
        var queue = new PriorityQueue<int, double>();
        foreach ((int row, int col) in seeds)
        {
            if ((uint)row >= (uint)h || (uint)col >= (uint)w)
            {
                throw new ArgumentOutOfRangeException(nameof(seeds),
                    $"seed ({row}, {col}) is outside the {h}x{w} image.");
            }

            int p = (row * w) + col;
            time[p] = 0;
            queue.Enqueue(p, 0);
        }

        while (queue.TryDequeue(out int p, out double cost))
        {
            if (settled[p])
            {
                continue;
            }

            settled[p] = true;
            int r = p / w;
            int c = p % w;
            foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, connectivity))
            {
                if ((uint)nr >= (uint)h || (uint)nc >= (uint)w)
                {
                    continue;
                }

                int q = (nr * w) + nc;
                if (settled[q])
                {
                    continue;
                }

                double speed = Math.Max(weight[nr, nc, 0], 1e-9);
                double step = nr != r && nc != c ? Math.Sqrt(2) : 1.0;
                double candidate = cost + (step / speed);
                if (candidate < time[q])
                {
                    time[q] = candidate;
                    queue.Enqueue(q, candidate);
                }
            }
        }

        double finite = 0;
        foreach (double value in time)
        {
            if (!double.IsInfinity(value))
            {
                finite = Math.Max(finite, value);
            }
        }

        var mask = new ImageBuffer(h, w, 1);
        var arrival = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double value = time[(r * w) + c];
                double normalized = double.IsInfinity(value) ? 1.0 : (finite > 0 ? value / finite : 0.0);
                arrival[r, c, 0] = normalized;
                mask[r, c, 0] = normalized <= threshold ? 1.0 : 0.0;
            }
        }

        GC.KeepAlive(weight);
        return (mask, arrival);
    }

    /// <summary>
    /// k-means over pixel colour (MATLAB <c>imsegkmeans</c>), with k-means++ seeding so the result
    /// does not depend on which corner of the picture happened to be sampled first.
    /// </summary>
    /// <param name="image">The picture to cluster.</param>
    /// <param name="clusters">How many clusters.</param>
    /// <param name="random">The generator the seeding draws from.</param>
    /// <param name="iterations">Maximum Lloyd iterations.</param>
    public static (int[,] Labels, double[][] Centers) KMeans(
        ImageBuffer image, int clusters, Random random, int iterations = 100)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clusters);
        int h = image.Height;
        int w = image.Width;
        int channels = image.Channels;
        int n = h * w;

        double[][] centers = SeedCenters(image, clusters, random);
        var labels = new int[h, w];
        var sums = new double[clusters][];
        for (int k = 0; k < clusters; k++)
        {
            sums[k] = new double[channels];
        }

        var counts = new int[clusters];
        for (int pass = 0; pass < iterations; pass++)
        {
            bool changed = false;
            for (int k = 0; k < clusters; k++)
            {
                Array.Clear(sums[k]);
                counts[k] = 0;
            }

            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int best = 0;
                    double bestDistance = double.PositiveInfinity;
                    for (int k = 0; k < clusters; k++)
                    {
                        double distance = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            double d = image[r, c, ch] - centers[k][ch];
                            distance += d * d;
                        }

                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = k;
                        }
                    }

                    if (labels[r, c] != best + 1)
                    {
                        labels[r, c] = best + 1;
                        changed = true;
                    }

                    counts[best]++;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        sums[best][ch] += image[r, c, ch];
                    }
                }
            }

            for (int k = 0; k < clusters; k++)
            {
                if (counts[k] == 0)
                {
                    continue;
                }

                for (int ch = 0; ch < channels; ch++)
                {
                    centers[k][ch] = sums[k][ch] / counts[k];
                }
            }

            if (!changed)
            {
                break;
            }
        }

        GC.KeepAlive(image);
        _ = n;
        return (labels, centers);
    }

    /// <summary>
    /// SLIC superpixels (MATLAB <c>superpixels</c>): k-means in a five-dimensional space of colour
    /// and position, searched only within twice the expected superpixel spacing.
    /// </summary>
    /// <remarks>
    /// Restricting the search is the whole idea. Ordinary k-means over position would let a cluster
    /// centre migrate anywhere, and the result would be colour segmentation rather than a tiling;
    /// looking only 2S away keeps each superpixel local and turns the cost from clusters-per-pixel
    /// into a constant.
    /// </remarks>
    /// <param name="image">The picture.</param>
    /// <param name="wanted">Roughly how many superpixels.</param>
    /// <param name="compactness">How much position counts against colour; larger is more square.</param>
    /// <param name="zeroParameter">Whether to adapt the compactness per cluster (MATLAB's SLIC0).</param>
    /// <param name="iterations">How many refinement passes.</param>
    public static (int[,] Labels, int Count) Superpixels(
        ImageBuffer image, int wanted, double compactness = 10.0,
        bool zeroParameter = true, int iterations = 10)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wanted);
        int h = image.Height;
        int w = image.Width;
        int channels = image.Channels;

        double spacing = Math.Sqrt((double)h * w / wanted);
        int step = Math.Max(1, (int)Math.Round(spacing));

        var centers = new List<(double R, double C, double[] Color)>();
        for (int r = step / 2; r < h; r += step)
        {
            for (int c = step / 2; c < w; c += step)
            {
                var color = new double[channels];
                for (int ch = 0; ch < channels; ch++)
                {
                    color[ch] = image[r, c, ch];
                }

                centers.Add((r, c, color));
            }
        }

        if (centers.Count == 0)
        {
            centers.Add((h / 2.0, w / 2.0, new double[channels]));
        }

        var labels = new int[h, w];
        var distance = new double[h, w];
        var largestColor = new double[centers.Count];
        Array.Fill(largestColor, 1.0);

        for (int pass = 0; pass < iterations; pass++)
        {
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    labels[r, c] = 0;
                    distance[r, c] = double.PositiveInfinity;
                }
            }

            var passColor = new double[centers.Count];
            for (int k = 0; k < centers.Count; k++)
            {
                (double cr, double cc, double[] color) = centers[k];
                int fromR = Math.Max(0, (int)(cr - (2 * step)));
                int toR = Math.Min(h - 1, (int)(cr + (2 * step)));
                int fromC = Math.Max(0, (int)(cc - (2 * step)));
                int toC = Math.Min(w - 1, (int)(cc + (2 * step)));

                double weight = zeroParameter
                    ? compactness * compactness / Math.Max(largestColor[k], 1e-6)
                    : compactness * compactness;

                for (int r = fromR; r <= toR; r++)
                {
                    for (int c = fromC; c <= toC; c++)
                    {
                        double colorDistance = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            double d = image[r, c, ch] - color[ch];
                            colorDistance += d * d;
                        }

                        double spatial = (((r - cr) * (r - cr)) + ((c - cc) * (c - cc))) / (step * (double)step);
                        double total = colorDistance + (weight * spatial);
                        if (total < distance[r, c])
                        {
                            distance[r, c] = total;
                            labels[r, c] = k + 1;
                            passColor[k] = Math.Max(passColor[k], colorDistance);
                        }
                    }
                }
            }

            largestColor = passColor;

            var sumR = new double[centers.Count];
            var sumC = new double[centers.Count];
            var sumColor = new double[centers.Count][];
            var counts = new int[centers.Count];
            for (int k = 0; k < centers.Count; k++)
            {
                sumColor[k] = new double[channels];
            }

            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int k = labels[r, c] - 1;
                    if (k < 0)
                    {
                        continue;
                    }

                    counts[k]++;
                    sumR[k] += r;
                    sumC[k] += c;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        sumColor[k][ch] += image[r, c, ch];
                    }
                }
            }

            for (int k = 0; k < centers.Count; k++)
            {
                if (counts[k] == 0)
                {
                    continue;
                }

                var color = new double[channels];
                for (int ch = 0; ch < channels; ch++)
                {
                    color[ch] = sumColor[k][ch] / counts[k];
                }

                centers[k] = (sumR[k] / counts[k], sumC[k] / counts[k], color);
            }
        }

        GC.KeepAlive(image);
        return Renumber(labels, centers.Count);
    }

    /// <summary>Drops empty labels and renumbers the rest densely from 1.</summary>
    private static (int[,] Labels, int Count) Renumber(int[,] labels, int upper)
    {
        var remap = new int[upper + 1];
        int next = 0;
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int label = labels[r, c];
                if (label == 0)
                {
                    continue;
                }

                if (remap[label] == 0)
                {
                    remap[label] = ++next;
                }

                labels[r, c] = remap[label];
            }
        }

        return (labels, next);
    }

    /// <summary>k-means++ seeding: each new centre is drawn against the squared distance to the nearest chosen one.</summary>
    private static double[][] SeedCenters(ImageBuffer image, int clusters, Random random)
    {
        int h = image.Height;
        int w = image.Width;
        int channels = image.Channels;
        var centers = new double[clusters][];

        int firstR = random.Next(h);
        int firstC = random.Next(w);
        centers[0] = new double[channels];
        for (int ch = 0; ch < channels; ch++)
        {
            centers[0][ch] = image[firstR, firstC, ch];
        }

        var nearest = new double[h * w];
        Array.Fill(nearest, double.PositiveInfinity);
        for (int k = 1; k < clusters; k++)
        {
            double total = 0;
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    double distance = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        double d = image[r, c, ch] - centers[k - 1][ch];
                        distance += d * d;
                    }

                    int p = (r * w) + c;
                    nearest[p] = Math.Min(nearest[p], distance);
                    total += nearest[p];
                }
            }

            double target = random.NextDouble() * total;
            double running = 0;
            int pick = 0;
            for (int p = 0; p < nearest.Length; p++)
            {
                running += nearest[p];
                if (running >= target)
                {
                    pick = p;
                    break;
                }
            }

            centers[k] = new double[channels];
            for (int ch = 0; ch < channels; ch++)
            {
                centers[k][ch] = image[pick / w, pick % w, ch];
            }
        }

        GC.KeepAlive(image);
        return centers;
    }
}
