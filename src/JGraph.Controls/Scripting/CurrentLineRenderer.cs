using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace JGraph.Controls.Scripting;

/// <summary>Highlights the line the debugger is paused at with a soft yellow band.</summary>
/// <remarks>
/// Unlike <see cref="BreakpointMargin"/> this is an <see cref="IBackgroundRenderer"/>, not a
/// <c>FrameworkElement</c>, so it has no resource lookup of its own and cannot follow a theme swap.
/// <see cref="ScriptEditorControl"/> owns a themed dependency property and pushes the brush in.
/// </remarks>
internal sealed class CurrentLineRenderer : IBackgroundRenderer
{
    private int? _line;

    /// <summary>The band's fill. Semi-transparent: the code underneath has to stay readable.</summary>
    public Brush HighlightBrush { get; set; } = Brushes.Transparent;

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Moves the highlight to <paramref name="line"/> (null hides it).</summary>
    public void SetCurrentLine(int? line) => _line = line;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_line is not int line || textView.Document is null || line < 1 || line > textView.Document.LineCount)
        {
            return;
        }

        DocumentLine documentLine = textView.Document.GetLineByNumber(line);
        foreach (System.Windows.Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, documentLine))
        {
            drawingContext.DrawRectangle(
                HighlightBrush, null,
                new System.Windows.Rect(0, rect.Top, textView.ActualWidth, rect.Height));
        }
    }
}
