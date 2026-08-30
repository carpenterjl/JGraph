namespace JGraph.Application.Services;

/// <summary>
/// Shows the bug-report dialog — from the Help menu while everything is fine, and from the crash
/// guard when it is not.
/// </summary>
public interface IBugReportService
{
    /// <summary>Opens the dialog for a report the user chose to write.</summary>
    /// <param name="script">The script on screen, offered as an optional attachment, or null.</param>
    void ShowReportDialog(BugReportScriptSnapshot? script);

    /// <summary>
    /// Opens the dialog for a crash, prefilled with <paramref name="exception"/>. Blocks until the
    /// dialog closes — the caller exits the application immediately after.
    /// </summary>
    /// <param name="exception">What went wrong, shown to the user verbatim before it is sent.</param>
    /// <param name="script">The script on screen, offered as an optional attachment, or null.</param>
    void ShowCrashDialog(Exception exception, BugReportScriptSnapshot? script);
}

/// <summary>The script a bug report can attach: its file name and the text as edited, saved or not.</summary>
/// <param name="FileName">The document's file name, or null for a never-saved script.</param>
/// <param name="Text">The editor's text at the moment of the report.</param>
public sealed record BugReportScriptSnapshot(string? FileName, string Text);
