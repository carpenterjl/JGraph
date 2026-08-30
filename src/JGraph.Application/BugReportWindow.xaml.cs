using System.Windows;
using System.Windows.Controls;
using JGraph.Application.Services;
using JGraph.Reporting;

namespace JGraph.Application;

/// <summary>
/// The bug-report dialog, in two moods: opened from Help it is a blank form; opened by the crash
/// guard it arrives prefilled with the exception and says plainly that JGraph will close. Either
/// way the whole report is on screen before Send — what the user reads is exactly what travels —
/// and Copy Report is the path that still works with no network and no relay.
/// </summary>
public partial class BugReportWindow : Window
{
    private readonly IBugReportTransport _transport;
    private readonly string _appVersion;
    private readonly string _osVersion;
    private readonly BugReportScriptSnapshot? _script;
    private readonly Exception? _exception;
    private readonly Action<string?> _rememberReplyTo;
    private bool _sending;

    /// <summary>Creates the dialog. A non-null <paramref name="exception"/> selects the crash mood.</summary>
    public BugReportWindow(
        IBugReportTransport transport,
        string appVersion,
        string osVersion,
        string? replyTo,
        BugReportScriptSnapshot? script,
        Exception? exception,
        Action<string?> rememberReplyTo)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
        _osVersion = osVersion ?? throw new ArgumentNullException(nameof(osVersion));
        _script = script;
        _exception = exception;
        _rememberReplyTo = rememberReplyTo ?? throw new ArgumentNullException(nameof(rememberReplyTo));
        InitializeComponent();

        ReplyToBox.Text = replyTo ?? string.Empty;
        if (script is not null)
        {
            AttachScriptBox.Content = "Attach the open script (" + (script.FileName ?? "untitled") + ")";
            AttachScriptBox.Visibility = Visibility.Visible;
        }

        if (exception is not null)
        {
            Title = "Report a Crash";
            CrashHeader.Visibility = Visibility.Visible;
            CancelButton.Content = "Close Without Sending";
            CancelButton.Width = double.NaN;
            CancelButton.Padding = new Thickness(12, 2, 12, 2);
            TitleBox.Text = "Crash: " + exception.GetType().Name;
            DescriptionBox.Text = exception.ToString();
        }

        if (!_transport.IsConfigured)
        {
            NotConfiguredLabel.Visibility = Visibility.Visible;
        }

        UpdateSendEnabled();
    }

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => UpdateSendEnabled();

    private void UpdateSendEnabled() =>
        SendButton.IsEnabled = !_sending
            && _transport.IsConfigured
            && !string.IsNullOrWhiteSpace(TitleBox.Text)
            && !string.IsNullOrWhiteSpace(DescriptionBox.Text);

    private BugReportPayload BuildPayload() => BugReportBuilder.Build(
        title: TitleBox.Text,
        description: DescriptionBox.Text,
        replyTo: ReplyToBox.Text,
        appVersion: _appVersion,
        osVersion: _osVersion,
        now: DateTimeOffset.Now,
        isCrash: _exception is not null,
        exception: _exception?.ToString(),
        scriptFileName: AttachScriptBox.IsChecked == true ? _script?.FileName : null,
        scriptText: AttachScriptBox.IsChecked == true ? _script?.Text : null);

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        BugReportPayload payload = BuildPayload();
        _rememberReplyTo(payload.ReplyTo);

        _sending = true;
        SetInputsEnabled(false);
        UpdateSendEnabled();
        ShowStatus("Sending…");
        try
        {
            BugReportSendResult result = await _transport.SendAsync(payload);
            if (result.Ok)
            {
                DialogResult = true;
                return;
            }

            ShowStatus((result.Error ?? "The report could not be sent.")
                + " Copy Report keeps it on the clipboard so it is not lost.");
        }
        finally
        {
            _sending = false;
            if (IsLoaded && DialogResult is null)
            {
                SetInputsEnabled(true);
                UpdateSendEnabled();
            }
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BuildPayload().ToClipboardText());
            ShowStatus("The report is on the clipboard.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another process holds the clipboard open; the standard WPF hazard.
            ShowStatus("The clipboard is in use by another program — try Copy Report again.");
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        // Send itself is owned by UpdateSendEnabled, which already knows about _sending.
        TitleBox.IsEnabled = enabled;
        DescriptionBox.IsEnabled = enabled;
        ReplyToBox.IsEnabled = enabled;
        AttachScriptBox.IsEnabled = enabled;
    }

    private void ShowStatus(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.Visibility = Visibility.Visible;
    }
}
