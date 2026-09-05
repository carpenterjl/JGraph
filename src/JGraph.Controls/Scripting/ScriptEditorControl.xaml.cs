using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly MenuItem _openSymbolItem = new() { InputGestureText = "Ctrl+D" };

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
        InstallContextMenu();
    }

    /// <summary>
    /// The text area's context menu: <em>Open name</em> for the identifier under the pointer (the
    /// caret is moved there first, as MATLAB does, so the menu names what was right-clicked), then
    /// the editing commands. Ctrl+D and F12 are the keyboard forms — Ctrl+D is MATLAB's, and it
    /// shadows AvalonEdit's own delete-line binding on purpose.
    /// </summary>
    private void InstallContextMenu()
    {
        _openSymbolItem.Click += (_, _) => RequestOpenSymbol();
        var menu = new ContextMenu();
        menu.Items.Add(_openSymbolItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Cu_t", Command = ApplicationCommands.Cut, CommandTarget = Editor.TextArea });
        menu.Items.Add(new MenuItem { Header = "_Copy", Command = ApplicationCommands.Copy, CommandTarget = Editor.TextArea });
        menu.Items.Add(new MenuItem { Header = "_Paste", Command = ApplicationCommands.Paste, CommandTarget = Editor.TextArea });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Select _All", Command = ApplicationCommands.SelectAll, CommandTarget = Editor.TextArea });
        menu.Opened += (_, _) =>
        {
            string? name = IdentifierAtCaret();
            _openSymbolItem.Header = name is null ? "_Open" : $"_Open {name}";
            _openSymbolItem.IsEnabled = name is not null;
        };
        Editor.TextArea.ContextMenu = menu;

        Editor.TextArea.PreviewMouseRightButtonDown += (_, e) =>
        {
            // A right-click inside a selection keeps it (Cut/Copy act on it); anywhere else the
            // caret goes under the pointer so 'Open' names the word that was clicked.
            if (Editor.SelectionLength == 0
                && Editor.GetPositionFromPoint(e.GetPosition(Editor)) is { } position)
            {
                Editor.TextArea.Caret.Position = position;
            }
        };

        var open = new ActionCommand(RequestOpenSymbol);
        Editor.TextArea.InputBindings.Add(new KeyBinding(open, Key.D, ModifierKeys.Control));
        Editor.TextArea.InputBindings.Add(new KeyBinding(open, Key.F12, ModifierKeys.None));
    }

    private void RequestOpenSymbol()
    {
        if (IdentifierAtCaret() is { } name)
        {
            OpenSymbolRequested?.Invoke(this, name);
        }
    }

    /// <summary>Raised when the user asks to open the definition of a name (the context menu, Ctrl+D or F12).</summary>
    public event EventHandler<string>? OpenSymbolRequested;

    /// <summary>
    /// The identifier at the caret: the selection when it is one word, otherwise the word the caret
    /// touches. Null when the caret is on nothing that could be a name.
    /// </summary>
    public string? IdentifierAtCaret()
    {
        if (Editor.SelectionLength > 0)
        {
            string selected = Editor.SelectedText.Trim();
            return IsIdentifier(selected) ? selected : null;
        }

        return IdentifierAt(Editor.CaretOffset);
    }

    /// <summary>The identifier that includes <paramref name="offset"/> (or ends right before it), or null.</summary>
    public string? IdentifierAt(int offset)
    {
        ICSharpCode.AvalonEdit.Document.TextDocument document = Editor.Document;
        if (offset < 0 || offset > document.TextLength)
        {
            return null;
        }

        int start = offset;
        while (start > 0 && IsIdentifierChar(document.GetCharAt(start - 1)))
        {
            start--;
        }

        int end = offset;
        while (end < document.TextLength && IsIdentifierChar(document.GetCharAt(end)))
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        string word = document.GetText(start, end - start);
        return IsIdentifier(word) ? word : null;
    }

    /// <summary>Puts the caret at the start of <paramref name="line"/> (1-based, clamped), scrolls to it and focuses the editor.</summary>
    public void GoToLine(int line)
    {
        int target = Math.Clamp(line, 1, Math.Max(1, Editor.Document.LineCount));
        Editor.CaretOffset = Editor.Document.GetLineByNumber(target).Offset;
        Editor.ScrollToLine(target);
        FocusEditor();
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsIdentifier(string word) =>
        word.Length > 0 && (char.IsLetter(word[0]) || word[0] == '_') && word.All(IsIdentifierChar);

    /// <summary>A command over a plain action, for the key bindings the editor adds itself.</summary>
    private sealed class ActionCommand : ICommand
    {
        private readonly Action _action;

        public ActionCommand(Action action) => _action = action;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _action();
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
