namespace JGraph.Reporting;

/// <summary>
/// Carries a report to wherever reports go. The seam exists so the dialog can be exercised against a
/// fake and so the one piece of networking in the product is a single replaceable type.
/// </summary>
public interface IBugReportTransport
{
    /// <summary>
    /// Whether this transport can actually deliver — false while <see cref="BugReportRelay.Url"/> is
    /// still the placeholder, which the dialog turns into a disabled Send and an explanation.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends <paramref name="payload"/>. Never throws — every failure, from no network to a refusal
    /// by the relay, comes back as <see cref="BugReportSendResult.Error"/> for the dialog to show.
    /// </summary>
    Task<BugReportSendResult> SendAsync(BugReportPayload payload, CancellationToken cancellationToken = default);
}
