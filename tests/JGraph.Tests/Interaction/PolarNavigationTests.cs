using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Maths.Transforms;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

/// <summary>
/// M83: the gestures on a polar axes.
/// <para>
/// The wheel and the drag were polar-blind for a nameable reason — the renderer handed the
/// interaction layer the <em>Cartesian</em> mapper even for a polar axes, and a polar axes stores θ
/// as its plots' X data and r as their Y. So a wheel moved <c>XLim</c> and <c>YLim</c> perfectly
/// correctly, over ranges the drawing does not read from, and nothing on screen changed.
/// </para>
/// <para>
/// The last fixture is the silent half of the milestone: without <c>RAxis</c> and the rotation in
/// <c>AxesViewState</c>, every gesture below works and the undo stack stays empty.
/// </para>
/// </summary>
public class PolarNavigationTests
{
    private static (InteractionController Controller, AxesModel Axes, FakeInteractionSurface Surface) Polar()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.IsPolar = true;
        axes.RAxis.AutoScale = false;
        axes.RAxis.Range = new DataRange(0, 10);
        axes.ThetaAxis.Range = new DataRange(0, 360);

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 200, 200));
        return (new InteractionController(surface), axes, surface);
    }

    private static (InteractionController Controller, AxesModel Axes, FakeInteractionSurface Surface) Cartesian()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 10);

        var surface = new FakeInteractionSurface(axes, new Rect2D(0, 0, 200, 200));
        return (new InteractionController(surface), axes, surface);
    }

    private static void Drag(InteractionController controller, Point2D from, Point2D to)
    {
        controller.SetMode(InteractionModeKind.Pan);
        controller.PointerDown(new PointerEventArgs(from, PointerButton.Left, ModifierKeys.None));
        controller.PointerMove(new PointerEventArgs(to, PointerButton.None, ModifierKeys.None));
        controller.PointerUp(new PointerEventArgs(to, PointerButton.Left, ModifierKeys.None));
    }

    [Fact]
    public void TheWheelScalesTheRadiusAndLeavesTheAngleAlone()
    {
        (InteractionController controller, AxesModel axes, _) = Polar();

        controller.Wheel(new WheelEventArgs(new Point2D(100, 100), 120, ModifierKeys.None));

        Assert.True(axes.RAxis.Range.Length < 10, "the radial range should have narrowed");
        Assert.Equal(0, axes.ThetaAxis.Range.Min, 6);
        Assert.Equal(360, axes.ThetaAxis.Range.Max, 6);
    }

    /// <summary>
    /// And it leaves the Cartesian pair alone, which is what says the gesture found the rulers this
    /// axes is drawn through rather than the ones its data happens to be stored on.
    /// </summary>
    [Fact]
    public void TheWheelDoesNotTouchTheCartesianPairOfAPolarAxes()
    {
        (InteractionController controller, AxesModel axes, _) = Polar();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 7);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 7);

        controller.Wheel(new WheelEventArgs(new Point2D(100, 100), 120, ModifierKeys.None));

        Assert.Equal(0, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(7, axes.PrimaryXAxis.Range.Max, 6);
        Assert.Equal(0, axes.PrimaryYAxis.Range.Min, 6);
        Assert.Equal(7, axes.PrimaryYAxis.Range.Max, 6);
    }

    /// <summary>A drag straight out from the centre slides the radii and does not turn the chart.</summary>
    [Fact]
    public void ARadialDragSlidesTheRadiiAndDoesNotTurnTheChart()
    {
        (InteractionController controller, AxesModel axes, _) = Polar();

        Drag(controller, new Point2D(140, 100), new Point2D(170, 100));

        Assert.NotEqual(0, axes.RAxis.Range.Min, 6);
        Assert.Equal(0, axes.ThetaZeroOffset, 6);
    }

    /// <summary>
    /// And a drag around the centre turns it and leaves the radii alone. One gesture with two
    /// components rather than two modes: the chart follows the pointer.
    /// </summary>
    [Fact]
    public void ATangentialDragTurnsTheChartAndLeavesTheRadiiAlone()
    {
        (InteractionController controller, AxesModel axes, _) = Polar();
        DataRange before = axes.RAxis.Range;

        // A quarter turn about the centre, at a constant radius.
        Drag(controller, new Point2D(160, 100), new Point2D(100, 160));

        Assert.NotEqual(0, axes.ThetaZeroOffset, 6);
        Assert.Equal(before.Min, axes.RAxis.Range.Min, 6);
        Assert.Equal(before.Max, axes.RAxis.Range.Max, 6);
    }

    /// <summary>
    /// <c>Dimensions</c> maps onto the two rulers a polar axes has: X is θ and Y is r. The default XY
    /// scales r alone, because that is what zooming a polar chart means.
    /// </summary>
    [Fact]
    public void DimensionsNamesWhichPolarRulerAGestureMoves()
    {
        (InteractionController controller, AxesModel axes, _) = Polar();
        axes.Interactions.Clear();
        axes.Interactions.Add(new ZoomInteractionModel { Dimensions = InteractionDimensions.X });

        controller.Wheel(new WheelEventArgs(new Point2D(100, 100), 120, ModifierKeys.None));

        Assert.True(axes.ThetaAxis.Range.Length < 360, "aimed at theta, the wheel should narrow the wedge");
        Assert.Equal(10, axes.RAxis.Range.Length, 6);
    }

    [Fact]
    public void DisableDefaultInteractivitySilencesThePolarGesturesToo()
    {
        (InteractionController controller, AxesModel axes, _) = Polar();
        axes.InteractionsDisabled = true;

        controller.Wheel(new WheelEventArgs(new Point2D(100, 100), 120, ModifierKeys.None));
        Drag(controller, new Point2D(160, 100), new Point2D(100, 160));

        Assert.Equal(10, axes.RAxis.Range.Length, 6);
        Assert.Equal(0, axes.ThetaZeroOffset, 6);
    }

    /// <summary>A Cartesian axes is untouched by every one of these — the branch is on the mapper.</summary>
    [Fact]
    public void ACartesianAxesNavigatesExactlyAsItDid()
    {
        (InteractionController controller, AxesModel axes, _) = Cartesian();

        controller.Wheel(new WheelEventArgs(new Point2D(100, 100), 120, ModifierKeys.None));

        Assert.True(axes.PrimaryXAxis.Range.Length < 10);
        Assert.True(axes.PrimaryYAxis.Range.Length < 10);
        Assert.Equal(0, axes.ThetaZeroOffset, 6);
    }

    /// <summary>Resetting the view restores what a polar gesture moved, not only the Cartesian pair.</summary>
    [Fact]
    public void ResetViewRestoresTheRadiusTheWedgeAndTheRotation()
    {
        (_, AxesModel axes, _) = Polar();
        axes.RAxis.Range = new DataRange(2, 5);
        axes.ThetaAxis.Range = new DataRange(30, 200);
        axes.ThetaZeroOffset = 45;

        Navigation.ResetView(axes);

        Assert.True(axes.RAxis.AutoScale);
        Assert.Equal(0, axes.ThetaAxis.Range.Min, 6);
        Assert.Equal(360, axes.ThetaAxis.Range.Max, 6);
        Assert.Equal(0, axes.ThetaZeroOffset, 6);
    }

    // --- The silent half ---------------------------------------------------------------------------

    /// <summary>
    /// Without the polar rulers in <see cref="AxesViewState"/>, a polar gesture works and the before
    /// and after compare equal, so <c>CommitViewChange</c> pushes nothing and there is nothing to undo.
    /// </summary>
    [Fact]
    public void APolarGestureIsOneUndoableChange()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Polar();

        controller.Wheel(new WheelEventArgs(new Point2D(100, 100), 120, ModifierKeys.None));
        Assert.True(surface.UndoStack.CanUndo, "a polar wheel must be undoable");

        surface.UndoStack.Undo();
        Assert.Equal(0, axes.RAxis.Range.Min, 6);
        Assert.Equal(10, axes.RAxis.Range.Max, 6);
    }

    [Fact]
    public void ATurnIsUndoneAsOneChange()
    {
        (InteractionController controller, AxesModel axes, FakeInteractionSurface surface) = Polar();

        Drag(controller, new Point2D(160, 100), new Point2D(100, 160));
        Assert.NotEqual(0, axes.ThetaZeroOffset, 6);

        int undos = 0;
        while (surface.UndoStack.CanUndo)
        {
            surface.UndoStack.Undo();
            undos++;
        }

        Assert.Equal(1, undos);
        Assert.Equal(0, axes.ThetaZeroOffset, 6);
    }

    // --- The rotation itself -----------------------------------------------------------------------

    /// <summary>
    /// The rotation is a change of the zero angle, so it moves where a drawn angle lands. Shifting
    /// <c>ThetaLim</c> was the other candidate and moves nothing: the visible turn decides which
    /// angles are drawn, not where a drawn one goes.
    /// </summary>
    [Fact]
    public void TheRotationMovesWhereAnAngleIsDrawn()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.IsPolar = true;
        axes.RAxis.AutoScale = false;
        axes.RAxis.Range = new DataRange(0, 1);

        var area = new Rect2D(0, 0, 200, 200);
        Point2D before = PolarTransform.Create(axes, area).DataToPixel(0, 1);

        axes.ThetaZeroOffset = 90;
        Point2D after = PolarTransform.Create(axes, area).DataToPixel(0, 1);
        Assert.True(Distance(before, after) > 1, "a rotation should move the point at theta = 0");

        // And shifting the visible turn does not, which is why it is not the mechanism.
        axes.ThetaZeroOffset = 0;
        axes.ThetaAxis.Range = new DataRange(30, 390);
        Point2D shifted = PolarTransform.Create(axes, area).DataToPixel(0, 1);
        Assert.Equal(before.X, shifted.X, 6);
        Assert.Equal(before.Y, shifted.Y, 6);
    }

    private static double Distance(Point2D a, Point2D b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}
