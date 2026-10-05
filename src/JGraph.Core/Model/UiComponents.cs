using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>The components of a <c>uifigure</c> that U5 brings, by MATLAB class.</summary>
public enum UiComponentKind
{
    Label,
    Button,
    StateButton,
    EditField,
    NumericEditField,
    TextArea,
    DropDown,
    ListBox,
    CheckBox,
    RadioButton,
    ToggleButton,
    Slider,
    RangeSlider,
    Spinner,
    Image,
    Hyperlink,
}

/// <summary>How a component sets its content from top to bottom (<c>VerticalAlignment</c>).</summary>
public enum UiVerticalAlignment
{
    Top,
    Center,
    Bottom,
}

/// <summary>
/// One of the components MATLAB makes for a <c>uifigure</c> (app-building plan, U5): a
/// <c>uilabel</c>, a <c>uibutton</c>, a <c>uieditfield</c> and the rest. Unlike a <c>uicontrol</c>
/// each is a class of its own with its own properties, its place is always in pixels, and its text
/// is 12-pixel Helvetica. Defaults are R2025b's (research B's <c>get</c> dumps and probe
/// <c>u5_matrix</c>). The window realises it from a <see cref="UiComponentFrame"/>.
/// </summary>
public abstract class UiComponentModel : UiObject
{
    /// <summary>R2025b's text colour for a component, 33/255.</summary>
    public static readonly UiColor DefaultFontColor = new(33 / 255.0, 33 / 255.0, 33 / 255.0);

    /// <summary>R2025b's button grey, 245/255.</summary>
    public static readonly UiColor Grey = new(245 / 255.0, 245 / 255.0, 245 / 255.0);

    public static readonly UiColor White = new(1, 1, 1);

    private string _fontName = "Helvetica";
    private double _fontSize = 12;
    private bool _bold;
    private bool _italic;
    private UiColor _fontColor = DefaultFontColor;
    private UiColor? _background;
    private long _userWriteSeq;
    private int _focusRequests;

    protected UiComponentModel(string name, Rect2D position)
    {
        Name = name;
        Units = UiUnits.Pixels;
        Position = position;
    }

    /// <summary>Which of MATLAB's classes this is.</summary>
    [Browsable(false)]
    public abstract UiComponentKind Kind { get; }

    [Browsable(false)]
    public string FontName
    {
        get => _fontName;
        set => SetProperty(ref _fontName, value ?? "Helvetica", InvalidationKind.Ui);
    }

