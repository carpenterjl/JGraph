using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// What holds components: a figure, or a container inside one (app-building plan, section B).
/// </summary>
public interface IUiContainer
{
    /// <summary>The components directly inside, in creation order — back to front.</summary>
    GraphObjectCollection<UiObject> Components { get; }

    /// <summary>The size in pixels of the area children are placed in.</summary>
    Size2D InnerPixelSize { get; }

    /// <summary>MATLAB's <c>AutoResizeChildren</c>.</summary>
    bool AutoResizeChildren { get; set; }
}

/// <summary>
/// A component that holds others: the base of <c>uipanel</c> and, later, button groups and tabs. Its
/// children are placed in its inner area, which is its own rectangle less whatever border and title
/// it draws. Axes placed in it stay in their figure's list — the renderer, the document format and
/// every drawing verb know an axes as a figure's — and name it as their <see cref="AxesModel.Container"/>.
/// </summary>
public abstract class UiContainerModel : UiObject, IUiContainer
{
    private bool _autoResizeChildren;
    private bool _scrollable;

    protected UiContainerModel()
    {
        Components = new GraphObjectCollection<UiObject>(this);
    }

    /// <inheritdoc />
    [Browsable(false)]
    public GraphObjectCollection<UiObject> Components { get; }

    /// <inheritdoc />
    [Browsable(false)]
    public bool AutoResizeChildren
    {
        get => _autoResizeChildren;
        set => SetProperty(ref _autoResizeChildren, value, InvalidationKind.None);
    }

    /// <summary>MATLAB's <c>Scrollable</c>. Kept; nothing scrolls until the grid stage (U5).</summary>
    [Browsable(false)]
    public bool Scrollable
    {
        get => _scrollable;
        set => SetProperty(ref _scrollable, value, InvalidationKind.Ui);
    }

    /// <summary>How far the inner area stands in from each edge, in pixels.</summary>
    public abstract Thickness Insets();

    /// <summary>
    /// The inner area as MATLAB's pixel rectangle in the parent's coordinates — what
    /// <c>InnerPosition</c> answers in pixels.
    /// </summary>
    public Rect2D InnerPixelRect()
    {
        Rect2D outer = PixelPosition();
        Thickness inset = Insets();
        return new Rect2D(
            outer.X + inset.Left,
            outer.Y + inset.Bottom,
            System.Math.Max(0, outer.Width - inset.Left - inset.Right),
            System.Math.Max(0, outer.Height - inset.Top - inset.Bottom));
    }

    /// <inheritdoc />
    [Browsable(false)]
    public Size2D InnerPixelSize
    {
        get
        {
            Rect2D inner = InnerPixelRect();
            return new Size2D(inner.Width, inner.Height);
        }
    }

    /// <summary>The axes placed in this container, in the order their figure holds them.</summary>
    public IReadOnlyList<AxesModel> ContainedAxes()
    {
        var found = new List<AxesModel>();
        if (Figure is { } figure)
        {
            foreach (AxesModel axes in figure.Axes)
            {
                if (ReferenceEquals(axes.Container, this))
                {
                    found.Add(axes);
                }
            }
        }

        return found;
    }

