using System.Collections.ObjectModel;
using System.ComponentModel;
using JGraph.Controls.Internal;
using JGraph.Core.Model;
using JGraph.Objects.Annotations;

namespace JGraph.Controls.Browser;

/// <summary>
/// One node of the plot browser tree, wrapping a <see cref="GraphObject"/>. Nodes expose a computed
/// header, a visibility toggle (committed undoably through the owning control), and two-way selection
/// that the control keeps in sync with the shared <see cref="Interaction.SelectionManager"/>.
/// </summary>
public sealed class GraphNodeViewModel : ViewModelBase, IDisposable
{
    private readonly PlotBrowserControl _owner;
    private bool _isExpanded = true;
    private bool _isSelected;

    internal GraphNodeViewModel(GraphObject model, PlotBrowserControl owner)
    {
        Model = model;
        _owner = owner;
        Model.PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>The wrapped model object.</summary>
    public GraphObject Model { get; }

    /// <summary>Child nodes mirroring the model tree.</summary>
    public ObservableCollection<GraphNodeViewModel> Children { get; } = new();

    /// <summary>The caption shown in the tree.</summary>
    public string Header => Model switch
    {
        FigureModel figure => WithDetail("Figure", figure.Title),
        AxesModel axes => WithDetail("Axes", axes.Title),
        AxisModel axis => WithDetail(axis.IsHorizontal ? "X axis" : "Y axis", axis.Label),
        GridModel => "Grid",
        LegendModel => "Legend",
        TextAnnotation text => WithDetail("Text", Truncate(text.Text)),
        AnnotationObject annotation => annotation.Name,
        PlotObject plot => WithDetail(plot.Name, plot.DisplayName),
        _ => Model.Name,
    };

    /// <summary>Whether the visibility check box is shown (hidden for the figure root and axis models).</summary>
    public bool ShowVisibilityToggle => Model is not FigureModel and not AxisModel;

    /// <summary>The model's <see cref="GraphObject.Visible"/> flag; setting commits an undoable edit.</summary>
    public bool IsVisibleChecked
    {
        get => Model.Visible;
        set
        {
            if (Model.Visible != value)
            {
                _owner.CommitVisibleChange(Model, value);
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>Tree selection state, kept in sync with the shared selection manager by the control.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                _owner.OnNodeSelectionChanged(this, value);
            }
        }
    }

    /// <summary>Whether the Delete command applies to this node (annotations and plots only).</summary>
    public bool CanDelete => Model is AnnotationObject or PlotObject;

    /// <inheritdoc />
    public void Dispose()
    {
        Model.PropertyChanged -= OnModelPropertyChanged;
        foreach (GraphNodeViewModel child in Children)
        {
            child.Dispose();
        }

        Children.Clear();
    }

    /// <summary>Finds the node wrapping <paramref name="model"/> in this subtree, or null.</summary>
    public GraphNodeViewModel? FindNode(GraphObject model)
    {
        if (ReferenceEquals(Model, model))
        {
            return this;
        }

        foreach (GraphNodeViewModel child in Children)
        {
            if (child.FindNode(model) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Refreshes <see cref="IsSelected"/> from outside without echoing back to the manager.</summary>
    internal void SetSelectedSilently(bool value)
    {
        if (SetField(ref _isSelected, value, nameof(IsSelected)) && value)
        {
            ExpandAncestors();
        }
    }

    private void ExpandAncestors()
    {
        // Walk the tree from the root; parents of this node must be expanded for it to be shown.
        _owner.ExpandPathTo(this);
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GraphObject.Name):
            case nameof(PlotObject.DisplayName):
            case nameof(AxesModel.Title):
            case nameof(AxisModel.Label):
            case nameof(TextAnnotation.Text):
                OnPropertyChanged(nameof(Header));
                break;
            case nameof(GraphObject.Visible):
                OnPropertyChanged(nameof(IsVisibleChecked));
                break;
        }
    }

    private static string WithDetail(string kind, string? detail) =>
        string.IsNullOrEmpty(detail) ? kind : $"{kind} — {detail}";

    private static string Truncate(string text) =>
        text.Length <= 24 ? text : text[..21] + "…";
}
