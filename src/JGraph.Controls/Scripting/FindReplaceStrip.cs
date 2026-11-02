using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using JGraph.Controls.Themes;
using JGraph.Scripting.Editing;
using TextSearch = JGraph.Scripting.Editing.TextSearch;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The editor's Find and Replace strip (App Designer window plan W9a, R2025b's <c>findandreplace-ui/FindStrip</c> over
/// <c>rtc/plugins/find</c>): a strip at the top right of the text with the find box and its result count, Find previous and Find next,
/// Match case, Whole word, Regular expression and Find in selection, and behind its arrow the replace box with Replace and Replace All.
/// Typing finds from the current occurrence, Enter finds the next and Shift+Enter the previous, Down opens the history, Esc closes; the
/// options and both histories are the session's, kept across the strips of every editor as R2025b's <c>FindPreference</c> keeps them.
/// Replace All passes over an occurrence on a line that may not be edited (<c>isCursorReadOnly</c>).
/// </summary>
internal sealed class FindReplaceStrip : Border
{
    private const double StripWidth = 450;
    private static readonly TimeSpan TypingThreshold = TimeSpan.FromMilliseconds(300);

    // FindPreference's state, one for the session.
    private static readonly FindHistory FindTexts = new();
    private static readonly FindHistory ReplaceTexts = new();
    private static bool _matchCase;
    private static bool _wholeWord;
    private static bool _regex;
    private static bool _replaceOpen;
    private static string _lastSearchedText = string.Empty;
    private static bool _lastMatchCase;
    private static bool _lastWholeWord;
    private static bool _lastRegex;

    private readonly TextEditor _editor;
    private readonly Func<int, bool> _canEdit;
    private readonly TextBox _findBox = new();
    private readonly TextBox _replaceBox = new();
    private readonly TextBlock _result = new();
    private readonly TextBlock _message = new();
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _replaceOne;
    private readonly Button _replaceAll;
    private readonly ToggleButton _toggleReplace;
    private readonly ToggleButton _matchCaseButton;
    private readonly ToggleButton _wholeWordButton;
    private readonly ToggleButton _regexButton;
    private readonly ToggleButton _inSelectionButton;
    private readonly Grid _replaceRow;
    private readonly DispatcherTimer _typing = new() { Interval = TypingThreshold };
    private SearchResult? _resultCache;
    private bool _settingSelection;
    private bool _hasResults = true;
    private int _selectionFirst = -1;
    private int _selectionLast = -1;
    private bool _showing;

    public FindReplaceStrip(TextEditor editor, Func<int, bool> canEdit)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(canEdit);
        _editor = editor;
        _canEdit = canEdit;
        Width = StripWidth;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(4);
        Visibility = Visibility.Collapsed;
        SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        SetResourceReference(BorderBrushProperty, ThemeKeys.BorderStrong);
        AutomationProperties.SetAutomationId(this, "Find");

