using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// A coordinate system within a <see cref="FigureModel"/>: one or more X and Y axes, the plotted
/// content, a grid, and a legend. An axes occupies a rectangular fraction of the figure
/// (<see cref="NormalizedBounds"/>) so that multiple axes can be tiled into subplots.
/// </summary>
public sealed class AxesModel : GraphObject
{
    private string _title = string.Empty;
    private TextStyle _titleStyle = new(Colors.Black, 15, bold: true);
    private Color _background = Colors.White;
    private Rect2D _normalizedBounds = new(0, 0, 1, 1);
    private double _autoScalePadding = 0.05;
    private bool _equalAspect;
    private bool _frameVisible = true;
    private bool _is3D;
    private double _azimuth = -37.5;
    private double _elevation = 30;

    public AxesModel()
    {
        Name = "Axes";
        XAxes = new GraphObjectCollection<AxisModel>(this);
        YAxes = new GraphObjectCollection<AxisModel>(this);
        Plots = new GraphObjectCollection<PlotObject>(this);
        Annotations = new GraphObjectCollection<AnnotationObject>(this);

        Grid = new GridModel();
        Grid.SetParent(this);
        Legend = new LegendModel();
        Legend.SetParent(this);
        Colorbar = new ColorbarModel();
        Colorbar.SetParent(this);

        ZAxis = new AxisModel(AxisOrientation.Vertical, AxisPosition.Left) { Name = "ZAxis" };
        ZAxis.SetParent(this);

        XAxes.Add(new AxisModel(AxisOrientation.Horizontal, AxisPosition.Bottom));
        YAxes.Add(new AxisModel(AxisOrientation.Vertical, AxisPosition.Left));
    }

    /// <summary>The X axes. The first entry is the primary (bottom) axis.</summary>
    public GraphObjectCollection<AxisModel> XAxes { get; }

    /// <summary>The Y axes. The first entry is the primary (left) axis.</summary>
    public GraphObjectCollection<AxisModel> YAxes { get; }

    /// <summary>The plotted content drawn in this coordinate system.</summary>
    public GraphObjectCollection<PlotObject> Plots { get; }

    /// <summary>
    /// Annotations drawn over the plots, clipped to the plot area. Their anchors are data coordinates
    /// (unless an annotation's <see cref="AnnotationObject.Space"/> says otherwise), so they follow
    /// zoom and pan. Annotations never influence auto-scaling.
    /// </summary>
    public GraphObjectCollection<AnnotationObject> Annotations { get; }

    /// <summary>The grid lines.</summary>
    public GridModel Grid { get; }

    /// <summary>The legend (hidden until enabled).</summary>
    public LegendModel Legend { get; }

    /// <summary>The colorbar (hidden until enabled). Legends the first color-mapped plot's colormap.</summary>
    public ColorbarModel Colorbar { get; }

    /// <summary>
    /// The Z axis. Always constructed so its label/range/tick configuration persists, but only
    /// consulted (for autoscale, projection, and drawing) when <see cref="Is3D"/> is true.
    /// </summary>
    public AxisModel ZAxis { get; }

    /// <summary>The primary (first) X axis.</summary>
    public AxisModel PrimaryXAxis => XAxes[0];

    /// <summary>The primary (first) Y axis.</summary>
    public AxisModel PrimaryYAxis => YAxes[0];

