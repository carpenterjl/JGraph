using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using JGraph.Scripting.Analysis;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The Code Analyzer's marks in the editor (App Designer window plan W10): the host hands the messages over
/// (<see cref="SetCodeMessages"/>) after it analyzed the text; the editor underlines their spans, lists them on the
/// message bar at the right, shows a message's text when the pointer rests on its span, and applies a message's fix
/// to the text (the bar's Fix button, Alt+Enter, or the context menu's Fix item) by its edits on the document.
/// </summary>
public partial class ScriptEditorControl
{
    private readonly CodeMessageRenderer _messageRenderer = new();
    private readonly MenuItem _fixItem = new() { Header = "_Fix", InputGestureText = "Alt+Enter" };
    private IReadOnlyList<CodeMessage> _codeMessages = [];

    /// <summary>Fixes the message under the caret (Alt+Enter).</summary>
    public static RoutedUICommand FixMessage { get; } = Tool("Fix", nameof(FixMessage), Key.Enter, ModifierKeys.Alt);

    /// <summary>Goes to the next Code Analyzer message after the caret.</summary>
    public static RoutedUICommand NextMessage { get; } = new("Next Message", nameof(NextMessage), typeof(ScriptEditorControl));

    /// <summary>The Code Analyzer's messages shown, as the host last set them.</summary>
    public IReadOnlyList<CodeMessage> CodeMessages => _codeMessages;

    /// <summary>Raised after a fix changed the text, so the host analyzes again at once rather than after its pause.</summary>
    public event EventHandler? FixApplied;

    /// <summary>The text area's context menu, for a host to add an item to (App Designer's "Disable coding alerts").</summary>
    public ContextMenu? TextContextMenu => Editor.TextArea.ContextMenu;

