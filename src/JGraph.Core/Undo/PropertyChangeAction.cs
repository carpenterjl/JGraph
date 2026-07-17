using System.Reflection;
using JGraph.Core.Model;

namespace JGraph.Core.Undo;

/// <summary>
/// An undoable edit of a single property on a <see cref="GraphObject"/> (from the property inspector,
/// the plot browser, or programmatic editing). The action is constructed after the new value has been
/// applied; undo/redo swap the old and new values back onto the object via reflection, which raises
/// the object's normal change notifications so every view stays in sync.
/// </summary>
public sealed class PropertyChangeAction : IMergeableAction
{
    private readonly GraphObject _target;
    private readonly PropertyInfo _property;
    private readonly object? _oldValue;
    private object? _newValue;

    /// <param name="target">The object whose property was edited.</param>
    /// <param name="propertyName">The name of a public writable property on <paramref name="target"/>.</param>
    /// <param name="oldValue">The value before the edit.</param>
    /// <param name="newValue">The value after the edit (already applied by the caller).</param>
    public PropertyChangeAction(GraphObject target, string propertyName, object? oldValue, object? newValue)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        _target = target;
        _property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"'{target.GetType().Name}' has no public property '{propertyName}'.", nameof(propertyName));
        if (!_property.CanWrite)
        {
            throw new ArgumentException($"Property '{propertyName}' on '{target.GetType().Name}' is read-only.", nameof(propertyName));
        }

        _oldValue = oldValue;
        _newValue = newValue;
    }

    /// <summary>The object whose property this action edits.</summary>
    public GraphObject Target => _target;

    /// <summary>The name of the edited property.</summary>
    public string PropertyName => _property.Name;

    /// <inheritdoc />
    public string Description => $"Change {_property.Name}";

    /// <inheritdoc />
    public void Redo() => _property.SetValue(_target, _newValue);

    /// <inheritdoc />
    public void Undo() => _property.SetValue(_target, _oldValue);

    /// <summary>
    /// Merges a subsequent edit of the same property on the same object into this action, keeping this
    /// action's old value and adopting the newer new value.
    /// </summary>
    public bool TryMerge(IUndoableAction next)
    {
        if (next is not PropertyChangeAction other
            || !ReferenceEquals(other._target, _target)
            || other._property.Name != _property.Name)
        {
            return false;
        }

        _newValue = other._newValue;
        return true;
    }
}
