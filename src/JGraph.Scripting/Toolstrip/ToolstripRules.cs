namespace JGraph.Scripting.Toolstrip;

/// <summary>
/// How R2025b's toolstrip framework lays out a column, read from its <c>Column</c> and
/// <c>Section</c> widgets (App Designer window plan, stage W0): which buttons are drawn large and
/// which small, and which columns may drop their labels when the tab is too narrow.
/// </summary>
public static class ToolstripRules
{
    /// <summary>
    /// Whether a column's control is laid out vertically - a large button, its icon over its label -
    /// rather than horizontally, a small icon beside its label. A column of one control draws it
    /// large; a column of several draws them small (<c>Column._updateChildrenOrientations</c>).
    /// Only buttons have an orientation; a panel's columns follow the same rule when the panel is
    /// its column's only control, and are small otherwise.
    /// </summary>
    /// <param name="column">The column the control is in.</param>
    /// <param name="item">The control.</param>
    public static bool IsVertical(ToolstripColumn column, ToolstripItem item) =>
        item.IsButton && column.Items.Count == 1 && ReferenceEquals(column.Items[0], item);

    /// <summary>
    /// Whether a control in a column of a panel is laid out vertically: when the panel is the only
    /// control of its column and the panel's column holds one control.
    /// </summary>
    /// <param name="outer">The column that holds the panel.</param>
    /// <param name="inner">The panel's column.</param>
    /// <param name="item">The control.</param>
    public static bool IsVerticalInPanel(ToolstripColumn outer, ToolstripColumn inner, ToolstripItem item) =>
        item.IsButton && outer.Items.Count == 1 && inner.Items.Count == 1;

    /// <summary>
    /// Whether a column's labels may be hidden to make the tab narrower
    /// (<c>Section._isColumnEligibleToHideDescendantLabels</c> with <c>toggleDescendantLabels</c>):
    /// a column without a fixed width whose controls, empty controls aside, are all small buttons
    /// with a label.
    /// </summary>
    public static bool CanHideLabels(ToolstripColumn column)
    {
        if (column.Width != 0)
        {
            return false;
        }

        foreach (ToolstripItem item in column.Items)
        {
            if (item.Kind == ToolstripItemKind.EmptyControl)
            {
                continue;
            }

            if (!item.IsButton || IsVertical(column, item) || string.IsNullOrEmpty(item.Text))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A panel's rows: consecutive columns side by side on one row, and each other control on a row
    /// of its own (the VIEW tab's grid interval label and spinner over Snap to Grid).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ToolstripPanelChild>> PanelRows(ToolstripItem panel)
    {
        var rows = new List<IReadOnlyList<ToolstripPanelChild>>();
        List<ToolstripPanelChild>? columns = null;
        foreach (ToolstripPanelChild child in panel.Children)
        {
            if (child.Column is not null)
            {
                if (columns is null)
                {
                    columns = [];
                    rows.Add(columns);
                }

                columns.Add(child);
            }
            else
            {
                columns = null;
                rows.Add([child]);
            }
        }

        return rows;
    }

    /// <summary>
    /// A label as a button draws it: a large button breaks its label where the text has a line
    /// break; a small one, and a tooltip's title, run it together on one line.
    /// </summary>
    /// <param name="text">The label.</param>
    /// <param name="vertical">Whether the button is large.</param>
    public static string Label(string? text, bool vertical)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        if (vertical)
        {
            return string.Join("\n", text.Split('\n').Select(l => l.Trim()));
        }

        return string.Join(" ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    /// <summary>
    /// The tooltip of a control: its label on one line, the shortcut in brackets, then its
    /// description - or the description alone when there is no label.
    /// </summary>
    public static string ToolTip(string? text, string? description, string? shortcut)
    {
        string title = Label(text, vertical: false);
        if (shortcut is { Length: > 0 })
        {
            title = title.Length > 0 ? $"{title} ({shortcut})" : $"({shortcut})";
        }

        if (string.IsNullOrEmpty(description) || description == title)
        {
            return title;
        }

        return title.Length > 0 ? $"{title}\n{description}" : description;
    }

    /// <summary>
    /// The key tip of the quick access bar's n-th button (from 0): 1 to 9, then 01 to 09, then 0A to
    /// 0Z, then none (<c>MnemonicsManager</c>'s numbering).
    /// </summary>
    public static string? QuickAccessKeyTip(int index) => index switch
    {
        < 0 => null,
        < 9 => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        < 18 => "0" + (index - 8).ToString(System.Globalization.CultureInfo.InvariantCulture),
        < 44 => "0" + (char)('A' + index - 18),
        _ => null,
    };
}
