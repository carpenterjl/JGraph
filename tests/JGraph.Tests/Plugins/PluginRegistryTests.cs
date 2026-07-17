using System;
using System.Linq;
using JGraph.Core.Drawing;
using JGraph.Plugins;
using Xunit;

namespace JGraph.Tests.Plugins;

public class PluginRegistryTests
{
    [Fact]
    public void CreateDefault_RegistersStandardThemesAndColormaps()
    {
        PluginRegistry registry = PluginRegistry.CreateDefault();

        Assert.Equal(
            new[] { "Light", "Dark", "Presentation", "IEEE" },
            registry.Themes.Select(t => t.Name).ToArray());
        Assert.Contains(registry.Colormaps, c => c.Name == "Viridis");
        Assert.Equal(5, registry.Colormaps.Count);
        Assert.Single(registry.Plugins);
    }

    [Fact]
    public void AddTheme_DuplicateName_Throws()
    {
        var registry = new PluginRegistry();
        registry.AddTheme(Theme.Light);

        Assert.Throws<ArgumentException>(() => registry.AddTheme(Theme.Light));
        Assert.Single(registry.Themes);
    }

    [Fact]
    public void AddColormap_DuplicateName_Throws()
    {
        var registry = new PluginRegistry();
        registry.AddColormap(Colormap.Jet);

        Assert.Throws<ArgumentException>(() => registry.AddColormap(Colormap.Jet));
    }

    [Fact]
    public void TryGetTheme_IsCaseInsensitive()
    {
        PluginRegistry registry = PluginRegistry.CreateDefault();

        Assert.True(registry.TryGetTheme("presentation", out ITheme theme));
        Assert.Same(Theme.Presentation, theme);
    }

    [Fact]
    public void GetTheme_MissingName_Throws()
    {
        PluginRegistry registry = PluginRegistry.CreateDefault();

        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => registry.GetTheme("Nope"));
    }

    [Fact]
    public void TryGetColormap_ResolvesRegisteredMap()
    {
        PluginRegistry registry = PluginRegistry.CreateDefault();

        Assert.True(registry.TryGetColormap("Hot", out Colormap map));
        Assert.Same(Colormap.Hot, map);
    }

    [Fact]
    public void Apply_WrapsConfigureFailureInPluginException()
    {
        var registry = new PluginRegistry();

        PluginException ex = Assert.Throws<PluginException>(() => registry.Apply(new ThrowingPlugin()));

        Assert.Contains("Throwing", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Empty(registry.Plugins);
    }

    [Fact]
    public void Apply_DuplicateAcrossPlugins_SurfacesAsPluginException()
    {
        PluginRegistry registry = PluginRegistry.CreateDefault();

        // The standard library already registered "Light"; a plugin re-adding it must fail loudly.
        Assert.Throws<PluginException>(() => registry.Apply(new DuplicateThemePlugin()));
    }

    private sealed class ThrowingPlugin : IPlugin
    {
        public string Name => "Throwing";

        public string Version => "1.0";

        public void Configure(IPluginRegistry registry) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class DuplicateThemePlugin : IPlugin
    {
        public string Name => "Duplicate";

        public string Version => "1.0";

        public void Configure(IPluginRegistry registry) => registry.AddTheme(Theme.Light);
    }
}
