using System.Windows.Input;

namespace JGraph.Application.Scripting;

/// <summary>
/// Every action the scripting workspace offers, declared once. The menu bar, the toolbar and the
/// keyboard all bind to these, so a shortcut cannot drift from the menu item that advertises it, and
/// enablement is decided in one <c>CanExecute</c> handler rather than by assigning
/// <c>IsEnabled</c> on individual buttons.
/// </summary>
public static class WorkspaceCommands
{
    /// <summary>Creates a blank script. The command parameter is the language name, or "Text".</summary>
    public static RoutedUICommand NewScript { get; } = Create("New Script", Key.N, ModifierKeys.Control);

    /// <summary>Opens a script file in a new tab.</summary>
    public static RoutedUICommand OpenFile { get; } = Create("Open File…", Key.O, ModifierKeys.Control);

    /// <summary>Opens a folder as the workspace root.</summary>
    public static RoutedUICommand OpenWorkspace { get; } =
        Create("Open Workspace…", Key.O, ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>Saves the active script, prompting for a path when it has none.</summary>
    public static RoutedUICommand Save { get; } = Create("Save", Key.S, ModifierKeys.Control);

    /// <summary>Saves the active script to a path the user picks, re-homing the document there.</summary>
    public static RoutedUICommand SaveAs { get; } = Create("Save As…", Key.S, ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>Closes the active script tab.</summary>
    public static RoutedUICommand CloseTab { get; } = Create("Close Tab", Key.W, ModifierKeys.Control);

    /// <summary>Closes JGraph.</summary>
    public static RoutedUICommand Exit { get; } = Create("Exit");

    /// <summary>Runs the active script, or continues a paused one.</summary>
    public static RoutedUICommand RunOrContinue { get; } = Create("Run", Key.F5, ModifierKeys.None);

    /// <summary>Stops the running script.</summary>
    public static RoutedUICommand Stop { get; } = Create("Stop", Key.F5, ModifierKeys.Shift);

    /// <summary>Pauses the running script at its next statement.</summary>
    public static RoutedUICommand Pause { get; } = Create("Pause");

    /// <summary>Executes the paused statement, running calls to completion.</summary>
    public static RoutedUICommand StepOver { get; } = Create("Step Over", Key.F10, ModifierKeys.None);

    /// <summary>Executes the paused statement, entering functions.</summary>
    public static RoutedUICommand StepIn { get; } = Create("Step In", Key.F11, ModifierKeys.None);

    /// <summary>Runs until the current function returns.</summary>
    public static RoutedUICommand StepOut { get; } = Create("Step Out", Key.F11, ModifierKeys.Shift);

    /// <summary>Toggles a breakpoint on the line the caret is on.</summary>
    public static RoutedUICommand ToggleBreakpoint { get; } = Create("Toggle Breakpoint", Key.F9, ModifierKeys.None);

    /// <summary>Shows a tool pane. The command parameter is its content id.</summary>
    /// <summary>Empties every live console session — variables, their buffers, and the figure registry.</summary>
    public static RoutedUICommand ClearWorkspace { get; } = Create("Clear Workspace");

    public static RoutedUICommand ShowPane { get; } = Create("Show Panel");

    /// <summary>Puts every tool pane back where it belongs.</summary>
    public static RoutedUICommand ResetLayout { get; } = Create("Reset Layout");

    /// <summary>Opens an empty figure window.</summary>
    public static RoutedUICommand NewFigureWindow { get; } = Create("New Figure Window");

    /// <summary>Opens the Options dialog.</summary>
    public static RoutedUICommand Options { get; } = Create("Options…");

    /// <summary>Opens the scripting guide.</summary>
    public static RoutedUICommand ScriptingGuide { get; } = Create("Scripting Guide", Key.F1, ModifierKeys.None);

    /// <summary>Opens the bug-report dialog.</summary>
    public static RoutedUICommand ReportBug { get; } = Create("Report a Bug…");

    private static RoutedUICommand Create(string text) =>
        new(text, text.Replace("…", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal),
            typeof(WorkspaceCommands));

    private static RoutedUICommand Create(string text, Key key, ModifierKeys modifiers)
    {
        RoutedUICommand command = Create(text);
        command.InputGestures.Add(new KeyGesture(key, modifiers));
        return command;
    }
}
