using System.ComponentModel;
using JGraph.Core.Drawing;

namespace JGraph.Core.Model;

/// <summary>Where a legend is anchored within (or beside) its axes.</summary>
public enum LegendPosition
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
    Top,
    Bottom,
    Right,
    Left,
}

/// <summary>
/// The legend of an <see cref="AxesModel"/>. In this milestone it stores placement and styling; the
/// renderer builds its entries automatically from the plot objects' display names. Legends are
/// hidden by default and shown via the API (for example <c>JG.Legend()</c>).
/// </summary>
public sealed class LegendModel : GraphObject
{
    private LegendPosition _position = LegendPosition.TopRight;
    private Color _background = Colors.White.WithOpacity(0.85);
    private Color _borderColor = Colors.Gray;
    private bool _showBorder = true;
    private TextStyle _textStyle = new(Colors.Black, 11);
    private string? _title;

    public LegendModel()
    {
        Name = "Legend";
        Visible = false;
    }

    [Category("Appearance")]
    public LegendPosition Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Layout);
    }

    [Category("Appearance")]
    public Color Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Border color")]
    public Color BorderColor
    {
        get => _borderColor;
        set => SetProperty(ref _borderColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Show border")]
    public bool ShowBorder
    {
        get => _showBorder;
        set => SetProperty(ref _showBorder, value, InvalidationKind.Render);
    }

    public TextStyle TextStyle
    {
        get => _textStyle;
        set => SetProperty(ref _textStyle, value, InvalidationKind.Layout);
    }

    [Category("General")]
    public string? Title
    {
        get => _title;
        set => SetProperty(ref _title, value, InvalidationKind.Layout);
    }
}
