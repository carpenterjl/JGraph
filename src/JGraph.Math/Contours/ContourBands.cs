using JGraph.Core.Primitives;

namespace JGraph.Maths.Contours;

/// <summary>
/// Every filled contour band of a scalar field, clipped out in a single pass over the grid and held
/// grouped by band in flat buffers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MarchingSquares.FilledCells"/> answers for one band at a time, so drawing a
/// twenty-level <c>contourf</c> with it walks the whole grid twenty-one times and heap-allocates a
/// polygon array per emitted cell. Almost all of that work is wasted: a cell spans one band, or two
/// where a level crosses it, and every other sweep visits it only to find nothing.
/// </para>
/// <para>
/// This walks the grid once. A cell's four corners bound the values inside it, so the bands it can
/// possibly touch are the ones between the band holding its lowest corner and the band holding its
/// highest — a range found by two binary searches and, in practice, one or two bands wide. The
/// polygons land in one shared vertex buffer in cell order and are then indexed by band through a
/// counting sort, so a caller can draw a whole band as one contiguous run without the geometry ever
/// being copied twice.
/// </para>
/// </remarks>
public sealed class ContourBands
{
    private Point2D[] _vertices = new Point2D[1024];
    private int[] _polygonStarts = new int[257];
    private int[] _polygonBands = new int[256];
    private int[] _order = [];
    private int[] _bandOffsets = [];
    private int[] _cursor = [];
    private double[] _boundaries = [];
    private int _vertexCount;

    /// <summary>How many bands the last <see cref="Build"/> was asked for.</summary>
    public int BandCount { get; private set; }

    /// <summary>How many polygons the last <see cref="Build"/> produced, across every band.</summary>
    public int PolygonCount { get; private set; }

    /// <summary>The largest vertex count any single band accounts for — a caller's buffer size.</summary>
    public int MaxBandVertices { get; private set; }

    /// <summary>The largest polygon count any single band accounts for.</summary>
    public int MaxBandPolygons { get; private set; }

    /// <summary>
    /// Clips <paramref name="z"/> into the bands delimited by <paramref name="boundaries"/>, which
    /// must be ascending: band <c>b</c> is the region <c>boundaries[b] ≤ z ≤ boundaries[b + 1]</c>.
    /// Cells touching a non-finite sample are skipped, as they are everywhere else in this namespace.
    /// </summary>
    public void Build(double[] x, double[] y, double[,] z, ReadOnlySpan<double> boundaries)
    {
        MarchingSquares.Validate(x, y, z);

        _boundaries = boundaries.ToArray();
        BandCount = System.Math.Max(0, boundaries.Length - 1);
        PolygonCount = 0;
        MaxBandVertices = 0;
        MaxBandPolygons = 0;
        _vertexCount = 0;
        _polygonStarts[0] = 0;
        if (BandCount == 0)
        {
            return;
        }

        Span<(Point2D P, double V)> quad = stackalloc (Point2D, double)[16];
        Span<(Point2D P, double V)> above = stackalloc (Point2D, double)[16];
        Span<(Point2D P, double V)> band = stackalloc (Point2D, double)[16];

        for (int r = 0; r < y.Length - 1; r++)
        {
            for (int c = 0; c < x.Length - 1; c++)
            {
                double v00 = z[r, c];
                double v10 = z[r, c + 1];
                double v11 = z[r + 1, c + 1];
                double v01 = z[r + 1, c];
                if (!double.IsFinite(v00) || !double.IsFinite(v10) || !double.IsFinite(v11) || !double.IsFinite(v01))
                {
                    continue;
                }

                double lowest = System.Math.Min(System.Math.Min(v00, v10), System.Math.Min(v11, v01));
                double highest = System.Math.Max(System.Math.Max(v00, v10), System.Math.Max(v11, v01));
                int first = FirstBand(boundaries, lowest);
                int last = LastBand(boundaries, highest);

                for (int b = first; b <= last; b++)
                {
                    quad[0] = (new Point2D(x[c], y[r]), v00);
                    quad[1] = (new Point2D(x[c + 1], y[r]), v10);
                    quad[2] = (new Point2D(x[c + 1], y[r + 1]), v11);
                    quad[3] = (new Point2D(x[c], y[r + 1]), v01);

                    int count = MarchingSquares.Clip(quad, 4, above, boundaries[b], keepAbove: true);
                    if (count < 3)
                    {
                        continue;
                    }

                    count = MarchingSquares.Clip(above, count, band, boundaries[b + 1], keepAbove: false);
                    if (count < 3)
                    {
                        continue;
                    }

                    Append(band, count, b);
                }
            }
        }

        GroupByBand();
    }

