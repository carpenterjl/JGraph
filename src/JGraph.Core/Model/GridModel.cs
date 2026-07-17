using System.ComponentModel;
using JGraph.Core.Drawing;

namespace JGraph.Core.Model;

/// <summary>
/// The grid lines of an <see cref="AxesModel"/>. Major grid lines coincide with major ticks; minor
/// grid lines coincide with minor ticks. Grid drawing itself is performed by the renderer using the
/// tick positions and these style settings.
/// </summary>
public sealed class GridModel : GraphObject
{
    private bool _showMajor = true;
    private bool _showMinor;
    private LineStyle _majorLineStyle = new(Colors.LightGray, 1.0);
    private LineStyle _minorLineStyle = new(Color.FromRgb(0xEC, 0xEC, 0xEC), 0.5);

    public GridModel()
    {
        Name = "Grid";
    }

    [Category("Appearance"), DisplayName("Show major lines")]
    public bool ShowMajor
    {
        get => _showMajor;
        set => SetProperty(ref _showMajor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Show minor lines")]
    public bool ShowMinor
    {
        get => _showMinor;
        set => SetProperty(ref _showMinor, value, InvalidationKind.Render);
    }

    public LineStyle MajorLineStyle
    {
        get => _majorLineStyle;
        set => SetProperty(ref _majorLineStyle, value, InvalidationKind.Render);
    }

    public LineStyle MinorLineStyle
    {
        get => _minorLineStyle;
        set => SetProperty(ref _minorLineStyle, value, InvalidationKind.Render);
    }
}
