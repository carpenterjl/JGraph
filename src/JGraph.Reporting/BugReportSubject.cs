using System.Globalization;

namespace JGraph.Reporting;

/// <summary>
/// The one place the email subject line is spelled. The relay uses the string verbatim, so the
/// format lives on this side where a test can pin it: <c>JGraph Bug Report: {title} - DDMMYYYY -
/// {version}</c>, day-month-year with no separators.
/// </summary>
public static class BugReportSubject
{
    /// <summary>Composes the subject for a report titled <paramref name="title"/> on <paramref name="date"/>.</summary>
    public static string Format(string title, DateTime date, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(appVersion);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"JGraph Bug Report: {title.Trim()} - {date:ddMMyyyy} - {appVersion}");
    }
}