    /// <summary>Whether this set was built for exactly <paramref name="boundaries"/>.</summary>
    public bool Matches(ReadOnlySpan<double> boundaries) => boundaries.SequenceEqual(_boundaries);

    /// <summary>How many polygons belong to <paramref name="band"/>.</summary>
    public int BandPolygonCount(int band) =>
        band < 0 || band >= BandCount ? 0 : _bandOffsets[band + 1] - _bandOffsets[band];

    /// <summary>The <paramref name="index"/>th polygon of <paramref name="band"/>, in data space.</summary>
    public ReadOnlySpan<Point2D> BandPolygon(int band, int index)
    {
        int polygon = _order[_bandOffsets[band] + index];
        int start = _polygonStarts[polygon];
        return _vertices.AsSpan(start, _polygonStarts[polygon + 1] - start);
    }

    private void Append(ReadOnlySpan<(Point2D P, double V)> polygon, int count, int band)
    {
        if (_vertexCount + count > _vertices.Length)
        {
            Array.Resize(ref _vertices, System.Math.Max(_vertexCount + count, _vertices.Length * 2));
        }

        if (PolygonCount >= _polygonBands.Length)
        {
            // One more start than polygons, always: the extra entry is the exclusive end of the
            // last one. Growing the two independently lets their lengths drift apart.
            int capacity = _polygonBands.Length * 2;
            Array.Resize(ref _polygonBands, capacity);
            Array.Resize(ref _polygonStarts, capacity + 1);
        }

        for (int i = 0; i < count; i++)
        {
            _vertices[_vertexCount++] = polygon[i].P;
        }

        _polygonBands[PolygonCount] = band;
        _polygonStarts[++PolygonCount] = _vertexCount;
    }

    /// <summary>
    /// Counting-sorts the polygon indices by band, so each band is a contiguous run of
    /// <see cref="_order"/>. Only the indices move; the vertices stay where the sweep put them.
    /// </summary>
    private void GroupByBand()
    {
        if (_bandOffsets.Length < BandCount + 1)
        {
            _bandOffsets = new int[BandCount + 1];
        }

        if (_order.Length < PolygonCount)
        {
            _order = new int[System.Math.Max(PolygonCount, 256)];
        }

        Array.Clear(_bandOffsets, 0, BandCount + 1);
        for (int i = 0; i < PolygonCount; i++)
        {
            _bandOffsets[_polygonBands[i] + 1]++;
        }

        for (int b = 0; b < BandCount; b++)
        {
            int polygons = _bandOffsets[b + 1];
            MaxBandPolygons = System.Math.Max(MaxBandPolygons, polygons);
            _bandOffsets[b + 1] = _bandOffsets[b] + polygons;
        }

        if (_cursor.Length < BandCount)
        {
            _cursor = new int[BandCount];
        }

        _bandOffsets.AsSpan(0, BandCount).CopyTo(_cursor);
        for (int i = 0; i < PolygonCount; i++)
        {
            _order[_cursor[_polygonBands[i]]++] = i;
        }

        for (int b = 0; b < BandCount; b++)
        {
            int vertices = 0;
            for (int i = _bandOffsets[b]; i < _bandOffsets[b + 1]; i++)
            {
                int polygon = _order[i];
                vertices += _polygonStarts[polygon + 1] - _polygonStarts[polygon];
            }

            MaxBandVertices = System.Math.Max(MaxBandVertices, vertices);
        }
    }

    /// <summary>
    /// The first band a cell whose lowest corner is <paramref name="value"/> can reach: the smallest
    /// <c>b</c> with <c>boundaries[b + 1] ≥ value</c>. The bound is inclusive rather than strict so
    /// that a corner landing exactly on a boundary is still clipped against the band below it, which
    /// is what a one-band-at-a-time clip does.
    /// </summary>
    private static int FirstBand(ReadOnlySpan<double> boundaries, double value)
    {
        int low = 0;
        int high = boundaries.Length - 2;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (boundaries[mid + 1] >= value)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// <summary>
    /// The last band a cell whose highest corner is <paramref name="value"/> can reach: the largest
    /// <c>b</c> with <c>boundaries[b] ≤ value</c>. Both searches clamp to the outermost band, so a
    /// value off the end of the scale still lands somewhere.
    /// </summary>
    private static int LastBand(ReadOnlySpan<double> boundaries, double value)
    {
        int low = 0;
        int high = boundaries.Length - 2;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (boundaries[mid] <= value)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }
}
