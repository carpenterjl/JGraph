using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>The coordinate space an annotation's anchor points are expressed in.</summary>
public enum AnnotationSpace
{
    /// <summary>Anchors are data coordinates of the owning axes; the annotation moves with zoom/pan.</summary>
    Data,

    /// <summary>
    /// Anchors are normalized [0, 1] figure coordinates ((0, 0) = top-left); the annotation stays put
    /// regardless of axis navigation.
    /// </summary>
    Figure,
}

/// <summary>
/// The abstract base of every annotation (text label, arrow, shape, ...). Annotations live either on
/// an axes (<c>AxesModel.Annotations</c>, drawn over the plots in data space) or on the figure
/// (<c>FigureModel.Annotations</c>, drawn over everything in normalized figure space). Geometry is
/// expressed as a small set of anchor points so moving, snapshotting (for undo), and scale-correct
/// pixel translation are uniform across annotation types. Hit-testing and selection use the pixel
/// bounds recorded during the most recent paint, consistent with how the interaction layer reads all
/// other geometry.
/// </summary>
public abstract class AnnotationObject : GraphObject
{
    private AnnotationSpace _space = AnnotationSpace.Data;
    private double _opacity = 1.0;

    /// <summary>Which coordinate space this annotation's anchors are expressed in.</summary>
    [Category("Behavior")]
    public AnnotationSpace Space
    {
        get => _space;
        set => SetProperty(ref _space, value, InvalidationKind.Layout);
    }

    /// <summary>Overall opacity in [0, 1] applied on top of any per-element alpha.</summary>
    [Category("Appearance")]
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>
    /// The device-space bounds this annotation occupied in the most recent paint, or
    /// <see cref="Rect2D.Empty"/> if it has not been drawn yet. Set by the annotation's own render
    /// implementation; used for hit-testing and the selection highlight.
    /// </summary>
    [Browsable(false)]
    public Rect2D RenderedBounds { get; private set; }

    /// <summary>The geometry-defining points of this annotation, in its <see cref="Space"/> coordinates.</summary>
    public abstract IReadOnlyList<Point2D> GetAnchorPoints();

    /// <summary>
    /// Replaces the geometry-defining points. The list must have the same length and order as
    /// <see cref="GetAnchorPoints"/> returns.
    /// </summary>
    public abstract void SetAnchorPoints(IReadOnlyList<Point2D> anchors);

    /// <summary>
    /// Tests whether a device-space point hits this annotation, based on the bounds recorded during
    /// the most recent paint. Annotation types with sparse geometry (arrows) override this with a
    /// tighter test.
    /// </summary>
    public virtual bool HitTest(Point2D pixel, double tolerancePixels)
    {
        Rect2D b = RenderedBounds;
        if (b.IsEmpty)
        {
            return false;
        }

        return pixel.X >= b.Left - tolerancePixels
            && pixel.X <= b.Right + tolerancePixels
            && pixel.Y >= b.Top - tolerancePixels
            && pixel.Y <= b.Bottom + tolerancePixels;
    }

    /// <summary>
    /// Moves every anchor by a device-space delta, converting through <paramref name="mapper"/> so the
    /// move is exact for any axis scale (linear, log, inverted). The mapper must be the one for this
    /// annotation's <see cref="Space"/>.
    /// </summary>
    public void TranslateByPixels(Vector2D pixelDelta, ICoordinateMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        IReadOnlyList<Point2D> anchors = GetAnchorPoints();
        var shifted = new Point2D[anchors.Count];
        for (int i = 0; i < anchors.Count; i++)
        {
            shifted[i] = ShiftByPixels(anchors[i], pixelDelta, mapper);
        }

        SetAnchorPoints(shifted);
    }

    /// <summary>Shifts one anchor point by a device-space delta through the given mapper.</summary>
    public static Point2D ShiftByPixels(Point2D point, Vector2D pixelDelta, ICoordinateMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        Point2D pixel = mapper.DataToPixel(point.X, point.Y);
        return mapper.PixelToData(pixel.X + pixelDelta.X, pixel.Y + pixelDelta.Y);
    }

    /// <summary>Records the device-space bounds drawn in the current paint. Called from render code.</summary>
    protected void SetRenderedBounds(Rect2D bounds) => RenderedBounds = bounds;
}
