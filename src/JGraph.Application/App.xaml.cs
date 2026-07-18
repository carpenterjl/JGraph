using System.IO;
using System.Windows;
using JGraph.Application.Mvvm;
using JGraph.Application.Services;
using JGraph.Numerics;
using JGraph.Plugins;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Microsoft.Extensions.DependencyInjection;

namespace JGraph.Application;

/// <summary>
/// The MVVM application shell. Its <see cref="OnStartup"/> builds the dependency-injection container
/// (the composition root), then resolves and shows the figure window.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Delete numeric-buffer temp files orphaned by power loss (a crash alone never orphans:
        // they are opened delete-on-close). Fire-and-forget; files held by live processes are skipped.
        Task.Run(() => BufferAllocator.SweepOrphans(BufferAllocator.DefaultMappedDirectory));

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        _services = collection.BuildServiceProvider();

        var window = _services.GetRequiredService<FigureWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // The plugin registry: the built-in standard library (Light/Dark/Presentation/IEEE themes and
        // colormaps) plus anything discovered in a "plugins" folder next to the executable.
        string pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        services.AddSingleton(PluginLoader.LoadDefault(pluginDirectory));

        services.AddSingleton<IFigureFactory, SampleFigureFactory>();
        services.AddSingleton<IFigureExportService, FigureExportService>();
        services.AddSingleton<IFigureDocumentService, FigureDocumentService>();
        services.AddSingleton<IDataImportService, DataImportService>();

        // Scripting engines: C# and JGS are always available; Python is available when a CPython runtime is found.
        services.AddSingleton<IScriptEngine, CSharpScriptEngine>();
        services.AddSingleton<IScriptEngine, PythonScriptEngine>();
        services.AddSingleton<IScriptEngine, JgsScriptEngine>();
        services.AddSingleton<IWorkspaceStateService, WorkspaceStateService>();
        services.AddSingleton<IFigureWindowService, FigureWindowService>();
        services.AddSingleton<IScriptingService, ScriptingService>();

        services.AddTransient<FigureViewModel>();
        services.AddTransient<FigureWindow>();
    }
}
