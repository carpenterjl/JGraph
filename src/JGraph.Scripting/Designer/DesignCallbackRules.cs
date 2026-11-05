namespace JGraph.Scripting.Designer;

/// <summary>What an edit of a callback over several components does to one of them, in the order it is done.</summary>
public enum DesignCallbackStepKind
{
    /// <summary>A new function, bound to the component's property (<c>AddCallbackCommand</c>).</summary>
    Add,

    /// <summary>The property, unbound, bound to a function the app has (<c>AttachCallbackCommand</c>).</summary>
    Attach,

    /// <summary>The property moved to another function the app has (<c>ReassignCallbackCommand</c>).</summary>
    Reassign,

    /// <summary>The property unbound, its function kept (<c>DetachAppCallbackFunctionCommand</c>).</summary>
    Detach,

    /// <summary>The function the property holds given the new name, and whatever is bound to it following (<c>RenameAppCallbackFunctionCommand</c>).</summary>
    Rename,
}

/// <summary>One step of an edit of a callback over several components.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Component">The component it does it to.</param>
/// <param name="From">The function the property held; empty for none.</param>
/// <param name="To">The function it holds after; empty for none.</param>
public sealed record DesignCallbackStep(DesignCallbackStepKind Kind, DesignComponent Component, string From, string To);

/// <summary>What an item of the Callbacks submenu is.</summary>
public enum DesignCallbackItemKind
{
    /// <summary>Makes a callback for the property, named as App Designer names it.</summary>
    Add,

    /// <summary>Goes to the function the property holds.</summary>
    GoTo,

    /// <summary>A submenu of a group's items.</summary>
    Group,

    /// <summary>Opens the dialog that binds a property to a function the app has.</summary>
    SelectExisting,
}

/// <summary>An item of the Callbacks submenu.</summary>
/// <param name="Text">What it says.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Property">The callback property it is for; null for a group's and the dialog's.</param>
/// <param name="Callback">The function it goes to; null for any other.</param>
/// <param name="Disabled">Whether it is greyed out.</param>
/// <param name="Items">The items under a group's.</param>
public sealed record DesignCallbackItem(string Text, DesignCallbackItemKind Kind, string? Property = null, string? Callback = null, bool Disabled = false, IReadOnlyList<DesignCallbackItem>? Items = null);

