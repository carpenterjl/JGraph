using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using JGraph.Scripting.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The editor's CODE tools (App Designer window plan W9b): Comment (Ctrl+R), Uncomment (Ctrl+Shift+R, Ctrl+T), Wrap Comments (Ctrl+J),
/// Smart Indent (Ctrl+I), Increase Indent (Ctrl+]) and Decrease Indent (Ctrl+[), each over the selection's lines or the caret's, as one
/// undo step, and never on a line that may not be edited. Tab over a selection spanning lines indents it, Shift+Tab unindents, as
/// R2025b's editor does with the Tab key inserting spaces.
/// </summary>
public partial class ScriptEditorControl
{
    /// <summary>Comment (Ctrl+R): <c>% </c> on each line at the lines' common indent.</summary>
    public static RoutedUICommand CommentLines { get; } = Tool("Comment", nameof(CommentLines), Key.R, ModifierKeys.Control);

    /// <summary>Uncomment (Ctrl+Shift+R, and Ctrl+T): the leading comment sign off each line.</summary>
    public static RoutedUICommand UncommentLines { get; } = Tool("Uncomment", nameof(UncommentLines), Key.R, ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>Wrap Comments (Ctrl+J): the caret's run of comment lines, or the selection's, joined and broken at the width.</summary>
    public static RoutedUICommand WrapCommentLines { get; } = Tool("Wrap Comments", nameof(WrapCommentLines), Key.J, ModifierKeys.Control);

    /// <summary>Smart Indent (Ctrl+I): the selection's lines, or the caret's, indented by the code around them.</summary>
    public static RoutedUICommand SmartIndentLines { get; } = Tool("Smart Indent", nameof(SmartIndentLines), Key.I, ModifierKeys.Control);

    /// <summary>Increase Indent (Ctrl+]).</summary>
    public static RoutedUICommand IncreaseIndent { get; } = Tool("Increase Indent", nameof(IncreaseIndent), Key.OemCloseBrackets, ModifierKeys.Control);

    /// <summary>Decrease Indent (Ctrl+[).</summary>
    public static RoutedUICommand DecreaseIndent { get; } = Tool("Decrease Indent", nameof(DecreaseIndent), Key.OemOpenBrackets, ModifierKeys.Control);

    /// <summary>How a function's contents are indented by Smart Indent (<c>FunctionIndentingFormat</c>); R2025b's default.</summary>
    public IndentMode SmartIndentMode { get; set; } = IndentMode.Mixed;

    private void InstallCodeTools()
    {
        // The Tab key inserts spaces (matlab.editor.tab.InsertSpaces), four of them (IndentSize).
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = EditorTabs.IndentSize;
        void Bind(ICommand command, Action run) =>
            CommandBindings.Add(new CommandBinding(command, (_, e) => { run(); e.Handled = true; }, (_, e) => { e.CanExecute = CanEditSelectedLines(); e.Handled = true; }));

        Bind(CommentLines, () => EditSelectedLines(static (lines, first, last) => CommentRules.Comment(lines, first, last)));
        Bind(UncommentLines, () => EditSelectedLines(static (lines, first, last) => CommentRules.Uncomment(lines, first, last)));
        Bind(WrapCommentLines, WrapSelectedComments);
        Bind(SmartIndentLines, SmartIndentSelectedLines);
        Bind(IncreaseIndent, () => EditSelectedLines((lines, first, last) => IndentRules.Indent(lines, first, last, LineIsEditable)));
        Bind(DecreaseIndent, () => EditSelectedLines((lines, first, last) => IndentRules.Unindent(lines, first, last, LineIsEditable)));
        Editor.TextArea.InputBindings.Add(new KeyBinding(UncommentLines, Key.T, ModifierKeys.Control));
        Editor.TextArea.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Tab)
            {
                return;
            }

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (shift)
            {
                // rtc_shift_tab: the caret's line or the selection's unindented.
                if (CanEditSelectedLines())
                {
                    EditSelectedLines((lines, first, last) => IndentRules.Unindent(lines, first, last, LineIsEditable));
                }

                e.Handled = true;
            }
            else if (SelectionSpansLines() && CanEditSelectedLines())
            {
                // rtc_format_code_tab: a selection spanning lines is indented as a block.
                EditSelectedLines((lines, first, last) => IndentRules.Indent(lines, first, last, LineIsEditable));
                e.Handled = true;
            }
        };
    }

    private bool LineIsEditable(int line) => line >= 1 && line <= Editor.Document.LineCount && _readOnlyLines.CanInsert(Editor.Document.GetLineByNumber(line).Offset);

    private bool SelectionSpansLines()
    {
        if (Editor.SelectionLength == 0)
        {
            return false;
        }

        TextDocument document = Editor.Document;
        return document.GetLineByOffset(Editor.SelectionStart).LineNumber != document.GetLineByOffset(Editor.SelectionStart + Editor.SelectionLength).LineNumber;
    }

    /// <summary>The one-based lines the tools work on: the selection's (<c>_getSelectionRangeForCursor</c>), or the caret's.</summary>
    private (int First, int Last) SelectedLines()
    {
        TextDocument document = Editor.Document;
        if (Editor.SelectionLength == 0)
        {
            return (Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Line);
        }

        int start = Editor.SelectionStart;
        int end = start + Editor.SelectionLength;
        TextLocation a = document.GetLocation(start);
        TextLocation b = document.GetLocation(end);
        return CommentRules.LinesOf(a.Line, a.Column, b.Line, b.Column, end >= document.TextLength);
    }

    private bool CanEditSelectedLines()
    {
        (int first, int last) = SelectedLines();
        for (int line = first; line <= last; line++)
        {
            if (LineIsEditable(line))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs <paramref name="edit"/> over the text's lines for the selected range and writes back the lines that changed, as one undo step, the selection kept over them.</summary>
    private void EditSelectedLines(Action<List<string>, int, int> edit)
    {
        (int first, int last) = SelectedLines();
        List<string> lines = Lines();
        List<string> before = [.. lines];
        edit(lines, first, last);
        if (lines.Count != before.Count)
        {
            return;
        }

        ApplyLines(before, lines, first, last);
    }

    private List<string> Lines()
    {
        TextDocument document = Editor.Document;
        var lines = new List<string>(document.LineCount);
        for (int i = 1; i <= document.LineCount; i++)
        {
            lines.Add(document.GetText(document.GetLineByNumber(i)));
        }

        return lines;
    }

    /// <summary>Writes the lines of [<paramref name="first"/>, <paramref name="last"/>] that differ, editable ones only, as one undo step.</summary>
    private void ApplyLines(List<string> before, List<string> after, int first, int last)
    {
        TextDocument document = Editor.Document;
        bool hadSelection = Editor.SelectionLength > 0;
        int caretLine = Editor.TextArea.Caret.Line;
        int caretColumn = Editor.TextArea.Caret.Column;
        bool changed = false;
        using (document.RunUpdate())
        {
            for (int line = last; line >= first; line--)
            {
                if (line < 1 || line > before.Count || before[line - 1] == after[line - 1] || !LineIsEditable(line))
                {
                    continue;
                }

                DocumentLine at = document.GetLineByNumber(line);
                document.Replace(at.Offset, at.Length, after[line - 1]);
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        if (hadSelection)
        {
            SelectWholeLines(first, last);
        }
        else
        {
            DocumentLine at = document.GetLineByNumber(Math.Clamp(caretLine, 1, document.LineCount));
            Editor.TextArea.Caret.Line = at.LineNumber;
            Editor.TextArea.Caret.Column = Math.Clamp(caretColumn + (after[at.LineNumber - 1].Length - before[at.LineNumber - 1].Length), 1, at.Length + 1);
        }
    }

    private void SelectWholeLines(int first, int last)
    {
        TextDocument document = Editor.Document;
        first = Math.Clamp(first, 1, document.LineCount);
        last = Math.Clamp(last, first, document.LineCount);
        int start = document.GetLineByNumber(first).Offset;
        int end = document.GetLineByNumber(last).EndOffset;
        Editor.Select(start, end - start);
    }

    /// <summary>Smart Indent over the selected lines, each by the lines above it (<c>IndentUtilities.formatLines</c>); blank lines emptied (<c>PadEmptyLines</c> off).</summary>
    private void SmartIndentSelectedLines()
    {
        (int first, int last) = SelectedLines();
        List<string> before = Lines();
        List<string> after = SmartIndent.Format(before, first - 1, last - 1, SmartIndentMode, padEmptyLines: false);
        ApplyLines(before, after, first, last);
    }

    /// <summary>Wrap Comments at the caret or over the selection (<c>WrapComments._wrapComments</c>); nothing when a line it would change may not be edited.</summary>
    private void WrapSelectedComments()
    {
        TextDocument document = Editor.Document;
        List<string> before = Lines();
        List<string> after = [.. before];
        int selectionFirst = -1;
        int selectionLast = -1;
        if (Editor.SelectionLength > 0)
        {
            (int first, int last) = SelectedLines();
            selectionFirst = first - 1;
            selectionLast = last - 1;
        }

        int sections = WrapComments.Wrap(after, Editor.TextArea.Caret.Line - 1, selectionFirst, selectionLast);
        if (sections == 0 || after.SequenceEqual(before))
        {
            return;
        }

        // The stretch that differs, by the lines the two have in common at the start and at the end.
        int prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        int firstLine = prefix + 1;
        int lastLine = before.Count - suffix;
        for (int line = firstLine; line <= lastLine; line++)
        {
            if (!LineIsEditable(line))
            {
                return;
            }
        }

        string newLine = TextUtilities.GetNewLineFromDocument(document, Math.Max(1, Math.Min(firstLine, document.LineCount)));
        int start = document.GetLineByNumber(firstLine).Offset;
        int end = lastLine >= firstLine ? document.GetLineByNumber(lastLine).EndOffset : start;
        string text = string.Join(newLine, after.Skip(prefix).Take(after.Count - prefix - suffix));
        bool hadSelection = Editor.SelectionLength > 0;
        using (document.RunUpdate())
        {
            document.Replace(start, end - start, text);
        }

        if (hadSelection)
        {
            SelectWholeLines(firstLine, after.Count - suffix);
        }
    }
}
