using System.Text.RegularExpressions;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The property surfaces of the <c>uifigure</c> components and of <c>uigridlayout</c> (app-building
/// plan, U5). Each class is R2025b's own, with its own names; the words, coercions and refusals were
/// recorded headless against one battery of values (probes <c>u5_matrix</c>, <c>u5_forms</c>,
/// <c>u5_grid</c>). Two families of refusal appear: the graphics data types', which name the property
/// and the class first, as a <c>uicontrol</c>'s do, and the components' own, <c>MATLAB:ui:Class:…</c>,
/// which say only what the value must be.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static readonly string[] BusyActionWords = ["queue", "cancel"];
    private static readonly string[] AutoManualWords = ["auto", "manual"];
    private static readonly string[] VerticalWords = ["top", "center", "bottom"];
    private static readonly string[] InterpreterWords = ["none", "html", "latex", "tex"];
    private static readonly string[] InputTypeWords = ["text", "letters", "digits", "alphanumerics"];
    private static readonly string[] IconAlignmentWords = ["left", "right", "center", "top", "bottom", "leftmargin", "rightmargin"];
    private static readonly string[] ScaleMethodWords = ["fit", "fill", "none", "scaledown", "scaleup", "stretch"];
    private static readonly string[] OrientationWords = ["horizontal", "vertical"];
    private static readonly string[] StockIcons = ["error", "warning", "info", "success", "question"];

    /// <summary>
    /// The few properties of a component to which a string scalar is not the same as a character
    /// row: a list's items and data, a slider's labels, a number, a position in a list.
    /// </summary>
    internal static bool KeepsStringScalar(GraphObject target, string name) => target switch
    {
        UiItemsModel => name.Equals("Items", StringComparison.OrdinalIgnoreCase) || name.Equals("ItemsData", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ValueIndex", StringComparison.OrdinalIgnoreCase),
        UiGaugeModel => name.Equals("MajorTickLabels", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ScaleColors", StringComparison.OrdinalIgnoreCase) || name.Equals("ScaleColorLimits", StringComparison.OrdinalIgnoreCase),
        UiScaleModel => name.Equals("MajorTickLabels", StringComparison.OrdinalIgnoreCase),
        UiNumericModel => name.Equals("Value", StringComparison.OrdinalIgnoreCase),
        UiGridLayoutModel => name.Equals("RowHeight", StringComparison.OrdinalIgnoreCase) || name.Equals("ColumnWidth", StringComparison.OrdinalIgnoreCase),
        UiTableModel => name.Equals("Data", StringComparison.OrdinalIgnoreCase) || name.Equals("ColumnEditable", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ColumnSortable", StringComparison.OrdinalIgnoreCase) || name.Equals("ColumnFormat", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Selection", StringComparison.OrdinalIgnoreCase),
        UiToolModel => name.Equals("CData", StringComparison.OrdinalIgnoreCase),
        UiTabGroupModel => name.Equals("SelectedTab", StringComparison.OrdinalIgnoreCase),
        MenuItemModel => name.Equals("Position", StringComparison.OrdinalIgnoreCase),
        UiTreeNodeModel => name.Equals("NodeData", StringComparison.OrdinalIgnoreCase),
        UiDatePickerModel => name.Equals("DisplayFormat", StringComparison.OrdinalIgnoreCase),
        UiHtmlModel => name.Equals("Data", StringComparison.OrdinalIgnoreCase) || name.Equals("HTMLSource", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DataChangedFcn", StringComparison.OrdinalIgnoreCase) || name.Equals("HTMLEventReceivedFcn", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>
    /// R2025b writes a component's property through <c>isequal</c> first (probe <c>u9_extra</c>, U9):
    /// a value equal to the one held is not written at all — so <c>false</c> lands on a knob at 0
    /// where <c>true</c> is refused, and <c>[]</c> on a picker with no disabled dates — and a datetime
    /// met by text, or text by a datetime, is converted on the way, which warns when the text is no
    /// date. The comparison is this build's <c>isequal</c>; a property that cannot be read is written.
    /// </summary>
    private static bool SkipsEqualWrite(JgsHandleEntry entry, GraphicsProperty property, JgsValue value)
    {
        JgsValue current;
        try
        {
            current = property.Read(entry);
        }
        catch (JgsRuntimeException)
        {
            return false;
        }

        // An empty datetime and an empty cell of the same shape compare equal there: datetime({})
        // is the empty datetime (probe u9_props: {} lands on a picker with no disabled dates).
        if (current.IsDatetime && current.ArrayLength == 0 && value.Type == JgsType.Cell && value.AsCell.Length == 0
            && current.Rows == value.Rows && current.Cols == value.Cols)
        {
            return true;
        }

        // Only values of one kind and shape are compared: a number against a number or a logical,
        // text against text. [] is not '' here — [] on a Tag of '' is refused, '' on data of [] is
        // written — though false is 0 (probe u9_props); an empty of another shape is written.
        if (current.Type == JgsType.Array && value.Type == JgsType.Array && !current.Dims.SequenceEqual(value.Dims))
        {
            return false;
        }

        return KindOf(current) == KindOf(value) && JgsBuiltins.IsEqualValues(current, value);
    }

    private static string KindOf(JgsValue value) => value.Type switch
    {
        JgsType.String => "text",
        JgsType.Array when value.IsStringArray => "text",
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Bool or JgsType.Number or JgsType.Complex or JgsType.Array => "number",
        _ => value.Type.ToString(),
    };

    private const string AutoConvertWarning = "Unable to convert text array to a datetime array because the format was not recognized.";

    /// <summary>
    /// R2025b's validators compare a text property with what is written through <c>isequal</c>,
    /// which converts a datetime's partner to a datetime and warns when the text is no date (probe
    /// <c>u9_extra</c>). So a datetime written to a word, a line of text or the items warns before
    /// it is refused; the on/off and enumeration properties convert first and do not.
    /// </summary>
    private static void WarnDatetimeAgainstText(JgsValue value, bool textHeld = true)
    {
        if (value.IsDatetime && textHeld)
        {
            PropertyWarning("MATLAB:datetime:AutoConvertStrings", AutoConvertWarning);
        }
    }

    /// <summary>
    /// The other way about: text written to a datetime property — a character row, a string scalar
    /// or a cell of text — is converted on the way and warns when it is no date (R2025b).
    /// </summary>
    private static void WarnTextAgainstDatetime(JgsValue text)
    {
        IEnumerable<string>? texts = text.Type == JgsType.String && !text.IsCharMatrix && text.AsString.Length > 0 ? [text.AsString]
            : text.IsStringArray && text.ArrayLength == 1 ? [JgsBuiltins.TextOf(text.ElementAt(0))]
            : text.Type == JgsType.Cell && text.AsCell.Length > 0 && Array.TrueForAll(text.AsCell, JgsBuiltins.IsTextScalar)
                ? text.AsCell.Select(JgsBuiltins.TextOf)
            : null;
        if (texts is not null && texts.Any(static t => !JgsTime.TryParse(t, null, out _)))
        {
            PropertyWarning("MATLAB:datetime:AutoConvertStrings", AutoConvertWarning);
        }
    }

    /// <summary>
    /// The width over the height a component keeps whatever rectangle it is given (probe
    /// <c>u9_extra</c>): the round ones and the semicircular gauge their shape, a switch its track.
    /// </summary>
    private static double? AspectRatioOf(UiObject component) => component switch
    {
        UiKnobModel or UiDiscreteKnobModel or UiLampModel => 1,
        UiGaugeModel { Style: UiGaugeStyle.Circular or UiGaugeStyle.NinetyDegree } => 1,
        UiGaugeModel { Style: UiGaugeStyle.Semicircular } => 120.0 / 65,
        UiSwitchModel toggle => toggle.Vertical ? 20.0 / 45 : 45.0 / 20,
        _ => null,
    };

    /// <summary>MATLAB's class of an object, in full, where its property errors spell it so.</summary>
    internal static string FullClassOf(GraphObject target) => target switch
    {
        UiControlModel => "matlab.ui.control.UIControl",
        UiComponentContainerModel area => area.ClassName, // a custom component (U10)
        UiButtonGroupModel => "matlab.ui.container.ButtonGroup",
        UiProgressIndicatorModel => "matlab.ui.control.internal.ProgressIndicator",
        UiPanelModel => "matlab.ui.container.Panel",
        UiGridLayoutModel => "matlab.ui.container.GridLayout",
        UiTabModel => "matlab.ui.container.Tab",
        UiTabGroupModel => "matlab.ui.container.TabGroup",
        UiToolModel tool => tool.IsToggle ? "matlab.ui.container.toolbar.ToggleTool" : "matlab.ui.container.toolbar.PushTool",
        UiToolbarModel => "matlab.ui.container.Toolbar",
        MenuItemModel => "matlab.ui.container.Menu",
        ContextMenuModel => "matlab.ui.container.ContextMenu",
        UiTreeNodeModel => "matlab.ui.container.TreeNode",
        UiTreeModel tree => tree.CheckBoxes ? "matlab.ui.container.CheckBoxTree" : "matlab.ui.container.Tree",
        UiComponentModel => "matlab.ui.control." + JgsGraphicsCallbackValues.ClassWord(target),
        FigureModel => "matlab.ui.Figure",
        AxesModel { IsUiAxes: true } => "matlab.ui.control.UIAxes",
        _ => JgsGraphicsCallbackValues.ClassWord(target),
    };

    private static string Cls(JgsHandleEntry entry) => JgsGraphicsCallbackValues.ClassWord(entry.Target);

    /// <summary>One of a component's own refusals: <c>MATLAB:ui:Class:id</c>, and the sentence alone.</summary>
    private static JgsRuntimeException UiError(JgsHandleEntry entry, string id, string message, int line, int col) =>
        new(line, col, $"MATLAB:ui:{Cls(entry)}:{id}", message);

    // --- shared coercions ------------------------------------------------------------------------

    /// <summary>
    /// R2025b's on/off state: <c>'on'</c> and <c>'off'</c>, or any numeric or logical scalar, where
    /// only zero is off. Other text is refused one way and anything else another.
    /// </summary>
    private static bool OnOffState(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        const string words = "Invalid enum value. Use one of these values: 'on' | 'off'.";
        if (value.Type is JgsType.Bool or JgsType.Number or JgsType.Complex)
        {
            return value.Type == JgsType.Complex ? value.AsComplex != System.Numerics.Complex.Zero : value.AsNumber != 0;
        }

        if (value.Type == JgsType.String || value.IsCharMatrix)
        {
            // Blanks around the word are ignored (probe u9_extra: 'off ' and ' on' are taken).
            string word = value.IsCharMatrix ? string.Empty : value.AsString.Trim();
            if (JgsBuiltins.IsMissingText(word))
            {
                throw ComponentError(entry, property, "MATLAB:datatypes:onoffboolean:IncorrectValue", words, line, col);
            }

            return word.Equals("on", StringComparison.OrdinalIgnoreCase) ? true
                : word.Equals("off", StringComparison.OrdinalIgnoreCase) ? false
                : throw ComponentError(entry, property, "MATLAB:datatypes:onoffboolean:UnknownOnOffBooleanValue", words, line, col);
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsStringArray && !value.IsTime
            && value.ElementAt(0).Type is JgsType.Bool or JgsType.Number or JgsType.Complex)
        {
            return OnOffState(entry, property, value.ElementAt(0), line, col);
        }

        throw ComponentError(entry, property, "MATLAB:datatypes:onoffboolean:IncorrectValue", words, line, col);
    }

    /// <summary>
    /// One of a graphics enumeration's words, R2025b's way for a component: the word in any case, or
    /// a start of it that only one word has. Not text, an unknown word and no text at all are three
    /// different refusals.
    /// </summary>
    private static string EnumWord(JgsHandleEntry entry, string property, JgsValue value, string[] words, int line, int col)
    {
        string list = string.Join(" | ", words.Select(static word => $"'{word}'"));
        if (value.Type != JgsType.String && !value.IsCharMatrix)
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:InvalidEnumValueFor",
                $"Invalid enum value. Use one of these values: {list}.", line, col);
        }

        // Blanks around the word are ignored (probe u9_props: 'off ' is HandleVisibility off), though
        // a refusal quotes the text as typed.
        string typed = value.IsCharMatrix ? ColumnMajorText(value) : value.AsString;
        if (JgsBuiltins.IsMissingText(typed))
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:InvalidEnumValueFor",
                $"Invalid enum value. Use one of these values: {list}.", line, col);
        }

        if (typed.Length == 0)
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:InvalidValue",
                $"Value cannot be empty. Use one of these values: {list}.", line, col);
        }

        return Matching(typed.Trim(), words)
            ?? throw ComponentError(entry, property, "MATLAB:datatypes:InvalidEnumValue",
                $"'{typed}' is not a valid value. Use one of these values: {list}.", line, col);
    }

    /// <summary>The word text names: itself in any case, or the one word it begins.</summary>
    private static string? Matching(string typed, string[] words)
    {
        if (typed.Length == 0)
        {
            return null;
        }

        string? exact = Array.Find(words, word => word.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        string[] starts = [.. words.Where(word => word.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
        return starts.Length == 1 ? starts[0] : null;
    }

    /// <summary>
    /// One of a component's own word lists, refused with the component's own sentence. The word is
    /// taken in any case; only <c>InputType</c> also takes a start of one (probe <c>u5_matrix</c>).
    /// </summary>
    private static string UiWord(
        JgsHandleEntry entry, JgsValue value, string[] words, string id, string message, int line, int col, bool starts = false)
    {
        WarnDatetimeAgainstText(value);
        return (value.Type == JgsType.String && !JgsBuiltins.IsMissingText(value.AsString)
            ? starts ? Matching(value.AsString, words) : Array.Find(words, word => word.Equals(value.AsString, StringComparison.OrdinalIgnoreCase))
            : null)
            ?? throw UiError(entry, id, message, line, col);
    }

    /// <summary>A character matrix read down its columns, which is how R2025b reads one as a word.</summary>
    private static string ColumnMajorText(JgsValue value)
    {
        string[] rows = value.CharMatrixRows();
        var text = new System.Text.StringBuilder();
        for (int c = 0; rows.Length > 0 && c < rows[0].Length; c++)
        {
            foreach (string row in rows)
            {
                text.Append(row[c]);
            }
        }

        return text.ToString();
    }

    /// <summary>A double's 0-by-0 empty, which R2025b takes for no text where the text is already empty.</summary>
    private static bool IsEmptyDouble(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0 && value.Rows == 0 && value.Cols == 0 && !value.IsStringArray && !value.IsCharMatrix
        && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double";

    /// <summary>
    /// One line of text: a character row or a string scalar, a missing string being none. A
    /// <c>[]</c> written over text that is already empty changes nothing, as in R2025b.
    /// </summary>
    private static string OneLine(JgsHandleEntry entry, JgsValue value, string current, string id, string message, int line, int col)
    {
        WarnDatetimeAgainstText(value, current.Length > 0);
        if (value.Type == JgsType.String)
        {
            return MissingAsEmpty(value.AsString);
        }

        if (IsEmptyDouble(value) && current.Length == 0)
        {
            return current;
        }

        throw UiError(entry, id, message, line, col);
    }

    /// <summary>
    /// A caption: a character row, or a vector of lines given as a cell of text or a string array,
    /// which reads back as a column cell.
    /// </summary>
    private static UiText Caption(JgsHandleEntry entry, JgsValue value, UiText current, string id, string property, int line, int col)
    {
        string message = $"'{property}' must be a character vector, or a N-by-1 array of the following type: cell array of character vectors, string, or categorical.";
        WarnDatetimeAgainstText(value, current.Lines.Any(static l => l.Length > 0));
        if (value.Type == JgsType.String)
        {
            return UiText.Of(MissingAsEmpty(value.AsString));
        }

        if (IsEmptyDouble(value) && current.Lines.All(static l => l.Length == 0) && current.Form == UiTextForm.CharRow)
        {
            return current;
        }

        if (TextVector(value) is { Length: > 0 } lines)
        {
            return new UiText(UiTextForm.Cell, lines);
        }

        throw UiError(entry, id, message, line, col);
    }

    /// <summary>
    /// The lines of a vector of text — a cell whose every element is a character row or a string
    /// scalar, or a string array — or null when the value is neither or is not a vector.
    /// </summary>
    private static string[]? TextVector(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            if (value.Rows > 1 && value.Cols > 1)
            {
                return null;
            }

            JgsValue[] cells = value.AsCell;
            var lines = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                if (!JgsBuiltins.IsTextScalar(cells[i]))
                {
                    return null;
                }

                lines[i] = MissingAsEmpty(JgsBuiltins.TextOf(cells[i]));
            }

            return lines;
        }

        if (value.IsStringArray)
        {
            if (value.Rows > 1 && value.Cols > 1)
            {
                return null;
            }

            return [.. Enumerable.Range(0, value.ArrayLength).Select(i => MissingAsEmpty(JgsBuiltins.TextOf(value.ElementAt(i))))];
        }

        return null;
    }

    private static JgsValue RowCell(IReadOnlyList<string> items)
    {
        JgsValue cell = JgsValue.Cell([.. items.Select(static item => JgsValue.Str(item))]);
        if (items.Count == 0)
        {
            cell.Reshape(0, 0);
        }

        return cell;
    }

    private static JgsValue EmptyCell()
    {
        JgsValue cell = JgsValue.Cell([]);
        cell.Reshape(0, 0);
        return cell;
    }

    /// <summary>
    /// A colour that cannot be <c>'none'</c>: R2025b's <c>RGBColor</c>, whose refusals differ from
    /// the <c>RGBAColor</c> a label's background takes.
    /// </summary>
    private static UiColor SolidColor(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        const string triplet = "Invalid RGB triplet. Specify a three-element vector of values between 0 and 1.";
        if (value.Type == JgsType.String || value.IsCharMatrix)
        {
            string word = value.IsCharMatrix ? string.Empty : value.AsString.Trim();
            return NamedColor(word) ?? throw ComponentError(entry, property, "MATLAB:datatypes:RGBColor:ParseError",
                "Invalid color name or hexadecimal color code. Valid names include: 'red', 'green', 'blue', 'cyan', 'magenta', 'yellow', 'black', and 'white'. Valid hexadecimal color codes consist of '#' followed by three or six hexadecimal digits.",
                line, col);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        double[] rgb = kind != "logical" && IsNumericKind(kind) && value.Type != JgsType.Complex
            ? JgsBuiltins.ToDoubles(property, value, line, col)
            : [];
        if (rgb.Length != 3)
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:RGBColor:ValueMustBe3ElementVector", triplet, line, col);
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

    /// <summary>A colour that may be <c>'none'</c>, with four numbers read as a colour and an opacity.</summary>
    private static UiColor? ClearableColor(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix)
        {
            value = JgsValue.Str("?");
        }

        if (value.Type == JgsType.Complex)
        {
            throw ComponentError(entry, property, "MATLAB:datatypes:RGBAColor:ValueMustBe3or4ElementVector",
                "Invalid RGB triplet. Specify a three-element vector of values between 0 and 1.", line, col);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);

        // Three or four logicals are a colour, and a colour with an opacity (R2025b, fixture u8_props).
        if (value.Type == JgsType.Array && kind == "logical" && value.ArrayLength is 3 or 4)
        {
            double[] flags = JgsBuiltins.ToDoubles(property, value, line, col);
            return new UiColor(flags[0], flags[1], flags[2]);
        }

        if (value.Type == JgsType.Array && !value.IsStringArray && value.ArrayLength == 4 && kind != "logical" && IsNumericKind(kind))
        {
            double[] rgba = JgsBuiltins.ToDoubles(property, value, line, col);
            return rgba.All(static x => x >= 0 && x <= 1)
                ? new UiColor(rgba[0], rgba[1], rgba[2])
                : throw ComponentError(entry, property, "MATLAB:hg:ColorBase:BadColorValue",
                    "Invalid RGB triplet. Specify a three-element vector of values between 0 and 1.", line, col);
        }

        return ComponentColor(entry, property, value, line, col);
    }

    /// <summary>A real scalar of class double, or null: what a component's numeric <c>Value</c> must be.</summary>
    private static double? DoubleScalar(JgsValue value) =>
        value.Type == JgsType.Number && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double" ? value.AsNumber
        : value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsStringArray && !value.IsCharMatrix
          && value.ElementAt(0).Type == JgsType.Number && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double"
            ? value.ElementAt(0).AsNumber
            : null;

    /// <summary>The real numbers of a numeric array of any numeric class, or null for anything else.</summary>
    private static double[]? RealNumbers(JgsValue value, bool logicalToo = false)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (value.IsStringArray || value.IsCharMatrix || value.Type is JgsType.String or JgsType.Complex
            || (kind == "logical" && !logicalToo) || (kind != "logical" && !IsNumericKind(kind)))
        {
            return null;
        }

        if (value.Type == JgsType.Array && Enumerable.Range(0, value.ArrayLength).Any(i => value.ElementAt(i).Type == JgsType.Complex))
        {
            return null;
        }

        return value.Type is JgsType.Number or JgsType.Bool or JgsType.Array ? JgsBuiltins.ToDoubles("value", value, 0, 0) : null;
    }

    private static bool IsRowOrScalar(JgsValue value) => value.Type != JgsType.Array || value.Rows == 1;

    // --- the block every component shares ----------------------------------------------------------

    /// <summary>What a <c>uifigure</c> component and a grid have in place of a <c>uicontrol</c>'s common block.</summary>
    private static void AddUiComponentCommon(Type type, IDictionary<string, GraphicsProperty> table)
    {
        // A component is always placed in pixels and has no Units to say otherwise.
        table.Remove("Units");
        table.Remove("TooltipString");
        table.Remove("Selected");
        table.Remove("SelectionHighlight");
        table.Remove("HitTest");
        table.Remove("UIContextMenu");
        table.Remove("ButtonDownFcn");

        bool isGrid = typeof(UiGridLayoutModel).IsAssignableFrom(type);
        if (!isGrid)
        {
            table.Remove("Children"); // only a container has any
        }

        foreach (string name in new[] { "Position", "InnerPosition", "OuterPosition" })
        {
            string captured = name;
            Put(table, captured,
                entry => ComponentRect(entry, captured),
                captured == "OuterPosition" || isGrid
                    ? null
                    : (entry, value, line, col) => SetComponentRect(entry, captured, value, line, col));
        }

        Put(table, "Visible",
            entry => OnOff(entry.Target.Visible),
            (entry, value, line, col) => entry.Target.Visible = OnOffState(entry, "Visible", value, line, col));
        AddSameFigureContextMenu(table);

        if (isGrid)
        {
            table.Remove("Enable");
        }
        else
        {
            Put(table, "Enable",
                entry => OnOff(((UiObject)entry.Target).Enable == UiEnable.On),
                (entry, value, line, col) => ((UiObject)entry.Target).Enable =
                    OnOffState(entry, "Enable", value, line, col) ? UiEnable.On : UiEnable.Off);
        }

        Put(table, "Tooltip",
            entry => TextValue(((UiObject)entry.Target).Tooltip),
            (entry, value, line, col) =>
            {
                var component = (UiObject)entry.Target;
                component.Tooltip = Caption(entry, value, component.Tooltip, "invalidTooltip", "Tooltip", line, col);
            });

        Put(table, "Tag",
            entry => JgsValue.Str(entry.Target.Tag ?? string.Empty),
            (entry, value, line, col) =>
            {
                if (value.IsCharMatrix)
                {
                    entry.Target.Tag = ColumnMajorText(value);
                    return;
                }

                if (value.IsStringArray && value.ArrayLength != 1)
                {
                    throw ComponentError(entry, "Tag", "MATLAB:class:RequireScalar", "Value must be a scalar.", line, col);
                }

                if (value.Type != JgsType.String)
                {
                    throw ComponentError(entry, "Tag", "MATLAB:class:RequireString",
                        "Value must be a character vector or a string scalar.", line, col);
                }

                entry.Target.Tag = JgsBuiltins.IsMissingText(value.AsString)
                    ? throw ComponentError(entry, "Tag", "MATLAB:string:MissingNotSupported", "<missing> string element not supported.", line, col)
                    : value.AsString;
            });

        Put(table, "HandleVisibility",
            entry => JgsValue.Str(entry.HandleVisibility),
            (entry, value, line, col) =>
                entry.HandleVisibility = EnumWord(entry, "HandleVisibility", value, HandleVisibilityWords, line, col));
        Put(table, "BusyAction",
            entry => JgsValue.Str(entry.BusyActionQueues ? "queue" : "cancel"),
            (entry, value, line, col) =>
                entry.BusyActionQueues = EnumWord(entry, "BusyAction", value, BusyActionWords, line, col) == "queue");
        Put(table, "Interruptible",
            entry => OnOff(entry.Interruptible),
            (entry, value, line, col) => entry.Interruptible = OnOffState(entry, "Interruptible", value, line, col));

        // Where it sits in a grid: R2025b's GridLayoutOptions, here a classed struct of Row and
        // Column. A radio or toggle button is only ever in a button group, and has none.
        if (typeof(UiRadioButtonModel).IsAssignableFrom(type) || typeof(UiToggleButtonModel).IsAssignableFrom(type))
        {
            table.Remove("Layout");
        }
        else
        {
            Put(table, "Layout", LayoutValue, SetLayout);
        }
    }

    /// <summary>A component's rectangle as a script reads it: the track for a slider, with its ticks in the outer one.</summary>
    private static JgsValue ComponentRect(JgsHandleEntry entry, string name)
    {
        var component = (UiObject)entry.Target;
        Rect2D box = component.PixelPosition();
        if (component is UiGridLayoutModel grid)
        {
            if (name == "InnerPosition")
            {
                IReadOnlyList<double> pad = grid.Padding;
                box = new Rect2D(box.X + pad[0], box.Y + pad[1],
                    System.Math.Max(0, box.Width - pad[0] - pad[2]), System.Math.Max(0, box.Height - pad[1] - pad[3]));
            }
        }
        else if (component is UiSliderModel slider && name == "OuterPosition")
        {
            box = component.Parent is UiGridLayoutModel holder ? holder.RectOf(slider) : UiSliderModel.OuterOf(box, slider.Vertical);
        }
        else if (name == "OuterPosition" && component is UiKnobModel knobModel)
        {
            box = knobModel.OuterOf(box);
        }
        else if (name == "OuterPosition" && component is UiDiscreteKnobModel dialModel)
        {
            box = dialModel.OuterOf(box);
        }
        else if (name == "OuterPosition" && component is UiSwitchModel switchModel)
        {
            box = switchModel.OuterOf(box);
        }

        return Row(box.X, box.Y, box.Width, box.Height);
    }

    private static void SetComponentRect(JgsHandleEntry entry, string name, JgsValue value, int line, int col)
    {
        var component = (UiObject)entry.Target;
        if (JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "logical" || value.IsCharMatrix)
        {
            throw ComponentError(entry, name, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:ExpectedNumeric", "Value must be numeric and finite", line, col);
        }

        if (value.Type == JgsType.Complex || (value.Type == JgsType.Array && value.Rows > 1 && value.Cols > 1))
        {
            throw ComponentError(entry, name, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:Expected4ElementVector", "Value must be a 4 element vector", line, col);
        }

        Rect2D box = ComponentPosition(entry, name, value, line, col);

        // A round component, a switch and a semicircular gauge keep their proportions (probe
        // u9_extra): the rectangle asked for is shrunk to the largest of that shape inside it.
        // R2025b's view warns about it later, off the script's thread; this build does not (ADR 0207).
        if (AspectRatioOf(component) is { } ratio && System.Math.Abs(box.Width - (box.Height * ratio)) > 1e-9)
        {
            double width = System.Math.Min(box.Width, box.Height * ratio);
            box = new Rect2D(box.X, box.Y, width, width / ratio);
        }

        // A grid places its children itself: R2025b warns and leaves the component where it is.
        if (component.Parent is UiGridLayoutModel)
        {
            PropertyWarning("MATLAB:ui:components:noPositionSetWhenInLayoutContainer",
                "Unable to set 'Position', 'InnerPosition', or 'OuterPosition' for components in 'GridLayout'.");
            return;
        }

        if (component is UiSliderModel slider)
        {
            // The track is 3 pixels thick whatever is asked for.
            bool resized = slider.Vertical ? box.Width != 3 : box.Height != 3;
            box = slider.Vertical ? new Rect2D(box.X, box.Y, 3, box.Height) : new Rect2D(box.X, box.Y, box.Width, 3);
            component.Position = box;
            if (resized)
            {
                PropertyWarning($"MATLAB:ui:{Cls(entry)}:{(slider.Vertical ? "fixedWidth" : "fixedHeight")}",
                    slider.Vertical ? "The width of this component cannot be changed." : "The height of this component cannot be changed.");
            }

            return;
        }

        component.Position = box;
    }

    // --- Layout ----------------------------------------------------------------------------------

    internal const string GridLayoutOptionsClass = "matlab.ui.layout.GridLayoutOptions";

    /// <summary>The grid a component or an axes sits in, or null.</summary>
    internal static UiGridLayoutModel? GridOf(GraphObject target) => ParentOf(target) as UiGridLayoutModel;

    private static JgsValue SpanValue(int from, int to) => from == to ? JgsValue.Number(from) : Row(from, to);

    /// <summary>
    /// MATLAB's <c>Layout</c>: the options object of a grid's child, with <c>Row</c> and
    /// <c>Column</c>; an empty for anything that is not in a grid.
    /// </summary>
    internal static JgsValue LayoutValue(JgsHandleEntry entry)
    {
        if (GridOf(entry.Target) is null)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        UiGridCell cell = UiGridLayoutModel.CellOf(entry.Target);
        JgsValue options = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Row"] = SpanValue(cell.Row, cell.RowEnd),
            ["Column"] = SpanValue(cell.Column, cell.ColumnEnd),
        });
        options.SetClassName(GridLayoutOptionsClass);
        return options;
    }

    internal static void SetLayout(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        UiGridLayoutModel? grid = GridOf(entry.Target);
        bool isOptions = value.Type == JgsType.Struct && value.ClassName == GridLayoutOptionsClass && !value.IsStructArray;
        if (!isOptions)
        {
            bool empty = value.Type == JgsType.Array && value.ArrayLength == 0;
            if (empty && grid is null)
            {
                return;
            }

            throw empty
                ? new JgsRuntimeException(line, col, "MATLAB:ui:components:invalidClassParentDependent",
                    "'Layout' must be a 'matlab.ui.layout.GridLayoutOptions' object when the parent is 'matlab.ui.container.GridLayout'.")
                : ComponentError(entry, "Layout", "MATLAB:ui:datatypes:LayoutOptionsDatatype:InvalidClass",
                    "'Layout' value must be specified as a matlab.ui.layout.LayoutOptions object.", line, col);
        }

        if (grid is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ui:components:invalidLayoutValueWithNonLayoutContainerParent",
                "'Layout' must be empty when the parent object is not a layout container.");
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        (int row, int rowEnd) = Span(fields.GetValueOrDefault("Row") ?? JgsValue.Number(1), "Row", line, col);
        (int column, int columnEnd) = Span(fields.GetValueOrDefault("Column") ?? JgsValue.Number(1), "Column", line, col);
        PlaceInGrid(grid, entry.Target, new UiGridCell(row, rowEnd, column, columnEnd));
    }

    /// <summary>R2025b's <c>Row</c> and <c>Column</c>: a positive whole number, or two that increase.</summary>
    private static (int From, int To) Span(JgsValue value, string name, int line, int col)
    {
        double[]? numbers = value.Type is JgsType.Number or JgsType.Array && IsRowOrScalar(value) ? RealNumbers(value) : null;
        bool whole = numbers is { Length: 1 or 2 } && numbers.All(static x => x >= 1 && x == System.Math.Floor(x) && x < int.MaxValue);
        if (!whole || (numbers!.Length == 2 && numbers[1] <= numbers[0]))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:ui:GridLayoutOptions:InvalidGrid{name}",
                $"'{name}' must be a positive integer or a 1x2 increasing array of positive integers.");
        }

        return ((int)numbers[0], (int)numbers[^1]);
    }

    /// <summary>Puts a grid's child in a cell, growing the grid to hold it.</summary>
    internal static void PlaceInGrid(UiGridLayoutModel grid, GraphObject child, UiGridCell cell)
    {
        switch (child)
        {
            case UiObject component:
                component.GridCell = cell;
                break;
            case AxesModel axes:
                axes.GridCell = cell;
                break;
        }

        grid.Hold(cell);
        if (child is AxesModel)
        {
            grid.PinAxes();
        }
    }

    /// <summary>
    /// A child has arrived in a grid, or left one (U5): it takes the next cell, or lets go of the
    /// one it had.
    /// </summary>
    internal static void GridMembershipChanged(GraphObject child)
    {
        UiGridLayoutModel? grid = GridOf(child);
        if (grid is null)
        {
            switch (child)
            {
                case UiObject component:
                    component.GridCell = null;
                    break;
                case AxesModel axes:
                    axes.GridCell = null;
                    break;
            }

            return;
        }

        UiGridCell? had = child switch
        {
            UiObject component => component.GridCell,
            AxesModel axes => axes.GridCell,
            _ => null,
        };
        // One that comes from another grid keeps the cell it had there, and this grid grows to
        // hold it; anything else takes the next cell.
        PlaceInGrid(grid, child, had ?? grid.NextCell(child));
    }

    // --- one class at a time ---------------------------------------------------------------------

    private static UiComponentModel Leaf(JgsHandleEntry entry) => (UiComponentModel)entry.Target;

    private static UiCaptionModel CaptionOf(JgsHandleEntry entry) => (UiCaptionModel)entry.Target;

    private static void AddUiComponentBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        AddUiComponentCommon(type, table);
        if (typeof(UiGridLayoutModel).IsAssignableFrom(type))
        {
            AddUiGridBlock(table);
            return;
        }

        bool caption = typeof(UiCaptionModel).IsAssignableFrom(type);
        bool hasFont = type != typeof(UiImageModel) && type != typeof(UiLampModel) && type != typeof(UiColorPickerModel) && type != typeof(UiHtmlModel);
        if (hasFont)
        {
            AddUiFontBlock(table);
        }

        // BackgroundColor: every class but a check box, a radio button and a slider. A label, an
        // image and a hyperlink may have none.
        bool clearable = type == typeof(UiLabelModel) || type == typeof(UiImageModel) || type == typeof(UiHyperlinkModel);
        if (type != typeof(UiCheckBoxModel) && type != typeof(UiRadioButtonModel) && !typeof(UiSliderModel).IsAssignableFrom(type)
            && type != typeof(UiKnobModel) && type != typeof(UiDiscreteKnobModel) && type != typeof(UiSwitchModel) && type != typeof(UiLampModel)
            && type != typeof(UiHtmlModel))
        {
            Put(table, "BackgroundColor",
                entry => UiColorValue(Leaf(entry).BackgroundColor),
                (entry, value, line, col) => Leaf(entry).BackgroundColor = clearable
                    ? ClearableColor(entry, "BackgroundColor", value, line, col)
                    : SolidColor(entry, "BackgroundColor", value, line, col));
        }

        if (caption)
        {
            AddCaptionBlock(type, table);
        }

        if (type == typeof(UiButtonModel))
        {
            AddNamedSlot(table, "ButtonPushedFcn");
        }

        if (type == typeof(UiStateButtonModel) || type == typeof(UiCheckBoxModel))
        {
            AddNamedSlot(table, "ValueChangedFcn");
        }

        if (type == typeof(UiHyperlinkModel))
        {
            AddHyperlinkBlock(table);
        }

        if (type == typeof(UiEditFieldModel))
        {
            AddEditFieldBlock(table);
        }

        if (typeof(UiNumericModel).IsAssignableFrom(type))
        {
            AddNumericBlock(type, table);
        }

        if (type == typeof(UiTextAreaModel))
        {
            AddTextAreaBlock(table);
        }

        if (typeof(UiItemsModel).IsAssignableFrom(type))
        {
            AddItemsBlock(type, table);
        }

        if (typeof(UiSliderModel).IsAssignableFrom(type))
        {
            AddSliderBlock(type, table);
        }

        if (type == typeof(UiImageModel))
        {
            AddUiImageBlock(table);
        }

        // U9: knobs, switches, gauges, the lamp, the pickers and the trees.
        if (type == typeof(UiKnobModel))
        {
            AddKnobBlock(table);
        }

        if (typeof(UiGaugeModel).IsAssignableFrom(type))
        {
            AddGaugeBlock(table);
            AddGaugeWords(GaugeStyleOf(type), table);
        }

        if (type == typeof(UiSwitchModel))
        {
            AddSwitchOrientation(table);
        }

        if (type == typeof(UiLampModel))
        {
            AddLampBlock(table);
        }

        if (type == typeof(UiDatePickerModel))
        {
            AddDatePickerBlock(table);
        }

        if (type == typeof(UiColorPickerModel))
        {
            AddColorPickerBlock(table);
        }

        if (typeof(UiTreeModel).IsAssignableFrom(type))
        {
            AddTreeBlock(table);
            AddTreeKindBlock(type == typeof(UiCheckBoxTreeModel), table);
        }

        // U9b: a web page.
        if (type == typeof(UiHtmlModel))
        {
            AddHtmlBlock(table);
        }

        // R2025b's set(h) lists no words for these on the U9 kinds, though each takes its words
        // (probe u9_defaults): set(k).Orientation is {} on a switch.
        if (IsU9Kind(type))
        {
            Options(table, "Orientation");
            Options(table, "ScaleDirection");
        }
    }

    /// <summary>The kinds U9 added that have a font: their font words are not listed by set(h) in R2025b.</summary>
    private static bool IsU9Kind(Type type) =>
        type == typeof(UiKnobModel) || type == typeof(UiDiscreteKnobModel) || type == typeof(UiSwitchModel)
        || typeof(UiGaugeModel).IsAssignableFrom(type) || type == typeof(UiDatePickerModel) || typeof(UiTreeModel).IsAssignableFrom(type);

    /// <summary>A callback slot kept by name on the handle's entry (U5's are too many for a field each).</summary>
    private static void AddNamedSlot(IDictionary<string, GraphicsProperty> table, string name)
    {
        AddCallbackSlot(table, name,
            entry => entry.NamedCallbacks.GetValueOrDefault(name),
            (entry, value) =>
            {
                if (value is null)
                {
                    entry.NamedCallbacks.Remove(name);
                }
                else
                {
                    entry.NamedCallbacks[name] = value;
                }
            });

        // A component's callback also takes a string array, kept as a column cell of text, and a
        // character matrix, kept as its text read down the columns (probe u5_matrix).
        GraphicsProperty slot = table[name];
        Put(table, name, slot.Read, (entry, value, line, col) =>
        {
            if (value.IsStringArray && value.ArrayLength > 1)
            {
                JgsValue cell = JgsValue.Cell([.. Enumerable.Range(0, value.ArrayLength).Select(i => JgsValue.Str(JgsBuiltins.TextOf(value.ElementAt(i))))]);
                cell.Reshape(value.ArrayLength, 1);
                value = cell;
            }
            else if (value.IsCharMatrix)
            {
                value = JgsValue.Str(ColumnMajorText(value));
            }

            slot.Write!(entry, value, line, col);
        });
    }

    private static void AddUiFontBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "FontName",
            entry => JgsValue.Str(Leaf(entry).FontName),
            (entry, value, line, col) =>
            {
                WarnDatetimeAgainstText(value);
                Leaf(entry).FontName =
                    value.Type == JgsType.String && value.AsString.Length > 0 && !JgsBuiltins.IsMissingText(value.AsString)
                        ? value.AsString
                        : throw UiError(entry, "invalidFontName", "'FontName' must be a non empty character vector or a string scalar.", line, col);
            });
        Put(table, "FontSize",
            entry => JgsValue.Number(Leaf(entry).FontSize),
            (entry, value, line, col) => Leaf(entry).FontSize =
                DoubleScalar(value) is { } size && size > 0 && double.IsFinite(size)
                    ? size
                    : throw UiError(entry, "invalidFontSize", "'FontSize' must be a positive double.", line, col));
        Put(table, "FontWeight",
            entry => JgsValue.Str(Leaf(entry).Bold ? "bold" : "normal"),
            (entry, value, line, col) => Leaf(entry).Bold =
                UiWord(entry, value, FontWeightShown, "invalidFontWeight", "'FontWeight' value must be 'normal' or 'bold'.", line, col) == "bold");
        Put(table, "FontAngle",
            entry => JgsValue.Str(Leaf(entry).Italic ? "italic" : "normal"),
            (entry, value, line, col) => Leaf(entry).Italic =
                UiWord(entry, value, FontAngleShown, "invalidFontAngle", "'FontAngle' value must be 'normal' or 'italic'.", line, col) == "italic");
        Put(table, "FontColor",
            entry => UiColorValue(Leaf(entry).FontColor),
            (entry, value, line, col) => Leaf(entry).FontColor = SolidColor(entry, "FontColor", value, line, col));
    }

    private static UiHorizontalAlignment HorizontalOf(JgsHandleEntry entry, JgsValue value, int line, int col) =>
        (UiHorizontalAlignment)Array.IndexOf(AlignmentWords, UiWord(entry, value, AlignmentWords, "invalidHorizontalAlignment",
            "'HorizontalAlignment' value must be 'left', 'center', or 'right'.", line, col));

    private static UiVerticalAlignment VerticalOf(JgsHandleEntry entry, JgsValue value, int line, int col) =>
        (UiVerticalAlignment)Array.IndexOf(VerticalWords, UiWord(entry, value, VerticalWords, "invalidVerticalAlignment",
            "'VerticalAlignment' value must be 'top', 'center', or 'bottom'.", line, col));

    private static void AddCaptionBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Text",
            entry => TextValue(CaptionOf(entry).Text),
            (entry, value, line, col) =>
            {
                UiCaptionModel model = CaptionOf(entry);
                model.Text = Caption(entry, value, model.Text, "invalidMultilineTextValue", "Text", line, col);
            });
        Put(table, "WordWrap",
            entry => OnOff(CaptionOf(entry).WordWrap),
            (entry, value, line, col) => CaptionOf(entry).WordWrap = OnOffState(entry, "WordWrap", value, line, col));

        bool button = type == typeof(UiButtonModel) || type == typeof(UiStateButtonModel) || type == typeof(UiToggleButtonModel);
        if (button || type == typeof(UiLabelModel) || type == typeof(UiHyperlinkModel))
        {
            Put(table, "HorizontalAlignment",
                entry => JgsValue.Str(AlignmentWords[(int)CaptionOf(entry).HorizontalAlignment]),
                (entry, value, line, col) => CaptionOf(entry).HorizontalAlignment = HorizontalOf(entry, value, line, col));
            Put(table, "VerticalAlignment",
                entry => JgsValue.Str(VerticalWords[(int)CaptionOf(entry).VerticalAlignment]),
                (entry, value, line, col) => CaptionOf(entry).VerticalAlignment = VerticalOf(entry, value, line, col));
        }

        if (button || type == typeof(UiLabelModel) || type == typeof(UiRadioButtonModel))
        {
            Put(table, "Interpreter",
                entry => JgsValue.Str(CaptionOf(entry).Interpreter),
                (entry, value, line, col) => CaptionOf(entry).Interpreter = UiWord(entry, value, InterpreterWords,
                    "invalidInterpreter", "'Interpreter' value must be 'none', 'html', 'latex', or 'tex'.", line, col));
        }

        if (button)
        {
            Put(table, "Icon",
                entry => entry.UiCData ?? JgsValue.Str(CaptionOf(entry).IconSource),
                (entry, value, line, col) =>
                {
                    UiCaptionModel model = CaptionOf(entry);
                    (UiImage? image, string source, JgsValue? kept) = PictureOf(entry, value, model.IconSource, stock: true, line, col);
                    model.Icon = image;
                    model.IconSource = source;
                    entry.UiCData = kept;
                });
            Put(table, "IconAlignment",
                entry => JgsValue.Str(CaptionOf(entry).IconAlignment),
                (entry, value, line, col) => CaptionOf(entry).IconAlignment = UiWord(entry, value, IconAlignmentWords,
                    "invalidIconRelationToText", "Unrecognized value for the 'IconAlignment' property.", line, col));
        }

        if (type == typeof(UiStateButtonModel) || type == typeof(UiCheckBoxModel))
        {
            Put(table, "Value",
                entry => JgsValue.Bool(CaptionOf(entry).Value),
                (entry, value, line, col) => CaptionOf(entry).Value = TrueOrFalse(value)
                    ?? throw UiError(entry, "invalidSelected", "'Value' must be true or false.", line, col));
        }

        if (type == typeof(UiRadioButtonModel) || type == typeof(UiToggleButtonModel))
        {
            Put(table, "Value",
                entry => JgsValue.Bool(CaptionOf(entry).Value),
                (entry, value, line, col) =>
                {
                    bool on = TrueOrFalse(value) ?? throw UiError(entry, "invalidValue", "'Selected' must be true or false.", line, col);
                    SelectGroupButton(entry, CaptionOf(entry), on, line, col);
                });
        }
    }

    /// <summary>R2025b's true or false: a logical scalar, or a numeric scalar that is exactly 1 or 0.</summary>
    private static bool? TrueOrFalse(JgsValue value)
    {
        JgsValue scalar = value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsStringArray && !value.IsCharMatrix
            ? value.ElementAt(0)
            : value;
        return scalar.Type == JgsType.Bool ? scalar.AsBool
            : scalar.Type == JgsType.Number && scalar.AsNumber is 0 or 1 ? scalar.AsNumber == 1
            : null;
    }

    /// <summary>
    /// A radio or toggle button's <c>Value</c> written by a script (probe <c>u5_forms</c>): turning one
    /// on turns the others of its group off; turning the selected one off selects the group's first
    /// button, unless it is the only one, which R2025b refuses.
    /// </summary>
    private static void SelectGroupButton(JgsHandleEntry entry, UiCaptionModel button, bool on, int line, int col)
    {
        if (button.Parent is not UiButtonGroupModel group)
        {
            button.Value = on;
            return;
        }

        List<UiCaptionModel> buttons = [.. group.Components.OfType<UiCaptionModel>().Where(static b => b is UiRadioButtonModel or UiToggleButtonModel)];
        if (on)
        {
            foreach (UiCaptionModel other in buttons)
            {
                other.Value = ReferenceEquals(other, button);
            }

            return;
        }

        if (!button.Value)
        {
            return;
        }

        UiCaptionModel? next = buttons.FirstOrDefault(other => !ReferenceEquals(other, button));
        if (next is null)
        {
            throw UiError(entry, "noButtonSelected",
                "'Value' cannot be set to false because it is the only component of class 'matlab.ui.control.RadioButton' or 'matlab.ui.control.ToggleButton' in the container.\n",
                line, col);
        }

        button.Value = false;
        next.Value = true;
    }

    private static void AddHyperlinkBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddNamedSlot(table, "HyperlinkClickedFcn");
        Put(table, "URL",
            entry => JgsValue.Str(((UiHyperlinkModel)entry.Target).Url),
            (entry, value, line, col) =>
            {
                var link = (UiHyperlinkModel)entry.Target;
                link.Url = UrlOf(entry, value, link.Url, line, col);
            });
        Put(table, "VisitedColor",
            entry => UiColorValue(((UiHyperlinkModel)entry.Target).VisitedColor),
            (entry, value, line, col) => ((UiHyperlinkModel)entry.Target).VisitedColor = SolidColor(entry, "VisitedColor", value, line, col));
    }

    /// <summary>R2025b's <c>URL</c>: text holding a dot with something either side of it, or nothing at all.</summary>
    private static string UrlOf(JgsHandleEntry entry, JgsValue value, string current, int line, int col)
    {
        string text = OneLine(entry, value, current, "invalidURL", "'URL' must be a character vector or a string scalar.", line, col);
        int dot = text.IndexOf('.');
        return text.Length == 0 || (dot > 0 && dot < text.Length - 1)
            ? text
            : throw UiError(entry, "invalidURL", "'URL' must contain both domain and top level domain such as '.com'.", line, col);
    }

    private static void AddEditFieldBlock(IDictionary<string, GraphicsProperty> table)
    {
        static UiEditFieldModel Field(JgsHandleEntry entry) => (UiEditFieldModel)entry.Target;
        const string limitsMessage =
            "'CharacterLimits' must be a non-decreasing 1-by-2 array of nonnegative integers that specifies the minimum and maximum number of entered characters. Use Inf to indicate no bound for the upper limit.";

        AddNamedSlot(table, "ValueChangedFcn");
        AddNamedSlot(table, "ValueChangingFcn");
        Put(table, "Value",
            entry => JgsValue.Str(Field(entry).Value),
            (entry, value, line, col) =>
            {
                UiEditFieldModel field = Field(entry);
                string text = OneLine(entry, value, field.Value, "invalidText", "'Value' must be a character vector or a string scalar.", line, col);
                if (text.Length < field.MinCharacters || text.Length > field.MaxCharacters)
                {
                    throw UiError(entry, "valueLengthNotInRange",
                        "The number of characters in 'Value' must be within the range of 'CharacterLimits'.", line, col);
                }

                field.Value = UiEditFieldModel.FitsInputType(text, field.InputType)
                    ? text
                    : throw UiError(entry, "invalidValueText", "'Value' must consist of valid text as specified by 'InputType'.", line, col);
            });
        Put(table, "CharacterLimits",
            entry => Row(Field(entry).MinCharacters, Field(entry).MaxCharacters),
            (entry, value, line, col) =>
            {
                UiEditFieldModel field = Field(entry);
                double[]? limits = value.Type == JgsType.Array && value.Rows == 1 ? RealNumbers(value) : null;
                bool sound = limits is { Length: 2 } && limits[0] >= 0 && limits[0] == System.Math.Floor(limits[0]) && double.IsFinite(limits[0])
                    && limits[1] >= limits[0] && (double.IsPositiveInfinity(limits[1]) || limits[1] == System.Math.Floor(limits[1]))
                    && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double";
                if (!sound)
                {
                    throw UiError(entry, "invalidCharacterLimits", limitsMessage, line, col);
                }

                field.MinCharacters = limits![0];
                field.MaxCharacters = limits[1];

                // Text the new limits do not admit is emptied, as R2025b empties it.
                if (field.Value.Length < limits[0] || field.Value.Length > limits[1])
                {
                    field.Value = string.Empty;
                }
            });
        Put(table, "InputType",
            entry => JgsValue.Str(Field(entry).InputType),
            (entry, value, line, col) =>
            {
                UiEditFieldModel field = Field(entry);
                field.InputType = UiWord(entry, value, InputTypeWords, "invalidFourStringEnum",
                    "'InputType' value must be 'text', 'letters', 'digits', or 'alphanumerics'.", line, col, starts: true);
                if (!UiEditFieldModel.FitsInputType(field.Value, field.InputType))
                {
                    field.Value = string.Empty;
                }
            });
        Put(table, "Editable",
            entry => OnOff(Field(entry).Editable),
            (entry, value, line, col) => Field(entry).Editable = OnOffState(entry, "Editable", value, line, col));
        Put(table, "Placeholder",
            entry => JgsValue.Str(Field(entry).Placeholder),
            (entry, value, line, col) => Field(entry).Placeholder = OneLine(entry, value, Field(entry).Placeholder,
                "invalidPlaceholder", "'Placeholder' must be a character vector or a string scalar.", line, col));
        Put(table, "HorizontalAlignment",
            entry => JgsValue.Str(AlignmentWords[(int)Field(entry).HorizontalAlignment]),
            (entry, value, line, col) => Field(entry).HorizontalAlignment = HorizontalOf(entry, value, line, col));
    }

    private static void AddTextAreaBlock(IDictionary<string, GraphicsProperty> table)
    {
        static UiTextAreaModel Area(JgsHandleEntry entry) => (UiTextAreaModel)entry.Target;
        AddNamedSlot(table, "ValueChangedFcn");
        AddNamedSlot(table, "ValueChangingFcn");
        Put(table, "Value",
            entry =>
            {
                IReadOnlyList<string> lines = Area(entry).Lines;
                JgsValue cell = JgsValue.Cell([.. lines.Select(static l => JgsValue.Str(l))]);
                cell.Reshape(lines.Count, 1);
                return cell;
            },
            (entry, value, line, col) =>
            {
                const string message = "'Value' must be a character vector, or a N-by-1 array of the following type: cell array of character vectors, string, or categorical.";
                string[]? lines = value.Type == JgsType.String ? [MissingAsEmpty(value.AsString)] : TextVector(value);
                if (lines is not { Length: > 0 })
                {
                    throw UiError(entry, "invalidMultilineTextValue", message, line, col);
                }

                // A newline inside a line starts another.
                Area(entry).Lines = [.. lines.SelectMany(static l => l.Split('\n'))];
            });
        Put(table, "Editable",
            entry => OnOff(Area(entry).Editable),
            (entry, value, line, col) => Area(entry).Editable = OnOffState(entry, "Editable", value, line, col));
        Put(table, "WordWrap",
            entry => OnOff(Area(entry).WordWrap),
            (entry, value, line, col) => Area(entry).WordWrap = OnOffState(entry, "WordWrap", value, line, col));
        Put(table, "Placeholder",
            entry => JgsValue.Str(Area(entry).Placeholder),
            (entry, value, line, col) => Area(entry).Placeholder = OneLine(entry, value, Area(entry).Placeholder,
                "invalidPlaceholder", "'Placeholder' must be a character vector or a string scalar.", line, col));
        Put(table, "HorizontalAlignment",
            entry => JgsValue.Str(AlignmentWords[(int)Area(entry).HorizontalAlignment]),
            (entry, value, line, col) => Area(entry).HorizontalAlignment = HorizontalOf(entry, value, line, col));
    }

    // --- numeric fields --------------------------------------------------------------------------

    private static readonly Regex FormatOperator = new(
        @"%[-+ 0#]*\d*(\.\d+)?(l|h|b|t)?[diouxXfeEgGcs]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whether text holds exactly one <c>sprintf</c> operator, which is what a display format must.</summary>
    private static bool IsDisplayFormat(string format)
    {
        string bare = format.Replace("%%", string.Empty, StringComparison.Ordinal);
        MatchCollection found = FormatOperator.Matches(bare);
        return found.Count == 1 && FormatOperator.Replace(bare, string.Empty).IndexOf('%') < 0;
    }

    /// <summary>A numeric field's value as its format shows it, with the padding a field width adds taken off.</summary>
    internal static string DisplayOf(UiNumericModel field)
    {
        if (field.Value is not { } number)
        {
            return string.Empty;
        }

        try
        {
            return JgsSprintf.FormatMatlab(field.ValueDisplayFormat, [JgsValue.Number(number)]).Trim();
        }
        catch (FormatException)
        {
            return number.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static void SetNumber(UiNumericModel field, double? number)
    {
        field.Value = number;
        field.DisplayText = DisplayOf(field);
    }

    private static void AddNumericBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        static UiNumericModel Field(JgsHandleEntry entry) => (UiNumericModel)entry.Target;
        const string limitsMessage =
            "'Limits' must be a 1-by-2 array of increasing double values, such as [0 100]. Use -Inf and/or Inf to indicate no bound for the lower and/or upper limit.";

        AddNamedSlot(table, "ValueChangedFcn");
        Put(table, "Value",
            entry => Field(entry).Value is { } number ? JgsValue.Number(number) : JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, line, col) =>
            {
                UiNumericModel field = Field(entry);
                if (value.IsStringArray)
                {
                    throw UiError(entry, "invalidValue", "'Value' must be a double scalar.", line, col);
                }

                if ((IsEmptyDouble(value) && value.Rows == 0 && value.Cols == 0)
                    || (value.Type == JgsType.String && value.AsString.Length == 0))
                {
                    if (!field.AllowEmpty)
                    {
                        throw UiError(entry, "invalidValue", "'Value' cannot be [] when 'AllowEmpty' is 'off'.", line, col);
                    }

                    SetNumber(field, null);
                    return;
                }

                // A logical false is taken for the number 0; nothing else that is not a double is taken.
                double? number = value.Type == JgsType.Bool && !value.AsBool ? 0 : DoubleScalar(value);
                if (number is not { } given)
                {
                    throw UiError(entry, "invalidValue", "'Value' must be a double scalar.", line, col);
                }

                double stored = field.Rounded(given);
                if (!field.InRange(stored))
                {
                    throw UiError(entry, "invalidValue", "'Value' must be a double scalar within the range of 'Limits'.", line, col);
                }

                SetNumber(field, stored);
            });
        Put(table, "Limits",
            entry => Row(Field(entry).Lower, Field(entry).Upper),
            (entry, value, line, col) =>
            {
                UiNumericModel field = Field(entry);
                double[]? limits = value.Type == JgsType.Array && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double" && (value.Rows == 1 || value.Cols == 1)
                    ? RealNumbers(value)
                    : null;
                if (limits is not { Length: 2 } || double.IsNaN(limits[0]) || double.IsNaN(limits[1]))
                {
                    throw UiError(entry, "invalidLimits", limitsMessage, line, col);
                }

                if (limits[1] < limits[0])
                {
                    throw UiError(entry, "notIncreasingLimits",
                        "'Limits' must be a 1-by-2 array of non-decreasing double values, such as [0 100]. Use -Inf and/or Inf to indicate no bound for the lower and/or upper limit.",
                        line, col);
                }

                field.Lower = limits[0];
                field.Upper = limits[1];
                Reconcile(field);
            });
        foreach ((string name, bool lower) in new[] { ("LowerLimitInclusive", true), ("UpperLimitInclusive", false) })
        {
            string captured = name;
            Put(table, captured,
                entry => OnOff(lower ? Field(entry).LowerInclusive : Field(entry).UpperInclusive),
                (entry, value, line, col) =>
                {
                    bool on = OnOffState(entry, captured, value, line, col);
                    UiNumericModel field = Field(entry);
                    if (lower)
                    {
                        field.LowerInclusive = on;
                    }
                    else
                    {
                        field.UpperInclusive = on;
                    }

                    Reconcile(field);
                });
        }

        Put(table, "RoundFractionalValues",
            entry => OnOff(Field(entry).RoundFractionalValues),
            (entry, value, line, col) =>
            {
                bool on = OnOffState(entry, "RoundFractionalValues", value, line, col);
                UiNumericModel field = Field(entry);
                if (on && field is UiSpinnerModel { Step: var step } && step != System.Math.Floor(step))
                {
                    throw UiError(entry, "invalidRoundFractionalValueWhenStepIsFloat",
                        "'RoundFractionalValues' cannot be set to 'on' when 'Step' is a fractional value.", line, col);
                }

                field.RoundFractionalValues = on;
                Reconcile(field);
            });
        Put(table, "ValueDisplayFormat",
            entry => JgsValue.Str(Field(entry).ValueDisplayFormat),
            (entry, value, line, col) =>
            {
                UiNumericModel field = Field(entry);
                field.ValueDisplayFormat = value.Type == JgsType.String && IsDisplayFormat(value.AsString)
                    ? value.AsString
                    : throw UiError(entry, "invalidDisplayFormat",
                        "'ValueDisplayFormat' value must be a character vector or a string scalar containing a valid formatting operator, such as '%d'. For more information on formatting operators, see the documentation for sprintf.",
                        line, col);
                field.DisplayText = DisplayOf(field);
            });
        Put(table, "AllowEmpty",
            entry => OnOff(Field(entry).AllowEmpty),
            (entry, value, line, col) =>
            {
                UiNumericModel field = Field(entry);
                field.AllowEmpty = OnOffState(entry, "AllowEmpty", value, line, col);
                if (!field.AllowEmpty && field.Value is null)
                {
                    SetNumber(field, field.Clamped(0));
                }
            });
        Put(table, "Editable",
            entry => OnOff(Field(entry).Editable),
            (entry, value, line, col) => Field(entry).Editable = OnOffState(entry, "Editable", value, line, col));
        Put(table, "Placeholder",
            entry => JgsValue.Str(Field(entry).Placeholder),
            (entry, value, line, col) => Field(entry).Placeholder = OneLine(entry, value, Field(entry).Placeholder,
                "invalidPlaceholder", "'Placeholder' must be a character vector or a string scalar.", line, col));
        Put(table, "HorizontalAlignment",
            entry => JgsValue.Str(AlignmentWords[(int)Field(entry).HorizontalAlignment]),
            (entry, value, line, col) => Field(entry).HorizontalAlignment = HorizontalOf(entry, value, line, col));

        if (type == typeof(UiSpinnerModel))
        {
            AddNamedSlot(table, "ValueChangingFcn");
            Put(table, "Step",
                entry => JgsBuiltins.NumberOfClass(((UiSpinnerModel)entry.Target).Step, ((UiSpinnerModel)entry.Target).StepClass),
                (entry, value, line, col) =>
                {
                    var spinner = (UiSpinnerModel)entry.Target;
                    double[]? given = IsRowOrScalar(value) ? RealNumbers(value, logicalToo: true) : null;
                    if (given is not { Length: 1 } || !(given[0] > 0) || !double.IsFinite(given[0]))
                    {
                        throw UiError(entry, "invalidValue", "'Step' must be a finite, positive number.", line, col);
                    }

                    if (spinner.RoundFractionalValues && given[0] != System.Math.Floor(given[0]))
                    {
                        throw UiError(entry, "invalidStepWhenRoundingIsTrue",
                            "'Step' cannot be fractional when 'RoundFractionalValues' is 'on'.", line, col);
                    }

                    string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                    spinner.StepClass = kind == "logical" ? "double" : kind;
                    spinner.Step = given[0];
                });
        }
    }

    /// <summary>Brings a numeric field's value back inside its limits and its rounding after either changed.</summary>
    private static void Reconcile(UiNumericModel field)
    {
        if (field.Value is not { } number)
        {
            return;
        }

        double kept = field.Rounded(field.Clamped(number));
        if (!field.InRange(kept))
        {
            kept = field.Clamped(kept);
        }

        SetNumber(field, kept);
    }

    // --- lists -----------------------------------------------------------------------------------

    private static UiItemsModel Items(JgsHandleEntry entry) => (UiItemsModel)entry.Target;

    /// <summary>The elements of an <c>ItemsData</c> value, one per item it stands for.</summary>
    private static List<JgsValue> DataElements(JgsValue? data)
    {
        var elements = new List<JgsValue>();
        if (data is null)
        {
            return elements;
        }

        if (data.Type == JgsType.Cell)
        {
            elements.AddRange(data.AsCell);
        }
        else if (data.Type == JgsType.String && !data.IsStringArray)
        {
            elements.AddRange(data.AsString.Select(static c => JgsValue.Str(c.ToString())));
        }
        else if (data.Type == JgsType.Array)
        {
            for (int i = 0; i < data.ArrayLength; i++)
            {
                JgsValue element = data.ElementAt(i);
                elements.Add(data.IsStringArray ? JgsValue.StringScalar(JgsBuiltins.TextOf(element)) : JgsBuiltins.ElementOfClass(data, i));
            }
        }
        else
        {
            elements.Add(data);
        }

        return elements;
    }

    /// <summary>What each item stands for: its <c>ItemsData</c> element when there is data, its text otherwise.</summary>
    private static List<JgsValue> ItemValues(JgsHandleEntry entry)
    {
        UiItemsModel model = Items(entry);
        List<JgsValue> data = DataElements(entry.ItemsData);
        if (data.Count == 0)
        {
            return [.. model.Items.Select(static item => JgsValue.Str(item))];
        }

        // Data past the last item stands for nothing; an item past the last datum has no value.
        return [.. data.Take(model.Items.Count)];
    }

    private static bool HasData(JgsHandleEntry entry) => DataElements(entry.ItemsData).Count > 0;

    private static int IndexOfValue(List<JgsValue> values, JgsValue wanted)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (JgsBuiltins.IsEqualValues(values[i], wanted))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>MATLAB's <c>Value</c> of a drop-down or a list, read off the selection.</summary>
    private static JgsValue ItemsValue(JgsHandleEntry entry)
    {
        UiItemsModel model = Items(entry);
        if (model is UiDropDownModel { Typed: { } typed })
        {
            return JgsValue.Str(typed);
        }

        List<JgsValue> values = ItemValues(entry);
        List<JgsValue> picked = [.. model.Selected.Where(i => i >= 0 && i < values.Count).Select(i => values[i])];
        bool multi = model is UiListBoxModel { Multiselect: true };
        if (!multi)
        {
            return picked.Count > 0 ? picked[0] : EmptyCell();
        }

        if (picked.Count == 0)
        {
            return EmptyCell();
        }

        // Several values of one numeric data array read back as an array; anything else as a cell.
        if (entry.ItemsData is { Type: JgsType.Array } data && !data.IsStringArray && HasData(entry))
        {
            return JgsBuiltins.RowOfClass([.. picked.Select(static v => v.AsNumber)], JgsBuiltins.ClassOf(data, JgsDialect.Matlab));
        }

        return JgsValue.Cell([.. picked]);
    }

    private static void AddItemsBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        bool isList = type == typeof(UiListBoxModel);
        bool isDrop = type == typeof(UiDropDownModel);
        bool isSwitch = type == typeof(UiSwitchModel);
        bool isDial = type == typeof(UiDiscreteKnobModel);
        AddNamedSlot(table, "ValueChangedFcn");
        if (isList || isDrop)
        {
            AddNamedSlot(table, "ClickedFcn");
        }

        if (isList)
        {
            AddNamedSlot(table, "DoubleClickedFcn");
        }

        if (isDrop)
        {
            AddNamedSlot(table, "DropDownOpeningFcn");
        }

        Put(table, "Items",
            entry => RowCell(Items(entry).Items),
            (entry, value, line, col) =>
            {
                WarnDatetimeAgainstText(value, Items(entry).Items.Count > 0);
                if (value.Type == JgsType.Cell && value.Rows > 1 && value.Cols > 1 && !isSwitch && !isDial)
                {
                    throw UiError(entry, "invalidText", "Expected input to be a vector.", line, col);
                }

                string[]? items = value.Type == JgsType.Cell && value.AsCell.Length == 0 ? [] : TextVector(value);
                if (items is null)
                {
                    throw UiError(entry, "invalidText", "'Items' must be a 1-by-N cell array of character vectors or a string array.", line, col);
                }

                // A switch has two items, a discrete knob at least two (U9, probe u9_matrix).
                if (isSwitch && items.Length != 2)
                {
                    throw UiError(entry, "invalidText", "'Items' must have 2 elements.", line, col);
                }

                if (isDial && items.Length < 2)
                {
                    throw UiError(entry, "invalidText", "'Items' must have at least 2 elements.", line, col);
                }

                ReplaceItems(entry, () => Items(entry).Items = items);
            });
        Put(table, "ItemsData",
            entry => entry.ItemsData ?? JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.Function)
                {
                    throw UiError(entry, "invalidItemsData", "Data type not supported.", line, col);
                }

                if (value.IsCharMatrix || (value.Type is JgsType.Array or JgsType.Cell && value.Rows > 1 && value.Cols > 1))
                {
                    throw UiError(entry, "invalidItemsData", "'ItemsData' must be a vector, such as [1,2] or {'Data 1', 'Data 2'}'.", line, col);
                }

                // A switch's data has two elements, or none (U9).
                if (isSwitch)
                {
                    int count = value.Type == JgsType.Cell ? value.AsCell.Length
                        : value.Type == JgsType.Array ? value.ArrayLength
                        : value.Type == JgsType.String ? value.AsString.Length
                        : 1;
                    if (count > 2)
                    {
                        throw UiError(entry, "invalidItemsData", "'ItemsData' must have at most 2 elements.", line, col);
                    }

                    if (count == 1)
                    {
                        throw UiError(entry, "invalidItemsData", "'ItemsData' must have at least 2 elements.", line, col);
                    }
                }

                // A discrete knob's data has at least two elements, or none (U9, probe u9_extra).
                if (isDial)
                {
                    int count = value.Type == JgsType.Cell ? value.AsCell.Length
                        : value.Type == JgsType.Array ? value.ArrayLength
                        : value.Type == JgsType.String ? value.AsString.Length
                        : 1;
                    if (count == 1)
                    {
                        throw UiError(entry, "invalidItemsData", "'ItemsData' must have at least 2 elements.", line, col);
                    }
                }

                // A column is kept as a row, which is how R2025b hands it back.
                JgsValue kept = JgsValue.Share(value);
                if (kept.Type is JgsType.Array or JgsType.Cell && kept.Cols == 1 && kept.Rows > 1)
                {
                    kept = JgsBuiltins.AsRow(kept);
                    if (value.TimeTag is { } tag)
                    {
                        kept = kept.MarkTime(tag); // a column of datetimes is still one
                    }
                }

                ReplaceItems(entry, () => entry.ItemsData = kept);
            });
        Put(table, "Value",
            ItemsValue,
            (entry, value, line, col) => SetItemsValue(entry, value, line, col));
        Put(table, "ValueIndex",
            entry =>
            {
                UiItemsModel model = Items(entry);
                IReadOnlyList<int> selected = model is UiDropDownModel { Typed: not null } ? [] : model.Selected;
                return selected.Count == 0
                    ? JgsMatrix.FromColumnMajor([], 0, 0)
                    : JgsBuiltins.RowOfClass([.. selected.Select(static i => (double)i + 1)], entry.ValueIndexClass);
            },
            (entry, value, line, col) => SetValueIndex(entry, value, line, col));

        if (isList)
        {
            Put(table, "Multiselect",
                entry => OnOff(((UiListBoxModel)entry.Target).Multiselect),
                (entry, value, line, col) =>
                {
                    var list = (UiListBoxModel)entry.Target;
                    list.Multiselect = OnOffState(entry, "Multiselect", value, line, col);
                    if (!list.Multiselect && list.Selected.Count > 1)
                    {
                        list.Selected = [list.Selected[0]];
                    }
                });
        }
        else if (isDrop)
        {
            Put(table, "Editable",
                entry => OnOff(((UiDropDownModel)entry.Target).Editable),
                (entry, value, line, col) =>
                {
                    var drop = (UiDropDownModel)entry.Target;
                    drop.Editable = OnOffState(entry, "Editable", value, line, col);
                    if (!drop.Editable && drop.Typed is not null)
                    {
                        drop.Typed = null;
                        drop.Selected = drop.Items.Count > 0 ? [0] : [];
                    }
                });
            Put(table, "Placeholder",
                entry => JgsValue.Str(((UiDropDownModel)entry.Target).Placeholder),
                (entry, value, line, col) => ((UiDropDownModel)entry.Target).Placeholder = OneLine(entry, value,
                    ((UiDropDownModel)entry.Target).Placeholder, "invalidPlaceholder", "'Placeholder' must be a character vector or a string scalar.", line, col));
        }

        // The styles added with addStyle (U9); a switch and a discrete knob take none.
        if (isList || isDrop)
        {
            Put(table, "StyleConfigurations", StyleConfigurationsValue);
        }
    }

    /// <summary>
    /// Changes a list's items or its data and settles the selection afterwards (probe
    /// <c>u5_forms</c>): a drop-down keeps the value it had when the new items still have it and
    /// takes the first otherwise; a list keeps the positions that still exist.
    /// </summary>
    private static void ReplaceItems(JgsHandleEntry entry, Action change)
    {
        UiItemsModel model = Items(entry);
        List<JgsValue> before = ItemValues(entry);
        List<JgsValue> had = [.. model.Selected.Where(i => i >= 0 && i < before.Count).Select(i => before[i])];
        bool dataBefore = HasData(entry);
        change();
        List<JgsValue> after = ItemValues(entry);
        if (model is not UiListBoxModel)
        {
            if (model is UiDropDownModel { Typed: not null })
            {
                return;
            }

            // Data taken away, or first given, leaves the selection where it was (probe u9_extra:
            // a drop-down at its second item keeps it when data arrives); anything else keeps the
            // value when the new items or data still have it, and takes the first otherwise. A
            // switch and a discrete knob follow the drop-down here (U9).
            bool stays = model.Selected.Count > 0 && model.Selected[0] < model.Items.Count;
            if (stays && dataBefore != HasData(entry))
            {
                return;
            }

            int kept = had.Count > 0 ? IndexOfValue(after, had[0]) : -1;
            model.Selected = kept >= 0 ? [kept] : model.Items.Count > 0 ? [0] : [];
            return;
        }

        List<int> still = [.. model.Selected.Where(i => i >= 0 && i < model.Items.Count)];
        if (still.Count == 0 && model.Selected.Count > 0 && model.Items.Count > 0)
        {
            still.Add(0);
        }

        model.Selected = still;
    }

    private static void SetValueIndex(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        UiItemsModel model = Items(entry);
        bool isList = model is UiListBoxModel;
        string message = isList
            ? "'ValueIndex' must be [] or a positive integer representing an index in 'Items'."
            : "'ValueIndex' must be a positive integer representing an index in 'Items'.";
        if (value.Type == JgsType.Complex && !isList)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeReal", "Argument must be real.");
        }

        if (value.IsStringArray)
        {
            throw UiError(entry, "invalidValueIndex", message, line, col);
        }

        bool empty = (IsEmptyDouble(value) && value.Rows == 0 && value.Cols == 0) || (value.Type == JgsType.String && value.AsString.Length == 0);
        if (empty && isList)
        {
            model.Selected = [];
            return;
        }

        double[]? given = value.Type == JgsType.Bool && value.AsBool ? [1] : IsRowOrScalar(value) ? RealNumbers(value) : null;
        bool multi = model is UiListBoxModel { Multiselect: true };
        if (given is not { Length: > 0 } || (given.Length > 1 && !multi)
            || given.Any(x => x < 1 || x != System.Math.Floor(x) || x > model.Items.Count))
        {
            throw UiError(entry, "invalidValueIndex", message, line, col);
        }

        // An item with no datum behind it cannot be selected through data: R2025b leaves the selection be.
        List<JgsValue> values = ItemValues(entry);
        if (given.Any(x => x > values.Count))
        {
            return;
        }

        if (model is UiDropDownModel drop)
        {
            drop.Typed = null;
        }

        model.Selected = [.. given.Select(static x => (int)x - 1)];
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        entry.ValueIndexClass = kind == "logical" ? "double" : kind;
    }

    private static void SetItemsValue(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        UiItemsModel model = Items(entry);
        WarnDatetimeAgainstText(value, model.Items.Count > 0 && !HasData(entry));
        List<JgsValue> values = ItemValues(entry);
        bool data = HasData(entry);
        string notIn = data
            ? "'Value' must be an element defined in the 'ItemsData' property."
            : "'Value' must be an element defined in the 'Items' property.";
        string notInId = data ? "valueNotInItemsData" : "valueNotInText";

        UiDropDownModel? drop = model as UiDropDownModel;
        if (model is not UiListBoxModel)
        {
            if (model.Items.Count == 0)
            {
                if (value.Type == JgsType.Cell && value.AsCell.Length == 0)
                {
                    return;
                }

                throw UiError(entry, "valueNotEmpty", "'Value' must be the empty cell because the 'Items' property is empty.", line, col);
            }

            JgsValue wanted = value.Type == JgsType.Cell && value.AsCell.Length == 1 ? value.AsCell[0] : value;
            int at = IndexOfValue(values, wanted);
            if (at >= 0)
            {
                if (drop is not null)
                {
                    drop.Typed = null;
                }

                model.Selected = [at];
                return;
            }

            if (drop is { Editable: true } && !data && wanted.Type == JgsType.String)
            {
                drop.Typed = wanted.AsString;
                return;
            }

            // A datum that exists, but stands past the last item, is refused in words of its own.
            throw data && IndexOfValue(DataElements(entry.ItemsData), wanted) >= 0
                ? UiError(entry, "valueNotWithinLengthOfText",
                    "'Value' must be an element defined in the 'ItemsData' property within the length of the 'Items' property.", line, col)
                : UiError(entry, notInId, notIn, line, col);
        }

        var list = (UiListBoxModel)model;
        bool emptyCell = value.Type == JgsType.Cell && value.AsCell.Length == 0;
        if (emptyCell)
        {
            list.Selected = [];
            return;
        }

        if (!list.Multiselect)
        {
            JgsValue wanted = value.Type == JgsType.Cell && value.AsCell.Length == 1 ? value.AsCell[0] : value;
            bool scalar = data ? !(value.Type == JgsType.Cell && value.AsCell.Length > 1) : wanted.Type == JgsType.String || wanted.IsCharMatrix;
            if (!scalar)
            {
                throw UiError(entry, "scalarItemInSingleSelect",
                    "'Value' must be a character vector, string scalar or an empty cell array when component is single selection.", line, col);
            }

            int at = IndexOfValue(values, wanted);
            list.Selected = at >= 0 ? [at] : throw UiError(entry, notInId, notIn, line, col);
            return;
        }

        // Several: a cell of values, a string array, or an array of data.
        // A cell of values answers to data kept in a cell, and to no other data.
        bool cellData = entry.ItemsData is { Type: JgsType.Cell };
        if (data && value.Type == JgsType.Cell && !cellData)
        {
            throw UiError(entry, "notASubset",
                "The 'Value' property must be a subset of the 'ItemsData' property. Note that elements in the 'ItemsData' property with no corresponding element in the 'Items' property are ignored. Use the empty cell to indicate no selection.",
                line, col);
        }

        List<JgsValue> asked = value.Type == JgsType.Cell ? [.. value.AsCell]
            : value.IsStringArray ? [.. Enumerable.Range(0, value.ArrayLength).Select(i => JgsValue.Str(JgsBuiltins.TextOf(value.ElementAt(i))))]
            : value.Type == JgsType.Array && data ? DataElements(value)
            : [value];
        var picked = new List<int>();
        foreach (JgsValue one in asked)
        {
            int at = IndexOfValue(values, one);
            if (at < 0 || picked.Contains(at))
            {
                throw UiError(entry, "notASubset", data
                    ? "The 'Value' property must be a subset of the 'ItemsData' property. Note that elements in the 'ItemsData' property with no corresponding element in the 'Items' property are ignored. Use the empty cell to indicate no selection."
                    : "The 'Value' property must be a subset of the 'Items' property. Note that elements in the 'Items' property with no corresponding element in the 'ItemsData' property are ignored. Use the empty cell to indicate no selection.",
                    line, col);
            }

            picked.Add(at);
        }

        list.Selected = picked;
    }

    // --- sliders ---------------------------------------------------------------------------------

    private static UiSliderModel Slider(JgsHandleEntry entry) => (UiSliderModel)entry.Target;

    /// <summary>
    /// A tick's label as R2025b writes it (probe <c>u5_forms</c>): the number in full when four
    /// significant digits say all of it — 13340, 0.0015 — and to four digits when they do not
    /// (12345 is <c>1.234e+04</c>).
    /// </summary>
    private static string TickLabel(double tick) => IsFourDigits(tick)
        ? tick.ToString("0.###############", System.Globalization.CultureInfo.InvariantCulture)
        : JgsSprintf.FormatMatlab("%.4g", [JgsValue.Number(tick)]);

    /// <summary>Whether four significant digits are all a number has.</summary>
    private static bool IsFourDigits(double value)
    {
        if (value == 0)
        {
            return true;
        }

        double scale = System.Math.Pow(10, 3 - System.Math.Floor(System.Math.Log10(System.Math.Abs(value))));
        double scaled = value * scale;
        return System.Math.Abs(scaled - System.Math.Round(scaled)) < 1e-6 * System.Math.Max(1, System.Math.Abs(scaled));
    }

    /// <summary>
    /// Works out the ticks a slider leaves to itself. R2025b's are worked out in its view, from the
    /// pixels each label takes; these follow the same habits — about five intervals of a round size,
    /// fewer where the labels are long — and agree with it on the common ranges, not on all.
    /// </summary>
    internal static void RefreshSliderTicks(UiSliderModel slider)
    {
        double low = slider.Lower;
        double high = slider.Upper;
        double span = high - low;
        if (!slider.StepManual)
        {
            slider.Step = span / 1000;
        }

        if (!slider.MajorTicksManual && span > 0 && double.IsFinite(span))
        {
            // The most equal intervals, each at least 18 pixels long, whose ticks are all short
            // numbers and whose labels do not run into their neighbours'.
            double length = System.Math.Max(1, slider.TrackLength());
            double[] best = [low, high];
            for (int intervals = 2; intervals <= 50 && length / intervals >= 18; intervals++)
            {
                double room = length / intervals;
                var ticks = new double[intervals + 1];
                bool sound = true;
                double before = 0;
                for (int i = 0; i <= intervals && sound; i++)
                {
                    ticks[i] = i == intervals ? high : RoundTick(low + (span * i / intervals), span);
                    double width = UiFit.TextWidth([TickLabel(ticks[i])], slider.FontName, slider.FontSize, slider.Bold, slider.Italic);
                    sound = (IsFourDigits(ticks[i]) || i == 0 || i == intervals)
                        && (i == 0 || ((before + width) / 2) + 2 <= room);
                    before = width;
                }

                if (sound)
                {
                    best = ticks;
                }
            }

            if (!slider.MajorTicks.SequenceEqual(best))
            {
                slider.MajorTicks = best;
            }
        }

        if (!slider.MajorTickLabelsManual)
        {
            string[] labels = [.. slider.MajorTicks.Select(TickLabel)];
            if (!slider.MajorTickLabels.SequenceEqual(labels))
            {
                slider.MajorTickLabels = labels;
            }
        }

        if (!slider.MinorTicksManual)
        {
            IReadOnlyList<double> major = slider.MajorTicks;
            var minor = new List<double>();
            for (int i = 0; i + 1 < major.Count; i++)
            {
                for (int k = 0; k < 5; k++)
                {
                    minor.Add(System.Math.Round(major[i] + ((major[i + 1] - major[i]) * k / 5), 12));
                }
            }

            if (major.Count > 1)
            {
                minor.Add(major[^1]);
            }

            if (!slider.MinorTicks.SequenceEqual(minor))
            {
                slider.MinorTicks = minor;
            }
        }
    }

    /// <summary>
    /// Brings every slider of a figure up to the length its track has now: a grid may have
    /// stretched it since its ticks were last worked out. Called as a frame is about to be taken
    /// and after a resize, on the script thread.
    /// </summary>
    internal static void SettleSliders(FigureModel figure)
    {
        static void Walk(IReadOnlyList<UiObject> components)
        {
            foreach (UiObject component in components)
            {
                if (component is UiScaleModel scale)
                {
                    RefreshTicks(scale);
                }
                else if (component is UiContainerModel container)
                {
                    // A grid's axes are pinned to their cells as they are now.
                    (container as UiGridLayoutModel)?.PinAxes();
                    Walk(container.Components);
                }
            }
        }

        using (UiGridLayoutModel.Remembering())
        {
            Walk(figure.Components);
        }
    }

    /// <summary>A tick's value with the arithmetic's last-place noise taken off.</summary>
    private static double RoundTick(double value, double span)
    {
        double digits = System.Math.Clamp(12 - System.Math.Ceiling(System.Math.Log10(System.Math.Max(System.Math.Abs(value), span))), 0, 15);
        return System.Math.Round(value, (int)digits);
    }

    private static void AddSliderBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        bool range = type == typeof(UiRangeSliderModel);
        AddNamedSlot(table, "ValueChangedFcn");
        AddNamedSlot(table, "ValueChangingFcn");
        Put(table, "Value",
            entry => entry.Target is UiRangeSliderModel two ? Row(two.Value, two.High) : JgsValue.Number(Slider(entry).Value),
            (entry, value, line, col) =>
            {
                UiSliderModel slider = Slider(entry);
                if (slider is UiRangeSliderModel two)
                {
                    double[]? pair = value.Type == JgsType.Array && value.Rows == 1 && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double"
                        ? RealNumbers(value)
                        : null;
                    if (pair is not { Length: 2 } || !(pair[0] <= pair[1]) || pair[0] < slider.Lower || pair[1] > slider.Upper)
                    {
                        throw UiError(entry, "invalidValue",
                            "'Value' must be a 1-by-2 non-decreasing array of doubles within the range of 'Limits'.", line, col);
                    }

                    two.Value = pair[0];
                    two.High = pair[1];
                    return;
                }

                double? number = value.Type == JgsType.Bool && !value.AsBool ? 0 : DoubleScalar(value);
                slider.Value = number is { } given && given >= slider.Lower && given <= slider.Upper
                    ? given
                    : throw UiError(entry, "invalidValue", "'Value' must be a double scalar within the range of 'Limits'.", line, col);
            });
        AddScaleLimits(table, clamp: true);
        Put(table, "Step",
            entry => JgsValue.Number(Slider(entry).Step),
            (entry, value, line, col) =>
            {
                UiSliderModel slider = Slider(entry);
                if (value.Type is JgsType.Cell or JgsType.Struct or JgsType.Function)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                        $"Error setting property 'Step' of class '{FullClassOf(slider)}'. Value must be of type double or be convertible to double.");
                }

                double[]? given = value.Type == JgsType.String && value.AsString.Length == 1 ? [value.AsString[0]]
                    : IsRowOrScalar(value) ? RealNumbers(value, logicalToo: true) : null;
                slider.Step = given is { Length: 1 } && given[0] > 0 && double.IsFinite(given[0])
                    ? given[0]
                    : throw UiError(entry, "invalidStep", "'Step' must be a finite, positive number.", line, col);
                slider.StepManual = true;
            });
        Put(table, "StepMode",
            entry => JgsValue.Str(Slider(entry).StepManual ? "manual" : "auto"),
            (entry, value, line, col) =>
            {
                Slider(entry).StepManual = EnumWord(entry, "StepMode", value, AutoManualWords, line, col) == "manual";
                RefreshSliderTicks(Slider(entry));
            });

        AddScaleTicksBlock(table);
        Put(table, "Orientation",
            entry => JgsValue.Str(Slider(entry).Vertical ? "vertical" : "horizontal"),
            (entry, value, line, col) =>
            {
                UiSliderModel slider = Slider(entry);
                bool vertical = UiWord(entry, value, OrientationWords, "invalidOrientation",
                    "'Orientation' value must be 'horizontal' or 'vertical'.", line, col) == "vertical";
                if (vertical == slider.Vertical)
                {
                    return;
                }

                // The track turns about its corner: its length becomes its height.
                Rect2D box = slider.Position;
                slider.Vertical = vertical;
                slider.Position = new Rect2D(box.X, box.Y, box.Height, box.Width);
            });
        _ = range;
    }

    // --- pictures --------------------------------------------------------------------------------

    private static void AddUiImageBlock(IDictionary<string, GraphicsProperty> table)
    {
        static UiImageModel Picture(JgsHandleEntry entry) => (UiImageModel)entry.Target;
        AddNamedSlot(table, "ImageClickedFcn");
        Put(table, "ImageSource",
            entry => entry.UiCData ?? JgsValue.Str(Picture(entry).Source),
            (entry, value, line, col) =>
            {
                UiImageModel model = Picture(entry);
                (UiImage? image, string source, JgsValue? kept) = PictureOf(entry, value, model.Source, stock: false, line, col);
                model.Image = image;
                model.Source = source;
                entry.UiCData = kept;
            });
        Put(table, "ScaleMethod",
            entry => JgsValue.Str(Picture(entry).ScaleMethod),
            (entry, value, line, col) => Picture(entry).ScaleMethod = UiWord(entry, value, ScaleMethodWords, "invalidScaleMethod",
                "'ScaleMethod' value must be 'fit', 'fill', 'none', 'scaledown', 'scaleup', or 'stretch'.", line, col));
        Put(table, "HorizontalAlignment",
            entry => JgsValue.Str(AlignmentWords[(int)Picture(entry).HorizontalAlignment]),
            (entry, value, line, col) => Picture(entry).HorizontalAlignment = HorizontalOf(entry, value, line, col));
        Put(table, "VerticalAlignment",
            entry => JgsValue.Str(VerticalWords[(int)Picture(entry).VerticalAlignment]),
            (entry, value, line, col) => Picture(entry).VerticalAlignment = VerticalOf(entry, value, line, col));
        Put(table, "URL",
            entry => JgsValue.Str(Picture(entry).Url),
            (entry, value, line, col) => Picture(entry).Url = UrlOf(entry, value, Picture(entry).Url, line, col));
        Put(table, "AltText",
            entry => JgsValue.Str(Picture(entry).AltText),
            (entry, value, line, col) => Picture(entry).AltText = OneLine(entry, value, Picture(entry).AltText,
                "invalidAltText", "'AltText' must be a character vector or a string scalar.", line, col));
    }

    /// <summary>
    /// A picture as <c>Icon</c> and <c>ImageSource</c> take one (probe <c>u5_forms</c>): a file of
    /// one of five kinds, an m-by-n-by-3 array of double, single, uint8 or uint16, nothing — and,
    /// for a button, one of the five stock words. Answers the picture, the text to read back, and
    /// the array to read back when it was an array.
    /// </summary>
    private static (UiImage? Image, string Source, JgsValue? Kept) PictureOf(
        JgsHandleEntry entry, JgsValue value, string current, bool stock, int line, int col)
    {
        const string cdata = "You have specified an invalid CData. Specify an RGB image as an m-by-n-by-3 array of type double, single, uint8, or uint16.";
        JgsRuntimeException NotAFile() => stock
            ? UiError(entry, "invalidIconFile",
                "You have specified a file that cannot be found or is not an image. Specify a file name that is on the MATLAB path, or use a full or relative path.", line, col)
            : UiError(entry, "InvalidImageSourceSpecified", "ImageSource value must be a valid file path, or an m-by-n-by-3 color data matrix.", line, col);

        if (value.IsCharMatrix)
        {
            throw UiError(entry, "MustBeChar", "Input must be a row vector of characters, or a string scalar, or a cellstr, or a string matrix.", line, col);
        }

        if (value.Type == JgsType.String)
        {
            string text = MissingAsEmpty(value.AsString);
            if (text.Length == 0)
            {
                return (null, string.Empty, null);
            }

            if (stock && StockIcons.Contains(text, StringComparer.Ordinal))
            {
                return (null, text, null);
            }

            string extension = Path.GetExtension(text).TrimStart('.').ToLowerInvariant();
            if (extension.Length == 0)
            {
                throw NotAFile();
            }

            if (extension is not ("png" or "jpg" or "jpeg" or "gif" or "svg"))
            {
                throw UiError(entry, "invalidIconFormat",
                    "You have specified an invalid file format. Valid file formats are one of the following: png, jpg, jpeg, gif, svg.", line, col);
            }

            string path = JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host ? host.Resolve(text) : text;
            return File.Exists(path) && JgsBuiltins.TryReadPicture(path) is { } read
                ? (read, text, null)
                : throw NotAFile();
        }

        if (IsEmptyDouble(value) && current.Length == 0)
        {
            return (null, string.Empty, null);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind == "logical" || !IsNumericKind(kind))
        {
            throw NotAFile();
        }

        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        if (kind is not ("double" or "single" or "uint8" or "uint16") || dims.Length != 3 || dims[2] != 3 || value.Type == JgsType.Complex)
        {
            throw UiError(entry, "invalidIconCData", cdata, line, col);
        }

        double[] data = JgsBuiltins.ToDoubles("image", value, line, col);
        double high = kind switch
        {
            "uint8" => 255,
            "uint16" => 65535,
            _ => 1,
        };
        int rows = dims[0];
        int cols = dims[1];
        var pixels = new byte[rows * cols * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                byte Channel(int plane) => (byte)System.Math.Round(255 * System.Math.Clamp(data[r + (c * rows) + (plane * rows * cols)] / high, 0, 1));
                pixels[at] = Channel(2);
                pixels[at + 1] = Channel(1);
                pixels[at + 2] = Channel(0);
                pixels[at + 3] = 255;
            }
        }

        return (rows > 0 && cols > 0 ? new UiImage(cols, rows, pixels) : null, string.Empty, JgsValue.Share(value));
    }

    // --- uigridlayout ----------------------------------------------------------------------------

    private static UiGridLayoutModel Grid(JgsHandleEntry entry) => (UiGridLayoutModel)entry.Target;

    private static readonly Regex WeightText = new(@"^\s*([0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*[xX]\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static JgsValue TracksValue(IReadOnlyList<UiGridTrack> tracks)
    {
        JgsValue cell = JgsValue.Cell([.. tracks.Select(static track => track.Kind switch
        {
            UiGridTrackKind.Fit => JgsValue.Str("fit"),
            UiGridTrackKind.Weight => JgsValue.Str(track.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "x"),
            _ => JgsBuiltins.NumberOfClass(track.Value, track.NumberClass),
        })]);
        if (tracks.Count == 0)
        {
            cell.Reshape(0, 0);
        }

        return cell;
    }

    /// <summary>One track from one element: <c>'fit'</c>, <c>'Nx'</c>, or a number of pixels of any numeric class.</summary>
    private static UiGridTrack? TrackOf(JgsValue element)
    {
        if (JgsBuiltins.IsTextScalar(element))
        {
            string text = JgsBuiltins.TextOf(element);
            if (text.Trim().Equals("fit", StringComparison.OrdinalIgnoreCase))
            {
                return UiGridTrack.Fit;
            }

            Match weight = WeightText.Match(text);
            return weight.Success
                ? new UiGridTrack(UiGridTrackKind.Weight, double.Parse(weight.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                : null;
        }

        string kind = JgsBuiltins.ClassOf(element, JgsDialect.Matlab);
        if (element.Type != JgsType.Number || kind == "logical" || !IsNumericKind(kind))
        {
            return null;
        }

        double pixels = element.AsNumber;
        return pixels >= 0 && double.IsFinite(pixels) ? new UiGridTrack(UiGridTrackKind.Fixed, pixels, kind) : null;
    }

    private static IReadOnlyList<UiGridTrack> TracksOf(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        JgsRuntimeException Refused() => UiError(entry, $"Invalid{property}",
            $"'{property}' must be a cell vector containing the keyword 'fit', numbers, or numbers paired with 'x' characters.  \n You can specify any combination of values, e.g. {{100, '2x', 'fit', '1x'}}.\n If all the elements are of the same type, vector arrays are accepted, e.g. [\"fit\", \"1x\"] or [100, 200].",
            line, col);

        var elements = new List<JgsValue>();
        if (value.Type == JgsType.Cell)
        {
            if (value.Rows > 1 && value.Cols > 1)
            {
                throw Refused();
            }

            elements.AddRange(value.AsCell);
        }
        else if (value.IsStringArray)
        {
            elements.AddRange(Enumerable.Range(0, value.ArrayLength).Select(i => JgsValue.Str(JgsBuiltins.TextOf(value.ElementAt(i)))));
        }
        else if (value.Type == JgsType.Number || (value.Type == JgsType.Array && value.ArrayLength > 0 && !value.IsCharMatrix))
        {
            int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
            for (int i = 0; i < count; i++)
            {
                elements.Add(value.Type == JgsType.Array ? JgsBuiltins.ElementOfClass(value, i) : value);
            }
        }
        else
        {
            throw Refused();
        }

        var tracks = new List<UiGridTrack>(elements.Count);
        foreach (JgsValue element in elements)
        {
            tracks.Add(TrackOf(element) ?? throw Refused());
        }

        return tracks;
    }

    private static void AddUiGridBlock(IDictionary<string, GraphicsProperty> table)
    {
        table.Remove("Title");
        Put(table, "RowHeight",
            entry => TracksValue(Grid(entry).Rows),
            (entry, value, line, col) => Grid(entry).Rows = TracksOf(entry, "RowHeight", value, line, col));
        Put(table, "ColumnWidth",
            entry => TracksValue(Grid(entry).Columns),
            (entry, value, line, col) => Grid(entry).Columns = TracksOf(entry, "ColumnWidth", value, line, col));
        Put(table, "Padding",
            entry => JgsBuiltins.RowOfClass([.. Grid(entry).Padding], Grid(entry).PaddingClass),
            (entry, value, line, col) =>
            {
                double[]? given = IsRowOrScalar(value) ? RealNumbers(value) : null;
                if (given is not { Length: 1 or 4 } || given.Any(static x => !(x >= 0) || !double.IsFinite(x)))
                {
                    throw UiError(entry, "InvalidPadding",
                        "'Padding' must be a nonnegative number or a 1x4 vector of nonnegative numbers representing the padding on the left, bottom, right and top.",
                        line, col);
                }

                // R2025b builds the four from a scalar by concatenating it with doubles, which an
                // integer scalar refuses.
                string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                if (given.Length == 1 && kind is not ("double" or "single"))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:mixedClasses",
                        "Integers can only be combined with integers of the same class, or scalar doubles.");
                }

                Grid(entry).PaddingClass = given.Length == 4 || kind == "single" ? kind : "double";
                Grid(entry).Padding = given.Length == 1 ? [given[0], given[0], given[0], given[0]] : given;
            });
        foreach (bool row in new[] { true, false })
        {
            string name = row ? "RowSpacing" : "ColumnSpacing";
            Put(table, name,
                entry => JgsBuiltins.NumberOfClass(
                    row ? Grid(entry).RowSpacing : Grid(entry).ColumnSpacing,
                    row ? Grid(entry).RowSpacingClass : Grid(entry).ColumnSpacingClass),
                (entry, value, line, col) =>
                {
                    double[]? given = IsRowOrScalar(value) ? RealNumbers(value) : null;
                    if (given is not { Length: 1 } || !(given[0] >= 0) || !double.IsFinite(given[0]))
                    {
                        throw UiError(entry, $"Invalid{name}", $"'{name}' must be a nonnegative number.", line, col);
                    }

                    string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                    if (row)
                    {
                        Grid(entry).RowSpacing = given[0];
                        Grid(entry).RowSpacingClass = kind;
                    }
                    else
                    {
                        Grid(entry).ColumnSpacing = given[0];
                        Grid(entry).ColumnSpacingClass = kind;
                    }
                });
        }

        Put(table, "Scrollable",
            entry => OnOff(Grid(entry).Scrollable),
            (entry, value, line, col) => Grid(entry).Scrollable = OnOffState(entry, "Scrollable", value, line, col));
        Put(table, "BackgroundColor",
            entry => UiColorValue(Grid(entry).BackgroundColor),
            (entry, value, line, col) => Grid(entry).BackgroundColor =
                value.Type == JgsType.String && value.AsString.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? throw new JgsRuntimeException(line, col, "MATLAB:hg:ColorSpec_None", "Cannot set GridLayout BackgroundColor to 'none'.")
                    : ClearableColor(entry, "BackgroundColor", value, line, col)!.Value);
    }

    // --- uiprogressdlg ---------------------------------------------------------------------------

    private const string ProgressClass = "matlab.ui.dialog.ProgressDialog";

    private static UiOverlayModel Overlay(JgsHandleEntry entry) =>
        entry.Target is UiOverlayModel { BeingDeleted: false, Parent: not null } overlay || entry.Target is UiOverlayModel { BeingDeleted: false } && _creatingOverlay
            ? (UiOverlayModel)entry.Target
            : throw new JgsRuntimeException(0, 0, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");

    [ThreadStatic]
    private static bool _creatingOverlay;

    private static JgsRuntimeException ProgressError(string property, string id, string reason, int line, int col) =>
        new(line, col, id, $"Error setting property '{property}' of class '{ProgressClass}'. {reason}");

    private static bool ProgressOnOff(string property, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Array && value.ArrayLength != 1 && !value.IsStringArray)
        {
            throw ProgressError(property, "MATLAB:validation:IncompatibleSize", "Value must be a scalar.", line, col);
        }

        if (value.Type is JgsType.Bool or JgsType.Number)
        {
            return value.IsTruthy;
        }

        string word = JgsBuiltins.IsTextScalar(value) ? JgsBuiltins.TextOf(value) : string.Empty;
        return word.Equals("on", StringComparison.OrdinalIgnoreCase) ? true
            : word.Equals("off", StringComparison.OrdinalIgnoreCase) ? false
            : throw ProgressError(property, "MATLAB:validation:UnableToConvert", $"'{word}' is invalid. Value must be 'off' or 'on'.", line, col);
    }

    /// <summary>R2025b's <c>ProgressDialog</c>: ten properties, checked by its own validators (probe <c>u5_dialogs</c>).</summary>
    private static void AddOverlayBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Value",
            entry => JgsBuiltins.NumberOfClass(Overlay(entry).Value, entry.OverlayValueClass),
            (entry, value, line, col) =>
            {
                UiOverlayModel overlay = Overlay(entry);
                string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                if (kind == "logical" || !IsNumericKind(kind) || value.IsCharMatrix)
                {
                    throw ProgressError("Value", "MATLAB:validators:mustBeNumeric", "Value must be numeric.", line, col);
                }

                if (value.Type == JgsType.Array && value.ArrayLength != 1)
                {
                    throw ProgressError("Value", "MATLAB:validation:IncompatibleSize", "Value must be a scalar.", line, col);
                }

                double number = JgsBuiltins.ToDoubles("Value", value, line, col)[0];
                if (!(number >= 0))
                {
                    throw ProgressError("Value", "MATLAB:validators:mustBeGreaterThanOrEqual", "Value must be greater than or equal to 0.", line, col);
                }

                if (number > 1)
                {
                    throw ProgressError("Value", "MATLAB:validators:mustBeLessThanOrEqual", "Value must be less than or equal to 1.", line, col);
                }

                overlay.Value = number;
                entry.OverlayValueClass = kind;
            });
        Put(table, "Message",
            entry => JgsValue.Str(string.Join("\n", Overlay(entry).Message)),
            (entry, value, line, col) =>
            {
                UiOverlayModel overlay = Overlay(entry);
                string[]? lines = value.Type == JgsType.String ? [value.AsString] : TextVector(value);
                overlay.Message = lines ?? throw new JgsRuntimeException(line, col, "MATLAB:uitools:uidialogs:InvalidMessageText",
                    "'Message' must be a char array, string array or a cell array of character vectors.");
            });
        foreach (string name in new[] { "Title", "CancelText" })
        {
            string captured = name;
            Put(table, captured,
                entry => JgsValue.Str(captured == "Title" ? Overlay(entry).Title : Overlay(entry).CancelText),
                (entry, value, line, col) =>
                {
                    UiOverlayModel overlay = Overlay(entry);
                    string text = value.Type == JgsType.String
                        ? value.AsString
                        : throw new JgsRuntimeException(line, col, "MATLAB:uitools:uidialogs:InvalidTitleText",
                            "'Title' must be a char array or a string scalar.");
                    if (captured == "Title")
                    {
                        overlay.Title = text;
                    }
                    else
                    {
                        overlay.CancelText = text;
                    }
                });
        }

        Put(table, "Indeterminate",
            entry => OnOff(Overlay(entry).Indeterminate),
            (entry, value, line, col) => Overlay(entry).Indeterminate = ProgressOnOff("Indeterminate", value, line, col));
        Put(table, "ShowPercentage",
            entry => OnOff(Overlay(entry).ShowPercentage),
            (entry, value, line, col) => Overlay(entry).ShowPercentage = ProgressOnOff("ShowPercentage", value, line, col));
        Put(table, "Cancelable",
            entry => OnOff(Overlay(entry).Cancelable),
            (entry, value, line, col) => Overlay(entry).Cancelable = ProgressOnOff("Cancelable", value, line, col));
        Put(table, "Icon",
            entry => JgsValue.Str(Overlay(entry).Icon),
            (entry, value, line, col) =>
            {
                UiOverlayModel overlay = Overlay(entry);
                (overlay.Icon, overlay.Image) = JgsBuiltins.DialogIcon(value, "MATLAB:ui:ProgressDialog:", line, col);
            });
        Put(table, "Interpreter",
            entry => JgsValue.Str(Overlay(entry).Interpreter),
            (entry, value, line, col) => Overlay(entry).Interpreter =
                (value.Type == JgsType.String ? Array.Find(InterpreterWords, word => word.Equals(value.AsString, StringComparison.OrdinalIgnoreCase)) : null)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:uitools:uidialogs:InvalidInterpreter",
                    "'Interpreter' value must be 'none', 'html', 'latex', or 'tex'."));
        Put(table, "CancelRequested",
            entry => JgsValue.Bool(Overlay(entry).CancelRequested),
            (entry, value, line, col) => Overlay(entry).CancelRequested = value.IsTruthy);
    }

    /// <summary>Marks the options of a creating <c>uiprogressdlg</c>, whose dialog is not yet over its figure.</summary>
    internal static IDisposable CreatingOverlay()
    {
        _creatingOverlay = true;
        return new OverlayScope();
    }

    private sealed class OverlayScope : IDisposable
    {
        public void Dispose() => _creatingOverlay = false;
    }
}
