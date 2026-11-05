using System.Windows.Controls;
using System.Windows.Threading;
using JGraph.Controls.Designer;
using JGraph.Scripting.Analysis;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Code Analyzer and the coding alerts in App Designer's Code View (App Designer window plan W10): the class text
/// as the code view shows it is analyzed when the app opens, after the designer writes the class again and half a
/// second after the last keystroke (R2025b's <c>ProgrammingAlertPlugin</c> waits 500 ms); App Designer's own alerts -
/// a graphics function called in the user's code without an axes (<see cref="DesignCodingAlerts"/>) - join the
/// messages on the editable lines while the "Enable app coding alerts" setting is on, one per line and not on a line
/// the analyzer already warns about, as R2025b's plugin adds them. "Disable coding alerts" is in the code view's menu.
/// </summary>
partial class AppDesignerWindow
{
    private static readonly TimeSpan AlertsPause = TimeSpan.FromMilliseconds(500);
    private bool _analysisSettingWired;

    private void WireAnalysis(AppTab app)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AlertsPause };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            AnalyzeApp(app);
        };
        void Later()
        {
            timer.Stop();
            timer.Start();
        }

        app.CodeView.Editor.TextChanged += (_, _) => Later();
        app.CodeView.TextReplaced += (_, _) => Later();
        app.CodeView.Editor.FixApplied += (_, _) =>
        {
            timer.Stop();
            AnalyzeApp(app);
        };
        if (app.CodeView.Editor.TextContextMenu is { } menu)
        {
            var disable = new MenuItem { Header = DesignCodingAlerts.DisableText, IsCheckable = true };
            disable.Click += (_, _) => Settings.Set(DesignSettings.ShowProgrammingTips, !disable.IsChecked);
            menu.Opened += (_, _) => disable.IsChecked = !Settings.GetBool(DesignSettings.ShowProgrammingTips);
            menu.Items.Add(new Separator());
            menu.Items.Add(disable);
        }

        if (!_analysisSettingWired)
        {
            _analysisSettingWired = true;
            Settings.Changed += (_, name) =>
            {
                if (name == DesignSettings.ShowProgrammingTips)
                {
                    foreach (AppTab open in Apps)
                    {
                        AnalyzeApp(open);
                    }
                }
            };
        }

        AnalyzeApp(app);
    }

    private void AnalyzeApp(AppTab app)
    {
        string text = app.CodeView.Editor.ScriptText;
        string name = app.Document.Path ?? (app.Document.Code?.ClassName ?? "App") + ".m";
        var messages = new List<CodeMessage>(ScriptWorkspaceWindow.AnalyzeText(text, name));
        if (Settings.GetBool(DesignSettings.ShowProgrammingTips) && app.Document.Code is not null)
        {
            List<string> lines = [.. text.Split('\n').Select(static l => l.TrimEnd('\r'))];
            IReadOnlySet<int> editable = DesignCodeView.EditableLinesOf(text);
            string[] axes = [.. app.Document.Design.Components.Where(static c => c.Type is "matlab.ui.control.UIAxes" or "matlab.graphics.axis.Axes").Select(static c => c.CodeName)];
            var warned = new HashSet<int>(messages.Where(static m => m.Severity != CodeSeverity.Info).Select(static m => m.Line));
            foreach (CodeMessage alert in DesignCodingAlerts.Find(lines, editable, axes))
            {
                if (!warned.Contains(alert.Line))
                {
                    messages.Add(alert);
                }
            }
        }

        app.CodeView.Editor.SetCodeMessages([.. messages.OrderBy(static m => m.Line).ThenBy(static m => m.StartColumn)]);
    }
}
