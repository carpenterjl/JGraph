namespace JGraph.Scripting;

/// <summary>
/// Thrown by the <c>exit</c>/<c>quit</c> builtins to unwind a script that asked to stop. It is not a
/// failure: engines catch it and return <see cref="ScriptRunResult.Exited"/>, so the host learns the
/// code the script wants the process to end with. Deliberately not derived from any language's error
/// type — a script's own <c>try</c> must not be able to swallow "stop now".
/// </summary>
public sealed class ScriptExitException : Exception
{
    /// <summary>Creates the request with the process exit code the script asked for.</summary>
    public ScriptExitException(int exitCode)
        : base($"The script called exit({exitCode}).") =>
        ExitCode = exitCode;

    /// <summary>The exit code the script passed to <c>exit</c>, or 0 when it passed none.</summary>
    public int ExitCode { get; }

    /// <summary>
    /// Finds the exit request inside <paramref name="exception"/>, which may have been wrapped by a
    /// hosted runtime (pythonnet wraps .NET exceptions before rethrowing them), or null when the
    /// exception is an ordinary failure.
    /// </summary>
    public static ScriptExitException? Unwrap(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ScriptExitException exit)
            {
                return exit;
            }
        }

        return null;
    }
}
