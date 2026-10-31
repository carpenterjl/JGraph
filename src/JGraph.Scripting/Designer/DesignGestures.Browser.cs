namespace JGraph.Scripting.Designer;

/// <content>
/// The component browser's context menu (App Designer window plan, stage W7a), in
/// <c>DesignAreaContextMenuController._createComponentTreeContextMenu</c>'s order: Include labels, the two
/// sorts, Add Label, Rename, Cut, Copy, Paste, Duplicate, Delete, Reorder, Insert at Cursor, Callbacks with the Context Menu and Help on
/// Selection (W7d), the tab order.
/// </content>
public sealed partial class DesignGestures
{
    /// <summary>
    /// The browser's menu for what is selected, for <paramref name="state"/>'s browser.
    /// </summary>
    /// <param name="state">The app's browser.</param>
    /// <param name="inCodeView">Whether the Code View shows, where the edits are not offered and Insert at Cursor is (<c>getViewMode</c>).</param>
    /// <param name="viewChanged">Run after the browser's list changes.</param>
    /// <param name="rename">Opens the row's editor.</param>
    /// <param name="insert">Inserts the text at the Code View's cursor.</param>
    /// <param name="canInsert">Whether the cursor is on an editable line (<c>getCursorReadOnlyState</c>).</param>
    public IReadOnlyList<DesignMenuItem> BrowserMenu(DesignBrowserState state, bool inCodeView, Action viewChanged, Action rename, Action<string> insert, bool canInsert)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(viewChanged);
        ArgumentNullException.ThrowIfNull(rename);
        ArgumentNullException.ThrowIfNull(insert);
        DesignDocument document = Document;
        DesignClipboard? clipboard = Clipboard;
        var items = new List<DesignMenuItem>();

        // Include labels, shown but for the tab order (IncludeLabelComponentsMenuItemProvider).
        if (state.View != DesignBrowserList.TabOrder)
        {
            bool complete = state.View == DesignBrowserList.Complete;
            items.Add(new DesignMenuItem(DesignBrowser.IncludeLabels, true, () =>
            {
                state.SetView(complete ? DesignBrowserList.ExcludeLabels : DesignBrowserList.Complete);
                viewChanged();
            }) { Checked = complete });
            items.Add(DesignMenuItem.Separator);
        }

        // The sorts (ComponentTreeSortMenuItemProvider): a click on the one checked does nothing.
        bool tabs = state.View == DesignBrowserList.TabOrder;
        items.Add(new DesignMenuItem(DesignBrowser.SortByStackOrder, true, () =>
        {
            if (tabs)
            {
                state.ShowStackOrder();
                viewChanged();
            }
        }) { Checked = !tabs });
        items.Add(new DesignMenuItem(DesignBrowser.SortByTabOrder, true, () =>
        {
            if (!tabs)
            {
                state.SetView(DesignBrowserList.TabOrder);
                viewChanged();
            }
        }) { Checked = tabs });

        bool app = document.AppSelected;
        IReadOnlyList<DesignPick> picks = document.Picks;
        bool none = document.SelectionIsEmpty;
        bool figure = !none && document.Selection is [{ Parent: null }];

        // Add Label (AddLabelMenuItemProvider): one component whose library entry has a label.
        if (!app && picks is [{ Component: { } labelled }] && document.Palette.LabelPlaceOf(labelled.Type) != DesignLabelPlace.None)
        {
            items.Add(DesignMenuItem.Separator);
            items.Add(new DesignMenuItem("Add Label", document.CanAddLabel, () =>
            {
                if (document.AddLabel() is { } refused)
                {
                    Alerted?.Invoke(this, (DesignDocument.AddLabelFailedTitle, refused));
                }
            }) { Shortcut = "Ctrl+L" });
        }

        items.Add(DesignMenuItem.Separator);

        // Rename (ComponentRenameMenuItemProvider), offered in the Design View only.
        if (!inCodeView)
        {
            bool one = !app && (picks is [{ Component: not null }] || document.SelectedLabeled is not null);
            items.Add(new DesignMenuItem("Rename", one, rename) { Shortcut = "F2" });

            // Cut, Copy, Paste and Duplicate (StandardMenuItemsProvider): Paste by the selection, not a point.
            bool copy = !app && document.CanCopy && clipboard is not null;
            items.Add(new DesignMenuItem("Cut", copy, () => document.Cut(clipboard!)) { Shortcut = "Ctrl+X" });
            items.Add(new DesignMenuItem("Copy", copy, () => document.Copy(clipboard!)) { Shortcut = "Ctrl+C" });
            items.Add(new DesignMenuItem("Paste", !app && clipboard is not null && document.CanPaste(clipboard), () => document.Paste(clipboard!)) { Shortcut = "Ctrl+V" });
            if (!none && !figure)
            {
                items.Add(new DesignMenuItem("Duplicate", document.CanDuplicate, () => document.Duplicate()) { Shortcut = "Ctrl+D" });
            }
        }

        // Delete and Reorder: not for nothing, the figure or the app.
        if (!none && !figure)
        {
            items.Add(new DesignMenuItem("Delete", document.Selection.Any(document.CanDelete), () => document.Delete(document.Selection)) { Shortcut = "Delete" });
            bool front = document.CanStack(DesignStack.Front), forward = document.CanStack(DesignStack.Forward);
            bool backward = document.CanStack(DesignStack.Backward), back = document.CanStack(DesignStack.Back);
            items.Add(new DesignMenuItem("Reorder", front || forward || backward || back, Items:
            [
                new("Bring to Front", front, () => document.Stack(DesignStack.Front)),
                new("Bring Forward", forward, () => document.Stack(DesignStack.Forward)),
                new("Send Backward", backward, () => document.Stack(DesignStack.Backward)),
                new("Send to Back", back, () => document.Stack(DesignStack.Back)),
            ]));
        }

        // Insert at Cursor (InsertCodeNameMenuItemProvider): the Code View's, one component.
        if (inCodeView && !app && picks is [{ Component: { } named }])
        {
            items.Add(new DesignMenuItem("Insert at Cursor", canInsert, () => insert(DesignBrowser.LabelOf(named))));
        }

        // Callbacks, the Context Menu submenu and Help on Selection (ComponentMenuItemsProvider, W7d).
        AddComponentItems(items, inCodeView);

        // The tab order (TabOrderMenuItemsProvider): one container, or the whole app.
        if (app || (document.Selection is [var holder] && document.CanApplyAutoTabOrder(holder)))
        {
            DesignComponent? container = app ? null : document.Selection[0];
            items.Add(DesignMenuItem.Separator);
            items.Add(new DesignMenuItem("Apply Auto Tab Order", true, () =>
            {
                document.ApplyAutoTabOrder(container);
                ShowTabOrder(document.TabOrder(container), VisualizeLimit);
            }));
            items.Add(new DesignMenuItem("Visualize Tab Order", true, () => ShowTabOrder(document.TabOrder(container))));
        }

        while (items.Count > 0 && items[^1].IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }
}
