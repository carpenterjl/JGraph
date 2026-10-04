using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>uicontrol</c>'s property surface (app-building plan, U1): R2025b's forty names, with the
/// words, coercions and refusals measured headless in <c>tools/matlab-checklist/ui-probes/u1</c>.
/// Every refusal carries R2025b's identifier and its sentence; a write through <c>set</c> or the dot
/// says which property and class it was setting, as R2025b's do, and one inside the creating call
/// does not, as R2025b's do not.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private const string UiControlClass = "UIControl";

    /// <summary>True while <c>uicontrol(...)</c> applies its options, whose refusals R2025b words bare.</summary>
    [ThreadStatic]
    private static bool _creatingComponent;

    /// <summary>Marks the options of a creating call, for the returned scope.</summary>
    internal static IDisposable CreatingComponent()
    {
        bool outer = _creatingComponent;
        _creatingComponent = true;
        return new CreatingScope(outer);
    }

    private sealed class CreatingScope(bool outer) : IDisposable
    {
        public void Dispose() => _creatingComponent = outer;
    }

    private static readonly string[] StyleWords =
        ["pushbutton", "togglebutton", "radiobutton", "checkbox", "edit", "text", "slider", "frame", "listbox", "popupmenu"];

    private static readonly string[] AlignmentWords = ["left", "center", "right"];
    private static readonly string[] EnableWords = ["on", "off", "inactive"];
    private static readonly string[] FontUnitWords = ["inches", "centimeters", "normalized", "points", "pixels"];
    private static readonly string[] UnitWords = ["inches", "centimeters", "characters", "normalized", "points", "pixels"];

    // R2025b lists two weights and two angles, and still takes the older words and hands them back.
    private static readonly string[] FontWeightShown = ["normal", "bold"];
    private static readonly string[] FontWeightWords = ["normal", "bold", "light", "demi"];
    private static readonly string[] FontAngleShown = ["normal", "italic"];
    private static readonly string[] FontAngleWords = ["normal", "italic", "oblique"];

    private static UiControlModel Control(JgsHandleEntry entry) => (UiControlModel)entry.Target;

    /// <summary>Whether a class name is one of MATLAB's numeric classes.</summary>
    private static bool IsNumericKind(string kind) => JgsNumericClasses.Parse(kind) is not null;

    private static void AddUiControlBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Style",
            entry => JgsValue.Str(StyleWords[(int)Control(entry).Style]),
            (entry, value, line, col) => Control(entry).Style =
                (UiControlStyle)Word(entry, "Style", value, StyleWords, StyleWords, line, col));

        Put(table, "String",
            entry => TextValue(Control(entry).Text),
            (entry, value, line, col) => Control(entry).Text = ControlString(entry, value, line, col));

        Put(table, "Value",
            entry => NumbersValue(Control(entry).Value),
            (entry, value, line, col) => Control(entry).Value = ControlValue(entry, value, line, col));

        Put(table, "Max",
            entry => JgsValue.Number(Control(entry).Max),
            (entry, value, line, col) => Control(entry).Max = NumericScalar(entry, "Max", value, line, col));
        Put(table, "Min",
            entry => JgsValue.Number(Control(entry).Min),
            (entry, value, line, col) => Control(entry).Min = NumericScalar(entry, "Min", value, line, col));
        Put(table, "ListboxTop",
            entry => JgsValue.Number(Control(entry).ListboxTop),
            (entry, value, line, col) => Control(entry).ListboxTop = NumericScalar(entry, "ListboxTop", value, line, col));
        Put(table, "SliderStep",
            entry => Row(Control(entry).SliderStepSmall, Control(entry).SliderStepLarge),
            (entry, value, line, col) =>
            {
                double[] step = NumericVector(entry, "SliderStep", value, 2, line, col);
                Control(entry).SliderStepSmall = step[0];
                Control(entry).SliderStepLarge = step[1];
            });

        Put(table, "HorizontalAlignment",
            entry => JgsValue.Str(AlignmentWords[(int)Control(entry).HorizontalAlignment]),
            (entry, value, line, col) => Control(entry).HorizontalAlignment =
                (UiHorizontalAlignment)Word(entry, "HorizontalAlignment", value, AlignmentWords, AlignmentWords, line, col));

        Put(table, "BackgroundColor",
            entry => UiColorValue(Control(entry).BackgroundColor),
            (entry, value, line, col) => Control(entry).BackgroundColor = ComponentColor(entry, "BackgroundColor", value, line, col));
        Put(table, "ForegroundColor",
            entry => UiColorValue(Control(entry).ForegroundColor),
            (entry, value, line, col) => Control(entry).ForegroundColor = ComponentColor(entry, "ForegroundColor", value, line, col));

        Put(table, "FontName",
            entry => JgsValue.Str(Control(entry).FontName),
            (entry, value, line, col) =>
            {
                if (!JgsBuiltins.IsTextScalar(value))
                {
                    throw ComponentError(entry, "FontName", "MATLAB:class:RequireString",
                        "Value must be a character vector or a string scalar.", line, col);
                }

                string name = JgsBuiltins.TextOf(value);
                Control(entry).FontName = name.Length > 0
                    ? name
                    : throw ComponentError(entry, "FontName", "MATLAB:class:MATLABConversionError",
                        "Character vector value must not be empty", line, col);
            });
        Put(table, "FontSize",
            entry => JgsValue.Number(Control(entry).FontSize),
            (entry, value, line, col) => Control(entry).FontSize = FontSizeOf(entry, value, line, col));
        Put(table, "FontUnits",
            entry => JgsValue.Str(FontUnitWords[(int)Control(entry).FontUnits]),
            (entry, value, line, col) =>
            {
                // A change of units keeps the size the text is drawn at: FontSize is re-expressed.
                UiControlModel control = Control(entry);
                var units = (UiFontUnits)Word(entry, "FontUnits", value, FontUnitWords, FontUnitWords, line, col);
                double pixels = control.FontSizeInPixels(control.Position.Height);
                control.FontUnits = units;
                double one = control.FontSizeInPixels(control.Position.Height) / control.FontSize;
                control.FontSize = one > 0 && double.IsFinite(one) ? pixels / one : control.FontSize;
            });
        Put(table, "FontWeight",
            entry => JgsValue.Str(Control(entry).FontWeight),
            (entry, value, line, col) => Control(entry).FontWeight =
                FontWeightWords[Word(entry, "FontWeight", value, FontWeightWords, FontWeightShown, line, col)]);
        Put(table, "FontAngle",
            entry => JgsValue.Str(Control(entry).FontAngle),
            (entry, value, line, col) => Control(entry).FontAngle =
                FontAngleWords[Word(entry, "FontAngle", value, FontAngleWords, FontAngleShown, line, col)]);

        AddCallbackSlot(table, "Callback", static entry => entry.UiCallback, static (entry, value) => entry.UiCallback = value);
        AddCallbackSlot(table, "KeyPressFcn", static entry => entry.KeyPressFcn, static (entry, value) => entry.KeyPressFcn = value);
        AddCallbackSlot(table, "KeyReleaseFcn", static entry => entry.KeyReleaseFcn, static (entry, value) => entry.KeyReleaseFcn = value);

        // CData is an image for a button face; U1 keeps what was set and draws none (U3 draws it).
        Put(table, "CData",
            entry => entry.UiCData ?? JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, line, col) => entry.UiCData = JgsValue.Share(value));

        // An estimate from the font until the window measures text (U3): one line's height, and a
        // width of about half an em per character.
        Put(table, "Extent", entry =>
        {
            UiControlModel control = Control(entry);
            double size = control.FontSizeInPixels(control.Position.Height);
            int longest = control.Text.Lines.Count == 0 ? 0 : control.Text.Lines.Max(static l => l.Length);
            return Row(0, 0, longest * size * 0.5, System.Math.Max(1, control.Text.Lines.Count) * size * 1.25);
        });
    }

    /// <summary>What every component shares: its place, whether it is on, its tooltip, its units.</summary>
    private static void AddUiObjectBlock(IDictionary<string, GraphicsProperty> table)
    {
        // MATLAB's interaction words on drawn objects are not a component's: R2025b's uicontrol has
        // no Selected, SelectionHighlight, HitTest or PickableParts, and answers neither of them.
        foreach (string drawnOnly in new[] { "Selected", "SelectionHighlight", "HitTest", "PickableParts" })
        {
            table.Remove(drawnOnly);
        }

        foreach (string name in new[] { "Position", "InnerPosition", "OuterPosition" })
        {
            string captured = name;
            Put(table, captured,
                entry =>
                {
                    Rect2D box = ((UiObject)entry.Target).Position;
                    return Row(box.X, box.Y, box.Width, box.Height);
                },
                (entry, value, line, col) => ((UiObject)entry.Target).Position = ComponentPosition(entry, captured, value, line, col));
        }

        Put(table, "Units",
            static _ => JgsValue.Str("pixels"),
            (entry, value, line, col) =>
            {
                int word = Word(entry, "Units", value, UnitWords, UnitWords, line, col);
                if (UnitWords[word] != "pixels")
                {
                    // The units engine arrives in U2; until then a component is placed in pixels only.
                    throw new JgsRuntimeException(line, col,
                        $"A component is placed in pixels in this build, so Units cannot be '{UnitWords[word]}' yet.");
                }
            });

        Put(table, "Enable",
            entry => JgsValue.Str(EnableWords[(int)((UiObject)entry.Target).Enable]),
            (entry, value, line, col) => ((UiObject)entry.Target).Enable =
                (UiEnable)Word(entry, "Enable", value, EnableWords, EnableWords, line, col));

        Put(table, "Visible",
            entry => OnOff(entry.Target.Visible),
            (entry, value, line, col) => entry.Target.Visible = ComponentOnOff(entry, "Visible", value, line, col));

        foreach (string name in new[] { "Tooltip", "TooltipString" })
        {
            Put(table, name,
                entry => TextValue(((UiObject)entry.Target).Tooltip),
                (entry, value, line, col) => ((UiObject)entry.Target).Tooltip = ComponentTooltip(entry, value, line, col));
        }
    }

    // --- value shapes ----------------------------------------------------------------------------

    /// <summary>A component's text as R2025b hands it back: a char row, a char matrix, or a column cell.</summary>
    internal static JgsValue TextValue(UiText text)
    {
        switch (text.Form)
        {
            case UiTextForm.CharMatrix:
                return JgsValue.CharMatrix([.. text.Lines]);
            case UiTextForm.Cell:
            {
                JgsValue[] lines = [.. text.Lines.Select(static line => JgsValue.Str(line))];
                JgsValue cell = JgsValue.Cell(lines);
                cell.Reshape(lines.Length, lines.Length == 0 ? 0 : 1);
                return cell;
            }

            default:
                return JgsValue.Str(text.Lines.Count == 0 ? string.Empty : text.Lines[0]);
        }
    }

    private static JgsValue NumbersValue(UiNumbers numbers)
    {
        JgsValue value = JgsMatrix.FromColumnMajor([.. numbers.Data], numbers.Rows, numbers.Columns);
        value.Reshape(numbers.Rows, numbers.Columns);
        return value;
    }

    private static JgsValue UiColorValue(UiColor? color) =>
        color is { } rgb ? Row(rgb.R, rgb.G, rgb.B) : JgsValue.Str("none");

    // --- coercions -------------------------------------------------------------------------------

    /// <summary>
    /// R2025b's <c>String</c> coercions (U1): text stays text, a string scalar is a char row, a string
    /// array or a cell becomes a column of lines, a number is its <c>num2str</c>, an array of numbers
    /// one line per element, and a logical is refused.
    /// </summary>
    private static UiText ControlString(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (value.IsCharMatrix)
        {
            return new UiText(UiTextForm.CharMatrix, value.CharMatrixRows());
        }

        if (value.Type == JgsType.String || (kind == "char" && IsEmptyArray(value)))
        {
            return UiText.Of(value.Type != JgsType.String ? string.Empty
                : value.IsStringArray ? MissingAsEmpty(value.AsString) : value.AsString);
        }

        if (kind == "string")
        {
            if (value.ArrayLength == 1)
            {
                string text = JgsBuiltins.TextOf(value);
                return UiText.Of(JgsBuiltins.IsMissingText(text) ? string.Empty : text);
            }

            return new UiText(UiTextForm.Cell,
                [.. Enumerable.Range(0, value.ArrayLength).Select(i => MissingAsEmpty(JgsBuiltins.TextOf(value.ElementAt(i))))]);
        }

        if (value.Type == JgsType.Cell)
        {
            return new UiText(UiTextForm.Cell, [.. value.AsCell.Select(element => CellLine(entry, element, line, col))]);
        }

        if (kind == "logical" || !IsNumericKind(kind))
        {
            throw ComponentError(entry, "String", "MATLAB:hg:datatypes:NumericOrStringDataType:ArrayClass",
                "Value must be a character vector, categorical array, string array, numeric array, or cell array of character vectors.",
                line, col);
        }

        if (IsEmptyArray(value))
        {
            return UiText.Empty;
        }

        double[] numbers = JgsBuiltins.ToDoubles("String", value, line, col);
        if (numbers.Length == 1)
        {
            return UiText.Of(NumberLine(numbers[0], line, col));
        }

        string[] rows = [.. numbers.Select(number => NumberLine(number, line, col))];
        int width = rows.Max(static row => row.Length);
        return new UiText(UiTextForm.CharMatrix, [.. rows.Select(row => row.PadRight(width))]);
    }

    /// <summary>An empty array — never a scalar, whose length is not stored as an array's.</summary>
    private static bool IsEmptyArray(JgsValue value) => value.Type == JgsType.Array && value.ArrayLength == 0;

    private static string MissingAsEmpty(string text) => JgsBuiltins.IsMissingText(text) ? string.Empty : text;

    private static string CellLine(JgsHandleEntry entry, JgsValue element, int line, int col)
    {
        if (JgsBuiltins.IsTextScalar(element))
        {
            return MissingAsEmpty(JgsBuiltins.TextOf(element));
        }

        if (element.Type is JgsType.Number or JgsType.Bool)
        {
            return NumberLine(element.AsNumber, line, col);
        }

        if (element.Type == JgsType.String || (element.IsCharMatrix && element.CharMatrixRows().Length == 1))
        {
            return element.IsCharMatrix ? element.CharMatrixRows()[0] : element.AsString;
        }

        throw ComponentError(entry, "String", "MATLAB:hg:datatypes:NumericOrStringDataType:ArrayClass",
            "Value must be a character vector, categorical array, string array, numeric array, or cell array of character vectors.",
            line, col);
    }

    private static string NumberLine(double number, int line, int col) =>
        JgsBuiltins.NumberText([JgsValue.Number(number)], line, col).AsString;

    /// <summary>R2025b's <c>Value</c>: any numeric or logical array, kept as given, held as double.</summary>
    private static UiNumbers ControlValue(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind != "logical" && !IsNumericKind(kind))
        {
            throw ComponentError(entry, "Value", "MATLAB:hg:UIControlValueCharArrayString",
                "This is not a valid UIControlValue value. Value must be numeric.", line, col);
        }

        if (value.Type != JgsType.Array)
        {
            return new UiNumbers(JgsBuiltins.ToDoubles("Value", value, line, col), 1, 1);
        }

        double[] numbers = value.ArrayLength == 0 ? [] : JgsBuiltins.ToDoubles("Value", value, line, col);
        return new UiNumbers(numbers, value.Rows, value.Cols);
    }

    private static double NumericScalar(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind != "logical" && !IsNumericKind(kind))
        {
            throw ComponentError(entry, property, "MATLAB:class:RequireNumeric", "Value must be numeric or logical.", line, col);
        }

        double[] numbers = JgsBuiltins.ToDoubles(property, value, line, col);
        return numbers.Length == 1
            ? numbers[0]
            : throw ComponentError(entry, property, "MATLAB:class:RequireScalar", "Value must be a scalar.", line, col);
    }

    private static double[] NumericVector(JgsHandleEntry entry, string property, JgsValue value, int count, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        double[] numbers = kind == "logical" || IsNumericKind(kind)
            ? JgsBuiltins.ToDoubles(property, value, line, col)
            : [];
        return numbers.Length == count && numbers.All(double.IsFinite)
            ? numbers
            : throw ComponentError(entry, property, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:ExpectedNumeric",
                $"Value must be a {count} element numeric vector", line, col);
    }

    /// <summary>R2025b's <c>Position</c>: four finite numbers in any shape, width and height not negative.</summary>
    private static Rect2D ComponentPosition(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind != "logical" && !IsNumericKind(kind))
        {
            throw ComponentError(entry, property, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:ExpectedNumeric",
                "Value must be numeric and finite", line, col);
        }

        double[] box = JgsBuiltins.ToDoubles(property, value, line, col);
        if (box.Length != 4)
        {
            throw ComponentError(entry, property, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:Expected4ElementVector",
                "Value must be a 4 element vector", line, col);
        }

        if (!box.All(double.IsFinite))
        {
            throw ComponentError(entry, property, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:ExpectedNumeric",
                "Value must be numeric and finite", line, col);
        }

        return box[2] < 0 || box[3] < 0
            ? throw ComponentError(entry, property, "MATLAB:hg:set_chck:DimensionsOutsideRange",
                "Width and height must be greater than or equal to 0", line, col)
            : new Rect2D(box[0], box[1], box[2], box[3]);
    }

    /// <summary>R2025b's <c>FontSize</c>: a finite number, not negative — and zero refused bare.</summary>
    private static double FontSizeOf(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        const string prefix = "MATLAB:datatypes:PositiveWithZeroDataType:";
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        double[] numbers = kind == "logical" || IsNumericKind(kind)
            ? JgsBuiltins.ToDoubles("FontSize", value, line, col)
            : [];
        if (numbers.Length != 1)
        {
            throw ComponentError(entry, "FontSize", prefix + "MustBeANumericScalar", "Value should be a numeric scalar.", line, col);
        }

        double size = numbers[0];
        if (double.IsNaN(size))
        {
            throw ComponentError(entry, "FontSize", prefix + "MustBeNotNaN", "Value should be a number.", line, col);
        }

        if (double.IsInfinity(size))
        {
            throw ComponentError(entry, "FontSize", prefix + "MustBeFinite", "Value should be a finite double number.", line, col);
        }

        if (size < 0)
        {
            throw ComponentError(entry, "FontSize", prefix + "MustBeNotNegative",
                "Value should be a double number greater than or equal to 0.", line, col);
        }

        // R2025b refuses zero with a sentence of its own and without saying which property it was.
        return size > 0
            ? size
            : throw new JgsRuntimeException(line, col, prefix + "MustBePositive", "Value should be a double number greater than 0.");
    }

    private static bool ComponentOnOff(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Bool or JgsType.Number)
        {
            return value.IsTruthy;
        }

        if (JgsBuiltins.IsTextScalar(value))
        {
            string word = JgsBuiltins.TextOf(value);
            if (word.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (word.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        throw ComponentError(entry, property, "MATLAB:datatypes:onoffboolean:UnknownOnOffBooleanValue",
            "Invalid enum value. Use one of these values: 'on' | 'off'.", line, col);
    }

    private static UiText ComponentTooltip(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Cell)
        {
            return new UiText(UiTextForm.Cell, [.. value.AsCell.Select(element => CellLine(entry, element, line, col))]);
        }

        if (JgsBuiltins.IsTextScalar(value))
        {
            return UiText.Of(MissingAsEmpty(JgsBuiltins.TextOf(value)));
        }

        if (value.Type == JgsType.String || (value.IsCharMatrix))
        {
            return value.IsCharMatrix ? new UiText(UiTextForm.CharMatrix, value.CharMatrixRows()) : UiText.Of(value.AsString);
        }

        throw ComponentError(entry, "Tooltip", "MATLAB:graphics:datatype:UIStringsDataType:NoNumericValue",
            "UIStrings data type does not support numeric values.", line, col);
    }

    /// <summary>
    /// A component colour as R2025b takes it (U1): an RGB triplet in [0, 1] of any numeric class
    /// (integers scaled from their range), a short or long colour name, a hex code, or <c>'none'</c>.
    /// Kept as doubles, so what was written is what reads back.
    /// </summary>
    private static UiColor? ComponentColor(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        const string triplet = "Invalid RGB triplet. Specify a three-element vector of values between 0 and 1.";
        if (JgsBuiltins.IsTextScalar(value))
        {
            string word = JgsBuiltins.TextOf(value).Trim();
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (NamedColor(word) is { } named)
            {
                return named;
            }

            throw ComponentError(entry, property, "MATLAB:datatypes:RGBAColor:ParseError",
                "Invalid color name or hexadecimal color code. Valid names include: 'red', 'green', 'blue', 'cyan', 'magenta', 'yellow', 'black', 'white', and 'none'. Valid hexadecimal color codes consist of '#' followed by three or six hexadecimal digits.",
                line, col);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind == "logical" || !IsNumericKind(kind))
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:RGBAColor:ValueMustBe3or4ElementVector", triplet, line, col);
        }

        double[] rgb = JgsBuiltins.ToDoubles(property, value, line, col);
        if (rgb.Length != 3)
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:RGBAColor:ValueMustBe3or4ElementVector", triplet, line, col);
        }

        double scale = kind switch
        {
            "uint8" => 255,
            "uint16" => 65535,
            _ => 1,
        };

        for (int i = 0; i < 3; i++)
        {
            rgb[i] /= scale;
            if (!(rgb[i] >= 0 && rgb[i] <= 1))
            {
                throw ComponentError(entry, property, "MATLAB:hg:ColorBase:BadColorValue", triplet, line, col);
            }
        }

        return new UiColor(rgb[0], rgb[1], rgb[2]);
    }

    private static UiColor? NamedColor(string word)
    {
        switch (word.ToLowerInvariant())
        {
            case "r" or "red": return new UiColor(1, 0, 0);
            case "g" or "green": return new UiColor(0, 1, 0);
            case "b" or "blue": return new UiColor(0, 0, 1);
            case "c" or "cyan": return new UiColor(0, 1, 1);
            case "m" or "magenta": return new UiColor(1, 0, 1);
            case "y" or "yellow": return new UiColor(1, 1, 0);
            case "k" or "black": return new UiColor(0, 0, 0);
            case "w" or "white": return new UiColor(1, 1, 1);
        }

        if (word.Length is 4 or 7 && word[0] == '#'
            && word.Skip(1).All(Uri.IsHexDigit))
        {
            string digits = word.Length == 4
                ? string.Concat(word.Skip(1).Select(static c => new string(c, 2)))
                : word[1..];
            double Channel(int at) => int.Parse(digits.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return new UiColor(Channel(0), Channel(2), Channel(4));
        }

        return null;
    }

    /// <summary>
    /// One of a property's words, as R2025b matches them: any case, or a prefix that only one word
    /// starts with. <paramref name="shown"/> is the list its refusal names, which leaves out the older
    /// words R2025b still takes. Answers the word's index in <paramref name="words"/>.
    /// </summary>
    private static int Word(
        JgsHandleEntry entry, string property, JgsValue value, string[] words, string[] shown, int line, int col)
    {
        string list = string.Join(" | ", shown.Select(static word => $"'{word}'"));
        if (!JgsBuiltins.IsTextScalar(value))
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:InvalidEnumValueFor",
                $"Invalid enum value. Use one of these values: {list}.", line, col);
        }

        string typed = JgsBuiltins.TextOf(value);
        int exact = Array.FindIndex(words, word => word.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0)
        {
            return exact;
        }

        int[] starts = typed.Length == 0
            ? []
            : [.. Enumerable.Range(0, words.Length).Where(i => words[i].StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
        return starts.Length == 1
            ? starts[0]
            : throw ComponentError(entry, property, "MATLAB:datatypes:InvalidEnumValue",
                $"'{typed}' is not a valid value. Use one of these values: {list}.", line, col);
    }

    /// <summary>
    /// A component's refusal in R2025b's words. Through <c>set</c> or the dot it names the property
    /// and the class first, on a line of its own; inside the creating call it does not.
    /// </summary>
    private static JgsRuntimeException ComponentError(
        JgsHandleEntry entry, string property, string identifier, string reason, int line, int col) =>
        new(line, col, identifier, _creatingComponent
            ? reason
            : $"Error setting property '{property}' of class '{JgsGraphicsCallbackValues.ClassWord(entry.Target)}':\n{reason}");
}
