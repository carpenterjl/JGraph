using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The designer window's clipboard (App Designer window plan, stage W5): Cut, Copy and Paste reach the
/// canvas in the Design View and the editor in the Code View; Duplicate (Ctrl+D on the canvas) and
/// Select All. One clipboard serves every app the window has open, as App Designer's one service
/// does, so components copied in one app paste into another.
/// </summary>
internal sealed partial class AppDesignerWindow
{
    /// <summary>Duplicates the selection on the canvas (Ctrl+D).</summary>
    public static readonly RoutedCommand DuplicateSelection = new(nameof(DuplicateSelection), typeof(AppDesignerWindow));

    /// <summary>Selects everything at the top of the figure.</summary>
    public static readonly RoutedCommand SelectAllComponents = new(nameof(SelectAllComponents), typeof(AppDesignerWindow));

    private readonly DesignClipboard _clipboard = new();

    private void BindClipboardCommands()
    {
        // Cut, copy and paste: the canvas's in the Design View, the editor's in the Code View.
        foreach (RoutedCommand edit in new[] { ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste })
        {
            CommandBindings.Add(new CommandBinding(edit, (_, e) => RunEdit((RoutedCommand)e.Command), (_, e) => e.CanExecute = CanEdit((RoutedCommand)e.Command)));
        }

        CommandBindings.Add(new CommandBinding(DuplicateSelection, (_, _) => OnCanvas(static d => d.Duplicate()),
            (_, e) => e.CanExecute = Active is { InCodeView: false, Document.CanDuplicate: true }));
        CommandBindings.Add(new CommandBinding(SelectAllComponents, (_, _) => OnCanvas(static d => { d.SelectAll(); return true; }),
            (_, e) => e.CanExecute = Active is { InCodeView: false }));
        _clipboard.Changed += (_, _) => CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>The canvas's own key for Duplicate; Ctrl+D in the Code View is the editor's.</summary>
    private static void AddClipboardKeys(DesignCanvas canvas) =>
        canvas.InputBindings.Add(new KeyBinding(DuplicateSelection, Key.D, ModifierKeys.Control));

    private bool CanEdit(RoutedCommand command)
    {
        if (Active is not { } app)
        {
            return false;
        }

        if (app.InCodeView)
        {
            return command.CanExecute(null, app.CodeView.Editor.EditingSurface);
        }

        DesignDocument d = app.Document;
        return command == ApplicationCommands.Paste ? d.CanPaste(_clipboard) : d.CanCopy;
    }

    private void RunEdit(RoutedCommand command)
    {
        if (Active is not { } app)
        {
            return;
        }

        if (app.InCodeView)
        {
            command.Execute(null, app.CodeView.Editor.EditingSurface);
            return;
        }

        OnCanvas(d => command == ApplicationCommands.Cut ? d.Cut(_clipboard)
            : command == ApplicationCommands.Copy ? d.Copy(_clipboard)
            : d.Paste(_clipboard));
    }

    // A canvas action: the gesture under way ended, the action run, and the keyboard given back to the canvas (_requestCanvasFocus).
    private void OnCanvas(Func<DesignDocument, bool> action)
    {
        if (Active is not { InCodeView: false } app)
        {
            return;
        }

        app.Canvas.CancelGesture();
        action(app.Document);
        app.Canvas.Focus();
    }
}
