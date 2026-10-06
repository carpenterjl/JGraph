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

    /// <summary>How many times a script has asked for the menu to open (<c>open(cm, x, y)</c>; U9).</summary>
    [Browsable(false)]
    public int OpenRequests { get; private set; }

    /// <summary>Where the last <c>open</c> asked for it, in pixels from the figure's lower-left corner.</summary>
    [Browsable(false)]
    public (double X, double Y) OpenAt { get; private set; }

    /// <summary>Asks the window to show the menu at a point of its figure.</summary>
    public void RequestOpen(double x, double y)
    {
        OpenAt = (x, y);
        OpenRequests++;
        Invalidate(InvalidationKind.Ui);
    }
}

/// <summary>
/// One entry of a figure's menu bar, of a <see cref="ContextMenuModel"/> or of another entry —
/// MATLAB's <c>uimenu</c>. An entry with items of its own opens them as a submenu; its own
/// selection then does nothing, which is how MATLAB treats a menu that became a folder.
/// </summary>
public sealed class MenuItemModel : GraphObject
{
    private string _text = string.Empty;
    private bool _checked;
    private bool _enable = true;
    private bool _separator;
    private string _accelerator = string.Empty;
    private string _tooltip = string.Empty;
    private Color _foregroundColor = UiComponentModel.DefaultFontColor.ToColor(); // R2025b's 33/255 grey

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
        set => SetProperty(ref _text, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>Whether the entry shows a check mark.</summary>
    [Category("Appearance")]
    public bool Checked
    {
        get => _checked;
        set => SetProperty(ref _checked, value, InvalidationKind.Ui);
    }

    /// <summary>Whether the entry can be picked; a disabled entry is shown greyed.</summary>
    [Category("Behavior")]
    public bool Enable
    {
        get => _enable;
        set => SetProperty(ref _enable, value, InvalidationKind.Ui);
    }

    /// <summary>Whether a dividing line is drawn above this entry.</summary>
    [Category("Appearance")]
    public bool Separator
    {
        get => _separator;
        set => SetProperty(ref _separator, value, InvalidationKind.Ui);
    }

    /// <summary>The keyboard shortcut letter MATLAB documents (stored; menus here are mouse-driven).</summary>
    [Category("Behavior")]
    public string Accelerator
    {
        get => _accelerator;
        set => SetProperty(ref _accelerator, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>Hover text for the entry.</summary>
    [Category("Appearance")]
    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The label's colour.</summary>
    [Category("Appearance"), DisplayName("Foreground color")]
    public Color ForegroundColor
    {
        get => _foregroundColor;
        set => SetProperty(ref _foregroundColor, value, InvalidationKind.Ui);
    }
}
