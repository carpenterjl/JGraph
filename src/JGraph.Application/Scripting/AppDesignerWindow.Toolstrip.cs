using System.Globalization;
using System.Windows;
using System.Windows.Input;
using JGraph.Controls.Scripting;
using JGraph.Controls.Toolstrip;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Application.Scripting;

/// <summary>
/// The designer window's toolstrip (App Designer window plan, stage W2): R2025b's App Designer
/// toolstrip (<see cref="DesignToolstrip"/>) in place of the flat toolbar. Each control reaches its
/// action by id: an action JGraph runs is a command of this window, one whose stage has not landed
/// (<see cref="DesignActions"/>) is shown disabled with R2025b's words. The view picks the tabs, as
/// App Designer's two contexts do; the RUN section swaps Run for Pause while the app's code runs.
/// </summary>
internal sealed partial class AppDesignerWindow
{
    /// <summary>Adds a public function to the editable section (EDITOR > Function > Public Function).</summary>
    public static readonly RoutedCommand AddPublicFunction = new(nameof(AddPublicFunction), typeof(AppDesignerWindow));

    /// <summary>Adds a public property (EDITOR > Property > Public Property).</summary>
    public static readonly RoutedCommand AddPublicProperty = new(nameof(AddPublicProperty), typeof(AppDesignerWindow));

    /// <summary>Quits App Designer: closes the window and every app in it (Ctrl+Q).</summary>
    public static readonly RoutedCommand Quit = new(nameof(Quit), typeof(AppDesignerWindow));

    /// <summary>Shows the Keyboard Shortcuts dialog (Ctrl+/).</summary>
    public static readonly RoutedCommand ShowShortcuts = new(nameof(ShowShortcuts), typeof(AppDesignerWindow));

    /// <summary>Shows or hides the canvas grid (Alt+G).</summary>
    public static readonly RoutedCommand ToggleGrid = new(nameof(ToggleGrid), typeof(AppDesignerWindow));

    /// <summary>Turns snapping to the grid on or off (Alt+P).</summary>
    public static readonly RoutedCommand ToggleSnap = new(nameof(ToggleSnap), typeof(AppDesignerWindow));

    /// <summary>Widens the grid interval by a step (Alt+Page Up).</summary>
    public static readonly RoutedCommand GridIncrement = new(nameof(GridIncrement), typeof(AppDesignerWindow));

    /// <summary>Narrows the grid interval by a step (Alt+Page Down).</summary>
    public static readonly RoutedCommand GridDecrement = new(nameof(GridDecrement), typeof(AppDesignerWindow));

    // An action JGraph does not run yet: its controls are disabled, with R2025b's words.
    private static readonly RoutedCommand NotYet = new(nameof(NotYet), typeof(AppDesignerWindow));

    private readonly Toolstrip _toolstrip = new();
    private bool _running;

    /// <summary>App Designer's settings (WO7); the host persists them when <see cref="DesignSettings.Changed"/> is raised.</summary>
    public DesignSettings Settings { get; }

