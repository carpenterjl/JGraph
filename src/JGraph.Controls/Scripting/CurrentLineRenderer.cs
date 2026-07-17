using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace JGraph.Controls.Scripting;

/// <summary>Highlights the line the debugger is paused at with a soft yellow band.</summary>
internal sealed class CurrentLineRenderer : IBackgroundRenderer
{
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xF5, 0xDD, 0x60));

    private int? _line;

    static CurrentLineRenderer() => HighlightBrush.Freeze();

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
