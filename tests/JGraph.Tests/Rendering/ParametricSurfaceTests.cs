using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M45.A: a surface carrying a position per vertex rather than one per row and column. This is what
/// a sphere or a cylinder needs — shapes that fold back over themselves in X or Y, and so have no
/// generating vectors to be described by. ADR 0046 §6 recorded the opposite as a divergence; this
/// reverses it, while keeping the rectilinear grid as the fast path it has always been.
/// </summary>
public class ParametricSurfaceTests
{
    /// <summary>
    /// A cylinder of unit radius: X and Y trace a circle around every ring, so both vary along the
    /// columns and neither can be collapsed to a vector. Rows are the height.
    /// </summary>
    private static (double[,] X, double[,] Y, double[,] Z) Cylinder(int rings = 4, int around = 9)
    {
        var x = new double[rings, around];
        var y = new double[rings, around];
        var z = new double[rings, around];
        for (int r = 0; r < rings; r++)
        {
            for (int c = 0; c < around; c++)
            {
                double theta = 2 * System.Math.PI * c / (around - 1);
                x[r, c] = System.Math.Cos(theta);
                y[r, c] = System.Math.Sin(theta);
                z[r, c] = r / (double)(rings - 1);
            }
        }

        return (x, y, z);
    }

    /// <summary>The same grid a meshgrid produces: X constant down every column, Y across every row.</summary>
    private static (double[,] X, double[,] Y, double[,] Z) MeshGrid(int n = 5)
    {
        var x = new double[n, n];
        var y = new double[n, n];
        var z = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                x[r, c] = c;
                y[r, c] = r;
                z[r, c] = r + c;
            }
        }

        return (x, y, z);
    }

    private static RecordingRenderContext Render(AxesModel axes)
    {
        var figure = new FigureModel();
        figure.Axes.Add(axes);
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    private static AxesModel AxesWith(SurfacePlot surface)
    {
        var axes = new AxesModel { Is3D = true };
        axes.Plots.Add(surface);
        return axes;
    }

    [Fact]
    public void AParametricSurface_DrawsEveryCell()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder();
        var surface = new SurfacePlot(x, y, z);

        Assert.True(surface.IsParametric);
        RecordingRenderContext context = Render(AxesWith(surface));

        // 3 rings of cells by 8 around, two triangles each.
        Assert.Equal(3 * 8 * 6, context.TotalTriangleVertices);
    }

    /// <summary>
    /// The analytic sweep is only correct for a height field over a monotone grid — a cylinder's far
    /// wall sits behind its near one at the same X and Y — so a parametric surface has to fall back
    /// to the depth sort. That fallback staying alive is the reason M44 kept it.
    /// </summary>
    [Fact]
    public void AParametricSurface_FallsBackToTheDepthSort()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder();
        RecordingRenderContext context = Render(AxesWith(new SurfacePlot(x, y, z)));

        // The sweep batches whole wavefronts; the depth sort orders cells individually, so faces and
        // edges have to interleave one cell at a time. 24 cells, one face batch each.
        Assert.Equal(24, context.TriangleBatchCount);
    }

    /// <summary>
    /// The axes have to scale to where the surface actually is. Reading the index ramps instead of
    /// the matrices would put a unit cylinder in a 0..8 box and shrink it to nothing.
    /// </summary>
    [Fact]
    public void AParametricSurface_ReportsThePositionsItActuallyHas()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder();
        var surface = new SurfacePlot(x, y, z);

        Assert.Equal(-1, surface.GetXDataBounds().Min, 6);
        Assert.Equal(1, surface.GetXDataBounds().Max, 6);
        Assert.Equal(-1, surface.GetYDataBounds().Min, 6);
        Assert.Equal(1, surface.GetYDataBounds().Max, 6);
    }

    /// <summary>
    /// Normals come from the cross product of the two tangents rather than from a height gradient,
    /// since a parametric surface has no <c>z = f(x, y)</c>. On a cylinder that means the lit side
    /// faces the light and the opposite side does not — the thing a height-field normal cannot say.
    /// </summary>
    [Fact]
    public void AParametricSurface_IsLitByItsTangentNormals()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder(rings: 3, around: 9);
        var surface = new SurfacePlot(x, y, z)
        {
            Colormap = new Colormap("gray", Colors.White, Colors.White),
            Style = SurfaceStyle.Filled,
        };

        AxesModel axes = AxesWith(surface);
        axes.Lights.Add(new LightModel { Position = new Vector3D(1, 0, 0) });
        RecordingRenderContext context = Render(axes);

        // Every cell of one ring, in the order the depth sort emitted them: the wall facing +X is lit
        // and the wall facing -X is not, so the shaded colors cannot all be the same.
        uint[] colors = context.TriangleColors.ToArray();
        Assert.True(colors.Length > 0);
        Assert.True(
            System.Array.Exists(colors, c => c != colors[0]),
            "a cylinder lit from one side must not come out uniformly shaded");
    }

    /// <summary>
    /// Floor contours march squares over a height field, which a parametric surface is not. The
    /// property is still recorded — it is the caller's stated intent — but nothing is drawn.
    /// </summary>
    [Fact]
    public void AParametricSurface_DrawsNoFloorContours()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder();
        var surface = new SurfacePlot(x, y, z) { Style = SurfaceStyle.Wireframe, ShowContourBelow = true };

        RecordingRenderContext parametric = Render(AxesWith(surface));

        (double[,] mx, double[,] my, double[,] mz) = MeshGrid();
        var rectilinear = new SurfacePlot(FirstRow(mx), FirstColumn(my), mz)
        {
            Style = SurfaceStyle.Wireframe,
            ShowContourBelow = true,
        };
        RecordingRenderContext heightField = Render(AxesWith(rectilinear));

        Assert.True(surface.ShowContourBelow);
        Assert.Equal(24, parametric.PathBatchCount); // the wireframe cells, and nothing else
        Assert.True(
            heightField.PathBatchCount > 16,
            $"the height field should add contour batches, got {heightField.PathBatchCount}");
    }

    /// <summary>A NaN in X or Y punches a hole exactly as a NaN height does.</summary>
    [Fact]
    public void ANaNPosition_SkipsTheCellsAroundIt()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder();
        RecordingRenderContext before = Render(AxesWith(new SurfacePlot(x, y, z)));

        x[1, 1] = double.NaN;
        RecordingRenderContext after = Render(AxesWith(new SurfacePlot(x, y, z)));

        // Vertex (1, 1) is a corner of four cells.
        Assert.Equal(before.TotalTriangleVertices - (4 * 6), after.TotalTriangleVertices);
    }

    [Fact]
    public void MismatchedGrids_AreRejected()
    {
        var x = new double[3, 4];
        var y = new double[3, 4];
        var z = new double[3, 5];

        ArgumentException error = Assert.Throws<ArgumentException>(() => new SurfacePlot(x, y, z));
        Assert.Contains("same size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Switching a surface back to a rectilinear grid has to drop the parametric one with it.</summary>
    [Fact]
    public void SetData_SwitchesBetweenTheTwoGridForms()
    {
        (double[,] x, double[,] y, double[,] z) = Cylinder();
        var surface = new SurfacePlot(x, y, z);
        Assert.True(surface.IsParametric);

        surface.SetData([0, 1, 2], [0, 1], new double[2, 3]);

        Assert.False(surface.IsParametric);
        Assert.Null(surface.XGrid);
        Assert.Equal(0, surface.GetXDataBounds().Min, 6);
        Assert.Equal(2, surface.GetXDataBounds().Max, 6);
    }

    private static double[] FirstRow(double[,] grid)
    {
        var values = new double[grid.GetLength(1)];
        for (int c = 0; c < values.Length; c++)
        {
            values[c] = grid[0, c];
        }

        return values;
    }

    private static double[] FirstColumn(double[,] grid)
    {
        var values = new double[grid.GetLength(0)];
        for (int r = 0; r < values.Length; r++)
        {
            values[r] = grid[r, 0];
        }

        return values;
    }
}
