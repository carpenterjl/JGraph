using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs.Debug;

namespace JGraph.Application.Scripting;

/// <summary>
/// The debugger in the Code View (App Designer window plan W11, decision WD4): breakpoints set in the
/// code view's margin or with Set/Clear (F12), kept in the MATLAB session's store the shell's editors
/// and <c>dbstop</c> share; the RUN section's Pause, Continue, Step (F10), Step In (F11), Step Out
/// (Shift+F11) and Stop (Shift+F5) over the session's own debugger; the BREAKPOINTS group's Clear All,
/// Set/Clear, Enable/Disable and Set Condition; the ERROR HANDLING group's Pause on Errors, Warnings,
/// NaN or Inf and Output; the paused line shown in the app's code view, which switches to the Code View
/// and locks while paused, as R2025b's does; the app's variables for the editor's datatips (the shell's
/// Workspace pane shows them too). While paused in one of its apps the designer is debugging: Run,
/// Share, the insertions and the view switch rest, and Save quits debugging first, as R2025b's
/// <c>SaveAction</c> does.
/// </summary>
/// <remarks>
/// The debugger counts the lines of the app's file - the class text the interpreter runs (W0: breakpoints
/// in an <c>.mlapp</c> count the generated class text's lines) - and the store holds those. The code view
/// shows the class as the designer writes it again around the user's code, which is the file's text line
/// for line once the app is saved by this designer and may differ before that (an app R2025b or a hand
/// wrote): <see cref="DesignDebug.MapLines"/> maps the two, from W14's line comparison, and the margin,
/// the paused marker and the caret's commands go through it. A save writes the file again, so the
/// breakpoints are carried across it by their place in their functions (<see cref="DesignDebug.Anchor"/>).
/// </remarks>
partial class AppDesignerWindow
{
    /// <summary>Pauses the running app's code at its next line (RUN > Pause).</summary>
    public static readonly RoutedCommand PauseRun = new(nameof(PauseRun), typeof(AppDesignerWindow));

    /// <summary>Continues the paused code (RUN > Continue).</summary>
    public static readonly RoutedCommand ContinueRun = new(nameof(ContinueRun), typeof(AppDesignerWindow));

    /// <summary>Runs the paused line (RUN > Step, F10).</summary>
    public static readonly RoutedCommand StepOverLine = new(nameof(StepOverLine), typeof(AppDesignerWindow));

    /// <summary>Runs the paused line and stops in the function it calls (RUN > Step In, F11).</summary>
    public static readonly RoutedCommand StepIntoLine = new(nameof(StepIntoLine), typeof(AppDesignerWindow));

    /// <summary>Runs until the current function returns (RUN > Step Out, Shift+F11).</summary>
    public static readonly RoutedCommand StepOutOfFunction = new(nameof(StepOutOfFunction), typeof(AppDesignerWindow));

    /// <summary>Quits debugging, or stops the app's run (RUN > Stop, Shift+F5).</summary>
    public static readonly RoutedCommand StopDebugging = new(nameof(StopDebugging), typeof(AppDesignerWindow));

    /// <summary>Sets or clears the breakpoint on the caret's line (BREAKPOINTS > Set/Clear, F12).</summary>
    public static readonly RoutedCommand ToggleBreakpoint = new(nameof(ToggleBreakpoint), typeof(AppDesignerWindow));

    /// <summary>Clears every breakpoint in every file (BREAKPOINTS > Clear All).</summary>
    public static readonly RoutedCommand ClearAllBreakpoints = new(nameof(ClearAllBreakpoints), typeof(AppDesignerWindow));

    /// <summary>Enables or disables the breakpoint on the caret's line (BREAKPOINTS > Enable/Disable).</summary>
    public static readonly RoutedCommand ToggleBreakpointEnabled = new(nameof(ToggleBreakpointEnabled), typeof(AppDesignerWindow));

    /// <summary>Sets or changes the condition of the breakpoint on the caret's line (BREAKPOINTS > Set Condition).</summary>
    public static readonly RoutedCommand SetBreakpointCondition = new(nameof(SetBreakpointCondition), typeof(AppDesignerWindow));

