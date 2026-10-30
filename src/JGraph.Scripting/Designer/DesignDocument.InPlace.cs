using System.Globalization;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// In-place text editing on the canvas (App Designer window plan, stage W5b): which component an
/// edit opens on, what its editor opens with and where, and the text saved back as one property edit,
/// by R2025b's <c>InplaceEditPlugin</c>, <c>DesignAreaKeyboardController</c>,
/// <c>InPlaceEditorManager</c> and each controller's <c>getEditorOptions</c>.
/// </content>
public sealed partial class DesignDocument
{
    private const string LabelType = "matlab.ui.control.Label";

    // A check box's and a radio button's box beside its text (checkBoxRadioIconNode), and the gap after it.
    private const double ToggleIcon = 14;
    private const double ToggleGap = 3;

    // A spinner's arrows at its right (buttonsContainerNode).
    private const double SpinnerArrows = 20;

    // A tab group's strip of headings, where the canvas cannot say where a heading is.
    private const double TabStrip = 23;

    /// <summary>
    /// Whether <paramref name="component"/> is edited on the canvas (<c>isInPlaceEditable</c>): a
    /// label without a LaTeX or HTML interpreter, the buttons, fields, lists, an image, the sliders,
    /// knobs, gauges and switches, a table's column names, a panel's, button group's and tab's title -
    /// not a responsive app's region.
    /// </summary>
    public bool CanEditInPlace(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return DesignInPlace.IsEditable(component.Type, InterpreterOf(component)) && !IsRegion(component);
    }

    /// <summary>
    /// The component F2 and a typed character open the editor on (<c>_processSelectionChange</c>): the
    /// one selected alone - a labelled component's label - when it is auto-editable; null otherwise.
    /// </summary>
    public DesignComponent? InPlaceTarget
    {
        get
        {
            DesignComponent? target = SelectedLabeled is { Label: { } label } ? label
                : _picks is [{ Group: null } pick] ? pick.Component
                : null;
            return target is not null && CanEditInPlace(target) && DesignInPlace.IsAutoEditable(target.Type, InterpreterOf(target)) ? target : null;
        }
    }

    /// <summary>
    /// The node F2 opens its tree's editor on (W5c, the tree's own editor rather than
    /// <c>isAutoEditable</c>): the one tree node selected alone; null otherwise.
    /// </summary>
    public DesignComponent? SelectedTreeNode => _picks is [{ Component: { Type: TreeNodeType } node }] ? node : null;

    /// <summary>
    /// The component a double click at <paramref name="at"/> edits (<c>matchSelector</c> on
    /// <c>.ad-can-inplace-edit</c>): one of whose places to edit is under it - a limit or an item drawn
    /// outside the component - else the front component under it, or the nearest container round it,
    /// that is edited on the canvas; null when none is.
    /// </summary>
    public DesignComponent? InPlaceComponentAt(Point2D at, Func<DesignComponent, IReadOnlyList<Rect2D>?>? hotspotsOf = null)
    {
        IReadOnlyList<DesignBox> boxes = Boxes;
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            DesignBox box = boxes[i];
            if (box.Shown && DesignInPlace.HasHotspots(box.Component.Type) && CanEditInPlace(box.Component)
                && Hotspots(box.Component, hotspotsOf).Any(h => h.Contains(at)))
            {
                return box.Component;
            }
        }

        for (DesignComponent? up = ComponentAt(at); up is not null; up = up.Parent)
        {
            if (CanEditInPlace(up))
            {
                return up;
            }
        }

