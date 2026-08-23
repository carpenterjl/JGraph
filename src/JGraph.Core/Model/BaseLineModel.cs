using System.ComponentModel;
using JGraph.Core.Drawing;

namespace JGraph.Core.Model;

/// <summary>
/// The line a bar, a stem or an area stands on. MATLAB gives every one of those three a
/// <c>BaseLine</c> object of its own — a real handle with its own colour, width and dash — rather
/// than a colour property on the chart, and this is that object.
/// <para>
/// It owns the base value too, so the chart's own <c>BaseValue</c> and this object's cannot drift
/// apart: there is one number and two spellings of it, which is the same arrangement the axes and
/// its rulers have had since M51.
/// </para>
/// </summary>
public sealed class BaseLineModel : GraphObject
{
    private double _baseValue;
    private Color? _color;
    private double _lineWidth = 0.5;
    private DashStyle _lineStyle = DashStyle.Solid;

    public BaseLineModel() => Name = "Baseline";

    /// <summary>Where the line sits, and where the bars it belongs to grow from.</summary>
    [Category("Appearance"), DisplayName("Base value")]
    public double BaseValue
    {
        get => _baseValue;
        set => SetProperty(ref _baseValue, value, InvalidationKind.Data);
    }

    /// <summary>The line's colour, or null to draw it in the chart's own edge ink.</summary>
    [Category("Appearance")]
    public Color? Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>How the line is dashed.</summary>
    [Category("Appearance"), DisplayName("Line style")]
    public DashStyle LineStyle
    {
        get => _lineStyle;
        set => SetProperty(ref _lineStyle, value, InvalidationKind.Render);
    }
}
