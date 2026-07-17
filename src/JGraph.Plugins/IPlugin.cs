namespace JGraph.Plugins;

/// <summary>
/// A JGraph extension. A plugin is a small, self-contained unit that contributes named
/// resources — themes, colormaps — to a <see cref="PluginRegistry"/> through
/// <see cref="Configure"/>. Plugins are discovered by <see cref="PluginLoader"/> from assemblies
/// (including DLLs dropped into a plugins directory) and must expose a public parameterless
/// constructor. Configuration is data-only: a plugin never touches rendering, WPF, or the file
/// format directly, which keeps extensions portable and testable.
/// </summary>
public interface IPlugin
{
    /// <summary>A unique, human-readable name for the plugin (shown in about/diagnostics).</summary>
    string Name { get; }

    /// <summary>The plugin's version string.</summary>
    string Version { get; }

    /// <summary>Registers this plugin's contributions with the host registry.</summary>
    void Configure(IPluginRegistry registry);
}
