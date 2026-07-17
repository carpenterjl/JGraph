namespace JGraph.Scripting.Workspace;

/// <summary>The run state of the scripting session.</summary>
public enum ScriptSessionState
{
    /// <summary>No script is running.</summary>
    Idle,

    /// <summary>A script is running.</summary>
    Running,

    /// <summary>A debugged script is paused at a statement.</summary>
    Paused,
}

/// <summary>
/// The UI-free command-state machine of the scripting window: which languages are available, whether a
/// run is in flight, and therefore which of Run/Stop are enabled. One run at a time — the window's
/// commands bind to this so the enablement logic is testable without WPF.
/// </summary>
public sealed class ScriptSessionModel
{
    private readonly HashSet<string> _availableLanguages;

    /// <summary>Creates the session over the languages whose engines are available.</summary>
    public ScriptSessionModel(IEnumerable<string> availableLanguages)
    {
        ArgumentNullException.ThrowIfNull(availableLanguages);
        _availableLanguages = new HashSet<string>(availableLanguages, StringComparer.Ordinal);
    }

    /// <summary>The current run state.</summary>
    public ScriptSessionState State { get; private set; } = ScriptSessionState.Idle;

    /// <summary>The language of the script currently running, or null when idle.</summary>
    public string? RunningLanguage { get; private set; }

    /// <summary>Raised whenever <see cref="State"/> changes (on the caller's thread).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Whether a script in <paramref name="language"/> can start now.</summary>
    public bool CanRun(string? language) =>
        State == ScriptSessionState.Idle && language is not null && _availableLanguages.Contains(language);

    /// <summary>Whether the running script can be stopped.</summary>
    public bool CanStop => State is ScriptSessionState.Running or ScriptSessionState.Paused;

    /// <summary>Whether a running (not yet paused) script can be asked to pause.</summary>
    public bool CanPause => State == ScriptSessionState.Running;

    /// <summary>Whether the paused script can be stepped or continued.</summary>
    public bool CanStep => State == ScriptSessionState.Paused;

    /// <summary>Marks the running script as paused (debugger hit a breakpoint or completed a step).</summary>
    public void MarkPaused()
    {
        if (State != ScriptSessionState.Running)
        {
            return;
        }

        State = ScriptSessionState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the paused script as running again.</summary>
    public void MarkResumed()
    {
        if (State != ScriptSessionState.Paused)
        {
            return;
        }

        State = ScriptSessionState.Running;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Enters the running state; returns false when a run is already in flight
    /// or the language's engine is unavailable.</summary>
    public bool TryBeginRun(string? language)
    {
        if (!CanRun(language))
        {
            return false;
        }

        State = ScriptSessionState.Running;
        RunningLanguage = language;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Returns to idle after a run finishes (success, failure, or cancellation).</summary>
    public void EndRun()
    {
        if (State == ScriptSessionState.Idle)
        {
            return;
        }

        State = ScriptSessionState.Idle;
        RunningLanguage = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
