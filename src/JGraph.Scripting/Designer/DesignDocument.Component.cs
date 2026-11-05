using System.Text.RegularExpressions;

namespace JGraph.Scripting.Designer;

/// <content>
/// Custom UI component authoring (App Designer window plan W15): the events with public callbacks and
/// the managed properties App Designer's dialogs add, edit and delete (<c>AddCodeEventCommand</c>,
/// <c>UpdateCodeEventCommand</c>, <c>RemoveCodeEventCommand</c>, <c>AddManagedUserPropertyCommand</c>,
/// <c>ModifyManagedUserPropertyCommand</c>, <c>RemoveManagedUserPropertyCommand</c>), each one step of
/// the undo history; the <c>Give Test Cases Access</c> setting; and the <c>update</c> method's body,
/// taken back from the Code View with the callbacks' bodies.
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>Whether the document is a custom UI component's (its design's root stands for the component).</summary>
    public bool IsComponent => Design.IsComponent;

    /// <summary>A component's events with public callbacks, in the order added; empty for an app.</summary>
    public IReadOnlyList<DesignCodeEvent> Events => Code?.Events ?? [];

    /// <summary>A component's managed properties, in the order added; empty for an app.</summary>
    public IReadOnlyList<DesignManagedProperty> ManagedProperties => Code?.ManagedProperties ?? [];

    /// <summary>Whether a component gives test cases access to its underlying components.</summary>
    public bool AllowTestCaseAccess => Code?.AllowTestCaseAccess ?? false;

    /// <summary>
    /// Why <paramref name="name"/> cannot be an event's name (<c>EventDialogController._handleNameChange</c>):
    /// not a name of the app's (<paramref name="current"/>, the event's own, is free), and not ending in
    /// <c>Fcn</c>, which the public callback appends. Null when it can.
    /// </summary>
    public string? WhyNotEventName(string name, string? current = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (DesignNames.WhyNotName(name, current, NamesInUse(), WhatANameIs) is { } why)
        {
            return why;
        }

        return name.EndsWith("Fcn", StringComparison.OrdinalIgnoreCase) ? DesignComponentCode.FcnSuffixWarning : null;
    }

    /// <summary>Why <paramref name="name"/> cannot be a managed property's name (<c>CodeEditUtil.validateAttributeName</c>); null when it can.</summary>
    public string? WhyNotManagedPropertyName(string name, string? current = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return DesignNames.WhyNotName(name, current, NamesInUse(), WhatANameIs);
    }

    /// <summary>Adds the event <paramref name="name"/> with <paramref name="description"/>, one undoable step.</summary>
    /// <returns>Null when added; else why not, and nothing changed.</returns>
    public string? AddEvent(string name, string description = "")
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        if (Code is null || !IsComponent)
        {
            return CodeProblem ?? "Only a custom UI component has events with public callbacks.";
        }

        if (WhyNotEventName(name) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        Do([new EventListEdit(Design.Figure, new DesignCodeEvent(name, description), Add: true)]);
        return null;
    }

    /// <summary>
    /// Gives <paramref name="existing"/> the name <paramref name="name"/> and <paramref name="description"/>
    /// (<c>UpdateCodeEventCommand</c>): the user's <c>notify(comp, 'Old')</c> calls follow a renamed event.
    /// </summary>
    /// <returns>Null when changed; else why not, and nothing changed.</returns>
    public string? UpdateEvent(DesignCodeEvent existing, string name, string description)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        if (Code is null || !Code.Events.Contains(existing))
        {
            return CodeProblem ?? "The component has no such event.";
        }

        if (name != existing.Name && WhyNotEventName(name, existing.Name) is { } why)
        {
            return why;
        }

        if (name == existing.Name && description == existing.Description)
        {
            return null;
        }

        var step = new List<Edit> { new EventChangeEdit(Design.Figure, existing, existing.Name, existing.Description, name, description) };
        if (name != existing.Name)
        {
            step.Add(new UserCodeEdit(Design.Figure, this, NotifyRewrite(existing.Name, name)));
        }

        EndGesture(commit: false);
        Do([.. step]);
        return null;
    }

    /// <summary>Takes the event <paramref name="existing"/> out (<c>RemoveCodeEventCommand</c>); the user's code that fires it stays.</summary>
    /// <returns>Null when removed; else why not.</returns>
    public string? RemoveEvent(DesignCodeEvent existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (Code is null || !Code.Events.Contains(existing))
        {
            return CodeProblem ?? "The component has no such event.";
        }

        EndGesture(commit: false);
        Do([new EventListEdit(Design.Figure, existing, Add: false)]);
        return null;
    }

    /// <summary>Adds the managed property <paramref name="property"/>, one undoable step; its default value is checked first (<see cref="DesignRuntime.ValidateManagedProperty"/>).</summary>
    /// <returns>Null when added; else why not, and nothing changed.</returns>
    public string? AddManagedProperty(DesignManagedProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (Code is null || !IsComponent)
        {
            return CodeProblem ?? "Only a custom UI component has managed properties.";
        }

        if (WhyNotManagedPropertyName(property.Name) is { } why)
        {
            return why;
        }

        if (Runtime.ValidateManagedProperty(property).Problem is { } refused)
        {
            return refused;
        }

        EndGesture(commit: false);
        Do([new ManagedListEdit(Design.Figure, property, Add: true)]);
        return null;
    }

    /// <summary>
    /// Makes <paramref name="existing"/> what <paramref name="replacement"/> says (<c>ModifyManagedUserPropertyCommand</c>):
    /// the user's <c>comp.Old</c> references follow a renamed property.
    /// </summary>
    /// <returns>Null when changed; else why not, and nothing changed.</returns>
    public string? ModifyManagedProperty(DesignManagedProperty existing, DesignManagedProperty replacement)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);
        if (Code is null || !Code.ManagedProperties.Contains(existing))
        {
            return CodeProblem ?? "The component has no such property.";
        }

        if (replacement.Name != existing.Name && WhyNotManagedPropertyName(replacement.Name, existing.Name) is { } why)
        {
            return why;
        }

        if (Runtime.ValidateManagedProperty(replacement).Problem is { } refused)
        {
            return refused;
        }

        if (existing.Declaration == replacement.Declaration && existing.BlockId == replacement.BlockId)
        {
            return null;
        }

        var step = new List<Edit> { new ManagedChangeEdit(Design.Figure, existing, existing.Clone(), replacement.Clone()) };
        if (replacement.Name != existing.Name)
        {
            step.Add(new UserCodeEdit(Design.Figure, this, ComponentRewrite(existing.Name, replacement.Name)));
        }

        EndGesture(commit: false);
        Do([.. step]);
        return null;
    }

    /// <summary>Takes the managed property <paramref name="existing"/> out (<c>RemoveManagedUserPropertyCommand</c>); the user's code that reads it stays.</summary>
    /// <returns>Null when removed; else why not.</returns>
    public string? RemoveManagedProperty(DesignManagedProperty existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (Code is null || !Code.ManagedProperties.Contains(existing))
        {
            return CodeProblem ?? "The component has no such property.";
        }

        EndGesture(commit: false);
        Do([new ManagedListEdit(Design.Figure, existing, Add: false)]);
        return null;
    }

    /// <summary>
    /// Sets a public property's default value from the inspector's row (<c>validateInspectorPropertyValue</c>,
    /// <c>InspectableComponentNodeController._handlePropertiesSet</c>): the typed <paramref name="text"/>, checked
    /// against the property's class, size and validation functions, becomes its default.
    /// </summary>
    /// <returns>Null when set; else why not.</returns>
    public string? SetManagedDefault(DesignManagedProperty existing, string text)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(text);
        (string? canonical, string? problem) = Runtime.CanonicalDefault(existing, text);
        if (problem is not null)
        {
            return problem;
        }

        DesignManagedProperty replacement = existing.Clone();
        replacement.DefaultValue = canonical ?? string.Empty;
        return ModifyManagedProperty(existing, replacement);
    }

    /// <summary>Gives test cases access to the underlying components, or takes it back (<c>AllowTestCaseAccess</c>), one undoable step.</summary>
    /// <returns>Null when set; else why not.</returns>
    public string? SetAllowTestCaseAccess(bool on)
    {
        if (Code is null || !IsComponent)
        {
            return CodeProblem ?? "Only a custom UI component gives test cases access.";
        }

        if (Code.AllowTestCaseAccess == on)
        {
            return null;
        }

        EndGesture(commit: false);
        Do([new TestCaseAccessEdit(Design.Figure, on)]);
        return null;
    }

    /// <summary><c>notify(comp, 'from')</c> renamed on a line, whichever quotes it uses (<c>renameEditableCode</c> for an event).</summary>
    private static Func<string, string> NotifyRewrite(string from, string to)
    {
        var reference = new Regex(@"(\bnotify\s*\(\s*" + DesignComponentCode.ObjectName + @"\s*,\s*['""])" + Regex.Escape(from) + @"(['""])");
        return line => reference.Replace(line, m => m.Groups[1].Value + to + m.Groups[2].Value);
    }

    /// <summary>An event put into or taken out of the component's list.</summary>
    private sealed record EventListEdit(DesignComponent Component, DesignCodeEvent Event, bool Add) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => Apply(document, Add);

        public override void Undo(DesignDocument document) => Apply(document, !Add);

        private void Apply(DesignDocument document, bool add)
        {
            List<DesignCodeEvent> events = document.Code!.Events;
            if (add)
            {
                if (!events.Contains(Event))
                {
                    events.Add(Event);
                }
            }
            else
            {
                events.Remove(Event);
            }
        }
    }

    /// <summary>An event's name and description changed in place.</summary>
    private sealed record EventChangeEdit(DesignComponent Component, DesignCodeEvent Event, string NameBefore, string DescriptionBefore, string NameAfter, string DescriptionAfter) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => (Event.Name, Event.Description) = (NameAfter, DescriptionAfter);

        public override void Undo(DesignDocument document) => (Event.Name, Event.Description) = (NameBefore, DescriptionBefore);
    }

    /// <summary>A managed property put into or taken out of the component's list.</summary>
    private sealed record ManagedListEdit(DesignComponent Component, DesignManagedProperty Property, bool Add) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => Apply(document, Add);

        public override void Undo(DesignDocument document) => Apply(document, !Add);

        private void Apply(DesignDocument document, bool add)
        {
            List<DesignManagedProperty> properties = document.Code!.ManagedProperties;
            if (add)
            {
                if (!properties.Contains(Property))
                {
                    properties.Add(Property);
                }
            }
            else
            {
                properties.Remove(Property);
            }
        }
    }

    /// <summary>A managed property made what another says, in place, so that the inspector's and the browser's rows keep it.</summary>
    private sealed record ManagedChangeEdit(DesignComponent Component, DesignManagedProperty Property, DesignManagedProperty Before, DesignManagedProperty After) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => Copy(After, Property);

        public override void Undo(DesignDocument document) => Copy(Before, Property);

        private static void Copy(DesignManagedProperty from, DesignManagedProperty into)
        {
            into.Name = from.Name;
            into.Description = from.Description;
            into.DataType = from.DataType;
            into.DefaultValue = from.DefaultValue;
            into.Size.Clear();
            into.Size.AddRange(from.Size);
            into.ValidationFunctions.Clear();
            into.ValidationFunctions.AddRange(from.ValidationFunctions);
            into.Access = from.Access;
            into.SetFlags(from.Flags);
        }
    }

    /// <summary>The test case access toggled.</summary>
    private sealed record TestCaseAccessEdit(DesignComponent Component, bool After) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => document.Code!.AllowTestCaseAccess = After;

        public override void Undo(DesignDocument document) => document.Code!.AllowTestCaseAccess = !After;
    }
}
