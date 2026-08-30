namespace JGraph.Reporting;

/// <summary>The outcome of one send: delivered, or a reason it was not.</summary>
/// <param name="Ok">Whether the relay accepted the report.</param>
/// <param name="Error">What went wrong when it did not, in words fit for the dialog.</param>
public sealed record BugReportSendResult(bool Ok, string? Error)
{
    /// <summary>The report was accepted.</summary>
    public static BugReportSendResult Sent { get; } = new(true, null);

    /// <summary>The report was not accepted, for the given reason.</summary>
    public static BugReportSendResult Failed(string error) => new(false, error);
}
