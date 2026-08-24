using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Maths.Transforms;
using JGraph.Objects;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>
/// M87: a click in three dimensions lands on what it looks like it landed on.
/// <para>
/// ADR 0071 recorded <em>3D plots are unpickable</em> and ADR 0072 carried it forward: no plot type
/// in space implemented a hit test at all, so every click fell through to the axes and a
/// <c>ButtonDownFcn</c> on a <c>surf</c> never fired. The camera needed to do it has been built on
/// every click since M75, for the axes' <c>CurrentPoint</c> — the picking simply never asked it.
/// </para>
/// <para>
/// The tests here pick their pixels by projecting known data points through a camera built the same
/// way the hit test builds one. That is the only honest way to write them: what a click lands on is
/// decided by the picture, so a test that guessed at pixels would be testing its own guess.
/// </para>
/// </summary>
public class SpatialPickingTests
{
    private static readonly Rect2D PlotArea = new(0, 0, 400, 300);

    /// <summary>
    /// The camera the hit test will build for this axes. Built here the same way so a test can ask
    /// where a data point was drawn and then click exactly there.
    /// </summary>
    private static Projection3D CameraFor(AxesModel axes) =>
        new(axes.PrimaryXAxis.Range,
            axes.ActiveYAxis.Range,
            axes.ZAxis.Range,
            axes.Azimuth,
            axes.Elevation,
            PlotArea,
            axes.PlotBoxAspect,
            axes.Roll);

