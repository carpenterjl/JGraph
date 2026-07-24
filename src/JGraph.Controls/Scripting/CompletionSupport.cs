using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using JGraph.Scripting.Completion;
using JGraph.Scripting.Jgs.Completion;

namespace JGraph.Controls.Scripting;

/// <summary>
/// Wires code completion into one AvalonEdit editor. JGS gets the smart treatment — context-aware
/// completions from <see cref="JgsCompletionEngine"/> (auto-triggered while typing an identifier, or
/// Ctrl+Space), and signature help with the active parameter bold when typing <c>(</c> or <c>,</c> —
/// while C# and Python get the curated <see cref="WordListCompletion"/> lists. Selecting a function
/// inserts <c>name(p1, p2)</c> with the first parameter selected, ready to overtype.
/// </summary>
internal sealed class CompletionSupport
{
    private readonly TextEditor _editor;
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _insightWindow;

    public CompletionSupport(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextEntering += OnTextEntering;
        editor.TextArea.TextEntered += OnTextEntered;

        // handledEventsToo: a docking host may mark Escape handled at the window level before the
        // tunnel reaches the editor; the tooltip must still close on it.
        editor.TextArea.AddHandler(
            System.Windows.UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);

        // An open signature tooltip must track the caret: the active parameter advances and retreats,
        // and the tooltip goes away once the caret leaves the call (arrow keys, clicks, deletions).
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_insightWindow is not null)
            {
                UpdateSignatureHelp(openIfClosed: false);
            }
        };
    }

    private string? _insightName;
    private int _insightActive;

    /// <summary>The document's language ("JGS", "MATLAB", "C#", "Python"); anything else disables completion.</summary>
    public string? Language { get; set; }

    /// <summary>Whether this document runs on our own interpreter, which is what powers builtin
    /// completion and signature help. Both of its dialects qualify.</summary>
    private bool UsesOurInterpreter => Language == JgsSyntax.Name || Language == MatlabSyntax.Name;

    /// <summary>Whether the buffer is MATLAB, which is lexed by MATLAB's rules.</summary>
    private bool IsMatlab => Language == MatlabSyntax.Name;

    /// <summary>Supplies symbols from the rest of the workspace (JGS only): <c>fn</c>s defined in other
    /// scripts, harvested by the host via <see cref="JgsCompletionEngine.HarvestFunctions"/>.</summary>
    public Func<IReadOnlyList<CompletionItem>>? WorkspaceSymbols { get; set; }

    /// <summary>Supplies the workspace's files and folders for path completion inside the string
    /// arguments of the file builtins (<c>readcsv("…</c>). Null when no workspace is open.</summary>
    public Func<IReadOnlyList<WorkspaceFileEntry>>? WorkspaceFiles { get; set; }

    /// <summary>Whether the open completion window lists workspace paths (different close rules:
    /// <c>.</c>/<c>-</c>/digits keep filtering; quotes, separators, and whitespace end it).</summary>
    private bool _completionIsPath;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control && !e.Handled)
        {
            e.Handled = true;
            ShowCompletion();
            return;
        }

        if (e.Key == Key.Escape)
        {
            _insightWindow?.Close(); // not Handled: Escape still reaches an open completion window etc.
        }
    }

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        // The open completion list only makes sense while its filter text grows. Close it BEFORE any
        // other character inserts — without this the window lingers as an empty shell (with its
        // description tooltip) and swallows keys. Enter/Tab commit through the list itself and never
        // reach here. Path lists accept filename characters ('.', '-', …) and end at quotes,
        // separators, or whitespace; identifier lists end at any non-identifier character.
        if (_completionWindow is not null && e.Text.Length > 0)
        {
            char typed = e.Text[0];
            bool ends = _completionIsPath
                ? typed is '"' or '\'' or '/' or '\\' or ')' || char.IsWhiteSpace(typed)
                : !char.IsLetterOrDigit(typed) && typed != '_';
            if (ends)
            {
                _completionWindow.Close();
            }
        }
    }

    private void OnTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 0)
        {
            return;
        }

        char typed = e.Text[^1];
        if (UsesOurInterpreter && typed is '(' or ',')
        {
            UpdateSignatureHelp(openIfClosed: true);
            return;
        }

        // Auto-trigger while an identifier is being typed; once open, AvalonEdit filters as keys arrive.
        // Quotes, '/', and digits also attempt — ShowCompletion's path detection decides whether they
        // mean anything (a quote right after readcsv( opens the file list; elsewhere nothing happens).
        if (_completionWindow is null
            && (char.IsLetter(typed) || typed is '_' || typed is '"' or '\'' or '/' || char.IsDigit(typed)))
        {
            ShowCompletion();
        }
    }

    private void ShowCompletion()
    {
        int offset = _editor.CaretOffset;
        int replaceStart;
        IReadOnlyList<CompletionItem> items;
        bool isPath = false;

        if (Language is string pathLanguage
            && WorkspaceFiles is not null
            && PathCompletion.Detect(_editor.Text, offset, pathLanguage) is { } pathContext)
        {
            replaceStart = pathContext.ReplaceStart;
            items = PathCompletion.GetCompletions(pathContext, WorkspaceFiles());
            isPath = true;
        }
        else if (UsesOurInterpreter)
        {
            JgsCompletionResult result = JgsCompletionEngine.GetCompletions(
                _editor.Text, offset, WorkspaceSymbols?.Invoke(), IsMatlab);
            replaceStart = result.ReplaceStart;
            items = result.Items;
        }
        else if (Language is string language && WordListCompletion.ForLanguage(language) is { } words)
        {
            replaceStart = offset;
            while (replaceStart > 0 && (char.IsLetterOrDigit(_editor.Text[replaceStart - 1]) || _editor.Text[replaceStart - 1] == '_'))
            {
                replaceStart--;
            }

            string wordPrefix = _editor.Text[replaceStart..offset];
            items = words.Where(w => w.Text.StartsWith(wordPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            return;
        }

        if (items.Count == 0)
        {
            return;
        }

        var window = new CompletionWindow(_editor.TextArea) { StartOffset = replaceStart };
        bool insertCallTemplates = !isPath && UsesOurInterpreter;
        foreach (CompletionItem item in items)
        {
            window.CompletionList.CompletionData.Add(new CompletionData(item, insertCallTemplates));
        }

        window.Closed += (_, _) =>
        {
            _completionWindow = null;
            _completionIsPath = false;
        };
        _completionWindow = window;
        _completionIsPath = isPath;
        window.Show();

        string prefix = _editor.Document.GetText(replaceStart, offset - replaceStart);
        if (prefix.Length > 0)
        {
            window.CompletionList.SelectItem(prefix);
        }
        else
        {
            // No prefix yet (Ctrl+Space, or a fresh "readcsv(" list): preselect the first item so
            // Enter/Tab commit immediately.
            window.CompletionList.SelectedItem = window.CompletionList.CompletionData[0];
        }
    }

    /// <summary>Recomputes signature help at the caret and reconciles the tooltip: closes it outside any
    /// known call, leaves it alone when nothing changed, and (re)creates it when the call or the active
    /// parameter changed. Only the explicit triggers ('(' and ',') may open a closed tooltip.</summary>
    private void UpdateSignatureHelp(bool openIfClosed)
    {
        JgsSignatureHelp? help = UsesOurInterpreter
            ? JgsCompletionEngine.GetSignatureHelp(
                _editor.Text, _editor.CaretOffset, WorkspaceSymbols?.Invoke(), IsMatlab)
            : null;
        if (help is null)
        {
            _insightWindow?.Close();
            return;
        }

        if (_insightWindow is not null && help.Name == _insightName && help.ActiveParameter == _insightActive)
        {
            return;
        }

        if (_insightWindow is null && !openIfClosed)
        {
            return;
        }

        _insightWindow?.Close();
        _insightName = help.Name;
        _insightActive = help.ActiveParameter;

        var header = new TextBlock { FontFamily = _editor.FontFamily };
        header.Inlines.Add(new Run(help.Name + "("));
        for (int i = 0; i < help.ParameterLabels.Count; i++)
        {
            if (i > 0)
            {
                header.Inlines.Add(new Run(", "));
            }

            var label = new Run(help.ParameterLabels[i]);
            if (i == help.ActiveParameter)
            {
                label.FontWeight = System.Windows.FontWeights.Bold;
            }

            header.Inlines.Add(label);
        }

        header.Inlines.Add(new Run(")"));

        var window = new OverloadInsightWindow(_editor.TextArea)
        {
            Provider = new SingleOverloadProvider(header, string.IsNullOrEmpty(help.Summary) ? null : help.Summary),
        };
        // Guarded: a Closed event from a superseded window must not null the live reference.
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_insightWindow, window))
            {
                _insightWindow = null;
            }
        };
        _insightWindow = window;
        window.Show();
    }

    /// <summary>One completion entry. For JGS functions with a known signature, completing inserts a call
    /// template (<c>name(p1, p2)</c> — required parameters only) and selects the first parameter.</summary>
    private sealed class CompletionData : ICompletionData
    {
        private readonly CompletionItem _item;
        private readonly bool _insertCallTemplate;

        public CompletionData(CompletionItem item, bool insertCallTemplate)
        {
            _item = item;
            _insertCallTemplate = insertCallTemplate;
        }

        public System.Windows.Media.ImageSource? Image => null;

        public string Text => _item.Text;

        public object Content => _item.Text;

        public object? Description => (_item.Signature, _item.Description) switch
        {
            (string signature, string description) => signature + "\n" + description,
            (string signature, null) => signature,
            (null, string description) => description,
            _ => null,
        };

        public double Priority => _item.Kind switch
        {
            // Local symbols first, then the library, then keywords.
            CompletionItemKind.Variable or CompletionItemKind.Function => 2,
            CompletionItemKind.Builtin => 1,
            _ => 0,
        };

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            if (!_insertCallTemplate || _item.Signature is not string signature ||
                _item.Kind is not (CompletionItemKind.Builtin or CompletionItemKind.Function))
            {
                textArea.Document.Replace(completionSegment, _item.Text);
                return;
            }

            IReadOnlyList<string> labels = JgsCompletionEngine.ParameterLabels(signature);
            var required = labels.Where(static l => !l.EndsWith('?')).ToArray();
            string insertion = $"{_item.Text}({string.Join(", ", required)})";
            int start = completionSegment.Offset;
            textArea.Document.Replace(completionSegment, insertion);

            if (required.Length > 0)
            {
                // Select the first parameter placeholder, ready to overtype.
                int parameterStart = start + _item.Text.Length + 1;
                textArea.Selection = Selection.Create(textArea, parameterStart, parameterStart + required[0].Length);
                textArea.Caret.Offset = parameterStart + required[0].Length;
            }
            else if (labels.Count > 0)
            {
                textArea.Caret.Offset = start + _item.Text.Length + 1; // inside the parens: optional args
            }
            else
            {
                textArea.Caret.Offset = start + insertion.Length; // after "()": a no-argument call
            }
        }
    }

    /// <summary>An <see cref="IOverloadProvider"/> over the single JGS signature (JGS has no overload sets;
    /// alternate call shapes are described in the summary).</summary>
    private sealed class SingleOverloadProvider : IOverloadProvider
    {
        private readonly object _header;
        private readonly object? _content;

        public SingleOverloadProvider(object header, object? content)
        {
            _header = header;
            _content = content;
        }

        // The single overload never changes, so no notifications are ever raised.
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public int SelectedIndex
        {
            get => 0;
            set { }
        }

        public int Count => 1;

        public string CurrentIndexText => string.Empty;

        public object CurrentHeader => _header;

        public object? CurrentContent => _content;
    }
}
