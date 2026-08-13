using JGraph.Maths.Contours;

namespace JGraph.Maths.Volumes;

/// <summary>
/// Turns a traced line into something with width: a ribbon that twists the way the field turns, and a
/// tube whose thickness says how much the field is spreading.
/// </summary>
/// <remarks>
/// Both are built as a surface grid — one ring or one pair of edges per point of the line — because a
/// grid is what the drawing already knows how to shade. The width at each point is the only thing
/// that differs between the two shapes and the field is the only thing that decides it, which is why
/// the two verbs share everything below except the ring they sweep.
/// </remarks>
public static class StreamGeometry
{
    /// <summary>
    /// A flat band along the line, turning about the line as the field turns. The band's width is
    /// fixed; what varies is which way it faces.
    /// </summary>
    public static (double[,] X, double[,] Y, double[,] Z) Ribbon(
        IReadOnlyList<(double X, double Y, double Z)> line,
        VectorField field,
        double width)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(field);
        if (line.Count < 2)
        {
            throw new ArgumentException("A ribbon needs a line of at least two points.", nameof(line));
        }

        int count = line.Count;
        var x = new double[2, count];
        var y = new double[2, count];
        var z = new double[2, count];
        double half = width / 2;

        // The curl is a property of the whole field, so it is worked out once and then read at each
        // point — computing it inside the loop would rebuild every reading of it per vertex.
        (VectorField turning, _) = field.Curl();

        for (int i = 0; i < count; i++)
        {
            (double tx, double ty, double tz) = Direction(line, i);
            (double cx, double cy, double cz) = turning.Sample(line[i].X, line[i].Y, line[i].Z);
            if (!double.IsFinite(cx) || !double.IsFinite(cy) || !double.IsFinite(cz)
                || (cx == 0 && cy == 0 && cz == 0))
            {
                (cx, cy, cz) = (0, 0, 1);
            }

            // The band lies across both the direction of travel and the axis the field turns about,
            // which is what makes the twist follow the flow rather than an arbitrary fixed up.
            (double ax, double ay, double az) = Normalize(Cross(tx, ty, tz, cx, cy, cz));
            if (ax == 0 && ay == 0 && az == 0)
            {
                (ax, ay, az) = Normalize(Cross(tx, ty, tz, 0, 0, 1));
                if (ax == 0 && ay == 0 && az == 0)
                {
                    (ax, ay, az) = (1, 0, 0);
                }
            }

            x[0, i] = line[i].X - (ax * half);
            y[0, i] = line[i].Y - (ay * half);
            z[0, i] = line[i].Z - (az * half);
            x[1, i] = line[i].X + (ax * half);
            y[1, i] = line[i].Y + (ay * half);
            z[1, i] = line[i].Z + (az * half);
        }

