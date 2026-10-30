using System.Globalization;
using System.Text.RegularExpressions;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// The editors App Designer opens over a component on its canvas (<c>InPlaceEditorManifest</c>): the
/// kind decides the box, what its keys do and how the text is saved.
/// </summary>
public enum DesignInPlaceKind
{
    /// <summary>One line, saved by Enter (<c>SingleLineInPlaceEditor</c>): a title, a field's value, a limit, an item.</summary>
    SingleLine,

    /// <summary>Lines, saved by Enter, Shift+Enter a new line (<c>MultiLineInPlaceEditor</c>): a label's or a button's text.</summary>
    MultiLine,

    /// <summary>Lines, Enter a new line, saved when it loses the keyboard (<c>TextAreaInPlaceEditor</c>): a text area's value.</summary>
    TextArea,

    /// <summary>A list's items, one a line, saved by Shift+Enter or Alt+Enter (<c>SelectionMultiLineInPlaceEditor</c>).</summary>
    Items,

    /// <summary>A hyperlink's text, with its URL in a box under it (<c>HyperlinkInPlaceEditor</c>).</summary>
    Hyperlink,

    /// <summary>No box: a file chosen for a picture (<c>FileBrowserInPlaceEditor</c>).</summary>
    FileBrowser,
}

/// <summary>The keys an in-place editor reacts to; any other is the text box's own.</summary>
public enum DesignInPlaceKey
{
    Enter,
    Escape,
    Tab,
    Z,
}

/// <summary>Which box of a hyperlink's editor has the keyboard.</summary>
public enum DesignInPlaceField
{
    Text,
    Url,
    Visit,
}

/// <summary>What a key does to an in-place editor.</summary>
public enum DesignInPlaceAction
{
    /// <summary>What the text box does with it.</summary>
    None,

    /// <summary>Nothing at all: the key is taken.</summary>
    Swallow,

    /// <summary>A line break where the caret is.</summary>
    NewLine,

    /// <summary>The text back to what the editor opened with.</summary>
    Revert,

    /// <summary>Saved and closed.</summary>
    Save,

    /// <summary>Saved, and the component's next place to edit opened (a component with several).</summary>
    SaveNext,

    /// <summary>Saved, and the component's previous place to edit opened.</summary>
    SavePrevious,

    /// <summary>Closed, nothing saved.</summary>
    Cancel,

    /// <summary>A hyperlink's URL box takes the keyboard.</summary>
    ToUrl,

    /// <summary>A hyperlink's text takes the keyboard.</summary>
    ToText,

    /// <summary>A hyperlink's Visit link takes the keyboard.</summary>
    ToVisit,

    /// <summary>A hyperlink's URL opened in the browser.</summary>
    Visit,
}

/// <summary>
/// An in-place edit as App Designer's <c>InPlaceEditorManager</c> opens it: on which component, in
/// which editor, of which property, with what text, where - and, for a component with several places
/// to edit (a gauge's two limits, a switch's two items, a table's columns), which.
/// </summary>
/// <param name="Component">The component edited.</param>
/// <param name="Kind">The editor.</param>
/// <param name="Property">The property saved: <c>Text</c>, <c>Value</c>, <c>Title</c>, <c>Items</c>, <c>ColumnName</c>, <c>ImageSource</c>, or <c>Minimum</c> or <c>Maximum</c> of <c>Limits</c>.</param>
/// <param name="Lines">What the editor opens with, a line each.</param>
/// <param name="Box">Where the editor goes, on the canvas's surface.</param>
/// <param name="Hotspot">Which place of the component, from 0.</param>
/// <param name="Hotspots">How many places the component has.</param>
/// <param name="Alignment">The text's alignment: <c>left</c>, <c>center</c> or <c>right</c>.</param>
/// <param name="WordWrap">Whether the lines wrap in the box.</param>
/// <param name="Seed">What was typed to open it, which replaces the text with the caret after it; null when it opened on its text, all selected.</param>
/// <param name="Url">A hyperlink's URL; null for any other.</param>
public sealed record DesignInPlaceEdit(
    DesignComponent Component,
    DesignInPlaceKind Kind,
    string Property,
    IReadOnlyList<string> Lines,
    Rect2D Box,
    int Hotspot,
    int Hotspots,
    string Alignment,
    bool WordWrap,
    string? Seed,
    string? Url)
{
    /// <summary>The text the editor opens with: the seed, or the lines.</summary>
    public string Text => Seed ?? string.Join('\n', Lines);

    /// <summary>The pattern a single line must match to be saved (<c>ValidationRegExp</c>); null when any is.</summary>
    public string? Pattern { get; init; }
}