        var rows = new StackPanel();
        var findRow = new Grid();
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 7; i++)
        {
            findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        // A toggle's state changes by a click or by UI Automation's Toggle, which raises no Click: the Checked and Unchecked events serve both.
        _toggleReplace = Toggle("▸", "Find.ToggleReplace", "Show replace options");
        _toggleReplace.Checked += (_, _) => ReplaceToggled();
        _toggleReplace.Unchecked += (_, _) => ReplaceToggled();
        findRow.Children.Add(_toggleReplace);

        var findHost = new Grid { Margin = new Thickness(2, 0, 2, 0) };
        _findBox.VerticalContentAlignment = VerticalAlignment.Center;
        _findBox.Padding = new Thickness(2, 0, 70, 0);
        AutomationProperties.SetAutomationId(_findBox, "Find.Text");
        AutomationProperties.SetName(_findBox, "Find");
        findHost.Children.Add(_findBox);
        var placeholder = new TextBlock { Text = "(↓ for history)", Opacity = 0.6, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        findHost.Children.Add(placeholder);
        _result.HorizontalAlignment = HorizontalAlignment.Right;
        _result.VerticalAlignment = VerticalAlignment.Center;
        _result.Margin = new Thickness(0, 0, 24, 0);
        _result.IsHitTestVisible = false;
        _result.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        AutomationProperties.SetAutomationId(_result, "Find.Result");
        findHost.Children.Add(_result);
        var clear = new Button { Content = "×", Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0), ToolTip = "Clear text", Focusable = false };
        AutomationProperties.SetAutomationId(clear, "Find.Clear");
        clear.Click += (_, _) => ClearFindText();
        findHost.Children.Add(clear);
        Grid.SetColumn(findHost, 1);
        findRow.Children.Add(findHost);
        _findBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _findBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_showing)
            {
                OnFindTyped();
            }
        };
        _findBox.LostKeyboardFocus += (_, _) =>
        {
            if (_typing.IsEnabled)
            {
                _typing.Stop();
                PutInHistory();
            }
        };

        _previous = Push("‹", "Find.Previous", "Find previous");
        _previous.Click += (_, _) => FindFromStrip(backward: true);
        _next = Push("›", "Find.Next", "Find next");
        _next.Click += (_, _) => FindFromStrip(backward: false);
        _matchCaseButton = Toggle("Aa", "Find.MatchCase", "Match case");
        _wholeWordButton = Toggle("ab", "Find.WholeWord", "Whole word");
        _regexButton = Toggle(".*", "Find.Regex", "Regular expression");
        foreach (ToggleButton option in new[] { _matchCaseButton, _wholeWordButton, _regexButton })
        {
            option.Checked += (_, _) => OptionChanged();
            option.Unchecked += (_, _) => OptionChanged();
        }

        _inSelectionButton = Toggle("☰", "Find.InSelection", "Find in selection");
        _inSelectionButton.Checked += (_, _) => InSelectionChanged();
        _inSelectionButton.Unchecked += (_, _) => InSelectionChanged();
        var close = Push("×", "Find.Close", "Close");
        close.Click += (_, _) => Close();
        int column = 2;
        foreach (ButtonBase button in new ButtonBase[] { _previous, _next, _matchCaseButton, _wholeWordButton, _regexButton, _inSelectionButton, close })
        {
            Grid.SetColumn(button, column++);
            findRow.Children.Add(button);
        }

        rows.Children.Add(findRow);

        _replaceRow = new Grid { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var spacer = new Border { Width = 22 };
        _replaceRow.Children.Add(spacer);
        _replaceBox.VerticalContentAlignment = VerticalAlignment.Center;
        _replaceBox.Margin = new Thickness(2, 0, 2, 0);
        AutomationProperties.SetAutomationId(_replaceBox, "Find.ReplaceText");
        AutomationProperties.SetName(_replaceBox, "Replace");
        Grid.SetColumn(_replaceBox, 1);
        _replaceRow.Children.Add(_replaceBox);
        _replaceOne = new Button { Content = "Replace", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(2, 0, 0, 0), ToolTip = "Replace the selected instance" };
        AutomationProperties.SetAutomationId(_replaceOne, "Find.Replace");
        _replaceOne.Click += (_, _) => ReplaceOne(backward: false);
        Grid.SetColumn(_replaceOne, 2);
        _replaceRow.Children.Add(_replaceOne);
        _replaceAll = new Button { Content = "Replace All", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(2, 0, 0, 0), ToolTip = "Replace all instances" };
        AutomationProperties.SetAutomationId(_replaceAll, "Find.ReplaceAll");
        _replaceAll.Click += (_, _) => ReplaceAll();
        Grid.SetColumn(_replaceAll, 3);
        _replaceRow.Children.Add(_replaceAll);
        rows.Children.Add(_replaceRow);

        _message.Margin = new Thickness(24, 2, 0, 0);
        _message.TextWrapping = TextWrapping.Wrap;
        _message.Visibility = Visibility.Collapsed;
        _message.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        AutomationProperties.SetAutomationId(_message, "Find.Message");
        rows.Children.Add(_message);
        Child = rows;

        _typing.Tick += (_, _) =>
        {
            _typing.Stop();
            PutInHistory();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            // The caret moved by itself: the next search starts from it (FindReplaceActions._handleCursorChange).
            if (!_settingSelection && _resultCache is { HasCurrent: true } && _editor.SelectionLength == 0)
            {
                _resultCache.ClearCurrent();
            }
        };
        _editor.Document.Changed += (_, _) => _resultCache = null;
        _editor.DocumentChanged += (_, _) =>
        {
            _resultCache = null;
            _editor.Document.Changed += (_, _) => _resultCache = null;
        };
    }

    /// <summary>Raised when the strip closes, so the host gives the keyboard back to the text.</summary>
    public event EventHandler? Closed;

    /// <summary>Whether the strip is shown.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether the strip has the keyboard.</summary>
    public bool HasFocus => IsKeyboardFocusWithin;

    /// <summary>
    /// Opens the strip (Ctrl+F; <paramref name="withReplace"/> for Ctrl+H), seeded with the selection's first line - with only the count shown
    /// when the selection spans lines - or else the last text searched (<c>FindStrip.initialize</c>). Open already, it is seeded again.
    /// </summary>
    public void Open(bool withReplace)
    {
        bool wasOpen = IsOpen;
        string selected = wasOpen && !_editor.TextArea.IsKeyboardFocusWithin ? string.Empty : _editor.SelectedText;
        bool countOnly = selected.Contains('\n') && (!wasOpen || selected.Length > 0);
        string seed = TextSearch.FirstNonBlankLine(selected);
        if (seed.Length == 0)
        {
            seed = FindTexts.Latest;
        }

        if (withReplace || (wasOpen && _replaceOpen))
        {
            _replaceOpen = true;
        }
        else if (!wasOpen)
        {
            _replaceOpen = false;
        }

        _showing = true;
        try
        {
            _findBox.Text = seed;
            _replaceBox.Text = ReplaceTexts.Latest;
            _matchCaseButton.IsChecked = _matchCase;
            _wholeWordButton.IsChecked = _wholeWord;
            _regexButton.IsChecked = _regex;
            _inSelectionButton.IsChecked = false;
            _selectionFirst = _selectionLast = -1;
            ToggleReplaceStrip(toSaved: true);
            Visibility = Visibility.Visible;
            _message.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _showing = false;
        }

        if (selected.Length > 0)
        {
            PutInHistory();
        }

        _lastSearchedText = _findBox.Text;
        _lastMatchCase = _matchCase;
        _lastWholeWord = _wholeWord;
        _lastRegex = _regex;
        EnableButtons();
        if (countOnly)
        {
            Find(new FindRequest { CountOnly = true });
        }
        else
        {
            Find(new FindRequest { Text = _findBox.Text, FromCurrentOccurrence = true });
        }

        (_replaceOpen && withReplace ? _replaceBox : _findBox).Focus();
        (_replaceOpen && withReplace ? _replaceBox : _findBox).SelectAll();
    }

    /// <summary>Closes the strip; the text takes the keyboard again.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        PutInHistory();
        Visibility = Visibility.Collapsed;
        _selectionFirst = _selectionLast = -1;
        _inSelectionButton.IsChecked = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Find Next (F3) or Find Previous (Shift+F3): the last text searched with its options, strip open or not (<c>keyFind</c>).</summary>
    public bool FindAgain(bool backward)
    {
        if (_lastSearchedText.Length == 0)
        {
            return false;
        }

        Find(new FindRequest { Text = _lastSearchedText, Backward = backward, MatchCase = _lastMatchCase, WholeWord = _lastWholeWord, Regex = _lastRegex });
        return true;
    }

    /// <summary>Find Selection (Ctrl+F3): the selection's first line, searched plainly unless the strip is open with options (<c>selectionKey</c>).</summary>
    public bool FindSelection()
    {
        string text = TextSearch.FirstNonBlankLine(_editor.SelectedText);
        if (text.Length == 0)
        {
            return false;
        }

        FindTexts.Add(text);
        if (IsOpen)
        {
            _showing = true;
            _findBox.Text = text;
            _showing = false;
            Find(new FindRequest { Text = text });
        }
        else
        {
            Find(new FindRequest { Text = text, MatchCase = false, WholeWord = false, Regex = false });
        }

        return true;
    }

    private sealed class FindRequest
    {
        public string? Text { get; init; }
        public bool Backward { get; init; }
        public bool FromCurrentOccurrence { get; init; }
        public bool CountOnly { get; init; }
        public bool? MatchCase { get; init; }
        public bool? WholeWord { get; init; }
        public bool? Regex { get; init; }
    }

    private IReadOnlyList<string> Lines()
    {
        TextDocument document = _editor.Document;
        var lines = new string[document.LineCount];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = document.GetText(document.GetLineByNumber(i + 1));
        }

        return lines;
    }

    /// <summary><c>FindReplaceActions.find</c> and <c>_findOnce</c>: the search from where it should start, its occurrence selected and scrolled to, the label said.</summary>
    private SearchResult? Find(FindRequest request)
    {
        string text = request.Text ?? FindTexts.Latest;
        bool matchCase = request.MatchCase ?? _matchCase;
        bool wholeWord = request.WholeWord ?? _wholeWord;
        bool regex = request.Regex ?? _regex;
        if (regex && TextSearch.WhyNotRegex(text) is { } why)
        {
            SayMessage(why);
            _resultCache = null;
            ShowLabel(TextSearch.NoResult, 0);
            return null;
        }

        SayMessage(null);
        (int line, int column) = StartFor(request, text, matchCase);
        var options = new SearchOptions
        {
            Text = text,
            MatchCase = matchCase,
            WholeWord = wholeWord,
            Regex = regex,
            Backward = request.Backward,
            StartLine = line,
            StartColumn = column,
            SelectionFirstLine = _selectionFirst,
            SelectionLastLine = _selectionLast,
        };
        SearchResult result = TextSearch.Find(Lines(), options, wrapAround: true, _resultCache);
        _resultCache = result;
        _lastSearchedText = text;
        _lastMatchCase = matchCase;
        _lastWholeWord = wholeWord;
        _lastRegex = regex;
        if (!request.CountOnly && result.Current is { } match)
        {
            SelectMatch(match);
        }

        ShowLabel(result.CurrentIndex, result.Matches.Count);
        return result;
    }

    /// <summary><c>_setStartAtInFindObject</c>: from the selection's start when asked to go on from the current occurrence, else from the caret - or nowhere in particular when the last occurrence is still current.</summary>
    private (int Line, int Column) StartFor(FindRequest request, string text, bool matchCase)
    {
        TextDocument document = _editor.Document;
        if (request.FromCurrentOccurrence)
        {
            _resultCache?.ClearCurrent();
            if (_editor.SelectionLength > 0)
            {
                TextLocation start = document.GetLocation(_editor.SelectionStart);
                return (start.Line - 1, start.Column - 1);
            }

            return StartAsPerCaret(text, matchCase);
        }

        TextLocation caret = _editor.TextArea.Caret.Location;
        return (caret.Line - 1, caret.Column - 1);
    }

    /// <summary><c>_setStartAtInFindObjectAsPerCursor</c>: while typing, the search starts far enough back that what was found so far stays found.</summary>
    private (int Line, int Column) StartAsPerCaret(string text, bool matchCase)
    {
        TextLocation caret = _editor.TextArea.Caret.Location;
        int line = caret.Line - 1;
        int column = caret.Column - 1;
        if (column >= text.Length)
        {
            column -= text.Length;
        }

        string lineText = _editor.Document.GetText(_editor.Document.GetLineByNumber(caret.Line));
        string needle = text;
        if (!matchCase)
        {
            lineText = lineText.ToLowerInvariant();
            needle = needle.ToLowerInvariant();
        }

        int at = needle.Length == 0 ? -1 : lineText.IndexOf(needle, Math.Min(column, lineText.Length), StringComparison.Ordinal);
        if (at == -1 && needle.Length > 0)
        {
            at = lineText.LastIndexOf(needle, StringComparison.Ordinal);
        }

        return (line, at >= 0 ? at : column);
    }

    private void SelectMatch(SearchMatch match)
    {
        TextDocument document = _editor.Document;
        if (match.Line < 0 || match.Line >= document.LineCount)
        {
            return;
        }

        DocumentLine line = document.GetLineByNumber(match.Line + 1);
        int start = line.Offset + Math.Min(match.StartColumn, line.Length);
        int end = line.Offset + Math.Min(match.EndColumn, line.Length);
        _settingSelection = true;
        try
        {
            _editor.Select(start, end - start);
            _editor.TextArea.Caret.Offset = end;
            ScrollTo(match.Line + 1);
        }
        finally
        {
            _settingSelection = false;
        }
    }

    /// <summary>A line not in view is brought to the middle (<c>scrollNearestVisibleLineToPercent(line, 50)</c>).</summary>
    private void ScrollTo(int line)
    {
        ICSharpCode.AvalonEdit.Rendering.TextView view = _editor.TextArea.TextView;
        view.EnsureVisualLines();
        DocumentLine documentLine = _editor.Document.GetLineByNumber(line);
        double top = view.GetVisualTopByDocumentLine(line);
        double bottom = top + view.DefaultLineHeight;
        if (top < view.VerticalOffset || bottom > view.VerticalOffset + view.ActualHeight)
        {
            _editor.ScrollToVerticalOffset(Math.Max(0, top - view.ActualHeight / 2));
        }

        _editor.ScrollTo(line, Math.Max(1, _editor.TextArea.Caret.Column));
        _ = documentLine;
    }

    private void ShowLabel(int current, int count)
    {
        _result.Text = TextSearch.Label(current, count);
        _hasResults = count != 0;
        EnableButtons();
    }

    private void SayMessage(string? message)
    {
        _message.Text = message ?? string.Empty;
        _message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary><c>disableButtonsIfFindEmpty</c> and <c>_disableReplaceStripElements</c>.</summary>
    private void EnableButtons()
    {
        bool can = _findBox.Text.Length > 0 && _hasResults;
        _previous.IsEnabled = can;
        _next.IsEnabled = can;
        bool replace = _replaceOpen && can;
        _replaceBox.IsEnabled = _replaceOpen;
        _replaceOne.IsEnabled = replace;
        _replaceAll.IsEnabled = replace;
    }

    private void OnFindTyped()
    {
        _typing.Stop();
        _typing.Start();
        EnableButtons();
        Find(new FindRequest { Text = _findBox.Text, FromCurrentOccurrence = true });
    }

    /// <summary><c>_setInHistory</c>: both boxes' texts to the front of their histories.</summary>
    private void PutInHistory()
    {
        FindTexts.Add(_findBox.Text);
        ReplaceTexts.Add(_replaceBox.Text);
    }

    private void FindFromStrip(bool backward)
    {
        if (_findBox.Text.Length == 0 && !backward)
        {
            return;
        }

        PutInHistory();
        Find(new FindRequest { Text = _findBox.Text, Backward = backward });
    }

    private void OptionChanged()
    {
        if (_showing)
        {
            return;
        }

        _matchCase = _matchCaseButton.IsChecked == true;
        _wholeWord = _wholeWordButton.IsChecked == true;
        _regex = _regexButton.IsChecked == true;
        PutInHistory();
        Find(new FindRequest { Text = _findBox.Text, FromCurrentOccurrence = true });
    }

    /// <summary><c>FindInSelectionHandler._stateChange</c>: on with a selection, the search is held to its lines; with none the button falls back off.</summary>
    private void InSelectionChanged()
    {
        if (_showing)
        {
            return;
        }

        if (_inSelectionButton.IsChecked == true)
        {
            if (_editor.SelectionLength == 0)
            {
                _showing = true;
                _inSelectionButton.IsChecked = false;
                _showing = false;
                _selectionFirst = _selectionLast = -1;
            }
            else
            {
                TextDocument document = _editor.Document;
                int a = document.GetLineByOffset(_editor.SelectionStart).LineNumber - 1;
                int b = document.GetLineByOffset(_editor.SelectionStart + _editor.SelectionLength).LineNumber - 1;
                _selectionFirst = Math.Min(a, b);
                _selectionLast = Math.Max(a, b);
            }
        }
        else
        {
            _selectionFirst = _selectionLast = -1;
        }

        PutInHistory();
        Find(new FindRequest { Text = _findBox.Text, FromCurrentOccurrence = true });
    }

    private void ClearFindText()
    {
        _showing = true;
        _findBox.Text = string.Empty;
        _showing = false;
        _resultCache = null;
        PutInHistory();
        ShowLabel(TextSearch.NoResult, 0);
        _findBox.Focus();
    }

    /// <summary>The arrow pressed (<c>onReplaceStripToggle</c>): the replace row follows it, and the session remembers.</summary>
    private void ReplaceToggled()
    {
        if (_showing)
        {
            return;
        }

        _replaceOpen = _toggleReplace.IsChecked == true;
        ToggleReplaceStrip(toSaved: true);
    }

    /// <summary><c>toggleReplaceStrip</c>: the replace row shown or hidden, as remembered for the session when <paramref name="toSaved"/>.</summary>
    private void ToggleReplaceStrip(bool toSaved)
    {
        if (!toSaved)
        {
            _replaceOpen = !_replaceOpen;
        }

        _replaceRow.Visibility = _replaceOpen ? Visibility.Visible : Visibility.Collapsed;
        bool showing = _showing;
        _showing = true;
        _toggleReplace.IsChecked = _replaceOpen;
        _showing = showing;
        _toggleReplace.Content = _replaceOpen ? "▾" : "▸";
        _toggleReplace.ToolTip = _replaceOpen ? "Hide replace options" : "Show replace options";
        EnableButtons();
    }

    /// <summary><c>FindReplaceActions._replace</c>: the selected occurrence replaced when it is the one found, then the next found.</summary>
    private void ReplaceOne(bool backward)
    {
        PutInHistory();
        string text = _findBox.Text;
        if (text.Length == 0)
        {
            return;
        }

        string replaceWith = _replaceBox.Text;
        bool replaced = false;
        if (_editor.SelectionLength > 0 && _canEdit(_editor.SelectionStart))
        {
            if (_regex)
            {
                if (_resultCache?.Current is { } current)
                {
                    TextDocument document = _editor.Document;
                    DocumentLine line = document.GetLineByOffset(_editor.SelectionStart);
                    var selection = new SearchMatch(line.LineNumber - 1, _editor.SelectionStart - line.Offset, _editor.SelectionStart + _editor.SelectionLength - line.Offset, current.SearchIndex);
                    if (selection.Line == current.Line && selection.StartColumn == current.StartColumn && selection.EndColumn == current.EndColumn)
                    {
                        string lineText = document.GetText(line);
                        replaced = ReplaceSelection(TextSearch.Replacement(lineText, current, text, _matchCase, replaceWith));
                    }
                }
            }
            else if (string.Equals(_editor.SelectedText, text, StringComparison.OrdinalIgnoreCase))
            {
                replaced = ReplaceSelection(replaceWith);
            }
        }

        _ = replaced;
        Find(new FindRequest { Text = text, Backward = backward });
    }

    private bool ReplaceSelection(string replacement)
    {
        int start = _editor.SelectionStart;
        int length = _editor.SelectionLength;
        _settingSelection = true;
        try
        {
            _editor.Document.Replace(start, length, replacement);
            _editor.CaretOffset = start + replacement.Length;
        }
        finally
        {
            _settingSelection = false;
        }

        return true;
    }

    /// <summary><c>_replaceAll</c>: every occurrence from the last back to the first, as one undo step, those on lines that may not be edited passed over; the caret put back.</summary>
    private void ReplaceAll()
    {
        PutInHistory();
        string text = _findBox.Text;
        if (text.Length == 0)
        {
            return;
        }

        SearchResult? result = Find(new FindRequest { Text = text });
        if (result is null || result.CurrentIndex < 0)
        {
            return;
        }

        TextDocument document = _editor.Document;
        int caret = _editor.CaretOffset;
        string replaceWith = _replaceBox.Text;
        int replaced = 0;
        using (document.RunUpdate())
        {
            for (int i = result.Matches.Count - 1; i >= 0; i--)
            {
                SearchMatch match = result.Matches[i];
                if (match.Line < 0 || match.Line >= document.LineCount)
                {
                    continue;
                }

                DocumentLine line = document.GetLineByNumber(match.Line + 1);
                int start = line.Offset + Math.Min(match.StartColumn, line.Length);
                int end = line.Offset + Math.Min(match.EndColumn, line.Length);
                if (!_canEdit(start))
                {
                    continue;
                }

                string replacement = _regex ? TextSearch.Replacement(document.GetText(line), match, text, _matchCase, replaceWith) : replaceWith;
                _settingSelection = true;
                try
                {
                    document.Replace(start, end - start, replacement);
                }
                finally
                {
                    _settingSelection = false;
                }

                replaced++;
            }
        }

        _editor.CaretOffset = Math.Min(caret, document.TextLength);
        _resultCache = null;
        Find(new FindRequest { Text = text });
        _message.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        SayMessage($"Replaced {replaced} occurrences.");
        _message.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        _toggleReplace.Focus();
    }

    /// <summary>The strip's keys (<c>FindStrip._keyHandler</c>, <c>FindAltShortcutsHandler</c>): Esc, Enter, Down for the history, Alt+M/W/X/S and, with the replace row, Alt+R/A.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                return;
            case Key.Enter when ReferenceEquals(Keyboard.FocusedElement, _findBox):
                FindFromStrip(backward: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                return;
            case Key.Enter when ReferenceEquals(Keyboard.FocusedElement, _replaceBox):
                ReplaceOne(backward: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                return;
            case Key.Down when Keyboard.FocusedElement is TextBox box && (ReferenceEquals(box, _findBox) || ReferenceEquals(box, _replaceBox)):
                OpenHistory(box);
                e.Handled = true;
                return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.M:
                _matchCaseButton.IsChecked = !_matchCaseButton.IsChecked;
                break;
            case Key.W:
                _wholeWordButton.IsChecked = !_wholeWordButton.IsChecked;
                break;
            case Key.X:
                _regexButton.IsChecked = !_regexButton.IsChecked;
                break;
            case Key.S:
                _inSelectionButton.IsChecked = !_inSelectionButton.IsChecked;
                break;
            case Key.R when _replaceOpen:
                ReplaceOne(backward: false);
                break;
            case Key.A when _replaceOpen:
                ReplaceAll();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The history under a box (<c>_downArrowKeyHandler</c>): the texts searched or written, the most recent first; one chosen is searched for.</summary>
    private void OpenHistory(TextBox box)
    {
        IReadOnlyList<string> items = ReferenceEquals(box, _findBox) ? FindTexts.MenuItems : ReplaceTexts.MenuItems;
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = box, Placement = PlacementMode.Bottom };
        AutomationProperties.SetAutomationId(menu, "Find.History");
        foreach (string item in items)
        {
            var entry = new MenuItem { Header = item };
            entry.Click += (_, _) =>
            {
                if (ReferenceEquals(box, _findBox))
                {
                    _showing = true;
                    _findBox.Text = item;
                    _showing = false;
                    EnableButtons();
                    Find(new FindRequest { Text = item, FromCurrentOccurrence = true });
                }
                else
                {
                    _replaceBox.Text = item;
                }

                box.Focus();
            };
            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    private static Button Push(string glyph, string id, string tip)
    {
        var button = new Button { Content = glyph, Width = 24, Height = 22, Margin = new Thickness(1, 0, 1, 0), ToolTip = tip, Focusable = false };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, tip);
        return button;
    }

    private static ToggleButton Toggle(string glyph, string id, string tip)
    {
        var button = new ToggleButton { Content = glyph, Width = 24, Height = 22, Margin = new Thickness(1, 0, 1, 0), ToolTip = tip, Focusable = false };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, tip);
        return button;
    }
}
