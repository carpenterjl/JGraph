using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>
/// Two texts side by side as R2025b's Comparison Tool draws them (App Designer window plan W14): the rows of a
/// <see cref="FileComparison"/>, each side a read-only editor with the comparison's own line numbers, a line of one
/// side with nothing beside it facing a grey filler row, inserted rows blue, deleted rows brown and changed rows
/// purple in the report's colours with the characters that changed boxed, the glyphs (+, −, ≠) in a gutter between,
/// the two scrolled together, and the current difference outlined. Reused for any two texts.
/// </summary>
public sealed class TextDiffView : UserControl
{
    private static readonly Color InsertedLight = Color.FromRgb(0xc9, 0xe7, 0xff);
    private static readonly Color DeletedLight = Color.FromRgb(0xff, 0xf4, 0xe0);
    private static readonly Color ChangedLight = Color.FromRgb(0xf3, 0xe3, 0xff);
    private static readonly Color FillerLight = Color.FromRgb(0xe0, 0xe0, 0xe0);
    private static readonly Color InsertedGlyph = Color.FromRgb(0x11, 0x71, 0xbe);
    private static readonly Color DeletedGlyph = Color.FromRgb(0x74, 0x5a, 0x26);
    private static readonly Color ChangedGlyph = Color.FromRgb(0xa7, 0x09, 0xf5);

    /// <summary>Whether the theme is dark (bound to <c>JG.Theme.IsDark</c>): the rows then take translucent shades of the glyph colours.</summary>
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.Register(
        nameof(IsDark), typeof(bool), typeof(TextDiffView), new FrameworkPropertyMetadata(false, static (d, _) => ((TextDiffView)d).Repaint()));

    private readonly TextEditor _left = Editor("Compare.Left");
    private readonly TextEditor _right = Editor("Compare.Right");
    private readonly DiffGutter _gutter;
    private readonly RowRenderer _leftRows;
    private readonly RowRenderer _rightRows;
    private readonly InlineColorizer _leftInline;
    private readonly InlineColorizer _rightInline;
    private FileComparison? _comparison;
    private bool _syncing;
    private int _current = -1;

