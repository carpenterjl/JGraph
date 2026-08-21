using System.ComponentModel;
using JGraph.Core.Drawing;

namespace JGraph.Core.Model;

/// <summary>
/// The grid lines of an <see cref="AxesModel"/>. Major grid lines coincide with major ticks; minor
/// grid lines coincide with minor ticks. Grid drawing itself is performed by the renderer using the
/// tick positions and these style settings. Visibility is carried per direction, as MATLAB's
/// <c>XGrid</c>/<c>YGrid</c>/<c>ZGrid</c> and their minor twins are; the appearance (one style for
/// the major lines, one for the minor) is shared by every direction, which is also MATLAB's shape.
/// </summary>
public sealed class GridModel : GraphObject
{
    private bool _showMajorX = true;
    private bool _showMajorY = true;
    private bool _showMajorZ = true;
    private bool _showMinorX;
    private bool _showMinorY;
    private bool _showMinorZ;
    private LineStyle _majorLineStyle = new(Colors.LightGray, 1.0);
    private LineStyle _minorLineStyle = new(Color.FromRgb(0xEC, 0xEC, 0xEC), 0.5);

    public GridModel()
    {
        Name = "Grid";
    }

    /// <summary>
    /// Whether major grid lines are drawn at all — true when any direction shows them. Setting it
    /// speaks for every direction at once, which is what <c>grid on</c> means.
    /// </summary>
    [Category("Appearance"), DisplayName("Show major lines")]
    public bool ShowMajor
    {
        get => _showMajorX || _showMajorY || _showMajorZ;
        set
        {
            ShowMajorX = value;
            ShowMajorY = value;
            ShowMajorZ = value;
        }
    }

    /// <summary>Whether minor grid lines are drawn at all; setting it speaks for every direction.</summary>
    [Category("Appearance"), DisplayName("Show minor lines")]
    public bool ShowMinor
    {
        get => _showMinorX || _showMinorY || _showMinorZ;
        set
        {
            ShowMinorX = value;
            ShowMinorY = value;
            ShowMinorZ = value;
        }
    }

    /// <summary>Major grid lines at the X ticks (MATLAB <c>XGrid</c>): the vertical lines.</summary>
    [Browsable(false)]
    public bool ShowMajorX
    {
        get => _showMajorX;
        set => SetProperty(ref _showMajorX, value, InvalidationKind.Render);
    }

    /// <summary>Major grid lines at the Y ticks (MATLAB <c>YGrid</c>): the horizontal lines.</summary>
    [Browsable(false)]
    public bool ShowMajorY
    {
        get => _showMajorY;
        set => SetProperty(ref _showMajorY, value, InvalidationKind.Render);
    }

    /// <summary>Major grid lines at the Z ticks (MATLAB <c>ZGrid</c>); consulted only in 3D.</summary>
    [Browsable(false)]
    public bool ShowMajorZ
    {
        get => _showMajorZ;
        set => SetProperty(ref _showMajorZ, value, InvalidationKind.Render);
    }

    /// <summary>Minor grid lines at the X minor ticks (MATLAB <c>XMinorGrid</c>).</summary>
    [Browsable(false)]
    public bool ShowMinorX
    {
        get => _showMinorX;
        set => SetProperty(ref _showMinorX, value, InvalidationKind.Render);
    }

    /// <summary>Minor grid lines at the Y minor ticks (MATLAB <c>YMinorGrid</c>).</summary>
    [Browsable(false)]
    public bool ShowMinorY
    {
        get => _showMinorY;
        set => SetProperty(ref _showMinorY, value, InvalidationKind.Render);
    }

    /// <summary>Minor grid lines at the Z minor ticks (MATLAB <c>ZMinorGrid</c>); consulted only in 3D.</summary>
    [Browsable(false)]
    public bool ShowMinorZ
    {
        get => _showMinorZ;
        set => SetProperty(ref _showMinorZ, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Major lines")]
    public LineStyle MajorLineStyle
    {
        get => _majorLineStyle;
        set => SetProperty(ref _majorLineStyle, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Minor lines")]
    public LineStyle MinorLineStyle
    {
        get => _minorLineStyle;
        set => SetProperty(ref _minorLineStyle, value, InvalidationKind.Render);
    }

    /// <summary>
    /// True once a script has chosen the major line color (MATLAB <c>GridColorMode</c> 'manual').
    /// A theme pass leaves a manual color alone and rewrites an automatic one, which is the whole
    /// observable difference between the two modes.
    /// </summary>
    [Browsable(false)]
    public bool MajorColorManual { get; set; }

    /// <summary>True once a script has chosen the minor line color (MATLAB <c>MinorGridColorMode</c>).</summary>
    [Browsable(false)]
    public bool MinorColorManual { get; set; }

    /// <summary>True once a script has chosen the major line opacity (MATLAB <c>GridAlphaMode</c>).</summary>
    [Browsable(false)]
    public bool MajorAlphaManual { get; set; }

    /// <summary>True once a script has chosen the minor line opacity (MATLAB <c>MinorGridAlphaMode</c>).</summary>
    [Browsable(false)]
    public bool MinorAlphaManual { get; set; }
}