/// <summary>
/// App Designer's in-place editing rules, read from R2025b's client: which components it edits on the
/// canvas (each model's <c>InPlaceEditing</c>), what each one's editor edits (each controller's
/// <c>getEditorOptions</c>), which place of it a key opens (<c>_getEditableHotspotIndex</c>), and
/// what the editors' keys do.
/// </summary>
public static partial class DesignInPlace
{
    /// <summary>A limit as a slider's, a knob's and a gauge's editor takes one, its dots unescaped as R2025b writes them.</summary>
    public const string LimitPattern = "^[-+]?([0-9]*.?[0-9]+|[0-9]+.?[0-9]*)([eE][-+]?[0-9]+)?$|^$";

    /// <summary>A number as a numeric field's and a spinner's editor takes one.</summary>
    public const string NumberPattern = "^[-+]?([0-9]*.?[0-9]+|[0-9]+.?[0-9]*)([eE][-+]?[0-9]+)?$|^[-+]?[Ii]nf$|^s?$";

    private const string Label = "matlab.ui.control.Label";
    private const string Slider = "matlab.ui.control.Slider";
    private const string Knob = "matlab.ui.control.Knob";
    private const string DiscreteKnob = "matlab.ui.control.DiscreteKnob";
    private const string Gauge = "matlab.ui.control.Gauge";
    private const string LinearGauge = "matlab.ui.control.LinearGauge";
    private const string NinetyDegreeGauge = "matlab.ui.control.NinetyDegreeGauge";
    private const string SemicircularGauge = "matlab.ui.control.SemicircularGauge";
    private const string Table = "matlab.ui.control.Table";

    // The models whose InPlaceEditing says isInPlaceEditable (W5d brings the menus' and tools').
    private static readonly HashSet<string> Editable = new(StringComparer.Ordinal)
    {
        Label, "matlab.ui.control.Button", "matlab.ui.control.StateButton", "matlab.ui.control.ToggleButton",
        "matlab.ui.control.CheckBox", "matlab.ui.control.RadioButton", "matlab.ui.control.Hyperlink",
        "matlab.ui.control.EditField", "matlab.ui.control.NumericEditField", "matlab.ui.control.Spinner",
        "matlab.ui.control.TextArea", "matlab.ui.control.DropDown", "matlab.ui.control.ListBox",
        "matlab.ui.control.Image", Slider, Knob, DiscreteKnob, Gauge, LinearGauge, NinetyDegreeGauge, SemicircularGauge,
        "matlab.ui.control.Switch", "matlab.ui.control.RockerSwitch", "matlab.ui.control.ToggleSwitch", Table,
        "matlab.ui.container.Panel", "matlab.ui.container.ButtonGroup", "matlab.ui.container.Tab",
    };

    /// <summary>
    /// Whether a component of <paramref name="type"/> is edited on the canvas
    /// (<c>isInPlaceEditable</c>); a label only when its <c>Interpreter</c> is neither LaTeX nor HTML.
    /// </summary>
    public static bool IsEditable(string type, string? interpreter = null) =>
        Editable.Contains(type) && !(type == Label && interpreter is "latex" or "html");

    /// <summary>
    /// Whether F2 or a typed character opens the editor of a component of <paramref name="type"/>
    /// selected alone (<c>isAutoEditable</c>): any it edits but a drop-down's and a list's items, which
    /// only a double click opens.
    /// </summary>
    public static bool IsAutoEditable(string type, string? interpreter = null) =>
        IsEditable(type, interpreter) && type is not "matlab.ui.control.DropDown" and not "matlab.ui.control.ListBox";

    /// <summary>Whether a component of <paramref name="type"/> has several places to edit (<c>isMultiHotSpots</c>).</summary>
    public static bool HasHotspots(string type) => type is Slider or Knob or DiscreteKnob or Gauge or LinearGauge or NinetyDegreeGauge or SemicircularGauge
        or "matlab.ui.control.Switch" or "matlab.ui.control.RockerSwitch" or "matlab.ui.control.ToggleSwitch" or Table;

