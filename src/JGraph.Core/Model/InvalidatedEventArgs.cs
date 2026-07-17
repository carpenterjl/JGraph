namespace JGraph.Core.Model;

/// <summary>
/// Raised when a <see cref="GraphObject"/> (or one of its descendants) changes in a way that
/// affects layout or rendering. The <see cref="Source"/> is the object that originated the change,
/// preserved as the event bubbles up the object tree to the figure root.
/// </summary>
public sealed class InvalidatedEventArgs : EventArgs
{
    public InvalidatedEventArgs(GraphObject source, InvalidationKind kind)
    {
        Source = source;
        Kind = kind;
    }

    public GraphObject Source { get; }

    public InvalidationKind Kind { get; }
}
