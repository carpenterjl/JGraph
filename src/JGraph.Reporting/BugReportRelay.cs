namespace JGraph.Reporting;

/// <summary>
/// Where bug reports go. The relay is a Google Apps Script web app deployed under the developer's
/// own account (<c>tools/bug-report-relay</c>) — the app POSTs the report there and the script sends
/// the email, so no credential of any kind ships in this binary. The URL is public by design: the
/// worst it can do is send its owner a bug report.
/// </summary>
public static class BugReportRelay
{
    /// <summary>
    /// The deployed relay's <c>/exec</c> URL (deployed 2026-08-30; see
    /// <c>tools/bug-report-relay/README.md</c> to redeploy). Were this ever put back to a
    /// placeholder like <c>https://REPLACE-AFTER-DEPLOY.invalid</c>, Send would simply disable
    /// itself and offer the clipboard instead — <see cref="IsConfigured"/> is the gate.
    /// </summary>
    public const string Url =
        "https://script.google.com/macros/s/AKfycbyM2ZWXwBtl29FKMh-AS5zCTg4tmc-8JQJsDJt74XQ05Yb8H7fvrc9WY8frfz-wJlG4oQ/exec";

    /// <summary>
    /// Whether <paramref name="url"/> names a real deployed relay rather than the placeholder. A
    /// report is user data, so it travels over https — plain http is accepted only to the machine's
    /// own loopback, which is how the transport is tested.
    /// </summary>
    public static bool IsConfigured(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback))
        && !parsed.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase);
}
