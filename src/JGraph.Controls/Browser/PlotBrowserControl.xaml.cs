using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using JGraph.Core.Model;
using JGraph.Core.Undo;
using JGraph.Interaction;

namespace JGraph.Controls.Browser;

/// <summary>
/// A tree view of the figure's object hierarchy (figure → axes → axis/grid/legend/plots/annotations).
/// It stays in sync with the model through the bubbling <c>Invalidated</c> event (rebuilding on
/// structural changes), offers per-object visibility toggles (undoable), and shares its selection
/// with the edit mode and property inspector through a <see cref="SelectionManager"/>.
/// </summary>
public partial class PlotBrowserControl : UserControl
{
    /// <summary>Identifies the <see cref="Figure"/> dependency property.</summary>
    public static readonly DependencyProperty FigureProperty = DependencyProperty.Register(
        nameof(Figure),
        typeof(FigureModel),
        typeof(PlotBrowserControl),
        new PropertyMetadata(null, OnFigureChanged));

    private readonly ObservableCollection<GraphNodeViewModel> _roots = new();
    private SelectionManager? _selection;
    private bool _syncingSelection;
    private bool _rebuildQueued;

    public PlotBrowserControl()
    {
        InitializeComponent();
        Tree.ItemsSource = _roots;
    }

    /// <summary>The figure whose object tree is shown.</summary>
    public FigureModel? Figure
    {
        get => (FigureModel?)GetValue(FigureProperty);
        set => SetValue(FigureProperty, value);
    }

    /// <summary>The undo stack visibility toggles and annotation deletions are recorded on.</summary>
    public UndoStack? UndoStack { get; set; }

    /// <summary>The shared selection manager (typically <c>FigureControl.Selection</c>).</summary>
    public SelectionManager? Selection
    {
        get => _selection;
        set
        {
            if (_selection is not null)
            {
                _selection.SelectionChanged -= OnManagerSelectionChanged;
            }

            _selection = value;
            if (_selection is not null)
            {
                _selection.SelectionChanged += OnManagerSelectionChanged;
                OnManagerSelectionChanged(this, _selection.Selected);
            }
        }
    }

    /// <summary>Applies a visibility toggle as an undoable property edit.</summary>
    internal void CommitVisibleChange(GraphObject model, bool visible)
    {
        bool oldValue = model.Visible;
        model.Visible = visible;
        UndoStack?.Push(new PropertyChangeAction(model, nameof(GraphObject.Visible), oldValue, visible));
    }

