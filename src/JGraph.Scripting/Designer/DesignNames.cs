using System.Text.RegularExpressions;

namespace JGraph.Scripting.Designer;

/// <summary>
/// The name App Designer gives a component's class (designer plan DS3): the code name a new one is
/// called by (<c>DefaultCodeName</c>), the text a new one shows (<c>DefaultText</c>), and the
/// property that text is in (<c>DefaultTextPropertyName</c>; null when the class shows none), from
/// each class's model in R2025b's App Designer.
/// </summary>
/// <param name="CodeName">The code name a new one is called by: <c>Button</c>, <c>EditField</c>, <c>UITable</c>.</param>
/// <param name="Text">The text a new one shows, or a labelled one's label shows: <c>Button</c>, <c>Edit Field</c>.</param>
/// <param name="TextProperty">The property that shows the text: <c>Text</c>, <c>Title</c>, <c>Name</c>; null when none.</param>
public sealed record DesignNaming(string CodeName, string Text, string? TextProperty);

/// <summary>
/// App Designer's naming of components (designer plan DS3), transcribed from R2025b's App Designer:
/// the code name and text a new component gets (<c>CreateCodeNameGenerator</c>, with its labelled
/// and restricted-parent forms), the new code name a component gets when its text is edited
/// (<c>ModifyCodeNameGenerator</c>), the counting under them (<c>UniqueNameGenerator</c>,
/// <c>_appendUniqueIncrement</c>), and the rules a code name typed by hand must keep
/// (<c>CodeEditUtil.isAppAttributeNameValid</c>).
/// </summary>
/// <remarks>
/// The counting is App Designer's, quirks and all: a name already ending in digits counts from them,
/// and any name in use that holds the base and ends in digits raises the count, whatever stands
/// between (<c>Button Group2</c> makes the next new button's text <c>Button3</c>).
/// </remarks>
public static partial class DesignNames
{
    /// <summary>The longest name MATLAB takes (<c>namelengthmax</c>), which App Designer checks a code name against.</summary>
    public const int NameLengthMax = 63;