    /// <summary>Whether a component of <paramref name="type"/> edits one of its limits.</summary>
    public static bool EditsLimits(string type) => type is Slider or Knob or Gauge or LinearGauge or NinetyDegreeGauge or SemicircularGauge;

    /// <summary>
    /// Which place a key opens on a component with several (<c>_getEditableHotspotIndex</c> with no
    /// pointer): a horizontal slider's or linear gauge's minimum and a vertical one's maximum, a
    /// clockwise circular gauge's minimum, a ninety-degree gauge's by its orientation and direction, a
    /// semicircular gauge's by its orientation flipped when it runs anticlockwise, a horizontal
    /// switch's first item and a vertical one's second, a knob's, a discrete knob's and a table's first.
    /// </summary>
    public static int DefaultHotspot(string type, string orientation, string scaleDirection)
    {
        bool clockwise = scaleDirection == "clockwise";
        return type switch
        {
            Slider or LinearGauge or "matlab.ui.control.Switch" or "matlab.ui.control.RockerSwitch" or "matlab.ui.control.ToggleSwitch"
                => orientation == "vertical" ? 1 : 0,
            Gauge => clockwise ? 0 : 1,
            NinetyDegreeGauge => orientation.StartsWith("north", StringComparison.Ordinal) == clockwise ? 0 : 1,
            SemicircularGauge => (orientation is "south" or "west") == clockwise ? 1 : 0,
            _ => 0,
        };
    }

    /// <summary>
    /// The place after (or before) <paramref name="hotspot"/> that Tab opens: of two, the other; of
    /// more, the next, round from the last to the first; of a table's columns, round the ones it shows,
    /// from <paramref name="first"/> to <paramref name="last"/>.
    /// </summary>
    public static int NextHotspot(int hotspot, bool backwards, int first, int last)
    {
        int next = backwards ? hotspot - 1 : hotspot + 1;
        return next > last ? first : next < first ? last : next;
    }

    /// <summary>
    /// The text's alignment in a limit's or an item's editor (each controller's <c>getEditorOptions</c>):
    /// a slider's and a linear gauge's centred when horizontal; a gauge's and a knob's away from the
    /// dial; a discrete knob's away from its middle; a switch's away from the switch.
    /// </summary>
    public static string HotspotAlignment(string type, int hotspot, int hotspots, string orientation, string scaleDirection)
    {
        bool clockwise = scaleDirection == "clockwise";
        switch (type)
        {
            case Slider or LinearGauge:
                return orientation == "horizontal" ? "center" : "left";
            case Knob:
                return hotspot == 0 ? "right" : "left";
            case DiscreteKnob:
                double middle = (hotspots - 1) / 2.0;
                return hotspot == middle ? "center" : hotspot < middle ? "right" : "left";
            case Gauge:
                return hotspot == 0 ? (clockwise ? "left" : "right") : (clockwise ? "right" : "left");
            case NinetyDegreeGauge:
                return orientation is "northwest" or "southwest" ? "left" : "right";
            case SemicircularGauge:
                return orientation switch
                {
                    "north" => hotspot == 0 == clockwise ? "left" : "right",
                    "south" => hotspot == 0 == clockwise ? "right" : "left",
                    "west" => "right",
                    _ => "left",
                };
            case Table:
                return "center";
            default:
                return orientation == "vertical" ? "center" : hotspot == 1 ? "left" : "right";
        }
    }

