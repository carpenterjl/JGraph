using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JGraph.Controls.Themes;
using JGraph.Deploy;

namespace JGraph.Application.Scripting;

/// <summary>
/// The packaging dialog a compiler app shows while it builds (compiler and SDK plan, stage K19), in
/// the shape of R2025b's <c>matlab.internal.compiler.ui.PackagingDialog</c>: the task's name and
/// version, a line per step ("Building Standalone App", "Creating Installer") that is scheduled, in
/// progress, done or failed, the log behind Show Log, and when it ends "Process completed
/// successfully" or "An error occurred while deploying" with Open Output Folder.
/// </summary>
internal sealed class CompilerPackagingDialog : Window
{
    private readonly List<(TextBlock Mark, TextBlock Label)> _steps = [];
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Height = 260,
        Visibility = Visibility.Collapsed,
    };
    private readonly Button _toggleLog = new() { Content = "Show Log", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _result = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
    private readonly Button _open = new() { Content = "Open Output Folder", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false };
    private readonly Button _close = new() { Content = "Close", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false, IsCancel = true };
    private string? _outputFolder;

    public CompilerPackagingDialog(CompilerTaskModel task, bool package)
    {
        ArgumentNullException.ThrowIfNull(task);
        Title = ProgressTitle(task.Target);
        Width = 580;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "PackagingDialog");

        var panel = new StackPanel { Margin = new Thickness(14) };
        var name = new TextBlock { Text = task.EffectiveName, FontSize = 18, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetAutomationId(name, "PackagingName");
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = task.Version, Margin = new Thickness(0, 0, 0, 10) });
        AddStep(panel, CompilerTargets.BuildStep(task.Target), "Step.Build");
        if (package)
        {
            AddStep(panel, CompilerTargets.PackageStep, "Step.Package");
        }

        AutomationProperties.SetAutomationId(_result, "PackagingResult");
        panel.Children.Add(_result);
        AutomationProperties.SetAutomationId(_toggleLog, "ToggleLog");
        _toggleLog.Click += (_, _) =>
        {
            bool shown = _log.Visibility == Visibility.Visible;
            _log.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            _toggleLog.Content = shown ? "Show Log" : "Hide Log";
        };
        panel.Children.Add(_toggleLog);
        AutomationProperties.SetAutomationId(_log, "PackagingLog");
        _log.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_log);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        AutomationProperties.SetAutomationId(_open, "OpenOutputFolder");
        _open.Margin = new Thickness(0, 0, 8, 0);
        _open.Click += (_, _) =>
        {
            if (_outputFolder is { } folder && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
        };
        AutomationProperties.SetAutomationId(_close, "ClosePackaging");
        _close.Click += (_, _) => Close();
        buttons.Children.Add(_open);
        buttons.Children.Add(_close);
        panel.Children.Add(buttons);
        Content = panel;
        Closing += (_, e) => e.Cancel = !_close.IsEnabled;
    }

    /// <summary>The dialog's title: R2025b's "Standalone App Packaging Progress" (compiler_ui_common), and the same form for the other targets.</summary>
    public static string ProgressTitle(CompilerTarget target) =>
        target == CompilerTarget.StandaloneApplication ? "Standalone App Packaging Progress" : CompilerTargets.TypeName(target) + " Packaging Progress";

    private void AddStep(StackPanel panel, string text, string id)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var mark = new TextBlock { Text = "○", Width = 20, FontSize = 14 };
        var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(label, id);
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        row.Children.Add(mark);
        row.Children.Add(label);
        panel.Children.Add(row);
        _steps.Add((mark, label));
    }

    /// <summary>Shows step <paramref name="index"/> in progress.</summary>
    public void StartStep(int index)
    {
        (TextBlock mark, TextBlock label) = _steps[index];
        mark.Text = "…";
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Accent);
    }

    /// <summary>Shows step <paramref name="index"/> done or failed, and adds what it printed to the log.</summary>
    public void EndStep(int index, CompilerRunOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        (TextBlock mark, TextBlock label) = _steps[index];
        mark.Text = outcome.Success ? "✓" : "✗";
        label.SetResourceReference(TextBlock.ForegroundProperty, outcome.Success ? ThemeKeys.Success : ThemeKeys.Error);
        _log.AppendText(outcome.Log);
        if (!outcome.Success && outcome.Message is { } message)
        {
            _log.AppendText("ERROR: " + message + Environment.NewLine);
            _log.Visibility = Visibility.Visible;
            _toggleLog.Content = "Hide Log";
        }

        _log.ScrollToEnd();
    }

    /// <summary>Ends the dialog's run: the result line, Open Output Folder for a success, Close.</summary>
    public void Finish(CompilerRunOutcome outcome, string outputFolder)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _result.Text = outcome.Success ? "Process completed successfully" : "An error occurred while deploying";
        _result.SetResourceReference(TextBlock.ForegroundProperty, outcome.Success ? ThemeKeys.Success : ThemeKeys.Error);
        _outputFolder = outputFolder;
        _open.IsEnabled = outcome.Success && Directory.Exists(outputFolder);
        _close.IsEnabled = true;
    }
}
