using JGraph.Reporting;
using Xunit;

namespace JGraph.Tests.Reporting;

/// <summary>
/// The builder turns dialog fields into the payload. What matters: blanks become nulls (the JSON
/// says what the email will show — nothing), the subject rides the local date, and the timestamp
/// leaves in UTC.
/// </summary>
public class BugReportBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 14, 30, 0, TimeSpan.FromHours(-5));

    private static BugReportPayload Build(
        string? replyTo = null, string? exception = null, string? scriptFileName = null, string? scriptText = null) =>
        BugReportBuilder.Build(
            "  A title  ", "It broke.", replyTo, "0.2.0", "Test OS 1.0", Now,
            isCrash: exception is not null, exception: exception,
            scriptFileName: scriptFileName, scriptText: scriptText);

    [Fact]
    public void TrimsTheTitleAndComposesTheSubjectFromTheLocalDate()
    {
        BugReportPayload payload = Build();

        Assert.Equal("A title", payload.Title);
        Assert.Equal("JGraph Bug Report: A title - 30082026 - 0.2.0", payload.Subject);
    }

    [Fact]
    public void TheTimestampLeavesInUtc()
    {
        BugReportPayload payload = Build();

        Assert.Equal(TimeSpan.Zero, payload.TimestampUtc.Offset);
        Assert.Equal(new DateTime(2026, 8, 30, 19, 30, 0), payload.TimestampUtc.DateTime);
    }

    [Fact]
    public void ABlankReplyToBecomesNull()
    {
        Assert.Null(Build(replyTo: "   ").ReplyTo);
        Assert.Equal("a@b.c", Build(replyTo: " a@b.c ").ReplyTo);
    }

    [Fact]
    public void AnUnattachedScriptBecomesNull()
    {
        BugReportPayload payload = Build(scriptFileName: "orphan.m", scriptText: "   ");

        // No text means no attachment — a file name without its script would be a lie.
        Assert.Null(payload.ScriptText);
        Assert.Null(payload.ScriptFileName);
    }

    [Fact]
    public void AnAttachedScriptKeepsItsNameAndText()
    {
        BugReportPayload payload = Build(scriptFileName: "plot_test.m", scriptText: "x = 1;");

        Assert.Equal("plot_test.m", payload.ScriptFileName);
        Assert.Equal("x = 1;", payload.ScriptText);
    }

    [Fact]
    public void ACrashCarriesItsExceptionText()
    {
        BugReportPayload payload = Build(exception: "System.InvalidOperationException: boom");

        Assert.True(payload.IsCrash);
        Assert.Equal("System.InvalidOperationException: boom", payload.Exception);
        Assert.False(Build().IsCrash);
        Assert.Null(Build().Exception);
    }
}
