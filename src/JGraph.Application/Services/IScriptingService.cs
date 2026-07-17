namespace JGraph.Application.Services;

/// <summary>
/// UI-facing scripting for the figure window: opens the script editor. Implementations own the
/// editor window and the script engines so the view model stays free of WPF and engine types.
/// Figures a script displays via <c>show()</c> open in their own numbered figure windows through
/// <see cref="IFigureWindowService"/> — the main window is not involved.
/// </summary>
public interface IScriptingService
{
    /// <summary>Opens (or re-focuses) the script editor.</summary>
    void OpenEditor();
}
