namespace JGraph.Imaging;

/// <summary>
/// The full <c>regionprops</c> measurement set for a labelled region, plus the whole-image measures
/// <c>bwarea</c>, <c>bweuler</c> and <c>bwferet</c>.
/// </summary>
/// <remarks>
/// Every measurement here comes from one of four things: the pixel list, the region's own mask, the
/// traced boundary, or the convex hull. Computing all four once per region and deriving the rest is
/// what keeps the property set from being thirty separate passes — and it is why asking for one
/// property costs about what asking for all of them does, which is the opposite of MATLAB's
/// arrangement but not something a script can observe.
/// </remarks>
public sealed class RegionMeasurement
{
    /// <summary>The region's label in the map it was measured from.</summary>
    public int Label { get; init; }

    /// <summary>Pixel count.</summary>
    public int Area { get; init; }

    /// <summary>Centre of mass, 0-based pixel coordinates.</summary>
    public double CentroidX { get; init; }

    /// <summary>Centre of mass, 0-based pixel coordinates.</summary>
    public double CentroidY { get; init; }

    /// <summary>Bounding box left edge, half a pixel before the first column.</summary>
    public double BoundingBoxX { get; init; }

    /// <summary>Bounding box top edge.</summary>
    public double BoundingBoxY { get; init; }

    /// <summary>Bounding box width in pixels.</summary>
    public double BoundingBoxWidth { get; init; }

    /// <summary>Bounding box height in pixels.</summary>
    public double BoundingBoxHeight { get; init; }

    /// <summary>Major axis of the ellipse with the region's second moments.</summary>
    public double MajorAxisLength { get; init; }

    /// <summary>Minor axis of that ellipse.</summary>
    public double MinorAxisLength { get; init; }

    /// <summary>The ellipse's eccentricity, 0 for a circle and 1 for a line segment.</summary>
    public double Eccentricity { get; init; }

    /// <summary>The major axis's angle in degrees, counter-clockwise from the x-axis.</summary>
    public double Orientation { get; init; }

    /// <summary>Boundary length, summed along the traced outline.</summary>
    public double Perimeter { get; init; }

    /// <summary>4π·Area / Perimeter², 1 for a perfect circle.</summary>
    public double Circularity { get; init; }

    /// <summary>The diameter of the circle with the same area.</summary>
    public double EquivDiameter { get; init; }

    /// <summary>Area divided by the bounding box's area.</summary>
    public double Extent { get; init; }

    /// <summary>Area divided by the convex hull's area.</summary>
    public double Solidity { get; init; }

    /// <summary>Pixel count of the filled convex hull.</summary>
    public int ConvexArea { get; init; }

    /// <summary>Pixel count with the region's holes filled in.</summary>
    public int FilledArea { get; init; }

    /// <summary>Objects minus holes — 1 for a solid blob, 0 for one with a single hole.</summary>
    public int EulerNumber { get; init; }

    /// <summary>The largest distance between two points of the hull.</summary>
    public double MaxFeretDiameter { get; init; }

    /// <summary>The angle in degrees at which that largest distance is measured.</summary>
    public double MaxFeretAngle { get; init; }

    /// <summary>The smallest width of the region over all directions.</summary>
    public double MinFeretDiameter { get; init; }

    /// <summary>The angle at which that smallest width is measured.</summary>
    public double MinFeretAngle { get; init; }

    /// <summary>Mean of the intensity image over the region; NaN without one.</summary>
    public double MeanIntensity { get; init; } = double.NaN;

    /// <summary>Smallest intensity in the region; NaN without an intensity image.</summary>
    public double MinIntensity { get; init; } = double.NaN;

    /// <summary>Largest intensity in the region; NaN without an intensity image.</summary>
    public double MaxIntensity { get; init; } = double.NaN;

    /// <summary>Intensity-weighted centre, 0-based; NaN without an intensity image.</summary>
    public double WeightedCentroidX { get; init; } = double.NaN;

