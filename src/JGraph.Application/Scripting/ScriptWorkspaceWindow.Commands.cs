using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Scripting;
using JGraph.Scripting.Startup;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// Binds <see cref="WorkspaceCommands"/> to the window. Every action has exactly one implementation
/// and one enablement rule here, whether it is reached from the menu bar, the toolbar or the
/// keyboard.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private void BindCommands()
    {
        Bind(WorkspaceCommands.NewScript, (_, e) => OpenNewScript(e.Parameter as string ?? DefaultNewScriptLanguage()));
        Bind(WorkspaceCommands.OpenFile, (_, _) => PromptOpenFile());
        Bind(WorkspaceCommands.OpenWorkspace, (_, _) => PromptOpenWorkspace());
        Bind(WorkspaceCommands.Save, (_, _) => SaveActive(), CanSave);
        Bind(WorkspaceCommands.SaveAs, (_, _) => SaveAsActive(), CanSave);
        Bind(WorkspaceCommands.CloseTab, (_, _) => ActiveDocument?.Document.Close(), CanCloseTab);
        Bind(WorkspaceCommands.Exit, (_, _) => Close());

        Bind(WorkspaceCommands.RunOrContinue, (_, _) => RunOrContinue(), CanRun);
        Bind(WorkspaceCommands.Stop, (_, _) => _cts?.Cancel(), (_, e) => e.CanExecute = _session.CanStop);
        Bind(WorkspaceCommands.Pause, (_, _) => _debugSession?.Pause(),
            (_, e) => e.CanExecute = _session.CanPause && _debugSession is not null);
        Bind(WorkspaceCommands.StepOver, (_, _) => StepCommand(static s => s.StepOver()), CanStep);
        Bind(WorkspaceCommands.StepIn, (_, _) => StepCommand(static s => s.StepIn()), CanStep);
        Bind(WorkspaceCommands.StepOut, (_, _) => StepCommand(static s => s.StepOut()), CanStep);
        Bind(WorkspaceCommands.ToggleBreakpoint, (_, _) => ActiveDocument?.Editor.ToggleBreakpointAtCaret(),
            (_, e) => e.CanExecute = ActiveDocument is not null);

        Bind(WorkspaceCommands.ClearWorkspace, (_, _) => ClearWorkspaceSessions(),
            (_, e) => e.CanExecute = _session.State == ScriptSessionState.Idle);

        Bind(WorkspaceCommands.ShowPane, (_, e) => ShowPane((string)e.Parameter));
        Bind(WorkspaceCommands.ResetLayout, (_, _) => ResetLayout());
        Bind(WorkspaceCommands.NewFigureWindow, (_, _) => OpenBlankFigure());
        Bind(WorkspaceCommands.Options, (_, _) => _options?.ShowOptions(),
            (_, e) => e.CanExecute = _options is not null);
        Bind(WorkspaceCommands.ScriptingGuide, (_, _) => OpenScriptingGuide());

        // A MenuItem loses the focused element as its command target the moment the menu opens, so
        // the standard editing commands would arrive with nowhere to go. Forward them explicitly.
        foreach (RoutedUICommand editing in new[]
                 {
                     ApplicationCommands.Undo, ApplicationCommands.Redo, ApplicationCommands.Cut,
                     ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.SelectAll,
                     ApplicationCommands.Find,
                 })
        {
            RoutedUICommand command = editing;
            CommandBindings.Add(new CommandBinding(
                command,
                (_, e) =>
                {
                    if (ActiveDocument?.Editor is { } editor && command.CanExecute(null, editor))
                    {
                        command.Execute(null, editor);
                        e.Handled = true;
                    }
                },
                (_, e) => e.CanExecute = ActiveDocument?.Editor is { } editor && command.CanExecute(null, editor)));
        }
    }

    private void Bind(RoutedUICommand command, ExecutedRoutedEventHandler execute, CanExecuteRoutedEventHandler? canExecute = null) =>
        CommandBindings.Add(canExecute is null
            ? new CommandBinding(command, execute)
            : new CommandBinding(command, execute, canExecute));

    private void CanRun(object sender, CanExecuteRoutedEventArgs e)
    {
        // A "Text" tab (or any language without an engine) is viewable but not runnable.
        bool runnable = ActiveDocument is not { } active || _engines.ContainsKey(active.Model.Language);
        e.CanExecute = runnable && _session.State is ScriptSessionState.Idle or ScriptSessionState.Paused;
    }

    private void CanStep(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session.CanStep;

    private void CanSave(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ActiveDocument is not null;

    private void CanCloseTab(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ActiveDocument is not null;

    /// <summary>
    /// Fills the View menu from the pane catalog, so a pane added later appears here automatically.
    /// </summary>
    private void BuildViewMenu()
    {
        foreach (PaneDescriptor pane in PaneCatalog.For(this))
        {
            ViewMenu.Items.Add(new MenuItem
            {
                Header = pane.Title,
                Command = WorkspaceCommands.ShowPane,
                CommandParameter = pane.ContentId,
            });
        }

        ViewMenu.Items.Add(new Separator());
        ViewMenu.Items.Add(new MenuItem
        {
            Header = "Reset Layout",
            Command = WorkspaceCommands.ResetLayout,
            ToolTip = "Put every panel back where it belongs",
        });
    }

    /// <summary>Shows every tool pane in its default place, for a layout the user has lost track of.</summary>
    private void ResetLayout()
    {
        foreach (PaneDescriptor pane in PaneCatalog.For(this))
        {
            ShowPane(pane.ContentId);
        }

        SetStatus("Every panel is showing. Drag them where you want them; the arrangement is saved on exit.");
    }

    private void OpenBlankFigure() =>
        SetStatus($"Opened Figure {_figureWindows.OpenBlankFigure()} — plot into it with figure(n) from a script.");

    private void OpenScriptingGuide()
    {
        if (StartupHelp.FindGuide(AppContext.BaseDirectory) is not { } guide)
        {
            SetStatus("The scripting guide was not found beside the application.");
            return;
        }

        try
        {
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(guide) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open the scripting guide: {ex.Message}");
        }
    }

    private void UpdateCommandStates()
    {
        // Enablement lives in the CanExecute handlers; this only asks WPF to re-ask them, plus the
        // one thing a command cannot express: Run's label doubles as the paused-state indicator.
        CommandManager.InvalidateRequerySuggested();
        RunButton.Content = _session.State == ScriptSessionState.Paused ? "▶ Continue (F5)" : "▶ Run (F5)";
    }
}
