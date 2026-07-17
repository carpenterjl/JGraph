using JGraph.Core.Drawing;

namespace JGraph.Plugins;

/// <summary>
/// The host-owned catalog of everything plugins contribute. It is both the write side handed to
/// <see cref="IPlugin.Configure"/> (via <see cref="IPluginRegistry"/>) and the read side the
/// application queries for available themes and colormaps. Registration order is preserved so menus
/// list resources in a stable order, and names are unique (case-insensitive).
/// </summary>
public sealed class PluginRegistry : IPluginRegistry
{
    private readonly List<ITheme> _themes = new();
    private readonly List<Colormap> _colormaps = new();
    private readonly List<IPlugin> _plugins = new();
    private readonly Dictionary<string, ITheme> _themesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Colormap> _colormapsByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registered themes, in registration order.</summary>
    public IReadOnlyList<ITheme> Themes => _themes;

    /// <summary>The registered colormaps, in registration order.</summary>
    public IReadOnlyList<Colormap> Colormaps => _colormaps;

    /// <summary>The plugins that have been applied to this registry, in application order.</summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins;

    /// <summary>
    /// Creates a registry pre-populated with the built-in <see cref="StandardLibraryPlugin"/>
    /// (the Light/Dark/Presentation/IEEE themes and the standard colormaps).
    /// </summary>
    public static PluginRegistry CreateDefault()
    {
        var registry = new PluginRegistry();
        registry.Apply(new StandardLibraryPlugin());
        return registry;
    }

    /// <inheritdoc />
    public void AddTheme(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!_themesByName.TryAdd(theme.Name, theme))
        {
            throw new ArgumentException($"A theme named '{theme.Name}' is already registered.", nameof(theme));
        }

        _themes.Add(theme);
    }

    /// <inheritdoc />
    public void AddColormap(Colormap colormap)
    {
        ArgumentNullException.ThrowIfNull(colormap);
        if (!_colormapsByName.TryAdd(colormap.Name, colormap))
        {
            throw new ArgumentException($"A colormap named '{colormap.Name}' is already registered.", nameof(colormap));
        }

        _colormaps.Add(colormap);
    }

    /// <summary>Applies a plugin's <see cref="IPlugin.Configure"/> to this registry.</summary>
    /// <exception cref="PluginException">The plugin's configuration failed (e.g. a duplicate name).</exception>
    public void Apply(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        try
        {
            plugin.Configure(this);
        }
        catch (Exception ex) when (ex is not PluginException)
        {
            throw new PluginException(
                $"Plugin '{plugin.Name}' ({plugin.Version}) failed to configure: {ex.Message}", ex);
        }

        _plugins.Add(plugin);
    }

    /// <summary>Looks up a theme by name (case-insensitive).</summary>
    public bool TryGetTheme(string name, out ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _themesByName.TryGetValue(name, out theme!);
    }

    /// <summary>Returns a theme by name (case-insensitive).</summary>
    /// <exception cref="KeyNotFoundException">No theme with that name is registered.</exception>
    public ITheme GetTheme(string name) => TryGetTheme(name, out ITheme theme)
        ? theme
        : throw new KeyNotFoundException($"No theme named '{name}' is registered.");

    /// <summary>Looks up a colormap by name (case-insensitive).</summary>
    public bool TryGetColormap(string name, out Colormap colormap)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _colormapsByName.TryGetValue(name, out colormap!);
    }
}
