namespace JGraph.Imaging;

/// <summary>
/// A morphological structuring element: which offsets around a pixel take part, and — for a non-flat
/// element — how much each of them adds to or subtracts from the sample under it. This is what
/// MATLAB's <c>strel</c> and <c>offsetstrel</c> build.
/// </summary>
/// <remarks>
/// <para>
/// Sizes are kept as a flat member list over a shape, rather than a <c>bool[,]</c>, because MATLAB's
/// <c>'cube'</c>, <c>'cuboid'</c> and <c>'sphere'</c> are three-dimensional and a volume needs the same
/// element type a picture does. A two-dimensional element has a two-entry <see cref="Size"/>; the
/// indexers and <see cref="ToMatrix"/> speak to that case directly.
/// </para>
/// <para>
/// The origin sits at <c>(n - 1) / 2</c> along each dimension, which is MATLAB's
/// <c>floor((size + 1) / 2)</c> written from zero. The two agree for every odd size and differ for
/// even ones, where MATLAB puts the origin just before the middle.
/// </para>
/// </remarks>
public sealed class StructuringElement
{
    private readonly bool[] _members;
    private readonly double[]? _heights;
    private readonly int[] _size;

    private StructuringElement(int[] size, bool[] members, double[]? heights, string shape)
    {
        _size = size;
        _members = members;
        _heights = heights;
        Shape = shape;
    }

    /// <summary>The element's extent along each dimension: <c>[rows, cols]</c> or <c>[rows, cols, pages]</c>.</summary>
    public IReadOnlyList<int> Size => _size;

    /// <summary>The shape word this was built from — <c>'disk'</c>, <c>'line'</c>, <c>'arbitrary'</c>.</summary>
    public string Shape { get; }

    /// <summary>Whether every member contributes zero height, so erosion is a plain minimum.</summary>
    public bool IsFlat => _heights is null;

    /// <summary>Whether this element addresses a volume rather than a picture.</summary>
    public bool Is3D => _size.Length == 3;

    /// <summary>Row count.</summary>
    public int Rows => _size[0];

    /// <summary>Column count.</summary>
    public int Cols => _size[1];

    /// <summary>Page count — 1 for a two-dimensional element.</summary>
    public int Pages => _size.Length == 3 ? _size[2] : 1;

    /// <summary>The origin's row, relative to which offsets are measured.</summary>
    public int OriginRow => (Rows - 1) / 2;

    /// <summary>The origin's column.</summary>
    public int OriginCol => (Cols - 1) / 2;

    /// <summary>The origin's page.</summary>
    public int OriginPage => (Pages - 1) / 2;