    /// <summary>
    /// An axes with its three rulers pinned, because nothing lays one out in a headless test and the
    /// camera is built from the ranges. Every test's data lives inside this box.
    /// </summary>
    private static (AxesModel Axes, FakeInteractionSurface Surface) Scene()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.Range = new DataRange(0, 2);
        axes.ActiveYAxis.Range = new DataRange(0, 2);
        axes.ZAxis.Range = new DataRange(0, 2);
        return (axes, new FakeInteractionSurface(axes, PlotArea));
    }

    private static Point2D PixelOf(AxesModel axes, double x, double y, double z) =>
        CameraFor(axes).Project(x, y, z).Position;

    // --- The four kinds that draw in space -----------------------------------------------------------

    /// <summary>A click on a line in space finds the line, not the axes under it.</summary>
    [Fact]
    public void AClickOnALineInSpaceFindsTheLine()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        Line3DPlot line = axes.AddLine3D([0, 1, 2], [0, 1, 2], [0, 1, 2]);

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 1, 1, 1));

        Assert.Same(line, hit.Target);
    }

    /// <summary>
    /// And on the middle of a segment, not only at a vertex — the same rule the flat line uses, so a
    /// long straight run is pickable along its length rather than at its ends alone.
    /// </summary>
    [Fact]
    public void AClickBetweenTwoVerticesStillFindsTheLine()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        Line3DPlot line = axes.AddLine3D([0, 2], [0, 2], [0, 2]);

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 1, 1, 1));

        Assert.Same(line, hit.Target);
    }

    [Fact]
    public void AClickOnAMarkerInSpaceFindsTheScatter()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        Scatter3DPlot points = axes.AddScatter3D([0, 1, 2], [0, 2, 1], [0, 1, 2]);

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 1, 2, 1));
        Assert.NotNull(hit.Axes);

        Assert.Same(points, hit.Target);
    }

    /// <summary>A stem is its head and its stalk, and a click on either is a click on the stem.</summary>
    [Theory]
    [InlineData(2.0)]
    [InlineData(1.0)]
    public void AClickOnAStemsHeadOrItsStalkFindsTheStem(double height)
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        Stem3DPlot stems = axes.AddStem3D([0, 1, 2], [0, 1, 2], [0, 2, 0]);

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 1, 1, height));

        Assert.Same(stems, hit.Target);
    }

    /// <summary>
    /// A click inside a surface cell hits the surface. A shape that could only be picked on its
    /// outline would be the wrong answer for the commonest thing anybody clicks on.
    /// </summary>
    [Fact]
    public void AClickInsideASurfaceCellFindsTheSurface()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        SurfacePlot sheet = axes.AddSurface(
            [0, 1, 2],
            [0, 1, 2],
            new double[,] { { 0, 0, 0 }, { 0, 1, 0 }, { 0, 0, 0 } });

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 0.5, 0.5, 0));

        Assert.Same(sheet, hit.Target);
    }

    /// <summary>The same for a patch, which is what every slice and cone in the volume family is.</summary>
    [Fact]
    public void AClickInsideAPatchFaceFindsThePatch()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        PatchPlot shape = axes.AddPatch(
            [0, 2, 2, 0], [0, 0, 2, 2], [0, 0, 0, 0], [[0, 1, 2, 3]]);
        axes.Is3D = true;

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 1, 1, 0));

        Assert.Same(shape, hit.Target);
    }

    // --- What must not change ------------------------------------------------------------------------

    /// <summary>
    /// A click on empty space in a 3-D axes still finds the axes, exactly as it did — the branch adds
    /// an answer where there was none, it does not take the old one away.
    /// </summary>
    [Fact]
    public void AClickOnNothingStillFindsTheAxes()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        axes.AddLine3D([0, 1], [0, 1], [0, 1]);

        // A corner of the plot rectangle, far from the little line through the middle of the box.
        FigureHit hit = FigureHitTesting.Resolve(surface, new Point2D(2, 2));

        Assert.Same(axes, hit.Target);
    }

    /// <summary>
    /// A flat axes is picked exactly as before. The camera is built only when the axes says it is in
    /// three dimensions, so nothing flat can reach any of this.
    /// </summary>
    [Fact]
    public void AFlatAxesIsPickedThroughItsFlatMapperAsBefore()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        LinePlot line = axes.AddLine([0, 1, 2], [0, 1, 2]);
        Assert.False(axes.Is3D);

        ICoordinateMapper mapper = AxisTransform.Create(
            PlotArea, axes.PrimaryXAxis, axes.PrimaryYAxis);
        FigureHit hit = FigureHitTesting.Resolve(surface, mapper.DataToPixel(1, 1));

        Assert.Same(line, hit.Target);
    }

    /// <summary>
    /// An object a script has taken out of the picking is not picked in space either. The gate is
    /// read before the hit test, as it always was, so <c>HitTest</c> and <c>PickableParts</c> keep
    /// meaning what they meant.
    /// </summary>
    [Fact]
    public void AnUnpickableObjectIsStillUnpickableInSpace()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();
        Line3DPlot line = axes.AddLine3D([0, 1, 2], [0, 1, 2], [0, 1, 2]);
        line.Selectable = false;

        FigureHit hit = FigureHitTesting.Resolve(surface, PixelOf(axes, 1, 1, 1));

        Assert.NotSame(line, hit.Target);
    }

    /// <summary>
    /// Of two objects under one pixel, the one nearer the camera wins — which is the one drawn on
    /// top, and the one a person was looking at.
    /// <para>
    /// Two flat sheets stacked one above the other, clicked where the upper one's middle was drawn.
    /// The test first checks the lower one is genuinely hit at that pixel too, because a version of
    /// this where the sheets did not overlap on screen would pass without testing anything.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNearerOfTwoSheetsUnderOnePixelIsTheOneHit()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();

        PatchPlot low = axes.AddPatch(
            [0, 2, 2, 0], [0, 0, 2, 2], [0, 0, 0, 0], [[0, 1, 2, 3]]);
        PatchPlot high = axes.AddPatch(
            [0, 2, 2, 0], [0, 0, 2, 2], [0.4, 0.4, 0.4, 0.4], [[0, 1, 2, 3]]);
        axes.Is3D = true;

        Projection3D camera = CameraFor(axes);
        Point2D pixel = camera.Project(1, 1, 0.4).Position;

        // Both sheets really are under this pixel — without that, the assertion below proves nothing.
        Assert.NotNull(low.HitTest3D(pixel, camera, FigureHitTesting.PlotPickTolerancePixels));
        Assert.NotNull(high.HitTest3D(pixel, camera, FigureHitTesting.PlotPickTolerancePixels));

        // And the upper one is nearer the camera, which is what the tie-break is reading.
        Assert.True(
            camera.Project(1, 1, 0.4).Depth > camera.Project(1, 1, 0).Depth,
            "the default view looks down, so the higher sheet should be the nearer one.");

        FigureHit hit = FigureHitTesting.Resolve(surface, pixel);

        Assert.Same(high, hit.Target);
        Assert.NotSame(low, hit.Target);
    }

    /// <summary>
    /// And the same the other way up, so the answer is the camera's and not the draw order's. The
    /// lower sheet is added second here; it must still lose.
    /// </summary>
    [Fact]
    public void DrawOrderDoesNotDecideWhichOfTwoSheetsIsHit()
    {
        (AxesModel axes, FakeInteractionSurface surface) = Scene();

        PatchPlot high = axes.AddPatch(
            [0, 2, 2, 0], [0, 0, 2, 2], [0.4, 0.4, 0.4, 0.4], [[0, 1, 2, 3]]);
        PatchPlot low = axes.AddPatch(
            [0, 2, 2, 0], [0, 0, 2, 2], [0, 0, 0, 0], [[0, 1, 2, 3]]);
        axes.Is3D = true;

        Point2D pixel = CameraFor(axes).Project(1, 1, 0.4).Position;
        FigureHit hit = FigureHitTesting.Resolve(surface, pixel);

        Assert.Same(high, hit.Target);
        Assert.NotSame(low, hit.Target);
    }
}

