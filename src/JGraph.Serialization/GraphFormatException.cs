namespace JGraph.Serialization;

/// <summary>
/// Thrown when a ".graph" document cannot be read: malformed JSON, a missing/incorrect format tag, or
/// a schema version this build does not understand.
/// </summary>
public sealed class GraphFormatException : Exception
{
    public GraphFormatException(string message)
        : base(message)
    {
    }

    public GraphFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