    /// <summary>Routes a user selection in the tree to the shared selection manager.</summary>
    internal void OnNodeSelectionChanged(GraphNodeViewModel node, bool isSelected)
    {
        if (_syncingSelection || _selection is null)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            if (isSelected)
            {
                _selection.Select(node.Model);
            }
            else if (ReferenceEquals(_selection.Selected, node.Model))
            {
                _selection.Clear();
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Expands every ancestor of a node so it becomes visible in the tree.</summary>
    internal void ExpandPathTo(GraphNodeViewModel target)
    {
        foreach (GraphNodeViewModel root in _roots)
        {
            ExpandPath(root, target);
        }
    }

    private static bool ExpandPath(GraphNodeViewModel node, GraphNodeViewModel target)
    {
        if (ReferenceEquals(node, target))
        {
            return true;
        }

        foreach (GraphNodeViewModel child in node.Children)
        {
            if (ExpandPath(child, target))
            {
                node.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private static void OnFigureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PlotBrowserControl)d;
        if (e.OldValue is FigureModel oldFigure)
        {
            oldFigure.Invalidated -= control.OnFigureInvalidated;
        }

        if (e.NewValue is FigureModel newFigure)
        {
            newFigure.Invalidated += control.OnFigureInvalidated;
        }

        control.Rebuild();
    }

    private void OnFigureInvalidated(object? sender, InvalidatedEventArgs e)
    {
        if (e.Kind != InvalidationKind.Structure || _rebuildQueued)
        {
            return;
        }

        // Coalesce bursts of structural changes into one rebuild after the current dispatcher pass.
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        foreach (GraphNodeViewModel root in _roots)
        {
            root.Dispose();
        }

        _roots.Clear();

        if (Figure is not { } figure)
        {
            return;
        }

        _roots.Add(BuildFigureNode(figure));

        // Restore the highlight for whatever is currently selected.
        if (_selection?.Selected is { } selected)
        {
            OnManagerSelectionChanged(this, selected);
        }
    }

    private GraphNodeViewModel BuildFigureNode(FigureModel figure)
    {
        var figureNode = new GraphNodeViewModel(figure, this);
        foreach (AxesModel axes in figure.Axes)
        {
            var axesNode = new GraphNodeViewModel(axes, this);
            foreach (AxisModel axis in axes.XAxes)
            {
                axesNode.Children.Add(new GraphNodeViewModel(axis, this));
            }

            foreach (AxisModel axis in axes.YAxes)
            {
                axesNode.Children.Add(new GraphNodeViewModel(axis, this));
            }

            axesNode.Children.Add(new GraphNodeViewModel(axes.Grid, this));

            var legendNode = new GraphNodeViewModel(axes.Legend, this);
            foreach (LegendEntryModel entry in axes.Legend.Entries)
            {
                legendNode.Children.Add(new GraphNodeViewModel(entry, this));
            }

            axesNode.Children.Add(legendNode);
            axesNode.Children.Add(new GraphNodeViewModel(axes.Colorbar, this));

            foreach (PlotObject plot in axes.Plots)
            {
                axesNode.Children.Add(new GraphNodeViewModel(plot, this));
            }

            foreach (AnnotationObject annotation in axes.Annotations)
            {
                axesNode.Children.Add(new GraphNodeViewModel(annotation, this));
            }

            figureNode.Children.Add(axesNode);
        }

        foreach (AnnotationObject annotation in figure.Annotations)
        {
            figureNode.Children.Add(new GraphNodeViewModel(annotation, this));
        }

        return figureNode;
    }

    private void OnManagerSelectionChanged(object? sender, GraphObject? selected)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            foreach (GraphNodeViewModel root in _roots)
            {
                SyncSelection(root, selected);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static void SyncSelection(GraphNodeViewModel node, GraphObject? selected)
    {
        node.SetSelectedSilently(ReferenceEquals(node.Model, selected));
        foreach (GraphNodeViewModel child in node.Children)
        {
            SyncSelection(child, selected);
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Tree.SelectedItem is not GraphNodeViewModel node)
        {
            e.Handled = true;   // nothing selected: no menu
            return;
        }

        TreeContextMenu.Items.Clear();

        foreach (ElementMenuItem item in BuildAddItems(node.Model))
        {
            TreeContextMenu.Items.Add(CreateMenuItem(item));
        }

        if (TreeContextMenu.Items.Count > 0 && node.CanDelete)
        {
            TreeContextMenu.Items.Add(new Separator());
        }

        if (node.CanDelete)
        {
            var delete = new MenuItem { Header = "Delete" };
            delete.Click += OnDeleteClick;
            TreeContextMenu.Items.Add(delete);
        }

        if (TreeContextMenu.Items.Count == 0)
        {
            e.Handled = true;
        }
    }

    private void OnAddButtonClick(object sender, RoutedEventArgs e)
    {
        GraphObject? target = (Tree.SelectedItem as GraphNodeViewModel)?.Model ?? Figure;
        if (target is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (ElementMenuItem item in BuildAddItems(target))
        {
            menu.Items.Add(CreateMenuItem(item));
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Nothing to add here", IsEnabled = false });
        }

        menu.IsOpen = true;
    }

    /// <summary>The applicable add-menu for a model object, or an empty list when it offers nothing.</summary>
    private IReadOnlyList<ElementMenuItem> BuildAddItems(GraphObject model)
    {
        if (Figure is not { } figure)
        {
            return Array.Empty<ElementMenuItem>();
        }

        return ElementMenuBuilder.Build(model, figure, UndoStack, Confirm);
    }

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    /// <summary>Turns a descriptor (and any children) into a WPF menu item.</summary>
    private static MenuItem CreateMenuItem(ElementMenuItem descriptor)
    {
        var item = new MenuItem
        {
            Header = descriptor.Header,
            IsEnabled = descriptor.Enabled,
            ToolTip = descriptor.Tooltip,
        };

        // Without this, a disabled item's tooltip — the reason it is unavailable — would not show.
        ToolTipService.SetShowOnDisabled(item, descriptor.Tooltip is not null);

        if (descriptor.Children is { Count: > 0 } children)
        {
            foreach (ElementMenuItem child in children)
            {
                item.Items.Add(CreateMenuItem(child));
            }
        }
        else if (descriptor.Invoke is { } invoke)
        {
            item.Click += (_, _) => invoke();
        }

        return item;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is not GraphNodeViewModel node)
        {
            return;
        }

        switch (node.Model)
        {
            case AnnotationObject annotation:
                DeleteAnnotation(annotation);
                break;

            case PlotObject plot when plot.Axes is { } axes:
                // Per the framework's undo policy, plot creation/removal is not undoable — confirm.
                MessageBoxResult result = MessageBox.Show(
                    $"Delete plot '{node.Header}'? This cannot be undone.",
                    "Delete plot",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.OK)
                {
                    ClearSelectionIf(plot);
                    axes.Plots.Remove(plot);
                }

                break;
        }
    }

    private void DeleteAnnotation(AnnotationObject annotation)
    {
        GraphObjectCollection<AnnotationObject>? collection = annotation.Parent switch
        {
            AxesModel axes => axes.Annotations,
            FigureModel figure => figure.Annotations,
            _ => null,
        };

        if (collection is null)
        {
            return;
        }

        int index = collection.IndexOf(annotation);
        if (index < 0)
        {
            return;
        }

        ClearSelectionIf(annotation);
        collection.RemoveAt(index);
        UndoStack?.Push(new RemoveAnnotationAction(collection, annotation, index));
    }

    private void ClearSelectionIf(GraphObject model)
    {
        if (ReferenceEquals(_selection?.Selected, model))
        {
            _selection.Clear();
        }
    }
}
