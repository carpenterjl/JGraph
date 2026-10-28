namespace JGraph.Scripting.Toolstrip;

/// <summary>A section as the toolstrip's resize sees it: what it can be told to do, and its priority.</summary>
public interface IToolstripResizable
{
    /// <summary>Higher collapses later; -1 never collapses.</summary>
    int CollapsePriority { get; }

    /// <summary>Whether the section is shown as its drop-down.</summary>
    bool IsCollapsed { get; }

    /// <summary>Shows the section as its drop-down, its columns in the drop-down's popup.</summary>
    void Collapse();

    /// <summary>Shows the section's columns on the tab again.</summary>
    void Expand();

    /// <summary>Shows or hides the labels of the columns that may drop them (<see cref="ToolstripRules.CanHideLabels"/>).</summary>
    void ShowLabels(bool show);
}

/// <summary>
/// What a toolstrip tab does when its width changes, transcribed from R2025b's toolstrip framework
/// (<c>MW/toolstrip/widgets/Tab.resize</c>; App Designer window plan, stage W0). The sections are
/// ordered by collapse priority, highest first, ties in their order on the tab. When the tab is too
/// narrow, labels drop from the last section towards the first, then sections collapse to their
/// drop-downs from the last towards the first, each collapsed section showing its labels again in
/// its popup. When the tab widens, sections expand from the first, without labels, as long as they
/// fit; only when every section is expanded do labels come back, from the first section, as long
/// as they fit. So while any section is collapsed, the expanded ones have no labels.
/// </summary>
/// <remarks>
/// The framework's galleries, which shrink before any label drops, are left out: no toolstrip of
/// App Designer's has one. A section that the widening pass expands and then has to collapse again
/// gets its labels back in its popup here; R2025b's pass leaves them hidden there, so its popup
/// shows bare icons until the next narrowing (recorded in the stage's ADR). The tab's own width
/// is the same either way.
/// </remarks>
public static class ToolstripResizer
{
    /// <summary>
    /// The width the tab keeps beside its sections for the button that collapses the whole
    /// toolstrip: 20 pixels, 50 in the framework's compact mode, which App Designer does not use.
    /// </summary>
    public const double CollapseButtonWidth = 20;

    /// <summary>
    /// Fits a tab's sections to its width, changing their states as the framework does.
    /// </summary>
    /// <param name="sections">The tab's sections, in their order on the tab.</param>
    /// <param name="available">The tab's width now.</param>
    /// <param name="sectionsWidth">The width the sections take in their present states, with <see cref="CollapseButtonWidth"/>; measured again after each change.</param>
    public static void Resize(IReadOnlyList<IToolstripResizable> sections, Func<double> available, Func<double> sectionsWidth)
    {
        // A stable sort: the highest priority first, equal ones in their order on the tab.
        IToolstripResizable[] t = [.. sections.Select((s, i) => (s, i)).OrderByDescending(p => p.s.CollapsePriority).ThenBy(p => p.i).Select(p => p.s)];
        int n = t.Length;
        double a = available();
        double c = sectionsWidth();
        if (a < c)
        {
            for (int u = n - 1; u >= 0 && a < c; u--)
            {
                t[u].ShowLabels(false);
                c = sectionsWidth();
                a = available();
            }

            for (int u = n - 1; u >= 0 && a < c; u--)
            {
                if (t[u].CollapsePriority != -1)
                {
                    t[u].Collapse();
                }

                t[u].ShowLabels(true);
                c = sectionsWidth();
                a = available();
            }

            return;
        }

        for (int u = 0; u < n && a > c; u++)
        {
            t[u].Expand();
            t[u].ShowLabels(false);
            c = sectionsWidth();
            a = available();
            if (a < c)
            {
                // R2025b measures nothing more here, so the pass that brings labels back does not run:
                // while a section stays collapsed, the expanded ones stay without labels.
                t[u].Collapse();
                if (t[u].IsCollapsed)
                {
                    t[u].ShowLabels(true);
                }

                break;
            }
        }

        for (int u = 0; u < n && a > c; u++)
        {
            t[u].ShowLabels(true);
            c = sectionsWidth();
            a = available();
            if (a < c)
            {
                t[u].ShowLabels(false);
                break;
            }
        }
    }
}
