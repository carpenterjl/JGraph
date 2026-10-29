using System.Globalization;
using System.Windows;
using System.Windows.Input;
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
        "AD_HELP" => ApplicationCommands.Help,
        "AD_EDITOR_FIND" => ApplicationCommands.Find,
        "AD_RUN" => RunApp,
        "AD_INPUT_ARGUMENTS" => CodeOptions,
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
        "AD_DESIGN_GRID_SHOW" or "AD_DESIGN_GRID_SNAP" or "AD_DESIGN_VIEW_ALIGN_HINTS" or "AD_DESIGN_GRID_INTERVAL" => null,
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
        System.Windows.Automation.AutomationProperties.SetAutomationId(_toolstrip, "AppDesigner.Toolstrip");

        CommandBindings.Add(new CommandBinding(NotYet, static (_, _) => { }, static (_, e) => e.CanExecute = false));
        CommandBindings.Add(new CommandBinding(AddPublicFunction, (_, _) => Declared(Active?.Document.InsertUserFunction("public"), function: true),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AddPublicProperty, (_, _) => Declared(Active?.Document.InsertUserProperty("public"), function: false),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(Quit, (_, _) => Close()));
        CommandBindings.Add(new CommandBinding(ShowShortcuts, (_, _) => AppDesignerDialogs.Shortcuts(this, Settings)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => OpenHelp()));
        CommandBindings.Add(new CommandBinding(ToggleGrid, (_, _) => Settings.Set(DesignSettings.ShowGrid, !Settings.GetBool(DesignSettings.ShowGrid)),
            (_, e) => e.CanExecute = Active is { InCodeView: false }));
        CommandBindings.Add(new CommandBinding(ToggleSnap, (_, _) => Settings.Set(DesignSettings.SnapToGrid, !Settings.GetBool(DesignSettings.SnapToGrid)),
            (_, e) => e.CanExecute = Active is { InCodeView: false }));
        CommandBindings.Add(new CommandBinding(GridIncrement, (_, _) => StepInterval(+1), (_, e) => e.CanExecute = Active is { InCodeView: false }));
        CommandBindings.Add(new CommandBinding(GridDecrement, (_, _) => StepInterval(-1), (_, e) => e.CanExecute = Active is { InCodeView: false }));
        // Cut, copy, paste and find reach the Code View's editor; on the canvas they wait for W5.
        foreach (RoutedCommand edit in new[] { ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.Find })
        {
            CommandBindings.Add(new CommandBinding(edit,
                (_, e) => ((RoutedCommand)e.Command).Execute(null, Active!.CodeView.Editor.EditingSurface),
                (_, e) => e.CanExecute = Active is { InCodeView: true } app && ((RoutedCommand)e.Command).CanExecute(null, app.CodeView.Editor.EditingSurface)));
        }

        InputBindings.Add(new KeyBinding(Quit, Key.Q, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ShowShortcuts, Key.Oem2, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ToggleGrid, Key.G, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(ToggleSnap, Key.P, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(GridIncrement, Key.PageUp, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(GridDecrement, Key.PageDown, ModifierKeys.Alt));
        Settings.Changed += (_, name) => SettingChanged(name);
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
            case "AD_DESIGN_GRID_INTERVAL" when e.Value is double interval:
                Settings.Set(DesignSettings.GridInterval, (int)DesignGeometry.GridIntervalWithin(interval));
                break;
            case "AD_DESIGN_GRID_INTERVAL" when e.Value is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed):
                Settings.Set(DesignSettings.GridInterval, (int)DesignGeometry.GridIntervalWithin(typed));
                break;
            case null when e.Tag == DesignToolstrip.AddCallbackButton:
                ShowAddCallback();
                break;
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
        _toolstrip.SetValue(DesignToolstrip.IntervalSpinner, ((int)Settings.GridIntervalValue).ToString(CultureInfo.InvariantCulture));
        foreach (string id in new[] { "AD_DESIGN_GRID_SHOW", "AD_DESIGN_GRID_SNAP", "AD_DESIGN_VIEW_ALIGN_HINTS", "AD_DESIGN_GRID_INTERVAL" })
        {
            _toolstrip.SetEnabled(id, app is not null);
        }

        _toolstrip.SetEnabled("appDesignerToolstrip.layoutTab.arrange.groupButton", app is not null);
        _toolstrip.SetEnabled(DesignToolstrip.AddCallbackButton, app is { Document.Code: not null });
        foreach ((string tag, bool shown) in DesignToolstrip.ExecutionControls(_running, debugging: false))
        {
            _toolstrip.SetVisible(tag, shown);
        }

        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>F1: the scripting guide's App Designer chapter (R2025b opens its own documentation).</summary>
    private void OpenHelp()
    {
        if (JGraph.Scripting.Startup.StartupHelp.FindGuide(AppContext.BaseDirectory) is not { } guide)
        {
            _status.Text = "The scripting guide was not found beside the application.";
            return;
        }

        try
        {
            string target = new Uri(guide).AbsoluteUri + "#apps-designer";
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Text = $"Could not open the scripting guide: {ex.Message}";
        }
    }
}
