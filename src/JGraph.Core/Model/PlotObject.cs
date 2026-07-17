using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The abstract base of everything that lives inside an <see cref="AxesModel"/> and represents
/// plotted content (lines, scatter, bars, images, annotations, ...). It carries the editable
/// properties common to all plot content and the seams the framework needs from any plot type:
/// reporting its data extent (for auto-scaling) and hit-testing (for selection).
/// </summary>
public abstract class PlotObject : GraphObject
{
    private string _displayName = string.Empty;
    private double _opacity = 1.0;
    private bool _hitTestVisible = true;
    private int _xAxisIndex;
    private int _yAxisIndex;

    /// <summary>The name shown for this object in a legend (MATLAB "DisplayName").</summary>
    [Category("General"), DisplayName("Display name")]
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <summary>Overall opacity in [0, 1] applied on top of any per-element alpha.</summary>
    [Category("Appearance")]
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>Whether this object participates in hit-testing (data cursor, selection).</summary>
    [Category("Behavior"), DisplayName("Hit-test visible")]
    public bool HitTestVisible
    {
        get => _hitTestVisible;
        set => SetProperty(ref _hitTestVisible, value, InvalidationKind.None);
    }

    /// <summary>Index of the X axis (within the axes' X-axis collection) this object is drawn against.</summary>
    [Category("Behavior"), DisplayName("X axis index")]
    public int XAxisIndex
    {
        get => _xAxisIndex;
        set => SetProperty(ref _xAxisIndex, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>Index of the Y axis (within the axes' Y-axis collection) this object is drawn against.</summary>
    [Category("Behavior"), DisplayName("Y axis index")]
    public int YAxisIndex
    {
        get => _yAxisIndex;
        set => SetProperty(ref _yAxisIndex, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>The owning axes, or null if this object is not attached to a figure tree.</summary>
    [Browsable(false)]
    public AxesModel? Axes => Parent as AxesModel;

    /// <summary>The extent of this object's data along the X direction, or empty if it has no data.</summary>
    public abstract DataRange GetXDataBounds();

    /// <summary>The extent of this object's data along the Y direction, or empty if it has no data.</summary>
    public abstract DataRange GetYDataBounds();

    /// <summary>
    /// Tests whether the given device-space point hits this object, within <paramref name="tolerancePixels"/>.
    /// The default returns no hit; concrete plot types override this to support selection and the data cursor.
    /// </summary>
    /// <param name="pixelPoint">The device-space point to test.</param>
    /// <param name="mapper">Maps between data and device space for the owning axes.</param>
    /// <param name="tolerancePixels">The pick radius in device pixels.</param>
    /// <returns>A hit result, or null if the point does not hit this object.</returns>
    public virtual PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels) => null;
}
