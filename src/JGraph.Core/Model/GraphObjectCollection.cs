using System.Collections.ObjectModel;

namespace JGraph.Core.Model;

/// <summary>
/// An observable collection of <see cref="GraphObject"/> children owned by a parent object. It keeps
/// each child's <see cref="GraphObject.Parent"/> link in sync and raises a
/// <see cref="InvalidationKind.Structure"/> invalidation on the owner whenever the set of children
/// changes, so the figure root is notified that the scene must be rebuilt.
/// </summary>
/// <typeparam name="T">The child object type.</typeparam>
public sealed class GraphObjectCollection<T> : ObservableCollection<T>
    where T : GraphObject
{
    private readonly GraphObject _owner;

    public GraphObjectCollection(GraphObject owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    protected override void InsertItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetParent(_owner);
        base.InsertItem(index, item);
        _owner.Invalidate(InvalidationKind.Structure);
    }

    protected override void SetItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this[index].SetParent(null);
        item.SetParent(_owner);
        base.SetItem(index, item);
        _owner.Invalidate(InvalidationKind.Structure);
    }

    protected override void RemoveItem(int index)
    {
        this[index].SetParent(null);
        base.RemoveItem(index);
        _owner.Invalidate(InvalidationKind.Structure);
    }

    protected override void ClearItems()
    {
        foreach (T item in this)
        {
            item.SetParent(null);
        }

        base.ClearItems();
        _owner.Invalidate(InvalidationKind.Structure);
    }

    /// <summary>Adds several children in one call.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (T item in items)
        {
            Add(item);
        }
    }

    /// <summary>Returns the children ordered by <see cref="GraphObject.ZOrder"/> for drawing.</summary>
    public IEnumerable<T> InDrawOrder() => this.OrderBy(static c => c.ZOrder);
}
