using System.Windows;
using System.Windows.Controls;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's component browser (designer plan DS3): the app's components as a tree, in
/// the order App Designer's browser lists them, each by its code name. Selecting one selects it on
/// the canvas - the way to the components the canvas does not draw (menus, a context menu, tree
/// nodes, toolbar tools) - and the canvas's selection is shown here.
/// </summary>
public sealed class DesignBrowserView : UserControl
{
    private readonly TreeView _tree = new() { BorderThickness = new Thickness(0) };
    private readonly Dictionary<DesignComponent, TreeViewItem> _items = new(ReferenceEqualityComparer.Instance);
    private DesignDocument? _document;
    private bool _showing;
    private string _shape = string.Empty;

    public DesignBrowserView()
    {
        _tree.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        _tree.SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_tree, "ComponentBrowser");
        _tree.SelectedItemChanged += (_, _) => Picked();
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F2 && _tree.SelectedItem is TreeViewItem { Tag: DesignComponent component })
            {
                RenameRequested?.Invoke(this, component);
                e.Handled = true;
            }
        };
        // A right-click selects the node under the mouse before its menu opens, as App Designer's trees do.
        _tree.PreviewMouseRightButtonDown += (_, e) =>
        {
            for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            {
                if (node is TreeViewItem item)
                {
                    item.IsSelected = true;
                    item.Focus();
                    break;
                }
            }
        };
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename...", InputGestureText = "F2" };
        rename.Click += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem { Tag: DesignComponent component })
            {
                RenameRequested?.Invoke(this, component);
            }
        };
        menu.Items.Add(rename);
        _tree.ContextMenu = menu;
        Content = _tree;
    }

    /// <summary>Raised for Rename (F2, the menu) on a component: the window asks for the name and shows what changes (DS5).</summary>
    public event EventHandler<DesignComponent>? RenameRequested;

    /// <summary>The app shown.</summary>
    public DesignDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document))
            {
                return;
            }

            if (_document is not null)
            {
                _document.Changed -= OnChanged;
            }

            _document = value;
            if (value is not null)
            {
                value.Changed += OnChanged;
            }

            _shape = string.Empty;
            Show();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Show();

    /// <summary>The tree again when the components changed, and the selection always.</summary>
    private void Show()
    {
        _showing = true;
        try
        {
            if (_document is not { } document)
            {
                _tree.Items.Clear();
                _items.Clear();
                return;
            }

            string shape = string.Join("|", document.Design.Components.Select(static c => c.CodeName + "@" + c.Parent?.CodeName));
            if (shape != _shape)
            {
                _shape = shape;
                _tree.Items.Clear();
                _items.Clear();
                _tree.Items.Add(Item(document.Design, document.Design.Figure));
            }

            if (document.Selection is [var one] && _items.TryGetValue(one, out TreeViewItem? item))
            {
                for (ItemsControl? up = ItemsControl.ItemsControlFromItemContainer(item); up is TreeViewItem parent; up = ItemsControl.ItemsControlFromItemContainer(parent))
                {
                    parent.IsExpanded = true;
                }

                item.IsSelected = true;
                item.BringIntoView();
            }
            else if (_tree.SelectedItem is TreeViewItem selected)
            {
                selected.IsSelected = false;
            }
        }
        finally
        {
            _showing = false;
        }
    }

    private TreeViewItem Item(AppDesign design, DesignComponent component)
    {
        var item = new TreeViewItem
        {
            Header = component.CodeName,
            Tag = component,
            IsExpanded = true,
            ToolTip = component.Type,
        };
        System.Windows.Automation.AutomationProperties.SetName(item, component.CodeName);
        _items[component] = item;
        foreach (DesignComponent child in DesignCodeWriter.BrowserOrder(design, component))
        {
            // A responsive app's grid is not listed: its regions are, under the figure (DS4b).
            if (_document?.IsResponsiveGrid(child) == true)
            {
                foreach (DesignComponent region in DesignCodeWriter.BrowserOrder(design, child))
                {
                    item.Items.Add(Item(design, region));
                }

                continue;
            }

            item.Items.Add(Item(design, child));
        }

        return item;
    }

    /// <summary>A component picked here is selected on the canvas; a tab is shown in its group.</summary>
    private void Picked()
    {
        if (_showing || _document is not { } document || _tree.SelectedItem is not TreeViewItem { Tag: DesignComponent component })
        {
            return;
        }

        if (component.Type == "matlab.ui.container.Tab")
        {
            document.ShowTab(component);
        }

        document.Select([component]);
    }
}
