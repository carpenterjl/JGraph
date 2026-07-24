using JGraph.Core.Model;

namespace JGraph.Core.Undo;

/// <summary>
/// An undoable reordering of a legend row. Constructed after the row has already been moved; undo
/// moves it back, redo moves it forward again. Reordering is a plain list move, so both directions
/// are a single <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/>.
/// </summary>
public sealed class MoveLegendEntryAction : IUndoableAction
{
    private readonly LegendModel _legend;
    private readonly int _from;
    private readonly int _to;

    public MoveLegendEntryAction(LegendModel legend, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(legend);
        _legend = legend;
        _from = from;
        _to = to;
    }

    /// <inheritdoc />
    public string Description => "Reorder legend entry";

    /// <inheritdoc />
    public void Redo() => _legend.Entries.Move(_from, _to);

    /// <inheritdoc />
    public void Undo() => _legend.Entries.Move(_to, _from);
}
