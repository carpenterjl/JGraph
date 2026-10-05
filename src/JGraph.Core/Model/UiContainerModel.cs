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
public class UiPanelModel : UiContainerModel
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
    /// How far a grid placed in this panel stands in from each edge, once R2025b's layout has
    /// settled (probe <c>u5_grid</c>). In a <c>uifigure</c> a title takes a line of its font and 6
    /// pixels, and the border a pixel on every side; elsewhere it is <see cref="Insets"/>.
    /// </summary>
    public Thickness GridInsets()
    {
        Thickness inset = Insets();
        if (Figure is not { IsUiFigure: true } || _borderType == UiBorderType.None || !HasTitle)
        {
            return inset;
        }

        bool bottomTitle = _titlePosition is UiTitlePosition.LeftBottom or UiTitlePosition.CenterBottom or UiTitlePosition.RightBottom;
        double strip = UiFit.LineHeight(FontSizeInPixels()) + 6;
        return bottomTitle
            ? new Thickness(inset.Left, inset.Left, inset.Right, strip)
            : new Thickness(inset.Left, strip, inset.Right, inset.Left);
    }

    /// <summary>The size of the area a grid placed in this panel fills.</summary>
    public Size2D GridArea()
    {
        Rect2D outer = PixelPosition();
        Thickness inset = GridInsets();
        return new Size2D(
            System.Math.Max(0, outer.Width - inset.Left - inset.Right),
            System.Math.Max(0, outer.Height - inset.Top - inset.Bottom));
    }

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

/// <summary>
/// MATLAB's <c>uibuttongroup</c> (<c>matlab.ui.container.ButtonGroup</c>): a panel that keeps at
/// most one of the radio buttons and toggle buttons placed directly in it selected (app-building
/// plan, U3). Its rules are R2025b's, measured headless (probes <c>u3_bgroup</c>, <c>u3_more</c>):
/// <list type="bullet">
/// <item>it watches a button that was a radio button or a toggle button, with a <c>Value</c> at its
/// <c>Min</c> or its <c>Max</c>, when it joined the group — by being made in it or moved into it;</item>
/// <item>the first such button to join is selected, and so is one that joins with its <c>Value</c>
/// at its <c>Max</c>;</item>
/// <item>writing a <c>Value</c> that starts with 1 to a watched button selects it, and writing 0
/// to the selected one leaves nothing selected;</item>
/// <item>selecting writes 1 into the button and 0 into the one that was selected, whatever their
/// <c>Min</c> and <c>Max</c>.</item>
/// </list>
/// </summary>
/// <summary>
/// The bar a <c>waitbar</c> fills (app-building plan, U4): R2025b's
/// <c>matlab.ui.control.internal.ProgressIndicator</c>, type <c>uiprogressindicator</c>. It is a
/// borderless box in pixels whose left part, <see cref="Value"/> of its width, is drawn in
/// <see cref="ProgressColor"/>. It is drawn the way a panel is, so it appears wherever panels do.
/// </summary>
public sealed class UiProgressIndicatorModel : UiPanelModel
{
    private double _value;
    private bool _indeterminate;
    private UiColor _progressColor = new(38 / 255.0, 140 / 255.0, 221 / 255.0);

    public UiProgressIndicatorModel()
    {
        Name = "ProgressIndicator";
        Units = UiUnits.Pixels;
        BorderType = UiBorderType.None;
        BackgroundColor = new UiColor(0.82, 0.82, 0.82);
    }

    /// <summary>How far along, from 0 to 1.</summary>
    [Browsable(false)]
    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, double.IsNaN(value) ? 0 : System.Math.Clamp(value, 0, 1), InvalidationKind.Ui);
    }

    /// <summary>Whether the bar shows activity of no known length.</summary>
    [Browsable(false)]
    public bool Indeterminate
    {
        get => _indeterminate;
        set => SetProperty(ref _indeterminate, value, InvalidationKind.Ui);
    }

    /// <summary>The colour of the filled part.</summary>
    [Browsable(false)]
    public UiColor ProgressColor
    {
        get => _progressColor;
        set => SetProperty(ref _progressColor, value, InvalidationKind.Ui);
    }
}