    /// <summary>Shows <paramref name="messages"/>: the marks under the text, the bar, the status.</summary>
    public void SetCodeMessages(IReadOnlyList<CodeMessage> messages)
    {
        _codeMessages = messages ?? [];
        _messageRenderer.Messages = _codeMessages;
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Selection);
        if (_secondaryReady)
        {
            Editor2.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Selection);
        }

        Strip.Show(_codeMessages, MessageAtCaret());
        ShowStatus();
    }

    private void InstallAnalysis()
    {
        Editor.TextArea.TextView.BackgroundRenderers.Add(_messageRenderer);
        _messageRenderer.ErrorBrush = (Brush)FindResource(Themes.ThemeKeys.Error);
        _messageRenderer.WarningBrush = (Brush)FindResource(Themes.ThemeKeys.Warning);
        _messageRenderer.InfoBrush = (Brush)FindResource(Themes.ThemeKeys.Accent);
        Strip.BindLineCount(() => Editor.Document.LineCount);
        Strip.NextRequested += (_, _) => GoToNextMessage();
        Strip.FixRequested += (_, _) => FixAtCaret();
        Strip.GoToLineRequested += (_, line) =>
        {
            GoToLine(line);
            FocusEditor();
        };
        Editor.TextArea.Caret.PositionChanged += (_, _) => Strip.ShowCaret(MessageAtCaret());
        CommandBindings.Add(new CommandBinding(FixMessage, (_, e) => { FixAtCaret(); e.Handled = true; }, (_, e) => { e.CanExecute = MessageAtCaret()?.Fix is not null; e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(NextMessage, (_, e) => { GoToNextMessage(); e.Handled = true; }, (_, e) => { e.CanExecute = _codeMessages.Count > 0; e.Handled = true; }));
        Editor.TextArea.InputBindings.Add(new KeyBinding(FixMessage, Key.Enter, ModifierKeys.Alt));
        _fixItem.Click += (_, _) => FixAtCaret();
        if (Editor.TextArea.ContextMenu is { } menu)
        {
            menu.Items.Insert(0, _fixItem);
            menu.Items.Insert(1, new Separator());
            menu.Opened += (_, _) =>
            {
                CodeMessage? at = MessageAtCaret();
                _fixItem.Header = at?.Fix is null ? "_Fix" : "_Fix: " + at.Text;
                _fixItem.IsEnabled = at?.Fix is not null;
                _fixItem.Visibility = _codeMessages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            };
        }
    }

    /// <summary>The message whose span holds <paramref name="offset"/>, or the first one on its line when the offset is at the line's end, or null.</summary>
    public CodeMessage? MessageAt(int offset)
    {
        TextDocument document = Editor.Document;
        if (offset < 0 || offset > document.TextLength)
        {
            return null;
        }

        DocumentLine line = document.GetLineByOffset(offset);
        int column = offset - line.Offset + 1;
        CodeMessage? best = null;
        foreach (CodeMessage message in _codeMessages)
        {
            if (message.Line != line.LineNumber)
            {
                continue;
            }

            bool inside = column >= message.StartColumn && column <= message.EndColumn + 1;
            if (inside && (best is null || message.EndColumn - message.StartColumn < best.EndColumn - best.StartColumn))
            {
                best = message;
            }
        }

        return best;
    }

    /// <summary>The message under the caret, or any on the caret's line with a fix when none is under it.</summary>
    public CodeMessage? MessageAtCaret()
    {
        CodeMessage? at = MessageAt(Editor.CaretOffset);
        if (at is not null)
        {
            return at;
        }

        int line = Editor.TextArea.Caret.Line;
        return _codeMessages.FirstOrDefault(m => m.Line == line && m.Fix is not null) ?? _codeMessages.FirstOrDefault(m => m.Line == line);
    }

    /// <summary>The text shown when the pointer rests at <paramref name="offset"/>: the messages' texts on that span, the fix's hint under them.</summary>
    internal string? MessageTextAt(int offset)
    {
        CodeMessage? at = MessageAt(offset);
        if (at is null)
        {
            return null;
        }

        IEnumerable<string> lines = _codeMessages.Where(m => m.Line == at.Line && m.StartColumn == at.StartColumn).Select(static m => m.Text).Distinct();
        string text = string.Join("\n", lines);
        return at.Fix is not null ? text + "\nFix: Alt+Enter" : text;
    }

    /// <summary>Applies the fix of the message under the caret, when it has one.</summary>
    /// <returns>Whether a fix was applied.</returns>
    public bool FixAtCaret() => MessageAtCaret() is { Fix: not null } message && ApplyFix(message);

    /// <summary>Applies <paramref name="message"/>'s fix to the document.</summary>
    public bool ApplyFix(CodeMessage message)
    {
        if (message.Fix is not { } fix)
        {
            return false;
        }

        TextDocument document = Editor.Document;
        document.BeginUpdate();
        try
        {
            foreach (CodeEdit edit in fix.Edits.OrderByDescending(static e => e.Line).ThenByDescending(static e => e.StartColumn).ThenByDescending(static e => e.EndColumn))
            {
                if (edit.Line < 1 || edit.Line > document.LineCount)
                {
                    continue;
                }

                DocumentLine line = document.GetLineByNumber(edit.Line);
                if (edit.StartColumn == 0)
                {
                    document.Remove(line.Offset, line.TotalLength);
                    continue;
                }

                int start = Math.Clamp(edit.StartColumn - 1, 0, line.Length);
                int end = Math.Clamp(edit.EndColumn, start, line.Length);
                document.Replace(line.Offset + start, end - start, edit.Replacement);
            }
        }
        finally
        {
            document.EndUpdate();
        }

        // The marks are stale until the host analyzes again; the fixed message goes now.
        SetCodeMessages([.. _codeMessages.Where(m => !ReferenceEquals(m, message))]);
        FixApplied?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Puts the caret on the next message after it (the first when none follows).</summary>
    public bool GoToNextMessage()
    {
        if (_codeMessages.Count == 0)
        {
            return false;
        }

        int line = Editor.TextArea.Caret.Line;
        int column = Editor.TextArea.Caret.Column;
        CodeMessage next = _codeMessages.Where(m => m.Line > line || (m.Line == line && m.StartColumn > column)).OrderBy(static m => m.Line).ThenBy(static m => m.StartColumn).FirstOrDefault()
            ?? _codeMessages.OrderBy(static m => m.Line).ThenBy(static m => m.StartColumn).First();
        if (next.Line >= 1 && next.Line <= Editor.Document.LineCount)
        {
            DocumentLine target = Editor.Document.GetLineByNumber(next.Line);
            Editor.CaretOffset = target.Offset + Math.Clamp(next.StartColumn - 1, 0, target.Length);
            Editor.ScrollToLine(next.Line);
            FocusEditor();
        }

        return true;
    }

    /// <summary>The part of the automation status that says how many messages the analyzer left.</summary>
    private string AnalysisStatus() => _codeMessages.Count > 0 ? $"; Messages {_codeMessages.Count}" : string.Empty;
}
