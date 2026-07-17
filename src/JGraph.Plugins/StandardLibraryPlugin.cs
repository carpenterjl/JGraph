using JGraph.Core.Drawing;

namespace JGraph.Plugins;

/// <summary>
/// The built-in plugin that ships JGraph's standard resources: the Light, Dark, Presentation, and
/// IEEE themes and the standard colormaps (Viridis, Jet, Hot, Cool, Grayscale). It is applied by
/// <see cref="PluginRegistry.CreateDefault"/> and also serves as the canonical example of how a
/// plugin registers its contributions.
/// </summary>
public sealed class StandardLibraryPlugin : IPlugin
{
    /// <inheritdoc />
    public string Name => "JGraph Standard Library";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public void Configure(IPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.AddTheme(Theme.Light);
        registry.AddTheme(Theme.Dark);
        registry.AddTheme(Theme.Presentation);
        registry.AddTheme(Theme.Ieee);

        registry.AddColormap(Colormap.Viridis);
        registry.AddColormap(Colormap.Jet);
        registry.AddColormap(Colormap.Hot);
        registry.AddColormap(Colormap.Cool);
        registry.AddColormap(Colormap.Grayscale);
    }
}
