using System.Collections.ObjectModel;
using System.IO;
using JGraph.Application.Services;
using JGraph.Core.Drawing;
using JGraph.Plugins;
using JGraph.Scripting;

namespace JGraph.Application.Mvvm;

/// <summary>
/// A discovered plugin the user can turn on or off. Disabling it adds its type name to the settings'
/// <c>DisabledPlugins</c>; the change takes effect on the next launch, since a loaded assembly cannot
/// be unloaded.
/// </summary>
public sealed class PluginToggle
{
    /// <summary>Creates a toggle for a plugin.</summary>
    public PluginToggle(string displayName, string typeName, bool enabled)
    {
        DisplayName = displayName;
        TypeName = typeName;
        Enabled = enabled;
    }

    /// <summary>The plugin's friendly name, for display.</summary>
    public string DisplayName { get; }

    /// <summary>The plugin's type full name — its identity in the settings' disabled list.</summary>
    public string TypeName { get; }

    /// <summary>Whether the plugin is loaded. Bound to a checkbox.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// The editable draft behind the Options dialog: the JGS language options, the default script directory,
/// theme and new-script language, and the plugin enable/disable list. UI-free so the wiring is testable
/// apart from the WPF window. <see cref="Apply"/> commits the draft through <see cref="ISettingsService"/>;
/// nothing is saved until then, so closing the dialog with Cancel discards every edit.
/// </summary>
public sealed class OptionsViewModel
{
    private readonly ISettingsService _settings;

    /// <summary>Creates the draft from the current settings and the available themes, languages and plugins.</summary>
    /// <param name="settings">The settings service the dialog reads from and commits back to.</param>
    /// <param name="themes">The figure themes to choose a default from (the plugin registry's list).</param>
    /// <param name="languages">The script languages a new document can start in.</param>
    /// <param name="pluginDirectory">The folder to discover toggleable plugins in, or null for none.</param>
    public OptionsViewModel(
        ISettingsService settings,
        IReadOnlyList<ITheme> themes,
        IReadOnlyList<string> languages,
        string? pluginDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(languages);
        _settings = settings;

        UserSettings current = settings.Current;
        OptionalLet = current.JgsOptionalLet;
        OneBasedIndexing = current.JgsIndexBase == 1;
        DefaultScriptDirectory = current.DefaultScriptDirectory ?? string.Empty;

        AvailableThemes = ["(first available)", .. themes.Select(t => t.Name)];
        DefaultTheme = current.DefaultFigureTheme is { } theme && AvailableThemes.Contains(theme)
            ? theme
            : AvailableThemes[0];

        NewScriptLanguages = [.. languages];
        DefaultNewScriptLanguage = current.DefaultNewScriptLanguage is { } language && NewScriptLanguages.Contains(language)
            ? language
            : NewScriptLanguages.FirstOrDefault() ?? "JGS";

        Plugins = new ObservableCollection<PluginToggle>(DiscoverToggles(pluginDirectory, current));
    }

    /// <summary>Whether a first assignment in JGS may omit <c>let</c>.</summary>
    public bool OptionalLet { get; set; }

    /// <summary>Whether JGS indexes from 1 rather than 0.</summary>
    public bool OneBasedIndexing { get; set; }

    /// <summary>The folder new open/save dialogs start in when no workspace is open (empty for the shell default).</summary>
    public string DefaultScriptDirectory { get; set; }

    /// <summary>The theme names to offer, with a "(first available)" sentinel first.</summary>
    public IReadOnlyList<string> AvailableThemes { get; }

    /// <summary>The selected default theme, or the sentinel for "the first registered one".</summary>
    public string DefaultTheme { get; set; }

    /// <summary>The languages a new script can start in.</summary>
    public IReadOnlyList<string> NewScriptLanguages { get; }

    /// <summary>The selected default new-script language.</summary>
    public string DefaultNewScriptLanguage { get; set; }

    /// <summary>The discovered plugins, each with an enabled checkbox.</summary>
    public ObservableCollection<PluginToggle> Plugins { get; }

    /// <summary>Whether any plugin toggle differs from how it is currently loaded — i.e. a restart is needed.</summary>
    public bool PluginsChanged { get; private set; }

    /// <summary>Commits the draft to the settings and persists it.</summary>
    public void Apply()
    {
        UserSettings before = _settings.Current;
        var updated = new UserSettings
        {
            JgsOptionalLet = OptionalLet,
            JgsIndexBase = OneBasedIndexing ? 1 : 0,
            DefaultScriptDirectory = string.IsNullOrWhiteSpace(DefaultScriptDirectory) ? null : DefaultScriptDirectory,
            DefaultFigureTheme = DefaultTheme == AvailableThemes[0] ? null : DefaultTheme,
            DefaultNewScriptLanguage = DefaultNewScriptLanguage,
            DisabledPlugins = [.. Plugins.Where(p => !p.Enabled).Select(p => p.TypeName)],
        };

        PluginsChanged = !updated.DisabledPlugins.ToHashSet(StringComparer.Ordinal)
            .SetEquals(before.DisabledPlugins);
        _settings.Save(updated);
    }

    private static IEnumerable<PluginToggle> DiscoverToggles(string? pluginDirectory, UserSettings current)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
        {
            yield break;
        }

        // Discovery reloads the same assemblies the loader already loaded — harmless (the default load
        // context caches them) and the only way to list what is toggleable.
        IReadOnlyList<IPlugin> discovered;
        try
        {
            discovered = PluginLoader.DiscoverPlugins(PluginLoader.LoadAssemblies(pluginDirectory));
        }
        catch (PluginException)
        {
            yield break; // a broken plugin folder must not stop the dialog from opening
        }

        foreach (IPlugin plugin in discovered)
        {
            string typeName = plugin.GetType().FullName ?? plugin.GetType().Name;
            yield return new PluginToggle($"{plugin.Name} ({plugin.Version})", typeName, current.IsPluginEnabled(typeName));
        }
    }
}
