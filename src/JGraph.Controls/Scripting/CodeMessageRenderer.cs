using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using JGraph.Scripting.Analysis;

namespace JGraph.Controls.Scripting;

/// <summary>
/// Draws the Code Analyzer's marks under the text (App Designer window plan W10): a wavy line under the span of each
/// message, red for an error, orange for a warning, a dotted grey-blue line for a suggestion, as R2025b's editor
/// underlines them. The spans are the analyzer's columns on the lines it measured; a line that has changed since
/// keeps its mark until the next analysis, which the host runs a moment after the last keystroke.
/// </summary>
internal sealed class CodeMessageRenderer : IBackgroundRenderer
{
    private IReadOnlyList<CodeMessage> _messages = [];

    /// <summary>The brush of an error's mark. Themed.</summary>
    public Brush ErrorBrush { get; set; } = Brushes.Red;

    /// <summary>The brush of a warning's mark. Themed.</summary>
    public Brush WarningBrush { get; set; } = Brushes.Orange;

    /// <summary>The brush of a suggestion's mark.</summary>
    public Brush InfoBrush { get; set; } = Brushes.SteelBlue;

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>The messages drawn.</summary>
    public IReadOnlyList<CodeMessage> Messages
    {
        get => _messages;
        set => _messages = value ?? [];
    }

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_messages.Count == 0 || textView.Document is not { } document || !textView.VisualLinesValid)
        {
            return;
        }

        int first = textView.VisualLines[0].FirstDocumentLine.LineNumber;
        int last = textView.VisualLines[^1].LastDocumentLine.LineNumber;
        foreach (CodeMessage message in _messages)
        {
            if (message.Line < first || message.Line > last || message.Line < 1 || message.Line > document.LineCount)
            {
                continue;
            }

            DocumentLine line = document.GetLineByNumber(message.Line);
            int start = Math.Clamp(message.StartColumn - 1, 0, line.Length);
            int end = Math.Clamp(message.EndColumn, start, line.Length);
            if (end == start)
            {
                end = Math.Min(start + 1, line.Length);
                if (end == start && start > 0)
                {
                    start--;
                }
            }

            var segment = new TextSegment { StartOffset = line.Offset + start, EndOffset = line.Offset + end };
            Brush brush = message.Severity switch
            {
                CodeSeverity.Error => ErrorBrush,
                CodeSeverity.Warning => WarningBrush,
                _ => InfoBrush,
            };
            var pen = new Pen(brush, 1.2);
            if (message.Severity == CodeSeverity.Info)
            {
                pen.DashStyle = DashStyles.Dot;
            }

            pen.Freeze();
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                if (rect.Width <= 0)
                {
                    continue;
                }

                double baseline = Math.Round(rect.Bottom) - 1.5;
                if (message.Severity == CodeSeverity.Info)
                {
                    drawingContext.DrawLine(pen, new Point(rect.Left, baseline), new Point(rect.Right, baseline));
                    continue;
                }

                // The wave: two points per three pixels, up and down.
                var geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(new Point(rect.Left, baseline), false, false);
                    bool up = true;
                    for (double x = rect.Left + 2; x < rect.Right + 2; x += 2)
                    {
                        context.LineTo(new Point(Math.Min(x, rect.Right), up ? baseline - 2 : baseline), true, false);
                        up = !up;
                    }
                }

                geometry.Freeze();
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }
    }
}
