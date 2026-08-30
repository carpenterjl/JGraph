namespace JGraph.Reporting;

/// <summary>
/// Turns what the dialog collected into a <see cref="BugReportPayload"/>. Pure — the dialog hands in
/// every environmental fact (version, OS, clock) so the whole shape is testable without a window.
/// </summary>
public static class BugReportBuilder
{
    /// <summary>
    /// Builds the payload. The title is trimmed (it rides in the subject line); a blank reply-to or
    /// an unattached script becomes null rather than an empty field, so the JSON says what the email
    /// will show — nothing.
    /// </summary>
    public static BugReportPayload Build(
        string title,
        string description,
        string? replyTo,
        string appVersion,
        string osVersion,
        DateTimeOffset now,
        bool isCrash = false,
        string? exception = null,
        string? scriptFileName = null,
        string? scriptText = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(osVersion);

        bool hasScript = !string.IsNullOrWhiteSpace(scriptText);
        return new BugReportPayload(
            // The subject's date is the caller's wall-clock date — the date already inside `now` —
            // not a re-conversion through the machine zone, which would shift it near midnight.
            Subject: BugReportSubject.Format(title, now.DateTime, appVersion),
            Title: title.Trim(),
            Description: description,
            ReplyTo: string.IsNullOrWhiteSpace(replyTo) ? null : replyTo.Trim(),
            AppVersion: appVersion,
            OsVersion: osVersion,
            TimestampUtc: now.ToUniversalTime(),
            IsCrash: isCrash,
            Exception: string.IsNullOrWhiteSpace(exception) ? null : exception,
            ScriptFileName: hasScript ? (string.IsNullOrWhiteSpace(scriptFileName) ? null : scriptFileName) : null,
            ScriptText: hasScript ? scriptText : null);
    }
}