    /// <summary>How many offsets take part.</summary>
    public int MemberCount
    {
        get
        {
            int count = 0;
            foreach (bool member in _members)
            {
                if (member)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Whether the offset at (row, column) of page <paramref name="page"/> takes part.</summary>
    public bool Member(int r, int c, int page = 0) => _members[Flat(r, c, page)];

    /// <summary>The height added at (row, column); zero for a flat element.</summary>
    public double HeightAt(int r, int c, int page = 0) => _heights?[Flat(r, c, page)] ?? 0.0;

    /// <summary>A square element of the given side, every offset a member (MATLAB <c>strel('square', n)</c>).</summary>
    public static StructuringElement Square(int side) => Rectangle(side, side, "square");

    /// <summary>A rectangular element (MATLAB <c>strel('rectangle', [m n])</c>).</summary>
    public static StructuringElement Rectangle(int rows, int cols) => Rectangle(rows, cols, "rectangle");

    /// <summary>A disk of the given radius: every offset within a Euclidean radius of the origin.</summary>
    /// <remarks>
    /// MATLAB approximates the disk by default with a decomposition into periodic lines, which changes
    /// the shape slightly; this is the exact disk, the shape MATLAB gives for <c>strel('disk', r, 0)</c>.
    /// </remarks>
    public static StructuringElement Disk(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        int side = (2 * radius) + 1;
        var members = new bool[side * side];
        for (int r = 0; r < side; r++)
        {
            for (int c = 0; c < side; c++)
            {
                double dy = r - radius;
                double dx = c - radius;
                members[(r * side) + c] = (dx * dx) + (dy * dy) <= (double)radius * radius;
            }
        }

        return new StructuringElement([side, side], members, null, "disk");
    }

    /// <summary>A diamond of the given radius: the offsets within a city-block distance of the origin.</summary>
    public static StructuringElement Diamond(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        int side = (2 * radius) + 1;
        var members = new bool[side * side];
        for (int r = 0; r < side; r++)
        {
            for (int c = 0; c < side; c++)
            {
                members[(r * side) + c] = Math.Abs(r - radius) + Math.Abs(c - radius) <= radius;
            }
        }

        return new StructuringElement([side, side], members, null, "diamond");
    }

    /// <summary>
    /// An octagon whose sides sit <paramref name="radius"/> from the origin along the axes; the radius
    /// must be a multiple of three, which is what makes the diagonal cuts land on whole pixels.
    /// </summary>
    public static StructuringElement Octagon(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (radius % 3 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "an octagon's radius must be a multiple of 3.");
        }

        int side = (2 * radius) + 1;
        // The diagonal cut sits at 4r/3: with r = 3k the octagon is the square of half-width 3k with
        // its corners taken off at |x| + |y| = 4k, which is the eight-sided figure MATLAB documents.
        int cut = 4 * radius / 3;
        var members = new bool[side * side];
        for (int r = 0; r < side; r++)
        {
            for (int c = 0; c < side; c++)
            {
                int dy = Math.Abs(r - radius);
                int dx = Math.Abs(c - radius);
                members[(r * side) + c] = dx + dy <= cut;
            }
        }

        return new StructuringElement([side, side], members, null, "octagon");
    }

    /// <summary>
    /// A line of the given length at the given angle in degrees, measured counter-clockwise from the
    /// horizontal (MATLAB <c>strel('line', len, deg)</c>). The line is symmetric about the origin.
    /// </summary>
    public static StructuringElement Line(double length, double degrees)
    {
        if (length < 1)
        {
            return Rectangle(1, 1, "line");
        }

        double theta = degrees * Math.PI / 180.0;
        double half = (length - 1) / 2.0;

        // Rows grow downward while the angle is measured upward, so the row step is negated: a 90°
        // line has to come out vertical, not upside down — which for a symmetric line is the same
        // element, but the sign matters the moment an odd angle rounds one way rather than the other.
        int x = (int)Math.Round(half * Math.Cos(theta), MidpointRounding.AwayFromZero);
        int y = -(int)Math.Round(half * Math.Sin(theta), MidpointRounding.AwayFromZero);

        (int R, int C)[] points = Bresenham(-y, -x, y, x);
        int spanR = 0;
        int spanC = 0;
        foreach ((int pr, int pc) in points)
        {
            spanR = Math.Max(spanR, Math.Abs(pr));
            spanC = Math.Max(spanC, Math.Abs(pc));
        }

        int rows = (2 * spanR) + 1;
        int cols = (2 * spanC) + 1;
        var members = new bool[rows * cols];
        foreach ((int pr, int pc) in points)
        {
            members[((pr + spanR) * cols) + pc + spanC] = true;
        }

        return new StructuringElement([rows, cols], members, null, "line");
    }

    /// <summary>A cube of the given side (MATLAB <c>strel('cube', n)</c>).</summary>
    public static StructuringElement Cube(int side) => Cuboid(side, side, side, "cube");

    /// <summary>A box element for volumes (MATLAB <c>strel('cuboid', [m n p])</c>).</summary>
    public static StructuringElement Cuboid(int rows, int cols, int pages) =>
        Cuboid(rows, cols, pages, "cuboid");

    /// <summary>A ball of the given radius for volumes (MATLAB <c>strel('sphere', r)</c>).</summary>
    public static StructuringElement Sphere(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        int side = (2 * radius) + 1;
        var members = new bool[side * side * side];
        for (int p = 0; p < side; p++)
        {
            for (int r = 0; r < side; r++)
            {
                for (int c = 0; c < side; c++)
                {
                    double dz = p - radius;
                    double dy = r - radius;
                    double dx = c - radius;
                    members[(((p * side) + r) * side) + c] =
                        (dx * dx) + (dy * dy) + (dz * dz) <= (double)radius * radius;
                }
            }
        }

        return new StructuringElement([side, side, side], members, null, "sphere");
    }

    /// <summary>
    /// A non-flat ball: a disk of radius <paramref name="radius"/> whose heights follow the upper half
    /// of an ellipsoid rising to <paramref name="height"/> at the origin (MATLAB
    /// <c>offsetstrel('ball', r, h)</c>).
    /// </summary>
    public static StructuringElement Ball(double radius, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        int r0 = (int)Math.Round(radius);
        int side = (2 * r0) + 1;
        var members = new bool[side * side];
        var heights = new double[side * side];
        for (int r = 0; r < side; r++)
        {
            for (int c = 0; c < side; c++)
            {
                double dy = r - r0;
                double dx = c - r0;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                int index = (r * side) + c;
                members[index] = distance <= radius;
                heights[index] = members[index]
                    ? height * Math.Sqrt(Math.Max(0.0, 1.0 - ((distance / radius) * (distance / radius))))
                    : 0.0;
            }
        }

        return new StructuringElement([side, side], members, heights, "ball");
    }

    /// <summary>
    /// A non-flat element read from an offset matrix, where <c>-Inf</c> (or NaN) marks an offset that
    /// takes no part and every other entry is that offset's height — MATLAB
    /// <c>offsetstrel('offset', h)</c>.
    /// </summary>
    public static StructuringElement Offset(double[,] offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        int rows = offsets.GetLength(0);
        int cols = offsets.GetLength(1);
        var members = new bool[rows * cols];
        var heights = new double[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double value = offsets[r, c];
                bool member = !double.IsNaN(value) && !double.IsNegativeInfinity(value);
                members[(r * cols) + c] = member;
                heights[(r * cols) + c] = member ? value : 0.0;
            }
        }

        return new StructuringElement([rows, cols], members, heights, "offset");
    }

    /// <summary>A flat element read from a 0/1 matrix (MATLAB <c>strel(nhood)</c>).</summary>
    public static StructuringElement Arbitrary(double[,] neighborhood)
    {
        ArgumentNullException.ThrowIfNull(neighborhood);
        int rows = neighborhood.GetLength(0);
        int cols = neighborhood.GetLength(1);
        var members = new bool[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                members[(r * cols) + c] = neighborhood[r, c] != 0;
            }
        }

        return new StructuringElement([rows, cols], members, null, "arbitrary");
    }