    private JgsDebugSession? _debugger;
    private bool _debugging;
    private AppTab? _pausedApp;

    /// <summary>Each open app's file text and the time it was read, so that the line map is built once per write.</summary>
    private readonly Dictionary<AppTab, (DateTime WrittenUtc, string Text)> _fileTexts = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The MATLAB session's own debugger, whose breakpoint store the code views show and set, and whose pauses
    /// the designer follows when they fall in one of its apps. Null without a session: the margin then sets nothing.
    /// </summary>
    public JgsDebugSession? Debugger
    {
        get => _debugger;
        set
        {
            if (ReferenceEquals(_debugger, value))
            {
                return;
            }

            if (_debugger is { } old)
            {
                old.Paused -= OnDebuggerPaused;
                old.Resumed -= OnDebuggerResumed;
                if (old.Breakpoints is { } oldStore)
                {
                    oldStore.Changed -= OnBreakpointStoreChanged;
                }
            }

            _debugger = value;
            if (value is not null)
            {
                value.Paused += OnDebuggerPaused;
                value.Resumed += OnDebuggerResumed;
                if (value.Breakpoints is { } store)
                {
                    store.Changed += OnBreakpointStoreChanged;
                }
            }

            RefreshBreakpointMargins();
            ShowToolstripState();
        }
    }

    /// <summary>Stops the app's run in the session while its code runs (the shell's Stop); null without one.</summary>
    public Action? StopRunner { get; set; }

    /// <summary>Whether the debugger is paused in one of this designer's apps (R2025b's <c>IsDebugging</c>).</summary>
    public bool IsDebugging => _debugging;

    private JgsBreakpointStore? Breakpoints => _debugger?.Breakpoints;

