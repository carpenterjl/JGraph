using JGraph.Core.Primitives;
using JGraph.Maths.Contours;

namespace JGraph.Maths.Volumes;

/// <summary>Which side of the level a cap covers.</summary>
public enum CapSide
{
    /// <summary>The part of each face where the readings are at or above the level.</summary>
    Above,

    /// <summary>The part of each face where the readings are at or below the level.</summary>
    Below,
}

/// <summary>
/// The lids that close an isosurface where it runs into the side of the box it was found in.
/// </summary>
/// <remarks>
/// An isosurface is only the place where the field reaches the level; where the region above the
/// level runs out of grid rather than curving back, the surface simply stops and the shape is left
/// open, so anything drawn behind it shows through the hole. The caps are the flat pieces that close
/// those openings: on each of the six faces of the box, the part of the face where the readings are
/// on the chosen side of the level. Each cell of a face contributes the part of itself that qualifies
/// — its own corners that are inside, plus the points along its edges where the level is crossed —
/// which is <see cref="MarchingSquares.FilledCells"/> read as a lid rather than as a band of colour.
/// </remarks>
public static class IsoCaps
{
    /// <summary>The six lids of a field at a level, as one mesh.</summary>
    public static IsoMesh Surface(ScalarField field, double level, CapSide side)
    {
        ArgumentNullException.ThrowIfNull(field);

        double lower = side == CapSide.Above ? level : double.NegativeInfinity;
        double upper = side == CapSide.Above ? double.PositiveInfinity : level;

        var vx = new List<double>();
        var vy = new List<double>();
        var vz = new List<double>();
        var faces = new List<int[]>();

        // Each face is the grid with one direction pinned; the two that are left become the plane the
        // cell walk happens in, and the pinned one supplies the missing coordinate afterwards.
        if (field.Pages > 1)
        {
            AddFace(field, lower, upper, Axis.Z, 0, vx, vy, vz, faces);
            AddFace(field, lower, upper, Axis.Z, field.Pages - 1, vx, vy, vz, faces);
        }

        if (field.Rows > 1)
        {
            AddFace(field, lower, upper, Axis.Y, 0, vx, vy, vz, faces);
            AddFace(field, lower, upper, Axis.Y, field.Rows - 1, vx, vy, vz, faces);
        }

        if (field.Columns > 1)
        {
            AddFace(field, lower, upper, Axis.X, 0, vx, vy, vz, faces);
            AddFace(field, lower, upper, Axis.X, field.Columns - 1, vx, vy, vz, faces);
        }

        return new IsoMesh([.. vx], [.. vy], [.. vz], [.. faces]);
    }

    private enum Axis
    {
        X,
        Y,
        Z,
    }

    private static void AddFace(
        ScalarField field,
        double lower,
        double upper,
        Axis pinned,
        int index,
        List<double> vx,
        List<double> vy,
        List<double> vz,
        List<int[]> faces)
    {
        (double[] across, double[] down, double[,] readings) = FaceOf(field, pinned, index);
        if (across.Length < 2 || down.Length < 2)
        {
            return;
        }

        IReadOnlyList<Point2D[]> cells = MarchingSquares.FilledCells(across, down, readings, lower, upper);
        double fixedValue = pinned switch
        {
            Axis.X => field.X[index],
            Axis.Y => field.Y[index],
            _ => field.Z[index],
        };

        foreach (Point2D[] polygon in cells)
        {
            if (polygon.Length < 3)
            {
                continue;
            }

            var corners = new int[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                (double x, double y, double z) = pinned switch
                {
                    // On the x face the plane is (z across, y down); on the y face it is (x, z); on
                    // the z face it is the ordinary (x, y) the grid is already written in.
                    Axis.X => (fixedValue, polygon[i].Y, polygon[i].X),
                    Axis.Y => (polygon[i].X, fixedValue, polygon[i].Y),
                    _ => (polygon[i].X, polygon[i].Y, fixedValue),
                };

                vx.Add(x);
                vy.Add(y);
                vz.Add(z);
                corners[i] = vx.Count - 1;
            }

            faces.Add(corners);
        }
    }

    private static (double[] Across, double[] Down, double[,] Readings) FaceOf(
        ScalarField field, Axis pinned, int index)
    {
        switch (pinned)
        {
            case Axis.X:
            {
                var readings = new double[field.Rows, field.Pages];
                for (int r = 0; r < field.Rows; r++)
                {
                    for (int p = 0; p < field.Pages; p++)
                    {
                        readings[r, p] = field.Values[r, index, p];
                    }
                }

                return (field.Z, field.Y, readings);
            }

            case Axis.Y:
            {
                var readings = new double[field.Pages, field.Columns];
                for (int p = 0; p < field.Pages; p++)
                {
                    for (int c = 0; c < field.Columns; c++)
                    {
                        readings[p, c] = field.Values[index, c, p];
                    }
                }

                return (field.X, field.Z, readings);
            }

            default:
            {
                var readings = new double[field.Rows, field.Columns];
                for (int r = 0; r < field.Rows; r++)
                {
                    for (int c = 0; c < field.Columns; c++)
                    {
                        readings[r, c] = field.Values[r, c, index];
                    }
                }

                return (field.X, field.Y, readings);
            }
        }
    }
}
