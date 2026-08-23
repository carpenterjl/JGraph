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
    private int _seriesIndex = -1;
    private bool _clipping = true;
    private PlotAnnotationModel? _annotation;
    private DataTipTemplateModel? _dataTipTemplate;

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

    /// <summary>
    /// The seat this plot took in its axes' series cycle, or -1 when it never took one (a plot
    /// built through the raw API). The renderer resolves the palette color from this at draw time,
    /// which is what lets a later <c>colororder</c> retint an auto-colored line, and why deleting a
    /// neighbor no longer recolors the survivors.
    /// </summary>
    [Browsable(false)]
    public int SeriesIndex
    {
        get => _seriesIndex;
        set => SetProperty(ref _seriesIndex, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether this object is trimmed to the plot box. MATLAB clips every plot by default and lets a
    /// script turn it off per object, which is how content that runs past the limits is shown; the
    /// axes' own <see cref="AxesModel.Clipping"/> still has the last word, because an axes told not to
    /// clip clips nothing.
    /// </summary>
    [Category("Behavior")]
    public bool Clipping
    {
        get => _clipping;
        set => SetProperty(ref _clipping, value, InvalidationKind.Render);
    }

    /// <summary>
    /// MATLAB's little side object, whose one useful part says whether this series takes a legend
    /// row. Built on first ask: a plot that is never asked never carries one.
    /// </summary>
    [Browsable(false)]
    public PlotAnnotationModel Annotation
    {
        get
        {
            if (_annotation is null)
            {
                _annotation = new PlotAnnotationModel();
                _annotation.SetParent(this);
            }

            return _annotation;
        }
    }

    /// <summary>What a data tip on this series says, built on first ask with this kind's own rows.</summary>
    [Browsable(false)]
    public DataTipTemplateModel DataTipTemplate
    {
        get
        {
            if (_dataTipTemplate is null)
            {
                _dataTipTemplate = new DataTipTemplateModel();
                _dataTipTemplate.SetParent(this);
                _dataTipTemplate.SetRows(DefaultDataTipRows());
            }

            return _dataTipTemplate;
        }
    }

    /// <summary>
    /// Whether a legend should carry a row for this series. Reading it never builds an annotation, so
    /// the renderer asking about every plot on an axes does not give them all one.
    /// </summary>
    [Browsable(false)]
    public bool ShowsInLegend =>
        _annotation is null || _annotation.LegendInformation.IconDisplayStyle != LegendIconDisplay.Off;

    /// <summary>
    /// The rows a data tip on this kind shows before anyone changes them. The base pair is what every
    /// series in a plot box has; kinds with more channels than a position override this.
    /// </summary>
    protected virtual IEnumerable<DataTipRowModel> DefaultDataTipRows()
    {
        yield return new DataTipRowModel("X", "XData");
        yield return new DataTipRowModel("Y", "YData");
    }

    /// <summary>The owning axes, or null if this object is not attached to a figure tree.</summary>
    [Browsable(false)]
    public AxesModel? Axes => Parent as AxesModel;

    /// <summary>
    /// Called when this plot joins an axes, and again when the axes' shared color state changes:
    /// a color-mapped plot copies the axes' <see cref="AxesModel.Colormap"/> and
    /// <see cref="AxesModel.ColorLimits"/> here, which is what makes <c>colormap</c> and
    /// <c>clim</c> act whichever side of the plotting verb they are called on. Plots that do not
    /// map data ignore it.
    /// </summary>
    public virtual void AdoptAxesDefaults(AxesModel axes)
    {
    }

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
