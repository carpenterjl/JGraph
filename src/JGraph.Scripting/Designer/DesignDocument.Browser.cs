namespace JGraph.Scripting.Designer;

/// <content>
/// What App Designer's component browser does to the app (App Designer window plan, stage W7a): the app's
/// own row selected, rows picked into a selection (<c>_setSelection</c>), and rows dropped among their
/// siblings or under another parent, as one edit (<c>TreeDragReorderCommand</c>).
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>Whether the app's own row is selected (<c>isInspectableAppSelected</c>); the canvas then shows nothing selected.</summary>
    public bool AppSelected { get; private set; }

    /// <summary>The name of the app's row: its class's (<c>GeneratedClassName</c>).</summary>
    public string AppName => Code?.ClassName ?? (Path is { } path ? Workspace.AppDesignerDocument.ClassNameFor(path) : "app1");

    /// <summary>Selects the app's own row.</summary>
    public void SelectApp()
    {
        EndGesture(commit: false);
        SelectPicks([DesignPick.Of(Design.Figure)]);
        if (!AppSelected)
        {
            AppSelected = true;
            _nothingSelected = true;
            OnChanged();
        }
    }

    /// <summary>The rows of the browser in <paramref name="view"/>, top to bottom.</summary>
    public IReadOnlyList<DesignBrowserRow> BrowserRows(DesignBrowserList view) => DesignBrowser.Rows(Design, AppName, view);

    /// <summary>
    /// The rows the tree shows selected (<c>_handleSingleSelection</c>, <c>_handleMultiSelection</c>): each
    /// component, a group's as its members; the app's row (a null) when it is selected.
    /// </summary>
    public IReadOnlyList<DesignComponent?> BrowserSelection => AppSelected ? [null] : SelectionIsEmpty ? [] : [.. _selection];

    /// <summary>
    /// Rows picked in the tree (<paramref name="asked"/>, a null for the app's) selected by R2025b's rule
    /// (<see cref="DesignBrowser.SelectionAfter"/>): a member of a group by itself, as the tree selects it.
    /// A tab asked alone shows.
    /// </summary>
    public void BrowserSelect(IReadOnlyList<DesignComponent?> asked)
    {
        ArgumentNullException.ThrowIfNull(asked);
        IReadOnlyList<DesignComponent?>? after = DesignBrowser.SelectionAfter(BrowserSelection, asked, Design.Figure);
        if (after is null)
        {
            return;
        }

        if (after is [null])
        {
            SelectApp();
            return;
        }

        if (after.Count == 0)
        {
            Select([]);
            return;
        }

        if (after is [{ Type: TabType } tab])
        {
            ShowTab(tab);
        }

        SelectPicks([.. after.Select(static c => DesignPick.Of(c!))]);
    }

    /// <summary>Whether <paramref name="sources"/> can drop into <paramref name="target"/>: by <see cref="DesignBrowser.CanDrop"/>, none in a group going to another parent.</summary>
    public bool BrowserCanDrop(IReadOnlyList<DesignComponent> sources, DesignComponent? target)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return DesignBrowser.CanDrop(sources, target)
            && (sources.All(s => ReferenceEquals(s.Parent, target)) || sources.All(s => Design.GroupOf(s) is null));
    }

    /// <summary>
    /// A drop in the tree, as one edit (<c>TreeDragReorderCommand</c>): <paramref name="sources"/> at
    /// <paramref name="place"/> among the rows under <paramref name="target"/> as <paramref name="view"/> lists
    /// them. Among their siblings, by R2025b's rule; under another parent, one after another from the
    /// place they land at. The selection stays on them.
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    public bool BrowserDrop(DesignBrowserList view, IReadOnlyList<DesignComponent> sources, DesignComponent target, DesignBrowserPlace place, DesignComponent? at = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(target);
        if (!BrowserCanDrop(sources, target))
        {
            return false;
        }

        EndGesture(commit: false);
        DesignComponent[] rows = BrowserRows(view).Where(r => ReferenceEquals(r.Parent, target)).Select(static r => r.Component!).ToArray();
        IReadOnlyList<DesignComponent> rowsAfter = DesignBrowser.Moved(rows, sources, place, at);
        if (ReferenceEquals(sources[0].Parent, target))
        {
            List<DesignComponent> children = [.. target.Children];
            List<DesignComponent> order = [.. DesignBrowser.DropOrder(view, children, rowsAfter, sources)];
            return ReorderEdits(target, children, order) is { } edit && DoOrder([edit]);
        }

        // MoveCommand and DeleteAndCopyCommand both end here: the one object goes over, and its callbacks with it.
        int landing = Array.FindIndex(rowsAfter.ToArray(), c => ReferenceEquals(c, sources[0]));
        var edits = new List<Edit>();
        foreach (DesignComponent source in sources.Reverse())
        {
            edits.Add(new ParentEdit(source, source.Parent!, IndexIn(source.Parent!, source), target, landing));
        }

        Do([.. edits]);
        SelectPicks([.. sources.Select(static s => DesignPick.Of(s))]);
        return true;
    }

    /// <summary>
    /// The browser's collapsed form (<c>_generateBreadcrumbDataFromSelection</c>): from the app down to the
    /// selection; a labelled component as its field; a plain group as its first member's parent and
    /// <c>Group(n)</c>; several as their parent and <c>Multiple Components(n)</c>.
    /// </summary>
    public IReadOnlyList<DesignBreadcrumb> Breadcrumbs()
    {
        var crumbs = new List<DesignBreadcrumb>();
        DesignComponent? down;
        DesignBreadcrumb? end = null;
        if (AppSelected)
        {
            down = null;
        }
        else if (_picks.Count > 1)
        {
            down = _selection[0].Parent;
            end = new DesignBreadcrumb($"Multiple Components({_picks.Count})", null, false);
        }
        else if (_picks is [{ Group: { } group }])
        {
            if (group is { IsLabeled: true, Field: { } field })
            {
                down = field;
            }
            else
            {
                down = group.AllComponents().FirstOrDefault()?.Parent;
                end = new DesignBreadcrumb($"Group({group.Members.Count})", null, false);
            }
        }
        else
        {
            down = _selection.Count > 0 ? _selection[0] : Design.Figure;
        }

        DesignComponent? hidden = DesignResponsive.Grid(Design);
        for (DesignComponent? c = down; c is not null; c = c.Parent)
        {
            if (!ReferenceEquals(c, hidden))
            {
                crumbs.Insert(0, new DesignBreadcrumb(DesignBrowser.CrumbText(c.CodeName), c, false));
            }
        }

        crumbs.Insert(0, new DesignBreadcrumb(DesignBrowser.CrumbText(AppName), null, true));
        if (end is not null)
        {
            crumbs.Add(end);
        }

        return crumbs;
    }
}
