using System.Windows;
using JGraph.Application.Scripting;
using JGraph.Plugins;
using JGraph.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace JGraph.Application.Startup;

/// <summary>
/// Brings up the interactive application: splash, warm-up, shell. The warm-up exists because the two
/// slowest things at startup — scanning the plugins folder and probing the machine for a CPython
/// runtime — are both lazy DI singletons that would otherwise be resolved at an arbitrary later
/// moment, freezing the UI then instead of now.
/// </summary>
public static class InteractiveStartup
{
    /// <summary>
    /// Warms the container behind a splash and returns the restored shell window, ready to show.
    /// </summary>
    /// <param name="services">The composition root.</param>
    public static async Task<ScriptWorkspaceWindow> PrepareShellAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var splash = new SplashWindow();
        splash.Show();
        try
        {
            splash.Report("Loading plugins…", 0.15);
            await Task.Run(() =>
            {
                // Both are thread-safe to construct: the registry only reads assemblies from disk,
                // and the Python engine's constructor merely probes for an interpreter. Neither
                // touches WPF, and nothing here initialises CPython (that is thread-affine and
                // deliberately deferred to the first Python run).
                _ = services.GetRequiredService<PluginRegistry>();

                splash.Report("Locating script engines…", 0.5);
                foreach (IScriptEngine engine in services.GetServices<IScriptEngine>())
                {
                    _ = engine.IsAvailable;
                }
            }).ConfigureAwait(true);

            splash.Report("Restoring workspace…", 0.8);
            var shell = services.GetRequiredService<ScriptWorkspaceWindow>();
            shell.RestoreSession();
            splash.Report("Ready", 1.0);
            return shell;
        }
        finally
        {
            splash.Close();
        }
    }
}
