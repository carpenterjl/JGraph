using JGraph.Core.Model;
using JGraph.Core.Undo;
using JGraph.Interaction.Editing;

namespace JGraph.Controls.Browser;

/// <summary>
/// One entry in an "Add" menu: a caption, whether it is enabled, a tooltip (used to explain why a
/// disabled item is unavailable), an action to run when clicked, and optional child items for a
/// submenu. Purely descriptive so both the tree's context menu and the header "Add" button render
/// from the same source, and so the applicability logic is unit-testable without WPF.
/// </summary>
public sealed record ElementMenuItem(
    string Header,
    bool Enabled = true,
    string? Tooltip = null,
    Action? Invoke = null,
    IReadOnlyList<ElementMenuItem>? Children = null);

/// <summary>
/// Builds the applicable "add element" menu for whatever object is selected in the plot browser.
/// Items whose target already exists are kept but disabled with an explanatory tooltip rather than
/// hidden, so the user is never left guessing why an action is missing.
/// </summary>
internal static class ElementMenuBuilder
{
    /// <summary>
    /// The add-menu items for <paramref name="selected"/> within <paramref name="figure"/>. Empty
    /// when the selection offers nothing to add (a plot, an axis, a grid). <paramref name="confirm"/>
    /// gates the structural, non-undoable removals; it returns true to proceed.
    /// </summary>
    public static IReadOnlyList<ElementMenuItem> Build(
        GraphObject? selected,
        FigureModel figure,
        UndoStack? undo,
        Func<string, string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(confirm);

        return selected switch
        {
            FigureModel fig => FigureItems(fig, undo),
            AxesModel axes => AxesItems(axes, figure, undo, confirm),
            LegendModel legend => LegendItems(legend, undo),
            LegendEntryModel entry => LegendEntryItems(entry, undo),
            _ => Array.Empty<ElementMenuItem>(),
        };
    }

    private static IReadOnlyList<ElementMenuItem> FigureItems(FigureModel figure, UndoStack? undo)
    {
        bool hasTitle = !string.IsNullOrEmpty(figure.Title);
        return new[]
        {
            new ElementMenuItem(
                "Figure title",
                Enabled: !hasTitle,
                Tooltip: hasTitle ? "This figure already has a title." : "Add an editable figure title.",
                Invoke: () => FigureElementCommands.SetFigureTitle(figure, undo: undo)),
            new ElementMenuItem("Add axes", Invoke: () => FigureElementCommands.AddAxes(figure)),
            new ElementMenuItem("Add subplot grid", Children: SubplotGridItems(figure)),
            new ElementMenuItem("Add text annotation", Enabled: figure.Axes.Count > 0,
                Tooltip: figure.Axes.Count == 0 ? "Add an axes first." : null,
                Invoke: () => AddAnnotationToFirstAxes(figure, undo)),
        };
    }

    private static IReadOnlyList<ElementMenuItem> SubplotGridItems(FigureModel figure)
    {
        (string Label, int Rows, int Cols)[] grids =
        {
            ("1 × 2", 1, 2),
            ("2 × 1", 2, 1),
            ("2 × 2", 2, 2),
            ("3 × 1", 3, 1),
        };

        var items = new ElementMenuItem[grids.Length];
        for (int i = 0; i < grids.Length; i++)
        {
            (string label, int rows, int cols) = grids[i];
            items[i] = new ElementMenuItem(label, Invoke: () => FigureElementCommands.ApplySubplotGrid(figure, rows, cols));
        }

        return items;
    }

