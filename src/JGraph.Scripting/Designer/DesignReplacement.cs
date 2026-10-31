using System.Text.RegularExpressions;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Where a class's position sits in the box its ticks or labels take, as a new one has it: App Designer's
/// <c>Location</c> less <c>OuterLocation</c>, its <c>Size</c> and its <c>OuterSize</c>.
/// </summary>
public readonly record struct DesignClassBox(double Left, double Bottom, Size2D Size, Size2D Outer);

/// <summary>
/// The rules of App Designer's Replace With (<c>appdesigner/componentreplacement</c>,
/// <c>CompReplacementUtil</c>, <c>ReplaceComponentCodeNameGenerator</c>): which classes stand in for
/// which, where the new one goes and what size it is, how a value and limits carry over, and what it and
/// its label are called. <see cref="DesignDocument.ReplaceWith"/> does the replacing.
/// </summary>
public static partial class DesignReplacement
{
    private const string Numeric = "NUMERIC";
    private const string TextualInput = "TEXTUAL_INPUT";
    private const string TextualDisplay = "TEXTUAL_DISPLAY";
    private const string Gauge = "GAUGE";

    private const string NumericEditField = "matlab.ui.control.NumericEditField";
    private const string Spinner = "matlab.ui.control.Spinner";
    private const string EditField = "matlab.ui.control.EditField";
    private const string TextArea = "matlab.ui.control.TextArea";
    private const string Knob = "matlab.ui.control.Knob";
    private const string Slider = "matlab.ui.control.Slider";
    private const string RangeSlider = "matlab.ui.control.RangeSlider";
    private const string LinearGauge = "matlab.ui.control.LinearGauge";
    private const string SemicircularGauge = "matlab.ui.control.SemicircularGauge";

    // Every class with a family, in the order App Designer's component infos hold them, which is the
    // order the menu offers them in: its name, its family, and for two classes a family it is also
    // offered in to one other class (Family's Cross). The tools' family is undefined in R2025b's table
    // (ComponentFamilyTypes has no Tool), which works because the two share it and no class else does.
    private static readonly (string Type, string Name, string Family, string? CrossFamily, string? CrossFor)[] Classes =
    [
        ("matlab.ui.container.CheckBoxTree", "Tree (Check Box)", "TREE", null, null),
        ("matlab.ui.container.Tree", "Tree", "TREE", null, null),
        ("matlab.ui.container.toolbar.PushTool", "Push Tool", "TOOL", null, null),
        ("matlab.ui.container.toolbar.ToggleTool", "Toggle Tool", "TOOL", null, null),
        ("matlab.ui.control.Button", "Button", "BUTTON", null, null),
        ("matlab.ui.control.DiscreteKnob", "Knob (Discrete)", "State", null, null),
        ("matlab.ui.control.DropDown", "Drop Down", "State", null, null),
        (EditField, "Edit Field (Text)", TextualInput, Numeric, NumericEditField),
        ("matlab.ui.control.Gauge", "Gauge", Gauge, null, null),
        ("matlab.ui.control.Hyperlink", "Hyperlink", TextualDisplay, null, null),
        (Knob, "Knob", Numeric, null, null),
        ("matlab.ui.control.Label", "Label", TextualDisplay, null, null),
        (LinearGauge, "Gauge (Linear)", Gauge, null, null),
        ("matlab.ui.control.ListBox", "List Box", "State", null, null),
        ("matlab.ui.control.NinetyDegreeGauge", "Gauge (90 Degree)", Gauge, null, null),
        (NumericEditField, "Edit Field (Numeric)", Numeric, TextualInput, EditField),
        (RangeSlider, "Slider (Range)", Numeric, null, null),
        ("matlab.ui.control.RockerSwitch", "Switch (Rocker)", "SWITCH", null, null),
        (SemicircularGauge, "Gauge (Semicircular)", Gauge, null, null),
        (Slider, "Slider", Numeric, null, null),
        (Spinner, "Spinner", Numeric, null, null),
        ("matlab.ui.control.StateButton", "State Button", "BUTTON", null, null),
        ("matlab.ui.control.Switch", "Switch", "SWITCH", null, null),
        (TextArea, "Text Area", TextualInput, null, null),
        ("matlab.ui.control.ToggleSwitch", "Switch (Toggle)", "SWITCH", null, null),
    ];

    /// <summary>The family of <paramref name="type"/> (<c>CompReplacementUtil.getFamily</c>); null for a class with none.</summary>
    public static string? FamilyOf(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        foreach (var c in Classes)
        {
            if (c.Type == type)
            {
                return c.Family;
            }
        }

        return null;
    }

    /// <summary>
    /// The classes <paramref name="type"/> can be replaced with, in the menu's order, with the names it
    /// shows (<c>getComponentsOfFamily</c>): its family's others, and a class that names it as its cross.
    /// </summary>
    public static IReadOnlyList<(string Type, string Name)> Offered(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (FamilyOf(type) is not { } family)
        {
            return [];
        }

        return Classes
            .Where(c => c.Type != type && (c.Family == family || (c.CrossFamily == family && c.CrossFor == type)))
            .Select(static c => (c.Type, c.Name))
            .ToArray();
    }

