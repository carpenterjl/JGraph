using System.ComponentModel;
using JGraph.Core.Drawing;

namespace JGraph.Core.Model;

/// <summary>
/// The colorbar of an <see cref="AxesModel"/> — a vertical gradient strip beside the plot area that
/// legends the colormap of the axes' first color-mapped plot (image, surface, or contour). Hidden by
/// default and shown via the API (for example <c>JG.Colorbar()</c>). Like the legend, this model only
/// stores placement/styling; the renderer reads the colormap and range from the plots.
/// </summary>
public sealed class ColorbarModel : GraphObject
{
    private double _width = 18;
    private string? _label;
    private TextStyle _tickLabelStyle = new(Colors.DarkGray, 11);

    public ColorbarModel()
    {
        Name = "Colorbar";
        Visible = false;
    }

    /// <summary>The width of the gradient strip in pixels.</summary>
    [Category("Appearance")]
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, System.Math.Max(4, value), InvalidationKind.Layout);
    }

    /// <summary>An optional label drawn alongside the colorbar.</summary>
    [Category("General")]
    public string? Label
    {
        get => _label;
        set => SetProperty(ref _label, value, InvalidationKind.Layout);
    }

    /// <summary>The style of the value labels beside the strip.</summary>
    public TextStyle TickLabelStyle
    {
        get => _tickLabelStyle;
        set => SetProperty(ref _tickLabelStyle, value, InvalidationKind.Layout);
    }
}
