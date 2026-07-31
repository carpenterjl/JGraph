using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M45.D: the drawing primitives — a 3D polyline, a 3D marker cloud, filled polygons, and a label
/// anchored in space. Each is checked for the thing that makes it more than a 2D plot with a third
/// array: gaps at NaN, painter order by projected depth, and an anchor that moves with the camera.
/// </summary>
public class Primitive3DRenderingTests
{
    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    private static FigureModel Figure3D(out AxesModel axes)
    {
        var figure = new FigureModel();
        axes = figure.AddAxes();
        axes.Is3D = true;
        return figure;
    }

    // --- Line3DPlot ---------------------------------------------------------------------------

    [Fact]
    public void Line3D_DrawsOnePolylineThroughItsPoints()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        axes.AddLine3D([0, 1, 2, 3], [0, 1, 0, 1], [0, 1, 2, 3]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(1, context.PolylineCount);
    }

    /// <summary>
    /// A NaN is a break in the line, not a point at the origin: the run splits into two polylines,
    /// which is the same reading a 2D series gets.
    /// </summary>
    [Fact]
    public void Line3D_BreaksAtANonFinitePoint()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        axes.AddLine3D([0, 1, 2, 3, 4], [0, 1, 0, 1, 0], [0, 1, double.NaN, 1, 2]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PolylineCount);
    }

    /// <summary>And no marker is drawn at the break, only at the four real points.</summary>
    [Fact]
    public void Line3D_DrawsMarkersOnlyAtFinitePoints()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        Line3DPlot plot = axes.AddLine3D([0, 1, 2, 3, 4], [0, 1, 0, 1, 0], [0, 1, double.NaN, 1, 2]);
        plot.Marker = MarkerType.Circle;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(4, context.TotalMarkerPoints);
    }

    [Fact]
    public void Line3D_ReportsItsBoundsOnAllThreeAxes()
    {
        var plot = new Line3DPlot([1, 5], [-2, 3], [0, 40]);

        Assert.Equal(1, plot.GetXDataBounds().Min);
        Assert.Equal(5, plot.GetXDataBounds().Max);
        Assert.Equal(-2, plot.GetYDataBounds().Min);
        Assert.Equal(40, plot.GetZDataBounds().Max);
    }

    [Fact]
    public void Line3D_RejectsMismatchedLengths() =>
        Assert.Throws<ArgumentException>(() => new Line3DPlot([1, 2, 3], [1, 2], [1, 2, 3]));

    // --- Scatter3DPlot ------------------------------------------------------------------------

    [Fact]
    public void Scatter3_DrawsEveryPointInOneBatchWhenTheStyleIsUniform()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        axes.AddScatter3D([0, 1, 2], [0, 1, 2], [0, 1, 2]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(1, context.MarkerBatchCount);
        Assert.Equal(3, context.TotalMarkerPoints);
    }

    /// <summary>
    /// The reason a 3D scatter is its own plot type: markers go out back to front, so a near one
    /// covers a far one. At the default camera (elevation 30) the far end of the Z axis is the low
    /// end, and Y grows downward on screen, so the first marker drawn must sit lowest.
    /// </summary>
    [Fact]
    public void Scatter3_DrawsPointsBackToFront()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        // Deliberately declared near-first, so a plot that ignored depth would fail this.
        axes.AddScatter3D([0, 0, 0], [0, 0, 0], [2, 1, 0]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(3, context.MarkerPoints.Count);
        Assert.True(
            context.MarkerPoints[0].Y > context.MarkerPoints[^1].Y,
            "the first marker drawn should be the lowest on screen, which is the farthest away");
    }

    /// <summary>Per-point colors force one call per point, and they come out of the colormap.</summary>
    [Fact]
    public void Scatter3_WithColorData_DrawsEachPointInItsOwnColor()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        Scatter3DPlot plot = axes.AddScatter3D([0, 1, 2], [0, 1, 2], [0, 1, 2]);
        plot.ColorData = new double[] { 0, 0.5, 1 };
        plot.Colormap = Colormap.Grayscale;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(3, context.MarkerBatchCount);
        Assert.Equal(3, context.MarkerStyles.Select(s => s.Edge).Distinct().Count());
    }

    /// <summary>
    /// A scatter with no color data must not claim the axes' colorbar — that belongs to whatever is
    /// actually colormapped, which is the whole reason <c>HasMappedData</c> exists.
    /// </summary>
    [Fact]
    public void Scatter3_WithoutColorData_DoesNotClaimTheColorbar()
    {
        var plain = new Scatter3DPlot([0, 1], [0, 1], [0, 1]);
        var colored = new Scatter3DPlot([0, 1], [0, 1], [0, 1]) { ColorData = new double[] { 1, 2 } };

        Assert.False(((IColorMapped)plain).HasMappedData);
        Assert.True(((IColorMapped)colored).HasMappedData);
        Assert.Equal((1, 2), ((IColorMapped)colored).ColorRange);
    }

    [Fact]
    public void Scatter3_RejectsColorDataOfTheWrongLength()
    {
        var plot = new Scatter3DPlot([0, 1, 2], [0, 1, 2], [0, 1, 2]);

        Assert.Throws<ArgumentException>(() => plot.ColorData = new double[] { 1, 2 });
    }

    // --- PatchPlot ----------------------------------------------------------------------------

    [Fact]
    public void Patch_DrawsOnePolygonPerFaceIn2D()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddPatch(
            [0, 1, 1, 0, 2, 3, 3],
            [0, 0, 1, 1, 0, 0, 1],
            new double[7],
            [[0, 1, 2, 3], [4, 5, 6]]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PolygonCount);
    }

    /// <summary>
    /// In 3D the faces are sorted by mean projected depth, so a face behind another is painted first.
    /// Two parallel squares at different Y put the far one lower on screen at the default camera.
    /// </summary>
    [Fact]
    public void Patch_DrawsFacesBackToFrontIn3D()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        axes.AddPatch(
            [0, 1, 1, 0, 0, 1, 1, 0],
            [0, 0, 0, 0, 4, 4, 4, 4],   // the second face is far along +y
            [0, 0, 1, 1, 0, 0, 1, 1],
            [[0, 1, 2, 3], [4, 5, 6, 7]]);

        RecordingRenderContext context = Render(figure);

        Assert.Equal(2, context.PolygonCount);
        Assert.True(
            context.PolygonMeanY[0] < context.PolygonMeanY[1],
            "the far face should be painted first, and it sits higher on screen");
    }

    /// <summary>
    /// Interp shading is the one path that goes through M44's triangle primitive: a quad fans into
    /// two triangles, six vertices, each with its own color.
    /// </summary>
    [Fact]
    public void Patch_WithInterpShading_DrawsATriangleFan()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        PatchPlot patch = axes.AddPatch([0, 1, 1, 0], [0, 0, 1, 1], [0, 0, 1, 1]);
        patch.ColorData = new double[] { 0, 0.3, 0.6, 1 };
        patch.Shading = PatchShading.Interp;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(1, context.TriangleBatchCount);
        Assert.Equal(6, context.TotalTriangleVertices);
        Assert.Equal(4, context.TriangleColors.Distinct().Count());
    }

    /// <summary>Per-face color data colors each face once, without touching the triangle path.</summary>
    [Fact]
    public void Patch_WithPerFaceColorData_FillsEachFaceFlat()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        PatchPlot patch = axes.AddPatch(
            [0, 1, 1, 0, 2, 3, 3, 2],
            [0, 0, 1, 1, 0, 0, 1, 1],
            new double[8],
            [[0, 1, 2, 3], [4, 5, 6, 7]]);
        patch.ColorData = new double[] { 0, 1 };
        patch.Colormap = Colormap.Grayscale;

        RecordingRenderContext context = Render(figure);

        Assert.Equal(0, context.TriangleBatchCount);
        Assert.Equal(2, context.PolygonCount);
        Assert.NotEqual(context.PolygonFills[0], context.PolygonFills[1]);
    }

    [Fact]
    public void Patch_WithNoEdgeColor_DrawsNoOutline()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        PatchPlot patch = axes.AddPatch([0, 1, 1], [0, 0, 1], new double[3]);
        patch.EdgeColor = null;

        RecordingRenderContext context = Render(figure);

        Assert.All(context.PolygonStrokes, stroke => Assert.Null(stroke));
    }

    [Fact]
    public void Patch_RejectsAFaceThatNamesAMissingVertex() =>
        Assert.Throws<ArgumentException>(() =>
            new PatchPlot([0, 1, 1], [0, 0, 1], new double[3], [[0, 1, 5]]));

    // --- text in space ------------------------------------------------------------------------

    /// <summary>
    /// A label in a 3D axes is anchored through the camera, so turning the camera moves it. That is
    /// the whole difference from a 2D label, which would sit at the same pixel either way. The anchor
    /// is a corner of the box rather than its centre — the centre sits on the axis the azimuth turns
    /// about, so it is the one point that would not move.
    /// </summary>
    [Fact]
    public void Text3D_AnchorFollowsTheCamera()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        axes.AddSurface([0, 1], [0, 1], new double[,] { { 0, 1 }, { 1, 2 } });
        axes.Annotations.Add(new TextAnnotation(1, 0, "peak") { Z = 2 });

        Point2D atDefault = AnchorOf(Render(figure), "peak");
        axes.Azimuth += 90;
        Point2D afterTurn = AnchorOf(Render(figure), "peak");

        Assert.NotEqual(atDefault.X, afterTurn.X, 3);
    }

    /// <summary>The height matters: two labels at the same X and Y but different Z land apart.</summary>
    [Fact]
    public void Text3D_HeightMovesTheLabelUpTheBox()
    {
        FigureModel figure = Figure3D(out AxesModel axes);
        axes.AddSurface([0, 1], [0, 1], new double[,] { { 0, 1 }, { 1, 2 } });
        axes.Annotations.Add(new TextAnnotation(0.5, 0.5, "low") { Z = 0 });
        axes.Annotations.Add(new TextAnnotation(0.5, 0.5, "high") { Z = 2 });

        RecordingRenderContext context = Render(figure);

        Assert.True(AnchorOf(context, "low").Y > AnchorOf(context, "high").Y);
    }

    private static Point2D AnchorOf(RecordingRenderContext context, string text) =>
        context.TextPositions[context.Texts.IndexOf(text)];
}
