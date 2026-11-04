using System.IO;
using System.Windows.Input;
using JGraph.Deploy;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <content>
/// The SHARE drop-down (App Designer window plan W12): Standalone Desktop App opens the app's "Configure Standalone App for Sharing"
/// window over the saved app, as R2025b's <c>shareApp</c> and <c>DesktopAppSharingStrategy.showProjectWebUI</c> do; Web App stays
/// disabled, as R2025b's is without a compiler (web apps are out of JGraph's scope, compiler plan KD2, stage X2).
/// </content>
internal sealed partial class AppDesignerWindow
{
    /// <summary>Share &gt; Standalone Desktop App.</summary>
    public static readonly RoutedCommand ShareDesktop = new(nameof(ShareDesktop), typeof(AppDesignerWindow));

    private readonly Dictionary<string, AppShareWindow> _shareWindows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs a packaging script in the MATLAB session for a Share window's Package, as the compiler apps' scripts
    /// run (compiler plan K19), with what it printed for the packaging dialog's log; null with no session.
    /// </summary>
    public Func<string, Task<CompilerRunOutcome>>? ScriptRunner { get; set; }

    /// <summary>
    /// The dependency analysis of files, as <c>matlab.codetools.requiredFilesAndProducts</c> answers it in the
    /// MATLAB session, for a Share window's files required (the second argument stops at the top level); null with no session.
    /// </summary>
    public Func<IReadOnlyList<string>, bool, Task<DependencyReport>>? Analyzer { get; set; }

    private void BindShareCommands()
    {
        CommandBindings.Add(new CommandBinding(ShareDesktop, (_, _) => ShareActive(AppShareKind.Standalone),
            (_, e) => e.CanExecute = Active is not null && !_running));
    }

    /// <summary>
    /// A Share item: the app saved first (an app with no file asks where), then its window, one per app and kind,
    /// brought up again when it is already open.
    /// </summary>
    private void ShareActive(AppShareKind kind)
    {
        if (Active is not { } app || !SaveBeforeRun(app))
        {
            return;
        }

        string path = app.Document.Path!;
        string key = kind + "|" + path;
        if (_shareWindows.TryGetValue(key, out AppShareWindow? open))
        {
            open.Activate();
            return;
        }

        DesignAppDetailsValues details = app.Document.AppDetailsValues;
        // R2025b sets the installer's ApplicationName from App Details as it is; an empty name is refused by the
        // options, so the file's name stands in for one (a divergence).
        string name = details.Name.Length > 0 ? details.Name : Path.GetFileNameWithoutExtension(path);
        var window = new AppShareWindow(kind, path, new AppShareDetails(name, details.Version, details.Author, details.Summary, details.Description))
        {
            Owner = this,
            Analyzer = Analyzer,
            Runner = ScriptRunner,
        };
        window.Closed += (_, _) => _shareWindows.Remove(key);
        _shareWindows[key] = window;
        window.Show();
    }
}
