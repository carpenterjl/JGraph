using System.IO;
using System.Text;
using JGraph.Deploy;
using JGraph.Scripting;

namespace JGraph.Application.Scripting;

/// <summary>
/// The window's half of the compiler apps (compiler and SDK plan, stage K19): the door
/// <c>standaloneApplicationCompiler</c> and its siblings come through, the windows themselves (one
/// per target, made when first wanted, and the gallery), the Deploy menu that opens them by the
/// same commands, and the runs of their scripts in the MATLAB session with the console's lines
/// captured for the packaging dialog's log.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private readonly Dictionary<CompilerTarget, CompilerAppWindow> _compilerApps = [];
    private CompilerAppGalleryWindow? _compilerGallery;
    private IScriptCompilerApps? _compilerAppsHost;
    private CompilerAppServices? _compilerServices;
    private StringBuilder? _compilerLog;

    private IScriptCompilerApps CompilerAppsDoor => _compilerAppsHost ??= new CompilerAppsHost(this);

    /// <summary>
    /// Brings up the app for <paramref name="target"/> (the gallery for null) with
    /// <paramref name="mainFile"/> as its main file when given: what the commands and the Deploy menu
    /// both do.
    /// </summary>
    /// <returns>Null when it opened, else why not.</returns>
    private string? OpenCompilerApp(CompilerTarget? target, string? mainFile, CompilerAppServices services)
    {
        _compilerServices = services;
        if (target is not { } wanted)
        {
            if (_compilerGallery is null)
            {
                _compilerGallery = new CompilerAppGalleryWindow(chosen => OpenCompilerApp(chosen, null, services));
                _compilerGallery.Closed += (_, _) => _compilerGallery = null;
            }

            _compilerGallery.Show();
            _compilerGallery.Activate();
            return null;
        }

        if (!_compilerApps.TryGetValue(wanted, out CompilerAppWindow? window))
        {
            window = new CompilerAppWindow(wanted, services) { Runner = RunCompilerScriptAsync };
            window.Closed += (_, _) => _compilerApps.Remove(wanted);
            _compilerApps[wanted] = window;
        }

        if (mainFile is not null)
        {
            try
            {
                window.SetMainFile(mainFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }

        window.Bring();
        return null;
    }

    /// <summary>The Deploy menu: the app's command run at the prompt, so the window opens as it does from a script.</summary>
    private void OpenCompilerAppFromMenu(string command)
    {
        _ = RunWorkspaceStatementAsync("MATLAB", command);
    }

    /// <summary>
    /// Runs a compiler app's script in the MATLAB session as the prompt would, with what it prints
    /// captured for the app's dialog as well as shown in the console.
    /// </summary>
    private async Task<CompilerRunOutcome> RunCompilerScriptAsync(string code)
    {
        if (SessionFor("MATLAB") is not { } session)
        {
            return new CompilerRunOutcome(false, "There is no MATLAB session to build in.", "");
        }

        await YieldPumpAsync().ConfigureAwait(true);
        if (!_session.TryBeginRun("MATLAB"))
        {
            return new CompilerRunOutcome(false, "MATLAB is busy; wait for the prompt before building.", "");
        }

        var log = new StringBuilder();
        _compilerLog = log;
        _runOutputLines = 0;
        _runOutputTruncated = false;
        _cts = new System.Threading.CancellationTokenSource();
        ScriptRunResult result;
        try
        {
            result = await session.ExecuteAsync(code, string.Empty, _cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            result = ScriptRunResult.Failed(ex.Message);
        }
        finally
        {
            _compilerLog = null;
            _cts.Dispose();
            _cts = null;
            _session.EndRun();
        }

        ShowRunResult(result, announceSuccess: false, "MATLAB");
        PumpGraphicsEventsWhenIdle();
        string text;
        lock (log)
        {
            text = log.ToString();
        }

        return new CompilerRunOutcome(result.Success, result.Success ? null : result.Message, text);
    }

    /// <summary>Keeps a copy of script output for a compiler app's dialog while one of its scripts runs.</summary>
    private void CaptureCompilerOutput(string text, bool newline)
    {
        if (_compilerLog is { } log)
        {
            lock (log)
            {
                log.Append(text);
                if (newline)
                {
                    log.Append(Environment.NewLine);
                }
            }
        }
    }

    /// <summary>What the compiler apps' commands reach from a script: the windows, opened on the UI thread.</summary>
    private sealed class CompilerAppsHost(ScriptWorkspaceWindow window) : IScriptCompilerApps
    {
        public string? Open(CompilerTarget? target, string? mainFile, CompilerAppServices services) =>
            window.Dispatcher.Invoke(() => window.OpenCompilerApp(target, mainFile, services));
    }
}
