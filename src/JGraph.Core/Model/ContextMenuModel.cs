using System.ComponentModel;
using JGraph.Core.Drawing;

namespace JGraph.Core.Model;

/// <summary>
/// A right-click menu defined by a script — MATLAB's <c>uicontextmenu</c>. It belongs to a figure
/// and draws nothing itself: objects point at it through their <c>ContextMenu</c> property, and the
/// window shows its items in place of the built-in menu when such an object is right-clicked.
/// </summary>
public sealed class ContextMenuModel : GraphObject
{
    public ContextMenuModel()
    {
        Name = "ContextMenu";
        Items = new GraphObjectCollection<MenuItemModel>(this);
    }

    /// <summary>The menu's entries, in the order they show.</summary>
    public GraphObjectCollection<MenuItemModel> Items { get; }
}

/// <summary>
/// One entry of a <see cref="ContextMenuModel"/> — MATLAB's <c>uimenu</c>. An entry with items of
/// its own opens them as a submenu; its own selection then does nothing, which is how MATLAB
/// treats a menu that became a folder.
/// </summary>
public sealed class MenuItemModel : GraphObject
{
    private string _text = string.Empty;
    private bool _checked;
    private bool _enable = true;
    private bool _separator;
    private string _accelerator = string.Empty;
    private string _tooltip = string.Empty;
    private Color _foregroundColor = Colors.Black;

    public MenuItemModel()
    {
        Name = "Menu";
        Items = new GraphObjectCollection<MenuItemModel>(this);
    }

    /// <summary>Entries nested under this one — shown as a submenu.</summary>
    public GraphObjectCollection<MenuItemModel> Items { get; }

    /// <summary>The label shown in the menu.</summary>
    [Category("General")]
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty, InvalidationKind.None);
    }

    /// <summary>Whether the entry shows a check mark.</summary>
    [Category("Appearance")]
    public bool Checked
    {
        get => _checked;
        set => SetProperty(ref _checked, value, InvalidationKind.None);
    }

    /// <summary>Whether the entry can be picked; a disabled entry is shown greyed.</summary>
    [Category("Behavior")]
    public bool Enable
    {
        get => _enable;
        set => SetProperty(ref _enable, value, InvalidationKind.None);
    }

    /// <summary>Whether a dividing line is drawn above this entry.</summary>
    [Category("Appearance")]
    public bool Separator
    {
        get => _separator;
        set => SetProperty(ref _separator, value, InvalidationKind.None);
    }

    /// <summary>The keyboard shortcut letter MATLAB documents (stored; menus here are mouse-driven).</summary>
    [Category("Behavior")]
    public string Accelerator
    {
        get => _accelerator;
        set => SetProperty(ref _accelerator, value ?? string.Empty, InvalidationKind.None);
    }

    /// <summary>Hover text for the entry.</summary>
    [Category("Appearance")]
    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value ?? string.Empty, InvalidationKind.None);
    }

    /// <summary>The label's colour.</summary>
    [Category("Appearance"), DisplayName("Foreground color")]
    public Color ForegroundColor
    {
        get => _foregroundColor;
        set => SetProperty(ref _foregroundColor, value, InvalidationKind.None);
    }
}
