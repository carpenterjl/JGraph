namespace JGraph.Scripting.Designer;

/// <content>
/// Callbacks over a selection (the App Designer window plan's stage W7d): what the Callbacks tab and the Callbacks submenu are over, and an edit of
/// several as one step, as <c>MassEditCallbackCommand</c> runs it.
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>
    /// What the menu's Callbacks are over (<c>ComponentMenuItemsProvider._getMenuItemsInfo</c>): the selection, a group's as its members, and a labelled component's
    /// label left out with its component beside it. The first has the say.
    /// </summary>
    public IReadOnlyList<DesignComponent> MenuComponents =>
        SelectionIsEmpty
            ? []
            : _selection.Where(c => !(Design.LabeledGroupOf(c) is { } g && ReferenceEquals(g.Label, c) && _selection.Contains(g.Field!, ReferenceEqualityComparer.Instance))).ToArray();

    /// <summary>
    /// What the Callbacks tab is over (<c>CallbackPropertyViewFactory</c>): the app's own row, its one row; one component, or a labelled component's, its own groups;
    /// several, what they have in common with no group's title (<c>CallbackMultiselectView</c>), the labels of labelled components left out and the last one's values shown. Null with nothing selected.
    /// </summary>
    public DesignCallbackSheet? CallbackSheet
    {
        get
        {
            if (AppSelected)
            {
                return new DesignCallbackSheet([], null, [new DesignCallbackGroup(null, [DesignCallbackRules.StartupProperty])], null);
            }

            if (Inspection is not { } inspection)
            {
                return null;
            }

            if (inspection.Kind is DesignInspectionKind.Single or DesignInspectionKind.Labeled)
            {
                IReadOnlyList<DesignCallbackGroup> own = CallbackGroupsOf(inspection.Anchor);
                string what = DesignNames.Of(inspection.Anchor.Type)?.Text ?? inspection.Anchor.Class?.ShortName ?? inspection.Anchor.Type;
                return new DesignCallbackSheet([inspection.Anchor], inspection.Anchor, own, own.Count == 0 ? $"{what} {DesignCallbackRules.NoCallbacks}" : null);
            }

            List<DesignComponent> peers = inspection.Components.Where(c => !(Design.LabeledGroupOf(c) is { } g && ReferenceEquals(g.Label, c))).ToList();
            IReadOnlyList<string> common = DesignCallbackRules.Common(peers.Select(CallbackPropertiesOf).ToArray());
            return common.Count == 0
                ? new DesignCallbackSheet(peers, null, [], DesignCallbackRules.NothingInCommon)
                : new DesignCallbackSheet(peers, peers[^1], [new DesignCallbackGroup(null, common)], null);
        }
    }

    /// <summary>Whether a callback's row and item are greyed out for what the component is: a responsive app's region's <c>SizeChangedFcn</c> (<c>RegionPanelController.shouldDisableCallback</c>).</summary>
    public bool IsCallbackLocked(DesignComponent component, string property)
    {
        ArgumentNullException.ThrowIfNull(component);
        return property == "SizeChangedFcn" && IsRegion(component);
    }

    /// <summary>
    /// Why <paramref name="name"/> cannot be typed in <paramref name="property"/>'s row (<c>CallbackSection._validateCodeName</c>); null when it can.
    /// </summary>
    public string? WhyNotCallbackName(string property, string name)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(name);
        return DesignCallbackRules.WhyNot(property, name, Code?.Find(name) is not null, Code?.Startup?.Name ?? string.Empty, n => WhyNotFunctionName(n));
    }

    /// <summary>
    /// What a row of the Callbacks tab does with a value (<c>CallbackSection._handleCallbackNameEdited</c>): nothing when it is what <paramref name="anchor"/> holds, whatever the others do;
    /// a name refused says so; the default's name is <paramref name="anchor"/>'s; and a new name leaves an old function where it is.
    /// </summary>
    /// <param name="peers">Who it goes to; see <see cref="MassEditCallback"/>.</param>
    /// <param name="anchor">The component the row shows.</param>
    /// <param name="property">The row's property.</param>
    /// <param name="value">A name, empty to unbind, or <see cref="DesignCallbackRules.UseDefault"/>.</param>
    /// <param name="made">The function made, for the Code View to show; null when none was.</param>
    /// <returns>Null when done or when nothing changed; else why not, and nothing changed.</returns>
    public string? EditCallbackRow(List<DesignComponent> peers, DesignComponent anchor, string property, string value, out string? made)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        made = null;
        if (value == (CallbackOf(anchor, property) ?? string.Empty))
        {
            return null;
        }

        if (value.Length > 0 && value != DesignCallbackRules.UseDefault && WhyNotCallbackName(property, value) is { } why)
        {
            return why;
        }

        return MassEditCallback(peers, value == DesignCallbackRules.UseDefault ? DefaultCallbackName(anchor, property) : value, property, detachPrevious: true, out made);
    }

    /// <summary>
    /// Sets the callback of <paramref name="property"/> on every one of <paramref name="peers"/>, as one edit, the selection staying (<c>MassEditCallbackCommand</c>, by <see cref="DesignCallbackRules.Plan"/>).
    /// The list is reversed in place and gone through in that order, as the command's constructor does, so a tab's edits of the list it keeps go each way in turn.
    /// </summary>
    /// <param name="peers">Who it goes to.</param>
    /// <param name="name">The function; empty to unbind.</param>
    /// <param name="property">The callback property, which they all have.</param>
    /// <param name="detachPrevious">Whether a new name leaves an old function where it is, rather than renaming it.</param>
    /// <param name="made">The function made, for the Code View to show; null when none was.</param>
    /// <returns>Null when done or when nothing changed; else why not, and nothing changed.</returns>
    public string? MassEditCallback(List<DesignComponent> peers, string name, string property, bool detachPrevious, out string? made)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(property);
        made = null;
        if (Code is null)
        {
            return CodeProblem;
        }

        if (peers.FirstOrDefault(p => !CallbackPropertiesOf(p).Contains(property, StringComparer.Ordinal)) is { } lacking)
        {
            return $"{lacking.CodeName} has no callback property {property}.";
        }

        if (name.Length > 0)
        {
            DesignCallback? function = Code.Find(name);
            if (function is { IsStartup: true } or { IsGenerated: true })
            {
                return $"The app has no callback {name} a component can be bound to.";
            }

            if (function is null && WhyNotFunctionName(name) is { } why)
            {
                return why;
            }
        }

        peers.Reverse();
        IReadOnlyList<DesignCallbackStep> steps = DesignCallbackRules.Plan(peers, name, detachPrevious, p => CallbackOf(p, property) ?? string.Empty, n => Code.Find(n) is not null);
        if (steps.Count == 0)
        {
            return null;
        }

        EndGesture(commit: false);
        var edits = new List<Edit>();
        foreach (DesignCallbackStep step in steps)
        {
            if (step.Kind == DesignCallbackStepKind.Rename)
            {
                edits.Add(new RenameCallbackEdit(Design.Figure, Code.Find(step.From)!, step.From, step.To));
                continue;
            }

            if (step.Kind == DesignCallbackStepKind.Add)
            {
                IReadOnlyList<string> defaults = DesignCallbacks.R2025b.DefaultBody(step.Component, property);
                edits.Add(new CallbackListEdit(Design.Figure, new DesignCallback(name, defaults.Select(l => Style.Indent(3) + l)) { DefaultBody = defaults }, NewCallbackIndex(), Add: true));
                made = name;
            }

            string designProperty = DesignCallbacks.DesignPropertyName(step.Component, property);
            edits.Add(new PropertyEdit(step.Component, designProperty, Bound(step.From), Bound(step.To), RuntimeBefore: null, RuntimeAfter: null) { Selects = false });
        }

        edits.Add(new PicksEdit(Design.Figure, [.. _picks], Anchor));
        Do([.. edits]);
        return null;

        static DesignValue? Bound(string function) => function.Length == 0 ? null : new CallbackValue(function);
    }
}