    /// <summary>
    /// The size a <paramref name="target"/> replacing a <paramref name="source"/> is given (the replacers'
    /// <c>_convertSize</c>): a new one's; a gauge's as wide as what it replaces and shaped as its class is; a
    /// spinner and a numeric field the size of the other.
    /// </summary>
    public static Size2D SizeOf(string source, string target, Size2D sourceSize, DesignClassBox box)
    {
        switch (FamilyOf(target))
        {
            case Gauge:
                return target switch
                {
                    LinearGauge => new Size2D(sourceSize.Width, box.Size.Height),
                    SemicircularGauge => new Size2D(sourceSize.Width, sourceSize.Width / (box.Size.Width / box.Size.Height)),
                    _ => new Size2D(sourceSize.Width, sourceSize.Width),
                };
            case Numeric when source is NumericEditField or Spinner && target is NumericEditField or Spinner:
                return sourceSize;
            default:
                return box.Size;
        }
    }

    /// <summary>
    /// Where a <paramref name="target"/> of <paramref name="size"/> replacing a component whose outer box is
    /// <paramref name="sourceOuter"/> goes (<c>_convertLocation</c>, the numeric family's): at that box's
    /// corner; beside a label on its left, with that box's top kept; a knob's or slider's position within
    /// the box a new one takes. <paramref name="sourceLabel"/> is where the labelled component's label is.
    /// </summary>
    /// <remarks>
    /// R2025b's top kept takes a new one's height, so that a spinner or numeric field that keeps its own
    /// height moves by the difference: here it stays where it is.
    /// </remarks>
    public static Point2D LocationOf(string target, DesignLabelPlace sourceLabel, Rect2D sourceOuter, Size2D size, DesignClassBox box)
    {
        double x = sourceOuter.X;
        double y = sourceOuter.Y;
        if (sourceLabel is DesignLabelPlace.WestMiddle or DesignLabelPlace.WestTop)
        {
            y += sourceOuter.Height - size.Height;
        }

        if (target == Knob)
        {
            x += box.Left;
            y -= box.Bottom;
        }
        else if (target == Slider)
        {
            x += box.Left;
            y -= box.Outer.Height - (box.Bottom + box.Size.Height);
        }

        return new Point2D(x, y);
    }

    /// <summary>
    /// The limits a numeric class replacing one with limits has (<c>_convertLimits</c>): the same, but
    /// infinite ones in a class whose limits must be finite are the value's 100 either side.
    /// </summary>
    public static (double Low, double High) LimitsOf((double Low, double High) source, double value, (double Low, double High) targetDefault, double targetValue)
    {
        bool infinite = double.IsInfinity(source.Low) || double.IsInfinity(source.High);
        if (!infinite || !IsFinite(targetDefault))
        {
            return source;
        }

        double around = double.IsInfinity(value) ? targetValue : value;
        return (around - 100, around + 100);
    }

    /// <summary>
    /// The value a numeric class replacing another has (<c>_convertValue</c>): the same; a new one's where it
    /// was infinite and the limits must be finite; a range slider's first going to one number, and one number
    /// going to a range slider up to its upper limit. A numeric field replacing a text field is 0.
    /// </summary>
    /// <remarks>R2025b nests an infinite value's replacement in a range slider's pair: here it is the new one's pair.</remarks>
    public static double[] NumericValueOf(string source, string target, double[] value, (double Low, double High) targetDefaultLimits, double[] targetDefaultValue, double targetHigh)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(targetDefaultValue);
        if (target == NumericEditField && source == EditField)
        {
            return [0];
        }

        if (source == RangeSlider)
        {
            return [value[0]];
        }

        if (IsFinite(targetDefaultLimits) && double.IsInfinity(value[0]))
        {
            return targetDefaultValue;
        }

