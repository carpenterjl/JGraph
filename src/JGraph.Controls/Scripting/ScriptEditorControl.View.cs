using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using JGraph.Scripting.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>How the document is split in two views (R2025b's <c>SplitScreenDirections</c>).</summary>
public enum SplitDirection
{
    None,
    LeftRight,
    TopBottom,
}

/// <summary>
/// The editor's VIEW tools (App Designer window plan W9c): code folding over the blocks of the text (Ctrl+. collapses the block at the
/// caret, Ctrl+Shift+. expands it, Ctrl+, and Ctrl+Shift+, all of them), the display settings every editor shares (line numbers, the
/// current line, datatips) and the zoom (Ctrl+Plus, Ctrl+Minus, Ctrl+Alt+0, Ctrl+wheel), the document split in two views (F6 between
/// them), and Print (Ctrl+P) and Print Selection through WPF's print dialog with the code in its colours.
/// </summary>
public partial class ScriptEditorControl
{
    /// <summary>Collapse (Ctrl+.): the block at the caret.</summary>
    public static RoutedUICommand FoldCurrent { get; } = Tool("Collapse", nameof(FoldCurrent), Key.OemPeriod, ModifierKeys.Control);

    /// <summary>Expand (Ctrl+Shift+.): the block at the caret and those around it.</summary>
    public static RoutedUICommand UnfoldCurrent { get; } = Tool("Expand", nameof(UnfoldCurrent), Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>Collapse All (Ctrl+,).</summary>
    public static RoutedUICommand FoldAll { get; } = Tool("Collapse All", nameof(FoldAll), Key.OemComma, ModifierKeys.Control);

    /// <summary>Expand All (Ctrl+Shift+,).</summary>
    public static RoutedUICommand UnfoldAll { get; } = Tool("Expand All", nameof(UnfoldAll), Key.OemComma, ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>Zoom In (Ctrl+Plus).</summary>
    public static RoutedUICommand ZoomIn { get; } = Tool("Zoom In", nameof(ZoomIn), Key.OemPlus, ModifierKeys.Control);

    /// <summary>Zoom Out (Ctrl+Minus).</summary>
    public static RoutedUICommand ZoomOut { get; } = Tool("Zoom Out", nameof(ZoomOut), Key.OemMinus, ModifierKeys.Control);

    /// <summary>Reset Zoom (Ctrl+Alt+0).</summary>
    public static RoutedUICommand ResetZoom { get; } = Tool("Reset Zoom", nameof(ResetZoom), Key.D0, ModifierKeys.Control | ModifierKeys.Alt);

    /// <summary>Split Document: Off.</summary>
    public static RoutedUICommand SplitNone { get; } = new("Off", nameof(SplitNone), typeof(ScriptEditorControl));

    /// <summary>Split Document: Left/Right.</summary>
    public static RoutedUICommand SplitLeftRight { get; } = new("Left/Right", nameof(SplitLeftRight), typeof(ScriptEditorControl));

    /// <summary>Split Document: Top/Bottom.</summary>
    public static RoutedUICommand SplitTopBottom { get; } = new("Top/Bottom", nameof(SplitTopBottom), typeof(ScriptEditorControl));

    /// <summary>Switch Screen Focus (F6): the keyboard to the other view.</summary>
    public static RoutedUICommand SwitchSplit { get; } = Tool("Switch Screen Focus", nameof(SwitchSplit), Key.F6, ModifierKeys.None);

    /// <summary>Print Selection...</summary>
    public static RoutedUICommand PrintSelection { get; } = new("Print Selection...", nameof(PrintSelection), typeof(ScriptEditorControl));

    private FoldingManager? _folding2;
    private IReadOnlyList<FoldCandidate> _candidates = [];
    private IReadOnlySet<int> _closedByDefault = new HashSet<int>();
    private double _wheel;
    private bool _secondaryReady;
    private readonly System.Windows.Threading.DispatcherTimer _refold = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private Func<IReadOnlyList<string>, IReadOnlyList<FoldCandidate>>? _foldSource;

    /// <summary>
    /// Where a plain file's folds come from: the blocks of its lines, read again a moment after each edit (the Code View sets its
    /// candidates itself, with App Designer's default folds, and leaves this null). All open when first read.
    /// </summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<FoldCandidate>>? FoldSource
    {
        get => _foldSource;
        set
        {
            _foldSource = value;
            _refold.Stop();
            if (value is not null)
            {
                RefreshFolds();
            }
        }
    }

    /// <summary>The document's lines as they are, without their line ends.</summary>
    private IReadOnlyList<string> DocumentLines()
    {
        TextDocument document = Editor.Document;
        var lines = new List<string>(document.LineCount);
        foreach (DocumentLine line in document.Lines)
        {
            lines.Add(document.GetText(line));
        }

        return lines;
    }

    private void RefreshFolds()
    {
        if (_foldSource is { } source)
        {
            SetFoldCandidates(source(DocumentLines()), null);
        }
    }

    /// <summary>How the document is shown: in one view, or two side by side or one above the other.</summary>
    public SplitDirection Split { get; private set; }

    /// <summary>Raised after <see cref="Split"/> changed.</summary>
    public event EventHandler? SplitChanged;

    /// <summary>The name a print job carries: the file's, set by the host.</summary>
    public string? DocumentTitle { get; set; }

    private void InstallViewTools()
    {
        void Bind(ICommand command, Action run, Func<bool>? can = null) =>
            CommandBindings.Add(new CommandBinding(command, (_, e) => { run(); e.Handled = true; }, (_, e) => { e.CanExecute = can?.Invoke() ?? true; e.Handled = true; }));

        Bind(FoldCurrent, () => FoldAtCaret(fold: true), HasFolds);
        Bind(UnfoldCurrent, () => FoldAtCaret(fold: false), HasFolds);
        Bind(FoldAll, () => FoldEvery(fold: true), HasFolds);
        Bind(UnfoldAll, () => FoldEvery(fold: false), HasFolds);
        Bind(ZoomIn, () => EditorDisplay.ZoomFactor = ZoomRules.Next(EditorDisplay.ZoomFactor), () => ZoomRules.CanZoomIn(EditorDisplay.ZoomFactor));
        Bind(ZoomOut, () => EditorDisplay.ZoomFactor = ZoomRules.Previous(EditorDisplay.ZoomFactor), () => ZoomRules.CanZoomOut(EditorDisplay.ZoomFactor));
        Bind(ResetZoom, () => EditorDisplay.ZoomFactor = ZoomRules.Default, () => ZoomRules.CanReset(EditorDisplay.ZoomFactor));
        Bind(SplitNone, () => SetSplit(SplitDirection.None));
        Bind(SplitLeftRight, () => SetSplit(SplitDirection.LeftRight));
        Bind(SplitTopBottom, () => SetSplit(SplitDirection.TopBottom));
        Bind(SwitchSplit, SwitchView, () => Split != SplitDirection.None);
        Bind(ApplicationCommands.Print, () => Print(selection: false));
        Bind(PrintSelection, () => Print(selection: true), () => Editor.SelectionLength > 0);
        // The other keys R2025b gives the zoom: Ctrl+= and Ctrl+Shift+= for in, the number pad's for all three.
        Editor.TextArea.InputBindings.Add(new KeyBinding(ZoomIn, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
        Editor.TextArea.InputBindings.Add(new KeyBinding(ZoomIn, Key.Add, ModifierKeys.Control));
        Editor.TextArea.InputBindings.Add(new KeyBinding(ZoomOut, Key.Subtract, ModifierKeys.Control));
        Editor.TextArea.InputBindings.Add(new KeyBinding(ResetZoom, Key.NumPad0, ModifierKeys.Control | ModifierKeys.Alt));
        Editor.PreviewMouseWheel += OnWheel;
        Editor2.PreviewMouseWheel += OnWheel;
        Loaded += (_, _) =>
        {
            EditorDisplay.Changed += OnDisplayChanged;
            ApplyDisplay();
        };
        Unloaded += (_, _) => EditorDisplay.Changed -= OnDisplayChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => OpenFoldsAtCaret(_folding, Editor);
        _refold.Tick += (_, _) =>
        {
            _refold.Stop();
            RefreshFolds();
        };
        Editor.TextChanged += (_, _) =>
        {
            if (_foldSource is not null)
            {
                _refold.Stop();
                _refold.Start();
            }
        };
        ApplyDisplay();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => ApplyDisplay();

    /// <summary>Ctrl+wheel zooms once the wheel has turned past R2025b's threshold (<c>_handleWheelGestureRecognized</c>).</summary>
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        // A browser's wheel delta is positive turning down; WPF's is positive turning up.
        _wheel -= e.Delta;
        int step = ZoomRules.WheelStep(ref _wheel);
        if (step > 0 && ZoomRules.CanZoomIn(EditorDisplay.ZoomFactor))
        {
            EditorDisplay.ZoomFactor = ZoomRules.Next(EditorDisplay.ZoomFactor);
        }
        else if (step < 0 && ZoomRules.CanZoomOut(EditorDisplay.ZoomFactor))
        {
            EditorDisplay.ZoomFactor = ZoomRules.Previous(EditorDisplay.ZoomFactor);
        }
    }

    /// <summary>The shared display settings put on both views.</summary>
    private void ApplyDisplay()
    {
        foreach (TextEditor editor in new[] { Editor, Editor2 })
        {
            editor.ShowLineNumbers = EditorDisplay.ShowLineNumbers;
            editor.Options.HighlightCurrentLine = EditorDisplay.HighlightCurrentLine;
            // R2025b's HighlightCurrentLineColor, [229 250 221], and no border.
            editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(229, 250, 221));
            editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
            if (EditorDisplay.ZoomFactor == ZoomRules.Default)
            {
                editor.SetResourceReference(FontSizeProperty, Themes.ThemeKeys.FontSizeCode);
            }
            else
            {
                double size = TryFindResource(Themes.ThemeKeys.FontSizeCode) is double code ? code : 13;
                editor.FontSize = size * EditorDisplay.ZoomFactor;
            }
        }

        ShowStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    // --- folding ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The blocks the editor can fold, each closed when it first appears if its first line is in <paramref name="closedByDefault"/> and
    /// left as the user has it after that; a fold the caret stands in is opened.
    /// </summary>
    public void SetFoldCandidates(IReadOnlyList<FoldCandidate> candidates, IReadOnlySet<int>? closedByDefault)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _candidates = candidates;
        _closedByDefault = closedByDefault ?? new HashSet<int>();
        _folding ??= FoldingManager.Install(Editor.TextArea);
        _folding.UpdateFoldings(NewFoldings(), -1);
        OpenFoldsAtCaret(_folding, Editor);
        if (_folding2 is not null)
        {
            _folding2.UpdateFoldings(NewFoldings(), -1);
            OpenFoldsAtCaret(_folding2, Editor2);
        }

        ShowStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    private List<NewFolding> NewFoldings()
    {
        var made = new List<NewFolding>(_candidates.Count);
        TextDocument document = Editor.Document;
        foreach (FoldCandidate c in _candidates)
        {
            if (c.First < 1 || c.Last > document.LineCount || c.Last <= c.First)
            {
                continue;
            }

            DocumentLine start = document.GetLineByNumber(c.First);
            made.Add(new NewFolding(start.EndOffset, document.GetLineByNumber(c.Last).EndOffset) { Name = " ...", DefaultClosed = _closedByDefault.Contains(c.First) });
        }

        return [.. made.OrderBy(static f => f.StartOffset).ThenByDescending(static f => f.EndOffset)];
    }

    private bool HasFolds() => _folding is { AllFoldings: var all } && all.Any();

    /// <summary>A fold closed over the caret (strictly past its first line) is opened (<c>FoldingController._handleCursorChanged</c>).</summary>
    private static void OpenFoldsAtCaret(FoldingManager? manager, TextEditor editor)
    {
        if (manager is null)
        {
            return;
        }

        int offset = editor.CaretOffset;
        foreach (FoldingSection section in manager.GetFoldingsContaining(offset))
        {
            if (section.IsFolded && offset > section.StartOffset)
            {
                section.IsFolded = false;
            }
        }
    }

    /// <summary>Collapse or Expand at the caret (<c>FoldingActions</c>): the innermost block holding the caret's line; Expand opens it and every block around it.</summary>
    private void FoldAtCaret(bool fold)
    {
        (FoldingManager? manager, TextEditor editor) = ActiveView();
        if (manager is null)
        {
            return;
        }

        int line = editor.TextArea.Caret.Line;
        TextDocument document = editor.Document;
        FoldingSection? innermost = null;
        foreach (FoldingSection section in manager.AllFoldings)
        {
            int first = document.GetLineByOffset(section.StartOffset).LineNumber;
            int last = document.GetLineByOffset(section.EndOffset).LineNumber;
            if (line >= first && line <= last && (innermost is null || section.StartOffset > innermost.StartOffset))
            {
                innermost = section;
            }
        }

        if (innermost is null)
        {
            return;
        }

        if (fold)
        {
            innermost.IsFolded = true;
            if (editor.CaretOffset > innermost.StartOffset)
            {
                editor.CaretOffset = BlockStart(document, innermost);
            }
        }
        else
        {
            foreach (FoldingSection section in manager.GetFoldingsContaining(innermost.StartOffset).Append(innermost))
            {
                section.IsFolded = false;
            }
        }

        ShowStatus();
    }

    /// <summary>Collapse All or Expand All (<c>foldAll</c>, <c>unfoldAll</c>); the caret goes to the start of the innermost fold that took it.</summary>
    private void FoldEvery(bool fold)
    {
        (FoldingManager? manager, TextEditor editor) = ActiveView();
        if (manager is null)
        {
            return;
        }

        FoldingSection? holder = null;
        foreach (FoldingSection section in manager.AllFoldings)
        {
            section.IsFolded = fold;
            if (fold && editor.CaretOffset > section.StartOffset && editor.CaretOffset <= section.EndOffset && (holder is null || section.StartOffset > holder.StartOffset))
            {
                holder = section;
            }
        }

        if (holder is not null)
        {
            editor.CaretOffset = BlockStart(editor.Document, holder);
        }

        ShowStatus();
    }

    /// <summary>Where a fold's candidate starts (<c>getStartPosition</c>): the first character of its first line's text, past the indent.</summary>
    private static int BlockStart(TextDocument document, FoldingSection section)
    {
        DocumentLine line = document.GetLineByOffset(section.StartOffset);
        string text = document.GetText(line);
        return line.Offset + (text.Length - text.TrimStart().Length);
    }

    private (FoldingManager? Manager, TextEditor Editor) ActiveView() =>
        _secondaryReady && Editor2.TextArea.IsKeyboardFocusWithin ? (_folding2, Editor2) : (_folding, Editor);

    // --- the split document ----------------------------------------------------------------------------------------------

    /// <summary>Shows the document in one view or two (<c>SplitScreen._doSplit</c>): the new view takes the old one's place and caret; closing the split gives the second view's place back to the first when it had the keyboard.</summary>
    public void SetSplit(SplitDirection direction)
    {
        if (direction == Split)
        {
            return;
        }

        SplitDirection was = Split;
        bool secondaryHadFocus = _secondaryReady && Editor2.TextArea.IsKeyboardFocusWithin;
        Split = direction;
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();
        Grid.SetRow(Editor, 0);
        Grid.SetColumn(Editor, 0);
        Grid.SetRow(StripHost, 0);
        Grid.SetColumn(StripHost, 0);
        if (direction == SplitDirection.None)
        {
            Splitter.Visibility = Visibility.Collapsed;
            Editor2.Visibility = Visibility.Collapsed;
            if (secondaryHadFocus)
            {
                Editor.ScrollToVerticalOffset(Editor2.VerticalOffset);
                Editor.TextArea.Caret.Position = Editor2.TextArea.Caret.Position;
            }

            FocusEditor();
        }
        else
        {
            PrepareSecondary();
            if (direction == SplitDirection.LeftRight)
            {
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
                Splitter.Width = 4;
                Splitter.Height = double.NaN;
                Splitter.HorizontalAlignment = HorizontalAlignment.Center;
                Splitter.VerticalAlignment = VerticalAlignment.Stretch;
                Splitter.ResizeDirection = GridResizeDirection.Columns;
                Grid.SetColumn(Splitter, 1);
                Grid.SetRow(Splitter, 0);
                Grid.SetColumn(Editor2, 2);
                Grid.SetRow(Editor2, 0);
            }
            else
            {
                Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
                Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
                Splitter.Height = 4;
                Splitter.Width = double.NaN;
                Splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                Splitter.VerticalAlignment = VerticalAlignment.Center;
                Splitter.ResizeDirection = GridResizeDirection.Rows;
                Grid.SetRow(Splitter, 1);
                Grid.SetColumn(Splitter, 0);
                Grid.SetRow(Editor2, 2);
                Grid.SetColumn(Editor2, 0);
            }

            Splitter.Visibility = Visibility.Visible;
            Editor2.Visibility = Visibility.Visible;
            if (was == SplitDirection.None)
            {
                // The new view mirrors the first: its first visible line, its scroll and its caret (_getDataToMirror).
                Editor2.ScrollToVerticalOffset(Editor.VerticalOffset);
                Editor2.ScrollToHorizontalOffset(Editor.HorizontalOffset);
                Editor2.TextArea.Caret.Position = Editor.TextArea.Caret.Position;
            }
        }

        ShowStatus();
        SplitChanged?.Invoke(this, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>The second view over the same document, made once: the same text, highlighting, read-only lines and folds.</summary>
    private void PrepareSecondary()
    {
        if (_secondaryReady)
        {
            return;
        }

        _secondaryReady = true;
        Editor2.Document = Editor.Document;
        Editor2.SyntaxHighlighting = Editor.SyntaxHighlighting;
        Editor2.Options.ConvertTabsToSpaces = true;
        Editor2.Options.IndentationSize = EditorTabs.IndentSize;
        Editor2.TextArea.ReadOnlySectionProvider = _readOnlyLines;
        var shade = new GeneratedCodeRenderer { Enabled = _generatedCodeRenderer.Enabled, ShadeBrush = GeneratedCodeBrush };
        Editor2.TextArea.TextView.BackgroundRenderers.Add(shade);
        Editor2.TextChanged += (_, _) => shade.Invalidate();
        _folding2 = FoldingManager.Install(Editor2.TextArea);
        _folding2.UpdateFoldings(NewFoldings(), -1);
        Editor2.TextArea.Caret.PositionChanged += (_, _) =>
        {
            OpenFoldsAtCaret(_folding2, Editor2);
            ShowStatus();
        };
        ApplyDisplay();
    }

    /// <summary>F6: the keyboard to the other view (<c>_switchView</c>).</summary>
    private void SwitchView()
    {
        if (Split == SplitDirection.None)
        {
            return;
        }

        if (Editor2.TextArea.IsKeyboardFocusWithin)
        {
            Editor.Focus();
        }
        else
        {
            Editor2.Focus();
        }
    }

    // --- Print ----------------------------------------------------------------------------------------------------------

    /// <summary>Print or Print Selection (<c>rtc/plugins/print/Print</c>): the text, or the selection's lines, in the editor's font and colours through WPF's print dialog.</summary>
    private void Print(bool selection)
    {
        TextDocument document = Editor.Document;
        int first = 1;
        int last = document.LineCount;
        if (selection && Editor.SelectionLength > 0)
        {
            first = document.GetLineByOffset(Editor.SelectionStart).LineNumber;
            last = document.GetLineByOffset(Editor.SelectionStart + Editor.SelectionLength).LineNumber;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FlowDocument flow = PrintDocumentFor(first, last);
        flow.PageWidth = dialog.PrintableAreaWidth;
        flow.PageHeight = dialog.PrintableAreaHeight;
        dialog.PrintDocument(((IDocumentPaginatorSource)flow).DocumentPaginator, DocumentTitle ?? "JGraph");
    }

    /// <summary>The lines <paramref name="first"/> to <paramref name="last"/> as a flow document, each line a paragraph of the highlighter's runs.</summary>
    public FlowDocument PrintDocumentFor(int first, int last)
    {
        TextDocument document = Editor.Document;
        var flow = new FlowDocument
        {
            FontFamily = Editor.FontFamily,
            FontSize = 10,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity,
            Foreground = Brushes.Black,
            Background = Brushes.White,
        };
        DocumentHighlighter? highlighter = Editor.SyntaxHighlighting is { } definition ? new DocumentHighlighter(document, definition) : null;
        try
        {
            for (int line = Math.Max(1, first); line <= Math.Min(last, document.LineCount); line++)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0), TextIndent = 0 };
                if (highlighter is not null)
                {
                    foreach (Run run in highlighter.HighlightLine(line).ToRichText().CreateRuns())
                    {
                        paragraph.Inlines.Add(run);
                    }
                }
                else
                {
                    paragraph.Inlines.Add(new Run(document.GetText(document.GetLineByNumber(line))));
                }

                flow.Blocks.Add(paragraph);
            }
        }
        finally
        {
            highlighter?.Dispose();
        }

        return flow;
    }

    /// <summary>What the view tools add to the editor's <c>ItemStatus</c>: the folds closed of those there are, the zoom when not 100%, the split.</summary>
    private string ViewStatus()
    {
        string status = string.Empty;
        if (_folding is { } folding)
        {
            int total = folding.AllFoldings.Count();
            if (total > 0)
            {
                status += $"; Folds {folding.AllFoldings.Count(static f => f.IsFolded)}/{total}";
            }
        }

        if (EditorDisplay.ZoomFactor != ZoomRules.Default)
        {
            status += $"; Zoom {Math.Round(EditorDisplay.ZoomFactor * 100)}%";
        }

        if (Split != SplitDirection.None)
        {
            status += "; Split " + (Split == SplitDirection.LeftRight ? "Left/Right" : "Top/Bottom");
        }

        // The display settings only where they differ from R2025b's defaults.
        if (!EditorDisplay.ShowLineNumbers)
        {
            status += "; Line numbers off";
        }

        if (EditorDisplay.HighlightCurrentLine)
        {
            status += "; Current line on";
        }

        if (EditorDisplay.Datatips)
        {
            status += "; Datatips on";
        }

        return status;
    }
}
