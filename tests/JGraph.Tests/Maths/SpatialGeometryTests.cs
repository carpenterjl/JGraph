using JGraph.Maths.Geometry;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// The two kernels M76 added in space: the convex hull and the Delaunay tetrahedralization. Both
/// are checked by the properties that define them rather than by a pinned list of faces — there are
/// many correct triangulations of the same points, and only one set of invariants.
/// </summary>
public class SpatialGeometryTests
{
    private static (double[] X, double[] Y, double[] Z) Cube() =>
    (
        [0, 1, 0, 1, 0, 1, 0, 1],
        [0, 0, 1, 1, 0, 0, 1, 1],
        [0, 0, 0, 0, 1, 1, 1, 1]
    );

    [Fact]
    public void TheHullOfACube_IsTwelveTrianglesAndUnitVolume()
    {
        (double[] x, double[] y, double[] z) = Cube();

        int[,] faces = ConvexHull3D.Faces(x, y, z);

        Assert.Equal(12, faces.GetLength(0));   // six square sides, two triangles each
        Assert.Equal(1.0, ConvexHull3D.Volume(faces, x, y, z), 9);
    }

    [Fact]
    public void APointInside_IsNotAHullVertex()
    {
        (double[] x, double[] y, double[] z) = Cube();
        double[] px = [.. x, 0.5];
        double[] py = [.. y, 0.5];
        double[] pz = [.. z, 0.5];

        int[,] faces = ConvexHull3D.Faces(px, py, pz);

        for (int f = 0; f < faces.GetLength(0); f++)
        {
            for (int v = 0; v < 3; v++)
            {
                Assert.NotEqual(8, faces[f, v]);
            }
        }

        Assert.Equal(1.0, ConvexHull3D.Volume(faces, px, py, pz), 9);
    }

    [Fact]
    public void TheHullOfATetrahedron_IsItsFourFaces()
    {
        double[] x = [0, 1, 0, 0];
        double[] y = [0, 0, 1, 0];
        double[] z = [0, 0, 0, 1];

        int[,] faces = ConvexHull3D.Faces(x, y, z);

        Assert.Equal(4, faces.GetLength(0));
        Assert.Equal(1.0 / 6, ConvexHull3D.Volume(faces, x, y, z), 9);
    }

    /// <summary>Every point must be inside the hull, and the hull must be closed.</summary>
    [Theory]
    [InlineData(12)]
    [InlineData(30)]
    [InlineData(60)]
    public void ARandomCloud_IsEnclosedByItsHull(int count)
    {
        var random = new Random(count);
        var x = new double[count];
        var y = new double[count];
        var z = new double[count];
        for (int i = 0; i < count; i++)
        {
            x[i] = System.Math.Round((random.NextDouble() * 10) - 5, 4);
            y[i] = System.Math.Round((random.NextDouble() * 10) - 5, 4);
            z[i] = System.Math.Round((random.NextDouble() * 10) - 5, 4);
        }

        int[,] faces = ConvexHull3D.Faces(x, y, z);

        // Closed: every edge is shared by exactly two faces.
        var edges = new Dictionary<(int, int), int>();
        for (int f = 0; f < faces.GetLength(0); f++)
        {
            for (int v = 0; v < 3; v++)
            {
                int a = faces[f, v];
                int b = faces[f, (v + 1) % 3];
                (int, int) key = a < b ? (a, b) : (b, a);
                edges[key] = edges.TryGetValue(key, out int already) ? already + 1 : 1;
            }
        }

        Assert.All(edges.Values, static shared => Assert.Equal(2, shared));

        // Convex: no point lies outside any face.
        for (int f = 0; f < faces.GetLength(0); f++)
        {
            int a = faces[f, 0];
            int b = faces[f, 1];
            int c = faces[f, 2];
            double nx = ((y[b] - y[a]) * (z[c] - z[a])) - ((z[b] - z[a]) * (y[c] - y[a]));
            double ny = ((z[b] - z[a]) * (x[c] - x[a])) - ((x[b] - x[a]) * (z[c] - z[a]));
            double nz = ((x[b] - x[a]) * (y[c] - y[a])) - ((y[b] - y[a]) * (x[c] - x[a]));
            double length = System.Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

            for (int p = 0; p < count; p++)
            {
                double above = (((x[p] - x[a]) * nx) + ((y[p] - y[a]) * ny) + ((z[p] - z[a]) * nz)) / length;
                Assert.True(above < 1e-7, $"point {p} is {above} outside face {f}");
            }
        }

        Assert.True(ConvexHull3D.Volume(faces, x, y, z) > 0);
    }

