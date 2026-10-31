namespace JGraph.Scripting.Designer;

/// <content>
/// The inspector over the selection (the App Designer window plan's stage W7b): what it shows and an edit of it, one
/// step however many components it reaches, as <c>CompositePropertySetCommand</c> runs it.
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>What the selection comes to in the inspector; null for the app's own row and for nothing selected.</summary>
    public DesignInspection? Inspection => AppSelected || SelectionIsEmpty ? null : DesignInspectorSheet.Inspect(_picks);

    /// <summary>The rows the inspector shows for <paramref name="inspection"/>.</summary>
    public IReadOnlyList<DesignInspectorGroup> SheetOf(DesignInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return DesignInspectorSheet.Sheet(inspection, InspectorGroupsOf(inspection.Anchor));
    }

    /// <summary>What the inspector's first tab says.</summary>
    public string InspectorTab => AppSelected ? "App" : SelectionIsEmpty ? DesignInspectorSheet.NoComponent : DesignInspectorSheet.TabOf(_picks);

    /// <summary>Sets <paramref name="name"/> as the inspector edits it: on whoever <see cref="DesignInspectorSheet.EditTargets"/> says, as one step.</summary>
    /// <returns>Null when done; else what refused it, and nothing changed.</returns>
    public string? SetInspected(DesignInspection inspection, string name, DesignValue? value)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(name);
        IReadOnlyList<DesignComponent> targets = DesignInspectorSheet.EditTargets(inspection, name);
        if (targets.Count != 1 || inspection.Components.Count != 1)
        {
            return SetProperties(targets, name, value);
        }

        EndGesture(commit: false);
        var edits = new List<Edit>();
        if (PropertyEdits(targets[0], name, value, edits, null, skipSame: true) is { } refused)
        {
            return refused;
        }

        if (edits.Count > 0)
        {
            Do([.. edits]);
        }

        return null;
    }

    /// <summary>
    /// Sets <paramref name="name"/> on every one of <paramref name="targets"/> able to take it as one edit, the selection staying
    /// (<c>PropertyCommandFactory.executePropertyEditorCommand</c>): one already holding the value is left alone, the renames
    /// the text brings differ from each other, and when one refuses those done are put back and nothing has changed
    /// (<c>CompositePropertySetCommand</c>).
    /// </summary>
    /// <returns>Null when done or when none needed it; else what refused it.</returns>
    public string? SetProperties(IReadOnlyList<DesignComponent> targets, string name, DesignValue? value)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(name);
        EndGesture(commit: false);
        var edits = new List<Edit>();
        List<string> inUse = NamesInUse().ToList();
        foreach (DesignComponent target in targets)
        {
            if (IsEditable(target, name) && PropertyEdits(target, name, value, edits, inUse, skipSame: true) is { } refused)
            {
                foreach (PropertyEdit done in edits.OfType<PropertyEdit>().Reverse())
                {
                    if (done.RuntimeBefore is { } before)
                    {
                        Runtime.Set(done.Component, done.Name, before);
                    }
                }

                return refused;
            }
        }

        if (edits.Count == 0)
        {
            return null;
        }

        Edit[] step = [.. edits.Select(static e => e with { Selects = false }), new PicksEdit(edits[0].Component, [.. _picks], Anchor)];
        Do(step);
        return null;
    }
}
