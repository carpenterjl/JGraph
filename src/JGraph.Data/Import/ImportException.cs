namespace JGraph.Data.Import;

/// <summary>
/// Thrown when data cannot be imported: a missing or unreadable file, empty input, a corrupt workbook,
/// or an unknown worksheet. Recoverable parsing issues (ragged rows, unparseable cells) surface as
/// <see cref="ImportResult.Warnings"/> instead.
/// </summary>
public sealed class ImportException : Exception
{
    public ImportException(string message)
        : base(message)
    {
    }

    public ImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
