using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace JGraph.Controls.Scripting;

/// <summary>
/// A script document editor: a syntax-highlighting code editor (AvalonEdit) whose highlighting follows
/// the document's <see cref="ScriptLanguage"/>. It is a pure editing surface — running scripts, console
/// output, and debugging live in the hosting window, which reads <see cref="ScriptText"/> and listens
/// to <see cref="TextChanged"/>.
/// </summary>
public partial class ScriptEditorControl : UserControl
{
    private string? _language;

    static ScriptEditorControl()
    {
        // Teach the shared highlighting manager about our two languages so GetDefinition("JGS") and
        // GetDefinition("MATLAB") resolve like the built-in "C#"/"Python" definitions do. Registering is
        // idempotent for our purposes (this runs once).
        Register(JgsSyntax.Name, JgsSyntax.Xshd, ".jgs");
        Register(MatlabSyntax.Name, MatlabSyntax.Xshd, ".m");

        static void Register(string name, string xshd, string extension)
        {
            using var reader = XmlReader.Create(new StringReader(xshd));
            IHighlightingDefinition definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(name, new[] { extension }, definition);
        }
    }

    private readonly BreakpointMargin _breakpointMargin = new();
    private readonly CurrentLineRenderer _currentLineRenderer = new();
    private readonly CompletionSupport _completion;

    public ScriptEditorControl()
    {
        InitializeComponent();
        _completion = new CompletionSupport(Editor);
        Editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_currentLineRenderer);
        _breakpointMargin.BreakpointToggled += (_, _) => BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _breakpointMargin.SetNextLineRequested += (_, line) => SetNextStatementRequested?.Invoke(this, line);
    }

    /// <summary>Raised whenever the buffer text changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised when the user drags the execution arrow to a new line (or right-clicks the
    /// gutter and picks "Set next statement here"). The host forwards it to the debugger, which may
    /// reject the target.</summary>
    public event EventHandler<int>? SetNextStatementRequested;

    /// <summary>Raised when the user toggles a breakpoint in the gutter (or via <see cref="ToggleBreakpointAtCaret"/>).</summary>
    public event EventHandler? BreakpointsChanged;

    /// <summary>The 1-based lines carrying a breakpoint.</summary>
    public IReadOnlyCollection<int> Breakpoints => _breakpointMargin.Breakpoints;

    /// <summary>Replaces the breakpoint set (e.g. restoring persisted breakpoints). Does not raise
    /// <see cref="BreakpointsChanged"/> — the host initiated it.</summary>
    public void SetBreakpoints(IEnumerable<int> lines) => _breakpointMargin.SetBreakpoints(lines);

    /// <summary>Toggles a breakpoint on the caret's line (the F9 gesture).</summary>
    public void ToggleBreakpointAtCaret() => _breakpointMargin.Toggle(Editor.TextArea.Caret.Line);

    /// <summary>
    /// Moves the current-execution marker (gutter arrow + line highlight) to <paramref name="line"/>,
    /// scrolling it into view; null clears it.
    /// </summary>
    public void SetCurrentLine(int? line)
    {
        _breakpointMargin.SetCurrentLine(line);
        _currentLineRenderer.SetCurrentLine(line);
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
        if (line is int target && target >= 1 && target <= Editor.Document.LineCount)
        {
            Editor.ScrollToLine(target);
        }
    }

    /// <summary>The language whose syntax highlighting the editor shows ("C#", "Python", "JGS").</summary>
    public string? ScriptLanguage
    {
        get => _language;
        set
        {
            _language = value;
            _completion.Language = value;
            Editor.SyntaxHighlighting = value is null ? null : HighlightingManager.Instance.GetDefinition(value);
        }
    }

    /// <summary>Supplies completion symbols from the rest of the workspace (JGS documents): <c>fn</c>s
    /// defined in other scripts, harvested by the host. Null when the document stands alone.</summary>
    public Func<IReadOnlyList<JGraph.Scripting.Completion.CompletionItem>>? CompletionWorkspaceSymbols
    {
        get => _completion.WorkspaceSymbols;
        set => _completion.WorkspaceSymbols = value;
    }

    /// <summary>Supplies the workspace's files and folders for path completion inside the string
    /// arguments of the file builtins (<c>readcsv("…</c>). Null when no workspace is open.</summary>
    public Func<IReadOnlyList<JGraph.Scripting.Completion.WorkspaceFileEntry>>? CompletionWorkspaceFiles
    {
        get => _completion.WorkspaceFiles;
        set => _completion.WorkspaceFiles = value;
    }

    /// <summary>The script source shown in the editor.</summary>
    public string ScriptText
    {
        get => Editor.Text;
        set => Editor.Text = value ?? string.Empty;
    }

    /// <summary>
    /// Puts the keyboard focus in the text area. When the control is not in the visual tree yet
    /// (a freshly created document tab), the focus is deferred until it loads.
    /// </summary>
    public void FocusEditor()
    {
        if (Editor.IsLoaded)
        {
            Editor.Focus();
            return;
        }

        RoutedEventHandler? once = null;
        once = (_, _) =>
        {
            Editor.Loaded -= once;
            Editor.Focus();
        };
        Editor.Loaded += once;
    }

    private void OnEditorTextChanged(object sender, EventArgs e) => TextChanged?.Invoke(this, EventArgs.Empty);
}
