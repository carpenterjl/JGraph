using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>
/// Keeps whole lines of a document from being edited (designer plan DS5): the lines App Designer
/// generates in its code view, which only the design changes. Which lines are editable is asked of
/// the document's text when it has changed, so an edit that moves a line keeps the right lines open.
/// Nothing can be typed on a read-only line, nothing of one deleted, and a line break that joins an
/// editable line to a read-only one cannot be deleted either.
/// </summary>
internal sealed class LineReadOnlySectionProvider : IReadOnlySectionProvider
{
    private readonly TextDocument document;
    private IReadOnlySet<int>? _editable;
    private bool _stale = true;

    public LineReadOnlySectionProvider(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
        // Every change, as it happens - inside an update too, where the editor asks about the line a
        // new line break just made before the update's TextChanged is raised.
        document.Changed += (_, _) => _stale = true;
    }

    /// <summary>The one-based lines of a text that may be edited; every line when null.</summary>
    public Func<string, IReadOnlySet<int>>? EditableLines { get; set; }

    /// <summary>Says the text, or what decides its editable lines, changed, so they are asked for again.</summary>
    public void Invalidate() => _stale = true;

    /// <inheritdoc />
    public bool CanInsert(int offset) => IsEditable(document.GetLineByOffset(offset));

    /// <inheritdoc />
    public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.Length == 0)
        {
            if (CanInsert(segment.Offset))
            {
                yield return segment;
            }

            yield break;
        }

        for (DocumentLine? line = document.GetLineByOffset(segment.Offset); line is not null && line.Offset <= segment.EndOffset; line = line.NextLine)
        {
            if (!IsEditable(line))
            {
                continue;
            }

            // The line break after an editable line goes only when the next line is editable too.
            int end = line.NextLine is { } next && IsEditable(next) ? line.EndOffset + line.DelimiterLength : line.EndOffset;
            int start = Math.Max(segment.Offset, line.Offset);
            end = Math.Min(segment.EndOffset, end);
            if (end > start)
            {
                yield return new TextSegment { StartOffset = start, EndOffset = end };
            }
        }
    }

    private bool IsEditable(DocumentLine line)
    {
        if (EditableLines is null)
        {
            return true;
        }

        if (_stale || _editable is null)
        {
            _editable = EditableLines(document.Text);
            _stale = false;
        }

        return _editable.Contains(line.LineNumber);
    }
}
