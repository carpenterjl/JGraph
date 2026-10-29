using System.Windows;
using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The designer window's arranging (App Designer window plan, stage W4): CANVAS > ALIGN, ARRANGE
/// (Same Size, Reorder, Tab Order) and SPACE, Add Label and the font keys, each a command of the
/// window that the toolstrip's controls and the canvas's keys reach (R2025b's
/// <c>DesignAreaKeyboardController</c>: Ctrl+Alt+1 to 6, Ctrl+Shift+Home, End, Up and Down, Ctrl+L,
/// Ctrl+B, Ctrl+I, Ctrl+] and Ctrl+[, while the canvas has the keyboard).
/// </summary>
internal sealed partial class AppDesignerWindow
{
    /// <summary>The arrange actions JGraph runs since W4, each by a command of its own.</summary>
    private static readonly IReadOnlyDictionary<string, RoutedCommand> ArrangeCommands = new[]
    {
        "AD_DESIGN_ALIGN_LEFT", "AD_DESIGN_ALIGN_CENTER", "AD_DESIGN_ALIGN_RIGHT", "AD_DESIGN_ALIGN_TOP", "AD_DESIGN_ALIGN_MIDDLE", "AD_DESIGN_ALIGN_BOTTOM",
        "AD_DESIGN_SIZE_SAME_SIZE", "AD_DESIGN_SIZE_SAME_WIDTH", "AD_DESIGN_SIZE_SAME_HEIGHT",
        "AD_DESIGN_SPACING_HORZ", "AD_DESIGN_SPACING_VERT",
        "AD_DESIGN_REORDER_COMPONENT_FRONT", "AD_DESIGN_REORDER_COMPONENT_FORWARD", "AD_DESIGN_REORDER_COMPONENT_BACKWARD", "AD_DESIGN_REORDER_COMPONENT_BACK",
        "AD_DESIGN_TABORDER_APP_AUTO", "AD_DESIGN_TABORDER_APP_VISUALIZE",
        "AD_DESIGN_FONT_BOLD", "AD_DESIGN_FONT_ITALIC", "AD_DESIGN_FONT_INCREMENT", "AD_DESIGN_FONT_DECREMENT",
        "AD_ADD_LABEL",
    }.ToDictionary(static id => id, static id => new RoutedCommand(id, typeof(AppDesignerWindow)), StringComparer.Ordinal);

    private void BindArrangeCommands()
    {
        foreach ((string id, RoutedCommand command) in ArrangeCommands)
        {
            CommandBindings.Add(new CommandBinding(command, (_, _) => RunArrange(id), (_, e) => e.CanExecute = CanArrange(id)));
        }
    }

