using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JGraph.Core.Model;

/// <summary>
/// The abstract base of every object in a figure. Provides identity, common editable properties
/// (visibility, z-order, selectability, tag, user data), change notification via
/// <see cref="INotifyPropertyChanged"/>, and a bubbling <see cref="Invalidated"/> event that lets a
/// figure root observe changes anywhere in its subtree.
/// </summary>
public abstract class GraphObject : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _tag;
    private object? _userData;
    private bool _visible = true;
    private int _zOrder;
    private bool _selectable = true;
    private bool _isSelected;

    /// <summary>A stable identity for this object, useful for serialization and selection tracking.</summary>
    [Browsable(false)]
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The parent object in the figure tree, or null if this is a root or detached.</summary>
    [Browsable(false)]
    public GraphObject? Parent { get; private set; }

    /// <summary>A human-readable name shown in the plot browser and used by the object-oriented API.</summary>
    [Category("General")]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty, InvalidationKind.None);
    }

    /// <summary>An arbitrary user-assigned label (MATLAB "Tag"), not interpreted by the framework.</summary>
    [Browsable(false)]
    public string? Tag
    {
        get => _tag;
        set => SetProperty(ref _tag, value, InvalidationKind.None);
    }

    /// <summary>Arbitrary user-attached state (MATLAB "UserData"), not interpreted by the framework.</summary>
    [Browsable(false)]
    public object? UserData
    {
        get => _userData;
        set => SetProperty(ref _userData, value, InvalidationKind.None);
    }

    /// <summary>Whether this object is drawn. Hidden objects still participate in the tree.</summary>
    [Category("General")]
    public bool Visible
    {
        get => _visible;
        set => SetProperty(ref _visible, value, InvalidationKind.Render);
    }

    /// <summary>Relative draw order among siblings; higher values are drawn on top.</summary>
    [Category("General"), DisplayName("Z order")]
    public int ZOrder
    {
        get => _zOrder;
        set => SetProperty(ref _zOrder, value, InvalidationKind.Render);
    }

    /// <summary>Whether this object may be selected via hit-testing in the figure window.</summary>
    [Category("Behavior")]
    public bool Selectable
    {
        get => _selectable;
        set => SetProperty(ref _selectable, value, InvalidationKind.None);
    }

    /// <summary>Whether this object is currently selected.</summary>
    [Browsable(false)]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when this object or any descendant changes. Bubbles up the tree so a figure root can
    /// subscribe once and observe its entire subtree.
    /// </summary>
    public event EventHandler<InvalidatedEventArgs>? Invalidated;

    /// <summary>Assigns the parent link. Called by container collections; not part of the public API.</summary>
    internal void SetParent(GraphObject? parent) => Parent = parent;

    /// <summary>
    /// Raises <see cref="Invalidated"/> for this object and propagates it toward the root, preserving
    /// the original <paramref name="kind"/> and source.
    /// </summary>
    public void Invalidate(InvalidationKind kind)
    {
        if (kind == InvalidationKind.None)
        {
            return;
        }

        RaiseInvalidated(new InvalidatedEventArgs(this, kind));
    }

    /// <summary>
    /// Sets a backing field, and if the value changed raises <see cref="PropertyChanged"/> and (when
    /// <paramref name="kind"/> is not <see cref="InvalidationKind.None"/>) an invalidation.
    /// </summary>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        InvalidationKind kind,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        Invalidate(kind);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Override to react to invalidations before they bubble further up the tree.</summary>
    protected virtual void OnInvalidated(InvalidatedEventArgs args)
    {
    }

    private void RaiseInvalidated(InvalidatedEventArgs args)
    {
        OnInvalidated(args);
        Invalidated?.Invoke(this, args);
        Parent?.RaiseInvalidated(args);
    }
}
