using JGraph.Maths.Contours;
using JGraph.Maths.Volumes;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// The volume kernels M59 draws with, each checked against an answer worked out on paper rather than
/// against what the code happens to do.
/// </summary>
public class VolumeFieldTests
{
    private static double[] Evenly(double low, double high, int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = count == 1 ? low : low + ((high - low) * i / (count - 1));
        }

        return values;
    }

    /// <summary>Builds a field from a formula in x, y and z.</summary>
    private static ScalarField FieldOf(Func<double, double, double, double> f, int count = 11)
    {
        double[] x = Evenly(-1, 1, count);
        double[] y = Evenly(-1, 1, count);
        double[] z = Evenly(-1, 1, count);
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

        return new ScalarField(x, y, z, values);
    }

    private static VectorField VectorOf(
        Func<double, double, double, (double U, double V, double W)> f, int count = 11)
    {
        ScalarField u = FieldOf((x, y, z) => f(x, y, z).U, count);
        ScalarField v = FieldOf((x, y, z) => f(x, y, z).V, count);
        ScalarField w = FieldOf((x, y, z) => f(x, y, z).W, count);
        return new VectorField(u, v, w);
    }

    [Fact]
    public void A_reading_taken_where_the_grid_crosses_is_the_reading_that_is_there()
    {
        ScalarField field = FieldOf((x, y, z) => (100 * x) + (10 * y) + z);
        Assert.Equal(0, field.Sample(0, 0, 0), 12);
        Assert.Equal(111, field.Sample(1, 1, 1), 12);
        Assert.Equal(-100 + 10, field.Sample(-1, 1, 0), 12);
    }

    [Fact]
    public void A_reading_between_the_grid_lines_is_interpolated_along_each_direction()
    {
        // A field that is linear in each direction is reproduced exactly by straight-line blending,
        // which is the strongest statement that can be made about trilinear interpolation.
        ScalarField field = FieldOf((x, y, z) => (3 * x) - (2 * y) + (0.5 * z));
        Assert.Equal((3 * 0.137) - (2 * -0.44) + (0.5 * 0.29), field.Sample(0.137, -0.44, 0.29), 10);
    }

    [Fact]
    public void A_point_outside_the_box_has_no_reading()
    {
        ScalarField field = FieldOf((x, y, z) => x + y + z);
        Assert.True(double.IsNaN(field.Sample(2, 0, 0)));
        Assert.True(double.IsNaN(field.Sample(0, -3, 0)));
        Assert.True(double.IsNaN(field.Sample(0, 0, double.NaN)));
    }

    [Fact]
    public void The_gradient_of_a_sloping_field_is_its_slopes()
    {
        ScalarField field = FieldOf((x, y, z) => (2 * x) + (5 * y) - (3 * z));
        (ScalarField gx, ScalarField gy, ScalarField gz) = field.Gradient();

        // Including the faces, where the difference is one-sided: a straight slope has the same
        // gradient there, so a wrong one-sided formula shows up at the edge and nowhere else.
        for (int r = 0; r < field.Rows; r++)
        {
            for (int c = 0; c < field.Columns; c++)
            {
                for (int p = 0; p < field.Pages; p++)
                {
                    Assert.Equal(2, gx.Values[r, c, p], 9);
                    Assert.Equal(5, gy.Values[r, c, p], 9);
                    Assert.Equal(-3, gz.Values[r, c, p], 9);
                }
            }
        }
    }

    [Fact]
    public void The_divergence_of_the_outward_field_is_three_everywhere()
    {
        // div(x, y, z) = 1 + 1 + 1.
        VectorField field = VectorOf((x, y, z) => (x, y, z));
        ScalarField divergence = field.Divergence();
        Assert.Equal(3, divergence.Values[5, 5, 5], 9);
        Assert.Equal(3, divergence.Values[0, 0, 0], 9);
    }

    [Fact]
    public void The_curl_of_a_field_turning_about_z_points_along_z()
    {
        // curl(-y, x, 0) = (0, 0, 2), and the angular velocity is half its length.
        VectorField field = VectorOf((x, y, z) => (-y, x, 0));
        (VectorField curl, ScalarField speed) = field.Curl();
        Assert.Equal(0, curl.U.Values[4, 6, 3], 9);
        Assert.Equal(0, curl.V.Values[4, 6, 3], 9);
        Assert.Equal(2, curl.W.Values[4, 6, 3], 9);
        Assert.Equal(1, speed.Values[4, 6, 3], 9);
    }

    [Fact]
    public void The_curl_of_a_field_that_does_not_turn_is_nothing()
    {
        VectorField field = VectorOf((x, y, z) => (x, y, z));
        (VectorField curl, ScalarField speed) = field.Curl();
        Assert.Equal(0, curl.W.Values[2, 3, 4], 9);
        Assert.Equal(0, speed.Values[2, 3, 4], 9);
    }

    [Fact]
    public void A_streamline_in_a_field_that_points_one_way_is_straight()
    {
        VectorField field = VectorOf((x, y, z) => (1, 0, 0));
        IReadOnlyList<(double X, double Y, double Z)> line =
            StreamlineIntegrator.Trace(field, -1, 0.2, -0.4, new StreamlineOptions());

        Assert.True(line.Count > 5);
        foreach ((double _, double y, double z) in line)
        {
            Assert.Equal(0.2, y, 9);
            Assert.Equal(-0.4, z, 9);
        }

        // It runs from where it started to the far wall, and stops there rather than going on.
        Assert.Equal(-1, line[0].X, 9);
        Assert.True(line[^1].X > 0.9, $"the line stopped early, at x = {line[^1].X}");
        Assert.True(line[^1].X <= 1 + 1e-9);
    }

    [Fact]
    public void A_streamline_going_round_stays_on_its_circle()
    {
        // The midpoint step is what this checks: a first-order step spirals outward visibly here.
        VectorField field = VectorOf((x, y, z) => (-y, x, 0), 41);
        IReadOnlyList<(double X, double Y, double Z)> line = StreamlineIntegrator.Trace(
            field, 0.5, 0, 0, new StreamlineOptions(0.1, 400));

        Assert.True(line.Count > 100);
        foreach ((double x, double y, double _) in line)
        {
            double radius = System.Math.Sqrt((x * x) + (y * y));
            Assert.True(
                System.Math.Abs(radius - 0.5) < 0.02,
                $"the orbit drifted to radius {radius:0.####}");
        }
    }

    [Fact]
    public void A_streamline_started_outside_the_grid_has_no_points()
    {
        VectorField field = VectorOf((x, y, z) => (1, 0, 0));
        Assert.Empty(StreamlineIntegrator.Trace(field, 5, 0, 0, new StreamlineOptions()));
    }

    [Fact]
    public void A_streamline_keeps_to_its_budget()
    {
        VectorField field = VectorOf((x, y, z) => (-y, x, 0), 41);
        IReadOnlyList<(double X, double Y, double Z)> line = StreamlineIntegrator.Trace(
            field, 0.5, 0, 0, new StreamlineOptions(0.1, 25));
        Assert.Equal(25, line.Count);
    }

    [Fact]
    public void A_streamline_standing_still_stops_rather_than_spending_its_budget()
    {
        // The field is nothing at all, so the line cannot move anywhere from where it started.
        VectorField field = VectorOf((x, y, z) => (0, 0, 0));
        IReadOnlyList<(double X, double Y, double Z)> line = StreamlineIntegrator.Trace(
            field, 0, 0, 0, new StreamlineOptions(0.1, 5000));
        Assert.Single(line);
    }

    [Fact]
    public void A_subvolume_keeps_the_readings_inside_the_box_and_no_others()
    {
        ScalarField field = FieldOf((x, y, z) => x, 11);
        ScalarField cut = VolumeReduction.Subvolume(field, [0, 1, double.NaN, double.NaN, -1, 0]);

        Assert.All(cut.X, x => Assert.True(x >= 0));
        Assert.Equal(field.Rows, cut.Rows);
        Assert.All(cut.Z, z => Assert.True(z <= 0));
        Assert.Equal(0, cut.Values[0, 0, 0], 9);
    }

    [Fact]
    public void Reducing_a_volume_keeps_the_ends_so_it_still_spans_what_it_spanned()
    {
        ScalarField field = FieldOf((x, y, z) => x, 11);
        ScalarField smaller = VolumeReduction.Reduce(field, 3, 3, 3);

        Assert.True(smaller.Columns < field.Columns);
        Assert.Equal(field.X[0], smaller.X[0], 12);
        Assert.Equal(field.X[^1], smaller.X[^1], 12);
        Assert.Equal(field.Y[^1], smaller.Y[^1], 12);
    }

    [Fact]
    public void Smoothing_a_flat_field_leaves_it_flat()
    {
        ScalarField field = FieldOf((x, y, z) => 4);
        ScalarField smoothed = VolumeReduction.Smooth(field, [3, 3, 3]);
        Assert.Equal(4, smoothed.Values[0, 0, 0], 9);
        Assert.Equal(4, smoothed.Values[5, 5, 5], 9);
    }

    [Fact]
    public void Smoothing_pulls_a_lone_spike_down_towards_its_neighbours()
    {
        ScalarField field = FieldOf((x, y, z) => 0, 7);
        field.Values[3, 3, 3] = 27;
        ScalarField smoothed = VolumeReduction.Smooth(field, [3, 3, 3]);

        Assert.True(smoothed.Values[3, 3, 3] < 27);
        Assert.True(smoothed.Values[3, 3, 3] > 0);
        Assert.True(smoothed.Values[2, 3, 3] > 0, "the spike did not spread to its neighbour at all");
        Assert.Equal(27, Total(smoothed), 6);
    }

    [Fact]
    public void A_reading_without_a_value_does_not_spread_when_smoothed()
    {
        ScalarField field = FieldOf((x, y, z) => 2, 7);
        field.Values[3, 3, 3] = double.NaN;
        ScalarField smoothed = VolumeReduction.Smooth(field, [3, 3, 3]);

        Assert.True(double.IsNaN(smoothed.Values[3, 3, 3]));
        Assert.Equal(2, smoothed.Values[2, 3, 3], 9);
        Assert.Equal(2, smoothed.Values[3, 4, 3], 9);
    }

    [Fact]
    public void The_bounds_of_a_field_are_the_ends_of_its_grid()
    {
        ScalarField field = FieldOf((x, y, z) => 0);
        Assert.Equal([-1, 1, -1, 1, -1, 1], VolumeReduction.Bounds(field));
    }

    [Fact]
    public void Caps_close_a_surface_that_runs_into_the_side_of_its_box()
    {
        // A field below the level in the middle and above it at the walls, so the region above the
        // level reaches every face: every cap has area, and none of them is the whole face.
        ScalarField field = FieldOf((x, y, z) => (x * x) + (y * y) + (z * z), 11);
        IsoMesh caps = IsoCaps.Surface(field, 0.5, CapSide.Above);

        Assert.NotEmpty(caps.Faces);
        Assert.All(caps.Faces, face => Assert.True(face.Length >= 3));

        // Every cap vertex sits on a wall of the box, which is what a cap is.
        Assert.All(
            Enumerable.Range(0, caps.VertexCount),
            i => Assert.True(
                OnAWall(caps.X[i]) || OnAWall(caps.Y[i]) || OnAWall(caps.Z[i]),
                $"a cap vertex at ({caps.X[i]:0.##}, {caps.Y[i]:0.##}, {caps.Z[i]:0.##}) is not on a wall"));
    }

    [Fact]
    public void The_two_sides_of_a_cap_are_the_two_parts_of_the_wall()
    {
        // The level has to be one the walls actually cross for both sides to exist: on the wall
        // z = -1 this field reads x² + y² + 1, which runs from 1 to 3, so 1.5 divides it and 0.5
        // does not — at 0.5 the whole wall is above and the lower cap is honestly empty.
        ScalarField field = FieldOf((x, y, z) => (x * x) + (y * y) + (z * z), 11);
        Assert.NotEmpty(IsoCaps.Surface(field, 1.5, CapSide.Above).Faces);
        Assert.NotEmpty(IsoCaps.Surface(field, 1.5, CapSide.Below).Faces);
        Assert.Empty(IsoCaps.Surface(field, 0.5, CapSide.Below).Faces);
    }

    [Fact]
    public void Normals_of_a_sphere_lie_along_its_radius()
    {
        // This field grows outwards, so the negated slope points inwards: every normal runs down the
        // radius towards the centre. Sign and all, because the field says which way round it is.
        ScalarField field = FieldOf((x, y, z) => (x * x) + (y * y) + (z * z), 21);
        IsoMesh sphere = MarchingTetrahedra.Surface(field.X, field.Y, field.Z, field.Values, 0.3);
        (double[] nx, double[] ny, double[] nz) = MeshOperations.Normals(field, sphere);

        Assert.True(sphere.VertexCount > 50);
        for (int i = 0; i < sphere.VertexCount; i++)
        {
            double radius = System.Math.Sqrt(
                (sphere.X[i] * sphere.X[i]) + (sphere.Y[i] * sphere.Y[i]) + (sphere.Z[i] * sphere.Z[i]));
            double along =
                ((nx[i] * sphere.X[i]) + (ny[i] * sphere.Y[i]) + (nz[i] * sphere.Z[i])) / radius;
            Assert.True(along < -0.99, $"a normal was off the radius by {along:0.###}");
        }
    }

    [Fact]
    public void Reducing_a_mesh_leaves_fewer_faces_and_no_degenerate_one()
    {
        ScalarField field = FieldOf((x, y, z) => (x * x) + (y * y) + (z * z), 21);
        IsoMesh sphere = MarchingTetrahedra.Surface(field.X, field.Y, field.Z, field.Values, 0.25);
        IsoMesh smaller = MeshOperations.Reduce(sphere, 0.25);

        Assert.True(smaller.Faces.Length < sphere.Faces.Length);
        Assert.NotEmpty(smaller.Faces);
        Assert.All(smaller.Faces, face => Assert.Equal(face.Length, face.Distinct().Count()));
    }

    [Fact]
    public void Shrinking_pulls_every_face_in_towards_its_own_centre()
    {
        var mesh = new IsoMesh([0, 1, 0], [0, 0, 1], [0, 0, 0], [[0, 1, 2]]);
        IsoMesh small = MeshOperations.Shrink(mesh, 0.5);

        double cx = (0 + 1 + 0) / 3.0;
        double cy = (0 + 0 + 1) / 3.0;
        Assert.Equal(3, small.VertexCount);
        Assert.Equal(cx + ((0 - cx) * 0.5), small.X[0], 12);
        Assert.Equal(cy + ((0 - cy) * 0.5), small.Y[0], 12);
        Assert.Equal(cx + ((1 - cx) * 0.5), small.X[1], 12);
    }

    [Fact]
    public void A_surface_grid_becomes_one_quadrilateral_for_each_of_its_cells()
    {
        var x = new double[2, 3] { { 0, 1, 2 }, { 0, 1, 2 } };
        var y = new double[2, 3] { { 0, 0, 0 }, { 1, 1, 1 } };
        var z = new double[2, 3] { { 5, 6, 7 }, { 8, 9, 10 } };

        IsoMesh mesh = MeshOperations.FromSurface(x, y, z);
        Assert.Equal(6, mesh.VertexCount);
        Assert.Equal(2, mesh.Faces.Length);
        Assert.All(mesh.Faces, face => Assert.Equal(4, face.Length));
    }

    [Fact]
    public void Colouring_a_mesh_reads_the_other_field_at_its_vertices()
    {
        ScalarField shape = FieldOf((x, y, z) => (x * x) + (y * y) + (z * z), 15);
        IsoMesh sphere = MarchingTetrahedra.Surface(shape.X, shape.Y, shape.Z, shape.Values, 0.25);
        ScalarField paint = FieldOf((x, y, z) => x, 15);

        double[] colors = MeshOperations.SampleAt(paint, sphere);
        Assert.Equal(sphere.VertexCount, colors.Length);
        for (int i = 0; i < colors.Length; i++)
        {
            Assert.Equal(sphere.X[i], colors[i], 6);
        }
    }

    [Fact]
    public void A_ribbon_is_two_edges_running_beside_the_line()
    {
        VectorField field = VectorOf((x, y, z) => (-y, x, 0), 21);
        IReadOnlyList<(double X, double Y, double Z)> line = StreamlineIntegrator.Trace(
            field, 0.5, 0, 0, new StreamlineOptions(0.1, 60));

        (double[,] x, double[,] y, double[,] z) = StreamGeometry.Ribbon(line, field, 0.1);
        Assert.Equal(2, x.GetLength(0));
        Assert.Equal(line.Count, x.GetLength(1));

        for (int i = 0; i < line.Count; i++)
        {
            double width = System.Math.Sqrt(
                System.Math.Pow(x[1, i] - x[0, i], 2)
                + System.Math.Pow(y[1, i] - y[0, i], 2)
                + System.Math.Pow(z[1, i] - z[0, i], 2));
            Assert.Equal(0.1, width, 9);

            // The line runs down the middle of the band.
            Assert.Equal(line[i].X, (x[0, i] + x[1, i]) / 2, 9);
            Assert.Equal(line[i].Y, (y[0, i] + y[1, i]) / 2, 9);
        }
    }

    [Fact]
    public void A_tube_is_a_closed_ring_at_every_point_of_its_line()
    {
        VectorField field = VectorOf((x, y, z) => (1, 0, 0), 11);
        IReadOnlyList<(double X, double Y, double Z)> line = StreamlineIntegrator.Trace(
            field, -1, 0, 0, new StreamlineOptions(0.2, 20));
        var radii = Enumerable.Repeat(0.05, line.Count).ToList();

        (double[,] x, double[,] y, double[,] z) = StreamGeometry.Tube(line, radii, 8);
        Assert.Equal(9, x.GetLength(0));
        Assert.Equal(line.Count, x.GetLength(1));

        for (int i = 0; i < line.Count; i++)
        {
            // The ring closes: the last point around is the first one again.
            Assert.Equal(x[0, i], x[8, i], 9);
            Assert.Equal(y[0, i], y[8, i], 9);
            Assert.Equal(z[0, i], z[8, i], 9);

            for (int k = 0; k < 8; k++)
            {
                double distance = System.Math.Sqrt(
                    System.Math.Pow(y[k, i] - line[i].Y, 2)
                    + System.Math.Pow(z[k, i] - line[i].Z, 2)
                    + System.Math.Pow(x[k, i] - line[i].X, 2));
                Assert.Equal(0.05, distance, 9);
            }
        }
    }

    [Fact]
    public void A_cone_points_the_way_it_was_given()
    {
        IsoMesh cone = StreamGeometry.Cone(0, 0, 0, 0, 0, 2, 1, 0.25, 6);

        Assert.NotEmpty(cone.Faces);
        // Vertex 0 is the tip, one length along the direction; vertex 1 is the centre of the base.
        Assert.Equal(0, cone.X[0], 9);
        Assert.Equal(0, cone.Y[0], 9);
        Assert.Equal(1, cone.Z[0], 9);
        Assert.Equal(0, cone.Z[1], 9);

        for (int i = 2; i < cone.VertexCount; i++)
        {
            double radius = System.Math.Sqrt((cone.X[i] * cone.X[i]) + (cone.Y[i] * cone.Y[i]));
            Assert.Equal(0.25, radius, 9);
            Assert.Equal(0, cone.Z[i], 9);
        }
    }

    [Fact]
    public void A_cone_with_no_direction_is_no_cone()
    {
        IsoMesh cone = StreamGeometry.Cone(0, 0, 0, 0, 0, 0, 1, 0.25, 6);
        Assert.Empty(cone.Faces);
    }

    [Fact]
    public void A_field_whose_readings_do_not_match_its_grid_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            new ScalarField([0, 1], [0, 1, 2], [0], new double[2, 2, 1]));
    }

    private static bool OnAWall(double value) =>
        System.Math.Abs(System.Math.Abs(value) - 1) < 1e-9;

    private static double Total(ScalarField field)
    {
        double sum = 0;
        for (int r = 0; r < field.Rows; r++)
        {
            for (int c = 0; c < field.Columns; c++)
            {
                for (int p = 0; p < field.Pages; p++)
                {
                    sum += field.Values[r, c, p];
                }
            }
        }

        return sum;
    }
}
