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
            (entry, value, line, col) =>
            {
                UiControlModel control = Control(entry);
                control.Style = (UiControlStyle)Word(entry, "Style", value, StyleWords, StyleWords, line, col);

                // A list reads a row of text as its items, whenever it became a list (U3).
                control.Text = ListItems(control, control.Text);
            });

        Put(table, "String",
            entry => TextValue(Control(entry).Text),
            (entry, value, line, col) =>
            {
                UiControlModel control = Control(entry);
                control.Text = ListItems(control, ComponentText(entry, "String", value, line, col));
            });

        Put(table, "Value",
            entry => NumbersValue(Control(entry).Value),
            (entry, value, line, col) =>
            {
                UiControlModel control = Control(entry);
                control.Value = ControlValue(entry, value, line, col);

                // A button group watching this button hears of it; a selection its style no longer
                // allows is refused once the value is in, as R2025b refuses it.
                if (control.Parent is UiButtonGroupModel group && !group.ValueWritten(control))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidSelectedObjectType",
                        "Must set the SelectedObject to a Radio Button or a Toggle Button.");
                }
            });

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
                double[] step = SliderStepOf(entry, value, line, col);
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
            (entry, value, line, col) => Control(entry).FontName = FontNameOf(entry, value, line, col));
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
                double height = control.PixelPosition().Height;
                double pixels = control.FontSizeInPixels(height);
                control.FontUnits = units;
                double one = control.FontSizeInPixels(height) / control.FontSize;
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

        // CData is a truecolor picture for the control's face: kept as given, so it reads back in
        // the class it was set in, and handed to the window as pixels (U3).
        Put(table, "CData",
            entry => entry.UiCData ?? ImageValue(Control(entry).Image),
            (entry, value, line, col) => SetControlImage(entry, value, line, col));

        // The size of the text, in the control's own units: R2025b's margins and whole points round
        // this build's own measurement of the font the window draws (U3).
        Put(table, "Extent", entry =>
        {
            UiControlModel control = Control(entry);
            Size2D pixels = control.ExtentPixels();
            Rect2D sized = UiUnitConverter.FromPixels(
                new Rect2D(1, 1, pixels.Width, pixels.Height), control.Units, control.ReferenceSize());
            return Row(0, 0, sized.Width, sized.Height);
        });

        // The older spellings of Tooltip, which take one line of text only; and the three words of
        // a drawn object that a uicontrol answers though it lists none of them (U3).
        foreach (string name in new[] { "TooltipString", "TooltipStr" })
        {
            string captured = name;
            Put(table, captured,
                entry => TextValue(Control(entry).Tooltip),
                (entry, value, line, col) => Control(entry).Tooltip = JgsBuiltins.IsTextScalar(value)
                    ? UiText.Of(MissingAsEmpty(JgsBuiltins.TextOf(value)))
                    : throw ComponentError(entry, captured, "MATLAB:class:RequireString",
                        "Value must be a character vector or a string scalar.", line, col));
        }

        Put(table, "Selected",
            entry => OnOff(entry.Target.IsSelected),
            (entry, value, line, col) => entry.Target.IsSelected = ComponentOnOff(entry, "Selected", value, line, col));
        Put(table, "SelectionHighlight",
            entry => OnOff(entry.Target.SelectionHighlight),
            (entry, value, line, col) =>
                entry.Target.SelectionHighlight = ComponentOnOff(entry, "SelectionHighlight", value, line, col));
        Put(table, "HitTest",
            entry => OnOff(entry.Target.Selectable),
            (entry, value, line, col) => entry.Target.Selectable = ComponentOnOff(entry, "HitTest", value, line, col));
        Unlist(table, "TooltipString", "TooltipStr", "UIContextMenu", "Selected", "SelectionHighlight", "HitTest");
    }

    /// <summary>
    /// What a list reads a row of text as (R2025b, probe <c>u3_wrap</c>): a <c>listbox</c> or a
    /// <c>popupmenu</c> takes <c>'a|b|c'</c> as three items, held as a character matrix. Text that is
    /// already several lines, or a cell, is left as it is.
    /// </summary>
    private static UiText ListItems(UiControlModel control, UiText text)
    {
        if (control.Style is not (UiControlStyle.ListBox or UiControlStyle.PopupMenu)
            || text.Form != UiTextForm.CharRow || text.Lines.Count != 1 || !text.Lines[0].Contains('|'))
        {
            return text;
        }

        string[] items = text.Lines[0].Split('|');
        int width = items.Max(static item => item.Length);
        return new UiText(UiTextForm.CharMatrix, [.. items.Select(item => item.PadRight(width))]);
    }

    /// <summary>
    /// R2025b's <c>SliderStep</c>: two numbers, the first between 0 and 1. A second smaller than the
    /// first is kept and warned about.
    /// </summary>
    private static double[] SliderStepOf(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (!IsNumericKind(kind))
        {
            throw ComponentError(entry, "SliderStep", "MATLAB:hg:UIControlSliderStepValueNumeric", "Value must be numeric.", line, col);
        }

        double[] step = JgsBuiltins.ToDoubles("SliderStep", value, line, col);
        if (step.Length != 2)
        {
            throw ComponentError(entry, "SliderStep", "MATLAB:hg:UIControlSliderStepValueSize",
                "Value must be a 2 element vector.", line, col);
        }

        if (step[0] < 0 || step[0] > 1)
        {
            throw ComponentError(entry, "SliderStep", "MATLAB:hg:UIControlSliderStepIncrement",
                "Slider step line increment must be between 0 and 1.", line, col);
        }

        if (step[1] < step[0])
        {
            PropertyWarning("MATLAB:hg:UIControlSliderStepValueDifference", "Sliderstep(2) cannot be less than sliderstep(1).");
        }

        return step;
    }

    /// <summary>
    /// A control's picture as a script reads it when it was not set through this handle — a copy's,
    /// or one read from a document: an m-by-n-by-3 array of doubles in [0, 1], NaN where it shows
    /// the face beneath.
    /// </summary>
    private static JgsValue ImageValue(UiImage? image)
    {
        if (image is null)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        int rows = image.Height;
        int cols = image.Width;
        var data = new double[rows * cols * 3];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                bool shown = image.Bgra[at + 3] != 0;
                for (int channel = 0; channel < 3; channel++)
                {
                    data[r + (c * rows) + (channel * rows * cols)] = shown ? image.Bgra[at + 2 - channel] / 255.0 : double.NaN;
                }
            }
        }

        JgsValue value = JgsMatrix.FromColumnMajor(data, rows * cols * 3, 1);
        value.ReshapeDims([rows, cols, 3]);
        return value;
    }

    /// <summary>
    /// R2025b's <c>CData</c> (probe <c>u3_styles</c>): an m-by-n-by-3 array of a numeric class, a
    /// floating one within [0, 1] or NaN; <c>[]</c> clears it and reads back as 0-by-0-by-3.
    /// </summary>
    private static void SetControlImage(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        const string words =
            "Value must be a three-dimensional matrix of RGB values that defines a truecolor image. Each value must be between 0.0 and 1.0 or NaN.";
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (!IsNumericKind(kind))
        {
            throw ComponentError(entry, "CData", "MATLAB:hg:shaped_arrays:CDataType", words, line, col);
        }

        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        if (count == 0 && dims.Length == 2)
        {
            JgsValue none = JgsMatrix.FromColumnMajor([], 0, 0);
            none.ReshapeDims([0, 0, 3]);
            entry.UiCData = none;
            Control(entry).Image = null;
            return;
        }

        if (dims.Length != 3 || dims[2] != 3)
        {
            throw ComponentError(entry, "CData", "MATLAB:hg:shaped_arrays:CDataSize", words, line, col);
        }

        double[] data = JgsBuiltins.ToDoubles("CData", value, line, col);
        bool floating = kind is "double" or "single";
        if (floating && data.Any(static x => x < 0 || x > 1))
        {
            throw ComponentError(entry, "CData", "MATLAB:hg:shaped_arrays:CDataPredicate", words, line, col);
        }

        // A whole-number class spans its own range: uint8 255, uint16 65535, a signed one from its
        // lowest to its highest.
        (double low, double high) = kind switch
        {
            "uint8" => (0d, 255d),
            "uint16" => (0d, 65535d),
            "int8" => (-128d, 127d),
            "int16" => (-32768d, 32767d),
            "uint32" => (0d, 4294967295d),
            "int32" => (-2147483648d, 2147483647d),
            _ => (0d, 1d),
        };

        int rows = dims[0];
        int cols = dims[1];
        var pixels = new byte[rows * cols * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                double red = data[r + (c * rows)];
                double green = data[r + (c * rows) + (rows * cols)];
                double blue = data[r + (c * rows) + (2 * rows * cols)];
                if (double.IsNaN(red) || double.IsNaN(green) || double.IsNaN(blue))
                {
                    continue; // a NaN pixel shows the face beneath
                }

                byte Channel(double x) => (byte)System.Math.Round(255 * System.Math.Clamp((x - low) / (high - low), 0, 1));
                pixels[at] = Channel(blue);
                pixels[at + 1] = Channel(green);
                pixels[at + 2] = Channel(red);
                pixels[at + 3] = 255;
            }
        }

        entry.UiCData = JgsValue.Share(value);
        Control(entry).Image = rows > 0 && cols > 0 ? new UiImage(cols, rows, pixels) : null;
    }

    /// <summary>The words each of a component's properties takes, as R2025b's <c>set(h)</c> lists them.</summary>
    private static void AddComponentOptions(Type type, IDictionary<string, GraphicsProperty> table)
    {
        Options(table, "Visible", OnOffWords);
        Options(table, "HandleVisibility", HandleVisibilityWords);
        Options(table, "BusyAction", "queue", "cancel");
        Options(table, "Interruptible", OnOffWords);
        Options(table, "Units", UnitWords);
        Options(table, "FontUnits", FontUnitWords);
        Options(table, "FontAngle", FontAngleShown);
        Options(table, "FontWeight", FontWeightShown);
        Options(table, "Selected", OnOffWords);
        Options(table, "SelectionHighlight", OnOffWords);
        Options(table, "HitTest", OnOffWords);
        if (typeof(UiControlModel).IsAssignableFrom(type))
        {
            Options(table, "Style", StyleWords);
            Options(table, "Enable", EnableWords);
            Options(table, "HorizontalAlignment", AlignmentWords);
        }
        else
        {
            Options(table, "Enable", OnOffWords);
            Options(table, "BorderType", BorderTypeShown);
            Options(table, "TitlePosition", TitlePositionWords);
            Options(table, "Clipping", OnOffWords);
            Options(table, "AutoResizeChildren", OnOffWords);
            Options(table, "Scrollable", OnOffWords);
        }
    }

    /// <summary>What every component shares: its place, whether it is on, its tooltip, its units.</summary>
    private static void AddUiObjectBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        // MATLAB's interaction words on drawn objects are not a component's: R2025b's uicontrol has
        // no Selected, SelectionHighlight, HitTest or PickableParts, and answers neither of them. A
        // panel still answers the first three, though it lists none.
        table.Remove("PickableParts");
        Unlist(table, "Selected", "SelectionHighlight", "HitTest", "UIContextMenu");

        // The right-click menu, with R2025b's refusals (U3). A menu since deleted reads as none.
        foreach (string name in new[] { "ContextMenu", "UIContextMenu" })
        {
            string captured = name;
            bool listed = name == "ContextMenu";
            table[captured] = new GraphicsProperty(captured,
                entry => entry.ContextMenu is { BeingDeleted: false } menu && JgsHandleRegistry.TryGetEntry(menu, out _)
                    ? JgsHandleRegistry.For(menu)
                    : JgsMatrix.FromColumnMajor([], 0, 0),
                (entry, value, line, col) =>
                {
                    if ((value.Type == JgsType.Array && value.ArrayLength == 0)
                        || (JgsBuiltins.IsTextScalar(value) && JgsBuiltins.TextOf(value).Length == 0))
                    {
                        entry.ContextMenu = null;
                        return;
                    }

                    if (!JgsHandleRegistry.TryGet(value, out JgsHandleEntry? menu))
                    {
                        throw ComponentError(entry, captured, "MATLAB:datatypes:handleoremptydatatype:InvalidHGHandle",
                            "The value set for this property must be a valid HG handle.", line, col);
                    }

                    entry.ContextMenu = menu.Target is ContextMenuModel
                        ? menu.Target
                        : throw new JgsRuntimeException(line, col, "MATLAB:hgutils:InvalidContextMenu", "Handle must be a uicontextmenu.");
                })
            {
                Listed = listed,
            };
        }

        foreach (string name in new[] { "Position", "InnerPosition", "OuterPosition" })
        {
            string captured = name;
            Put(table, captured,
                entry =>
                {
                    // A grid's child is where the grid put it, in its own units (U5).
                    var component = (UiObject)entry.Target;
                    Rect2D box = component.Parent is UiGridLayoutModel
                        ? UiUnitConverter.FromPixels(component.PixelPosition(), component.Units, component.ReferenceSize())
                        : component.Position;
                    return Row(box.X, box.Y, box.Width, box.Height);
                },
                (entry, value, line, col) =>
                {
                    Rect2D box = ComponentPosition(entry, captured, value, line, col);
                    if (((UiObject)entry.Target).Parent is UiGridLayoutModel)
                    {
                        PropertyWarning("MATLAB:ui:components:noPositionSetWhenInLayoutContainer",
                            "Unable to set 'Position', 'InnerPosition', or 'OuterPosition' for components in 'GridLayout'.");
                        return;
                    }

                    ((UiObject)entry.Target).Position = box;
                });
        }

        // The units engine (U2): changing Units keeps the component where it is and re-expresses
        // Position, so the order of 'Units' and 'Position' among a call's options matters, as in R2025b.
        Put(table, "Units",
            entry => UnitsValue(((UiObject)entry.Target).Units),
            (entry, value, line, col) => ((UiObject)entry.Target).ChangeUnits(UnitsWord(entry, value, line, col)));

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
    private static UiText ComponentText(JgsHandleEntry entry, string property, JgsValue value, int line, int col) =>
        SplitAtNewlines(ComponentTextAsGiven(entry, property, value, line, col));

    /// <summary>
    /// A newline in a component's text starts a new line (R2025b, probe <c>u3_wrap</c>): a row
    /// becomes a character matrix, padded, and a cell's element becomes several elements.
    /// </summary>
    private static UiText SplitAtNewlines(UiText text)
    {
        if (text.Form == UiTextForm.CharMatrix || !text.Lines.Any(static l => l.Contains('\n')))
        {
            return text;
        }

        string[] lines = [.. text.Lines.SelectMany(static l => l.Split('\n'))];
        if (text.Form == UiTextForm.Cell)
        {
            return new UiText(UiTextForm.Cell, lines);
        }

        int width = lines.Max(static l => l.Length);
        return new UiText(UiTextForm.CharMatrix, [.. lines.Select(l => l.PadRight(width))]);
    }

    private static UiText ComponentTextAsGiven(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
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
            throw ComponentError(entry, property, "MATLAB:hg:datatypes:NumericOrStringDataType:ArrayClass",
                "Value must be a character vector, categorical array, string array, numeric array, or cell array of character vectors.",
                line, col);
        }

        if (IsEmptyArray(value))
        {
            return UiText.Empty;
        }

        double[] numbers = JgsBuiltins.ToDoubles(property, value, line, col);
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
        if (kind == "cell")
        {
            throw ComponentError(entry, "Value", "MATLAB:invalidConversion",
                "Conversion to double from cell is not possible.", line, col);
        }

        if (kind == "string")
        {
            throw ComponentError(entry, "Value", "MATLAB:hg:DataTypeMismatchUIControlValueScalar",
                "This is not a valid UIControlValue value. This is not an expected input data type.", line, col);
        }

        if (kind != "logical" && !IsNumericKind(kind))
        {
            throw ComponentError(entry, "Value", "MATLAB:hg:UIControlValueCharArrayString",
                "This is not a valid UIControlValue value. Value must be numeric.", line, col);
        }

        if (value.Type == JgsType.Complex
            || (value.Type == JgsType.Array && Enumerable.Range(0, value.ArrayLength).Any(i => value.ElementAt(i).Type == JgsType.Complex)))
        {
            throw ComponentError(entry, "Value", "MATLAB:hg:UIControlValueComplex",
                "This is not a valid UIControlValue value. Complex inputs are not supported.", line, col);
        }

        if (value.Type != JgsType.Array)
        {
            return new UiNumbers(JgsBuiltins.ToDoubles("Value", value, line, col), 1, 1);
        }

        // A vector reads back as a row and an empty as 0-by-0; a matrix is refused (R2025b, probe
        // u3_styles).
        if (value.ArrayLength == 0)
        {
            return new UiNumbers([], 0, 0);
        }

        if (value.Rows > 1 && value.Cols > 1)
        {
            throw ComponentError(entry, "Value", "MATLAB:hg:UIControlValueDimensions_M",
                "This is not a valid UIControlValue value. The input must have 1 rows.", line, col);
        }

        double[] numbers = JgsBuiltins.ToDoubles("Value", value, line, col);
        return new UiNumbers(numbers, 1, numbers.Length);
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

        // A string array is a column of lines, as a cell is.
        if (JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "string")
        {
            return new UiText(UiTextForm.Cell,
                [.. Enumerable.Range(0, value.ArrayLength).Select(i => MissingAsEmpty(JgsBuiltins.TextOf(value.ElementAt(i))))]);
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
