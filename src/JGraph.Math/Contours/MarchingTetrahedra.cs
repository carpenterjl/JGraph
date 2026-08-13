namespace JGraph.Maths.Contours;

/// <summary>A triangulated surface: a list of vertices and the triangles that index into it.</summary>
public sealed class IsoMesh
{
    /// <summary>Builds a mesh from its vertices and the triangles indexing into them.</summary>
    public IsoMesh(double[] x, double[] y, double[] z, int[][] faces)
    {
        X = x;
        Y = y;
        Z = z;
        Faces = faces;
    }

    /// <summary>The x coordinate of each vertex.</summary>
    public double[] X { get; }

    /// <summary>The y coordinate of each vertex.</summary>
    public double[] Y { get; }

    /// <summary>The z coordinate of each vertex.</summary>
    public double[] Z { get; }

    /// <summary>Each triangle, as three zero-based indices into the vertex arrays.</summary>
    public int[][] Faces { get; }

    /// <summary>How many vertices the surface has.</summary>
    public int VertexCount => X.Length;
}

/// <summary>
/// The surface where a field sampled on a grid takes a given value — what marching squares does for a
/// plane, done for a box.
/// </summary>
/// <remarks>
/// <para>
/// Each cell of the grid is cut into six tetrahedra along a fixed main diagonal, and each tetrahedron
/// is crossed by the surface in one of only three ways: not at all, in a triangle cutting off one
/// corner, or in a quadrilateral separating two corners from the other two. That is the whole
/// algorithm, and it is why this is tetrahedra rather than the more usual cubes: a cube has fourteen
/// distinguishable cases and several of them are genuinely ambiguous — two opposite corners inside can
/// mean one surface or two, and a table has to pick. A tetrahedron has no such case, so there is
/// nothing to pick and no table to get wrong. The cost is more triangles for the same surface, and the
/// fixed diagonal is what keeps neighbouring cells agreeing about where their shared face is cut, so
/// the result has no cracks in it.
/// </para>
/// <para>
/// Vertices are shared: a crossing is named by the pair of grid corners whose edge it sits on, so two
/// tetrahedra that meet along that edge use the same vertex rather than each making its own. Triangle
/// winding is not made consistent, because nothing in this pipeline lights a face from one side only.
/// A cell with a reading that is not finite at any of its corners is left out entirely.
/// </para>
/// </remarks>
public static class MarchingTetrahedra
{
    /// <summary>
    /// The six tetrahedra a cell is cut into, each named by four of the cell's eight corners. Corner
    /// <c>n</c> is at offset <c>(n &amp; 1, (n &gt;&gt; 1) &amp; 1, (n &gt;&gt; 2) &amp; 1)</c> in
    /// (x, y, z), so every tetrahedron here holds corner 0 and corner 7 — the ends of the diagonal
    /// they all share.
    /// </summary>
    private static readonly int[][] Tetrahedra =
    [
        [0, 1, 3, 7], [0, 1, 5, 7], [0, 2, 3, 7],
        [0, 2, 6, 7], [0, 4, 5, 7], [0, 4, 6, 7],
    ];

    /// <summary>
    /// The surface where <paramref name="values"/> equals <paramref name="level"/>.
    /// </summary>
    /// <param name="x">The x positions of the grid columns.</param>
    /// <param name="y">The y positions of the grid rows.</param>
    /// <param name="z">The z positions of the grid pages.</param>
    /// <param name="values">
    /// The field, indexed <c>[row, column, page]</c> — rows index y, the same way a matrix handed to
    /// <c>surf</c> or <c>contour</c> does, so a field built with <c>meshgrid</c> needs no rearranging.
    /// </param>
    /// <param name="level">The value the surface is drawn at.</param>
    public static IsoMesh Surface(double[] x, double[] y, double[] z, double[,,] values, double level)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(values);

        int rows = values.GetLength(0);
        int columns = values.GetLength(1);
        int pages = values.GetLength(2);
        if (rows != y.Length || columns != x.Length || pages != z.Length)
        {
            throw new ArgumentException(
                "The field has to be as many rows as y, as many columns as x, and as many pages as z.",
                nameof(values));
        }

        var vertexOf = new Dictionary<long, int>();
        var vx = new List<double>();
        var vy = new List<double>();
        var vz = new List<double>();
        var faces = new List<int[]>();

        var corner = new int[8];
        var value = new double[8];

