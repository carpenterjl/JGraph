using System;
using System.IO;
using System.Linq;
using System.Reflection;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Plugins;
using Xunit;

namespace JGraph.Tests.Plugins;

public class PluginLoaderTests
{
    private static readonly Assembly ThisAssembly = typeof(PluginLoaderTests).Assembly;

    [Fact]
    public void DiscoverPlugins_FindsConcreteParameterlessPlugins()
    {
        var discovered = PluginLoader.DiscoverPlugins(new[] { ThisAssembly });

        Assert.Single(discovered.OfType<DiscoverableTestPlugin>());
    }

    [Fact]
    public void DiscoverPlugins_SkipsAbstractAndNonDefaultConstructible()
    {
        var discovered = PluginLoader.DiscoverPlugins(new[] { ThisAssembly });

        Assert.DoesNotContain(discovered, p => p is AbstractTestPlugin);
        Assert.DoesNotContain(discovered, p => p is NoDefaultCtorTestPlugin);
    }

    [Fact]
    public void DiscoveredPlugin_CanBeApplied()
    {
        DiscoverableTestPlugin plugin = PluginLoader.DiscoverPlugins(new[] { ThisAssembly })
            .OfType<DiscoverableTestPlugin>()
            .Single();

        var registry = new PluginRegistry();
        registry.Apply(plugin);

        Assert.True(registry.TryGetTheme(DiscoverableTestPlugin.ThemeName, out _));
    }

    [Fact]
    public void LoadAssemblies_MissingDirectory_IsEmpty()
    {
        Assert.Empty(PluginLoader.LoadAssemblies(Path.Combine(Path.GetTempPath(), "jgraph-no-such-dir-" + Guid.NewGuid())));
    }

    [Fact]
    public void LoadAssemblies_EmptyDirectory_IsEmpty()
    {
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-plugins-" + Guid.NewGuid())).FullName;
        try
        {
            Assert.Empty(PluginLoader.LoadAssemblies(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadDefault_WithoutDirectory_HasStandardThemes()
    {
        PluginRegistry registry = PluginLoader.LoadDefault();

        Assert.Equal(4, registry.Themes.Count);
    }

    [Fact]
    public void LoadDefault_MissingDirectory_IsIgnored()
    {
        PluginRegistry registry = PluginLoader.LoadDefault(Path.Combine(Path.GetTempPath(), "jgraph-none-" + Guid.NewGuid()));

        Assert.Equal(4, registry.Themes.Count);
    }

    // --- Discovery fixtures ---

    public sealed class DiscoverableTestPlugin : IPlugin
    {
        public const string ThemeName = "Unit Test Theme";

        public string Name => "Unit Test Plugin";

        public string Version => "0.1";

        public void Configure(IPluginRegistry registry) => registry.AddTheme(new Theme
        {
            Name = ThemeName,
            FigureBackground = Colors.White,
            AxesBackground = Colors.White,
            AxisLine = Colors.Black,
            TickLabel = Colors.Black,
            AxisLabel = Colors.Black,
            Title = Colors.Black,
            MajorGrid = Colors.Gray,
            MinorGrid = Colors.LightGray,
        });
    }

    public abstract class AbstractTestPlugin : IPlugin
    {
        public string Name => "Abstract";

        public string Version => "0";

        public abstract void Configure(IPluginRegistry registry);
    }

    public sealed class NoDefaultCtorTestPlugin : IPlugin
    {
        public NoDefaultCtorTestPlugin(string ignored) => _ = ignored;

        public string Name => "No Ctor";

        public string Version => "0";

        public void Configure(IPluginRegistry registry)
        {
        }
    }
}
