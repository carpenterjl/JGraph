namespace JGraph.Scripting;

/// <summary>
/// The seam <c>drawnow</c> flushes rendering through. A windowed host installs a flusher that
/// blocks until its display has actually painted (in WPF, an empty dispatcher call at render
/// priority); a one-shot run or a headless batch installs nothing, and <c>drawnow</c> keeps its
/// old contract there — JGraph draws as it goes, so with no window there is nothing to wait for.
/// </summary>
public static class ScriptRenderPump
{
    private static Action? _flusher;

    /// <summary>Installs the flusher for a windowed host; pass null when the host goes away.</summary>
    public static void SetFlusher(Action? flusher) => _flusher = flusher;

    /// <summary>Blocks until pending rendering has been shown, when a host can say so.</summary>
    public static void Flush()
    {
        if (_flusher is not { } flusher)
        {
            return;
        }

        // The display paints while the script waits here, so the script lets go of the graphics gate
        // for the wait (open item 361).
        using (JGraph.Core.Model.GraphicsGate.Park())
        {
            flusher();
        }
    }
}
