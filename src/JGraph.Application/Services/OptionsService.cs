using System.IO;
using System.Linq;
using System.Windows;
using JGraph.Application.Mvvm;
using JGraph.Plugins;
using JGraph.Scripting;

namespace JGraph.Application.Services;

/// <summary>
/// Builds the Options dialog's editable draft from the current settings, the registered themes and
/// engines, and the plugins folder, then shows it modally over the active window.
/// </summary>
public sealed class OptionsService : IOptionsService
{
    private readonly ISettingsService _settings;
    private readonly PluginRegistry _plugins;
    private readonly IReadOnlyList<string> _languages;

    /// <summary>Creates the service over the registered settings, plugin registry, and script engines.</summary>
    public OptionsService(ISettingsService settings, PluginRegistry plugins, IEnumerable<IScriptEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));

        // The languages a new script can start in: every engine, plus a plain text file.
        _languages = [.. engines.Select(e => e.Language), "Text"];
    }

    /// <inheritdoc />
    public void ShowOptions()
    {
        string pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        var model = new OptionsViewModel(_settings, _plugins.Themes, _languages, pluginDirectory);
        var window = new OptionsWindow(model)
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive),
        };
        window.ShowDialog();
    }
}