        return null;
    }

    /// <summary>
    /// Where <paramref name="component"/>'s places to edit are on the surface, as the canvas cannot
    /// say better (each controller's <c>getEditableHotspotPositions</c>): a panel's and a button
    /// group's title band, a tab's heading, a check box's and a radio button's text beside its box, a
    /// spinner's field beside its arrows, a component with several places its box cut across in so
    /// many, any other its box. Empty when the canvas does not show it.
    /// </summary>
    public IReadOnlyList<Rect2D> HotspotsOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.Type == TabType && component.Parent is { } group && BoxOf(group) is { Shown: true } strip)
        {
            int count = System.Math.Max(1, group.Children.Count(static c => c.Type == TabType));
            int index = group.Children.Where(static c => c.Type == TabType).ToList().IndexOf(component);
            double width = strip.Box.Width / count;
            return [new Rect2D(strip.Box.X + (index * width), strip.Box.Y, width, TabStrip)];
        }

        if (BoxOf(component) is not { Shown: true } shown)
        {
            return [];
        }

        Rect2D box = shown.Box;
        switch (component.Type)
        {
            case PanelType or ButtonGroupType:
                Rect2D inner = Runtime.InnerOnSurface(component);
                double band = System.Math.Max(inner.Y - box.Y, (FontSizeOf(component) ?? 12) + 6);
                return [new Rect2D(box.X, box.Y, box.Width, band)];
            case "matlab.ui.control.CheckBox" or "matlab.ui.control.RadioButton":
                return [new Rect2D(box.X + ToggleIcon + ToggleGap, box.Y, System.Math.Max(0, box.Width - ToggleIcon), box.Height)];
            case "matlab.ui.control.Spinner":
                return [new Rect2D(box.X, box.Y, System.Math.Max(0, box.Width - SpinnerArrows), box.Height)];
        }

        if (!DesignInPlace.HasHotspots(component.Type))
        {
            return [box];
        }

        int n = HotspotCount(component);
        bool upright = PlainText(ValueOf(component, "Orientation")) == "vertical";
        var parts = new Rect2D[n];
        for (int i = 0; i < n; i++)
        {
            // Upright, the first place is the bottom one, as a vertical scale's minimum is.
            parts[i] = upright
                ? new Rect2D(box.X, box.Y + ((n - 1 - i) * box.Height / n), box.Width, box.Height / n)
                : new Rect2D(box.X + (i * box.Width / n), box.Y, box.Width / n, box.Height);
        }

        return parts;
    }

    /// <summary>
    /// The edit a double click at <paramref name="pointer"/>, F2 or a typed <paramref name="seed"/>
    /// opens on <paramref name="component"/>, or Tab opens on its <paramref name="hotspot"/>
    /// (<c>InPlaceEditorManager.showEditorForWidget</c> with the controller's <c>getEditorOptions</c>):
    /// null when the component is not edited on the canvas, or a click missed each of its several
    /// places. <paramref name="hotspotsOf"/> says where the canvas drew each place; without it, or
    /// when it says nothing, <see cref="HotspotsOf"/> does.
    /// </summary>
    public DesignInPlaceEdit? InPlaceEdit(
        DesignComponent component, Point2D? pointer = null, string? seed = null, int? hotspot = null,
        Func<DesignComponent, IReadOnlyList<Rect2D>?>? hotspotsOf = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!CanEditInPlace(component))
        {
            return null;
        }

        string type = component.Type;
        IReadOnlyList<Rect2D> spots = Hotspots(component, hotspotsOf);
        if (spots.Count == 0)
        {
            return null;
        }

        string orientation = PlainText(ValueOf(component, "Orientation")) ?? string.Empty;
        string direction = PlainText(ValueOf(component, "ScaleDirection")) ?? string.Empty;
        string alignment = PlainText(ValueOf(component, "HorizontalAlignment")) ?? "left";
        bool wrap = ValueOf(component, "WordWrap") is OnOffValue { IsOn: true } or NumericValue { Data: [not 0] };

        if (!DesignInPlace.HasHotspots(type))
        {
            Rect2D box = spots[0];
            return type switch
            {
                "matlab.ui.control.Label" or "matlab.ui.control.Button" or "matlab.ui.control.StateButton" or "matlab.ui.control.ToggleButton"
                    => Edit(DesignInPlaceKind.MultiLine, "Text", TextLines(ValueOf(component, "Text")), alignment, wrap),
                "matlab.ui.control.CheckBox" or "matlab.ui.control.RadioButton"
                    => Edit(DesignInPlaceKind.MultiLine, "Text", TextLines(ValueOf(component, "Text")), "left", wrap),
                "matlab.ui.control.Hyperlink"
                    => Edit(DesignInPlaceKind.Hyperlink, "Text", TextLines(ValueOf(component, "Text")), alignment, wrap) with
                    {
                        Url = PlainText(ValueOf(component, "URL")) ?? string.Empty,
                    },
                "matlab.ui.control.EditField"
                    => Edit(DesignInPlaceKind.SingleLine, "Value", [PlainText(ValueOf(component, "Value")) ?? string.Empty], alignment, false),
                "matlab.ui.control.NumericEditField" or "matlab.ui.control.Spinner"
                    => Edit(DesignInPlaceKind.SingleLine, "Value", [NumberText(ValueOf(component, "Value"), 0)], alignment, false) with
                    {
                        Pattern = DesignInPlace.NumberPattern,
                    },
                "matlab.ui.control.TextArea"
                    => Edit(DesignInPlaceKind.TextArea, "Value", TextLines(ValueOf(component, "Value")), alignment, wrap),
                "matlab.ui.control.DropDown" or "matlab.ui.control.ListBox"
                    => Edit(DesignInPlaceKind.Items, "Items", TextLines(ValueOf(component, "Items")), "left", false),
                "matlab.ui.control.Image"
                    => Edit(DesignInPlaceKind.FileBrowser, "ImageSource", [ImageText(ValueOf(component, "ImageSource"))], "left", false),
                PanelType or ButtonGroupType
                    => Edit(DesignInPlaceKind.SingleLine, "Title", [PlainText(ValueOf(component, "Title")) ?? string.Empty], TitleAlignment(component), false),
                TabType
                    => Edit(DesignInPlaceKind.SingleLine, "Title", [PlainText(ValueOf(component, "Title")) ?? string.Empty], "center", false),
                TreeNodeType
                    => Edit(DesignInPlaceKind.SingleLine, "Text", [PlainText(ValueOf(component, "Text")) ?? string.Empty], "left", false),
                _ => null,
            };

            DesignInPlaceEdit Edit(DesignInPlaceKind kind, string property, IReadOnlyList<string> lines, string align, bool wordWrap) =>
                new(component, kind, property, lines, box, 0, 1, align, wordWrap, seed, null);
        }

        // A component with several places: a click edits the one under it, or none; a key, the one its kind starts at.
        (int first, int last) = VisibleHotspots(component, spots);
        int index;
        if (hotspot is { } wanted)
        {
            index = System.Math.Clamp(wanted, first, last);
        }
        else if (pointer is { } at)
        {
            index = -1;
            for (int i = first; i <= last; i++)
            {
                // A table's columns are told apart across only (TableController).
                bool hit = type == "matlab.ui.control.Table" ? at.X >= spots[i].Left && at.X <= spots[i].Right : spots[i].Contains(at);
                if (hit)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return null;
            }
        }
        else
        {
            index = type == "matlab.ui.control.Table" ? first : System.Math.Min(DesignInPlace.DefaultHotspot(type, orientation, direction), spots.Count - 1);
        }

        string along = DesignInPlace.HotspotAlignment(type, index, spots.Count, orientation, direction);
        if (DesignInPlace.EditsLimits(type))
        {
            double[] limits = Runtime.ReadNumbers(component, "Limits") ?? [0, 100];
            return new DesignInPlaceEdit(component, DesignInPlaceKind.SingleLine, index == 0 ? "Minimum" : "Maximum",
                [NumberText(limits.Length > index ? limits[index] : 0)], spots[index], index, spots.Count, along, false, seed, null)
            {
                Pattern = DesignInPlace.LimitPattern,
            };
        }

        string property = type == "matlab.ui.control.Table" ? "ColumnName" : "Items";
        IReadOnlyList<string> values = TextLines(ValueOf(component, property));
        return new DesignInPlaceEdit(component, DesignInPlaceKind.SingleLine, property,
            [index < values.Count ? values[index] : string.Empty], spots[index], index, spots.Count, along, false, seed, null);
    }

    /// <summary>
    /// The place Tab (or Shift+Tab) opens after <paramref name="edit"/>'s on a component with several,
    /// round the ones it shows (<c>_getEditableHotspotIndex</c> with <c>StartEditingNextHotspotOnClose</c>);
    /// null for a component with one.
    /// </summary>
    public int? NextHotspot(DesignInPlaceEdit edit, bool backwards, Func<DesignComponent, IReadOnlyList<Rect2D>?>? hotspotsOf = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!DesignInPlace.HasHotspots(edit.Component.Type))
        {
            return null;
        }

        (int first, int last) = VisibleHotspots(edit.Component, Hotspots(edit.Component, hotspotsOf));
        return DesignInPlace.NextHotspot(edit.Hotspot, backwards, first, last);
    }

    /// <summary>
    /// Saves what an in-place editor holds - <paramref name="text"/>, its lines split at line breaks,
    /// and a hyperlink's <paramref name="url"/> - as App Designer's editors and
    /// <c>_saveWidgetTextChanges</c> save it: nothing when the text is as it opened, or a single line
    /// fails its pattern; a labelled component's label emptied is deleted; the text as the property
    /// takes it - one line as characters and more as a column of them, a number for a numeric field,
    /// a spinner and a limit, which keeps the other limit, the item or column name edited in place
    /// among the others, a list's items each line one - through the property's own set, one edit; a
    /// hyperlink's URL, when it changed and is one it takes (<see cref="DesignInPlace.IsValidUrl"/>),
    /// as an edit after it.
    /// </summary>
    /// <returns>Null when saved or nothing was to save; else what the component said, and nothing changed.</returns>
    public string? SaveInPlace(DesignInPlaceEdit edit, string text, string? url = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(text);
        DesignComponent component = edit.Component;
        if (Design.Find(component.CodeName) is not { } live || !ReferenceEquals(live, component))
        {
            return null; // gone since the editor opened
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string joined = string.Join('\n', lines);
        bool changed = joined != string.Join('\n', edit.Lines);
        string? refused = null;

        if (edit.Kind == DesignInPlaceKind.SingleLine && !DesignInPlace.Matches(edit.Pattern, joined))
        {
            return null; // the editor closes unsaved (Abort)
        }

        if (changed)
        {
            refused = edit.Property switch
            {
                "Text" when lines is [""] && Design.LabeledGroupOf(component) is { Label: { } label } && ReferenceEquals(label, component)
                    => DeleteLabel(component),
                "Text" => SetProperty(component, "Text", LinesValue(lines)),
                "Title" => SetProperty(component, "Title", new CharValue(joined)),
                "Value" when component.Type == "matlab.ui.control.TextArea"
                    => SetProperty(component, "Value", CellValue.Column(lines.Select(static l => (DesignValue)new CharValue(l)).ToArray())),
                "Value" when component.Type is "matlab.ui.control.NumericEditField" or "matlab.ui.control.Spinner"
                    => DesignInPlace.Number(joined) is { } number ? SetProperty(component, "Value", NumericValue.Scalar(number)) : null,
                "Value" => SetProperty(component, "Value", new CharValue(joined)),
                "Minimum" or "Maximum" => SetLimit(component, edit.Hotspot, joined),
                "Items" when edit.Kind == DesignInPlaceKind.Items
                    => SetProperty(component, "Items", CellValue.Row(lines.Select(static l => (DesignValue)new CharValue(l)).ToArray())),
                "Items" or "ColumnName" => SetItem(component, edit.Property, edit.Hotspot, joined),
                _ => null,
            };
        }

        if (refused is null && edit.Kind == DesignInPlaceKind.Hyperlink && url is not null && url != edit.Url && DesignInPlace.IsValidUrl(url))
        {
            refused = SetProperty(component, "URL", new CharValue(url));
        }

        return refused;
    }

    /// <summary>
    /// Sets an image's <c>ImageSource</c> to the file <paramref name="path"/> chosen for it (the
    /// image's file browser): kept beside the app when it is in the app's folder, else by its full path.
    /// </summary>
    /// <returns>Null when set; else what the image said, and nothing changed.</returns>
    public string? SetImageFile(DesignComponent component, string path)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = System.IO.Path.GetFullPath(path);
        if (Path is { } app && System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(app)) is { } folder)
        {
            string relative = System.IO.Path.GetRelativePath(folder, full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative))
            {
                return SetProperty(component, "ImageSource", new ImageFileValue(relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)));
            }
        }

        return SetProperty(component, "ImageSource", new CharValue(full));
    }

    private string? DeleteLabel(DesignComponent label)
    {
        // A labelled component's label emptied goes, selected and deleted (InPlaceEditorManager).
        Select([label]);
        Delete([label]);
        return null;
    }

    private string? SetLimit(DesignComponent component, int index, string text)
    {
        if (DesignInPlace.Number(text) is not { } number || Runtime.ReadNumbers(component, "Limits") is not [var low, var high])
        {
            return null; // the empty text, or what is no number, changes nothing
        }

        if ((index == 0 ? low : high) == number)
        {
            return null;
        }

        return SetProperty(component, "Limits", index == 0 ? NumericValue.Row(number, high) : NumericValue.Row(low, number));
    }

    private string? SetItem(DesignComponent component, string property, int index, string text)
    {
        DesignValue? held = ValueOf(component, property);
        var items = TextLines(held).ToList();
        while (items.Count <= index)
        {
            items.Add(string.Empty);
        }

        items[index] = text;
        DesignValue[] cells = items.Select(static l => (DesignValue)new CharValue(l)).ToArray();
        return SetProperty(component, property, held is CellValue { Cols: 1, Rows: > 1 } ? CellValue.Column(cells) : CellValue.Row(cells));
    }

    private IReadOnlyList<Rect2D> Hotspots(DesignComponent component, Func<DesignComponent, IReadOnlyList<Rect2D>?>? hotspotsOf) =>
        hotspotsOf?.Invoke(component) is { Count: > 0 } drawn ? drawn : HotspotsOf(component);

    /// <summary>The places a component shows, as a table shows the columns inside its box (<c>TableController</c>); all of them for any other.</summary>
    private (int First, int Last) VisibleHotspots(DesignComponent component, IReadOnlyList<Rect2D> spots)
    {
        if (component.Type == "matlab.ui.control.Table" && BoxOf(component) is { } box)
        {
            int first = -1, last = -1;
            for (int i = 0; i < spots.Count; i++)
            {
                Rect2D s = spots[i];
                if (s.Left >= box.Box.Left && s.Top >= box.Box.Top && s.Right <= box.Box.Right && s.Bottom <= box.Box.Bottom)
                {
                    first = first < 0 ? i : first;
                    last = i;
                }
            }

            if (first >= 0)
            {
                return (first, last);
            }
        }

        return (0, spots.Count - 1);
    }

    private int HotspotCount(DesignComponent component) => component.Type switch
    {
        "matlab.ui.control.DiscreteKnob" => System.Math.Max(1, TextLines(ValueOf(component, "Items")).Count),
        "matlab.ui.control.Table" => System.Math.Max(1, TextLines(ValueOf(component, "ColumnName")).Count),
        _ => 2,
    };

    private string? InterpreterOf(DesignComponent component) =>
        component.Type == LabelType ? PlainText(ValueOf(component, "Interpreter")) : null;

    /// <summary>A panel's title as its editor aligns it: by its <c>TitlePosition</c> when at the top, else left.</summary>
    private string TitleAlignment(DesignComponent component) =>
        PlainText(ValueOf(component, "TitlePosition")) is { } position && position.EndsWith("top", StringComparison.Ordinal) && position.Length > 3
            ? position[..^3]
            : "left";

    private static string? PlainText(DesignValue? value) => value switch
    {
        CharValue c => c.Text,
        StringValue s => s.Text,
        CellValue { Items: [CharValue one] } => one.Text,
        CellValue { Items: [StringValue one] } => one.Text,
        _ => null,
    };

    /// <summary>A text property's lines as the editor shows them (the peer node's array): a cell's each, a character row's split at its line breaks.</summary>
    private static List<string> TextLines(DesignValue? value) => value switch
    {
        CharValue c => [.. c.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')],
        StringValue s => [.. s.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')],
        CellValue cell => cell.Items.Select(static i => i switch
        {
            CharValue c => c.Text,
            StringValue s => s.Text,
            _ => DesignValueText.Format(i),
        }).ToList(),
        null => [string.Empty],
        _ => [DesignValueText.Format(value)],
    };

    private static DesignValue LinesValue(string[] lines) =>
        lines.Length == 1 ? new CharValue(lines[0]) : CellValue.Column(lines.Select(static l => (DesignValue)new CharValue(l)).ToArray());

    private static string ImageText(DesignValue? value) => value switch
    {
        ImageFileValue file => file.RelativePath,
        _ => PlainText(value) ?? string.Empty,
    };

    private static string NumberText(DesignValue? value, double fallback) =>
        NumberText(value is NumericValue { Data: [var n] } ? n : fallback);

    /// <summary>A number as the editor's box shows it: as JavaScript writes a number into text, <c>Infinity</c> and all.</summary>
    internal static string NumberText(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value == 0)
        {
            return "0";
        }

        double magnitude = System.Math.Abs(value);
        if (magnitude >= 1e21 || magnitude < 1e-6)
        {
            string e = value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
            int at = e.IndexOf('e', StringComparison.Ordinal);
            if (at < 0)
            {
                e = value.ToString("0.#################e+0", CultureInfo.InvariantCulture);
                at = e.IndexOf('e', StringComparison.Ordinal);
            }

            string mantissa = e[..at];
            string exponent = e[(at + 1)..].TrimStart('+');
            bool negative = exponent.StartsWith('-');
            exponent = exponent.TrimStart('-').TrimStart('0');
            return mantissa + "e" + (negative ? "-" : "+") + (exponent.Length == 0 ? "0" : exponent);
        }

        return value.ToString("0.####################", CultureInfo.InvariantCulture) is var fixedText
               && double.Parse(fixedText, CultureInfo.InvariantCulture) == value
            ? fixedText
            : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
