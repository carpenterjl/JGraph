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

    /// <summary>
    /// Brings up the "App Designer Custom UI Component Metadata" dialog for the custom UI component class
    /// <paramref name="fullFileName"/> (App Designer window plan W15), which writes the <c>resources/appDesigner.json</c>
    /// beside it, and returns once the dialog is up. A host without the dialog refuses.
    /// </summary>
    /// <param name="fullFileName">The full path of a validated component class, <c>.m</c> or <c>.mlapp</c>.</param>
    /// <returns>Null when the dialog opened, else why not.</returns>
    string? ConfigureMetadata(string fullFileName) => "This host has no App Designer Custom UI Component Metadata dialog.";
}
