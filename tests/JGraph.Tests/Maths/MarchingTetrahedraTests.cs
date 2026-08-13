using JGraph.Maths.Contours;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M58: the surface where a field on a grid takes a given value. Built here for <c>fimplicit3</c>,
/// and deliberately ahead of M59's <c>isosurface</c>, which is the same question asked of measured
/// data instead of a formula.
/// </summary>
public class MarchingTetrahedraTests
{
    private static double[] Axis(double low, double high, int n)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = low + ((high - low) * i / (n - 1));
        }

        return values;
    }

    /// <summary>Samples f(x, y, z) onto the [row, column, page] grid the surface reads.</summary>
    private static double[,,] Field(double[] x, double[] y, double[] z, Func<double, double, double, double> f)
    {
        var values = new double[y.Length, x.Length, z.Length];
        for (int r = 0; r < y.Length; r++)
        {
            for (int c = 0; c < x.Length; c++)
            {
                for (int p = 0; p < z.Length; p++)
                {
                    values[r, c, p] = f(x[c], y[r], z[p]);
                }
            }
        }

        return values;
    }

    [Fact]
    public void ASphereFieldGivesASphere()
    {
        double[] axis = Axis(-2, 2, 41);
        double[,,] field = Field(axis, axis, axis, (x, y, z) => (x * x) + (y * y) + (z * z));

        IsoMesh mesh = MarchingTetrahedra.Surface(axis, axis, axis, field, 1);

        Assert.True(mesh.VertexCount > 100);
        Assert.NotEmpty(mesh.Faces);

        double worst = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            double radius = System.Math.Sqrt(
                (mesh.X[i] * mesh.X[i]) + (mesh.Y[i] * mesh.Y[i]) + (mesh.Z[i] * mesh.Z[i]));
            worst = System.Math.Max(worst, System.Math.Abs(radius - 1));
        }

        // The vertices sit on grid edges, so they miss the sphere by at most the curvature over one
        // cell — a hundredth of the radius at this density.
        Assert.True(worst < 0.02, $"a vertex missed the sphere by {worst}.");
    }

    /// <summary>
    /// A closed surface has no rim: every edge of it belongs to exactly two triangles. This is what
    /// the shared-vertex bookkeeping and the fixed cell diagonal are for, and it is the one property
    /// a wrong decomposition breaks first.
    /// </summary>
    [Fact]
    public void AClosedSurfaceHasNoCracksInIt()
    {
        double[] axis = Axis(-2, 2, 25);
        double[,,] field = Field(axis, axis, axis, (x, y, z) => (x * x) + (y * y) + (z * z));

        IsoMesh mesh = MarchingTetrahedra.Surface(axis, axis, axis, field, 1);

        var uses = new Dictionary<(int, int), int>();
        foreach (int[] face in mesh.Faces)
        {
            for (int i = 0; i < 3; i++)
            {
                int a = face[i];
                int b = face[(i + 1) % 3];
                (int, int) edge = a < b ? (a, b) : (b, a);
                uses[edge] = uses.TryGetValue(edge, out int count) ? count + 1 : 1;
            }
        }

        Assert.All(uses.Values, count => Assert.Equal(2, count));
    }

    [Fact]
    public void APlaneFieldGivesAPlane()
    {
        double[] axis = Axis(-1, 1, 11);
        double[,,] field = Field(axis, axis, axis, (x, _, _) => x);

        IsoMesh mesh = MarchingTetrahedra.Surface(axis, axis, axis, field, 0);

        Assert.NotEmpty(mesh.Faces);
        Assert.All(mesh.X, value => Assert.Equal(0, value, 12));
    }

    [Fact]
    public void AFieldThatNeverReachesTheLevelHasNoSurface()
    {
        double[] axis = Axis(0, 1, 6);
        double[,,] above = Field(axis, axis, axis, (_, _, _) => 5);

        Assert.Empty(MarchingTetrahedra.Surface(axis, axis, axis, above, 1).Faces);
        Assert.Empty(MarchingTetrahedra.Surface(axis, axis, axis, above, 9).Faces);
    }

    [Fact]
    public void ACellWithAReadingThatIsNotFiniteIsLeftOut()
    {
        double[] axis = Axis(-1, 1, 5);
        double[,,] field = Field(axis, axis, axis, (x, y, z) => (x * x) + (y * y) + (z * z));
        IsoMesh whole = MarchingTetrahedra.Surface(axis, axis, axis, field, 0.5);

        field[2, 2, 2] = double.NaN;
        IsoMesh holed = MarchingTetrahedra.Surface(axis, axis, axis, field, 0.5);

        Assert.True(holed.Faces.Length < whole.Faces.Length);
    }

    [Fact]
    public void AFieldThatDoesNotMatchItsAxesIsRefused()
    {
        double[] axis = Axis(0, 1, 4);
        var field = new double[3, 4, 4];

        Assert.Throws<ArgumentException>(
            () => MarchingTetrahedra.Surface(axis, axis, axis, field, 0));
    }
}