    /// <summary>MATLAB's keywords, which no code name may be (App Designer's list).</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "classdef", "methods", "properties", "events", "continue", "else", "elseif", "end",
        "for", "function", "global", "if", "otherwise", "parfor", "persistent", "return", "spmd", "switch", "try", "while",
    };

    /// <summary>
    /// The names every app has from <c>matlab.apps.AppBase</c> and its own initialization, which no
    /// component, callback, function or property may take (the <c>AppBaseProperty</c> and
    /// <c>AppBaseMethod</c> entries of App Designer's name map; <c>AppDesignerInitializationData</c>).
    /// </summary>
    public static IReadOnlySet<string> AppDesignerKeywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AppBase", "saveobj", "delete", "loadobj", "getRunningApp", "setAutoResize", "registerApp", "runStartupFcn",
        "createCallbackFcn", "empty", "TimingFields", "createBindings",
    };

    private static readonly Dictionary<string, DesignNaming> ByType = new(StringComparer.Ordinal)
    {
        ["matlab.ui.Figure"] = new("UIFigure", "MATLAB App", "Name"),
        ["matlab.ui.container.ButtonGroup"] = new("ButtonGroup", "Button Group", "Title"),
        ["matlab.ui.container.CheckBoxTree"] = new("Tree", "Tree", null),
        ["matlab.ui.container.ContextMenu"] = new("ContextMenu", "Context Menu", null),
        ["matlab.ui.container.GridLayout"] = new("GridLayout", "GridLayout", null),
        ["matlab.ui.container.Menu"] = new("Menu", "Menu", "Text"),
        ["matlab.ui.container.Panel"] = new("Panel", "Panel", "Title"),
        ["matlab.ui.container.Tab"] = new("Tab", "Tab", "Title"),
        ["matlab.ui.container.TabGroup"] = new("TabGroup", "Tab Group", null),
        ["matlab.ui.container.Toolbar"] = new("Toolbar", "Toolbar", null),
        ["matlab.ui.container.Tree"] = new("Tree", "Tree", null),
        ["matlab.ui.container.TreeNode"] = new("Node", "Node", "Text"),
        ["matlab.ui.container.toolbar.PushTool"] = new("PushTool", "PushTool", null),
        ["matlab.ui.container.toolbar.ToggleTool"] = new("ToggleTool", "ToggleTool", null),
        ["matlab.ui.control.Button"] = new("Button", "Button", "Text"),
        ["matlab.ui.control.CheckBox"] = new("CheckBox", "Check Box", "Text"),
        ["matlab.ui.control.ColorPicker"] = new("ColorPicker", "Color Picker", null),
        ["matlab.ui.control.DatePicker"] = new("DatePicker", "Date Picker", null),
        ["matlab.ui.control.DiscreteKnob"] = new("Knob", "Knob", null),
        ["matlab.ui.control.DropDown"] = new("DropDown", "Drop Down", null),
        ["matlab.ui.control.EditField"] = new("EditField", "Edit Field", null),
        ["matlab.ui.control.Gauge"] = new("Gauge", "Gauge", null),
        ["matlab.ui.control.HTML"] = new("HTML", "HTML", null),
        ["matlab.ui.control.Hyperlink"] = new("Hyperlink", "Hyperlink", null),
        ["matlab.ui.control.Image"] = new("Image", "Image", null),
        ["matlab.ui.control.Knob"] = new("Knob", "Knob", null),
        ["matlab.ui.control.Label"] = new("Label", "Label", "Text"),
        ["matlab.ui.control.Lamp"] = new("Lamp", "Lamp", null),
        ["matlab.ui.control.LinearGauge"] = new("Gauge", "Gauge", null),
        ["matlab.ui.control.ListBox"] = new("ListBox", "List Box", null),
        ["matlab.ui.control.NinetyDegreeGauge"] = new("Gauge", "Gauge", null),
        ["matlab.ui.control.NumericEditField"] = new("EditField", "Edit Field", null),
        ["matlab.ui.control.RadioButton"] = new("Button", "Button", "Text"),
        ["matlab.ui.control.RangeSlider"] = new("Slider", "Slider", null),
        ["matlab.ui.control.RockerSwitch"] = new("Switch", "Switch", null),
        ["matlab.ui.control.SemicircularGauge"] = new("Gauge", "Gauge", null),
        ["matlab.ui.control.Slider"] = new("Slider", "Slider", null),
        ["matlab.ui.control.Spinner"] = new("Spinner", "Spinner", null),
        ["matlab.ui.control.StateButton"] = new("Button", "Button", "Text"),
        ["matlab.ui.control.Switch"] = new("Switch", "Switch", null),
        ["matlab.ui.control.Table"] = new("UITable", "UITable", null),
        ["matlab.ui.control.TextArea"] = new("TextArea", "Text Area", null),
        ["matlab.ui.control.ToggleButton"] = new("Button", "Button", "Text"),
        ["matlab.ui.control.ToggleSwitch"] = new("Switch", "Switch", null),
        ["matlab.ui.control.UIAxes"] = new("UIAxes", "Axes", null),
    };

    /// <summary>
    /// The classes App Designer names among their siblings only (<c>isRestrictedParentWidget</c>): a
    /// new one's text counts against its siblings' texts rather than every text in the app.
    /// </summary>
    private static readonly HashSet<string> RestrictedParent = new(StringComparer.Ordinal)
    {
        "matlab.ui.container.Menu",
        "matlab.ui.container.Tab",
        "matlab.ui.container.TreeNode",
        "matlab.ui.container.toolbar.PushTool",
        "matlab.ui.container.toolbar.ToggleTool",
        "matlab.ui.control.RadioButton",
        "matlab.ui.control.ToggleButton",
    };

    /// <summary>How App Designer names a component of <paramref name="type"/>; null for a class it does not know.</summary>
    public static DesignNaming? Of(string type) => ByType.GetValueOrDefault(type);

    /// <summary>Whether a new component of <paramref name="type"/> is named among its siblings rather than the whole app.</summary>
    public static bool IsNamedAmongSiblings(string type) => RestrictedParent.Contains(type);

    // --- new components --------------------------------------------------------------------------

    /// <summary>
    /// The code name and text a new component of <paramref name="type"/> gets
    /// (<c>CreateCodeNameGenerator</c>): its text counted against <paramref name="textsInUse"/> - every
    /// text the app's components show, or a sibling-named class's siblings' texts - and its code name,
    /// the class's with the text's trailing digits, made unique among <paramref name="codeNamesInUse"/>.
    /// A class that shows no text gets its code name counted against the code names alone.
    /// </summary>
    /// <returns>The code name, and the text (null for a class that shows none).</returns>
    public static (string CodeName, string? Text) ForNew(string type, IReadOnlyList<string> codeNamesInUse, IReadOnlyList<string> textsInUse)
    {
        ArgumentNullException.ThrowIfNull(codeNamesInUse);
        ArgumentNullException.ThrowIfNull(textsInUse);
        DesignNaming naming = Of(type) ?? new DesignNaming(ShortName(type), ShortName(type), null);
        if (naming.TextProperty is null)
        {
            return (AppendUniqueIncrement(codeNamesInUse, naming.CodeName, distinct: false), null);
        }

        string text = GenerateVariableName(textsInUse, naming.Text, keepWhole: true);
        string codeName = AppendUniqueIncrement(codeNamesInUse, naming.CodeName + TrailingDigits(text), distinct: true);
        return (codeName, text);
    }

    /// <summary>
    /// The code names and label text of a new labelled component of <paramref name="type"/>
    /// (<c>CreateLabeledComponentCodeNameGenerator</c>): the label's text is the class's text counted
    /// against <paramref name="textsInUse"/>, the component's code name the class's with that text's
    /// trailing digits, and the label's the component's with <c>Label</c> after it, each unique.
    /// </summary>
    public static (string CodeName, string LabelCodeName, string LabelText) ForNewLabeled(
        string type, IReadOnlyList<string> codeNamesInUse, IReadOnlyList<string> textsInUse)
    {
        ArgumentNullException.ThrowIfNull(codeNamesInUse);
        ArgumentNullException.ThrowIfNull(textsInUse);
        DesignNaming naming = Of(type) ?? new DesignNaming(ShortName(type), ShortName(type), null);
        string text = GenerateVariableName(textsInUse, naming.Text, keepWhole: true);
        string codeName = AppendUniqueIncrement(codeNamesInUse, naming.CodeName + TrailingDigits(text), distinct: true);
        string labelCodeName = AppendUniqueIncrement(codeNamesInUse, codeName + "Label", distinct: true);
        return (codeName, labelCodeName, text);
    }

    // --- an edited text --------------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="codeName"/> was named by hand rather than from <paramref name="text"/>
    /// (<c>_isCodeNameUserEdited</c>): the name the text would give - its class text replaced by the
    /// class's code name, everything but letters, digits and underscores taken out, leading digits and
    /// underscores dropped, the class's code name added when it is not in it - with or without an
    /// <c>_N</c> after it, is not the code name.
    /// </summary>
    public static bool IsNamedByHand(string text, string defaultText, string codeName, string defaultCodeName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(codeName);
        string name = NameFromText(text, defaultText, defaultCodeName);
        return !Regex.IsMatch(codeName, "^" + Regex.Escape(name) + "(_(\\d)+)?$");
    }

    /// <summary>
    /// The code name a component named from its text gets when its text becomes
    /// <paramref name="newText"/> (<c>_generateCodeNameForEditedText</c>): the name the text gives,
    /// unique among <paramref name="codeNamesInUse"/>; its own name when that is what it gives, or an
    /// <c>_N</c> form of it already; and its own name too when the new one would be longer than
    /// <see cref="NameLengthMax"/>.
    /// </summary>
    public static string ForEditedText(string newText, string defaultText, string currentCodeName, string defaultCodeName, IReadOnlyList<string> codeNamesInUse)
    {
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(codeNamesInUse);
        string name = NameFromText(newText, defaultText, defaultCodeName);
        if (name == currentCodeName)
        {
            return currentCodeName;
        }

        var numbered = new Regex("^" + Regex.Escape(name) + "_(\\d)+$");
        bool taken = codeNamesInUse.Any(n => n == name || numbered.IsMatch(n));
        if (taken && numbered.IsMatch(currentCodeName))
        {
            return currentCodeName;
        }

        string unique = AppendUniqueIncrement(codeNamesInUse, name, distinct: true);
        return unique.Length > NameLengthMax ? currentCodeName : unique;
    }

    private static string NameFromText(string text, string defaultText, string defaultCodeName)
    {
        string name = text;
        if (defaultText.Length > 0 && defaultCodeName.Length > 0)
        {
            name = name.Replace(defaultText, defaultCodeName, StringComparison.Ordinal);
        }

        name = NonWord().Replace(name, string.Empty);
        name = LeadingDigitsAndUnderscores().Replace(name, string.Empty);
        if (!name.Contains(defaultCodeName, StringComparison.Ordinal))
        {
            name += defaultCodeName;
        }

        return name;
    }

    // --- a name typed by hand --------------------------------------------------------------------

    /// <summary>
    /// Why <paramref name="name"/> cannot be a component's code name (App Designer's
    /// <c>isAppAttributeNameValid</c>): a keyword; not a letter followed by letters, digits and
    /// underscores; longer than <see cref="NameLengthMax"/>; or already the name of something else
    /// in the app (<paramref name="namesInUse"/>; the component's own name, <paramref name="current"/>,
    /// is free). Null when it can.
    /// </summary>
    public static string? WhyNotName(string name, string? current, IEnumerable<string> namesInUse)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(namesInUse);
        if (Keywords.Contains(name))
        {
            return $"'{name}' is a MATLAB keyword.";
        }

        if (AppDesignerKeywords.Contains(name))
        {
            return "Name cannot be an App Designer keyword.";
        }

        if (!VarName().IsMatch(name))
        {
            return $"'{name}' is not a valid name: it must start with a letter and hold only letters, digits and underscores.";
        }

        if (name.Length > NameLengthMax)
        {
            return $"'{name}' is longer than {NameLengthMax} characters.";
        }

        if (name != current && namesInUse.Contains(name, StringComparer.Ordinal))
        {
            return $"'{name}' is already the name of something in the app.";
        }

        return null;
    }

    // --- counting --------------------------------------------------------------------------------

    /// <summary>
    /// <c>_appendUniqueIncrement</c>: with <paramref name="distinct"/> (a code name), the name itself
    /// when neither it nor an <c>_N</c> form of it is in use, else the first free <c>name_N</c>;
    /// without, <see cref="GenerateVariableName"/>'s count.
    /// </summary>
    internal static string AppendUniqueIncrement(IReadOnlyList<string> inUse, string name, bool distinct)
    {
        if (!distinct)
        {
            return GenerateVariableName(inUse, name, keepWhole: false);
        }

        var numbered = new Regex("^" + Regex.Escape(name) + "(_(\\d)+)?$");
        if (!inUse.Any(numbered.IsMatch))
        {
            return name;
        }

        string generated = GenerateVariableName(inUse, name + "_", keepWhole: false);
        return generated.EndsWith('_') ? generated + "2" : generated;
    }

    /// <summary>
    /// <c>UniqueNameGenerator.generateVariableName</c> (no suffix on the first, no delimiter): the
    /// prefix itself when nothing in use counts toward it, else the prefix - its own trailing digits
    /// taken off - with the first free number above the highest count. A name in use counts when it
    /// holds the prefix: by its trailing digits, or as 1 when it is the prefix itself. Without
    /// <paramref name="keepWhole"/> the prefix is its first word, spaces in a name are taken out, and a
    /// name whose character after the prefix is not a digit or space does not count.
    /// </summary>
    internal static string GenerateVariableName(IReadOnlyList<string> inUse, string prefix, bool keepWhole)
    {
        string s = keepWhole ? prefix : prefix.Split(' ')[0];
        bool tooLong = false;
        long highest = 0;
        foreach (string a in inUse)
        {
            if (string.IsNullOrEmpty(a) || !a.Contains(s, StringComparison.Ordinal))
            {
                continue;
            }

            string l;
            if (keepWhole)
            {
                l = a;
            }
            else
            {
                l = Whitespace().Replace(a, string.Empty);

                // JavaScript's a[s.length] reads the original name, spaces and all.
                if (a.Length > s.Length && !DigitBarOrSpace().IsMatch(a[s.Length].ToString()))
                {
                    continue;
                }
            }

            string g;
            long u;
            Match digits = TrailingNumber().Match(l);
            if (digits.Success)
            {
                g = l[..^digits.Value.Length];
                if (digits.Value.Length > 15)
                {
                    tooLong = true;
                    continue;
                }

                u = long.Parse(digits.Value, System.Globalization.CultureInfo.InvariantCulture);
                highest = System.Math.Max(highest, u);
            }
            else
            {
                g = l;
                u = 1;
            }

            if (g == s && u > highest)
            {
                highest = u;
            }
        }

        if (tooLong)
        {
            return FindAvailable(s, inUse, 0);
        }

        if (highest == 0)
        {
            return s;
        }

        Match own = TrailingNumber().Match(s);
        if (own.Success)
        {
            s = s[..own.Index];
        }

        return FindAvailable(s, inUse, highest);
    }

    /// <summary><c>_findAvailableCodeName</c>: the prefix with the first number past <paramref name="from"/> not in use (1 is the prefix alone).</summary>
    private static string FindAvailable(string prefix, IReadOnlyList<string> inUse, long from)
    {
        long n = from;
        string candidate;
        do
        {
            n++;
            candidate = n == 1 ? prefix : prefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        while (inUse.Contains(candidate, StringComparer.Ordinal));

        return candidate;
    }

    private static string TrailingDigits(string text) => TrailingNumber().Match(text) is { Success: true } m ? m.Value : string.Empty;

    private static string ShortName(string type) => type[(type.LastIndexOf('.') + 1)..];

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[0-9|\s]")]
    private static partial Regex DigitBarOrSpace();

    [GeneratedRegex(@"\W")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"^[0-9_]*")]
    private static partial Regex LeadingDigitsAndUnderscores();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*$")]
    private static partial Regex VarName();
}
