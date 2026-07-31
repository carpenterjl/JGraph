using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>M20b: headless rendering of surface/mesh/contour plots, the 3D axes box, and the colorbar.</summary>
public class Surface3DRenderingTests
{
    private static (double[] X, double[] Y, double[,] Z) Grid(int n = 11)
    {
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = i;
        }

        var z = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                z[r, c] = System.Math.Sin(x[c] * 0.5) * System.Math.Cos(y[r] * 0.5);
            }
        }

        return (x, y, z);
    }

    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    /// <summary>
    /// M44 wave 1: a surf issues two triangles per cell and one edge path per cell, batched by
    /// anti-diagonal wavefront. The batch count is the strategy itself — <c>rows + cols - 3</c>
    /// wavefronts for faces and the same again for the edges that have to sit between them — so it
    /// is pinned rather than merely asserted to be "small".
    /// </summary>
    [Fact]
    public void Surf_RendersTwoTrianglesPerCell_InWavefrontBatches()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        axes.AddSurface(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(600, context.TotalTriangleVertices); // 10x10 cells, 6 vertices each
        Assert.Equal(19, context.TriangleBatchCount);     // rows + cols - 3 wavefronts
        Assert.Equal(19, context.PathBatchCount);         // one edge batch behind each face batch
        Assert.Equal(0, context.PolygonCount);            // per-cell polygons are gone
        Assert.True(context.LineCount >= 8, $"expected box/grid lines, got {context.LineCount}");
        Assert.True(context.TextCount > 0, "expected tick labels");
        Assert.Equal(0, context.ClipDepth); // balanced push/pop
    }

    /// <summary>
    /// Every interior grid line is shared by two cells and must be stroked exactly once. Drawing it
    /// from both sides double-blends it, which darkens the whole wireframe of a translucent surface.
    /// A 10x10 cell grid has 2 * 10 * 11 = 220 edges in total.
    /// </summary>
    [Fact]
    public void Surf_StrokesEachGridEdge_ExactlyOnce()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        axes.AddSurface(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(220, context.TotalSubpaths);
        Assert.Equal(440, context.TotalPathVertices); // two endpoints per edge
    }

    /// <summary>
    /// A surface with no wireframe still traces its own silhouette, because the triangle primitive
    /// rasterizes without antialiasing and the outer boundary would otherwise be a hard staircase.
    /// Only the boundary: a 10x10 cell grid has four sides of ten cells and no interior edges.
    /// </summary>
    [Fact]
    public void FilledSurface_TracesItsOutline_AndNothingInside()
    {
        var figure = new FigureModel();
        (double[] x, double[] y, double[,] z) = Grid(11);
        figure.AddAxes().AddSurface(x, y, z, SurfaceStyle.Filled);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(40, context.TotalSubpaths);
        Assert.Equal(600, context.TotalTriangleVertices);
    }

    /// <summary>
    /// The outline is drawn in the surface's own color to soften the silhouette, which only works
    /// on an opaque surface — over a translucent one it would blend twice and darken the rim.
    /// </summary>
    [Fact]
    public void TranslucentFilledSurface_SkipsTheOutline()
    {
        var figure = new FigureModel();
        (double[] x, double[] y, double[,] z) = Grid(11);
        SurfacePlot surface = figure.AddAxes().AddSurface(x, y, z, SurfaceStyle.Filled);
        surface.Opacity = 0.5;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(0, context.PathBatchCount);
        Assert.Equal(600, context.TotalTriangleVertices);
    }

    /// <summary>
    /// <c>shading interp</c> colors the corners rather than the cell, so a batch carries as many
    /// distinct vertex colors as the surface has distinct heights instead of one per cell.
    /// </summary>
    [Fact]
    public void ShadingInterp_GivesEachVertexItsOwnColor()
    {
        (double[] x, double[] y, double[,] z) = Grid(11);

        var flat = new FigureModel();
        flat.AddAxes().AddSurface(x, y, z, SurfaceStyle.Filled);
        RecordingRenderContext flatContext = Render(flat);

        var interp = new FigureModel();
        SurfacePlot surface = interp.AddAxes().AddSurface(x, y, z, SurfaceStyle.Filled);
        surface.Shading = SurfaceShading.Interp;
        RecordingRenderContext interpContext = Render(interp);

        // Flat shading repeats one color across all six vertices of a cell; interp does not.
        Assert.Equal(flatContext.TotalTriangleVertices, interpContext.TotalTriangleVertices);
        Assert.True(
            interpContext.TriangleColors.Distinct().Count() > flatContext.TriangleColors.Distinct().Count(),
            "interp shading should introduce colors that flat shading never produces");
    }

    [Fact]
    public void AddSurface_Switches_AxesTo3D()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(3);
        axes.AddSurface(x, y, z);

        Assert.True(axes.Is3D);
    }

    [Fact]
    public void Mesh_WithContourBelow_AddsFloorLines()
    {
        (double[] x, double[] y, double[,] z) = Grid(11);

        var figure = new FigureModel();
        figure.AddAxes().AddSurface(x, y, z, SurfaceStyle.Wireframe);
        RecordingRenderContext plain = Render(figure);

        var figure2 = new FigureModel();
        SurfacePlot surface = figure2.AddAxes().AddSurface(x, y, z, SurfaceStyle.Wireframe);
        surface.ShowContourBelow = true;
        RecordingRenderContext withContours = Render(figure2);

        Assert.True(
            withContours.TotalPathVertices > plain.TotalPathVertices,
            "meshc should draw extra floor contour lines");
        Assert.Equal(plain.PathBatchCount + surface.ContourLevels, withContours.PathBatchCount);
    }

    /// <summary>
    /// The floor contour level count used to be the literal 8 buried in the renderer, so nothing
    /// could ask for more or fewer. It is a property now, and one batch is drawn per level.
    /// </summary>
    [Fact]
    public void ContourLevels_ControlsHowManyFloorContoursAreDrawn()
    {
        (double[] x, double[] y, double[,] z) = Grid(11);

        var figure = new FigureModel();
        SurfacePlot surface = figure.AddAxes().AddSurface(x, y, z, SurfaceStyle.Filled);
        surface.ShowContourBelow = true;

        surface.ContourLevels = 3;
        RecordingRenderContext three = Render(figure);
        surface.ContourLevels = 6;
        RecordingRenderContext six = Render(figure);

        Assert.Equal(three.PathBatchCount + 3, six.PathBatchCount);
    }

    [Fact]
    public void SurfaceWithNaN_SkipsAffectedCells()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        z[5, 5] = double.NaN; // kills the 4 cells sharing this vertex
        axes.AddSurface(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(96 * 6, context.TotalTriangleVertices);
    }

    [Fact]
    public void ZAutoscale_PicksUpSurfaceHeights()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(5);
        axes.AddSurface(x, y, z);
        axes.AutoScalePadding = 0;

        figure.RecomputeDataBounds();

        Assert.True(axes.ZAxis.Range.Min < 0);
        Assert.True(axes.ZAxis.Range.Max > 0);
    }

    /// <summary>
    /// M44 wave 2: iso-lines are assembled into whole curves and stroked one level at a time. The
    /// old renderer issued a <c>DrawLine</c> per two-point marching-squares segment, which is not
    /// merely slow: a dash pattern restarts at the start of every sub-path, so a dashed contour came
    /// out as an even row of ticks that ignored the pattern entirely.
    /// </summary>
    [Fact]
    public void ContourLines_StrokeOneAssembledPathBatch_PerLevel()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(21);
        ContourPlot contour = axes.AddContour(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.False(axes.Is3D);
        Assert.Equal(contour.LevelCount, context.PathBatchCount);
        Assert.True(context.TotalPathVertices > 20, $"expected contour geometry, got {context.TotalPathVertices}");
        Assert.NotNull(context.LastPathStroke);

        // Assembled, not loose: far fewer sub-paths than the segments they were chained out of.
        int segments = 0;
        foreach (double level in ContourLevels(contour, z))
        {
            segments += JGraph.Maths.Contours.MarchingSquares.Lines(x, y, z, level).Count;
        }

        Assert.True(
            context.TotalSubpaths < segments / 4,
            $"{context.TotalSubpaths} sub-paths for {segments} segments — the segments were not chained");
    }

    /// <summary>
    /// A filled band is one path of many tiling sub-paths, so Skia scan-converts it as a unit and
    /// the seams between its cells are gone. What survives is one stroke per band rather than one
    /// per cell: adjacent bands are still separate paths, and each has to cover its own half of the
    /// edge they share. It is the band's own fill color, so it is an outline and not a line.
    /// </summary>
    [Fact]
    public void ContourFilled_DrawsOneBandBatch_OutlinedInItsOwnColor()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(21);
        axes.AddContour(x, y, z, filled: true);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(9, context.PathBatchCount); // 8 interior levels -> 10 boundaries -> 9 bands
        Assert.True(context.TotalSubpaths > 100, $"expected band cells, got {context.TotalSubpaths}");
        Assert.Equal(0, context.PolygonCount); // per-cell polygons are gone
        Assert.All(context.PathFills, fill => Assert.NotNull(fill));
        Assert.Equal(context.PathFills[^1], context.LastPathStroke!.Value.Color);
    }

    /// <summary>
    /// Over a translucent contour the outline blends on top of the fill it is tracing, so the rim
    /// of every band comes out darker than the band — which is exactly the artifact the old
    /// per-cell seam stroke produced, at every cell border rather than every band border. There the
    /// outline is left off.
    /// </summary>
    [Fact]
    public void TranslucentContourFilled_SkipsTheBandOutline()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(21);
        ContourPlot contour = axes.AddContour(x, y, z, filled: true);
        contour.Opacity = 0.5;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(9, context.PathBatchCount);
        Assert.Null(context.LastPathStroke);
    }

    private static double[] ContourLevels(ContourPlot contour, double[,] z)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (double v in z)
        {
            min = System.Math.Min(min, v);
            max = System.Math.Max(max, v);
        }

        var levels = new double[contour.LevelCount];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = min + ((max - min) * (i + 1) / (contour.LevelCount + 1.0));
        }

        return levels;
    }

    [Fact]
    public void Colorbar_DrawsGradientStrip_AndReservesRoom()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(11);
        axes.AddSurface(x, y, z);
        axes.Colorbar.Visible = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(1, context.ImageCount); // the 1x256 gradient
        Assert.True(context.LastImageDestination.Width > 0);
        Assert.True(context.LastImageDestination.Right <= 640);
    }

    [Fact]
    public void Colorbar_WithoutColorMappedPlot_DrawsNothing()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine([1.0, 2, 3], [1.0, 4, 9]);
        axes.Colorbar.Visible = true;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(0, context.ImageCount);
    }

    [Fact]
    public void PainterOrder_RunsBackToFront_AndRepeats()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = Grid(15);
        axes.AddSurface(x, y, z);
        axes.Azimuth = -37.5;
        axes.Elevation = 30;

        RecordingRenderContext first = Render(figure);
        RecordingRenderContext second = Render(figure);

        Assert.Equal(first.TriangleBatchCount, second.TriangleBatchCount);
        Assert.Equal(first.TotalTriangleVertices, second.TotalTriangleVertices);
        Assert.Equal(first.LineCount, second.LineCount);
        Assert.Equal(first.TextCount, second.TextCount);

        // Not merely repeatable: the order is specified. Device Y grows downward and the camera
        // looks down on the surface, so the first batch drawn must sit above the last.
        Assert.True(
            first.TriangleBatchMeanY[0] < first.TriangleBatchMeanY[^1],
            $"first batch at y={first.TriangleBatchMeanY[0]:F1} should be behind the last at y={first.TriangleBatchMeanY[^1]:F1}");
    }

    /// <summary>
    /// M44 wave 0: a tall spike belongs to whichever grid cells it stands on, and those cells are
    /// painted when their <em>footprint</em> comes up — not when the spike's own height says so.
    /// Sorting cells by mean vertex depth gets this wrong: the spike's height pushes it toward the
    /// viewer, so it sorts in among cells that are physically rows in front of it and paints over
    /// them. The analytic sweep never looks at Z, so the spike stays in the far row where it lives.
    /// </summary>
    [Fact]
    public void PainterOrder_FollowsTheGroundSweep_NotVertexDepth()
    {
        const int n = 21;
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = i;
        }

        // Flat everywhere except one tall spike on the far edge (largest y, middle column).
        var z = new double[n, n];
        z[n - 1, n / 2] = 100;

        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddSurface(x, y, z);
        axes.Azimuth = 0;
        axes.Elevation = 30;

        RecordingRenderContext context = Render(figure);

        // Six vertices per cell, all one color under flat shading, so the vertex stream divides
        // cleanly into cells. Only the two cells straddling the spike differ from the flat color.
        uint flat = context.TriangleColors[0];
        var spikeCells = new List<int>();
        for (int i = 0; i < context.TriangleColors.Count; i += 6)
        {
            if (context.TriangleColors[i] != flat)
            {
                spikeCells.Add(i / 6);
            }
        }

        Assert.Equal(2, spikeCells.Count);

        // The spike sits on the far edge, so its cells belong to an early wavefront regardless of
        // how tall it is. Mean-vertex-depth sorting instead pushes them to the very end of the
        // ~400-cell order, which is the mis-ordering this replaced.
        Assert.All(spikeCells, i => Assert.True(
            i < 120,
            $"spike cell drawn at position {i} of {(n - 1) * (n - 1)}, expected near the front of the sweep"));
    }

    /// <summary>
    /// The sweep is only a valid painter's order when both grid vectors are monotonic. A grid that
    /// doubles back has overlapping cell footprints, so the general depth sort has to take over.
    /// </summary>
    [Fact]
    public void NonMonotonicGrid_StillRendersEveryCell()
    {
        (double[] x, double[] y, double[,] z) = Grid(11);
        x[4] = x[7]; // breaks monotonicity without changing the grid size

        var figure = new FigureModel();
        figure.AddAxes().AddSurface(x, y, z);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(600, context.TotalTriangleVertices);
    }
}
