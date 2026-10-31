namespace JGraph.Scripting.Designer;

/// <content>
/// What <c>ComponentMenuItemsProvider</c> adds to the canvas's, the bars', a tree node's and the browser's menus (the App Designer window plan's
/// stage W7d): Callbacks, the Context Menu submenu and Help on Selection.
/// </content>
public sealed partial class DesignGestures
{
    /// <summary>Raised with a function's name when the Callbacks submenu goes to it, or made it: the Code View shows it.</summary>
    public event EventHandler<string>? CallbackGoTo;

    /// <summary>Raised with who it is for when Select existing callback... is chosen.</summary>
    public event EventHandler<IReadOnlyList<DesignComponent>>? CallbackSelectAsked;

    /// <summary>Raised when Help on Selection is chosen.</summary>
    public event EventHandler? HelpAsked;

    // The provider's items for what is selected, after a line when there is anything above them.
    private void AddComponentItems(List<DesignMenuItem> items, bool inCodeView = false)
    {
        IReadOnlyList<DesignMenuItem> own = ComponentItems(inCodeView);
        if (own.Count > 0)
        {
            if (items.Count > 0 && !items[^1].IsSeparator)
            {
                items.Add(DesignMenuItem.Separator);
            }

            items.AddRange(own);
        }
    }

    // buildMenuItemsInfo: Callbacks, the Context Menu submenu but in the Code View, with a line under it, and Help on Selection.
    private List<DesignMenuItem> ComponentItems(bool inCodeView)
    {
        var items = new List<DesignMenuItem>();
        if (Document.SelectionIsEmpty && !Document.AppSelected)
        {
            return items;
        }

        if (CallbacksItem() is { } callbacks)
        {
            items.Add(callbacks);
        }

        if (!inCodeView && ContextMenuItem() is { } contextMenu)
        {
            items.Add(contextMenu);
            items.Add(DesignMenuItem.Separator);
        }

        items.Add(new DesignMenuItem(DesignCallbackRules.HelpText, true, () => HelpAsked?.Invoke(this, EventArgs.Empty)));
        return items;
    }

    // The Callbacks submenu over the app's own row or the selection; none for an app whose code cannot be worked on.
    private DesignMenuItem? CallbacksItem()
    {
        DesignDocument document = Document;
        if (document.Code is not { } code)
        {
            return null;
        }

        bool hasFunction = document.Callbacks.Any(static c => !c.IsStartup && !c.IsGenerated);
        if (document.AppSelected)
        {
            string startup = code.Startup?.Name ?? string.Empty;
            return Submenu(DesignCallbackRules.SubMenu(
                [new DesignCallbackGroup(null, [DesignCallbackRules.StartupProperty])], [DesignCallbackRules.StartupProperty], false, _ => startup, _ => false, hasFunction, isApp: true), []);
        }

        IReadOnlyList<DesignComponent> peers = document.MenuComponents;
        if (peers.Count == 0)
        {
            return null;
        }

        DesignComponent first = peers[0];
        IReadOnlyList<string> common = DesignCallbackRules.Common(peers.Select(document.CallbackPropertiesOf).ToArray());
        return Submenu(DesignCallbackRules.SubMenu(
            document.CallbackGroupsOf(first), common, peers.Count > 1, p => document.CallbackOf(first, p) ?? string.Empty, p => document.IsCallbackLocked(first, p), hasFunction, isApp: false), peers);
    }

    private DesignMenuItem? Submenu(IReadOnlyList<DesignCallbackItem> items, IReadOnlyList<DesignComponent> peers) =>
        items.Count == 0 ? null : new DesignMenuItem(DesignCallbackRules.MenuText, true, Items: items.Select(i => MenuItem(i, peers)).ToArray());

    private DesignMenuItem MenuItem(DesignCallbackItem item, IReadOnlyList<DesignComponent> peers) => item.Kind switch
    {
        DesignCallbackItemKind.Group => new DesignMenuItem(item.Text, true, Items: item.Items!.Select(i => MenuItem(i, peers)).ToArray()),
        DesignCallbackItemKind.SelectExisting => new DesignMenuItem(item.Text, !item.Disabled, () => CallbackSelectAsked?.Invoke(this, peers)),
        DesignCallbackItemKind.GoTo => new DesignMenuItem(item.Text, !item.Disabled, () => CallbackGoTo?.Invoke(this, item.Callback!)),
        _ => new DesignMenuItem(item.Text, !item.Disabled, () => AddFromMenu(item.Property!, peers)),
    };

    // The item's click (_ComponentContextMenuInfo's F): a function named for the first of them, by the name's rule for an app's own, for them all.
    private void AddFromMenu(string property, IReadOnlyList<DesignComponent> peers)
    {
        DesignDocument document = Document;
        string? made;
        string? refused;
        if (peers.Count == 0)
        {
            refused = document.AddStartup();
            made = refused is null ? document.Code?.Startup?.Name : null;
        }
        else
        {
            refused = document.MassEditCallback([.. peers], document.DefaultCallbackName(peers[0], property), property, detachPrevious: false, out made);
        }

        if (refused is not null)
        {
            Alerted?.Invoke(this, ("App Designer", refused));
        }
        else if (made is not null)
        {
            CallbackGoTo?.Invoke(this, made);
        }
    }
}