    public TextDiffView()
    {
        _leftRows = new RowRenderer(this, left: true);
        _rightRows = new RowRenderer(this, left: false);
        _leftInline = new InlineColorizer(this, left: true);
        _rightInline = new InlineColorizer(this, left: false);
        _left.TextArea.TextView.BackgroundRenderers.Add(_leftRows);
        _right.TextArea.TextView.BackgroundRenderers.Add(_rightRows);
        _left.TextArea.TextView.LineTransformers.Add(_leftInline);
        _right.TextArea.TextView.LineTransformers.Add(_rightInline);
        _left.TextArea.LeftMargins.Insert(0, new NumberMargin(this, left: true));
        _right.TextArea.LeftMargins.Insert(0, new NumberMargin(this, left: false));
        _left.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _gutter = new DiffGutter(this, _left.TextArea.TextView);
        _left.TextArea.TextView.ScrollOffsetChanged += (_, _) => Follow(_left, _right);
        _right.TextArea.TextView.ScrollOffsetChanged += (_, _) => Follow(_right, _left);
        _left.TextArea.Caret.PositionChanged += (_, _) => Picked(_left);
        _right.TextArea.Caret.PositionChanged += (_, _) => Picked(_right);
        SetResourceReference(IsDarkProperty, ThemeKeys.ThemeIsDark);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_gutter, 1);
        Grid.SetColumn(_right, 2);
        grid.Children.Add(_left);
        grid.Children.Add(_gutter);
        grid.Children.Add(_right);
        Content = grid;
        Loaded += (_, _) => Repaint();
    }

    /// <summary>Raised after the current difference changed, by a key, a click or <see cref="GoTo"/>.</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>Whether the theme is dark.</summary>
    public bool IsDark
    {
        get => (bool)GetValue(IsDarkProperty);
        set => SetValue(IsDarkProperty, value);
    }

    /// <summary>The comparison shown; null for none.</summary>
    public FileComparison? Comparison => _comparison;

    /// <summary>The index of the current difference among <see cref="FileComparison.Hunks"/>, or -1.</summary>
    public int Current => _current;

    /// <summary>The syntax highlighting's language ("MATLAB"); null for none.</summary>
    public string? SyntaxLanguage { get; set; } = MatlabSyntax.Name;

    /// <summary>Shows <paramref name="comparison"/>, keeping the current difference where there still is one.</summary>
    public void Show(FileComparison? comparison)
    {
        _comparison = comparison;
        var leftText = new System.Text.StringBuilder();
        var rightText = new System.Text.StringBuilder();
        if (comparison is not null)
        {
            for (int r = 0; r < comparison.Rows.Count; r++)
            {
                DiffRow row = comparison.Rows[r];
                if (r > 0)
                {
                    leftText.Append('\n');
                    rightText.Append('\n');
                }

                leftText.Append(row.Left > 0 ? comparison.LeftLines[row.Left - 1] : string.Empty);
                rightText.Append(row.Right > 0 ? comparison.RightLines[row.Right - 1] : string.Empty);
            }
        }

        _left.Text = leftText.ToString();
        _right.Text = rightText.ToString();
        _leftInline.Reset();
        _rightInline.Reset();
        int hunks = comparison?.Hunks.Count ?? 0;
        _current = hunks == 0 ? -1 : Math.Clamp(_current < 0 ? 0 : _current, 0, hunks - 1);
        Repaint();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes difference <paramref name="index"/> current and scrolls both sides to it.</summary>
    public void GoTo(int index)
    {
        if (_comparison is null || index < 0 || index >= _comparison.Hunks.Count)
        {
            return;
        }

        _current = index;
        DiffHunk hunk = _comparison.Hunks[index];
        int line = hunk.FirstRow + 1;
        _left.ScrollToLine(Math.Max(1, line - 3));
        _left.TextArea.Caret.Line = line;
        _left.TextArea.Caret.Column = 1;
        Repaint();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The next difference after the current one that <paramref name="accept"/> takes (every one for null); false at the end.</summary>
    public bool Next(Func<DiffHunk, bool>? accept = null) => Step(1, accept);

    /// <summary>The previous difference before the current one that <paramref name="accept"/> takes; false at the start.</summary>
    public bool Previous(Func<DiffHunk, bool>? accept = null) => Step(-1, accept);

    private bool Step(int direction, Func<DiffHunk, bool>? accept)
    {
        if (_comparison is null)
        {
            return false;
        }

        for (int i = _current + direction; i >= 0 && i < _comparison.Hunks.Count; i += direction)
        {
            if (accept is null || accept(_comparison.Hunks[i]))
            {
                GoTo(i);
                return true;
            }
        }

        return false;
    }

    private static TextEditor Editor(string automationId)
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        editor.SetResourceReference(Control.FontFamilyProperty, ThemeKeys.FontMono);
        editor.SetResourceReference(Control.FontSizeProperty, ThemeKeys.FontSizeCode);
        editor.SetResourceReference(Control.BackgroundProperty, ThemeKeys.EditorBackground);
        editor.SetResourceReference(Control.ForegroundProperty, ThemeKeys.EditorForeground);
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        AutomationProperties.SetAutomationId(editor, automationId);
        return editor;
    }

    private void Follow(TextEditor from, TextEditor to)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            to.ScrollToVerticalOffset(from.VerticalOffset);
            to.ScrollToHorizontalOffset(from.HorizontalOffset);
            _gutter.InvalidateVisual();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>A click (the caret moved by the mouse) on a row of a difference makes it current.</summary>
    private void Picked(TextEditor editor)
    {
        if (_comparison is null || !editor.TextArea.IsKeyboardFocusWithin || Mouse.LeftButton != MouseButtonState.Pressed && !editor.TextArea.IsMouseOver)
        {
            return;
        }

        int row = editor.TextArea.Caret.Line - 1;
        int index = _comparison.Hunks.ToList().FindIndex(h => row >= h.FirstRow && row < h.EndRow);
        if (index >= 0 && index != _current)
        {
            _current = index;
            Repaint();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Repaint()
    {
        Color background = (_left.Background as SolidColorBrush)?.Color ?? (IsDark ? Colors.Black : Colors.White);
        if (SyntaxLanguage is { } language)
        {
            _left.SyntaxHighlighting = SyntaxThemes.Resolve(language, IsDark, background);
            _right.SyntaxHighlighting = _left.SyntaxHighlighting;
        }

        _left.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _right.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _left.TextArea.TextView.Redraw();
        _right.TextArea.TextView.Redraw();
        _gutter.InvalidateVisual();
    }

    /// <summary>The row's background by its kind, for the side; the filler's for a side with no line; null for the same.</summary>
    private Brush? RowBrush(DiffRow row, bool left)
    {
        bool dark = IsDark;
        int line = left ? row.Left : row.Right;
        if (row.Kind == DiffRowKind.Same)
        {
            return null;
        }

        if (line == 0)
        {
            return Frozen(dark ? Color.FromArgb(0x50, 0x80, 0x80, 0x80) : FillerLight);
        }

        return row.Kind switch
        {
            DiffRowKind.Inserted => Frozen(dark ? Color.FromArgb(0x55, InsertedGlyph.R, InsertedGlyph.G, InsertedGlyph.B) : InsertedLight),
            DiffRowKind.Deleted => Frozen(dark ? Color.FromArgb(0x55, DeletedGlyph.R, DeletedGlyph.G, DeletedGlyph.B) : DeletedLight),
            _ => Frozen(dark ? Color.FromArgb(0x55, ChangedGlyph.R, ChangedGlyph.G, ChangedGlyph.B) : ChangedLight),
        };
    }

    private static Brush GlyphBrush(DiffRowKind kind) => Frozen(kind switch
    {
        DiffRowKind.Inserted => InsertedGlyph,
        DiffRowKind.Deleted => DeletedGlyph,
        _ => ChangedGlyph,
    });

    private static SolidColorBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>Paints each row's background and outlines the current difference.</summary>
    private sealed class RowRenderer(TextDiffView owner, bool left) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (owner._comparison is not { } comparison || !textView.VisualLinesValid)
            {
                return;
            }

            DiffHunk? current = owner._current >= 0 && owner._current < comparison.Hunks.Count ? comparison.Hunks[owner._current] : null;
            foreach (VisualLine visual in textView.VisualLines)
            {
                int index = visual.FirstDocumentLine.LineNumber - 1;
                if (index < 0 || index >= comparison.Rows.Count)
                {
                    continue;
                }

                DiffRow row = comparison.Rows[index];
                Brush? brush = owner.RowBrush(row, left);
                foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, visual.FirstDocumentLine))
                {
                    var full = new Rect(0, rect.Top, Math.Max(textView.ActualWidth, rect.Right), rect.Height);
                    if (brush is not null)
                    {
                        drawingContext.DrawRectangle(brush, null, full);
                    }

                    if (current is not null && index >= current.FirstRow && index < current.EndRow)
                    {
                        var pen = new Pen(GlyphBrush(row.Kind), 1.5);
                        if (index == current.FirstRow)
                        {
                            drawingContext.DrawLine(pen, new Point(0, full.Top + 0.75), new Point(full.Right, full.Top + 0.75));
                        }

                        if (index == current.EndRow - 1)
                        {
                            drawingContext.DrawLine(pen, new Point(0, full.Bottom - 0.75), new Point(full.Right, full.Bottom - 0.75));
                        }
                    }

                    break;
                }
            }
        }
    }

    /// <summary>Boxes the characters of a changed row that the other side does not have.</summary>
    private sealed class InlineColorizer(TextDiffView owner, bool left) : DocumentColorizingTransformer
    {
        private readonly Dictionary<int, IReadOnlyList<DiffSegment>> _segments = [];

        public void Reset() => _segments.Clear();

        protected override void ColorizeLine(DocumentLine line)
        {
            if (owner._comparison is not { } comparison)
            {
                return;
            }

            int index = line.LineNumber - 1;
            if (index < 0 || index >= comparison.Rows.Count || comparison.Rows[index].Kind != DiffRowKind.Changed)
            {
                return;
            }

            if (!_segments.TryGetValue(index, out IReadOnlyList<DiffSegment>? segments))
            {
                DiffRow row = comparison.Rows[index];
                (IReadOnlyList<DiffSegment> l, IReadOnlyList<DiffSegment> r) = TextDiff.Inline(comparison.LeftLines[row.Left - 1], comparison.RightLines[row.Right - 1]);
                segments = left ? l : r;
                _segments[index] = segments;
            }

            Brush box = Frozen(owner.IsDark ? Color.FromArgb(0x90, ChangedGlyph.R, ChangedGlyph.G, ChangedGlyph.B) : Color.FromArgb(0x60, ChangedGlyph.R, ChangedGlyph.G, ChangedGlyph.B));
            int at = line.Offset;
            foreach (DiffSegment segment in segments)
            {
                int end = Math.Min(at + segment.Text.Length, line.EndOffset);
                if (segment.Changed && end > at)
                {
                    ChangeLinePart(at, end, element => element.TextRunProperties.SetBackgroundBrush(box));
                }

                at = end;
            }
        }
    }

    /// <summary>The comparison's line numbers for a side: the file's own, none on a filler row.</summary>
    private sealed class NumberMargin(TextDiffView owner, bool left) : AbstractMargin
    {
        private double _width = 30;

        protected override Size MeasureOverride(Size availableSize)
        {
            int lines = owner._comparison is { } c ? Math.Max(left ? c.LeftLines.Count : c.RightLines.Count, 1) : 1;
            int digits = Math.Max(2, lines.ToString(CultureInfo.InvariantCulture).Length);
            var sample = new FormattedText(new string('9', digits), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(owner._left.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), owner._left.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            _width = sample.Width + 10;
            return new Size(_width, 0);
        }

        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
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

            InvalidateVisual();
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext drawingContext)
        {
            TextView? textView = TextView;
            if (textView is null || !textView.VisualLinesValid || owner._comparison is not { } comparison)
            {
                return;
            }

            Brush foreground = (TryFindResource(ThemeKeys.LineNumber) as Brush) ?? Brushes.Gray;
            var typeface = new Typeface(owner._left.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (VisualLine visual in textView.VisualLines)
            {
                int index = visual.FirstDocumentLine.LineNumber - 1;
                if (index < 0 || index >= comparison.Rows.Count)
                {
                    continue;
                }

                int number = left ? comparison.Rows[index].Left : comparison.Rows[index].Right;
                if (number == 0)
                {
                    continue;
                }

                var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, owner._left.FontSize, foreground, dip);
                double y = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;
                drawingContext.DrawText(text, new Point(_width - text.Width - 5, y));
            }
        }
    }

    /// <summary>The glyph column between the sides: +, − and ≠ on the report's colours, aligned with the left side's rows.</summary>
    private sealed class DiffGutter : FrameworkElement
    {
        private readonly TextDiffView _owner;
        private readonly TextView _textView;

        public DiffGutter(TextDiffView owner, TextView textView)
        {
            _owner = owner;
            _textView = textView;
            Width = 22;
            textView.VisualLinesChanged += (_, _) => InvalidateVisual();
            textView.ScrollOffsetChanged += (_, _) => InvalidateVisual();
            AutomationProperties.SetAutomationId(this, "Compare.Gutter");
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle((TryFindResource(ThemeKeys.Panel) as Brush) ?? Brushes.WhiteSmoke, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_owner._comparison is not { } comparison || !_textView.VisualLinesValid)
            {
                return;
            }

            var typeface = new Typeface(_owner._left.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (VisualLine visual in _textView.VisualLines)
            {
                int index = visual.FirstDocumentLine.LineNumber - 1;
                if (index < 0 || index >= comparison.Rows.Count || comparison.Rows[index].Kind == DiffRowKind.Same)
                {
                    continue;
                }

                DiffRow row = comparison.Rows[index];
                double top = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.LineTop) - _textView.VerticalOffset;
                double height = visual.Height;
                Brush brush = GlyphBrush(row.Kind);
                drawingContext.DrawRectangle(brush, null, new Rect(2, top, ActualWidth - 4, height));
                string glyph = row.Kind switch { DiffRowKind.Inserted => "+", DiffRowKind.Deleted => "−", _ => "≠" };
                var text = new FormattedText(glyph, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, _owner._left.FontSize, Brushes.White, dip);
                drawingContext.DrawText(text, new Point((ActualWidth - text.Width) / 2, top + (height - text.Height) / 2));
            }
        }
    }
}