    /// <summary>The command a toolstrip control's action runs.</summary>
    private ICommand? CommandFor(string action) => action switch
    {
        "AD_SAVE" => ApplicationCommands.Save,
        "AD_SAVEAS" => WorkspaceCommands.SaveAs,
        "AD_OPEN" => ApplicationCommands.Open,
        "AD_UNDO" => ApplicationCommands.Undo,
        "AD_REDO" => ApplicationCommands.Redo,
        "AD_DELETE" => ApplicationCommands.Delete,
        "AD_CUT" => ApplicationCommands.Cut,
        "AD_COPY" => ApplicationCommands.Copy,
        "AD_PASTE" => ApplicationCommands.Paste,
        "AD_DUPLICATE" => DuplicateSelection,
        "AD_SELECT_ALL" => SelectAllComponents,
        "AD_HELP" => ApplicationCommands.Help,
        // The editor's tools (W9a) reach the Code View's editor through the window's forwarding.
        "AD_EDITOR_FIND" => ApplicationCommands.Find,
        "AD_EDITOR_FIND_NEXT" => ScriptEditorControl.FindNext,
        "AD_EDITOR_FIND_PREVIOUS" => ScriptEditorControl.FindPrevious,
        "AD_EDITOR_FIND_SELECTION" => ScriptEditorControl.FindSelection,
        "AD_EDITOR_GOTO" => ScriptEditorControl.GoToLineCommand,
        "AD_EDITOR_TOGGLE_BOOKMARK" => ScriptEditorControl.ToggleBookmark,
        "AD_EDITOR_PREV_BOOKMARK" => ScriptEditorControl.PreviousBookmark,
        "AD_EDITOR_NEXT_BOOKMARK" => ScriptEditorControl.NextBookmark,
        // The CODE section (W9b).
        "AD_EDITOR_COMMENT" => ScriptEditorControl.CommentLines,
        "AD_EDITOR_UNCOMMENT" => ScriptEditorControl.UncommentLines,
        "AD_EDITOR_WRAP_COMMENT" => ScriptEditorControl.WrapCommentLines,
        "AD_EDITOR_SMART_INDENT" => ScriptEditorControl.SmartIndentLines,
        "AD_EDITOR_INDENT_MORE" => ScriptEditorControl.IncreaseIndent,
        "AD_EDITOR_INDENT_LESS" => ScriptEditorControl.DecreaseIndent,
        // The VIEW tab's tools (W9c); its toggles - the split, the display settings - answer through Invoked.
        "AD_EDITOR_FOLD_CURRENT" => ScriptEditorControl.FoldCurrent,
        "AD_EDITOR_UNFOLD_CURRENT" => ScriptEditorControl.UnfoldCurrent,
        "AD_EDITOR_FOLD_ALL" => ScriptEditorControl.FoldAll,
        "AD_EDITOR_UNFOLD_ALL" => ScriptEditorControl.UnfoldAll,
        "AD_EDITOR_ZOOM_IN" => ScriptEditorControl.ZoomIn,
        "AD_EDITOR_ZOOM_OUT" => ScriptEditorControl.ZoomOut,
        "AD_EDITOR_RESET_ZOOM" => ScriptEditorControl.ResetZoom,
        "AD_EDITOR_PRINT" => ApplicationCommands.Print,
        "AD_EDITOR_PRINT_SELECTION" => ScriptEditorControl.PrintSelection,
        "AD_EDITOR_SPLIT_NONE" or "AD_EDITOR_SPLIT_LEFTRIGHT" or "AD_EDITOR_SPLIT_TOPBOTTOM"
            or "AD_EDITOR_HIGHLIGHT_CURRENT_LINE" or "AD_EDITOR_LINE_NUMBERS" or "AD_EDITOR_DATATIPS" => null,
        "AD_RUN" => RunApp,
        // The SHARE drop-down (W12, W13): Standalone Desktop App and MATLAB App; Web App stays disabled (X2).
        "AD_SHARE_DESKTOP" => ShareDesktop,
        "AD_SHARE_MATLAB" => ShareMatlabApp,
        // Compare To on the DESIGNER and EDITOR tabs (W14).
        "OPEN_FILE_COMPARISON_DIALOG" => CompareTo,
        "OPEN_STARTSCREEN_DIALOG" => ShowStartPage,
        "AD_INPUT_ARGUMENTS" => CodeOptions,
        "AD_APP_DETAILS" => AppDetails,
        "AD_HELP_COMMENTS" => HelpText,
        "AD_SAVECOPY_AS" => SaveCopyAs,
        "AD_EXPORT" => ExportCode,
        "AD_RESPONSIFY_2COLUMN" => ConvertTwoPanel,
        "AD_RESPONSIFY_3COLUMN" => ConvertThreePanel,
        "AD_UNRESPONSIFY_TO_STANDARD" => ConvertStandard,
        "AD_EDITOR_ADD_CALLBACK" => AddCallback,
        "AD_EDITOR_ADD_FUNCTION" or "AD_EDITOR_ADD_PRIVATE_FUNCTION" => AddFunction,
        "AD_EDITOR_ADD_PUBLIC_FUNCTION" => AddPublicFunction,
        "AD_EDITOR_ADD_PROPERTY" or "AD_EDITOR_ADD_PRIVATE_PROPERTY" => AddProperty,
        "AD_EDITOR_ADD_PUBLIC_PROPERTY" => AddPublicProperty,
        "AD_DESIGN_GROUP_GROUP" => GroupSelection,
        "AD_DESIGN_GROUP_ADD" => AddToGroup,
        "AD_DESIGN_GROUP_REMOVE" => RemoveFromGroup,
        "AD_DESIGN_GROUP_UNGROUP" => UngroupSelection,
        // The toggles and the interval spinner keep settings: they answer through Invoked.
        "AD_DESIGN_GRID_SHOW" or "AD_DESIGN_GRID_SNAP" or "AD_DESIGN_VIEW_ALIGN_HINTS" or "AD_DESIGN_VIEW_DISPLAY_SIZE" or "AD_DESIGN_GRID_INTERVAL" => null,
        _ when ArrangeCommands.TryGetValue(action, out RoutedCommand? arrange) => arrange,
        _ when ZoomCommands.TryGetValue(action, out RoutedCommand? zoom) => zoom,
        _ => DesignActions.IsLive(action) ? null : NotYet,
    };