        for (int r = 0; r + 1 < rows; r++)
        {
            for (int c = 0; c + 1 < columns; c++)
            {
                for (int p = 0; p + 1 < pages; p++)
                {
                    bool whole = true;
                    for (int n = 0; n < 8; n++)
                    {
                        int cc = c + (n & 1);
                        int rr = r + ((n >> 1) & 1);
                        int pp = p + ((n >> 2) & 1);
                        corner[n] = ((rr * columns) + cc) * pages + pp;
                        value[n] = values[rr, cc, pp];
                        whole &= double.IsFinite(value[n]);
                    }

                    if (!whole)
                    {
                        continue;
                    }

                    foreach (int[] tetrahedron in Tetrahedra)
                    {
                        Cut(tetrahedron, corner, value, level, x, y, z, columns, pages,
                            vertexOf, vx, vy, vz, faces);
                    }
                }
            }
        }

        return new IsoMesh([.. vx], [.. vy], [.. vz], [.. faces]);
    }

    private static void Cut(
        int[] tetrahedron,
        int[] corner,
        double[] value,
        double level,
        double[] x,
        double[] y,
        double[] z,
        int columns,
        int pages,
        Dictionary<long, int> vertexOf,
        List<double> vx,
        List<double> vy,
        List<double> vz,
        List<int[]> faces)
    {
        Span<int> inside = stackalloc int[4];
        Span<int> outside = stackalloc int[4];
        int above = 0, below = 0;
        foreach (int n in tetrahedron)
        {
            if (value[n] >= level)
            {
                inside[above++] = n;
            }
            else
            {
                outside[below++] = n;
            }
        }

        if (above is 0 or 4)
        {
            return;
        }

        int Crossing(int a, int b) => VertexBetween(
            corner[a], corner[b], value[a], value[b], level,
            x, y, z, columns, pages, vertexOf, vx, vy, vz);

        switch (above)
        {
            case 1:
                faces.Add([
                    Crossing(inside[0], outside[0]),
                    Crossing(inside[0], outside[1]),
                    Crossing(inside[0], outside[2])]);
                break;

            case 3:
                faces.Add([
                    Crossing(outside[0], inside[0]),
                    Crossing(outside[0], inside[1]),
                    Crossing(outside[0], inside[2])]);
                break;

            default:
                // Two against two: the surface crosses four of the six edges, and the quadrilateral
                // they make is split into two triangles. The order below walks the quad's rim rather
                // than criss-crossing it.
                int a = Crossing(inside[0], outside[0]);
                int b = Crossing(inside[0], outside[1]);
                int c = Crossing(inside[1], outside[1]);
                int d = Crossing(inside[1], outside[0]);
                faces.Add([a, b, c]);
                faces.Add([a, c, d]);
                break;
        }
    }

    /// <summary>
    /// The point on the edge between two grid corners where the field reaches the level, made once and
    /// then shared by every tetrahedron that meets along that edge.
    /// </summary>
    private static int VertexBetween(
        int cornerA,
        int cornerB,
        double valueA,
        double valueB,
        double level,
        double[] x,
        double[] y,
        double[] z,
        int columns,
        int pages,
        Dictionary<long, int> vertexOf,
        List<double> vx,
        List<double> vy,
        List<double> vz)
    {
        long key = cornerA < cornerB
            ? ((long)cornerA << 32) | (uint)cornerB
            : ((long)cornerB << 32) | (uint)cornerA;
        if (vertexOf.TryGetValue(key, out int existing))
        {
            return existing;
        }

        double gap = valueB - valueA;
        double t = gap == 0 ? 0.5 : (level - valueA) / gap;
        t = System.Math.Clamp(t, 0, 1);

        (int ra, int ca, int pa) = Unpack(cornerA, columns, pages);
        (int rb, int cb, int pb) = Unpack(cornerB, columns, pages);

        vx.Add(x[ca] + ((x[cb] - x[ca]) * t));
        vy.Add(y[ra] + ((y[rb] - y[ra]) * t));
        vz.Add(z[pa] + ((z[pb] - z[pa]) * t));

        int index = vx.Count - 1;
        vertexOf[key] = index;
        return index;
    }

    private static (int Row, int Column, int Page) Unpack(int packed, int columns, int pages)
    {
        int page = packed % pages;
        int rest = packed / pages;
        return (rest / columns, rest % columns, page);
    }
}
