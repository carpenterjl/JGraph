using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The designer window's zoom and display (App Designer window plan, stage W6): VIEW > ZOOM's
/// Fit to View, Zoom In, Zoom Out and Reset Zoom, each a command of the window, with R2025b's keys
/// wherever the window has the keyboard (<c>ZoomHandler</c> listens on the whole page: Ctrl+Plus and
/// Ctrl+Minus, with Shift or on the keypad too, and Ctrl+Alt+0) - Space is the canvas's own - and
/// DISPLAY's Resizing Hints as a setting, which the canvas's menu turns on and off as well.
/// </summary>
internal sealed partial class AppDesignerWindow
{
    /// <summary>The zoom actions JGraph runs since W6, each by a command of its own.</summary>
    private static readonly IReadOnlyDictionary<string, RoutedCommand> ZoomCommands = new[]
    {
        "AD_ZOOM_IN", "AD_ZOOM_OUT", "AD_RESET_ZOOM", "AD_FIT_TO_VIEW",
    }.ToDictionary(static id => id, static id => new RoutedCommand(id, typeof(AppDesignerWindow)), StringComparer.Ordinal);

    private void BindZoomCommands()
    {
        foreach ((string id, RoutedCommand command) in ZoomCommands)
        {
            CommandBindings.Add(new CommandBinding(command, (_, _) => RunZoom(id), (_, e) => e.CanExecute = CanZoom(id)));
        }

        (Key Key, ModifierKeys Modifiers, string Action)[] keys =
        [
            (Key.OemPlus, ModifierKeys.Control, "AD_ZOOM_IN"),
            (Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, "AD_ZOOM_IN"),
            (Key.Add, ModifierKeys.Control, "AD_ZOOM_IN"),
            (Key.OemMinus, ModifierKeys.Control, "AD_ZOOM_OUT"),
            (Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift, "AD_ZOOM_OUT"),
            (Key.Subtract, ModifierKeys.Control, "AD_ZOOM_OUT"),
            (Key.D0, ModifierKeys.Control | ModifierKeys.Alt, "AD_RESET_ZOOM"),
            (Key.NumPad0, ModifierKeys.Control | ModifierKeys.Alt, "AD_RESET_ZOOM"),
        ];
        foreach ((Key key, ModifierKeys modifiers, string action) in keys)
        {
            InputBindings.Add(new KeyBinding(ZoomCommands[action], key, modifiers));
        }
    }

    private bool CanZoom(string id) => Active is { InCodeView: false, Canvas: var canvas } && id switch
    {
        "AD_ZOOM_IN" => canvas.CanZoomIn,
        "AD_ZOOM_OUT" => canvas.CanZoomOut,
        "AD_RESET_ZOOM" => canvas.CanResetZoom,
        _ => true,
    };

    private void RunZoom(string id)
    {
        if (Active is not { InCodeView: false, Canvas: var canvas })
        {
            return;
        }

        switch (id)
        {
            case "AD_ZOOM_IN":
                canvas.ZoomIn();
                break;
            case "AD_ZOOM_OUT":
                canvas.ZoomOut();
                break;
            case "AD_RESET_ZOOM":
                canvas.ResetZoom();
                break;
            default:
                canvas.FitToView();
                break;
        }

        // The zoom actions give the design area the keyboard back (focusDesignArea).
        canvas.Focus();
    }

    /// <summary>A canvas's zoom changed, or its menu turned a display option on or off.</summary>
    private void WatchView(DesignCanvas canvas)
    {
        canvas.ZoomChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        canvas.DisplayOptionToggled += (_, toggled) => Settings.Set(toggled.Setting, toggled.On);
    }
}