    [Fact]
    public void CoplanarPoints_AreRefusedRatherThanFlattened()
    {
        double[] x = [0, 1, 1, 0, 0.5];
        double[] y = [0, 0, 1, 1, 0.5];
        var z = new double[5];

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ConvexHull3D.Faces(x, y, z));
        Assert.Contains("one plane", error.Message);
    }

    [Fact]
    public void TooFewPoints_AreRefused() =>
        Assert.Throws<ArgumentException>(() =>
            ConvexHull3D.Faces([0, 1, 0], [0, 0, 1], [0, 0, 0]));

    // --- tetrahedralization -------------------------------------------------------------------

    [Fact]
    public void ATetrahedron_IsOneCell()
    {
        double[] x = [0, 1, 0, 0];
        double[] y = [0, 0, 1, 0];
        double[] z = [0, 0, 0, 1];

        int[,] cells = Delaunay3D.Tetrahedra(x, y, z);

        Assert.Equal(1, cells.GetLength(0));
        Assert.Equal(4, cells.GetLength(1));
    }

    [Fact]
    public void ACube_IsCutIntoCellsThatFillIt()
    {
        (double[] x, double[] y, double[] z) = Cube();

        int[,] cells = Delaunay3D.Tetrahedra(x, y, z);

        Assert.True(cells.GetLength(0) >= 5, $"a cube takes at least five tetrahedra, got {cells.GetLength(0)}");
        Assert.Equal(1.0, TotalVolume(cells, x, y, z), 9);
    }

    /// <summary>
    /// The property the triangulation is named for: no point sits inside any cell's circumsphere.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(35)]
    public void ARandomCloud_SatisfiesTheEmptySphereProperty(int count)
    {
        var random = new Random(500 + count);
        var x = new double[count];
        var y = new double[count];
        var z = new double[count];
        for (int i = 0; i < count; i++)
        {
            x[i] = System.Math.Round((random.NextDouble() * 8) - 4, 4);
            y[i] = System.Math.Round((random.NextDouble() * 8) - 4, 4);
            z[i] = System.Math.Round((random.NextDouble() * 8) - 4, 4);
        }

        int[,] cells = Delaunay3D.Tetrahedra(x, y, z);
        Assert.True(cells.GetLength(0) > 0);

        for (int c = 0; c < cells.GetLength(0); c++)
        {
            (double sx, double sy, double sz, double radius) = Circumsphere(cells, c, x, y, z);
            for (int p = 0; p < count; p++)
            {
                if (p == cells[c, 0] || p == cells[c, 1] || p == cells[c, 2] || p == cells[c, 3])
                {
                    continue;
                }

                double distance = System.Math.Sqrt(
                    ((x[p] - sx) * (x[p] - sx)) + ((y[p] - sy) * (y[p] - sy)) + ((z[p] - sz) * (z[p] - sz)));
                Assert.True(distance > radius - 1e-6,
                    $"point {p} is inside cell {c}'s circumsphere ({distance} vs {radius})");
            }
        }

        // The cells must together fill the hull, which is the other half of being a triangulation.
        double hull = ConvexHull3D.Volume(ConvexHull3D.Faces(x, y, z), x, y, z);
        Assert.Equal(hull, TotalVolume(cells, x, y, z), 6);
    }

    [Fact]
    public void CoplanarPoints_HaveNoTetrahedralization()
    {
        double[] x = [0, 1, 1, 0, 0.5];
        double[] y = [0, 0, 1, 1, 0.5];
        var z = new double[5];

        Assert.Throws<ArgumentException>(() => Delaunay3D.Tetrahedra(x, y, z));
    }

    private static double TotalVolume(int[,] cells, double[] x, double[] y, double[] z)
    {
        double total = 0;
        for (int c = 0; c < cells.GetLength(0); c++)
        {
            int a = cells[c, 0];
            int b = cells[c, 1];
            int m = cells[c, 2];
            int d = cells[c, 3];
            double ax = x[a] - x[d];
            double ay = y[a] - y[d];
            double az = z[a] - z[d];
            double bx = x[b] - x[d];
            double by = y[b] - y[d];
            double bz = z[b] - z[d];
            double cx = x[m] - x[d];
            double cy = y[m] - y[d];
            double cz = z[m] - z[d];
            total += System.Math.Abs(
                (ax * ((by * cz) - (bz * cy)))
                - (ay * ((bx * cz) - (bz * cx)))
                + (az * ((bx * cy) - (by * cx)))) / 6;
        }

        return total;
    }

    private static (double X, double Y, double Z, double Radius) Circumsphere(
        int[,] cells, int cell, double[] x, double[] y, double[] z)
    {
        int a = cells[cell, 0];
        int b = cells[cell, 1];
        int c = cells[cell, 2];
        int d = cells[cell, 3];

        // Three planes equidistant from the first vertex and each of the others.
        double[,] m =
        {
            { x[b] - x[a], y[b] - y[a], z[b] - z[a] },
            { x[c] - x[a], y[c] - y[a], z[c] - z[a] },
            { x[d] - x[a], y[d] - y[a], z[d] - z[a] },
        };
        double[] rhs =
        [
            (Square(x[b]) - Square(x[a]) + Square(y[b]) - Square(y[a]) + Square(z[b]) - Square(z[a])) / 2,
            (Square(x[c]) - Square(x[a]) + Square(y[c]) - Square(y[a]) + Square(z[c]) - Square(z[a])) / 2,
            (Square(x[d]) - Square(x[a]) + Square(y[d]) - Square(y[a]) + Square(z[d]) - Square(z[a])) / 2,
        ];

        double determinant =
            (m[0, 0] * ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1])))
            - (m[0, 1] * ((m[1, 0] * m[2, 2]) - (m[1, 2] * m[2, 0])))
            + (m[0, 2] * ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])));

        double cx = Replaced(m, rhs, 0) / determinant;
        double cy = Replaced(m, rhs, 1) / determinant;
        double cz = Replaced(m, rhs, 2) / determinant;
        double radius = System.Math.Sqrt(
            ((x[a] - cx) * (x[a] - cx)) + ((y[a] - cy) * (y[a] - cy)) + ((z[a] - cz) * (z[a] - cz)));
        return (cx, cy, cz, radius);
    }

    private static double Square(double v) => v * v;

    private static double Replaced(double[,] m, double[] rhs, int column)
    {
        var c = (double[,])m.Clone();
        for (int r = 0; r < 3; r++)
        {
            c[r, column] = rhs[r];
        }

        return (c[0, 0] * ((c[1, 1] * c[2, 2]) - (c[1, 2] * c[2, 1])))
            - (c[0, 1] * ((c[1, 0] * c[2, 2]) - (c[1, 2] * c[2, 0])))
            + (c[0, 2] * ((c[1, 0] * c[2, 1]) - (c[1, 1] * c[2, 0])));
    }
}
