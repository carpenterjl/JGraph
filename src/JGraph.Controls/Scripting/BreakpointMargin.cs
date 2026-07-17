using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The editor's debug gutter: click a line to toggle a red breakpoint dot; the yellow arrow marks the
/// statement the debugger is paused at, and can be dragged (or the margin right-clicked) to request
/// set-next-statement. Purely visual + input — the set of breakpoints is owned here and surfaced
/// through <see cref="Breakpoints"/>/<see cref="BreakpointToggled"/>; the host decides what they mean.
/// </summary>
internal sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 18;

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush BreakpointBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x2A, 0x2A));
    private static readonly Brush ArrowBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x00));
    private static readonly Brush GhostArrowBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xE8, 0xC0, 0x00));
    private static readonly Pen ArrowPen = new(new SolidColorBrush(Color.FromRgb(0x8a, 0x71, 0x00)), 1);

    private readonly HashSet<int> _breakpoints = new();
    private int? _currentLine;
    private int? _dragTargetLine;
    private bool _dragging;

    static BreakpointMargin()
    {
        BackgroundBrush.Freeze();
        BreakpointBrush.Freeze();
        ArrowBrush.Freeze();
        GhostArrowBrush.Freeze();
        ArrowPen.Freeze();
    }

    /// <summary>Raised when the user toggles a breakpoint by clicking the margin.</summary>
    public event EventHandler? BreakpointToggled;

    /// <summary>Raised when the user drags the execution arrow to another line (or picks
    /// "Set next statement here" from the margin's context menu). The host asks the debugger.</summary>
    public event EventHandler<int>? SetNextLineRequested;

    /// <summary>The 1-based lines carrying a breakpoint.</summary>
    public IReadOnlyCollection<int> Breakpoints => _breakpoints;

    /// <summary>Replaces the breakpoint set (e.g. restoring persisted breakpoints).</summary>
    public void SetBreakpoints(IEnumerable<int> lines)
    {
        _breakpoints.Clear();
        foreach (int line in lines)
        {
            _breakpoints.Add(line);
        }

        InvalidateVisual();
    }

    /// <summary>Toggles the breakpoint on <paramref name="line"/> and notifies the host.</summary>
    public void Toggle(int line)
    {
        if (!_breakpoints.Add(line))
        {
            _breakpoints.Remove(line);
        }

        InvalidateVisual();
        BreakpointToggled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the current-statement arrow to <paramref name="line"/> (null hides it).</summary>
    public void SetCurrentLine(int? line)
    {
        _currentLine = line;
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

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        TextView? textView = TextView;
        if (textView is null || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (VisualLine visualLine in textView.VisualLines)
        {
            int line = visualLine.FirstDocumentLine.LineNumber;
            double top = visualLine.VisualTop - textView.VerticalOffset;
            double centerY = top + (visualLine.Height / 2);

            if (_breakpoints.Contains(line))
            {
                drawingContext.DrawEllipse(BreakpointBrush, null, new Point(MarginWidth / 2, centerY), 5, 5);
            }

            if (_currentLine == line)
            {
                drawingContext.DrawGeometry(ArrowBrush, ArrowPen, BuildArrow(centerY));
            }

            if (_dragging && _dragTargetLine == line && _dragTargetLine != _currentLine)
            {
                drawingContext.DrawGeometry(GhostArrowBrush, null, BuildArrow(centerY));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (GetLineAt(e.GetPosition(this).Y) is not int line)
        {
            return;
        }

        // Grabbing the execution arrow starts a set-next-statement drag; anywhere else toggles.
        if (line == _currentLine)
        {
            _dragging = true;
            _dragTargetLine = line;
            CaptureMouse();
        }
        else
        {
            Toggle(line);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            _dragTargetLine = GetLineAt(e.GetPosition(this).Y);
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        int? target = GetLineAt(e.GetPosition(this).Y);
        InvalidateVisual();
        if (target is int line && line != _currentLine)
        {
            SetNextLineRequested?.Invoke(this, line);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_currentLine is null || GetLineAt(e.GetPosition(this).Y) is not int line)
        {
            return; // the gesture only exists while paused
        }

        var item = new System.Windows.Controls.MenuItem { Header = "Set next statement here" };
        item.Click += (_, _) => SetNextLineRequested?.Invoke(this, line);
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = this };
        menu.Items.Add(item);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private int? GetLineAt(double y)
    {
        TextView? textView = TextView;
        if (textView is null || !textView.VisualLinesValid)
        {
            return null;
        }

        VisualLine? visualLine = textView.GetVisualLineFromVisualTop(y + textView.VerticalOffset);
        return visualLine?.FirstDocumentLine.LineNumber;
    }

    private static Geometry BuildArrow(double centerY)
    {
        // A small right-pointing arrow, VS style.
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(4, centerY - 3), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(9, centerY - 3), true, false);
            ctx.LineTo(new Point(9, centerY - 6), true, false);
            ctx.LineTo(new Point(14, centerY), true, false);
            ctx.LineTo(new Point(9, centerY + 6), true, false);
            ctx.LineTo(new Point(9, centerY + 3), true, false);
            ctx.LineTo(new Point(4, centerY + 3), true, false);
        }

        geometry.Freeze();
        return geometry;
    }
}
