using System.Text.Json;
using JGraph.Reporting;
using Xunit;

namespace JGraph.Tests.Reporting;

/// <summary>
/// The JSON the relay parses: camelCase names (the relay reads <c>r.appVersion</c> and friends by
/// those exact spellings), nulls omitted rather than sent, and a faithful round trip.
/// </summary>
public class BugReportPayloadTests
{
    private static BugReportPayload Sample(string? replyTo = "a@b.c", string? scriptText = "x = 1;") => new(
        Subject: "JGraph Bug Report: t - 30082026 - 0.2.0",
        Title: "t",
        Description: "It broke.",
        ReplyTo: replyTo,
        AppVersion: "0.2.0",
        OsVersion: "Test OS 1.0",
        TimestampUtc: new DateTimeOffset(2026, 8, 30, 19, 30, 0, TimeSpan.Zero),
        IsCrash: false,
        Exception: null,
        ScriptFileName: scriptText is null ? null : "plot_test.m",
        ScriptText: scriptText);

    [Fact]
    public void TheJsonUsesTheNamesTheRelayReads()
    {
        using JsonDocument json = JsonDocument.Parse(Sample().ToJson());

        // Code.gs reads exactly these properties; a renamed one is a silently empty email field.
        foreach (string name in new[]
                 {
                     "subject", "title", "description", "replyTo", "appVersion", "osVersion",
                     "timestampUtc", "isCrash", "scriptFileName", "scriptText",
                 })
        {
            Assert.True(json.RootElement.TryGetProperty(name, out _), "missing '" + name + "'");
        }
    }

    [Fact]
    public void NullsAreOmittedNotSent()
    {
        using JsonDocument json = JsonDocument.Parse(Sample(replyTo: null, scriptText: null).ToJson());

        Assert.False(json.RootElement.TryGetProperty("replyTo", out _));
        Assert.False(json.RootElement.TryGetProperty("scriptText", out _));
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
    }

    [Fact]
    public void RoundTripsThroughItsOwnJson()
    {
        BugReportPayload payload = Sample();

        Assert.Equal(payload, BugReportPayload.FromJson(payload.ToJson()));
    }

    [Fact]
    public void TheClipboardTextCarriesTheWholeReport()
    {
        string text = Sample().ToClipboardText();

        Assert.Contains("JGraph Bug Report: t - 30082026 - 0.2.0", text, StringComparison.Ordinal);
        Assert.Contains("It broke.", text, StringComparison.Ordinal);
        Assert.Contains("App: 0.2.0", text, StringComparison.Ordinal);
        Assert.Contains("Reply to: a@b.c", text, StringComparison.Ordinal);
        Assert.Contains("Script (plot_test.m):", text, StringComparison.Ordinal);
        Assert.Contains("x = 1;", text, StringComparison.Ordinal);
    }
}