    [Category("General")]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty, InvalidationKind.Layout);
    }

    public TextStyle TitleStyle
    {
        get => _titleStyle;
        set => SetProperty(ref _titleStyle, value, InvalidationKind.Layout);
    }

    [Category("Appearance")]
    public Color Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    /// <summary>
    /// This axes' placement within the figure expressed as fractions in [0, 1] of the figure size
    /// (X and Y measured from the top-left). Defaults to the whole figure.
    /// </summary>
    public Rect2D NormalizedBounds
    {
        get => _normalizedBounds;
        set => SetProperty(ref _normalizedBounds, value, InvalidationKind.Layout);
    }

    /// <summary>Fractional padding added around the data extent when an axis auto-scales.</summary>
    [Category("Behavior"), DisplayName("Auto-scale padding")]
    public double AutoScalePadding
    {
        get => _autoScalePadding;
        set => SetProperty(ref _autoScalePadding, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>
    /// When true, one data unit spans the same number of pixels on both axes (MATLAB <c>axis equal</c>):
    /// the plot area is shrunk to a centered rectangle of the correct aspect so circles render round.
    /// Used by polar, Smith, and Nyquist plots. Only meaningful with linear scales.
    /// </summary>
    [Category("Behavior"), DisplayName("Equal aspect")]
    public bool EqualAspect
    {
        get => _equalAspect;
        set => SetProperty(ref _equalAspect, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// When true (default), the rectangular axis frame is drawn around the plot area. Polar and Smith
    /// charts turn it off because they draw their own circular grid instead.
    /// </summary>
    [Category("Appearance"), DisplayName("Frame visible")]
    public bool FrameVisible
    {
        get => _frameVisible;
        set => SetProperty(ref _frameVisible, value, InvalidationKind.Render);
    }

    /// <summary>
    /// When true, this axes renders as a 3D coordinate box: plots implementing the 3D drawing
    /// interface are projected through the camera angles below, and dragging rotates the view
    /// instead of panning. Set automatically by the surface-plot verbs.
    /// </summary>
    [Category("3D View"), DisplayName("3D")]
    public bool Is3D
    {
        get => _is3D;
        set => SetProperty(ref _is3D, value, InvalidationKind.Layout);
    }

    /// <summary>The camera azimuth in degrees (rotation about the vertical axis; MATLAB view() convention).</summary>
    [Category("3D View")]
    public double Azimuth
    {
        get => _azimuth;
        set => SetProperty(ref _azimuth, value, InvalidationKind.Render);
    }

    /// <summary>The camera elevation in degrees, clamped to [-90, 90].</summary>
    [Category("3D View")]
    public double Elevation
    {
        get => _elevation;
        set => SetProperty(ref _elevation, System.Math.Clamp(value, -90, 90), InvalidationKind.Render);
    }

    /// <summary>Adds a secondary X axis at the given position and returns it.</summary>
    public AxisModel AddXAxis(AxisPosition position = AxisPosition.Top)
    {
        var axis = new AxisModel(AxisOrientation.Horizontal, position);
        XAxes.Add(axis);
        return axis;
    }

    /// <summary>Adds a secondary Y axis at the given position and returns it.</summary>
    public AxisModel AddYAxis(AxisPosition position = AxisPosition.Right)
    {
        var axis = new AxisModel(AxisOrientation.Vertical, position);
        YAxes.Add(axis);
        return axis;
    }

    /// <summary>Returns the X axis a plot object is bound to, falling back to the primary axis.</summary>
    public AxisModel GetXAxisFor(PlotObject plot) =>
        plot.XAxisIndex >= 0 && plot.XAxisIndex < XAxes.Count ? XAxes[plot.XAxisIndex] : PrimaryXAxis;

    /// <summary>Returns the Y axis a plot object is bound to, falling back to the primary axis.</summary>
    public AxisModel GetYAxisFor(PlotObject plot) =>
        plot.YAxisIndex >= 0 && plot.YAxisIndex < YAxes.Count ? YAxes[plot.YAxisIndex] : PrimaryYAxis;

    /// <summary>
    /// Recomputes each axis' <see cref="AxisModel.DataBounds"/> from the plots bound to it and, for
    /// axes with <see cref="AxisModel.AutoScale"/> enabled, updates their visible
    /// <see cref="AxisModel.Range"/> to fit (with <see cref="AutoScalePadding"/> applied).
    /// </summary>
    public void RecomputeDataBounds()
    {
        UpdateAxisBounds(XAxes, isX: true);
        UpdateAxisBounds(YAxes, isX: false);
        UpdateZAxisBounds();
    }

    /// <summary>Unions the Z extents of visible 3D plots into <see cref="ZAxis"/> (all 3D plots share it).</summary>
    private void UpdateZAxisBounds()
    {
        DataRange bounds = DataRange.Empty;
        foreach (PlotObject plot in Plots)
        {
            if (plot.Visible && plot is IHasZData zData)
            {
                DataRange plotBounds = zData.GetZDataBounds();
                if (!plotBounds.IsEmpty)
                {
                    bounds = bounds.Union(plotBounds);
                }
            }
        }

        ZAxis.DataBounds = bounds;

        if (ZAxis.AutoScale)
        {
            DataRange fitted = bounds.IsEmpty ? DataRange.Unit : bounds.EnsureValid();
            if (_autoScalePadding > 0 && fitted.IsValid)
            {
                fitted = ExpandForScale(fitted, ZAxis.Scale, _autoScalePadding);
            }

            ZAxis.Range = fitted;
        }
    }

    private void UpdateAxisBounds(GraphObjectCollection<AxisModel> axes, bool isX)
    {
        for (int i = 0; i < axes.Count; i++)
        {
            AxisModel axis = axes[i];
            DataRange bounds = DataRange.Empty;

            foreach (PlotObject plot in Plots)
            {
                if (!plot.Visible)
                {
                    continue;
                }

                int boundIndex = isX ? plot.XAxisIndex : plot.YAxisIndex;
                if (boundIndex != i)
                {
                    continue;
                }

                DataRange plotBounds = isX ? plot.GetXDataBounds() : plot.GetYDataBounds();
                if (!plotBounds.IsEmpty)
                {
                    bounds = bounds.Union(plotBounds);
                }
            }

            axis.DataBounds = bounds;

            if (axis.AutoScale)
            {
                DataRange fitted = bounds.IsEmpty ? DataRange.Unit : bounds.EnsureValid();
                if (_autoScalePadding > 0 && fitted.IsValid)
                {
                    fitted = ExpandForScale(fitted, axis.Scale, _autoScalePadding);
                }

                axis.Range = fitted;
            }
        }
    }

    /// <summary>
    /// Expands a fitted range by a fraction of its span for auto-scale padding. On a logarithmic axis
    /// the padding is applied in log space (a fraction of the decade span) so a small positive minimum
    /// is not driven to or below zero, which would collapse the visible range.
    /// </summary>
    private static DataRange ExpandForScale(DataRange range, AxisScaleType scale, double fraction)
    {
        if (scale == AxisScaleType.Logarithmic && range.Min > 0 && range.Max > 0)
        {
            double logMin = System.Math.Log10(range.Min);
            double logMax = System.Math.Log10(range.Max);
            double pad = (logMax - logMin) * fraction;
            return new DataRange(System.Math.Pow(10, logMin - pad), System.Math.Pow(10, logMax + pad));
        }

        return range.Expand(fraction);
    }
}
