using JGraph.Reporting;
using Xunit;

namespace JGraph.Tests.Reporting;

/// <summary>
/// The email subject is a published format — <c>JGraph Bug Report: {title} - DDMMYYYY -
/// {version}</c> — and the relay uses it verbatim, so this is where it is pinned character for
/// character.
/// </summary>
public class BugReportSubjectTests
{
    [Fact]
    public void ComposesTheExactSubject()
    {
        string subject = BugReportSubject.Format("My bug", new DateTime(2026, 3, 5), "0.2.0");

        Assert.Equal("JGraph Bug Report: My bug - 05032026 - 0.2.0", subject);
    }

    [Fact]
    public void TheDateIsDayFirstAndZeroPadded()
    {
        // 1 December, not 12 January: the format is DDMMYYYY, so a transposed implementation is
        // caught by a date whose day and month differ once padded.
        string subject = BugReportSubject.Format("t", new DateTime(2026, 12, 1), "1.0.0");

        Assert.Contains(" - 01122026 - ", subject, StringComparison.Ordinal);
    }

    [Fact]
    public void TrimsTheTitle()
    {
        string subject = BugReportSubject.Format("  spaced out\t", new DateTime(2026, 1, 2), "0.2.0");

        Assert.Equal("JGraph Bug Report: spaced out - 02012026 - 0.2.0", subject);
    }
}