/// <summary>A row's choice in the Callbacks tab: what it says and what it means.</summary>
public readonly record struct DesignCallbackChoice(string Label, string Value)
{
    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>What the Callbacks tab is over.</summary>
/// <param name="Peers">What an edit goes to; the list stays with the tab, as <c>MassEditCallbackCommand</c> reverses the one it is given.</param>
/// <param name="Anchor">The component whose rows' values are shown; null for the app's own row.</param>
/// <param name="Groups">The rows in their groups, titled when there are several.</param>
/// <param name="Message">What it says with no rows.</param>
public sealed record DesignCallbackSheet(List<DesignComponent> Peers, DesignComponent? Anchor, IReadOnlyList<DesignCallbackGroup> Groups, string? Message)
{
    /// <summary>
    /// A custom UI component's own row (W15, <c>PublicCallbackSection</c>): the public callbacks its events
    /// give it, each <c>NameFcn</c>, listed over its private callbacks; null for anything else.
    /// </summary>
    public IReadOnlyList<string>? PublicCallbacks { get; init; }
}

/// <summary>
/// App Designer's rules for callbacks over a selection (the App Designer window plan's stage W7d), free of the screen and of
/// a document, transcribed from R2025b's <c>CallbackUtil</c>, <c>_ComponentContextMenuInfo</c>, <c>MassEditCallbackCommand</c>
/// and <c>CallbackSection</c>.
/// </summary>
public static class DesignCallbackRules
{
    /// <summary>The submenu's own item (<c>editCallbackPopupMenuItem</c>).</summary>
    public const string MenuText = "Callbacks";

    /// <summary>The provider's last item (<c>helpMenuItem</c>).</summary>
    public const string HelpText = "Help on Selection";

    /// <summary>The dialog's item (<c>selectExistingSubMenuItem</c>).</summary>
    public const string SelectExistingText = "Select existing callback...";

    /// <summary>What the tab says when several selected share nothing (<c>multiselectCallbacksMsg</c>).</summary>
    public const string NothingInCommon = "The selected components have no callbacks in common.";

    /// <summary>What follows a class's word when it has no callback properties (<c>noCallbacksMsg</c>).</summary>
    public const string NoCallbacks = "objects do not have callback properties.";

    /// <summary>What an empty row says (<c>callbackPlaceHolderText</c>).</summary>
    public const string Placeholder = "Function name";

    /// <summary>The title of the alert a refused name is said in (<c>errorDialogTitle</c>).</summary>
    public const string InvalidNameTitle = "Invalid Name";

    /// <summary>The row's value that asks for a new function named as App Designer names it (<c>defaultCallbackEntry</c>).</summary>
    public const string UseDefault = "AD_Use_Default";

    /// <summary>The app's own callback property (<c>isStartupCallbackByName</c>).</summary>
    public const string StartupProperty = "StartupFcn";

    /// <summary>Whether <paramref name="property"/> is the startup function's row: an app's <c>StartupFcn</c>, or a custom UI component's <c>PostSetupFcn</c> (<c>isStartupCallbackByName</c>, W15).</summary>
    public static bool IsStartupProperty(string property) => property is StartupProperty or DesignComponentCode.StartupProperty;

    /// <summary>What the startup function's name is refused for when another function has it (<c>invalidAttributeNameMessage_StartupCannotShare</c>).</summary>
    public const string StartupCannotShare = "Startup function name must be unique.";

    /// <summary>What a component's function is refused for when it is the startup function (<c>invalidAttributeNameMessage_CannotShareStartup</c>).</summary>
    public const string CannotShareStartup = "Name cannot be the same as the startup function.";

    /// <summary>
    /// The callbacks several have in common (<c>CallbackUtil.getCommonCallbacks</c>): the second list's that the first has, and so on,
    /// so they stand in the order of the last list.
    /// </summary>
    public static IReadOnlyList<string> Common(IReadOnlyList<IReadOnlyList<string>> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        if (lists.Count == 0)
        {
            return [];
        }

        var common = new List<string>(lists[0]);
        for (int i = 1; i < lists.Count; i++)
        {
            common.AddRange(lists[i]);
            List<string> all = common;
            common = all.Where((name, at) => all.IndexOf(name) != at).ToList();
            if (common.Count == 0)
            {
                break;
            }
        }

        return common;
    }

    /// <summary>
    /// The Callbacks submenu (<c>buildCallbackMenuItemsInfo</c>), over the selection's first component: empty when it has none or
    /// several have none in common. An item says Add or Go to by what that component holds. A class with several groups has its first group's
    /// items and a submenu of each of the others', before Select existing, which is not offered while the first group's are all bound or the app
    /// has no function to bind.
    /// </summary>
    /// <param name="groups">The first component's groups.</param>
    /// <param name="common">What the selection has in common.</param>
    /// <param name="several">Whether more than one is selected: then a group's items are those in common too, where R2025b lists them all and binds properties a component does not have.</param>
    /// <param name="boundTo">What the first component's property holds, empty for nothing.</param>
    /// <param name="locked">Whether a property is greyed out (<c>shouldDisableCallback</c>).</param>
    /// <param name="hasFunction">Whether the app has a function a component's property can be bound to.</param>
    /// <param name="isApp">Whether it is the app's own row, which binds nothing.</param>
    public static IReadOnlyList<DesignCallbackItem> SubMenu(
        IReadOnlyList<DesignCallbackGroup> groups, IReadOnlyList<string> common, bool several, Func<string, string> boundTo, Func<string, bool> locked, bool hasFunction, bool isApp)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(common);
        ArgumentNullException.ThrowIfNull(boundTo);
        ArgumentNullException.ThrowIfNull(locked);
        if (groups.Count == 0 || common.Count == 0)
        {
            return [];
        }

        IEnumerable<string> Shown(DesignCallbackGroup group) => several ? group.Properties.Where(common.Contains) : group.Properties;
        IReadOnlyList<string> first = groups.Count > 1 ? Shown(groups[0]).ToArray() : common;
        bool anyFree = false;
        var items = new List<DesignCallbackItem>();
        foreach (string property in first)
        {
            DesignCallbackItem item = Item(property, boundTo, locked);
            anyFree |= item.Kind == DesignCallbackItemKind.Add;
            items.Add(item);
        }

        foreach (DesignCallbackGroup group in groups.Skip(1))
        {
            DesignCallbackItem[] under = Shown(group).Select(p => Item(p, boundTo, locked)).ToArray();
            if (under.Length > 0)
            {
                items.Add(new DesignCallbackItem(GroupText(group), DesignCallbackItemKind.Group, Items: under));
            }
        }

        items.Add(new DesignCallbackItem(SelectExistingText, DesignCallbackItemKind.SelectExisting, Disabled: isApp || !hasFunction || !anyFree));
        return items;
    }

    private static DesignCallbackItem Item(string property, Func<string, string> boundTo, Func<string, bool> locked)
    {
        string bound = boundTo(property);
        return bound.Length == 0
            ? new DesignCallbackItem($"Add {WordOf(property)} callback", DesignCallbackItemKind.Add, property, Disabled: locked(property))
            : new DesignCallbackItem($"Go to {bound} callback", DesignCallbackItemKind.GoTo, property, bound, locked(property));
    }

    /// <summary>A property as the menu and a row's choice word it: without a trailing <c>Callback</c>.</summary>
    private static string WordOf(string property) =>
        property.EndsWith("Callback", StringComparison.Ordinal) ? property[..^"Callback".Length] : property;

    /// <summary>
    /// A group's submenu (<c>r[H.toLowerCase()+"CallbacksSubMenuItem"]</c>): Keyboard and Window have words of their own, and any other is its
    /// title and Callbacks, where R2025b's own fallback writes the title twice (<c>MouseMouse Callbacks</c>).
    /// </summary>
    private static string GroupText(DesignCallbackGroup group) => group.Title switch
    {
        "KEYBOARD" => "Keyboard Callbacks",
        "WINDOW" => "Window Callbacks",
        var title => title + " " + MenuText,
    };

    /// <summary>
    /// What an edit of a callback does (<c>MassEditCallbackCommand.execute</c>), to each of <paramref name="peers"/> in turn, seeing what those before it did:
    /// nothing bound and a name, the function bound, made when there is none; another bound and a name, moved to the function when there is one, and when there is
    /// none, made with the old one kept if <paramref name="detachPrevious"/>, else the old one renamed, whatever else holds it following; no name, unbound.
    /// </summary>
    /// <param name="peers">Who it goes to, in the order it is done.</param>
    /// <param name="name">The function; empty to unbind.</param>
    /// <param name="detachPrevious">Whether a new name leaves the old function where it is (<c>detachPrevious</c>).</param>
    /// <param name="boundTo">What a component's property holds, empty for nothing.</param>
    /// <param name="exists">Whether the app has a function of a name.</param>
    public static IReadOnlyList<DesignCallbackStep> Plan(IReadOnlyList<DesignComponent> peers, string name, bool detachPrevious, Func<DesignComponent, string> boundTo, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(boundTo);
        ArgumentNullException.ThrowIfNull(exists);
        var holds = new Dictionary<DesignComponent, string>(ReferenceEqualityComparer.Instance);
        foreach (DesignComponent peer in peers)
        {
            holds[peer] = boundTo(peer);
        }

        bool have = name.Length > 0 && exists(name);
        var steps = new List<DesignCallbackStep>();
        foreach (DesignComponent peer in peers)
        {
            string from = holds[peer];
            if (from == name)
            {
                continue;
            }

            if (name.Length == 0)
            {
                steps.Add(new DesignCallbackStep(DesignCallbackStepKind.Detach, peer, from, name));
            }
            else if (from.Length == 0)
            {
                steps.Add(new DesignCallbackStep(have ? DesignCallbackStepKind.Attach : DesignCallbackStepKind.Add, peer, from, name));
            }
            else if (have)
            {
                steps.Add(new DesignCallbackStep(DesignCallbackStepKind.Reassign, peer, from, name));
            }
            else if (detachPrevious)
            {
                steps.Add(new DesignCallbackStep(DesignCallbackStepKind.Detach, peer, from, string.Empty));
                steps.Add(new DesignCallbackStep(DesignCallbackStepKind.Add, peer, string.Empty, name));
            }
            else
            {
                steps.Add(new DesignCallbackStep(DesignCallbackStepKind.Rename, peer, from, name));
                foreach (DesignComponent other in peers.Where(p => holds[p] == from).ToArray())
                {
                    holds[other] = name;
                }
            }

            holds[peer] = name;
            have |= name.Length > 0;
        }

        return steps;
    }

    /// <summary>
    /// A row's choices (<c>CallbackSection._buildCallbacksList</c>): <c>&lt;no callback&gt;</c> when bound, the new function's, what it holds, then the
    /// others sorted, startup functions for the app's row and the rest for any other.
    /// </summary>
    /// <param name="functions">The app's functions, each a startup function or not.</param>
    /// <param name="current">What the row holds, empty for nothing.</param>
    /// <param name="property">The row's property.</param>
    public static IReadOnlyList<DesignCallbackChoice> Choices(IEnumerable<(string Name, bool IsStartup)> functions, string current, string property)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(property);
        var choices = new List<DesignCallbackChoice>();
        if (current.Length > 0)
        {
            choices.Add(new DesignCallbackChoice("<no callback>", string.Empty));
        }

        choices.Add(new DesignCallbackChoice($"<add {WordOf(property)} callback>", UseDefault));
        if (current.Length > 0)
        {
            choices.Add(new DesignCallbackChoice(current, current));
        }

        bool startup = IsStartupProperty(property);
        choices.AddRange(functions.Where(f => f.IsStartup == startup && f.Name != current).Select(static f => f.Name)
            .Order(StringComparer.CurrentCulture).Select(static n => new DesignCallbackChoice(n, n)));
        return choices;
    }

    /// <summary>
    /// What Select existing callback... offers for the first of those selected (<c>SharedCallbackDialog._getCallbacksForSelectedComponent</c>): its properties with nothing bound, but the startup function's,
    /// and only those they all have, where R2025b offers any of the first's and binds the others to a property they do not have.
    /// </summary>
    public static IReadOnlyList<string> Free(IReadOnlyList<string> properties, IReadOnlyList<string> common, Func<string, string> boundTo)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(common);
        ArgumentNullException.ThrowIfNull(boundTo);
        return properties.Where(p => !IsStartupProperty(p) && common.Contains(p) && boundTo(p).Length == 0).ToArray();
    }

    /// <summary>
    /// Why a typed name cannot go in a row (<c>CallbackUtil.validateCallbackFunctionName</c>): the startup row takes no function's name, a component's row not
    /// the startup function's, and a name no function has must do as a name of the app's. Null when it can.
    /// </summary>
    /// <param name="property">The row's property.</param>
    /// <param name="name">What was typed.</param>
    /// <param name="exists">Whether a function has it.</param>
    /// <param name="startupName">The startup function's name, empty for none.</param>
    /// <param name="whyNotName">Why it cannot be a new name of the app's; null when it can.</param>
    public static string? WhyNot(string property, string name, bool exists, string startupName, Func<string, string?> whyNotName)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(startupName);
        ArgumentNullException.ThrowIfNull(whyNotName);
        if (IsStartupProperty(property) && exists)
        {
            return StartupCannotShare;
        }

        if (!IsStartupProperty(property) && name == startupName)
        {
            return CannotShareStartup;
        }

        return exists ? null : whyNotName(name);
    }
}
