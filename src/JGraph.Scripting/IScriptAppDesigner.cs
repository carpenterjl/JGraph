namespace JGraph.Scripting;

/// <summary>
/// The host's App Designer window, behind <c>appdesigner</c> (designer plan DS2, decision DD3). The
/// app brings its designer window up; a host without windows (the CLI, a batch run, tests) passes
/// none, and the builtin refuses with the reason. Invoked on the engine's background thread, so a UI
/// host marshals it.
/// </summary>
public interface IScriptAppDesigner
{
    /// <summary>
    /// Brings up the designer's window with the app <paramref name="path"/> open in it, or with what
    /// it has open when <paramref name="path"/> is null, and returns once the app is on its canvas.
    /// </summary>
    /// <param name="path">The full path of an <c>.mlapp</c> that exists, or null.</param>
    /// <returns>Null when it opened, else why the designer could not open it.</returns>
    string? Open(string? path);
}