    /// <summary>Whether <paramref name="other"/> is this container or sits somewhere inside it.</summary>
    public bool Holds(GraphObject other)
    {
        for (GraphObject? up = other; up is not null; up = up is AxesModel { Container: { } held } ? held : up.Parent)
        {
            if (ReferenceEquals(up, this))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>MATLAB's six <c>BorderType</c> words; R2025b lists the first two and warns on the rest.</summary>
public enum UiBorderType
{
    None,
    Line,
    EtchedIn,
    EtchedOut,
    BeveledIn,
    BeveledOut,
}

/// <summary>MATLAB's six <c>TitlePosition</c> words, in R2025b's order.</summary>
public enum UiTitlePosition
{
    LeftTop,
    CenterTop,
    RightTop,
    LeftBottom,
    CenterBottom,
    RightBottom,
}

/// <summary>
/// MATLAB's <c>uipanel</c> (<c>matlab.ui.container.Panel</c>): a bordered, optionally titled area
/// that holds components and axes. Its defaults are a classic figure's; <see cref="ForUiFigure"/>
/// gives the ones a panel made in a <c>uifigure</c> starts with (R2025b, probe <c>u2_panel</c>).
/// </summary>
public sealed class UiPanelModel : UiContainerModel
{
    /// <summary>R2025b's panel grey, 245/255.</summary>
    public static readonly UiColor DefaultBackground = new(245 / 255.0, 245 / 255.0, 245 / 255.0);

    /// <summary>R2025b's border grey, 125/255.</summary>
    public static readonly UiColor DefaultBorder = new(125 / 255.0, 125 / 255.0, 125 / 255.0);

    private UiText _title = UiText.Empty;
    private UiTitlePosition _titlePosition = UiTitlePosition.LeftTop;
    private UiBorderType _borderType = UiBorderType.Line;
    private double _borderWidth = 1;
    private UiColor _background = DefaultBackground;
    private UiColor? _foreground = UiControlModel.DefaultForeground;
    private UiColor? _borderColor = DefaultBorder;
    private UiColor? _highlightColor = DefaultBorder;
    private UiColor _shadowColor = new(0.7, 0.7, 0.7);
    private string _fontName = "MS Sans Serif";
    private double _fontSize = 8;
    private UiFontUnits _fontUnits = UiFontUnits.Points;
    private string _fontWeight = "normal";
    private string _fontAngle = "normal";
    private bool _clipping = true;

    public UiPanelModel()
    {
        Name = "Panel";
        Units = UiUnits.Normalized;
        Position = new Rect2D(0, 0, 1, 1);
    }

    /// <summary>A panel with the defaults one made in a <c>uifigure</c> has.</summary>
    public static UiPanelModel ForUiFigure() => new()
    {
        Units = UiUnits.Pixels,
        Position = new Rect2D(20, 20, 260, 221),
        FontUnits = UiFontUnits.Pixels,
        FontSize = 12,
        FontName = "Helvetica",
        AutoResizeChildren = true,
    };

    /// <summary>MATLAB's <c>Title</c>; empty for none.</summary>
    [Browsable(false)]
    public UiText Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? UiText.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiTitlePosition TitlePosition
    {
        get => _titlePosition;
        set => SetProperty(ref _titlePosition, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiBorderType BorderType
    {
        get => _borderType;
        set => SetProperty(ref _borderType, value, InvalidationKind.Ui);
    }

    /// <summary>The border's width in pixels. R2025b takes NaN and infinity and keeps them.</summary>
    [Browsable(false)]
    public double BorderWidth
    {
        get => _borderWidth;
        set => SetProperty(ref _borderWidth, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiColor BackgroundColor
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Ui);
    }

    /// <summary>The title's colour, null for <c>'none'</c>.</summary>
    [Browsable(false)]
    public UiColor? ForegroundColor
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value, InvalidationKind.Ui);
    }

    /// <summary>The colour of a <c>line</c> border, null for <c>'none'</c>.</summary>
    [Browsable(false)]
    public UiColor? BorderColor
    {
        get => _borderColor;
        set => SetProperty(ref _borderColor, value, InvalidationKind.Ui);
    }

    /// <summary>The lit edge of an etched or bevelled border, null for <c>'none'</c>.</summary>
    [Browsable(false)]
    public UiColor? HighlightColor
    {
        get => _highlightColor;
        set => SetProperty(ref _highlightColor, value, InvalidationKind.Ui);
    }

    /// <summary>The shaded edge of an etched or bevelled border (R2025b warns that it is going).</summary>
    [Browsable(false)]
    public UiColor ShadowColor
    {
        get => _shadowColor;
        set => SetProperty(ref _shadowColor, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string FontName
    {
        get => _fontName;
        set => SetProperty(ref _fontName, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The title's font size in <see cref="FontUnits"/>.</summary>
    [Browsable(false)]
    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiFontUnits FontUnits
    {
        get => _fontUnits;
        set => SetProperty(ref _fontUnits, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string FontWeight
    {
        get => _fontWeight;
        set => SetProperty(ref _fontWeight, value ?? "normal", InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string FontAngle
    {
        get => _fontAngle;
        set => SetProperty(ref _fontAngle, value ?? "normal", InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Clipping</c>. Kept; a panel clips its children whatever it says.</summary>
    [Browsable(false)]
    public bool Clipping
    {
        get => _clipping;
        set => SetProperty(ref _clipping, value, InvalidationKind.None);
    }

    /// <summary>Whether a title is shown: any text at all.</summary>
    [Browsable(false)]
    public bool HasTitle => _title.Lines.Any(static line => line.Length > 0);

    /// <summary>The title's font size in pixels of 1/96 inch.</summary>
    public double FontSizeInPixels() => _fontUnits switch
    {
        UiFontUnits.Points => _fontSize * 96 / 72,
        UiFontUnits.Inches => _fontSize * 96,
        UiFontUnits.Centimeters => _fontSize * 96 / 2.54,
        UiFontUnits.Normalized => _fontSize * PixelPosition().Height,
        _ => _fontSize,
    };

    /// <summary>
    /// R2025b's inner area, measured in both figure kinds (probe <c>u2_panel</c>). The border takes
    /// its width from every edge — twice that when etched, nothing when <c>none</c> — and a title takes
    /// its font's pixel size, rounded, from the edge it sits on in place of the border there, whichever
    /// is larger.
    /// </summary>
    public override Thickness Insets()
    {
        double width = double.IsFinite(_borderWidth) ? System.Math.Max(0, _borderWidth) : 0;
        double border = _borderType switch
        {
            UiBorderType.None => 0,
            UiBorderType.EtchedIn or UiBorderType.EtchedOut => 2 * width,
            _ => width,
        };

        double top = border;
        double bottom = border;
        if (HasTitle)
        {
            double title = System.Math.Round(FontSizeInPixels(), MidpointRounding.AwayFromZero);
            if (_titlePosition is UiTitlePosition.LeftBottom or UiTitlePosition.CenterBottom or UiTitlePosition.RightBottom)
            {
                bottom = System.Math.Max(border, title);
            }
            else
            {
                top = System.Math.Max(border, title);
            }
        }

        return new Thickness(border, top, border, bottom);
    }
}