    /// <summary>Intensity-weighted centre, 0-based; NaN without an intensity image.</summary>
    public double WeightedCentroidY { get; init; } = double.NaN;

    /// <summary>Every pixel in the region, 0-based (row, column).</summary>
    public (int Row, int Col)[] Pixels { get; init; } = [];

    /// <summary>The intensity at each of those pixels; empty without an intensity image.</summary>
    public double[] PixelValues { get; init; } = [];

    /// <summary>The convex hull, as [x y] vertices in 0-based pixel coordinates.</summary>
    public (double X, double Y)[] ConvexHull { get; init; } = [];

    /// <summary>The eight extremal points, in MATLAB's documented order.</summary>
    public (double X, double Y)[] Extrema { get; init; } = [];

    /// <summary>The region's own mask, cropped to its bounding box.</summary>
    public bool[,] Image { get; init; } = new bool[0, 0];

    /// <summary>The same mask with its holes filled.</summary>
    public bool[,] FilledImage { get; init; } = new bool[0, 0];

    /// <summary>The convex hull filled in, over the bounding box.</summary>
    public bool[,] ConvexImage { get; init; } = new bool[0, 0];
}

/// <summary>Measures labelled regions the way MATLAB's <c>regionprops</c> does.</summary>
public static class RegionProperties
{
    /// <summary>The properties <c>'basic'</c> selects.</summary>
    public static readonly string[] Basic = ["Area", "Centroid", "BoundingBox"];

    /// <summary>Every property this can measure, in MATLAB's spelling.</summary>
    public static readonly string[] All =
    [
        "Area", "BoundingBox", "Centroid", "Circularity", "ConvexArea", "ConvexHull", "ConvexImage",
        "Eccentricity", "EquivDiameter", "EulerNumber", "Extent", "Extrema", "FilledArea",
        "FilledImage", "Image", "MajorAxisLength", "MaxFeretAngle", "MaxFeretDiameter",
        "MinFeretAngle", "MinFeretDiameter", "MinorAxisLength", "Orientation", "Perimeter",
        "PixelIdxList", "PixelList", "Solidity", "SubarrayIdx",
    ];

    /// <summary>The properties that need an intensity image, in MATLAB's spelling.</summary>
    public static readonly string[] Intensity =
    [
        "MaxIntensity", "MeanIntensity", "MinIntensity", "PixelValues", "WeightedCentroid",
    ];

