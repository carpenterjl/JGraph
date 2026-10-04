namespace JGraph.Scripting;

/// <summary>
/// An opt-in trace of the road a user's action takes to a script callback (app-building plan, U1):
/// the window raising it, the queue taking it, the pump delivering it, the frame coming back. Off
/// unless the environment variable <c>JGRAPH_UI_TRACE</c> names a file, which each line is appended
/// to with the time and the thread. It exists for the window checks, which see a window from outside
/// and cannot otherwise tell which step an action stopped at.
/// </summary>
public static class ScriptUiTrace
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("JGRAPH_UI_TRACE") is { Length: > 0 } path
        ? path
        : null;

    private static readonly object Gate = new();

    /// <summary>Whether tracing is on, so a caller can skip building its message.</summary>
    public static bool Enabled => Path is not null;

    /// <summary>Appends one line, when tracing is on.</summary>
    public static void Write(string message)
    {
        if (Path is null)
        {
            return;
        }

        lock (Gate)
        {
            File.AppendAllText(Path,
                $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
    }
}
