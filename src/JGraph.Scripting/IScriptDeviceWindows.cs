namespace JGraph.Scripting;

/// <summary>
/// The host's device panes, behind <c>serialExplorer</c> (device classes plan, stage 14, ADR 0197).
/// The app shows its Serial Explorer pane; a host without windows (the CLI, a batch run, tests) passes
/// none, and the builtin refuses with the reason. Invoked on the engine's background thread, so a UI
/// host marshals it.
/// </summary>
public interface IScriptDeviceWindows
{
    /// <summary>Shows the Serial Explorer pane and returns without waiting for it.</summary>
    void ShowSerialExplorer();
}
