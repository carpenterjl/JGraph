using JGraph.Application.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IScriptingService"/>: brings the scripting workspace to the
/// front. Since M30 that window is the application shell — a DI singleton created at startup and
/// already visible — so this is now an activation service rather than a window factory.
/// </summary>
public sealed class ScriptingService : IScriptingService
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the service over the container that owns the shell window.</summary>
    public ScriptingService(IServiceProvider services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public void OpenEditor() => Open();

    /// <inheritdoc />
    public void OpenEditorAndRun(string statement, string? logFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(statement);
        ScriptWorkspaceWindow window = Open();
        if (logFile is { Length: > 0 })
        {
            window.SetLogFile(logFile);
        }

        window.RunStartupStatement(statement);
    }

    private ScriptWorkspaceWindow Open()
    {
        // Never Owner-ed: the shell is the main window, and an owned window can never be one.
        var window = _services.GetRequiredService<ScriptWorkspaceWindow>();
        if (!window.IsVisible)
        {
            window.RestoreSession(); // no-op once the startup sequence has already run it
            window.Show();
        }

        window.Activate();
        return window;
    }
}
