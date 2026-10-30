using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The designer window's structure keys (App Designer window plan, stage W5c), which share their keys
/// with W4's: Ctrl+Shift+Home, End and the arrows move a selected tab or tree node among its siblings
/// (<c>ArrowKeyMoveReorderPlugin</c>, <c>TreeController._processReorderShortcuts</c>) and restack any
/// other component (<c>DesignAreaKeyboardController</c>); Ctrl+] and Ctrl+[ indent a selected tree
/// node (<c>_processIndentKeyShortcuts</c>) and change any other's font size.
/// </summary>
internal sealed partial class AppDesignerWindow
{
    private enum StructureKey
    {
        Home,
        End,
        Up,
        Down,
        Left,
        Right,
        IndentMore,
        IndentLess,
    }

    private static readonly IReadOnlyDictionary<StructureKey, RoutedCommand> StructureCommands =
        Enum.GetValues<StructureKey>().ToDictionary(static k => k, static k => new RoutedCommand("W5c" + k, typeof(AppDesignerWindow)));

    private void BindStructureCommands()
    {
        foreach ((StructureKey key, RoutedCommand command) in StructureCommands)
        {
            CommandBindings.Add(new CommandBinding(command, (_, _) => RunStructureKey(key), (_, e) => e.CanExecute = CanStructureKey(key)));
        }
    }

    /// <summary>The keys that move a tab or node, or restack, and that indent a node, or change the font, while the canvas has the keyboard.</summary>
    private static void AddStructureKeys(DesignCanvas canvas)
    {
        (Key Key, ModifierKeys Modifiers, StructureKey Action)[] keys =
        [
            (Key.Home, ModifierKeys.Control | ModifierKeys.Shift, StructureKey.Home),
            (Key.End, ModifierKeys.Control | ModifierKeys.Shift, StructureKey.End),
            (Key.Up, ModifierKeys.Control | ModifierKeys.Shift, StructureKey.Up),
            (Key.Down, ModifierKeys.Control | ModifierKeys.Shift, StructureKey.Down),
            (Key.Left, ModifierKeys.Control | ModifierKeys.Shift, StructureKey.Left),
            (Key.Right, ModifierKeys.Control | ModifierKeys.Shift, StructureKey.Right),
            (Key.OemCloseBrackets, ModifierKeys.Control, StructureKey.IndentMore),
            (Key.OemOpenBrackets, ModifierKeys.Control, StructureKey.IndentLess),
        ];
        foreach ((Key key, ModifierKeys modifiers, StructureKey action) in keys)
        {
            canvas.InputBindings.Add(new KeyBinding(StructureCommands[action], key, modifiers));
        }
    }

    private bool CanStructureKey(StructureKey key)
    {
        if (Active is not { InCodeView: false, Document: var d })
        {
            return false;
        }

        bool inside = d.MovesInside;
        bool node = d.SelectedTreeNode is not null;
        if (ToolKeyIsIdle(d, key))
        {
            return false;
        }

        return key switch
        {
            StructureKey.Home => inside ? d.CanMove(DesignMove.First) : d.CanStack(DesignStack.Front),
            StructureKey.End => inside ? d.CanMove(DesignMove.Last) : d.CanStack(DesignStack.Back),
            StructureKey.Up => inside ? d.CanMove(DesignMove.Previous) : d.CanStack(DesignStack.Forward),
            StructureKey.Down => inside ? d.CanMove(DesignMove.Next) : d.CanStack(DesignStack.Backward),
            StructureKey.Left => d.CanMove(DesignMove.Previous),
            StructureKey.Right => d.CanMove(DesignMove.Next),
            StructureKey.IndentMore => node ? d.CanIndent(increase: true) : d.CanChangeFont(DesignFontChange.Larger),
            _ => node ? d.CanIndent(increase: false) : d.CanChangeFont(DesignFontChange.Smaller),
        };
    }

    // A tool moves along its toolbar only and a toolbar up and down only (ToolKeyboardPlugin, ToolbarKeyboardPlugin, W5d).
    private static bool ToolKeyIsIdle(DesignDocument d, StructureKey key) => d.Selection is [var first, ..]
        && ((DesignBars.IsTool(first) && key is StructureKey.Up or StructureKey.Down)
            || (first.Type == DesignBars.ToolbarType && key is StructureKey.Left or StructureKey.Right));

    private void RunStructureKey(StructureKey key)
    {
        if (Active is not { InCodeView: false, Document: var d } app)
        {
            return;
        }

        app.Canvas.CancelGesture();
        if (ToolKeyIsIdle(d, key))
        {
            return;
        }

        bool inside = d.MovesInside;
        bool node = d.SelectedTreeNode is not null;
        _ = key switch
        {
            StructureKey.Home => inside ? d.Move(DesignMove.First) : d.Stack(DesignStack.Front),
            StructureKey.End => inside ? d.Move(DesignMove.Last) : d.Stack(DesignStack.Back),
            StructureKey.Up => inside ? d.Move(DesignMove.Previous) : d.Stack(DesignStack.Forward),
            StructureKey.Down => inside ? d.Move(DesignMove.Next) : d.Stack(DesignStack.Backward),
            StructureKey.Left => d.Move(DesignMove.Previous),
            StructureKey.Right => d.Move(DesignMove.Next),
            StructureKey.IndentMore => node ? d.Indent(increase: true) : d.ChangeFont(DesignFontChange.Larger),
            _ => node ? d.Indent(increase: false) : d.ChangeFont(DesignFontChange.Smaller),
        };
        app.Canvas.Focus();
    }
}
