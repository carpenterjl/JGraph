using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JGraph.Controls.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's code view (designer plan DS5): the app's class as the design and the user's
/// code now stand (<see cref="DesignDocument.CurrentText"/>) in the script editor, the lines App
/// Designer generates shaded and read-only, the user's - the editable section and the callbacks'
/// bodies - typed into and taken back into the document a moment after each edit, as App Designer's
/// editor hands its user changes to its code model. A change of the design writes the class again
/// around the user's code, with the caret kept where it was.
/// </summary>
public sealed partial class DesignCodeView : UserControl
{
    /// <summary>How long after the last keystroke the text is taken into the document (App Designer's <c>RTCUserEditManager</c> throttle).</summary>
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(70);

    private readonly ScriptEditorControl _editor = new() { ShadeGeneratedCode = true };
    private readonly DispatcherTimer _pending = new() { Interval = Throttle };
    private DesignDocument? _document;
    private bool _showing;

    public DesignCodeView()
    {
        _editor.ScriptLanguage = "MATLAB";
        // Nothing is editable while the code cannot be worked on (the design is view only).
        _editor.EditableLines = text => _document?.Code is null ? new HashSet<int>() : EditableLinesOf(text);
        _editor.TextChanged += (_, _) =>
        {
            if (!_showing && _document is not null)
            {
                _pending.Stop();
                _pending.Start();
            }
        };
        _pending.Tick += (_, _) =>
        {
            _pending.Stop();
            Push();
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_editor, "CodeView");
        Content = _editor;
    }

    /// <summary>Raised with what an edit could not do, for the window's status line.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>The editor itself.</summary>
    public ScriptEditorControl Editor => _editor;