    private void BuildToolstrip()
    {
        _toolstrip.CommandResolver = CommandFor;
        _toolstrip.CommandTarget = this;
        _toolstrip.TextFormatter = static text => DesignToolstrip.Format(text, component: false);
        _toolstrip.Layout = DesignToolstrip.Layout;
        _toolstrip.SetTabVisible(DesignToolstrip.SimulinkTab, false);
        // An app's code shows the plain Callback button; the split one, Event and Configure for Apps
        // are a custom component's (W15).
        _toolstrip.SetVisible(DesignToolstrip.AddCallbackSplitButton, false);
        _toolstrip.SetVisible(DesignToolstrip.AddEventButton, false);
        _toolstrip.SetVisible(DesignToolstrip.ConfigureMetadataColumn, false);
        // A drop-down, a combo box or a label whose action JGraph does not run has no command to
        // follow: disabled here, once. A drop-down whose items are all to come is too.
        foreach (ToolstripItem item in DesignToolstrip.Layout.AllItems())
        {
            bool live = item.Kind switch
            {
                ToolstripItemKind.DropDownButton => DesignToolstrip.Layout.Popup(item.PopupTag)?.Entries.Any(e => DesignActions.IsLive(e.ActionId)) == true,
                ToolstripItemKind.ComboBox or ToolstripItemKind.Label or ToolstripItemKind.ToggleButton => item.ActionId is null || DesignActions.IsLive(item.ActionId),
                _ => true,
            };
            if (!live && item.Tag is { } tag)
            {
                _toolstrip.SetEnabled(tag, false);
            }
        }

        _toolstrip.Invoked += (_, e) => Invoked(e);
        BindArrangeCommands();
        BindStructureCommands();
        BindZoomCommands();
        System.Windows.Automation.AutomationProperties.SetAutomationId(_toolstrip, "AppDesigner.Toolstrip");

        CommandBindings.Add(new CommandBinding(NotYet, static (_, _) => { }, static (_, e) => e.CanExecute = false));
        CommandBindings.Add(new CommandBinding(AddPublicFunction, (_, _) => Declared(Active?.Document.InsertUserFunction("public"), function: true),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AddPublicProperty, (_, _) => Declared(Active?.Document.InsertUserProperty("public"), function: false),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(Quit, (_, _) => Close()));
        CommandBindings.Add(new CommandBinding(ShowShortcuts, (_, _) => AppDesignerDialogs.Shortcuts(this, Settings)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => OpenHelp("apps-designer")));
        CommandBindings.Add(new CommandBinding(ToggleGrid, (_, _) => Settings.Set(DesignSettings.ShowGrid, !Settings.GetBool(DesignSettings.ShowGrid)),
            (_, e) => e.CanExecute = Active is { InCodeView: false }));
        CommandBindings.Add(new CommandBinding(ToggleSnap, (_, _) => Settings.Set(DesignSettings.SnapToGrid, !Settings.GetBool(DesignSettings.SnapToGrid)),
            (_, e) => e.CanExecute = Active is { InCodeView: false }));
        CommandBindings.Add(new CommandBinding(GridIncrement, (_, _) => StepInterval(+1), (_, e) => e.CanExecute = Active is { InCodeView: false }));
        CommandBindings.Add(new CommandBinding(GridDecrement, (_, _) => StepInterval(-1), (_, e) => e.CanExecute = Active is { InCodeView: false }));
        // The editor's tools reach the Code View's editor (Find since W2, the rest W9a); cut, copy and paste reach it or the canvas (W5).
        foreach (RoutedCommand tool in new RoutedCommand[]
                 {
                     ApplicationCommands.Find, ApplicationCommands.Replace,
                     ScriptEditorControl.FindNext, ScriptEditorControl.FindPrevious, ScriptEditorControl.FindSelection, ScriptEditorControl.GoToLineCommand,
                     ScriptEditorControl.ToggleBookmark, ScriptEditorControl.NextBookmark, ScriptEditorControl.PreviousBookmark,
                     ScriptEditorControl.CommentLines, ScriptEditorControl.UncommentLines, ScriptEditorControl.WrapCommentLines,
                     ScriptEditorControl.SmartIndentLines, ScriptEditorControl.IncreaseIndent, ScriptEditorControl.DecreaseIndent,
                     ScriptEditorControl.FoldCurrent, ScriptEditorControl.UnfoldCurrent, ScriptEditorControl.FoldAll, ScriptEditorControl.UnfoldAll,
                     ScriptEditorControl.ZoomIn, ScriptEditorControl.ZoomOut, ScriptEditorControl.ResetZoom, ScriptEditorControl.SwitchSplit,
                     ApplicationCommands.Print, ScriptEditorControl.PrintSelection,
                 })
        {
            RoutedCommand command = tool;
            CommandBindings.Add(new CommandBinding(command,
                (_, _) => ForwardToEditor(command, run: true),
                (_, e) => e.CanExecute = ForwardToEditor(command, run: false)));
        }

        BindClipboardCommands();
        BindShareCommands();
        BindCompareCommands();

        // The editor's display settings are every editor's (W9c): read from App Designer's settings, kept there when a toggle or the zoom changes them.
        EditorDisplay.Load(Settings.Values);
        EditorDisplay.Changed += OnEditorDisplayChanged;
        Closed += (_, _) => EditorDisplay.Changed -= OnEditorDisplayChanged;

        InputBindings.Add(new KeyBinding(Quit, Key.Q, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ShowShortcuts, Key.Oem2, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ToggleGrid, Key.G, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(ToggleSnap, Key.P, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(GridIncrement, Key.PageUp, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(GridDecrement, Key.PageDown, ModifierKeys.Alt));
        Settings.Changed += (_, name) => SettingChanged(name);
    }

    private bool _forwardingToEditor;

    /// <summary>The four display settings as text, for App Designer's settings (<c>matlab.editor.displaysettings</c>).</summary>
    private static IEnumerable<(string Name, string Value)> EditorDisplayValues()
    {
        yield return (DesignSettings.EditorShowLineNumbers, EditorDisplay.ShowLineNumbers ? "true" : "false");
        yield return (DesignSettings.EditorHighlightCurrentLine, EditorDisplay.HighlightCurrentLine ? "true" : "false");
        yield return (DesignSettings.EditorDatatips, EditorDisplay.Datatips ? "true" : "false");
        yield return (DesignSettings.EditorZoomFactor, EditorDisplay.ZoomFactor.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private void OnEditorDisplayChanged(object? sender, EventArgs e)
    {
        foreach ((string name, string value) in EditorDisplayValues())
        {
            Settings.Set(name, value);
        }

        ShowToolstripState();
    }

    /// <summary>
    /// Asks the Code View's editor whether it can run one of its tools, and runs it there when asked to; false with no app in its Code View.
    /// The editor answers every query of its own tools, but the guard keeps a query that comes back from asking again for ever.
    /// </summary>
    private bool ForwardToEditor(RoutedCommand command, bool run)
    {
        if (_forwardingToEditor || Active is not { InCodeView: true } app)
        {
            return false;
        }

        _forwardingToEditor = true;
        try
        {
            IInputElement target = app.CodeView.Editor.EditingSurface;
            if (!command.CanExecute(null, target))
            {
                return false;
            }

            if (run)
            {
                command.Execute(null, target);
            }

            return true;
        }
        finally
        {
            _forwardingToEditor = false;
        }
    }

    /// <summary>A toggle or the spinner changed a setting; INSERT > Callback has no action of its own.</summary>
    private void Invoked(ToolstripInvokedEventArgs e)
    {
        switch (e.ActionId)
        {
            case "AD_DESIGN_GRID_SHOW" when e.Value is bool on:
                Settings.Set(DesignSettings.ShowGrid, on);
                break;
            case "AD_DESIGN_GRID_SNAP" when e.Value is bool on:
                Settings.Set(DesignSettings.SnapToGrid, on);
                break;
            case "AD_DESIGN_VIEW_ALIGN_HINTS" when e.Value is bool on:
                Settings.Set(DesignSettings.ShowAlignmentHints, on);
                break;
            case "AD_DESIGN_VIEW_DISPLAY_SIZE" when e.Value is bool on:
                Settings.Set(DesignSettings.ShowResizingHints, on);
                break;
            case "AD_DESIGN_GRID_INTERVAL" when e.Value is double interval:
                Settings.Set(DesignSettings.GridInterval, (int)DesignGeometry.GridIntervalWithin(interval));
                break;
            case "AD_DESIGN_GRID_INTERVAL" when e.Value is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed):
                Settings.Set(DesignSettings.GridInterval, (int)DesignGeometry.GridIntervalWithin(typed));
                break;
            // The VIEW tab (W9c): the display toggles are every editor's; the split is the active Code View's.
            case "AD_EDITOR_HIGHLIGHT_CURRENT_LINE" when e.Value is bool on:
                EditorDisplay.HighlightCurrentLine = on;
                break;
            case "AD_EDITOR_LINE_NUMBERS" when e.Value is bool on:
                EditorDisplay.ShowLineNumbers = on;
                break;
            case "AD_EDITOR_DATATIPS" when e.Value is bool on:
                EditorDisplay.Datatips = on;
                break;
            case "AD_EDITOR_SPLIT_NONE" when e.Value is true:
                SetCodeViewSplit(SplitDirection.None);
                break;
            case "AD_EDITOR_SPLIT_LEFTRIGHT" when e.Value is true:
                SetCodeViewSplit(SplitDirection.LeftRight);
                break;
            case "AD_EDITOR_SPLIT_TOPBOTTOM" when e.Value is true:
                SetCodeViewSplit(SplitDirection.TopBottom);
                break;
            case null when e.Tag == DesignToolstrip.AddCallbackButton:
                ShowAddCallback();
                break;
            case null when DesignToolstrip.GoToFunctionOf(e.Tag) is { } function:
                GoToFunction(function);
                break;
            case null when DesignToolstrip.RunSetIndex(e.Tag) is int set and >= 0:
                RunSetChosen(set, e.Value as string);
                break;
            case null when e.Tag == DesignToolstrip.RunFieldItem:
                RunTyped(e.Value as string ?? string.Empty);
                break;
            case null when DesignToolstrip.RecentFileIndex(e.Tag) is int recent and >= 0
                && Settings.RecentFiles().Files is { } files && recent < files.Count:
                OpenRecent(files[recent].FilePath);
                break;
        }
    }

    private void SetCodeViewSplit(SplitDirection direction)
    {
        if (Active is { } app)
        {
            app.CodeView.Editor.SetSplit(direction);
            ShowToolstripState();
        }
    }

    private void StepInterval(int direction)
    {
        double next = DesignGeometry.GridIntervalWithin(Settings.GridIntervalValue + direction * DesignGeometry.GridStep);
        Settings.Set(DesignSettings.GridInterval, (int)next);
    }

    private void SettingChanged(string name)
    {
        ApplyCanvasOptions();
        ShowToolstripState();
        if (name is DesignSettings.ShowTitleFilePath or DesignSettings.ShowTitleFileExtension)
        {
            ShowTitle();
        }

        if (name is DesignSettings.MaxFileListSize or DesignSettings.FileList)
        {
            _startPage.RecentFiles = Settings.RecentFiles().Files;
        }

        if (name is DesignSettings.EditorShowLineNumbers or DesignSettings.EditorHighlightCurrentLine or DesignSettings.EditorDatatips or DesignSettings.EditorZoomFactor)
        {
            EditorDisplay.Load(Settings.Values);
        }
    }

    /// <summary>
    /// The toolstrip as the active app is: its view's tabs (none of the contextual ones with no app
    /// open), the toggles and the interval from the settings, the controls only an open app enables,
    /// and the RUN section's execution controls.
    /// </summary>
    private void ShowToolstripState()
    {
        AppTab? app = Active;
        _toolstrip.SetContext(app is null ? [] : app.InCodeView ? DesignToolstrip.CodeViewGroups : DesignToolstrip.DesignViewGroups);
        _toolstrip.SetSelected("AD_DESIGN_GRID_SHOW", Settings.GetBool(DesignSettings.ShowGrid));
        _toolstrip.SetSelected("AD_DESIGN_GRID_SNAP", Settings.GetBool(DesignSettings.SnapToGrid));
        _toolstrip.SetSelected("AD_DESIGN_VIEW_ALIGN_HINTS", Settings.GetBool(DesignSettings.ShowAlignmentHints));
        _toolstrip.SetSelected("AD_DESIGN_VIEW_DISPLAY_SIZE", Settings.GetBool(DesignSettings.ShowResizingHints));
        _toolstrip.SetValue(DesignToolstrip.IntervalSpinner, ((int)Settings.GridIntervalValue).ToString(CultureInfo.InvariantCulture));
        // The VIEW tab's toggles (W9c): the display settings every editor shares, and the active Code View's split.
        _toolstrip.SetSelected("AD_EDITOR_HIGHLIGHT_CURRENT_LINE", EditorDisplay.HighlightCurrentLine);
        _toolstrip.SetSelected("AD_EDITOR_LINE_NUMBERS", EditorDisplay.ShowLineNumbers);
        _toolstrip.SetSelected("AD_EDITOR_DATATIPS", EditorDisplay.Datatips);
        SplitDirection split = app?.CodeView.Editor.Split ?? SplitDirection.None;
        _toolstrip.SetSelected("AD_EDITOR_SPLIT_NONE", split == SplitDirection.None);
        _toolstrip.SetSelected("AD_EDITOR_SPLIT_LEFTRIGHT", split == SplitDirection.LeftRight);
        _toolstrip.SetSelected("AD_EDITOR_SPLIT_TOPBOTTOM", split == SplitDirection.TopBottom);
        foreach (string id in new[] { "AD_EDITOR_SPLIT_NONE", "AD_EDITOR_SPLIT_LEFTRIGHT", "AD_EDITOR_SPLIT_TOPBOTTOM" })
        {
            _toolstrip.SetEnabled(id, app is not null);
        }
        foreach (string id in new[] { "AD_DESIGN_GRID_SHOW", "AD_DESIGN_GRID_SNAP", "AD_DESIGN_VIEW_ALIGN_HINTS", "AD_DESIGN_VIEW_DISPLAY_SIZE", "AD_DESIGN_GRID_INTERVAL" })
        {
            _toolstrip.SetEnabled(id, app is not null);
        }

        _toolstrip.SetEnabled("appDesignerToolstrip.layoutTab.arrange.groupButton", app is not null);
        // The SHARE drop-down wants an app (enabledWithNoApp false), and rests while the app's code runs (W12).
        _toolstrip.SetEnabled("AD_SHARE_ALL_OPTIONS", app is not null && !_running);
        ShowArrangeState();
        _toolstrip.SetEnabled(DesignToolstrip.AddCallbackButton, app is { Document.Code: not null });
        _toolstrip.SetEnabled(DesignToolstrip.GoToButton, app is { Document.Code: not null });
        _toolstrip.SetIcon("AD_RUN", app is not null && !DesignRunArguments.IsNoArguments(app.Document.RunArguments.MostRecent) ? "runConfiguration" : "playControl");
        foreach ((string tag, bool shown) in DesignToolstrip.ExecutionControls(_running, debugging: false))
        {
            _toolstrip.SetVisible(tag, shown);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>F1 and the Start Page's links: a section of the scripting guide, F1's the App Designer chapter (R2025b opens its own documentation).</summary>
    private void OpenHelp(string anchor)
    {
        if (JGraph.Scripting.Startup.StartupHelp.FindGuide(AppContext.BaseDirectory) is not { } guide)
        {
            _status.Text = "The scripting guide was not found beside the application.";
            return;
        }

        try
        {
            string target = new Uri(guide).AbsoluteUri + "#" + anchor;
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Text = $"Could not open the scripting guide: {ex.Message}";
        }
    }
}
