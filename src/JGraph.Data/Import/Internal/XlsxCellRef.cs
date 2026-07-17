namespace JGraph.Data.Import.Internal;

/// <summary>
/// Parses the column portion of an A1-style cell reference (e.g. <c>"AB12"</c>) into a zero-based
/// column index.
/// </summary>
internal static class XlsxCellRef
{
    /// <summary>Returns the zero-based column index for a cell reference, or -1 when it has no letters.</summary>
    public static int ColumnIndex(string cellRef)
    {
        int col = 0;
        bool any = false;
        foreach (char c in cellRef)
        {
            if (c >= 'A' && c <= 'Z')
            {
                col = (col * 26) + (c - 'A' + 1);
                any = true;
            }
            else if (c >= 'a' && c <= 'z')
            {
                col = (col * 26) + (c - 'a' + 1);
                any = true;
            }
            else
            {
                break; // reached the row digits
            }
        }

        return any ? col - 1 : -1;
    }
}
