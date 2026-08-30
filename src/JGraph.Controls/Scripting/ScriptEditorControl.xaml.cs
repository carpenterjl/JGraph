using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

    private readonly BreakpointMargin _breakpointMargin = new();
    private readonly CurrentLineRenderer _currentLineRenderer = new();
    private readonly CompletionSupport _completion;

    /// <summary>
    /// The band drawn behind the line the debugger is paused at. Themed, and pushed into
    /// <see cref="CurrentLineRenderer"/> — which is a background renderer, not an element, so it
    /// cannot resolve a resource itself.
    /// </summary>
    public static readonly DependencyProperty CurrentLineBrushProperty =
        DependencyProperty.Register(
            nameof(CurrentLineBrush), typeof(Brush), typeof(ScriptEditorControl),
            new FrameworkPropertyMetadata(Brushes.Transparent, OnCurrentLineBrushChanged));

    /// <summary>
    /// Whether the dark theme is in force, which decides the syntax palette. Bound to the theme's own
    /// <c>JG.Theme.IsDark</c> flag, so switching themes re-highlights every open document.
    /// </summary>
    public static readonly DependencyProperty SyntaxIsDarkProperty =
        DependencyProperty.Register(
            nameof(SyntaxIsDark), typeof(bool), typeof(ScriptEditorControl),
            new FrameworkPropertyMetadata(false, OnSyntaxIsDarkChanged));

    public ScriptEditorControl()
    {
        InitializeComponent();
        _completion = new CompletionSupport(Editor);
        SetResourceReference(CurrentLineBrushProperty, Themes.ThemeKeys.CurrentLineHighlight);
        SetResourceReference(SyntaxIsDarkProperty, Themes.ThemeKeys.ThemeIsDark);
        Editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_currentLineRenderer);
        // Find has no implementation until the search panel is installed. Without this the Edit menu's
        // Find item is bound to a command nothing in the window can execute, so it is permanently grey.
        ICSharpCode.AvalonEdit.Search.SearchPanel.Install(Editor);
        _breakpointMargin.BreakpointToggled += (_, _) => BreakpointsChanged?.Invoke(this, EventArgs.Empty);
        _breakpointMargin.SetNextLineRequested += (_, line) => SetNextStatementRequested?.Invoke(this, line);
    }

    /// <summary>
    /// The element the standard editing commands act on. It is the text area and not this control:
    /// AvalonEdit installs Undo, Redo, Cut, Copy, Paste and Select All on the text area, whereas a
    /// routed command aimed at the UserControl finds nothing that handles it and bubbles back out to
    /// whatever hosts it.
    /// </summary>
    public IInputElement EditingSurface => Editor.TextArea;

    /// <summary>The band drawn behind the line the debugger is paused at.</summary>
    public Brush CurrentLineBrush
    {
        get => (Brush)GetValue(CurrentLineBrushProperty);
        set => SetValue(CurrentLineBrushProperty, value);
    }

    /// <summary>Whether the dark syntax palette is in force.</summary>
    public bool SyntaxIsDark
    {
        get => (bool)GetValue(SyntaxIsDarkProperty);
        set => SetValue(SyntaxIsDarkProperty, value);
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
            ApplyHighlighting();
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

    private void ApplyHighlighting()
    {
        if (_language is null)
        {
            Editor.SyntaxHighlighting = null;
            return;
        }

        // The editor's own background, not the theme's: it is what the highlighted text is actually
        // drawn on, and it is a DynamicResource so it is already correct for the theme in force.
        Color background = (Editor.Background as SolidColorBrush)?.Color ?? Colors.White;
        Editor.SyntaxHighlighting = SyntaxThemes.Resolve(_language, SyntaxIsDark, background);
    }

    private static void OnSyntaxIsDarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ScriptEditorControl)d).ApplyHighlighting();

    private static void OnCurrentLineBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScriptEditorControl)d;
        control._currentLineRenderer.HighlightBrush = (Brush)e.NewValue;
        control.Editor.TextArea.TextView.InvalidateLayer(
            ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
    }

    private void OnEditorTextChanged(object sender, EventArgs e) => TextChanged?.Invoke(this, EventArgs.Empty);
}
