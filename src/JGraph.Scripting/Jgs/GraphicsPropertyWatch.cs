using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The properties of one graphics object that <c>PostSet</c> listeners watch, with the value each had
/// when last told (open item 56). A write through <c>set</c> or the dot tells its listeners itself;
/// everything else that changes a property — <c>xlim</c>, <c>axis</c>, the window's zoom and pan — is
/// noticed by the object's <see cref="GraphObject.Invalidated"/>, which reaches it from anything below
/// it too: on the script thread the watched values are read again at once and a changed one is told,
/// and from the window's thread a check is queued for the script thread's next drain point.
/// </summary>
/// <remarks>
/// R2025b raises <c>PostSet</c> inside the write, for a write of the same value too. A change noticed
/// by the watch is told only when the value differs from the last one told, and one made from the
/// window waits for a drain point (<c>drawnow</c>, <c>pause</c>, an idle session) — recorded divergences.
/// </remarks>
internal sealed class GraphicsPropertyWatch
{
    [ThreadStatic]
    private static int t_quiet;

    private readonly JgsHandleEntry _entry;
    private readonly int _scriptThread;
    private readonly Dictionary<string, JgsValue?> _last = new(StringComparer.OrdinalIgnoreCase);
    private bool _checking;

    private GraphicsPropertyWatch(JgsHandleEntry entry)
    {
        _entry = entry;
        _scriptThread = Environment.CurrentManagedThreadId;
        entry.Target.Invalidated += OnInvalidated;
    }

    /// <summary>The object's watch, made on first use.</summary>
    public static GraphicsPropertyWatch For(JgsHandleEntry entry) => entry.Watch ??= new GraphicsPropertyWatch(entry);

    /// <summary>Starts watching <paramref name="properties"/> from their values now.</summary>
    public void Watch(IEnumerable<string> properties)
    {
        foreach (string property in properties)
        {
            if (!_last.ContainsKey(property))
            {
                _last[property] = Read(property);
            }
        }
    }

    /// <summary>
    /// Keeps the watch from reading anything while a write through <c>set</c> or the dot is under way;
    /// that write tells its own listeners and then asks the watch about the rest.
    /// </summary>
    public static IDisposable Quiet()
    {
        t_quiet++;
        return new QuietScope();
    }

    /// <summary>Takes a property's value now as the one last told — after a write told it itself.</summary>
    public void Refresh(string property)
    {
        if (_last.ContainsKey(property))
        {
            _last[property] = Read(property);
        }
    }

    /// <summary>Reads every watched property again and tells the listeners of each one that changed.</summary>
    public void Check()
    {
        if (_checking || _entry.Target.BeingDeleted)
        {
            return;
        }

        _checking = true;
        try
        {
            foreach (string property in _last.Keys.ToArray())
            {
                JgsValue? now = Read(property);
                if (now is null || (_last[property] is { } before && JgsBuiltins.IsEqualValues(before, now)))
                {
                    continue;
                }

                _last[property] = now;
                JgsBuiltins.FireGraphicsPropertyEvent(_entry, property, post: true);
            }
        }
        finally
        {
            _checking = false;
        }
    }

    private void OnInvalidated(object? sender, InvalidatedEventArgs args)
    {
        if (_checking || _last.Count == 0)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _scriptThread)
        {
            ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.PropertyWatch, _entry.Target), coalesce: true);
            return;
        }

        if (t_quiet == 0)
        {
            Check();
        }
    }

    private JgsValue? Read(string property)
    {
        try
        {
            return JgsGraphicsProperties.Get(_entry, property, 0, 0);
        }
        catch (JgsException)
        {
            return null;
        }
    }

    private sealed class QuietScope : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                t_quiet--;
            }
        }
    }
}