        return (x, y, z);
    }

    /// <summary>
    /// A round tube along the line whose radius follows the given widths — one per point of the line.
    /// </summary>
    public static (double[,] X, double[,] Y, double[,] Z) Tube(
        IReadOnlyList<(double X, double Y, double Z)> line,
        IReadOnlyList<double> radii,
        int sides)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(radii);
        if (line.Count < 2)
        {
            throw new ArgumentException("A tube needs a line of at least two points.", nameof(line));
        }

        if (radii.Count != line.Count)
        {
            throw new ArgumentException("A tube needs one radius for each point.", nameof(radii));
        }

        int around = System.Math.Max(3, sides);
        int count = line.Count;
        var x = new double[around + 1, count];
        var y = new double[around + 1, count];
        var z = new double[around + 1, count];

        // The ring is carried along the line rather than rebuilt at each point, so consecutive rings
        // stay lined up and the tube does not corkscrew where the line turns.
        (double ux, double uy, double uz) = (0, 0, 0);

        for (int i = 0; i < count; i++)
        {
            (double tx, double ty, double tz) = Direction(line, i);
            if (i == 0)
            {
                (ux, uy, uz) = Normalize(Cross(tx, ty, tz, 0, 0, 1));
                if (ux == 0 && uy == 0 && uz == 0)
                {
                    (ux, uy, uz) = Normalize(Cross(tx, ty, tz, 0, 1, 0));
                }
            }
            else
            {
                // Take out whatever part of the carried ring now points along the line.
                double along = (ux * tx) + (uy * ty) + (uz * tz);
                (ux, uy, uz) = Normalize((ux - (along * tx), uy - (along * ty), uz - (along * tz)));
            }

            if (ux == 0 && uy == 0 && uz == 0)
            {
                (ux, uy, uz) = (1, 0, 0);
            }

            (double vx, double vy, double vz) = Normalize(Cross(tx, ty, tz, ux, uy, uz));
            double radius = radii[i];

            for (int k = 0; k <= around; k++)
            {
                double angle = 2 * System.Math.PI * k / around;
                double cos = System.Math.Cos(angle) * radius;
                double sin = System.Math.Sin(angle) * radius;
                x[k, i] = line[i].X + (ux * cos) + (vx * sin);
                y[k, i] = line[i].Y + (uy * cos) + (vy * sin);
                z[k, i] = line[i].Z + (uz * cos) + (vz * sin);
            }
        }

        return (x, y, z);
    }

    /// <summary>
    /// A cone pointing along a direction, as a mesh — the arrowhead <c>coneplot</c> puts at each of
    /// its sample points.
    /// </summary>
    public static IsoMesh Cone(
        double x, double y, double z,
        double dx, double dy, double dz,
        double length, double radius, int sides)
    {
        (double tx, double ty, double tz) = Normalize((dx, dy, dz));
        if (tx == 0 && ty == 0 && tz == 0)
        {
            return new IsoMesh([], [], [], []);
        }

        (double ux, double uy, double uz) = Normalize(Cross(tx, ty, tz, 0, 0, 1));
        if (ux == 0 && uy == 0 && uz == 0)
        {
            (ux, uy, uz) = Normalize(Cross(tx, ty, tz, 0, 1, 0));
        }

        (double vx, double vy, double vz) = Normalize(Cross(tx, ty, tz, ux, uy, uz));

        int around = System.Math.Max(3, sides);
        var px = new List<double> { x + (tx * length), x };
        var py = new List<double> { y + (ty * length), y };
        var pz = new List<double> { z + (tz * length), z };

        for (int k = 0; k < around; k++)
        {
            double angle = 2 * System.Math.PI * k / around;
            double cos = System.Math.Cos(angle) * radius;
            double sin = System.Math.Sin(angle) * radius;
            px.Add(x + (ux * cos) + (vx * sin));
            py.Add(y + (uy * cos) + (vy * sin));
            pz.Add(z + (uz * cos) + (vz * sin));
        }

        var faces = new List<int[]>();
        for (int k = 0; k < around; k++)
        {
            int a = 2 + k;
            int b = 2 + ((k + 1) % around);
            faces.Add([0, a, b]);
            faces.Add([1, a, b]);
        }

        return new IsoMesh([.. px], [.. py], [.. pz], [.. faces]);
    }

    /// <summary>The direction of travel at one point of a line, by central difference.</summary>
    private static (double X, double Y, double Z) Direction(
        IReadOnlyList<(double X, double Y, double Z)> line, int index)
    {
        int before = System.Math.Max(0, index - 1);
        int after = System.Math.Min(line.Count - 1, index + 1);
        (double X, double Y, double Z) a = line[before];
        (double X, double Y, double Z) b = line[after];
        (double x, double y, double z) direction = Normalize((b.X - a.X, b.Y - a.Y, b.Z - a.Z));
        return direction == (0, 0, 0) ? (1, 0, 0) : direction;
    }

    private static (double X, double Y, double Z) Cross(
        double ax, double ay, double az, double bx, double by, double bz) =>
        ((ay * bz) - (az * by), (az * bx) - (ax * bz), (ax * by) - (ay * bx));

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = System.Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return length > 0 ? (v.X / length, v.Y / length, v.Z / length) : (0, 0, 0);
    }
}
