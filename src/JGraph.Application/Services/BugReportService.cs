using System.Linq;
using System.Windows;
using JGraph.Reporting;

namespace JGraph.Application.Services;

/// <summary>
/// Builds the bug-report dialog's environment — version, OS, the remembered reply-to — and shows it
/// modally over the active window, exactly as <see cref="OptionsService"/> does for Options. The
/// reply-to the user types is saved back to settings so the next report starts with it.
/// </summary>
public sealed class BugReportService : IBugReportService
{
    private readonly ISettingsService _settings;
    private readonly IBugReportTransport _transport;

    /// <summary>Creates the service over the settings store and the transport reports leave by.</summary>
    public BugReportService(ISettingsService settings, IBugReportTransport transport)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <inheritdoc />
    public void ShowReportDialog(BugReportScriptSnapshot? script) => Show(null, script);

    /// <inheritdoc />
    public void ShowCrashDialog(Exception exception, BugReportScriptSnapshot? script)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Show(exception, script);
    }

    private void Show(Exception? exception, BugReportScriptSnapshot? script)
    {
        var window = new BugReportWindow(
            _transport,
            appVersion: typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            osVersion: Environment.OSVersion.VersionString,
            replyTo: _settings.Current.BugReportReplyTo,
            script: script,
            exception: exception,
            rememberReplyTo: RememberReplyTo)
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive),
        };
        window.ShowDialog();
    }

    /// <summary>Keeps the reply-to for next time. Best-effort, like every settings write.</summary>
    private void RememberReplyTo(string? replyTo)
    {
        if (string.Equals(_settings.Current.BugReportReplyTo, replyTo, StringComparison.Ordinal))
        {
            return;
        }

        UserSettings updated = _settings.Current.Clone();
        updated.BugReportReplyTo = replyTo;
        _settings.Save(updated);
    }
}
