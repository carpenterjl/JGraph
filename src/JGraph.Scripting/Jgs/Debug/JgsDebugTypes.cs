namespace JGraph.Scripting.Jgs.Debug;

/// <summary>A position in a JGS program: which source (file path, or "" for unsaved code) and where.</summary>
/// <param name="SourceId">The source the position belongs to — the path passed to the parser.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column (0 when unknown).</param>
public sealed record JgsDebugLocation(string SourceId, int Line, int Column);

/// <summary>One frame of the paused call stack, top (innermost) first.</summary>
/// <param name="FunctionName">The executing function's name; "(script)" for the top level.</param>
/// <param name="SourceId">The source of <paramref name="Line"/> (the frame's current position).</param>
/// <param name="Line">The 1-based line the frame is currently at.</param>
/// <param name="Column">The 1-based column (0 when unknown).</param>
public sealed record JgsStackFrame(string FunctionName, string SourceId, int Line, int Column);

/// <summary>Why the session paused.</summary>
public enum PauseReason
{
    /// <summary>Execution reached a line with a breakpoint.</summary>
    Breakpoint,

    /// <summary>A step command completed.</summary>
    Step,

    /// <summary>The user asked to pause (break-all).</summary>
    PauseRequest,

    /// <summary>Execution was redirected by set-next-statement and is paused at the new target.</summary>
    EntryJump,

    /// <summary><c>dbstop if error</c> or <c>dbstop if caught error</c>: the statement raised an error (App Designer window plan W11).</summary>
    Error,

    /// <summary><c>dbstop if warning</c>: the statement raised a warning (W11).</summary>
    Warning,

    /// <summary><c>dbstop if naninf</c>: the statement assigned NaN or Inf (W11).</summary>
    NanInf,

    /// <summary>App Designer's Pause on Output: the statement displayed something (W11).</summary>
    Output,

    /// <summary>A breakpoint on a function's <c>end</c> line: the function is about to return (W11).</summary>
    FunctionEnd,
}

/// <summary>
/// Thrown through the paused statement when the debugger is quit (<c>dbquit</c>, Quit Debugging; App Designer window
/// plan W11): the run or callback unwinds as a cancelled one does, and the session reports nothing for it.
/// </summary>
public sealed class JgsDebugQuitException : OperationCanceledException
{
    /// <summary>Creates the exception.</summary>
    public JgsDebugQuitException()
        : base("Debugging was quit.")
    {
    }
}

/// <summary>The outcome of <see cref="JgsDebugSession.TryApplyEdit"/>.</summary>
public sealed class LiveEditResult
{
    private LiveEditResult(bool applied, string? message, JgsDebugLocation? newLocation)
    {
        Applied = applied;
        Message = message;
        NewLocation = newLocation;
    }

    /// <summary>Whether the edit took effect in the paused program.</summary>
    public bool Applied { get; }

    /// <summary>Why the edit is incompatible (null when <see cref="Applied"/>). An incompatible edit
    /// leaves the program untouched — the host typically offers to restart the run instead.</summary>
    public string? Message { get; }

    /// <summary>Where the paused statement now sits in the edited source (null when the pause is in
    /// another file) — the host moves its execution marker here.</summary>
    public JgsDebugLocation? NewLocation { get; }

    internal static LiveEditResult Ok(JgsDebugLocation? newLocation) => new(true, null, newLocation);

    internal static LiveEditResult Incompatible(string message) => new(false, message, null);
}

/// <summary>Payload of <see cref="JgsDebugSession.Paused"/>. Raised on the interpreter thread.</summary>
public sealed class JgsPausedEventArgs : EventArgs
{
    internal JgsPausedEventArgs(JgsDebugLocation location, IReadOnlyList<JgsStackFrame> callStack, PauseReason reason, string? detail = null)
    {
        Location = location;
        CallStack = callStack;
        Reason = reason;
        Detail = detail;
    }

    /// <summary>What the pause is about, for a status line: the error's or warning's text, the variable that holds NaN or Inf; null for a breakpoint or a step.</summary>
    public string? Detail { get; }

    /// <summary>Where execution is paused (the statement about to run).</summary>
    public JgsDebugLocation Location { get; }

    /// <summary>The call stack, innermost frame first.</summary>
    public IReadOnlyList<JgsStackFrame> CallStack { get; }

    /// <summary>Why the pause happened.</summary>
    public PauseReason Reason { get; }
}