public sealed class UiButtonGroupModel : UiPanelModel
{
    private static readonly UiColor White = new(1, 1, 1);
    private static readonly UiNumbers One = new([1], 1, 1);
    private UiControlModel? _selected;

    public UiButtonGroupModel()
    {
        Name = "ButtonGroup";

        // A classic figure's group draws its line white, where a panel's is grey (R2025b).
        BorderColor = White;
        HighlightColor = White;
    }

    /// <summary>A button group with the defaults one made in a <c>uifigure</c> has.</summary>
    public static new UiButtonGroupModel ForUiFigure() => new()
    {
        Units = UiUnits.Pixels,
        Position = new Rect2D(20, 20, 260, 210),
        FontUnits = UiFontUnits.Pixels,
        FontSize = 12,
        FontName = "Helvetica",
        AutoResizeChildren = true,
        BorderColor = DefaultBorder,
        HighlightColor = DefaultBorder,
    };

    /// <summary>MATLAB's <c>SelectedObject</c>: the selected button, or null for none.</summary>
    [Browsable(false)]
    public UiControlModel? SelectedObject =>
        _selected is { } selected && ReferenceEquals(selected.Parent, this) ? selected : null;

    /// <summary>Whether a control is of a style a group can select.</summary>
    public static bool IsButton(UiControlModel control) =>
        control.Style is UiControlStyle.RadioButton or UiControlStyle.ToggleButton;

    /// <summary>
    /// Selects a button, or nothing: 1 goes into it and 0 into the one that was selected. The caller
    /// has checked that it is a button of this group.
    /// </summary>
    public void Select(UiControlModel? button)
    {
        UiControlModel? old = SelectedObject;
        if (old is not null && !ReferenceEquals(old, button))
        {
            old.Value = UiNumbers.Zero;
        }

        _selected = button;
        if (button is not null)
        {
            button.Value = One;
        }

        Invalidate(InvalidationKind.Ui);
    }

    /// <summary>
    /// A control has joined the group — made in it, with its options applied, or moved into it.
    /// Decides whether the group watches it, and selects it when it is the first watched button or
    /// arrives with its <c>Value</c> at its <c>Max</c>.
    /// </summary>
    public void Added(UiControlModel control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.GroupManaged = false;
        if (!IsButton(control) || control.Value.Data.Count != 1)
        {
            return;
        }

        double value = control.Value.Data[0];
        bool atMax = value == control.Max;
        if (!atMax && value != control.Min)
        {
            return;
        }

        bool first = true;
        foreach (UiObject other in Components)
        {
            if (other is UiControlModel { GroupManaged: true } && !ReferenceEquals(other, control))
            {
                first = false;
                break;
            }
        }

        control.GroupManaged = true;
        if (atMax || first)
        {
            Select(control);
        }
    }

    /// <summary>
    /// A watched control's <c>Value</c> was written. Answers false when the write asks for a
    /// selection the control's style no longer allows — R2025b refuses that after the value is in.
    /// </summary>
    public bool ValueWritten(UiControlModel control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!control.GroupManaged || !ReferenceEquals(control.Parent, this))
        {
            return true;
        }

        IReadOnlyList<double> data = control.Value.Data;
        if (data.Count > 0 && data[0] == 1)
        {
            if (!IsButton(control))
            {
                return false;
            }

            Select(control);
        }
        else if (ReferenceEquals(SelectedObject, control) && data.Count == 1 && data[0] == 0)
        {
            _selected = null;
            Invalidate(InvalidationKind.Ui);
        }

        return true;
    }

    /// <summary>A control is leaving the group for another parent: the selected one is deselected.</summary>
    public void Leaving(UiControlModel control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (ReferenceEquals(_selected, control))
        {
            _selected = null;
            control.Value = UiNumbers.Zero;
        }

        control.GroupManaged = false;
    }

    /// <summary>
    /// Restores a selection read from a document or carried by a copy, writing nothing into the
    /// buttons: their values were saved with them.
    /// </summary>
    public void RestoreSelection(UiControlModel? button) => _selected = button;
}
