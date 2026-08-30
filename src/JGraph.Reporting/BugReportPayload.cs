using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.Reporting;

/// <summary>
/// One bug report, exactly as it leaves the machine. Every field here is shown to the user in the
/// dialog before Send — nothing rides along that they have not seen (no log tails, no machine or
/// user names) — and the subject is composed on this side (<see cref="BugReportSubject"/>) so its
/// format is a tested fact rather than a hope about the relay.
/// </summary>
/// <param name="Subject">The email subject, used verbatim by the relay.</param>
/// <param name="Title">The user's one-line summary.</param>
/// <param name="Description">What happened, in the user's words.</param>
/// <param name="ReplyTo">An email to answer when the bug is fixed, or null when left blank.</param>
/// <param name="AppVersion">The three-part application version.</param>
/// <param name="OsVersion">The operating system, as .NET names it.</param>
/// <param name="TimestampUtc">When the report was composed.</param>
/// <param name="IsCrash">Whether the report was raised by the crash guard rather than the menu.</param>
/// <param name="Exception">The full exception text on the crash path, or null.</param>
/// <param name="ScriptFileName">The attached script's file name, or null when nothing is attached.</param>
/// <param name="ScriptText">The attached script's text, or null when nothing is attached.</param>
public sealed record BugReportPayload(
    string Subject,
    string Title,
    string Description,
    string? ReplyTo,
    string AppVersion,
    string OsVersion,
    DateTimeOffset TimestampUtc,
    bool IsCrash,
    string? Exception,
    string? ScriptFileName,
    string? ScriptText)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The report as the relay receives it: camelCase JSON, nulls omitted.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parses a report back from <see cref="ToJson"/> output (tests, mainly).</summary>
    public static BugReportPayload? FromJson(string json) =>
        JsonSerializer.Deserialize<BugReportPayload>(json, Options);

    /// <summary>
    /// The report as readable text, for the clipboard when sending fails or is not configured — the
    /// one path that must work with no network and no relay, so a report is never simply lost.
    /// </summary>
    public string ToClipboardText()
    {
        var text = new StringBuilder();
        text.AppendLine(Subject);
        text.AppendLine();
        text.AppendLine(Description);
        text.AppendLine();
        text.AppendLine("---");
        text.AppendLine("App: " + AppVersion);
        text.AppendLine("OS: " + OsVersion);
        text.AppendLine("UTC: " + TimestampUtc.UtcDateTime.ToString("u", System.Globalization.CultureInfo.InvariantCulture));
        if (ReplyTo is not null)
        {
            text.AppendLine("Reply to: " + ReplyTo);
        }

        if (IsCrash)
        {
            text.AppendLine("CRASH REPORT");
        }

        if (Exception is not null)
        {
            text.AppendLine();
            text.AppendLine("Exception:");
            text.AppendLine(Exception);
        }

        if (ScriptText is not null)
        {
            text.AppendLine();
            text.AppendLine("Script (" + (ScriptFileName ?? "untitled") + "):");
            text.AppendLine(ScriptText);
        }

        return text.ToString();
    }
}