    private static IReadOnlyList<ElementMenuItem> AxesItems(
        AxesModel axes,
        FigureModel figure,
        UndoStack? undo,
        Func<string, string, bool> confirm)
    {
        bool hasTitle = !string.IsNullOrEmpty(axes.Title);
        return new[]
        {
            new ElementMenuItem(
                "Axes title",
                Enabled: !hasTitle,
                Tooltip: hasTitle ? "This axes already has a title." : "Add an editable axes title.",
                Invoke: () => FigureElementCommands.SetAxesTitle(axes, undo: undo)),
            new ElementMenuItem(
                "Show legend",
                Enabled: !axes.Legend.Visible,
                Tooltip: axes.Legend.Visible ? "This axes already has a legend." : "Show the legend.",
                Invoke: () => FigureElementCommands.ShowLegend(axes, undo)),
            new ElementMenuItem(
                "Show colorbar",
                Enabled: !axes.Colorbar.Visible,
                Tooltip: axes.Colorbar.Visible ? "This axes already has a colorbar." : "Show the colorbar.",
                Invoke: () => FigureElementCommands.ShowColorbar(axes, undo)),
            new ElementMenuItem("Add secondary X axis",
                Invoke: () => FigureElementCommands.AddSecondaryXAxis(axes)),
            new ElementMenuItem("Add secondary Y axis",
                Invoke: () => FigureElementCommands.AddSecondaryYAxis(axes)),
            new ElementMenuItem("Add text annotation",
                Invoke: () => FigureElementCommands.AddText(axes, undo)),
            new ElementMenuItem("Add arrow annotation",
                Invoke: () => FigureElementCommands.AddArrow(axes, undo)),
            new ElementMenuItem("Add shape annotation",
                Invoke: () => FigureElementCommands.AddShape(axes, undo)),
            new ElementMenuItem(
                "Remove axes",
                Enabled: figure.Axes.Count > 1,
                Tooltip: figure.Axes.Count > 1 ? "Remove this axes and its plots (cannot be undone)." : "A figure needs at least one axes.",
                Invoke: () =>
                {
                    if (confirm("Remove axes", "Remove this axes and every plot in it? This cannot be undone."))
                    {
                        FigureElementCommands.RemoveAxes(figure, axes);
                    }
                }),
        };
    }

    private static IReadOnlyList<ElementMenuItem> LegendItems(LegendModel legend, UndoStack? undo)
    {
        LegendEntryModel[] excluded = legend.Entries.Where(e => !e.Visible).ToArray();
        ElementMenuItem addEntry = excluded.Length == 0
            ? new ElementMenuItem("Add entry", Enabled: false, Tooltip: "Every series is already in the legend.")
            : new ElementMenuItem("Add entry", Children: excluded
                .Select(e => new ElementMenuItem(
                    EntryLabel(e),
                    Invoke: () => FigureElementCommands.IncludeLegendEntry(e, undo)))
                .ToArray());

        return new[]
        {
            addEntry,
            new ElementMenuItem(
                "Reset placement",
                Enabled: legend.Position == LegendPosition.Custom,
                Tooltip: legend.Position == LegendPosition.Custom ? "Return the legend to the top-right preset." : "The legend is already at a preset position.",
                Invoke: () => FigureElementCommands.ResetLegendPlacement(legend, undo)),
        };
    }

    private static IReadOnlyList<ElementMenuItem> LegendEntryItems(LegendEntryModel entry, UndoStack? undo)
    {
        if (entry.Parent is not LegendModel legend)
        {
            return Array.Empty<ElementMenuItem>();
        }

        int index = legend.Entries.IndexOf(entry);
        return new[]
        {
            new ElementMenuItem(
                entry.Visible ? "Exclude from legend" : "Include in legend",
                Invoke: () =>
                {
                    if (entry.Visible)
                    {
                        FigureElementCommands.ExcludeLegendEntry(entry, undo);
                    }
                    else
                    {
                        FigureElementCommands.IncludeLegendEntry(entry, undo);
                    }
                }),
            new ElementMenuItem(
                "Move up",
                Enabled: index > 0,
                Invoke: () => FigureElementCommands.MoveLegendEntry(legend, entry, -1, undo)),
            new ElementMenuItem(
                "Move down",
                Enabled: index >= 0 && index < legend.Entries.Count - 1,
                Invoke: () => FigureElementCommands.MoveLegendEntry(legend, entry, +1, undo)),
        };
    }

    private static void AddAnnotationToFirstAxes(FigureModel figure, UndoStack? undo)
    {
        if (figure.Axes.Count > 0)
        {
            FigureElementCommands.AddText(figure.Axes[0], undo);
        }
    }

    private static string EntryLabel(LegendEntryModel entry) =>
        entry.Plot is { DisplayName: { Length: > 0 } name } ? name : "Series";
}
