using System.Reflection;
using System.Runtime.Loader;

namespace JGraph.Plugins;

/// <summary>
/// Discovers and loads <see cref="IPlugin"/> implementations. Plugins can be found in already-loaded
/// assemblies or in <c>*.dll</c> files dropped into a plugins directory (loaded into the default load
/// context). Discovery is deterministic — assemblies and plugin types are processed in a stable
/// order — so the resulting registry is reproducible.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Builds a registry seeded with the built-in standard library, then discovers and applies any
    /// plugins found in <paramref name="pluginDirectory"/> (if given and present). This is the
    /// entry point applications use at startup.
    /// </summary>
    /// <param name="pluginDirectory">The folder to discover plugins in, or null for none.</param>
    /// <param name="include">A filter deciding which discovered plugins to apply — the user's
    /// enable/disable choice — or null to apply every one. The built-in standard library is always
    /// applied, so its themes and colormaps can never be turned off.</param>
    public static PluginRegistry LoadDefault(string? pluginDirectory = null, Func<IPlugin, bool>? include = null)
    {
        PluginRegistry registry = PluginRegistry.CreateDefault();
        if (!string.IsNullOrWhiteSpace(pluginDirectory))
        {
            AddFromDirectory(registry, pluginDirectory, include);
        }

        return registry;
    }

    /// <summary>
    /// Discovers plugins in the assemblies loaded from <paramref name="directory"/> and applies each
    /// one <paramref name="include"/> accepts to <paramref name="registry"/>. A missing directory is
    /// treated as "no plugins".
    /// </summary>
    public static void AddFromDirectory(PluginRegistry registry, string directory, Func<IPlugin, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (IPlugin plugin in DiscoverPlugins(LoadAssemblies(directory)))
        {
            if (include is null || include(plugin))
            {
                registry.Apply(plugin);
            }
        }
    }

    /// <summary>
    /// Loads every managed <c>*.dll</c> in <paramref name="directory"/> into the default load context.
    /// Returns an empty list if the directory does not exist. Non-managed DLLs are skipped; a managed
    /// assembly that fails to load raises a <see cref="PluginException"/> naming the file.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadAssemblies(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<Assembly>();
        }

        var assemblies = new List<Assembly>();
        foreach (string file in Directory.GetFiles(directory, "*.dll").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file)));
            }
            catch (BadImageFormatException)
            {
                // Not a managed assembly (e.g. a native dependency) — skip it.
            }
            catch (Exception ex) when (ex is FileLoadException or FileNotFoundException)
            {
                throw new PluginException($"Failed to load plugin assembly '{file}': {ex.Message}", ex);
            }
        }

        return assemblies;
    }

    /// <summary>
    /// Finds every concrete <see cref="IPlugin"/> type (with a public parameterless constructor) in
    /// the given assemblies and instantiates it. Types are returned in a stable order by full name.
    /// </summary>
    public static IReadOnlyList<IPlugin> DiscoverPlugins(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var plugins = new List<IPlugin>();
        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in GetLoadableTypes(assembly)
                         .Where(IsInstantiablePlugin)
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                try
                {
                    plugins.Add((IPlugin)Activator.CreateInstance(type)!);
                }
                catch (Exception ex) when (ex is not PluginException)
                {
                    throw new PluginException($"Failed to instantiate plugin '{type.FullName}': {ex.Message}", ex);
                }
            }
        }

        return plugins;
    }

    private static bool IsInstantiablePlugin(Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        typeof(IPlugin).IsAssignableFrom(type) &&
        type.GetConstructor(Type.EmptyTypes) is not null;

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A dependent type failed to load; use whatever loaded successfully.
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