    private void BindDebugCommands()
    {
        CommandBindings.Add(new CommandBinding(PauseRun, (_, _) => _debugger?.Pause(), (_, e) => e.CanExecute = _running && _debugger is not null && !_debugging));
        CommandBindings.Add(new CommandBinding(ContinueRun, (_, _) => _debugger?.Continue(), (_, e) => e.CanExecute = _debugging));
        CommandBindings.Add(new CommandBinding(StepOverLine, (_, _) => _debugger?.StepOver(), (_, e) => e.CanExecute = _debugging));
        CommandBindings.Add(new CommandBinding(StepIntoLine, (_, _) => _debugger?.StepIn(), (_, e) => e.CanExecute = _debugging));
        CommandBindings.Add(new CommandBinding(StepOutOfFunction, (_, _) => _debugger?.StepOut(), (_, e) => e.CanExecute = _debugging));
        CommandBindings.Add(new CommandBinding(StopDebugging, (_, _) => StopOrQuit(), (_, e) => e.CanExecute = _debugging || _running));
        CommandBindings.Add(new CommandBinding(ToggleBreakpoint, (_, _) => ToggleBreakpointAtCaret(), (_, e) => e.CanExecute = Active is { InCodeView: true, Document.Path: not null } && Breakpoints is not null));
        CommandBindings.Add(new CommandBinding(ClearAllBreakpoints, (_, _) => Breakpoints?.ClearAll(), (_, e) => e.CanExecute = Breakpoints is { IsEmpty: false }));
        CommandBindings.Add(new CommandBinding(ToggleBreakpointEnabled, (_, _) => ToggleBreakpointEnabledAtCaret(), (_, e) => e.CanExecute = BreakpointAtCaret() is not null));
        CommandBindings.Add(new CommandBinding(SetBreakpointCondition, (_, _) => PromptBreakpointCondition(), (_, e) => e.CanExecute = Active is { InCodeView: true, Document.Path: not null } && Breakpoints is not null));
        InputBindings.Add(new KeyBinding(StepOverLine, Key.F10, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(StepIntoLine, Key.F11, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(StepOutOfFunction, Key.F11, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(StopDebugging, Key.F5, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(ToggleBreakpoint, Key.F12, ModifierKeys.None));
    }

    /// <summary>Hooks an app's code view to the breakpoint store: margin clicks set and clear, a rewrite of the class is shown through the line map, datatips read the paused frame.</summary>
    private void WireDebug(AppTab app)
    {
        app.CodeView.Editor.BreakpointsChanged += (_, _) => OnMarginToggled(app);
        app.CodeView.TextReplaced += (_, _) => RefreshBreakpointMargin(app);
        app.CodeView.Editor.DatatipProvider = name => PausedDatatip(app, name);
        RefreshBreakpointMargin(app);
    }

    // --- the file's lines and the code view's -----------------------------------------------------------

    /// <summary>The class text the app's file holds now - what the debugger counts - read again after each write; null without a file or when it cannot be read.</summary>
    private string? FileTextOf(AppTab app)
    {
        if (app.Document.Path is not { } path)
        {
            return null;
        }

        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (_fileTexts.TryGetValue(app, out (DateTime WrittenUtc, string Text) cached) && cached.WrittenUtc == written)
        {
            return cached.Text;
        }

        string? text = DesignDebug.FileText(path);
        if (text is not null)
        {
            _fileTexts[app] = (written, text);
        }

        return text;
    }

    /// <summary>The map between the app's file lines and its code view's lines, as they stand now.</summary>
    private DesignLineMap MapOf(AppTab app)
    {
        string? file = FileTextOf(app);
        return file is null ? DesignLineMap.Identity : DesignDebug.MapLines(file, app.CodeView.Editor.ScriptText);
    }

    /// <summary>After the file was written again: the breakpoints follow their functions from the text before to the text after (<see cref="DesignDebug"/>).</summary>
    private void ReanchorAfterSave(AppTab app, string? fileBefore)
    {
        if (Breakpoints is not { } store || app.Document.Path is not { } path)
        {
            return;
        }

        IReadOnlyList<JgsBreakpoint> held = store.In(path);
        string? fileAfter = FileTextOf(app);
        if (held.Count > 0 && fileBefore is not null && fileAfter is not null && !string.Equals(fileBefore, fileAfter, StringComparison.Ordinal))
        {
            IReadOnlyList<JgsBreakpoint> resolved = DesignDebug.Resolve(fileAfter, path, DesignDebug.Anchor(fileBefore, held));
            if (!resolved.SequenceEqual(held))
            {
                store.Replace(path, resolved);
                int moved = resolved.Count(r => held.All(h => h.Line != r.Line));
                int dropped = held.Count - resolved.Count;
                _status.Text += moved > 0 || dropped > 0
                    ? $" {(moved > 0 ? $"{moved} breakpoint{(moved == 1 ? string.Empty : "s")} moved with the code" : string.Empty)}{(moved > 0 && dropped > 0 ? "," : string.Empty)}{(dropped > 0 ? $" {dropped} dropped: the code it was on is gone" : string.Empty)}."
                    : string.Empty;
                return;
            }
        }

        RefreshBreakpointMargin(app);
    }

    /// <summary>The margin toggled a line: the difference between what it shows and what the store holds is the click, snapped to a statement as R2025b snaps it, in the file's lines.</summary>
    private void OnMarginToggled(AppTab app)
    {
        if (Breakpoints is not { } store)
        {
            return;
        }

        if (app.Document.Path is not { } path)
        {
            _status.Text = "Save the app before setting breakpoints in it.";
            app.CodeView.Editor.SetBreakpoints([]);
            return;
        }

        DesignLineMap map = MapOf(app);
        var shown = new HashSet<int>(app.CodeView.Editor.Breakpoints);
        var held = new HashSet<int>(store.In(path).Select(b => map.ToView(b.Line) ?? -1).Where(static l => l > 0));
        foreach (int added in shown.Except(held).ToList())
        {
            if (DesignDebug.Snap(app.CodeView.Editor.ScriptText, added) is not int landed)
            {
                _status.Text = "Setting or clearing breakpoints past the start of the last expression is not supported.";
            }
            else if (map.ToFile(landed) is int fileLine)
            {
                store.Set(path, fileLine);
            }
            else
            {
                _status.Text = "Save the app before setting a breakpoint on a line the file does not have yet.";
            }
        }

        foreach (int removed in held.Except(shown).ToList())
        {
            if (map.ToFile(removed) is int fileLine)
            {
                store.Clear(path, fileLine);
            }
        }

        RefreshBreakpointMargin(app); // the margin shows the store, snapped
    }

    private void OnBreakpointStoreChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        RefreshBreakpointMargins();
        ShowToolstripState();
    });

    private void RefreshBreakpointMargins()
    {
        foreach (AppTab app in Apps)
        {
            RefreshBreakpointMargin(app);
        }
    }

    private void RefreshBreakpointMargin(AppTab app)
    {
        if (Breakpoints is not { } store || app.Document.Path is not { } path)
        {
            app.CodeView.Editor.SetBreakpoints([]);
            return;
        }

        DesignLineMap map = MapOf(app);
        var shown = new List<(int Line, JgsBreakpoint Breakpoint)>();
        foreach (JgsBreakpoint breakpoint in store.In(path))
        {
            if (map.ToView(breakpoint.Line) is int line)
            {
                shown.Add((line, breakpoint));
            }
        }

        app.CodeView.Editor.SetBreakpoints(shown.Select(static s => s.Line));
        app.CodeView.Editor.SetBreakpointStates(shown.Where(static s => !s.Breakpoint.Enabled).Select(static s => s.Line), shown.Where(static s => s.Breakpoint.Condition.Length > 0).Select(static s => s.Line));
    }

    /// <summary>The breakpoint on the caret's line, by the file's line it stands for, or null.</summary>
    private JgsBreakpoint? BreakpointAtCaret()
    {
        if (Active is not { InCodeView: true, Document.Path: { } path } app || Breakpoints is not { } store)
        {
            return null;
        }

        return MapOf(app).ToFile(app.CodeView.Editor.CaretLine) is int fileLine ? store.Find(path, fileLine) : null;
    }

    /// <summary>The file's line a breakpoint asked for at the caret lands on, the status line saying why there is none.</summary>
    private int? FileLineAtCaret(AppTab app)
    {
        int caret = app.CodeView.Editor.CaretLine;
        if (DesignDebug.Snap(app.CodeView.Editor.ScriptText, caret) is not int landed)
        {
            _status.Text = "Setting or clearing breakpoints past the start of the last expression is not supported.";
            return null;
        }

        if (MapOf(app).ToFile(landed) is not int fileLine)
        {
            _status.Text = "Save the app before setting a breakpoint on a line the file does not have yet.";
            return null;
        }

        return fileLine;
    }

    private void ToggleBreakpointAtCaret()
    {
        if (Active is not { InCodeView: true, Document.Path: { } path } app || Breakpoints is not { } store)
        {
            return;
        }

        if (BreakpointAtCaret() is { } existing)
        {
            store.Clear(path, existing.Line);
            return;
        }

        if (FileLineAtCaret(app) is int fileLine)
        {
            store.Toggle(path, fileLine);
        }
    }

    private void ToggleBreakpointEnabledAtCaret()
    {
        if (BreakpointAtCaret() is { } breakpoint && Breakpoints is { } store)
        {
            store.SetEnabled(breakpoint.File, breakpoint.Line, !breakpoint.Enabled);
        }
    }

    /// <summary>Set Condition: JGraph's dialog for the expression (R2025b's is the rich text editor's own), over the breakpoint at the caret, set first where there is none.</summary>
    private void PromptBreakpointCondition()
    {
        if (Active is not { InCodeView: true, Document.Path: { } path } app || Breakpoints is not { } store || FileLineAtCaret(app) is not int fileLine)
        {
            return;
        }

        JgsBreakpoint? existing = store.Find(path, fileLine);
        var dialog = new BreakpointConditionDialog(existing?.Condition ?? string.Empty) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            store.Set(path, fileLine, dialog.Condition);
            if (existing is { Enabled: false })
            {
                store.SetEnabled(path, fileLine, false);
            }
        }
    }

    private void StopOrQuit()
    {
        if (_debugging)
        {
            _debugger?.Quit();
        }
        else if (_running)
        {
            StopRunner?.Invoke();
        }
    }

    // --- the pause ----------------------------------------------------------------------------------------

    private void OnDebuggerPaused(object? sender, JgsPausedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        AppTab? app = Apps.FirstOrDefault(a => a.Document.Path is { } path && string.Equals(path, e.Location.SourceId, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            // Paused somewhere else: this designer is not debugging (R2025b's IsDebugging follows the app's file in the stack).
            return;
        }

        _debugging = true;
        _pausedApp = app;
        _tabs.SelectedItem = app.Item;
        SetView(app, code: true);
        app.CodeView.Locked = true;
        int? viewLine = MapOf(app).ToView(e.Location.Line);
        app.CodeView.Editor.SetCurrentLine(viewLine);
        string function = e.CallStack.Count > 0 ? e.CallStack[0].FunctionName : app.Document.Code?.ClassName ?? app.FileName;
        string where = viewLine is null ? $"at the file's line {e.Location.Line}, which the code view does not show until the app is saved" : $"at line {viewLine}";
        _status.Text = e.Reason switch
        {
            PauseReason.Error => $"Paused in {function} {where}: {FirstLine(e.Detail)}",
            PauseReason.Warning => $"Paused in {function} {where} on a warning: {FirstLine(e.Detail)}",
            PauseReason.NanInf => $"Paused in {function} {where}: {e.Detail}.",
            PauseReason.Output => $"Paused in {function} {where} on output.",
            _ => $"Paused in {function} {where}.",
        };
        ShowToolstripState();
        CommandManager.InvalidateRequerySuggested();
    });

    private void OnDebuggerResumed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!_debugging)
        {
            return;
        }

