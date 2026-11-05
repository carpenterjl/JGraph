using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JGraph.Scripting.Analysis;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The editor's message bar (App Designer window plan W10; R2025b's indicator to the right of the scroll bar): a
/// square at the top that is green with no message, orange with warnings and red with an error - a click goes to the
/// next message after the caret - a Fix button under it, enabled while the caret is on a message with a fix, and a
/// mark for each message's line down the strip, which a click goes to. Automation reads the strip's
/// <c>ItemStatus</c>: each message's id and span, so a window check sees the marks without a picture.
/// </summary>
public sealed class MessageIndicatorStrip : Grid
{
    private readonly Button _status = new() { Width = 12, Height = 12, Margin = new Thickness(0, 4, 0, 2), Padding = new Thickness(0), BorderThickness = new Thickness(1), Focusable = false };
    private readonly Button _fix = new() { Width = 12, Height = 12, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(0), BorderThickness = new Thickness(1), Focusable = false, FontSize = 9, Content = "✓" };
    private readonly Canvas _marks = new() { Background = Brushes.Transparent };
    private IReadOnlyList<CodeMessage> _messages = [];
    private Func<int>? _lineCount;

    public MessageIndicatorStrip()
    {
        Width = 16;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_status, 0);
        SetRow(_fix, 1);
        SetRow(_marks, 2);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _fix.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(_status);
        Children.Add(_fix);
        Children.Add(_marks);
        AutomationProperties.SetAutomationId(_status, "CodeAnalyzer.Status");
        AutomationProperties.SetAutomationId(_fix, "CodeAnalyzer.Fix");
        _status.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
        _fix.Click += (_, _) => FixRequested?.Invoke(this, EventArgs.Empty);
        _marks.SizeChanged += (_, _) => LayoutMarks();
        _marks.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { Tag: int line })
            {
                GoToLineRequested?.Invoke(this, line);
                e.Handled = true;
            }
        };
        SetResourceReference(BackgroundProperty, Themes.ThemeKeys.Panel);
        ShowState(0);
    }

    /// <summary>A click on the square: the next message after the caret.</summary>
    public event EventHandler? NextRequested;

    /// <summary>A click on the Fix button.</summary>
    public event EventHandler? FixRequested;

    /// <summary>A click on a mark: the message's line (1-based).</summary>
    public event EventHandler<int>? GoToLineRequested;

    /// <summary>Tells the strip how many lines the document has, for the marks' places.</summary>
    public void BindLineCount(Func<int> lineCount) => _lineCount = lineCount;

    /// <summary>Shows the messages: the square's colour and the marks; <paramref name="atCaret"/> is the message under the caret, which the Fix button offers.</summary>
    public void Show(IReadOnlyList<CodeMessage> messages, CodeMessage? atCaret)
    {
        _messages = messages;
        int errors = messages.Count(static m => m.Severity == CodeSeverity.Error);
        int warnings = messages.Count(static m => m.Severity == CodeSeverity.Warning);
        int infos = messages.Count - errors - warnings;
        ShowState(errors > 0 ? 2 : warnings > 0 ? 1 : 0);
        _status.ToolTip = messages.Count == 0 ? "No Code Analyzer messages"
            : string.Join(", ", new[] { Plural(errors, "error"), Plural(warnings, "warning"), Plural(infos, "suggestion") }.Where(static s => s.Length > 0)) + ". Click to go to the next message.";
        // The square's status, which automation reads (a panel has no peer): the state, then each message's id and span.
        string state = errors > 0 ? "error" : warnings > 0 ? "warning" : "ok";
        string listed = string.Join("; ", messages.Select(static m => $"{(m.Id.Length > 0 ? m.Id : "tip")} {m.Line}:{m.StartColumn}-{m.EndColumn}"));
        AutomationProperties.SetItemStatus(_status, listed.Length > 0 ? state + "; " + listed : state);
        ShowCaret(atCaret);
        LayoutMarks();
    }

    /// <summary>Says which message the caret is on, for the Fix button.</summary>
    public void ShowCaret(CodeMessage? atCaret)
    {
        _fix.IsEnabled = atCaret?.Fix is not null;
        _fix.ToolTip = atCaret?.Fix is not null ? $"Fix: {atCaret.Text} (Alt+Enter)" : "Fix the message under the caret (Alt+Enter)";
        _fix.Visibility = _messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Plural(int count, string word) => count == 0 ? string.Empty : count == 1 ? $"1 {word}" : $"{count} {word}s";

    private void ShowState(int level)
    {
        string key = level switch { 2 => Themes.ThemeKeys.Error, 1 => Themes.ThemeKeys.Warning, _ => Themes.ThemeKeys.Success };
        _status.SetResourceReference(Control.BackgroundProperty, key);
        _status.SetResourceReference(Control.BorderBrushProperty, Themes.ThemeKeys.BorderStrong);
        _fix.SetResourceReference(Control.BackgroundProperty, Themes.ThemeKeys.Control);
        _fix.SetResourceReference(Control.BorderBrushProperty, Themes.ThemeKeys.BorderStrong);
        _fix.SetResourceReference(Control.ForegroundProperty, Themes.ThemeKeys.Text);
    }

    private void LayoutMarks()
    {
        _marks.Children.Clear();
        double height = _marks.ActualHeight;
        int lines = Math.Max(1, _lineCount?.Invoke() ?? 1);
        if (height <= 0)
        {
            return;
        }

        foreach (CodeMessage message in _messages)
        {
            double y = Math.Min(height - 3, (message.Line - 1) * height / lines);
            var mark = new Rectangle { Width = 10, Height = 3, Tag = message.Line, ToolTip = message.Text, Cursor = Cursors.Hand };
            mark.SetResourceReference(Shape.FillProperty, message.Severity switch
            {
                CodeSeverity.Error => Themes.ThemeKeys.Error,
                CodeSeverity.Warning => Themes.ThemeKeys.Warning,
                _ => Themes.ThemeKeys.Accent,
            });
            Canvas.SetLeft(mark, 3);
            Canvas.SetTop(mark, y);
            _marks.Children.Add(mark);
        }
    }
}