    /// <summary>The app whose code is shown.</summary>
    public DesignDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document))
            {
                return;
            }

            Push();
            if (_document is not null)
            {
                _document.Changed -= OnChanged;
            }

            _document = value;
            if (value is not null)
            {
                value.Changed += OnChanged;
            }

            Show();
        }
    }

    /// <summary>
    /// Takes the text typed so far into the document now, ahead of whatever follows. Text whose
    /// structure no longer matches the app's is not taken: it is left in the editor for the user to
    /// put right, and the status line says so.
    /// </summary>
    /// <returns>False when the editor holds text the document would not take.</returns>
    public bool Push()
    {
        _pending.Stop();
        if (_document is not { } document || document.Code is null)
        {
            return true;
        }

        string text = _editor.ScriptText;
        if (text == document.CurrentText)
        {
            return true;
        }

        if (!document.ApplyEditedText(text))
        {
            Problem?.Invoke(this, "The code's structure no longer matches the app's (a function's end, or its signature, is not where App Designer wrote it), so the edit cannot be taken until it is put right.");
            return false;
        }

        return true;
    }

    /// <summary>Whether the caret is on a line the user may edit.</summary>
    public bool CaretIsEditable => _editor.CaretIsEditable;

    /// <summary>Inserts <paramref name="text"/> at the caret (App Designer's Insert at Cursor); false when the caret is on a generated line.</summary>
    public bool InsertAtCaret(string text) => _editor.InsertAtCaret(text);

    /// <summary>
    /// Goes to the function <paramref name="name"/> as App Designer's <c>GotoPlugin</c> does: the
    /// first line below its signature is scrolled into view; unless <paramref name="selectionOnly"/>
    /// (a browser row picked), the caret goes to that line's first character and the editor takes the focus.
    /// </summary>
    /// <returns>False when the text has no such function.</returns>
    public bool GoToFunction(string name, bool selectionOnly = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        string[] lines = _editor.ScriptText.Split('\n');
        int line = FunctionLine(lines, name);
        if (line <= 0)
        {
            return false;
        }

        int target = line + 1;
        if (selectionOnly)
        {
            _editor.ScrollToLine(target);
            return true;
        }

        string body = target - 1 < lines.Length ? lines[target - 1] : string.Empty;
        _editor.PlaceCaret(target, body.Length - body.TrimStart().Length + 1);
        return true;
    }

    /// <summary>Goes to the declaration of the property <paramref name="name"/>; the caret goes to its name unless <paramref name="selectionOnly"/>.</summary>
    /// <returns>False when the text declares no such property.</returns>
    public bool GoToProperty(string name, bool selectionOnly = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        string[] lines = _editor.ScriptText.Split('\n');
        int line = PropertyLine(lines, name);
        if (line <= 0)
        {
            return false;
        }

        if (selectionOnly)
        {
            _editor.ScrollToLine(line);
            return true;
        }

        _editor.PlaceCaret(line, lines[line - 1].IndexOf(name, StringComparison.Ordinal) + 1);
        return true;
    }

    /// <summary>
    /// Selects <paramref name="name"/> where the function (<paramref name="function"/>) or the property
    /// named so is declared, so that typing replaces it (what follows App Designer's Add Function and
    /// Add Property): a property on its own line, never on a signature that merely takes it.
    /// </summary>
    public void SelectDeclaration(string name, bool function)
    {
        ArgumentNullException.ThrowIfNull(name);
        string[] lines = _editor.ScriptText.Split('\n');
        int line = function ? FunctionLine(lines, name) : PropertyLine(lines, name);
        if (line > 0)
        {
            _editor.SelectText(line, name);
        }
    }

    /// <summary>
    /// The one-based line of the function <paramref name="name"/>'s signature (<c>CodeEditUtil.findFunctionLineNumber</c>):
    /// each line cut at its first comment sign and its strings taken out, a continued signature joined
    /// to the lines after it; the first that reads <c>function ... name</c>. 0 when there is none.
    /// </summary>
    public static int FunctionLine(IReadOnlyList<string> lines, string name)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(name);
        var pattern = new Regex(@"function\b.*?[=|\s]" + Regex.Escape(name) + @"\b(?!\w)");
        for (int i = 0; i < lines.Count; i++)
        {
            string code = WithoutStrings(Before(lines[i], "%"));
            if (!code.Contains("function", StringComparison.Ordinal))
            {
                continue;
            }

            int next = i + 1;
            while (code.Contains("...", StringComparison.Ordinal) && next < lines.Count)
            {
                code = Before(code, "...") + Before(lines[next], "%");
                next++;
            }

            if (pattern.IsMatch(code))
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// The one-based line declaring the property <paramref name="name"/> (<c>CodeEditUtil.findPropertyLineNumber</c>):
    /// inside a <c>properties</c> block, the first line whose first word is the name, or that lists it
    /// among others. 0 when there is none.
    /// </summary>
    public static int PropertyLine(IReadOnlyList<string> lines, string name)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(name);
        bool inBlock = false;
        for (int i = 0; i < lines.Count; i++)
        {
            string code = Before(lines[i], "%");
            bool starts = code.Contains("properties", StringComparison.Ordinal);
            bool ends = code.Contains("end", StringComparison.Ordinal);
            inBlock = starts || (inBlock && !ends);
            if (!inBlock)
            {
                continue;
            }

            string first = code.Trim().Split([' ', '=', '\n', '{'])[0];
            if (first.Length == 0)
            {
                continue;
            }

            if (first == name || code.Trim().Split([';', ','], StringSplitOptions.RemoveEmptyEntries).Any(t => t.Trim() == name))
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// The one-based lines of the class <paramref name="text"/> that are the user's: the editable
    /// section - without the one empty line App Designer keeps on each side of it, which are its
    /// own - and the callbacks' bodies.
    /// </summary>
    public static IReadOnlySet<int> EditableLinesOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var editable = new HashSet<int>();
        JgsMlappLayout layout = JgsMlappLayout.Of(text);
        foreach (MlappRegion region in layout.Regions.Where(static r => r.IsUserCode))
        {
            int first = region.FirstLine;
            int count = region.LineCount;
            if (region.Kind == MlappRegionKind.EditableSection)
            {
                if (count > 0 && layout.Lines[first].Length == 0)
                {
                    first++;
                    count--;
                }

                if (count > 0 && layout.Lines[first + count - 1].Length == 0)
                {
                    count--;
                }
            }

            for (int i = first; i < first + count; i++)
            {
                editable.Add(i + 1);
            }
        }

        return editable;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        // An edit of the design while text waits: the text goes in first, so nothing typed is lost.
        if (_pending.IsEnabled)
        {
            Push();
        }

        Show();
    }

    private void Show()
    {
        string text = _document?.CurrentText ?? string.Empty;
        if (_editor.ScriptText == text)
        {
            return;
        }

        _showing = true;
        try
        {
            _pending.Stop();
            _editor.ReplaceText(text);
            _editor.InvalidateEditableLines();
        }
        finally
        {
            _showing = false;
        }
    }

    private static string Before(string text, string mark)
    {
        int at = text.IndexOf(mark, StringComparison.Ordinal);
        return at < 0 ? text : text[..at];
    }

    private static string WithoutStrings(string text) => QuotedString().Replace(text, string.Empty);

    [GeneratedRegex(@"('|"")(.*?)('|"")")]
    private static partial Regex QuotedString();
}
