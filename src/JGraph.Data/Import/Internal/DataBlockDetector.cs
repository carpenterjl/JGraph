namespace JGraph.Data.Import.Internal;

/// <summary>
/// Finds where the real data starts in a file that opens with unrelated preamble — a station banner, a
/// couple of narrow summary tables, blank lines — before the wide, regular block the reader is after.
/// The signal is width: the data block is the run of records sharing the file's widest field count, and
/// anything above the first such record is preamble.
/// <para>
/// The detector deliberately refuses to engage unless it is confident. When the widest record is already
/// the first one — every ordinary CSV — it reports nothing to skip, so a clean file parses exactly as it
/// did before this existed. It also wants the candidate block to be at least two records and to make up
/// most of what follows it, so one stray wide row in the middle of a headerless file cannot swallow
/// everything above it.
/// </para>
/// </summary>
internal static class DataBlockDetector
{
    /// <summary>
    /// The number of leading records to drop, or zero when the grid already starts at its data block.
    /// </summary>
    public static int Detect(IReadOnlyList<string?[]> records)
    {
        if (records.Count < 3)
        {
            // Too little to tell preamble from data; a header plus one row is a whole file.
            return 0;
        }

        int width = 0;
        foreach (string?[] record in records)
        {
            width = System.Math.Max(width, record.Length);
        }

        if (width < 2)
        {
            // A single-column file has no width signal to read.
            return 0;
        }

        int first = -1;
        for (int r = 0; r < records.Count; r++)
        {
            if (records[r].Length == width)
            {
                first = r;
                break;
            }
        }

        if (first <= 0)
        {
            return 0;
        }

        int atWidth = 0;
        for (int r = first; r < records.Count; r++)
        {
            if (records[r].Length == width)
            {
                atWidth++;
            }
        }

        int candidate = records.Count - first;
        if (atWidth < 2 || atWidth * 2 <= candidate)
        {
            return 0;
        }

        return first;
    }
}
