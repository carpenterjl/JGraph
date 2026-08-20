namespace JGraph.Core.Model;

/// <summary>
/// Where the model announces that an object is leaving the figure tree for good. The scripting
/// layer listens so a MATLAB <c>DeleteFcn</c> can run <em>before</em> the object is destroyed —
/// while it is still parented and its children still reachable, which is when MATLAB runs it.
/// <para>
/// The announcement is made by whoever performs the removal: the container collections announce
/// their removals themselves, and the few deletions that are not a collection removal — closing a
/// figure, hiding a legend for good — announce explicitly. A removal that is really a <em>move</em>
/// (reparenting takes an object out of one collection to put it in another) wraps itself in
/// <see cref="SuppressNotifications"/>, because nothing is being deleted.
/// </para>
/// </summary>
public static class GraphObjectLifecycle
{
    [ThreadStatic]
    private static int _suppressed;

    /// <summary>Raised once per object, as its deletion begins and before it happens. The object is
    /// still in the tree. Raised on whatever thread performs the deletion.</summary>
    public static event Action<GraphObject>? Deleting;

    /// <summary>
    /// Marks the start of an object's deletion and raises <see cref="Deleting"/> — once. The
    /// <see cref="GraphObject.BeingDeleted"/> flag is the guard: a deletion that finds it already
    /// set (a child of a subtree already being torn down, a second <c>delete(h)</c>) does nothing,
    /// which is what keeps a DeleteFcn that itself deletes things from echoing forever.
    /// </summary>
    public static void NotifyDeleting(GraphObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_suppressed > 0 || !TryBeginDeleting(target))
        {
            return;
        }

        Deleting?.Invoke(target);
    }

    /// <summary>Sets the object's <see cref="GraphObject.BeingDeleted"/> flag, answering whether
    /// this call was the one that set it. Listeners walking a subtree use this to visit each
    /// descendant exactly once, whatever order the collections empty in afterwards.</summary>
    public static bool TryBeginDeleting(GraphObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.BeingDeleted)
        {
            return false;
        }

        target.BeingDeleted = true;
        return true;
    }

    /// <summary>Clears the deletion mark — an undo putting an object back is the one road back.</summary>
    internal static void Revive(GraphObject target) => target.BeingDeleted = false;

    /// <summary>Silences <see cref="Deleting"/> on this thread for the returned scope — for
    /// removals that are moves, not deletions.</summary>
    public static IDisposable SuppressNotifications()
    {
        _suppressed++;
        return new Suppression();
    }

    private sealed class Suppression : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _suppressed--;
            }
        }
    }
}
