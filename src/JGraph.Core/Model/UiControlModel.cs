using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>MATLAB's ten <c>uicontrol</c> styles, in R2025b's order.</summary>
public enum UiControlStyle
{
    PushButton,
    ToggleButton,
    RadioButton,
    CheckBox,
    Edit,
    Text,
    Slider,
    Frame,
    ListBox,
    PopupMenu,
}

/// <summary>How a <c>uicontrol</c> sets its text within its box (<c>HorizontalAlignment</c>).</summary>
public enum UiHorizontalAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>The units a <c>uicontrol</c>'s <c>FontSize</c> is counted in (<c>FontUnits</c>).</summary>
public enum UiFontUnits
{
    Inches,
    Centimeters,
    Normalized,
    Points,
    Pixels,
}

/// <summary>A numeric array a component holds as given — a <c>uicontrol</c>'s <c>Value</c>.</summary>
public sealed class UiNumbers
{
    /// <summary>The scalar zero, every style's default but the list ones.</summary>
    public static readonly UiNumbers Zero = new([0], 1, 1);

    public UiNumbers(IReadOnlyList<double> data, int rows, int columns)
    {
        Data = data;
        Rows = rows;
        Columns = columns;
    }

    /// <summary>The elements in column-major order.</summary>
    public IReadOnlyList<double> Data { get; }

    public int Rows { get; }

    public int Columns { get; }
}

/// <summary>
/// A picture a component shows — a button's <c>CData</c> — as the window draws it: rows top to
/// bottom, four bytes a pixel in blue, green, red, alpha order. A NaN pixel of MATLAB's is one with
/// no alpha.
/// </summary>
public sealed record UiImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// MATLAB's <c>uicontrol</c> (<c>matlab.ui.control.UIControl</c>): one classic component of ten
/// styles, each realised in the window (app-building plan, U1 and U3). The model holds R2025b's
/// whole property surface for every style, so a script can build any of them and read back what it
/// set.
/// </summary>
public sealed class UiControlModel : UiObject
{
    /// <summary>R2025b's grey for every style but <c>edit</c>, <c>listbox</c> and <c>popupmenu</c>, 245/255.</summary>
    public static readonly UiColor DefaultBackground = new(245 / 255.0, 245 / 255.0, 245 / 255.0);

    /// <summary>R2025b's text colour for a classic control, 33/255.</summary>
    public static readonly UiColor DefaultForeground = new(33 / 255.0, 33 / 255.0, 33 / 255.0);

    private UiControlStyle _style = UiControlStyle.PushButton;
    private UiText _text = UiText.Empty;
    private UiNumbers _value = UiNumbers.Zero;
    private bool _valueSet;
    private double _min;
    private double _max = 1;
    private double _listboxTop = 1;
    private double _sliderStepSmall = 0.01;
    private double _sliderStepLarge = 0.1;
    private UiHorizontalAlignment _alignment = UiHorizontalAlignment.Center;
    private UiColor? _background;
    private bool _backgroundSet;
    private UiColor? _foreground = DefaultForeground;
    private string _fontName = "MS Sans Serif";
    private double _fontSize = 8;
    private UiFontUnits _fontUnits = UiFontUnits.Points;
    private string _fontWeight = "normal";
    private string _fontAngle = "normal";
    private long _userWriteSeq;
    private UiImage? _image;
    private int _focusRequests;

    public UiControlModel()
    {
        Name = "UIControl";
    }

    [Browsable(false)]
    public UiControlStyle Style
    {
        get => _style;
        set
        {
            // A control that becomes a list while its Value is untouched takes 1, the first item,
            // and keeps it whatever it becomes next (R2025b, probe u3_styles).
            if (!_valueSet && value is UiControlStyle.ListBox or UiControlStyle.PopupMenu)
            {
                _value = One;
                _valueSet = true;
            }

            SetProperty(ref _style, value, InvalidationKind.Ui);
        }
    }