    /// <summary>
    /// Measures every labelled region. Regions are numbered 1…<paramref name="count"/>; a label with
    /// no pixels still produces an entry, which is what keeps <c>stats(k)</c> aligned with label
    /// <c>k</c> after a filter has emptied one.
    /// </summary>
    public static RegionMeasurement[] Measure(int[,] labels, int count, ImageBuffer? intensity = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
        if (intensity is not null && (intensity.Height != h || intensity.Width != w))
        {
            throw new ArgumentException(
                $"the intensity image is {intensity.Height}x{intensity.Width} but the labels are {h}x{w}.",
                nameof(intensity));
        }

        var pixels = new List<(int Row, int Col)>[count + 1];
        for (int i = 1; i <= count; i++)
        {
            pixels[i] = [];
        }

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int label = labels[r, c];
                if (label >= 1 && label <= count)
                {
                    pixels[label].Add((r, c));
                }
            }
        }

        var results = new RegionMeasurement[count];
        for (int label = 1; label <= count; label++)
        {
            results[label - 1] = MeasureOne(label, pixels[label], intensity);
        }

        GC.KeepAlive(intensity);
        return results;
    }

    /// <summary>
    /// The area of a binary image weighted the way MATLAB's <c>bwarea</c> weights it: each 2×2 pattern
    /// contributes a share chosen so that a straight diagonal edge measures its true length rather
    /// than the staircase's.
    /// </summary>
    public static double Area(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int h = image.Height;
        int w = image.Width;
        double total = 0;
        for (int r = -1; r < h; r++)
        {
            for (int c = -1; c < w; c++)
            {
                bool a = Sample(image, r, c);
                bool b = Sample(image, r, c + 1);
                bool d = Sample(image, r + 1, c);
                bool e = Sample(image, r + 1, c + 1);
                int set = (a ? 1 : 0) + (b ? 1 : 0) + (d ? 1 : 0) + (e ? 1 : 0);
                total += set switch
                {
                    0 => 0.0,
                    1 => 0.25,
                    3 => 0.875,
                    4 => 1.0,
                    // Two set: a diagonal pair is worth more than an adjacent one, because the pair
                    // stands for an edge cutting the square corner to corner.
                    _ => a == e && b == d ? 0.75 : 0.5,
                };
            }
        }

        GC.KeepAlive(image);
        return total;
    }

    /// <summary>
    /// The Euler number of a binary image (MATLAB <c>bweuler</c>): objects minus holes, counted from
    /// the 2×2 patterns rather than by labelling twice.
    /// </summary>
    public static int Euler(ImageBuffer image, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        MorphologicalReconstruction.CheckConnectivity(connectivity);
        int h = image.Height;
        int w = image.Width;
        var mask = new bool[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                mask[r, c] = image[r, c, 0] != 0;
            }
        }

        GC.KeepAlive(image);
        return Euler(mask, connectivity);
    }

    /// <summary>The Euler number of a mask.</summary>
    public static int Euler(bool[,] mask, int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(mask);
        int h = mask.GetLength(0);
        int w = mask.GetLength(1);

        bool At(int r, int c) => (uint)r < (uint)h && (uint)c < (uint)w && mask[r, c];

        // Every 2×2 window of the padded image falls into one of three classes, and the Euler number
        // is a fixed combination of how many windows are in each. The diagonal class is the one that
        // changes sign with the connectivity — a diagonal pair is one object under 8-connectivity and
        // two under 4.
        int ones = 0;
        int threes = 0;
        int diagonals = 0;
        for (int r = -1; r < h; r++)
        {
            for (int c = -1; c < w; c++)
            {
                bool a = At(r, c);
                bool b = At(r, c + 1);
                bool d = At(r + 1, c);
                bool e = At(r + 1, c + 1);
                int set = (a ? 1 : 0) + (b ? 1 : 0) + (d ? 1 : 0) + (e ? 1 : 0);
                if (set == 1)
                {
                    ones++;
                }
                else if (set == 3)
                {
                    threes++;
                }
                else if (set == 2 && a == e && b == d)
                {
                    diagonals++;
                }
            }
        }

        int weighted = connectivity == 8 ? -2 * diagonals : 2 * diagonals;
        return (ones - threes + weighted) / 4;
    }

    /// <summary>
    /// The Feret measurements of a hull: the largest distance between two of its points, and the
    /// smallest width over all directions (MATLAB <c>bwferet</c>).
    /// </summary>
    public static (double MaxDiameter, double MaxAngle, double MinDiameter, double MinAngle) Feret(
        (double X, double Y)[] hull)
    {
        ArgumentNullException.ThrowIfNull(hull);
        if (hull.Length == 0)
        {
            return (0, 0, 0, 0);
        }

        double maxDiameter = 0;
        double maxAngle = 0;
        for (int i = 0; i < hull.Length; i++)
        {
            for (int j = i + 1; j < hull.Length; j++)
            {
                double dx = hull[j].X - hull[i].X;
                double dy = hull[j].Y - hull[i].Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > maxDiameter)
                {
                    maxDiameter = distance;
                    maxAngle = Math.Atan2(-dy, dx) * 180.0 / Math.PI;
                }
            }
        }

        // The minimum width is attained with one hull edge flush against a supporting line, so only
        // the edge directions need testing — a rotating-calipers argument, without the calipers.
        double minDiameter = double.PositiveInfinity;
        double minAngle = 0;
        for (int i = 0; i < hull.Length; i++)
        {
            (double ax, double ay) = hull[i];
            (double bx, double by) = hull[(i + 1) % hull.Length];
            double dx = bx - ax;
            double dy = by - ay;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length == 0)
            {
                continue;
            }

            double widest = 0;
            foreach ((double px, double py) in hull)
            {
                widest = Math.Max(widest, Math.Abs(((py - ay) * dx) - ((px - ax) * dy)) / length);
            }

            if (widest < minDiameter)
            {
                minDiameter = widest;
                minAngle = Math.Atan2(-dx, -dy) * 180.0 / Math.PI;
            }
        }

        if (double.IsInfinity(minDiameter))
        {
            minDiameter = maxDiameter;
        }

        return (maxDiameter, maxAngle, minDiameter, NormalizeAngle(minAngle));
    }

    private static RegionMeasurement MeasureOne(
        int label, List<(int Row, int Col)> pixels, ImageBuffer? intensity)
    {
        int area = pixels.Count;
        if (area == 0)
        {
            return new RegionMeasurement { Label = label };
        }

        int minR = int.MaxValue;
        int maxR = int.MinValue;
        int minC = int.MaxValue;
        int maxC = int.MinValue;
        double sumX = 0;
        double sumY = 0;
        foreach ((int r, int c) in pixels)
        {
            minR = Math.Min(minR, r);
            maxR = Math.Max(maxR, r);
            minC = Math.Min(minC, c);
            maxC = Math.Max(maxC, c);
            sumX += c;
            sumY += r;
        }

        double centroidX = sumX / area;
        double centroidY = sumY / area;
        int boxH = maxR - minR + 1;
        int boxW = maxC - minC + 1;

        // The region's own mask, cropped to the box; everything that follows reads this rather than
        // the whole picture, so a small blob in a large image costs what the blob costs.
        var mask = new bool[boxH, boxW];
        foreach ((int r, int c) in pixels)
        {
            mask[r - minR, c - minC] = true;
        }

        (double major, double minor, double eccentricity, double orientation) =
            Ellipse(pixels, centroidX, centroidY, area);

        var corners = new List<(double X, double Y)>(area * 4);
        foreach ((int r, int c) in pixels)
        {
            Boundaries.AddCorners(corners, r, c);
        }

        (double X, double Y)[] hull = Boundaries.ConvexHull(corners);
        var convexImage = new bool[boxH, boxW];
        int convexArea = 0;
        for (int r = 0; r < boxH; r++)
        {
            for (int c = 0; c < boxW; c++)
            {
                if (Boundaries.InsidePolygon(hull, c + minC, r + minR))
                {
                    convexImage[r, c] = true;
                    convexArea++;
                }
            }
        }

        bool[,] filled = FillHoles(mask);
        int filledArea = 0;
        foreach (bool set in filled)
        {
            if (set)
            {
                filledArea++;
            }
        }

        double perimeter = Perimeter(mask);
        (double maxFeret, double maxAngle, double minFeret, double minAngle) = Feret(hull);

        double meanIntensity = double.NaN;
        double minIntensity = double.NaN;
        double maxIntensity = double.NaN;
        double weightedX = double.NaN;
        double weightedY = double.NaN;
        double[] values = [];
        if (intensity is not null)
        {
            values = new double[area];
            double total = 0;
            double wx = 0;
            double wy = 0;
            minIntensity = double.PositiveInfinity;
            maxIntensity = double.NegativeInfinity;
            for (int i = 0; i < area; i++)
            {
                (int r, int c) = pixels[i];
                double value = intensity[r, c, 0];
                values[i] = value;
                total += value;
                wx += value * c;
                wy += value * r;
                minIntensity = Math.Min(minIntensity, value);
                maxIntensity = Math.Max(maxIntensity, value);
            }

            meanIntensity = total / area;

            // A region whose samples are all zero has no weighted centre; NaN says so rather than
            // silently reporting the geometric one.
            if (total != 0)
            {
                weightedX = wx / total;
                weightedY = wy / total;
            }
        }

        return new RegionMeasurement
        {
            Label = label,
            Area = area,
            CentroidX = centroidX,
            CentroidY = centroidY,
            BoundingBoxX = minC - 0.5,
            BoundingBoxY = minR - 0.5,
            BoundingBoxWidth = boxW,
            BoundingBoxHeight = boxH,
            MajorAxisLength = major,
            MinorAxisLength = minor,
            Eccentricity = eccentricity,
            Orientation = orientation,
            Perimeter = perimeter,
            Circularity = perimeter > 0 ? 4 * Math.PI * area / (perimeter * perimeter) : double.PositiveInfinity,
            EquivDiameter = Math.Sqrt(4.0 * area / Math.PI),
            Extent = (double)area / (boxH * boxW),
            Solidity = convexArea > 0 ? (double)area / convexArea : 1.0,
            ConvexArea = convexArea,
            FilledArea = filledArea,
            EulerNumber = Euler(mask),
            MaxFeretDiameter = maxFeret,
            MaxFeretAngle = maxAngle,
            MinFeretDiameter = minFeret,
            MinFeretAngle = minAngle,
            MeanIntensity = meanIntensity,
            MinIntensity = minIntensity,
            MaxIntensity = maxIntensity,
            WeightedCentroidX = weightedX,
            WeightedCentroidY = weightedY,
            Pixels = [.. pixels],
            PixelValues = values,
            ConvexHull = hull,
            Extrema = Extrema(pixels),
            Image = mask,
            FilledImage = filled,
            ConvexImage = convexImage,
        };
    }

    /// <summary>
    /// The ellipse with the same second moments as the region. The <c>1/12</c> added to each variance
    /// is the variance of a unit square about its own centre: without it a one-pixel-wide line comes
    /// out with zero width, and the eccentricity of every thin region is exactly 1.
    /// </summary>
    private static (double Major, double Minor, double Eccentricity, double Orientation) Ellipse(
        List<(int Row, int Col)> pixels, double centroidX, double centroidY, int area)
    {
        double uxx = 0;
        double uyy = 0;
        double uxy = 0;

        // Rows grow downward and the orientation is quoted counter-clockwise, so y is negated here
        // and nowhere else — the centroid is quoted in the picture's own coordinates.
        double meanY = -centroidY;
        foreach ((int r, int c) in pixels)
        {
            double dx = c - centroidX;
            double dy = -r - meanY;
            uxx += dx * dx;
            uyy += dy * dy;
            uxy += dx * dy;
        }

        uxx = (uxx / area) + (1.0 / 12.0);
        uyy = (uyy / area) + (1.0 / 12.0);
        uxy /= area;

        double common = Math.Sqrt(((uxx - uyy) * (uxx - uyy)) + (4 * uxy * uxy));
        double major = 2 * Math.Sqrt(2) * Math.Sqrt(uxx + uyy + common);
        double minor = 2 * Math.Sqrt(2) * Math.Sqrt(Math.Max(0.0, uxx + uyy - common));
        double eccentricity = major > 0
            ? 2 * Math.Sqrt(Math.Max(0.0, ((major / 2) * (major / 2)) - ((minor / 2) * (minor / 2)))) / major
            : 0.0;

        double num;
        double den;
        if (uyy > uxx)
        {
            num = uyy - uxx + common;
            den = 2 * uxy;
        }
        else
        {
            num = 2 * uxy;
            den = uxx - uyy + common;
        }

        double orientation = num == 0 && den == 0 ? 0.0 : Math.Atan2(num, den) * 180.0 / Math.PI;
        return (major, minor, eccentricity, NormalizeAngle(orientation));
    }

    /// <summary>Boundary length, summed step by step along the traced outline.</summary>
    private static double Perimeter(bool[,] mask)
    {
        int h = mask.GetLength(0);
        int w = mask.GetLength(1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (!mask[r, c])
                {
                    continue;
                }

                (int Row, int Col)[] trace = Boundaries.Trace(mask, r, c, 8, r, c - 1);
                double total = 0;
                for (int i = 1; i < trace.Length; i++)
                {
                    int dr = trace[i].Row - trace[i - 1].Row;
                    int dc = trace[i].Col - trace[i - 1].Col;
                    total += dr != 0 && dc != 0 ? Math.Sqrt(2) : 1.0;
                }

                return total;
            }
        }

        return 0;
    }

    /// <summary>The eight extremal points, in the order MATLAB documents them.</summary>
    private static (double X, double Y)[] Extrema(List<(int Row, int Col)> pixels)
    {
        int minR = int.MaxValue;
        int maxR = int.MinValue;
        int minC = int.MaxValue;
        int maxC = int.MinValue;
        foreach ((int r, int c) in pixels)
        {
            minR = Math.Min(minR, r);
            maxR = Math.Max(maxR, r);
            minC = Math.Min(minC, c);
            maxC = Math.Max(maxC, c);
        }

        int topLeft = int.MaxValue;
        int topRight = int.MinValue;
        int bottomLeft = int.MaxValue;
        int bottomRight = int.MinValue;
        int leftTop = int.MaxValue;
        int leftBottom = int.MinValue;
        int rightTop = int.MaxValue;
        int rightBottom = int.MinValue;
        foreach ((int r, int c) in pixels)
        {
            if (r == minR)
            {
                topLeft = Math.Min(topLeft, c);
                topRight = Math.Max(topRight, c);
            }

            if (r == maxR)
            {
                bottomLeft = Math.Min(bottomLeft, c);
                bottomRight = Math.Max(bottomRight, c);
            }

            if (c == minC)
            {
                leftTop = Math.Min(leftTop, r);
                leftBottom = Math.Max(leftBottom, r);
            }

            if (c == maxC)
            {
                rightTop = Math.Min(rightTop, r);
                rightBottom = Math.Max(rightBottom, r);
            }
        }

        return
        [
            (topLeft - 0.5, minR - 0.5),
            (topRight + 0.5, minR - 0.5),
            (maxC + 0.5, rightTop - 0.5),
            (maxC + 0.5, rightBottom + 0.5),
            (bottomRight + 0.5, maxR + 0.5),
            (bottomLeft - 0.5, maxR + 0.5),
            (minC - 0.5, leftBottom + 0.5),
            (minC - 0.5, leftTop - 0.5),
        ];
    }

    private static bool[,] FillHoles(bool[,] mask)
    {
        int h = mask.GetLength(0);
        int w = mask.GetLength(1);
        var reachable = new bool[h, w];
        var queue = new Queue<(int R, int C)>();

        void Seed(int r, int c)
        {
            if ((uint)r < (uint)h && (uint)c < (uint)w && !mask[r, c] && !reachable[r, c])
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
            Seed(r - 1, c);
            Seed(r + 1, c);
            Seed(r, c - 1);
            Seed(r, c + 1);
        }

        var filled = new bool[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                filled[r, c] = mask[r, c] || !reachable[r, c];
            }
        }

        return filled;
    }

    private static double NormalizeAngle(double degrees)
    {
        // MATLAB quotes these angles in (−90, 90]: an axis has no head or tail, so a direction and
        // its opposite are the same answer and only one of them should ever be printed.
        while (degrees > 90)
        {
            degrees -= 180;
        }

        while (degrees <= -90)
        {
            degrees += 180;
        }

        return degrees;
    }

    private static bool Sample(ImageBuffer image, int r, int c) =>
        (uint)r < (uint)image.Height && (uint)c < (uint)image.Width && image[r, c, 0] != 0;
}
