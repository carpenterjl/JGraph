using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using JGraph.Scripting.Jgs;

namespace JGraph.Controls.Scripting;

/// <summary>
/// Shades the lines of an App Designer file's code that App Designer generates, as App Designer's
/// own code view does (app-building plan U7b, ADR 0205). The unshaded lines are the user's: the
/// editable section and the bodies of the callbacks. The shading says whose a line is and nothing
/// more - every line can be edited, and the save says what an edit to a shaded one means.
/// </summary>
/// <remarks>
/// The spans are read from the text by <see cref="JgsMlappLayout"/> the first time they are drawn
/// after an edit, not on every keystroke. Like <see cref="CurrentLineRenderer"/> this has no
/// resource lookup of its own; <see cref="ScriptEditorControl"/> pushes the themed brush in.
/// </remarks>
internal sealed class GeneratedCodeRenderer : IBackgroundRenderer
{
    private IReadOnlyList<(int FirstLine, int LineCount)> _spans = [];
    private bool _stale = true;

    /// <summary>The shade. Semi-transparent, so the selection and the paused line show through it.</summary>
    public Brush ShadeBrush { get; set; } = Brushes.Transparent;

    /// <summary>Whether the document is one to shade; any other is left alone.</summary>
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Says the text changed, so the spans are read again when next drawn.</summary>
    public void Invalidate() => _stale = true;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled || textView.Document is not { } document || !textView.VisualLinesValid)
        {
            return;
        }

        if (_stale)
        {
            _spans = JgsMlappLayout.Of(document.Text).GeneratedSpans;
            _stale = false;
        }

        foreach (VisualLine visual in textView.VisualLines)
        {
            int line = visual.FirstDocumentLine.LineNumber - 1;
            if (!IsGenerated(line))
            {
                continue;
            }

            foreach (System.Windows.Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, visual.FirstDocumentLine))
            {
                drawingContext.DrawRectangle(
                    ShadeBrush, null, new System.Windows.Rect(0, rect.Top, textView.ActualWidth, rect.Height));
            }
        }
    }

    private bool IsGenerated(int line)
    {
        foreach ((int first, int count) in _spans)
        {
            if (line >= first && line < first + count)
            {
                return true;
            }
        }

        return false;
    }
}