    /// <summary>MATLAB's <c>String</c>: the label, the edit field's contents, a list's items.</summary>
    [Browsable(false)]
    public UiText Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? UiText.Empty, InvalidationKind.Ui);
    }

    /// <summary>
    /// MATLAB's <c>Value</c>. Until a script sets it, it follows the style: 1 for a list or a pop-up
    /// menu, which select their first item, and 0 for the rest (R2025b, research B).
    /// </summary>
    [Browsable(false)]
    public UiNumbers Value
    {
        get => _valueSet ? _value
            : _style is UiControlStyle.ListBox or UiControlStyle.PopupMenu ? One : UiNumbers.Zero;
        set
        {
            _valueSet = true;
            SetProperty(ref _value, value ?? UiNumbers.Zero, InvalidationKind.Ui);
            Invalidate(InvalidationKind.Ui);
        }
    }

    private static readonly UiNumbers One = new([1], 1, 1);

    /// <summary>Whether a script set <see cref="Value"/>, rather than it following the style.</summary>
    [Browsable(false)]
    public bool ValueIsSet => _valueSet;

    /// <summary>Whether a script set <see cref="BackgroundColor"/>, rather than it following the style.</summary>
    [Browsable(false)]
    public bool BackgroundIsSet => _backgroundSet;

    [Browsable(false)]
    public double Min
    {
        get => _min;
        set => SetProperty(ref _min, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Max
    {
        get => _max;
        set => SetProperty(ref _max, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double ListboxTop
    {
        get => _listboxTop;
        set => SetProperty(ref _listboxTop, value, InvalidationKind.Ui);
    }

    /// <summary>The slider's arrow step, a fraction of its range.</summary>
    [Browsable(false)]
    public double SliderStepSmall
    {
        get => _sliderStepSmall;
        set => SetProperty(ref _sliderStepSmall, value, InvalidationKind.Ui);
    }

    /// <summary>The slider's trough step, a fraction of its range.</summary>
    [Browsable(false)]
    public double SliderStepLarge
    {
        get => _sliderStepLarge;
        set => SetProperty(ref _sliderStepLarge, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _alignment;
        set => SetProperty(ref _alignment, value, InvalidationKind.Ui);
    }

    /// <summary>
    /// MATLAB's <c>BackgroundColor</c>, null for <c>'none'</c>. Until a script sets one it follows the
    /// style — white for an edit field, grey otherwise — and changing the style changes it; once set it
    /// stays, whatever the style becomes (measured on R2025b in U1).
    /// </summary>
    [Browsable(false)]
    public UiColor? BackgroundColor
    {
        get => _backgroundSet ? _background : StyleBackground(_style);
        set
        {
            _backgroundSet = true;
            SetProperty(ref _background, value, InvalidationKind.Ui);
            Invalidate(InvalidationKind.Ui);
        }
    }

    /// <summary>MATLAB's <c>ForegroundColor</c>, null for <c>'none'</c>.</summary>
    [Browsable(false)]
    public UiColor? ForegroundColor
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string FontName
    {
        get => _fontName;
        set => SetProperty(ref _fontName, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The font size in <see cref="FontUnits"/>.</summary>
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

    /// <summary>The weight word as given: <c>normal</c> or <c>bold</c>, or the older <c>light</c> and
    /// <c>demi</c>, which R2025b still accepts and hands back.</summary>
    [Browsable(false)]
    public string FontWeight
    {
        get => _fontWeight;
        set => SetProperty(ref _fontWeight, value ?? "normal", InvalidationKind.Ui);
    }

    /// <summary>The angle word as given: <c>normal</c>, <c>italic</c>, or the older <c>oblique</c>.</summary>
    [Browsable(false)]
    public string FontAngle
    {
        get => _fontAngle;
        set => SetProperty(ref _fontAngle, value ?? "normal", InvalidationKind.Ui);
    }

    /// <summary>
    /// The sequence number of the last user edit written here — what the window compares with the
    /// edit it sent, so that a frame taken before the edit reached the model does not put the old
    /// text back into a field the user just typed in.
    /// </summary>
    [Browsable(false)]
    public long UserWriteSeq
    {
        get => _userWriteSeq;
        set => SetProperty(ref _userWriteSeq, value, InvalidationKind.Ui);
    }

    /// <summary>
    /// How many times a script has asked for the keyboard to go to this control —
    /// <c>uicontrol(h)</c>. The window gives it the keyboard when the count it sees has grown.
    /// </summary>
    [Browsable(false)]
    public int FocusRequests => _focusRequests;

    /// <summary>Asks the window to give this control the keyboard.</summary>
    public void RequestFocus()
    {
        _focusRequests++;
        Invalidate(InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>CData</c> as a picture to draw on the control's face, or null for none.</summary>
    [Browsable(false)]
    public UiImage? Image
    {
        get => _image;
        set => SetProperty(ref _image, value, InvalidationKind.Ui);
    }

    /// <summary>
    /// Whether the button group this control sits in watches its <c>Value</c>: decided when the
    /// control joins the group (see <see cref="UiButtonGroupModel.Added"/>), and kept through a later
    /// change of style, as R2025b keeps it.
    /// </summary>
    [Browsable(false)]
    public bool GroupManaged { get; set; }

    /// <summary>Whether <c>Max - Min</c> exceeds one: a multi-line edit field, a multiple-selection list.</summary>
    [Browsable(false)]
    public bool IsMultiple => _max - _min > 1;

    /// <summary>
    /// MATLAB's <c>Extent</c> in pixels: the size of the control's text, with R2025b's margins and in
    /// R2025b's whole points (probes <c>u3_extent</c>, <c>u3_metrics</c>) — the widest line plus 4
    /// points, the lines' height plus 6 points, each rounded to a point. A <c>text</c> control, and
    /// any style whose <c>Max - Min</c> exceeds one, measures every line; the rest measure the first.
    /// No lines at all is 4 by 6 points, and an empty line measures as a space. The text itself is
    /// measured in the font the window draws.
    /// </summary>
    public Size2D ExtentPixels()
    {
        const double PixelsPerPoint = 96.0 / 72;
        IReadOnlyList<string> lines = _text.Lines;
        int count = lines.Count == 0 ? 0 : _style == UiControlStyle.Text || IsMultiple ? lines.Count : 1;
        if (count == 0)
        {
            return new Size2D(4 * PixelsPerPoint, 6 * PixelsPerPoint);
        }

        double size = FontSizeInPixels(PixelPosition().Height);
        bool bold = _fontWeight is "bold" or "demi";
        bool italic = _fontAngle is "italic" or "oblique";
        double widest = 0;
        double line = 0;
        for (int i = 0; i < count; i++)
        {
            (double width, double height) = UiFonts.Measure(
                lines[i].Length == 0 ? " " : lines[i], _fontName, size, bold, italic);
            widest = System.Math.Max(widest, width);
            line = height;
        }

        double widthPoints = System.Math.Round((widest / PixelsPerPoint) + 4, MidpointRounding.AwayFromZero);
        double heightPoints = System.Math.Round((count * line / PixelsPerPoint) + 6, MidpointRounding.AwayFromZero);
        return new Size2D(widthPoints * PixelsPerPoint, heightPoints * PixelsPerPoint);
    }

    /// <summary>The font size in pixels of 1/96 inch, for drawing.</summary>
    public double FontSizeInPixels(double controlHeight) => _fontUnits switch
    {
        UiFontUnits.Points => _fontSize * 96 / 72,
        UiFontUnits.Inches => _fontSize * 96,
        UiFontUnits.Centimeters => _fontSize * 96 / 2.54,
        UiFontUnits.Normalized => _fontSize * controlHeight,
        _ => _fontSize,
    };

    private static UiColor StyleBackground(UiControlStyle style) =>
        style is UiControlStyle.Edit or UiControlStyle.ListBox or UiControlStyle.PopupMenu
            ? new UiColor(1, 1, 1)
            : DefaultBackground;
}
