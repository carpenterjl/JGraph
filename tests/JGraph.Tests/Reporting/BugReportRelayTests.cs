using JGraph.Reporting;
using Xunit;

namespace JGraph.Tests.Reporting;

/// <summary>
/// The one rule about the relay URL: the shipped placeholder is not a configuration, and only a
/// real https URL is. This is what keeps Send disabled in a build nobody has pointed anywhere.
/// </summary>
public class BugReportRelayTests
{
    [Fact]
    public void ThePlaceholderIsNotConfigured() =>
        Assert.False(BugReportRelay.IsConfigured("https://REPLACE-AFTER-DEPLOY.invalid"));

    [Fact]
    public void TheShippedUrlIsConfigured() =>
        // The constant points at the live deployment (M114). If this fails, the placeholder came
        // back and every shipped build has Send disabled - which had better be deliberate.
        Assert.True(BugReportRelay.IsConfigured(BugReportRelay.Url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://script.google.com/macros/s/x/exec")] // https only — a report is user data
    public void NonRelaysAreNotConfigured(string? url) => Assert.False(BugReportRelay.IsConfigured(url));

    [Fact]
    public void ADeployedUrlIsConfigured() =>
        Assert.True(BugReportRelay.IsConfigured("https://script.google.com/macros/s/AKfycbTest/exec"));

    [Fact]
    public void LoopbackHttpIsConfiguredForTesting() =>
        Assert.True(BugReportRelay.IsConfigured("http://127.0.0.1:8080/report"));
}
