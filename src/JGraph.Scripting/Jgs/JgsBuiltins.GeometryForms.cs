using JGraph.Maths.Geometry;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The documented forms of <c>convhull</c> and <c>delaunay</c> that M76 added: points given as one
/// matrix rather than as separate coordinates, the same two questions asked in space, the area or
/// volume as a second output, and <c>convhull</c>'s <c>'Simplify'</c> pair.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The coordinates a geometry verb was given, however they were spelled: as one n-by-2 or
    /// n-by-3 matrix of points, or as one array per direction.
    /// </summary>
    private static (double[] X, double[] Y, double[]? Z) CoordinatesOf(
        string name, IReadOnlyList<JgsValue> args, int count, int line, int col)
    {
        if (count == 1)
        {
            double[,] points = RectOf(name, args[0], line, col);
            int width = points.GetLength(1);
            if (width is not (2 or 3))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a single argument is a matrix of points, two or three columns wide, " +
                    $"but this one is {width}.");
            }

            int n = points.GetLength(0);
            var x = new double[n];
            var y = new double[n];
            double[]? z = width == 3 ? new double[n] : null;
            for (int i = 0; i < n; i++)
            {
                x[i] = points[i, 0];
                y[i] = points[i, 1];
                if (z is not null)
                {
                    z[i] = points[i, 2];
                }
            }

            return (x, y, z);
        }

        double[] xs = ToDoubles(name, args[0], line, col);
        double[] ys = ToDoubles(name, args[1], line, col);
        double[]? zs = count >= 3 ? ToDoubles(name, args[2], line, col) : null;

        if (xs.Length != ys.Length || (zs is not null && zs.Length != xs.Length))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs the same number of coordinates in every direction.");
        }

        return (xs, ys, zs);
    }

    /// <summary>
    /// <c>convhull</c> in the plane and in space, with the area or volume it encloses as a second
    /// output and the <c>'Simplify'</c> pair that says whether points lying along an edge are kept.
    /// </summary>
    private static JgsValue[] ConvexHullAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("convhull", args, 1, 5, line, col);

        // The option pair comes last and says nothing about how the points were spelled, so it is
        // taken off before the coordinates are read.
        int count = args.Count;
        bool simplify = true;
        if (count >= 2 && IsTextScalar(args[count - 2]))
        {
            string option = Str("convhull", args, count - 2, line, col);
            if (!string.Equals(option, "Simplify", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"convhull: '{option}' is not an option it takes; the only one is 'Simplify'.");
            }

            simplify = args[count - 1].IsTruthy;
            count -= 2;
        }

        if (count is < 1 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"convhull expects between 1 and 3 argument(s) of coordinates, but got {count}.");
        }

        (double[] x, double[] y, double[]? z) = CoordinatesOf("convhull", args, count, line, col);

        if (z is null)
        {
            if (x.Length < 3)
            {
                throw new JgsRuntimeException(line, col, "convhull needs at least 3 points.");
            }

            double[] hull = ConvexHull(x, y, simplify);
            JgsValue indices = Numbers(hull);
            return wanted <= 1 ? [indices] : [indices, JgsValue.Number(PolygonArea(hull, x, y))];
        }

        if (x.Length < 4)
        {
            throw new JgsRuntimeException(line, col, "a convex hull in space needs at least 4 points.");
        }

        int[,] faces = ConvexHull3D.Faces(x, y, z);
        JgsValue triangles = JgsMatrix.Build(faces.GetLength(0), 3, (f, v) => faces[f, v] + 1.0);
        return wanted <= 1
            ? [triangles]
            : [triangles, JgsValue.Number(ConvexHull3D.Volume(faces, x, y, z))];
    }

    /// <summary>The area a closed polygon encloses, by the shoelace sum over its vertices.</summary>
    private static double PolygonArea(double[] hull, double[] x, double[] y)
    {
        double twice = 0;
        for (int i = 0; i + 1 < hull.Length; i++)
        {
            int a = (int)hull[i] - 1;
            int b = (int)hull[i + 1] - 1;
            twice += (x[a] * y[b]) - (x[b] * y[a]);
        }

        return System.Math.Abs(twice) / 2;
    }

    /// <summary>
    /// <c>delaunay</c> in the plane and in space: triangles from points on a sheet, tetrahedra from
    /// points in a volume.
    /// </summary>
    private static JgsValue DelaunayAnswer(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("delaunay", args, 1, 3, line, col);
        (double[] x, double[] y, double[]? z) = CoordinatesOf("delaunay", args, args.Count, line, col);

        if (z is null)
        {
            if (x.Length < 3)
            {
                throw new JgsRuntimeException(line, col, "delaunay needs at least 3 points.");
            }

            int[,] triangles = Delaunay.Triangulate(x, y);

            // The kernel counts from zero; MATLAB's connectivity list counts from one, and this is a
            // list of vertex numbers rather than an index into anything JGraph subscripts.
            return JgsMatrix.Build(triangles.GetLength(0), 3, (t, v) => triangles[t, v] + 1.0);
        }

        if (x.Length < 4)
        {
            throw new JgsRuntimeException(line, col, "a triangulation in space needs at least 4 points.");
        }

        int[,] cells = Delaunay3D.Tetrahedra(x, y, z);
        return JgsMatrix.Build(cells.GetLength(0), 4, (t, v) => cells[t, v] + 1.0);
    }
}
