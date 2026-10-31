using JGraph.Core.Primitives;

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
        return SetAll(targets.Select(t => (t, name, value)));
    }

    /// <summary>
    /// Sets several properties as the inspector edits them, as one step: what a rich editor's change comes to, such as
    /// a gauge's colours with their limits, or a drop-down's items with its value.
    /// </summary>
    /// <returns>Null when done or when none needed it; else what refused it, and nothing changed.</returns>
    public string? SetInspected(DesignInspection inspection, IReadOnlyList<(string Name, DesignValue? Value)> changes)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(changes);
        return SetAll(changes.SelectMany(c => DesignInspectorSheet.EditTargets(inspection, c.Name).Select(t => (t, c.Name, c.Value))));
    }

    // CompositePropertySetCommand: all of them, or, when one refuses, those done put back.
    private string? SetAll(IEnumerable<(DesignComponent Target, string Name, DesignValue? Value)> changes)
    {
        EndGesture(commit: false);
        var edits = new List<Edit>();
        List<string> inUse = NamesInUse().ToList();
        foreach ((DesignComponent target, string name, DesignValue? value) in changes)
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

    // --- sized to its text ------------------------------------------------------------------------------

    // What a component's widget asks for as it now reads: its text in its font.
    private Size2D WidgetSize(DesignComponent component)
    {
        double size = ValueOf(component, "FontSize") is NumericValue { Data: [var points] } && points > 0 ? points : 12;
        string font = ValueOf(component, "FontName") is CharValue { Text.Length: > 0 } name ? name.Text : "Helvetica";
        bool bold = ValueOf(component, "FontWeight") is CharValue { Text: "bold" };
        bool italic = ValueOf(component, "FontAngle") is CharValue { Text: "italic" };
        IReadOnlyList<string> lines = component.Class?.Property("Text") is null ? [string.Empty] : TextLines(ValueOf(component, "Text"));
        return DesignPreferredSize.Widget(component.Type, lines, font, size, bold, italic);
    }

    // setComponentToPreferredSize once the value is set; a grid's child goes where its cells say.
    private Edit? FitEdit(DesignComponent component, Size2D previous)
    {
        if (IsGridChild(component) || !DesignPreferredSize.TryRule(component.Type, out DesignPreferredRule rule)
            || Runtime.ReadNumbers(component, "Position") is not [var x, var y, var width, var height])
        {
            return null;
        }

        var shown = new Size2D(width, height);
        Size2D made = Palette.BoxOf(component.Type).Size;
        string alignment = LikeOf(component, "HorizontalAlignment") is { } a ? DesignPalette.Text(a) ?? "left" : "left";
        bool wrap = LikeOf(component, "WordWrap") is OnOffValue { IsOn: true } or NumericValue { Data: [not 0] };
        (Size2D size, Point2D at) = DesignPreferredSize.Fit(
            rule, DesignPreferredSize.Minimum(component.Type), made, WidgetSize(component), previous, shown, shown, new Point2D(x, y), alignment, wrap);
        return size.Equals(shown) && at.X == x && at.Y == y
            ? null
            : new PositionEdit(component, component.Get("Position"), NumericValue.Row(at.X, at.Y, size.Width, size.Height), NumericValue.Row(x, y, width, height)) { Selects = false };
    }

    /// <summary>What is held of a component's image as the file picker's box shows it.</summary>
    public static string ImagePathText(DesignValue? value) => ImageText(value);
}
