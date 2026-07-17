using JGraph.Core.Drawing;

namespace JGraph.Plugins;

/// <summary>
/// The registration surface passed to <see cref="IPlugin.Configure"/>. A plugin adds named resources
/// through these methods; the host owns the concrete <see cref="PluginRegistry"/> and its read side.
/// Names are compared case-insensitively and must be unique across the registry — a collision is a
/// configuration error.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>Registers a theme. Throws if a theme with the same name is already registered.</summary>
    void AddTheme(ITheme theme);

    /// <summary>Registers a colormap. Throws if a colormap with the same name is already registered.</summary>
    void AddColormap(Colormap colormap);
}