    /// <summary>The font size in pixels: a component has no <c>FontUnits</c>.</summary>
    [Browsable(false)]
    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Bold
    {
        get => _bold;
        set => SetProperty(ref _bold, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Italic
    {
        get => _italic;
        set => SetProperty(ref _italic, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiColor FontColor
    {
        get => _fontColor;
        set => SetProperty(ref _fontColor, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>BackgroundColor</c>; null is <c>'none'</c>, which only some classes take.</summary>
    [Browsable(false)]
    public UiColor? BackgroundColor
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Ui);
    }

    /// <summary>The sequence number of the last user action written here (see <see cref="UiControlModel.UserWriteSeq"/>).</summary>
    [Browsable(false)]
    public long UserWriteSeq
    {
        get => _userWriteSeq;
        set => SetProperty(ref _userWriteSeq, value, InvalidationKind.Ui);
    }

    /// <summary>How many times a script has asked for the keyboard to go here — <c>focus(h)</c>.</summary>
    [Browsable(false)]
    public int FocusRequests => _focusRequests;

    /// <summary>Asks the window to give this component the keyboard.</summary>
    public void RequestFocus()
    {
        _focusRequests++;
        Invalidate(InvalidationKind.Ui);
    }

    /// <summary>The lines of text this component shows, for measuring; empty when it shows none.</summary>
    [Browsable(false)]
    public virtual IReadOnlyList<string> ShownLines => [];

    /// <summary>What the window needs to draw this component, copied out on the script thread.</summary>
    public abstract UiComponentFrame Snapshot();

    /// <summary>The part of a snapshot every class shares.</summary>
    protected UiComponentFrame Common() => new()
    {
        Source = this,
        Kind = Kind,
        Position = PixelPosition(),
        Visible = Visible,
        Enabled = Enable == UiEnable.On,
        Tooltip = Tooltip.Joined,
        Tag = Tag ?? string.Empty,
        FontName = _fontName,
        FontSize = _fontSize,
        Bold = _bold,
        Italic = _italic,
        Foreground = _fontColor,
        Background = _background,
        UserWriteSeq = _userWriteSeq,
        FocusRequests = _focusRequests,
        Lines = ShownLines,
    };
}

/// <summary>
/// The components that show a caption: a label, the buttons, a check box, a radio button, a
/// hyperlink. <c>Text</c> reads back as it was written — a character row, or a column cell of lines.
/// </summary>
public abstract class UiCaptionModel : UiComponentModel
{
    private UiText _text;
    private bool _wordWrap;
    private UiHorizontalAlignment _horizontal;
    private UiVerticalAlignment _vertical = UiVerticalAlignment.Center;
    private string _interpreter = "none";
    private bool _value;
    private UiImage? _icon;
    private string _iconSource = string.Empty;
    private string _iconAlignment = "left";

    protected UiCaptionModel(string name, Rect2D position, string text, UiHorizontalAlignment horizontal)
        : base(name, position)
    {
        _text = UiText.Of(text);
        _horizontal = horizontal;
    }

    [Browsable(false)]
    public UiText Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? UiText.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiVerticalAlignment VerticalAlignment
    {
        get => _vertical;
        set => SetProperty(ref _vertical, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Interpreter</c> word, kept; text is drawn as written.</summary>
    [Browsable(false)]
    public string Interpreter
    {
        get => _interpreter;
        set => SetProperty(ref _interpreter, value ?? "none", InvalidationKind.Ui);
    }

    /// <summary>Whether a state button is down, a check box is ticked, a radio or toggle button is selected.</summary>
    [Browsable(false)]
    public bool Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Ui);
    }

    /// <summary>A button's picture, or null for none.</summary>
    [Browsable(false)]
    public UiImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value, InvalidationKind.Ui);
    }

    /// <summary>What <c>Icon</c> was written as when it was a file or one of the stock words.</summary>
    [Browsable(false)]
    public string IconSource
    {
        get => _iconSource;
        set => SetProperty(ref _iconSource, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>Where the icon stands by the text: left, right, center, top, bottom and the four margins.</summary>
    [Browsable(false)]
    public string IconAlignment
    {
        get => _iconAlignment;
        set => SetProperty(ref _iconAlignment, value ?? "left", InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ShownLines => _text.Lines.Count == 0 ? [string.Empty] : _text.Lines;

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        WordWrap = _wordWrap,
        Horizontal = _horizontal,
        Vertical = _vertical,
        Checked = _value,
        Image = _icon,
        Word = _iconAlignment,
        InGroup = Parent is UiButtonGroupModel,
    };
}

/// <summary>MATLAB's <c>uilabel</c> (<c>matlab.ui.control.Label</c>).</summary>
public sealed class UiLabelModel : UiCaptionModel
{
    public UiLabelModel()
        : base("Label", new Rect2D(100, 100, 31, 22), "Label", UiHorizontalAlignment.Left)
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Label;
}

/// <summary>MATLAB's <c>uibutton</c> (<c>matlab.ui.control.Button</c>), the push button.</summary>
public sealed class UiButtonModel : UiCaptionModel
{
    public UiButtonModel()
        : base("Button", new Rect2D(100, 100, 100, 22), "Button", UiHorizontalAlignment.Center)
    {
        BackgroundColor = Grey;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Button;
}

/// <summary>MATLAB's <c>uibutton(…, 'state')</c> (<c>matlab.ui.control.StateButton</c>).</summary>
public sealed class UiStateButtonModel : UiCaptionModel
{
    public UiStateButtonModel()
        : base("StateButton", new Rect2D(100, 100, 100, 22), "State Button", UiHorizontalAlignment.Center)
    {
        BackgroundColor = Grey;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.StateButton;
}

/// <summary>MATLAB's <c>uicheckbox</c> (<c>matlab.ui.control.CheckBox</c>).</summary>
public sealed class UiCheckBoxModel : UiCaptionModel
{
    public UiCheckBoxModel()
        : base("CheckBox", new Rect2D(100, 100, 84, 22), "Check Box", UiHorizontalAlignment.Left)
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.CheckBox;
}

/// <summary>MATLAB's <c>uiradiobutton</c> (<c>matlab.ui.control.RadioButton</c>), a button group's child.</summary>
public sealed class UiRadioButtonModel : UiCaptionModel
{
    public UiRadioButtonModel()
        : base("RadioButton", new Rect2D(10, 10, 91, 22), "Radio Button", UiHorizontalAlignment.Left)
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.RadioButton;
}

/// <summary>MATLAB's <c>uitogglebutton</c> (<c>matlab.ui.control.ToggleButton</c>), a button group's child.</summary>
public sealed class UiToggleButtonModel : UiCaptionModel
{
    public UiToggleButtonModel()
        : base("ToggleButton", new Rect2D(10, 10, 100, 22), "Toggle Button", UiHorizontalAlignment.Center)
    {
        BackgroundColor = Grey;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.ToggleButton;
}

/// <summary>MATLAB's <c>uihyperlink</c> (<c>matlab.ui.control.Hyperlink</c>).</summary>
public sealed class UiHyperlinkModel : UiCaptionModel
{
    private string _url = string.Empty;
    private UiColor _visited = new(133 / 255.0, 22 / 255.0, 209 / 255.0);
    private bool _wasVisited;

    public UiHyperlinkModel()
        : base("Hyperlink", new Rect2D(100, 100, 70, 22), "Hyperlink", UiHorizontalAlignment.Left)
    {
        Bold = true;
        FontColor = new UiColor(17 / 255.0, 113 / 255.0, 190 / 255.0);
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Hyperlink;

    [Browsable(false)]
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value ?? string.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiColor VisitedColor
    {
        get => _visited;
        set => SetProperty(ref _visited, value, InvalidationKind.Ui);
    }

    /// <summary>Whether the link has been followed, which draws it in <see cref="VisitedColor"/>.</summary>
    [Browsable(false)]
    public bool WasVisited
    {
        get => _wasVisited;
        set => SetProperty(ref _wasVisited, value, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => base.Snapshot() with
    {
        Accent = _visited,
        Checked = _wasVisited,
        Text = _url,
    };
}

/// <summary>MATLAB's <c>uieditfield</c> (<c>matlab.ui.control.EditField</c>): one line of text.</summary>
public sealed class UiEditFieldModel : UiComponentModel
{
    private string _value = string.Empty;
    private double _minCharacters;
    private double _maxCharacters = double.PositiveInfinity;
    private string _inputType = "text";
    private bool _editable = true;
    private string _placeholder = string.Empty;
    private UiHorizontalAlignment _horizontal = UiHorizontalAlignment.Left;

    public UiEditFieldModel()
        : base("EditField", new Rect2D(100, 100, 100, 22))
    {
        BackgroundColor = White;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.EditField;

    [Browsable(false)]
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double MinCharacters
    {
        get => _minCharacters;
        set => SetProperty(ref _minCharacters, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double MaxCharacters
    {
        get => _maxCharacters;
        set => SetProperty(ref _maxCharacters, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>InputType</c>: text, letters, digits or alphanumerics.</summary>
    [Browsable(false)]
    public string InputType
    {
        get => _inputType;
        set => SetProperty(ref _inputType, value ?? "text", InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Editable
    {
        get => _editable;
        set => SetProperty(ref _editable, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value ?? string.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Ui);
    }

    /// <summary>Whether text satisfies <see cref="InputType"/> and the character limits.</summary>
    public bool Accepts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length >= _minCharacters && text.Length <= _maxCharacters && FitsInputType(text, _inputType);
    }

    /// <summary>Whether every character of text is of the kind an input type takes.</summary>
    public static bool FitsInputType(string text, string inputType) => inputType switch
    {
        "digits" => text.All(static c => c is >= '0' and <= '9'),
        "letters" => text.All(char.IsLetter),
        "alphanumerics" => text.All(char.IsLetterOrDigit),
        _ => true,
    };

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Text = _value,
        Editable = _editable,
        Placeholder = _placeholder,
        Horizontal = _horizontal,
        Word = _inputType,
        Min = _minCharacters,
        Max = _maxCharacters,
    };
}

/// <summary>
/// What a numeric edit field and a spinner share: a number inside limits, shown through a
/// <c>sprintf</c> format. <see cref="Value"/> null is MATLAB's <c>[]</c>, which <c>AllowEmpty</c> permits.
/// The script layer formats the number and leaves the text in <see cref="DisplayText"/>.
/// </summary>
public abstract class UiNumericModel : UiComponentModel
{
    private double? _value = 0;
    private double _lower = double.NegativeInfinity;
    private double _upper = double.PositiveInfinity;
    private bool _lowerInclusive = true;
    private bool _upperInclusive = true;
    private bool _round;
    private string _format = "%11.4g";
    private bool _allowEmpty;
    private bool _editable = true;
    private string _placeholder = string.Empty;
    private string _display = "0";
    private UiHorizontalAlignment _horizontal = UiHorizontalAlignment.Right;

    protected UiNumericModel(string name)
        : base(name, new Rect2D(100, 100, 100, 22))
    {
        BackgroundColor = White;
    }

    [Browsable(false)]
    public double? Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Lower
    {
        get => _lower;
        set => SetProperty(ref _lower, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Upper
    {
        get => _upper;
        set => SetProperty(ref _upper, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool LowerInclusive
    {
        get => _lowerInclusive;
        set => SetProperty(ref _lowerInclusive, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool UpperInclusive
    {
        get => _upperInclusive;
        set => SetProperty(ref _upperInclusive, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool RoundFractionalValues
    {
        get => _round;
        set => SetProperty(ref _round, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string ValueDisplayFormat
    {
        get => _format;
        set => SetProperty(ref _format, value ?? "%11.4g", InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool AllowEmpty
    {
        get => _allowEmpty;
        set => SetProperty(ref _allowEmpty, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Editable
    {
        get => _editable;
        set => SetProperty(ref _editable, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The value as the field shows it: formatted by the script layer, trimmed of padding.</summary>
    [Browsable(false)]
    public string DisplayText
    {
        get => _display;
        set => SetProperty(ref _display, value ?? string.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Ui);
    }

    /// <summary>Whether a number lies inside the limits, counting each end as its flag says.</summary>
    public bool InRange(double number) =>
        !double.IsNaN(number)
        && (_lowerInclusive ? number >= _lower : number > _lower)
        && (_upperInclusive ? number <= _upper : number < _upper);

    /// <summary>
    /// A number as R2025b stores it while <c>RoundFractionalValues</c> is on (probe <c>u5_forms</c>):
    /// rounded half away from zero, and — when that leaves the limits — taken to the whole number
    /// inside them instead.
    /// </summary>
    public double Rounded(double number)
    {
        if (!_round || double.IsInfinity(number))
        {
            return number;
        }

        double rounded = System.Math.Round(number, MidpointRounding.AwayFromZero);
        if (InRange(rounded))
        {
            return rounded;
        }

        double down = System.Math.Floor(number);
        double up = System.Math.Ceiling(number);
        return InRange(down) ? down : InRange(up) ? up : rounded;
    }

    /// <summary>
    /// The value a change of limits leaves (probe <c>u5_forms</c>): one outside is taken to the end
    /// it passed, and an end that is not inclusive gives the whole number just inside it.
    /// </summary>
    public double Clamped(double number)
    {
        if (InRange(number))
        {
            return number;
        }

        if (number >= _upper)
        {
            return _upperInclusive ? _upper : _upper - 1;
        }

        return _lowerInclusive ? _lower : _lower + 1;
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Text = _display,
        HasNumber = _value is not null,
        Number = _value ?? 0,
        Min = _lower,
        Max = _upper,
        Editable = _editable,
        Placeholder = _placeholder,
        Horizontal = _horizontal,
    };
}

/// <summary>MATLAB's <c>uieditfield(…, 'numeric')</c> (<c>matlab.ui.control.NumericEditField</c>).</summary>
public sealed class UiNumericEditFieldModel : UiNumericModel
{
    public UiNumericEditFieldModel()
        : base("NumericEditField")
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.NumericEditField;
}

/// <summary>MATLAB's <c>uispinner</c> (<c>matlab.ui.control.Spinner</c>): a numeric field with arrows.</summary>
public sealed class UiSpinnerModel : UiNumericModel
{
    private double _step = 1;
    private string _stepClass = "double";

    public UiSpinnerModel()
        : base("Spinner")
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Spinner;

    [Browsable(false)]
    public double Step
    {
        get => _step;
        set => SetProperty(ref _step, value, InvalidationKind.Ui);
    }

    /// <summary>The numeric class <see cref="Step"/> was written in, which reads back.</summary>
    [Browsable(false)]
    public string StepClass
    {
        get => _stepClass;
        set => _stepClass = value ?? "double";
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => base.Snapshot() with { Step = _step };
}

/// <summary>MATLAB's <c>uitextarea</c> (<c>matlab.ui.control.TextArea</c>): lines of text.</summary>
public sealed class UiTextAreaModel : UiComponentModel
{
    private static readonly IReadOnlyList<string> OneEmptyLine = [string.Empty];
    private IReadOnlyList<string> _lines = OneEmptyLine;
    private bool _editable = true;
    private bool _wordWrap = true;
    private string _placeholder = string.Empty;
    private UiHorizontalAlignment _horizontal = UiHorizontalAlignment.Left;

    public UiTextAreaModel()
        : base("TextArea", new Rect2D(100, 100, 150, 60))
    {
        BackgroundColor = White;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.TextArea;

    /// <summary>MATLAB's <c>Value</c>: the lines, never fewer than one.</summary>
    [Browsable(false)]
    public IReadOnlyList<string> Lines
    {
        get => _lines;
        set => SetProperty(ref _lines, value is { Count: > 0 } ? value : OneEmptyLine, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Editable
    {
        get => _editable;
        set => SetProperty(ref _editable, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value ?? string.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Text = string.Join("\n", _lines),
        Editable = _editable,
        WordWrap = _wordWrap,
        Placeholder = _placeholder,
        Horizontal = _horizontal,
    };
}

/// <summary>
/// What a drop-down list and a list box share: items, and which of them are selected, by position.
/// MATLAB's <c>Value</c> is read off the selection — the item's text, or its <c>ItemsData</c>
/// element, which the script layer holds because it can be any value at all.
/// </summary>
public abstract class UiItemsModel : UiComponentModel
{
    private static readonly IReadOnlyList<int> First = [0];
    private IReadOnlyList<string> _items;
    private IReadOnlyList<int> _selected = First;

    protected UiItemsModel(string name, Rect2D position, IReadOnlyList<string> items)
        : base(name, position)
    {
        _items = items;
    }

    [Browsable(false)]
    public IReadOnlyList<string> Items
    {
        get => _items;
        set => SetProperty(ref _items, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>The selected items' positions, from 0, in the order they were selected.</summary>
    [Browsable(false)]
    public IReadOnlyList<int> Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value ?? [], InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ShownLines => _items;
}

/// <summary>MATLAB's <c>uidropdown</c> (<c>matlab.ui.control.DropDown</c>).</summary>
public sealed class UiDropDownModel : UiItemsModel
{
    private bool _editable;
    private string? _typed;
    private string _placeholder = string.Empty;

    public UiDropDownModel()
        : base("DropDown", new Rect2D(100, 100, 100, 22), ["Option 1", "Option 2", "Option 3", "Option 4"])
    {
        BackgroundColor = Grey;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.DropDown;

    [Browsable(false)]
    public bool Editable
    {
        get => _editable;
        set => SetProperty(ref _editable, value, InvalidationKind.Ui);
    }

    /// <summary>The text typed into an editable drop-down that is none of its items, or null.</summary>
    [Browsable(false)]
    public string? Typed
    {
        get => _typed;
        set => SetProperty(ref _typed, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Items = Items,
        Selected = Selected,
        Editable = _editable,
        Text = _typed ?? string.Empty,
        HasNumber = _typed is not null,
        Placeholder = _placeholder,
    };
}

/// <summary>MATLAB's <c>uilistbox</c> (<c>matlab.ui.control.ListBox</c>).</summary>
public sealed class UiListBoxModel : UiItemsModel
{
    private bool _multiselect;

    public UiListBoxModel()
        : base("ListBox", new Rect2D(100, 100, 100, 74), ["Item 1", "Item 2", "Item 3", "Item 4"])
    {
        BackgroundColor = White;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.ListBox;

    [Browsable(false)]
    public bool Multiselect
    {
        get => _multiselect;
        set => SetProperty(ref _multiselect, value, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Items = Items,
        Selected = Selected,
        Multi = _multiselect,
    };
}

/// <summary>
/// MATLAB's <c>uislider</c> (<c>matlab.ui.control.Slider</c>). <c>Position</c> is the track alone, 3
/// pixels thick whatever is written; the ticks and their labels lie outside it, in
/// <c>OuterPosition</c>.
/// </summary>
public class UiSliderModel : UiComponentModel
{
    private double _value;
    private double _lower;
    private double _upper = 100;
    private double _step = 0.1;
    private bool _stepManual;
    private bool _vertical;
    private IReadOnlyList<double> _majorTicks = [0, 20, 40, 60, 80, 100];
    private IReadOnlyList<double> _minorTicks = [];
    private IReadOnlyList<string> _labels = ["0", "20", "40", "60", "80", "100"];
    private bool _majorManual;
    private bool _minorManual;
    private bool _labelsManual;

    public UiSliderModel()
        : this("Slider")
    {
    }

    protected UiSliderModel(string name)
        : base(name, new Rect2D(100, 100, 150, 3))
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Slider;

    [Browsable(false)]
    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Lower
    {
        get => _lower;
        set => SetProperty(ref _lower, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Upper
    {
        get => _upper;
        set => SetProperty(ref _upper, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Step
    {
        get => _step;
        set => SetProperty(ref _step, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool StepManual
    {
        get => _stepManual;
        set => SetProperty(ref _stepManual, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Vertical
    {
        get => _vertical;
        set => SetProperty(ref _vertical, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public IReadOnlyList<double> MajorTicks
    {
        get => _majorTicks;
        set => SetProperty(ref _majorTicks, value ?? [], InvalidationKind.Ui);
    }

    [Browsable(false)]
    public IReadOnlyList<double> MinorTicks
    {
        get => _minorTicks;
        set => SetProperty(ref _minorTicks, value ?? [], InvalidationKind.Ui);
    }

    [Browsable(false)]
    public IReadOnlyList<string> MajorTickLabels
    {
        get => _labels;
        set => SetProperty(ref _labels, value ?? [], InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool MajorTicksManual
    {
        get => _majorManual;
        set => SetProperty(ref _majorManual, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool MinorTicksManual
    {
        get => _minorManual;
        set => SetProperty(ref _minorManual, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool MajorTickLabelsManual
    {
        get => _labelsManual;
        set => SetProperty(ref _labelsManual, value, InvalidationKind.Ui);
    }

    /// <summary>The track's length in pixels: its width lying down, its height standing up.</summary>
    public double TrackLength()
    {
        Rect2D box = PixelPosition();
        return _vertical ? box.Height : box.Width;
    }

    /// <summary>
    /// R2025b's <c>OuterPosition</c> for a track rectangle (probe <c>u5_forms</c>): 7 pixels to the
    /// left, 30 below, 9 to the right and 6 above when it lies down; the same corner and the two
    /// sizes exchanged when it stands up.
    /// </summary>
    public static Rect2D OuterOf(Rect2D track, bool vertical) => vertical
        ? new Rect2D(track.X - 7, track.Y - 30, track.Width + 36, track.Height + 16)
        : new Rect2D(track.X - 7, track.Y - 30, track.Width + 16, track.Height + 36);

    /// <inheritdoc />
    public override Rect2D InCell(Rect2D cell) => TrackInCell(cell, _vertical);

    /// <summary>
    /// The track of a slider that has a grid's cell: the cell is its <c>OuterPosition</c>, and the
    /// track lies 7.5 pixels in from the left and 6 below the top (probe <c>u0_grid</c>).
    /// </summary>
    public static Rect2D TrackInCell(Rect2D cell, bool vertical) => vertical
        ? new Rect2D(cell.X + 7.5, cell.Y + 8, 3, System.Math.Max(0, cell.Height - 16))
        : new Rect2D(cell.X + 7.5, cell.Y + cell.Height - 9, System.Math.Max(0, cell.Width - 16), 3);

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Number = _value,
        Min = _lower,
        Max = _upper,
        Step = _step,
        Upright = _vertical,
        MajorTicks = _majorTicks,
        MinorTicks = _minorTicks,
        TickLabels = _labels,
    };
}

/// <summary>MATLAB's <c>uislider(…, 'range')</c> (<c>matlab.ui.control.RangeSlider</c>): two thumbs.</summary>
public sealed class UiRangeSliderModel : UiSliderModel
{
    private double _high = 100;

    public UiRangeSliderModel()
        : base("RangeSlider")
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.RangeSlider;

    /// <summary>The upper thumb; <see cref="UiSliderModel.Value"/> is the lower one.</summary>
    [Browsable(false)]
    public double High
    {
        get => _high;
        set => SetProperty(ref _high, value, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => base.Snapshot() with { Number2 = _high };
}

/// <summary>MATLAB's <c>uiimage</c> (<c>matlab.ui.control.Image</c>).</summary>
public sealed class UiImageModel : UiComponentModel
{
    private UiImage? _image;
    private string _source = string.Empty;
    private string _scaleMethod = "fit";
    private UiHorizontalAlignment _horizontal = UiHorizontalAlignment.Center;
    private UiVerticalAlignment _vertical = UiVerticalAlignment.Center;
    private string _url = string.Empty;
    private string _altText = string.Empty;

    public UiImageModel()
        : base("Image", new Rect2D(100, 100, 100, 100))
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Image;

    /// <summary>The picture, decoded, or null while there is none.</summary>
    [Browsable(false)]
    public UiImage? Image
    {
        get => _image;
        set => SetProperty(ref _image, value, InvalidationKind.Ui);
    }

    /// <summary>The file <c>ImageSource</c> named, when it named one.</summary>
    [Browsable(false)]
    public string Source
    {
        get => _source;
        set => SetProperty(ref _source, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>ScaleMethod</c>: fit, fill, none, scaledown, scaleup or stretch.</summary>
    [Browsable(false)]
    public string ScaleMethod
    {
        get => _scaleMethod;
        set => SetProperty(ref _scaleMethod, value ?? "fit", InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiHorizontalAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiVerticalAlignment VerticalAlignment
    {
        get => _vertical;
        set => SetProperty(ref _vertical, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value ?? string.Empty, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string AltText
    {
        get => _altText;
        set => SetProperty(ref _altText, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Image = _image,
        Word = _scaleMethod,
        Horizontal = _horizontal,
        Vertical = _vertical,
        Text = _url,
    };
}

/// <summary>
/// One component of a <c>uifigure</c> as a frame snapshot holds it: everything the window needs to
/// draw it, copied out of the model on the script thread. One record serves every class; each uses
/// the fields its class has and leaves the rest at their defaults. <c>Position</c> is MATLAB's pixel
/// rectangle in the parent — for a grid's child, the cell the grid gave it when the frame was taken,
/// which the layout works out again from the grid whenever the window is resized.
/// </summary>
public sealed record UiComponentFrame : IUiNodeFrame
{
    public required UiComponentModel Source { get; init; }

    public required UiComponentKind Kind { get; init; }

    public Rect2D Position { get; init; }

    public UiUnits Units => UiUnits.Pixels;

    public bool Visible { get; init; } = true;

    public bool Enabled { get; init; } = true;

    public string Tooltip { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string FontName { get; init; } = "Helvetica";

    public double FontSize { get; init; } = 12;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public UiColor Foreground { get; init; }

    public UiColor? Background { get; init; }

    /// <summary>A second colour: a hyperlink's once it has been followed.</summary>
    public UiColor? Accent { get; init; }

    /// <summary>The caption's lines, or a list's items as text to measure.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>One string: an edit field's text, a numeric field's formatted value, a link's address.</summary>
    public string Text { get; init; } = string.Empty;

    public string Placeholder { get; init; } = string.Empty;

    /// <summary>One word: an edit field's input type, an image's scale method, a button's icon alignment.</summary>
    public string Word { get; init; } = string.Empty;

    public UiHorizontalAlignment Horizontal { get; init; }

    public UiVerticalAlignment Vertical { get; init; } = UiVerticalAlignment.Center;

    public bool WordWrap { get; init; }

    public bool Editable { get; init; }

    public bool Checked { get; init; }

    public bool Multi { get; init; }

    public bool InGroup { get; init; }

    /// <summary>Whether <see cref="Number"/> holds a value: false for an empty numeric field.</summary>
    public bool HasNumber { get; init; } = true;

    public double Number { get; init; }

    /// <summary>A range slider's upper value.</summary>
    public double Number2 { get; init; }

    public double Min { get; init; }

    public double Max { get; init; } = 1;

    public double Step { get; init; } = 1;

    /// <summary>Whether a slider stands up.</summary>
    public bool Upright { get; init; }

    public IReadOnlyList<string> Items { get; init; } = [];

    public IReadOnlyList<int> Selected { get; init; } = [];

    public IReadOnlyList<double> MajorTicks { get; init; } = [];

    public IReadOnlyList<double> MinorTicks { get; init; } = [];

    public IReadOnlyList<string> TickLabels { get; init; } = [];

    public UiImage? Image { get; init; }

    public long UserWriteSeq { get; init; }

    public int FocusRequests { get; init; }

    /// <summary>The size this component asks for in a <c>'fit'</c> track of a grid.</summary>
    public Size2D Fit { get; init; }

    /// <summary>Its cell, when its parent is a grid.</summary>
    public UiGridCell? Cell { get; init; }
}
