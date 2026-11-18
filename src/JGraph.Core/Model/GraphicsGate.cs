namespace JGraph.Core.Model;

/// <summary>
/// Keeps the display from reading graphics objects while a script is changing them. A script thread
/// holds the gate for as long as it runs and opens it only where it waits for the display (a
/// <c>drawnow</c>, a figure being shown, a <c>pause</c>, a <c>uiwait</c>), which is when R2025b updates
/// its windows too. The display enters only when no script holds the gate; when one does, it skips
/// the read and asks to be called back once the scripts open it again.
/// </summary>
/// <remarks>
/// Without this a figure shown mid-run (a <c>drawnow</c> right after <c>figure</c>) was painted on the
/// UI thread while the script thread went on adding to it, and the paint met "Collection was
/// modified" (open item 361; FigureRenderer, FigureModel.RecomputeDataBounds, the plot browser).
/// This is the process's one gate; <see cref="GraphicsGateState"/> holds the rules.
/// </remarks>
public static class GraphicsGate
{
    /// <summary>The process's gate.</summary>
    public static GraphicsGateState Shared { get; } = new();

    /// <inheritdoc cref="GraphicsGateState.ScriptsBusy"/>
    public static bool ScriptsBusy => Shared.ScriptsBusy;

    /// <inheritdoc cref="GraphicsGateState.EnterScript"/>
    public static GraphicsGateState.ScriptScope EnterScript() => Shared.EnterScript();

    /// <inheritdoc cref="GraphicsGateState.Park"/>
    public static GraphicsGateState.ParkScope Park() => Shared.Park();

    /// <inheritdoc cref="GraphicsGateState.TryEnterDisplay"/>
    public static bool TryEnterDisplay() => Shared.TryEnterDisplay();

    /// <inheritdoc cref="GraphicsGateState.ExitDisplay"/>
    public static void ExitDisplay() => Shared.ExitDisplay();

    /// <inheritdoc cref="GraphicsGateState.WhenFree"/>
    public static void WhenFree(Action action) => Shared.WhenFree(action);
}

/// <summary>
/// The rules of a graphics gate (see <see cref="GraphicsGate"/>). Script threads do not exclude each
/// other: the gate only orders scripts against the display, so a second script (an idle drain, a
/// deployed component) is never made to wait for a first. The display never waits for a script, so a
/// script blocked on the UI thread while holding the gate cannot deadlock it; a script taking the
/// gate back after a park waits only for a display read already under way.
/// </summary>
public sealed class GraphicsGateState
{
    private readonly object _sync = new();

    /// <summary>How deeply each thread has entered as a script (nested entries on one thread count once).</summary>
    private readonly ThreadLocal<int> _depth = new();

    /// <summary>How deeply each thread has parked; the gate opens at the outermost park only.</summary>
    private readonly ThreadLocal<int> _parked = new();

    /// <summary>Script threads running and not parked.</summary>
    private int _busy;

    /// <summary>Display reads under way.</summary>
    private int _displaying;

    /// <summary>What to run once no script holds the gate.</summary>
    private List<Action>? _whenFree;

    /// <summary>Whether any script holds the gate now.</summary>
    public bool ScriptsBusy
    {
        get
        {
            lock (_sync)
            {
                return _busy > 0;
            }
        }
    }

    /// <summary>
    /// Enters as a script for the life of the returned scope: the thread a script runs on calls this
    /// around the script's work. Nested entries on one thread are free.
    /// </summary>
    public ScriptScope EnterScript()
    {
        if (_depth.Value++ == 0 && _parked.Value == 0)
        {
            Acquire();
        }

        return new ScriptScope(this);
    }

    /// <summary>
    /// Opens the gate while this script thread waits for the display, and takes it back when the
    /// scope ends. On a thread that is not running a script it does nothing.
    /// </summary>
    public ParkScope Park()
    {
        if (_depth.Value == 0)
        {
            return default;
        }

        if (_parked.Value++ == 0)
        {
            Release();
        }

        return new ParkScope(this);
    }

    /// <summary>
    /// Enters to read graphics objects for display. Returns false while a script holds the gate; the
    /// caller then skips the read and arranges to be called back with <see cref="WhenFree"/>. A true
    /// answer must be paired with <see cref="ExitDisplay"/>. A script may always read what it is
    /// changing on its own thread.
    /// </summary>
    public bool TryEnterDisplay()
    {
        int own = _depth.Value > 0 && _parked.Value == 0 ? 1 : 0;
        lock (_sync)
        {
            if (_busy - own > 0)
            {
                return false;
            }

            _displaying++;
            return true;
        }
    }

    /// <summary>Ends a display read begun by a true <see cref="TryEnterDisplay"/>.</summary>
    public void ExitDisplay()
    {
        lock (_sync)
        {
            if (--_displaying == 0)
            {
                Monitor.PulseAll(_sync);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> once no script holds the gate: now, when none does, or else on
    /// the thread of the script that opens it (at a park or at its end). Keep it short: post to the
    /// display's own thread rather than doing the work there.
    /// </summary>
    public void WhenFree(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_sync)
        {
            if (_busy > 0)
            {
                (_whenFree ??= []).Add(action);
                return;
            }
        }

        action();
    }

    private void Acquire()
    {
        lock (_sync)
        {
            // A display read already under way finishes before the script goes on changing what it reads.
            while (_displaying > 0)
            {
                Monitor.Wait(_sync);
            }

            _busy++;
        }
    }

    private void Release()
    {
        List<Action>? run = null;
        lock (_sync)
        {
            if (--_busy == 0 && _whenFree is not null)
            {
                run = _whenFree;
                _whenFree = null;
            }
        }

        if (run is null)
        {
            return;
        }

        foreach (Action action in run)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // A display that went away (a closed window's dispatcher) is not the script's fault.
            }
        }
    }

    private void EndScript()
    {
        if (--_depth.Value == 0 && _parked.Value == 0)
        {
            Release();
        }
    }

    private void EndPark()
    {
        if (--_parked.Value == 0 && _depth.Value > 0)
        {
            Acquire();
        }
    }

    /// <summary>A script's hold on the gate; disposing it ends the hold.</summary>
    public readonly struct ScriptScope : IDisposable
    {
        private readonly GraphicsGateState? _gate;

        internal ScriptScope(GraphicsGateState gate) => _gate = gate;

        /// <inheritdoc />
        public void Dispose() => _gate?.EndScript();
    }

    /// <summary>A script's park; disposing it takes the gate back.</summary>
    public readonly struct ParkScope : IDisposable
    {
        private readonly GraphicsGateState? _gate;

        internal ParkScope(GraphicsGateState gate) => _gate = gate;

        /// <inheritdoc />
        public void Dispose() => _gate?.EndPark();
    }
}