    /// <summary>
    /// What <paramref name="key"/> does in an editor of <paramref name="kind"/>, transcribed from each
    /// editor's key handlers. A single line: Enter saves with any modifier, Escape closes unsaved, Tab
    /// saves and opens the next place (Shift+Tab the previous), Ctrl+Z puts the text back. Lines: Enter
    /// alone saves, Shift+Enter breaks the line, Enter with Ctrl or Alt does nothing. A text area: Enter
    /// breaks the line, Tab leaves it (which saves). A list's items: Shift+Enter or Alt+Enter saves,
    /// Enter breaks the line. A hyperlink's text is as lines, Tab going to its URL and Shift+Tab saving;
    /// its URL box saves on Enter and goes to Visit on Tab, back to the text on Shift+Tab; Visit opens
    /// the URL on Enter, saves on Tab and goes back to the URL on Shift+Tab.
    /// </summary>
    public static DesignInPlaceAction KeyAction(DesignInPlaceKind kind, DesignInPlaceKey key, DesignModifiers modifiers, DesignInPlaceField field = DesignInPlaceField.Text)
    {
        bool shiftOnly = modifiers == DesignModifiers.Shift;
        bool control = modifiers.HasFlag(DesignModifiers.Control);
        if (kind == DesignInPlaceKind.Hyperlink && field != DesignInPlaceField.Text)
        {
            return (field, key) switch
            {
                (_, DesignInPlaceKey.Escape) => DesignInPlaceAction.Cancel,
                (DesignInPlaceField.Url, DesignInPlaceKey.Z) when control => DesignInPlaceAction.Revert,
                (DesignInPlaceField.Url, DesignInPlaceKey.Enter) => DesignInPlaceAction.Save,
                (DesignInPlaceField.Url, DesignInPlaceKey.Tab) => shiftOnly ? DesignInPlaceAction.ToText : DesignInPlaceAction.ToVisit,
                (DesignInPlaceField.Visit, DesignInPlaceKey.Enter) => DesignInPlaceAction.Visit,
                (DesignInPlaceField.Visit, DesignInPlaceKey.Tab) => shiftOnly ? DesignInPlaceAction.ToUrl : DesignInPlaceAction.Save,
                _ => DesignInPlaceAction.None,
            };
        }

        switch (key)
        {
            case DesignInPlaceKey.Escape:
                return DesignInPlaceAction.Cancel;
            case DesignInPlaceKey.Z when control:
                return kind == DesignInPlaceKind.Items ? DesignInPlaceAction.None : DesignInPlaceAction.Revert;
            case DesignInPlaceKey.Tab:
                return kind switch
                {
                    DesignInPlaceKind.SingleLine or DesignInPlaceKind.MultiLine => shiftOnly ? DesignInPlaceAction.SavePrevious : DesignInPlaceAction.SaveNext,
                    DesignInPlaceKind.Hyperlink => shiftOnly ? DesignInPlaceAction.Save : DesignInPlaceAction.ToUrl,
                    _ => DesignInPlaceAction.Save,
                };
            case DesignInPlaceKey.Enter:
                return kind switch
                {
                    DesignInPlaceKind.SingleLine => DesignInPlaceAction.Save,
                    DesignInPlaceKind.MultiLine or DesignInPlaceKind.Hyperlink => modifiers == DesignModifiers.None ? DesignInPlaceAction.Save
                        : shiftOnly ? DesignInPlaceAction.NewLine : DesignInPlaceAction.Swallow,
                    DesignInPlaceKind.Items => modifiers.HasFlag(DesignModifiers.Shift) || modifiers.HasFlag(DesignModifiers.Alt) ? DesignInPlaceAction.Save : DesignInPlaceAction.NewLine,
                    _ => DesignInPlaceAction.NewLine,
                };
            default:
                return DesignInPlaceAction.None;
        }
    }

    /// <summary>
    /// Whether <paramref name="url"/> is one a hyperlink's editor saves (<c>_validateURL</c>): not a
    /// <c>matlab:</c> or <c>javascript:</c> one, and, when not empty, with a dot inside it that neither
    /// starts nor ends it.
    /// </summary>
    public static bool IsValidUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.StartsWith("matlab:", StringComparison.Ordinal) || url.StartsWith("javascript:", StringComparison.Ordinal))
        {
            return false;
        }

        return url.Length == 0 || (url.Split('.').Length >= 2 && !url.StartsWith('.') && !url.EndsWith('.'));
    }

    /// <summary>Whether <paramref name="text"/> matches <paramref name="pattern"/> as the editor's validation tests it.</summary>
    public static bool Matches(string? pattern, string text) => pattern is null || Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);

    /// <summary>
    /// A number typed into a limit, a numeric field or a spinner, as MATLAB reads it - <c>Inf</c> and
    /// <c>-inf</c> too; null for anything else, the empty text included.
    /// </summary>
    public static double? Number(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string t = text.Trim();
        if (InfText().IsMatch(t))
        {
            return t.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
        }

        return t.Length > 0 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    [GeneratedRegex("^[-+]?[Ii]nf$")]
    private static partial Regex InfText();
}