    /// <summary>A flat element read from a boolean mask.</summary>
    public static StructuringElement Arbitrary(bool[,] neighborhood)
    {
        ArgumentNullException.ThrowIfNull(neighborhood);
        int rows = neighborhood.GetLength(0);
        int cols = neighborhood.GetLength(1);
        var members = new bool[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                members[(r * cols) + c] = neighborhood[r, c];
            }
        }

        return new StructuringElement([rows, cols], members, null, "arbitrary");
    }

    /// <summary>
    /// The element turned through 180° about its origin. Dilation is defined against the reflected
    /// element, which is what makes it the exact dual of erosion for an asymmetric shape such as a
    /// line; for a symmetric one this is the identity.
    /// </summary>
    public StructuringElement Reflect()
    {
        var members = new bool[_members.Length];
        double[]? heights = _heights is null ? null : new double[_heights.Length];
        for (int p = 0; p < Pages; p++)
        {
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    int from = Flat(r, c, p);
                    int to = Flat(Rows - 1 - r, Cols - 1 - c, Pages - 1 - p);
                    members[to] = _members[from];
                    if (heights is not null)
                    {
                        heights[to] = _heights![from];
                    }
                }
            }
        }

        return new StructuringElement(_size, members, heights, Shape);
    }

    /// <summary>The two-dimensional membership as a 0/1 matrix, which is how a script sees it.</summary>
    public double[,] ToMatrix()
    {
        if (Is3D)
        {
            throw new InvalidOperationException("a three-dimensional structuring element is not a matrix.");
        }

        var values = new double[Rows, Cols];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                values[r, c] = _members[(r * Cols) + c] ? 1.0 : 0.0;
            }
        }

        return values;
    }

    /// <summary>The two-dimensional heights as a matrix; non-members read as <c>-Inf</c>, as MATLAB prints them.</summary>
    public double[,] ToOffsetMatrix()
    {
        if (Is3D)
        {
            throw new InvalidOperationException("a three-dimensional structuring element is not a matrix.");
        }

        var values = new double[Rows, Cols];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int index = (r * Cols) + c;
                values[r, c] = _members[index] ? _heights?[index] ?? 0.0 : double.NegativeInfinity;
            }
        }

        return values;
    }

    private static StructuringElement Rectangle(int rows, int cols, string shape)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        var members = new bool[rows * cols];
        Array.Fill(members, true);
        return new StructuringElement([rows, cols], members, null, shape);
    }

    private static StructuringElement Cuboid(int rows, int cols, int pages, string shape)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages);
        var members = new bool[rows * cols * pages];
        Array.Fill(members, true);
        return new StructuringElement([rows, cols, pages], members, null, shape);
    }

    /// <summary>The integer points on the segment from one end to the other, ends included.</summary>
    private static (int R, int C)[] Bresenham(int r0, int c0, int r1, int c1)
    {
        int dr = Math.Abs(r1 - r0);
        int dc = Math.Abs(c1 - c0);
        int stepR = r0 < r1 ? 1 : -1;
        int stepC = c0 < c1 ? 1 : -1;
        var points = new List<(int, int)>(Math.Max(dr, dc) + 1);
        int error = dc - dr;
        int r = r0;
        int c = c0;
        while (true)
        {
            points.Add((r, c));
            if (r == r1 && c == c1)
            {
                break;
            }

            int twice = 2 * error;
            if (twice > -dr)
            {
                error -= dr;
                c += stepC;
            }

            if (twice < dc)
            {
                error += dc;
                r += stepR;
            }
        }

        return [.. points];
    }

    private int Flat(int r, int c, int page) => (((page * Rows) + r) * Cols) + c;
}