    /// <summary>The canvas's own keys for the arrange actions (<c>DesignAreaKeyboardController</c>): they act while the canvas has the keyboard.</summary>
    private static void AddArrangeKeys(DesignCanvas canvas)
    {
        (Key Key, ModifierKeys Modifiers, string Action)[] keys =
        [
            (Key.D1, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_LEFT"),
            (Key.D2, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_CENTER"),
            (Key.D3, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_RIGHT"),
            (Key.D4, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_TOP"),
            (Key.D5, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_MIDDLE"),
            (Key.D6, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_BOTTOM"),
            (Key.NumPad1, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_LEFT"),
            (Key.NumPad2, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_CENTER"),
            (Key.NumPad3, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_RIGHT"),
            (Key.NumPad4, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_TOP"),
            (Key.NumPad5, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_MIDDLE"),
            (Key.NumPad6, ModifierKeys.Control | ModifierKeys.Alt, "AD_DESIGN_ALIGN_BOTTOM"),
            (Key.Home, ModifierKeys.Control | ModifierKeys.Shift, "AD_DESIGN_REORDER_COMPONENT_FRONT"),
            (Key.Up, ModifierKeys.Control | ModifierKeys.Shift, "AD_DESIGN_REORDER_COMPONENT_FORWARD"),
            (Key.Down, ModifierKeys.Control | ModifierKeys.Shift, "AD_DESIGN_REORDER_COMPONENT_BACKWARD"),
            (Key.End, ModifierKeys.Control | ModifierKeys.Shift, "AD_DESIGN_REORDER_COMPONENT_BACK"),
            (Key.L, ModifierKeys.Control, "AD_ADD_LABEL"),
            (Key.B, ModifierKeys.Control, "AD_DESIGN_FONT_BOLD"),
            (Key.I, ModifierKeys.Control, "AD_DESIGN_FONT_ITALIC"),
            (Key.OemCloseBrackets, ModifierKeys.Control, "AD_DESIGN_FONT_INCREMENT"),
            (Key.OemOpenBrackets, ModifierKeys.Control, "AD_DESIGN_FONT_DECREMENT"),
        ];
        foreach ((Key key, ModifierKeys modifiers, string action) in keys)
        {
            canvas.InputBindings.Add(new KeyBinding(ArrangeCommands[action], key, modifiers));
        }
    }

    private bool CanArrange(string id)
    {
        if (Active is not { InCodeView: false, Document: var d })
        {
            return false;
        }

        return id switch
        {
            _ when id.StartsWith("AD_DESIGN_ALIGN_", StringComparison.Ordinal) => d.CanAlign,
            _ when id.StartsWith("AD_DESIGN_SIZE_", StringComparison.Ordinal) => d.CanSameSize,
            "AD_DESIGN_SPACING_HORZ" or "AD_DESIGN_SPACING_VERT" => d.CanSpace,
            "AD_DESIGN_REORDER_COMPONENT_FRONT" => d.CanStack(DesignStack.Front),
            "AD_DESIGN_REORDER_COMPONENT_FORWARD" => d.CanStack(DesignStack.Forward),
            "AD_DESIGN_REORDER_COMPONENT_BACKWARD" => d.CanStack(DesignStack.Backward),
            "AD_DESIGN_REORDER_COMPONENT_BACK" => d.CanStack(DesignStack.Back),
            "AD_DESIGN_FONT_BOLD" => d.CanChangeFont(DesignFontChange.Bold),
            "AD_DESIGN_FONT_ITALIC" => d.CanChangeFont(DesignFontChange.Italic),
            "AD_DESIGN_FONT_INCREMENT" => d.CanChangeFont(DesignFontChange.Larger),
            "AD_DESIGN_FONT_DECREMENT" => d.CanChangeFont(DesignFontChange.Smaller),
            "AD_ADD_LABEL" => d.CanAddLabel,
            _ => true, // the tab order, with an app open
        };
    }

    private void RunArrange(string id)
    {
        if (Active is not { InCodeView: false, Document: var d } app)
        {
            return;
        }

        app.Canvas.CancelGesture();
        _ = id switch
        {
            "AD_DESIGN_ALIGN_LEFT" => d.Align(DesignAlign.Left),
            "AD_DESIGN_ALIGN_CENTER" => d.Align(DesignAlign.Center),
            "AD_DESIGN_ALIGN_RIGHT" => d.Align(DesignAlign.Right),
            "AD_DESIGN_ALIGN_TOP" => d.Align(DesignAlign.Top),
            "AD_DESIGN_ALIGN_MIDDLE" => d.Align(DesignAlign.Middle),
            "AD_DESIGN_ALIGN_BOTTOM" => d.Align(DesignAlign.Bottom),
            "AD_DESIGN_SIZE_SAME_SIZE" => d.SameSize(DesignSameSize.Size),
            "AD_DESIGN_SIZE_SAME_WIDTH" => d.SameSize(DesignSameSize.Width),
            "AD_DESIGN_SIZE_SAME_HEIGHT" => d.SameSize(DesignSameSize.Height),
            "AD_DESIGN_SPACING_HORZ" => d.Space(horizontal: true, Spacing),
            "AD_DESIGN_SPACING_VERT" => d.Space(horizontal: false, Spacing),
            "AD_DESIGN_REORDER_COMPONENT_FRONT" => d.Stack(DesignStack.Front),
            "AD_DESIGN_REORDER_COMPONENT_FORWARD" => d.Stack(DesignStack.Forward),
            "AD_DESIGN_REORDER_COMPONENT_BACKWARD" => d.Stack(DesignStack.Backward),
            "AD_DESIGN_REORDER_COMPONENT_BACK" => d.Stack(DesignStack.Back),
            "AD_DESIGN_FONT_BOLD" => d.ChangeFont(DesignFontChange.Bold),
            "AD_DESIGN_FONT_ITALIC" => d.ChangeFont(DesignFontChange.Italic),
            "AD_DESIGN_FONT_INCREMENT" => d.ChangeFont(DesignFontChange.Larger),
            "AD_DESIGN_FONT_DECREMENT" => d.ChangeFont(DesignFontChange.Smaller),
            "AD_DESIGN_TABORDER_APP_AUTO" => ApplyTabOrder(app),
            "AD_DESIGN_TABORDER_APP_VISUALIZE" => VisualizeTabOrder(app),
            "AD_ADD_LABEL" => AddLabel(app),
            _ => false,
        };

        // The actions give the canvas the keyboard back (_requestCanvasFocus).
        app.Canvas.Focus();
    }

    // The SPACE section's box as it stands: "Evenly" or the pixels typed or picked (the combo's selectedItem).
    private string Spacing => _toolstrip.GetValue(DesignToolstrip.SpacingCombo) ?? DesignArrange.Evenly;

    // CANVAS > Tab Order: the whole app's, then shown, every component of it (TabOrderAction).
    private static bool ApplyTabOrder(AppTab app)
    {
        bool changed = app.Document.ApplyAutoTabOrder();
        app.Canvas.ShowTabOrder(app.Document.TabOrder());
        return changed;
    }

    private static bool VisualizeTabOrder(AppTab app)
    {
        app.Canvas.ShowTabOrder(app.Document.TabOrder());
        return true;
    }

    private bool AddLabel(AppTab app)
    {
        if (app.Document.AddLabel() is not { } refused)
        {
            return true;
        }

        ShowAlert(DesignDocument.AddLabelFailedTitle, refused);
        return false;
    }

    // App Designer's alert dialog (AppDesignerNotifications.displayAlertDialog), with its error icon and OK.
    private void ShowAlert(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>
    /// The arrange controls the selection enables that no command drives: the Reorder drop-down
    /// (when any of its four can move it), the SPACE box (two or more to space) and the Tab Order
    /// drop-down (an app in the Design View).
    /// </summary>
    private void ShowArrangeState()
    {
        DesignDocument? d = Active is { InCodeView: false } app ? app.Document : null;
        bool reorder = d is not null && (d.CanStack(DesignStack.Front) || d.CanStack(DesignStack.Forward) || d.CanStack(DesignStack.Backward) || d.CanStack(DesignStack.Back));
        _toolstrip.SetEnabled(DesignToolstrip.ReorderButton, reorder);
        _toolstrip.SetEnabled(DesignToolstrip.SpacingCombo, d?.CanSpace == true);
        _toolstrip.SetEnabled(DesignToolstrip.TabOrderButton, d is not null);
    }
}
