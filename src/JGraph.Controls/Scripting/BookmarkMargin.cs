using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using JGraph.Scripting.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The editor's bookmarks margin (App Designer window plan W9a, R2025b's <c>BookmarksDecorator</c> and its margin): a mark on each
/// bookmarked line, shown only while the document has bookmarks (<c>setHiddenInitialState</c>, <c>showMargin</c>). Each bookmark is an anchor,
/// so it follows its line through edits and goes when the line does. A click toggles the line's mark.
/// </summary>
internal sealed class BookmarkMargin : AbstractMargin
{
    private const double MarginWidth = 12;

    /// <summary>The mark's fill. Themed.</summary>
    public static readonly DependencyProperty MarkBrushProperty =
        DependencyProperty.Register(nameof(MarkBrush), typeof(Brush), typeof(BookmarkMargin),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<TextAnchor> _anchors = [];
    private readonly TextDocument _document;

    public BookmarkMargin(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        SetResourceReference(MarkBrushProperty, Themes.ThemeKeys.Accent);
        Visibility = Visibility.Collapsed;
    }

    /// <summary>The mark's fill.</summary>
    public Brush MarkBrush
    {
        get => (Brush)GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    /// <summary>Raised when a mark is set or cleared, by the user or the host.</summary>
    public event EventHandler? Changed;

    /// <summary>The one-based lines carrying a bookmark, in order; a line that was deleted has none any more.</summary>
    public IReadOnlyList<int> Lines
    {
        get
        {
            Prune();
            return [.. _anchors.Select(static a => a.Line).Distinct().Order()];
        }
    }

    /// <summary>The zero-based lines carrying a bookmark, as a set, for <see cref="BookmarkRules"/>.</summary>
    public IReadOnlySet<int> Marked => new HashSet<int>(Lines.Select(static l => l - 1));

    /// <summary>Whether the document has a bookmark.</summary>
    public bool HasBookmarks => Lines.Count > 0;

    /// <summary>Whether <paramref name="line"/> (one-based) is empty, which R2025b does not bookmark.</summary>
    public bool IsBlank(int line) => line < 1 || line > _document.LineCount || _document.GetLineByNumber(line).Length == 0;

    /// <summary>Sets or clears the bookmark on <paramref name="line"/> (one-based); an empty line takes none (<c>_toggleForNode</c>).</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Toggle(int line)
    {
        if (IsBlank(line))
        {
            return false;
        }

        Prune();
        int at = _anchors.FindIndex(a => a.Line == line);
        if (at >= 0)
        {
            _anchors.RemoveAt(at);
        }
        else
        {
            _anchors.Add(_document.CreateAnchor(_document.GetLineByNumber(line).Offset));
        }

        Show();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Replaces the bookmarks with those on <paramref name="lines"/> (one-based), a line beyond the text or empty left out; the host restores them so.</summary>
    public void SetLines(IEnumerable<int> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _anchors.Clear();
        foreach (int line in lines.Distinct().Order())
        {
            if (!IsBlank(line))
            {
                _anchors.Add(_document.CreateAnchor(_document.GetLineByNumber(line).Offset));
            }
        }

        Show();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears every bookmark (<c>clearAllBookmarks</c>).</summary>
    public void Clear() => SetLines([]);

    private void Prune()
    {
        if (_anchors.RemoveAll(static a => a.IsDeleted) > 0)
        {
            Show();
        }
    }

    private void Show()
    {
        Visibility = _anchors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    /// <inheritdoc />
    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e)
    {
        // A deleted line's mark goes with it: the margin hides itself when the last one went.
        Prune();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
        TextView? textView = TextView;
        if (textView is null || !textView.VisualLinesValid)
        {
            return;
        }

        var marked = new HashSet<int>(_anchors.Where(static a => !a.IsDeleted).Select(static a => a.Line));
        foreach (VisualLine visualLine in textView.VisualLines)
        {
            int line = visualLine.FirstDocumentLine.LineNumber;
            if (!marked.Contains(line))
            {
                continue;
            }

            double top = visualLine.VisualTop - textView.VerticalOffset;
            double height = Math.Min(visualLine.Height, 14);
            double y = top + (visualLine.Height - height) / 2;
            // A bookmark: a tag with a notch at its foot.
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(2, y), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(10, y), true, false);
                ctx.LineTo(new Point(10, y + height), true, false);
                ctx.LineTo(new Point(6, y + height - 4), true, false);
                ctx.LineTo(new Point(2, y + height), true, false);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(MarkBrush, null, geometry);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        TextView? textView = TextView;
        if (textView is null || !textView.VisualLinesValid)
        {
            return;
        }

        if (textView.GetVisualLineFromVisualTop(e.GetPosition(this).Y + textView.VerticalOffset) is { } visualLine)
        {
            Toggle(visualLine.FirstDocumentLine.LineNumber);
            e.Handled = true;
        }
    }
}