        return target == RangeSlider ? [value[0], targetHigh] : [value[0]];
    }

    /// <summary>
    /// The text a text input class replacing another holds (the textual input family's <c>_convertValue</c>):
    /// a text area's lines run together for a field, and nothing from a numeric field.
    /// </summary>
    public static IReadOnlyList<string> TextValueOf(string source, string target, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (target == EditField && source == NumericEditField)
        {
            return [string.Empty];
        }

        return target == EditField && source == TextArea ? [string.Concat(lines)] : lines;
    }

    private static bool IsFinite((double Low, double High) limits) => !double.IsInfinity(limits.Low) && !double.IsInfinity(limits.High);

    /// <summary>
    /// What a replacement and its label are called and what the label says (<c>ReplaceComponentCommand</c>,
    /// <c>ReplaceComponentCodeNameGenerator</c>, then <c>ModifyCodeNameGenerator</c>): a name given by hand is
    /// kept, one that follows from the text is made again for the new class, and a label that does not say the
    /// class's own word keeps saying what it says, the names following from it.
    /// </summary>
    /// <param name="source">The class replaced.</param>
    /// <param name="target">The class replacing it.</param>
    /// <param name="labelled">Whether it is a labelled component.</param>
    /// <param name="codeName">Its code name.</param>
    /// <param name="labelCodeName">Its label's code name, for a labelled component.</param>
    /// <param name="text">The label's text for a labelled component; else its own, when its class shows one.</param>
    /// <param name="targetText">The text the new one holds, when its class shows one.</param>
    /// <param name="codeNamesInUse">Every name in the app, these components' among them.</param>
    /// <param name="textsInUse">Every text the app shows, these components' among them.</param>
    /// <returns>The new code names; the label's new text, or null when it stays.</returns>
    /// <remarks>
    /// R2025b fails with a TypeError for a label named by hand that says its class's own word: here it says
    /// the new class's word and keeps its name.
    /// </remarks>
    public static (string CodeName, string? LabelCodeName, string? LabelText) NamesOf(
        string source, string target, bool labelled, string codeName, string? labelCodeName, string? text, string? targetText,
        IReadOnlyList<string> codeNamesInUse, IReadOnlyList<string> textsInUse)
    {
        ArgumentNullException.ThrowIfNull(codeName);
        ArgumentNullException.ThrowIfNull(codeNamesInUse);
        ArgumentNullException.ThrowIfNull(textsInUse);
        DesignNaming from = DesignNames.Of(source) ?? throw new ArgumentException($"No names for '{source}'.", nameof(source));
        DesignNaming to = DesignNames.Of(target) ?? throw new ArgumentException($"No names for '{target}'.", nameof(target));
        string said = text ?? string.Empty;
        bool namedByHand = DesignNames.IsNamedByHand(said, from.Text, codeName, from.CodeName);
        if (!labelled)
        {
            // Named as a labelled one is, label or none (makeCreateCodeNameGenerator's second argument is
            // true here), but for the classes named among their siblings.
            string madeName = DesignNames.IsNamedAmongSiblings(target)
                ? DesignNames.ForNew(target, codeNamesInUse, []).CodeName
                : DesignNames.ForNewLabeled(target, codeNamesInUse, textsInUse).CodeName;
            string name = namedByHand ? codeName : madeName;
            if (to.TextProperty is null || targetText is null)
            {
                return (name, null, null);
            }

            // The text is on the new one already, so a name it does not follow from counts as by hand.
            if (DesignNames.IsNamedByHand(targetText, to.Text, name, to.CodeName))
            {
                return (name, null, null);
            }

            List<string> after = [.. Without(codeNamesInUse, codeName), name];
            return (DesignNames.ForEditedText(targetText, to.Text, name, to.CodeName, after), null, null);
        }

        ArgumentNullException.ThrowIfNull(labelCodeName);
        bool labelByHand = DesignNames.IsNamedByHand(said, from.Text, TrailingLabel().Replace(labelCodeName, string.Empty), from.CodeName);
        bool ownWords = !Regex.IsMatch(said, "^" + from.Text + "[0-9]*$");
        (string newName, string newLabelName, string newText) = DesignNames.ForNewLabeled(target, codeNamesInUse, textsInUse);
        string component = namedByHand ? codeName : labelByHand ? DesignNames.ForNew(target, codeNamesInUse, textsInUse).CodeName : newName;
        string labelName = labelByHand ? labelCodeName : newLabelName;
        if (!ownWords)
        {
            return (component, labelName, newText);
        }

        // ModifyCodeNameGenerator, the label's text going from the new class's word to what it said.
        List<string> inUse = [.. Without(Without(codeNamesInUse, codeName), labelCodeName), component, labelName];
        string renamed = DesignNames.IsNamedByHand(to.Text, to.Text, component, to.CodeName)
            ? component
            : DesignNames.ForEditedText(said, to.Text, component, to.CodeName, inUse);
        if (renamed != component)
        {
            inUse.Add(renamed);
        }

        string stem = FirstLabel().Replace(labelName, string.Empty, 1);
        if (!DesignNames.IsNamedByHand(to.Text, to.Text, stem, to.CodeName))
        {
            labelName = DesignNames.ForEditedText(renamed != component ? renamed + "Label" : said, "Label", labelName, "Label", inUse);
        }

        return (renamed, labelName, null);
    }

    private static IEnumerable<string> Without(IEnumerable<string> names, string name)
    {
        bool gone = false;
        foreach (string n in names)
        {
            if (!gone && n == name)
            {
                gone = true;
                continue;
            }

            yield return n;
        }
    }

    [GeneratedRegex("Label$")]
    private static partial Regex TrailingLabel();

    [GeneratedRegex("Label")]
    private static partial Regex FirstLabel();
}
