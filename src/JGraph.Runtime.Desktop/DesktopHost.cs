using System.Windows;
using System.Windows.Threading;
using JGraph.Core.Model;
using JGraph.Plugins;
using JGraph.Runtime.Desktop.Mvvm;
using JGraph.Runtime.Desktop.Services;
using JGraph.Runtime.Desktop.Theming;
using JGraph.Scripting;

namespace JGraph.Runtime.Desktop;

/// <summary>
/// Where a deployed component's figures and uifigure apps are shown (compiler and SDK plan, stage
/// K5). <see cref="JgRuntime"/> starts it by name the first time a component shows a figure. In a
/// process that already runs WPF the windows join its application, on its thread, each carrying the
/// theme itself so the host's look is left alone; in one that does not (a console client, a service,
/// a Python process) a thread is started for them with an application of its own, which ends with
/// the process.
/// </summary>
/// <remarks>
/// Each component has its own window service, because each numbers its own figures as R2025b's do
/// (<c>k4_client</c>): two components' figure 1 are two windows. A window's toolbar is the figure's
/// without the application's Script… and Options… buttons (<see cref="DeployedFigureWindowShell"/>).
/// </remarks>
internal sealed class DesktopHost : IRuntimeDesktop
{
    private const string ThemeBase = "pack://application:,,,/JGraph.Controls;component/Themes/";

    private readonly Dispatcher _ui;
    private readonly bool _ownsApplication;
    private readonly Dictionary<object, FigureWindowService> _services = new(ReferenceEqualityComparer.Instance);
    private readonly PluginRegistry _plugins = PluginRegistry.CreateDefault();

    private DesktopHost(Dispatcher ui, bool ownsApplication)
    {
        _ui = ui;
        _ownsApplication = ownsApplication;

        // Before the first figure is shown, not with its window: on a host's thread that window opens
        // after the call that made it, and its components' frames are made during that call.
        FigureWindowService.InstallHooks(ui);
    }

    /// <summary>Starts the host: on the process's WPF application when it has one, else on a thread of its own.</summary>
    internal static DesktopHost Start()
    {
        if (System.Windows.Application.Current is { } running)
        {
            return new DesktopHost(running.Dispatcher, ownsApplication: false);
        }

        Dispatcher? ui = null;
        Exception? failed = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            System.Windows.Application app;
            try
            {
                // Its resources are the IDE's: the theme-independent styles and the light theme.
                app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(ThemeBase + "Shared.xaml") });
                new ThemeManager(new AppThemeCatalog(), app.Resources).Apply(AppThemeCatalog.LightId);
                ui = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failed = ex;
                ready.Set();
                return;
            }

            ready.Set();
            app.Run();
        })
        {
            IsBackground = true,
            Name = "JGraph Runtime windows",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failed is not null)
        {
            throw new InvalidOperationException("The JGraph Runtime could not start its window thread: " + failed.Message, failed);
        }

        return new DesktopHost(ui!, ownsApplication: true);
    }

    /// <inheritdoc />
    public bool IsWindowThread => _ui.CheckAccess();

    /// <inheritdoc />
    public IScriptFigureFiles FigureFiles { get; } = new AppScriptFigureFiles();

    /// <inheritdoc />
    public void Show(object owner, int number, FigureModel figure) =>
        OnWindowThread(() => ServiceOf(owner).ShowScriptFigure(number, figure));

    /// <inheritdoc />
    public void Close(object owner, int number) => OnWindowThread(() =>
    {
        if (_services.TryGetValue(owner, out FigureWindowService? service))
        {
            service.CloseScriptFigure(number);
        }
    });

    /// <inheritdoc />
    public void Retire(object owner) => OnWindowThread(() =>
    {
        if (_services.Remove(owner, out FigureWindowService? service))
        {
            service.Retire();
        }
    });

    /// <summary>
    /// Runs window work from the engine thread: waited for on the host's own thread, queued on a
    /// host application's, whose thread may itself be waiting for the call this came from (a client
    /// calling Component.Call from a button's handler) and could then never run it.
    /// </summary>
    private void OnWindowThread(Action work)
    {
        if (_ownsApplication)
        {
            _ui.Invoke(work);
        }
        else
        {
            _ui.BeginInvoke(work);
        }
    }

    /// <inheritdoc />
    public bool AnyVisible(object owner) =>
        _ui.Invoke(() => _services.TryGetValue(owner, out FigureWindowService? service) && service.AnyVisible);

    /// <summary>The window service of <paramref name="owner"/>'s figures, made on first use. UI thread.</summary>
    private FigureWindowService ServiceOf(object owner)
    {
        if (!_services.TryGetValue(owner, out FigureWindowService? service))
        {
            service = new FigureWindowService(NewWindow);
            _services[owner] = service;
        }

        return service;
    }

    private FigureWindow NewWindow()
    {
        var model = new FigureViewModel(
            new SampleFigureFactory(),
            new FigureExportService(),
            new FigureDocumentService(),
            new DataImportService(),
            _plugins,
            DeployedFigureWindowShell.Instance);
        var window = new FigureWindow(model);
        if (!_ownsApplication)
        {
            // The host's application is not ours to restyle: the window carries the theme itself.
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(ThemeBase + "Shared.xaml") });
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new AppThemeCatalog().Default.Source });
        }

        return window;
    }
}