        _debugging = false;
        if (_pausedApp is { } app)
        {
            app.CodeView.Editor.SetCurrentLine(null);
            app.CodeView.Locked = false;
            if (ReferenceEquals(Active, app))
            {
                _status.Text = _running ? $"Running {app.FileName}…" : "Continuing.";
            }
        }

        _pausedApp = null;
        ShowToolstripState();
        CommandManager.InvalidateRequerySuggested();
    });

    private static string FirstLine(string? text) =>
        text is null ? string.Empty : text.Split('\n').Select(static l => l.Trim()).FirstOrDefault(static l => l.Length > 0) ?? string.Empty;

    /// <summary>The datatip for a name hovered in a paused app's code view: the paused frame's variable, its class and a glance at its value.</summary>
    private string? PausedDatatip(AppTab app, string name)
    {
        if (!_debugging || !ReferenceEquals(app, _pausedApp) || _debugger is not { IsPaused: true } debugger)
        {
            return null;
        }

        IReadOnlyList<ScriptVariable> variables;
        try
        {
            variables = debugger.GetVariables();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        ScriptVariable? variable = variables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        if (variable is null)
        {
            return null;
        }

        string[] lines = variable.DisplayValue.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        string value = lines.Length <= 8 ? variable.DisplayValue : string.Join(Environment.NewLine, lines.Take(8)) + Environment.NewLine + "…";
        return $"{variable.Name}: {variable.Type}{Environment.NewLine}{value}";
    }

    // --- the toolstrip's groups ---------------------------------------------------------------------------

    /// <summary>The ERROR HANDLING group's check items (W11): each is a <c>dbstop if</c> condition of the store, Pause on Output the store's own flag.</summary>
    private void SetStopIf(string actionId, bool on)
    {
        if (Breakpoints is not { } store)
        {
            return;
        }

        switch (actionId)
        {
            case "AD_EDITOR_TOGGLE_STOP_IF_ERROR":
                StopIf(store, StopCondition.Error, on);
                break;
            case "AD_EDITOR_TOGGLE_STOP_IF_WARNING":
                StopIf(store, StopCondition.Warning, on);
                break;
            case "AD_EDITOR_TOGGLE_STOP_IF_NAN_INF":
                StopIf(store, StopCondition.NanInf, on);
                break;
            case "AD_EDITOR_TOGGLE_STOP_IF_UNSUPPRESSED_OUTPUT":
                store.PauseOnOutput = on;
                break;
        }
    }

    private static void StopIf(JgsBreakpointStore store, StopCondition condition, bool on)
    {
        if (on)
        {
            store.StopIf(condition);
        }
        else
        {
            store.ClearStopIf(condition, null, out _);
        }
    }

    /// <summary>The toolstrip's view of the debugger: the execution controls swapped, the ERROR HANDLING checks, what rests while debugging.</summary>
    private void ShowDebugState(AppTab? app)
    {
        foreach ((string tag, bool shown) in DesignToolstrip.ExecutionControls(_running && !_debugging, _debugging))
        {
            _toolstrip.SetVisible(tag, shown);
        }

        JgsBreakpointStore? store = Breakpoints;
        _toolstrip.SetSelected("AD_EDITOR_TOGGLE_STOP_IF_ERROR", store?.IsOn(StopCondition.Error) == true);
        _toolstrip.SetSelected("AD_EDITOR_TOGGLE_STOP_IF_WARNING", store?.IsOn(StopCondition.Warning) == true);
        _toolstrip.SetSelected("AD_EDITOR_TOGGLE_STOP_IF_NAN_INF", store?.IsOn(StopCondition.NanInf) == true);
        _toolstrip.SetSelected("AD_EDITOR_TOGGLE_STOP_IF_UNSUPPRESSED_OUTPUT", store?.PauseOnOutput == true);
        foreach (string id in new[] { "AD_EDITOR_TOGGLE_STOP_IF_ERROR", "AD_EDITOR_TOGGLE_STOP_IF_WARNING", "AD_EDITOR_TOGGLE_STOP_IF_NAN_INF", "AD_EDITOR_TOGGLE_STOP_IF_UNSUPPRESSED_OUTPUT" })
        {
            _toolstrip.SetEnabled(id, store is not null);
        }

        _toolstrip.SetEnabled("AD_EDITOR_CLEAR_ALL_BREAKPOINTS", store is { IsEmpty: false });
        bool codeFile = app is { InCodeView: true, Document.Path: not null } && store is not null;
        _toolstrip.SetEnabled("AD_EDITOR_TOGGLE_BREAKPOINT", codeFile);
        _toolstrip.SetEnabled("AD_EDITOR_TOGGLE_ENABLE_BREAKPOINT", codeFile);
        _toolstrip.SetEnabled("AD_EDITOR_TOGGLE_CONDITIONAL", codeFile);
        _toolstrip.SetEnabled("AD_EDITOR_PAUSE", _running && !_debugging && _debugger is not null);
        _toolstrip.SetEnabled("AD_EDITOR_CONTINUE", _debugging);
        _toolstrip.SetEnabled("AD_EDITOR_STEP", _debugging);
        _toolstrip.SetEnabled("AD_EDITOR_STEP_IN", _debugging);
        _toolstrip.SetEnabled("AD_EDITOR_STEP_OUT", _debugging);
        _toolstrip.SetEnabled("AD_EDITOR_STOP", _debugging || _running);
    }
}

/// <summary>Set Condition's dialog (W11): the expression a breakpoint pauses on, in JGraph's words (R2025b's is the rich text editor's own).</summary>
internal sealed class BreakpointConditionDialog : Window
{
    private readonly TextBox _expression;

    public BreakpointConditionDialog(string condition)
    {
        Title = "Set Condition";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "Breakpoint.ConditionDialog");
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Pause at this line only when the condition is true (an empty condition always pauses):", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _expression = new TextBox { Text = condition, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_expression, "Breakpoint.Condition");
        panel.Children.Add(_expression);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "Breakpoint.OK");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) =>
        {
            _expression.Focus();
            _expression.SelectAll();
        };
    }

    /// <summary>The expression typed, trimmed.</summary>
    public string Condition => _expression.Text.Trim();
}
