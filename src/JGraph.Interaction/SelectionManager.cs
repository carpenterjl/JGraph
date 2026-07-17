using JGraph.Core.Model;

namespace JGraph.Interaction;

/// <summary>
/// Tracks the single selected object of a figure and keeps the objects'
/// <see cref="GraphObject.IsSelected"/> flags in sync. Selection is shared between the edit
/// interaction mode, the plot browser, and the property inspector, so all three views agree.
/// (Multi-select is deferred to a later milestone.)
/// </summary>
public sealed class SelectionManager
{
    private GraphObject? _selected;

    /// <summary>Raised when the selected object changes (null = nothing selected).</summary>
    public event EventHandler<GraphObject?>? SelectionChanged;

    /// <summary>The selected object, or null when nothing is selected.</summary>
    public GraphObject? Selected => _selected;

    /// <summary>Selects an object (or clears the selection when null).</summary>
    public void Select(GraphObject? obj)
    {
        if (ReferenceEquals(_selected, obj))
        {
            return;
        }

        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        _selected = obj;
        if (obj is not null)
        {
            obj.IsSelected = true;
        }

        SelectionChanged?.Invoke(this, obj);
    }

    /// <summary>Clears the selection.</summary>
    public void Clear() => Select(null);
}
