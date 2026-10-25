using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

namespace JGraph.MATLAB.Runtime;

/// <summary>
/// Finds the JGraph Runtime for a client that carries only this assembly, as R2025b's
/// MathWorks.MATLAB.Runtime.dll finds the MATLAB Runtime (compiler and SDK plan, stage K9; the same
/// search K8's MWArray assembly makes): the engine's assemblies and native libraries are loaded from
/// the runtime's <c>bin\win64</c> when the client's own folder does not hold them. The runtime is the
/// one of this assembly's version: <c>JGRAPH_RUNTIME_ROOT</c>, then the installer's registry key.
/// This runs before any engine type is touched, so it cannot use the engine's own RuntimeLayout.
/// </summary>
internal static class RuntimeLocator
{
    private const string RootVariable = "JGRAPH_RUNTIME_ROOT";
    private const string RegistryKey = @"SOFTWARE\JGraph\JGraph Runtime";
    private const string MarkerFile = "jgraph-runtime.json";

    private static readonly object Gate = new();
    private static string _bin;
    private static bool _sought;

    // A library's module initializer, which CA2255 reserves for applications: the client's process
    // must know where the engine is before the first engine type is loaded, and nothing else runs first.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        AssemblyLoadContext.Default.Resolving += static (context, name) =>
            Bin() is { } bin && File.Exists(Path.Combine(bin, name.Name + ".dll"))
                ? context.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll"))
                : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += static (_, name) =>
        {
            if (Bin() is not { } bin)
            {
                return IntPtr.Zero;
            }

            string file = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
            foreach (string candidate in new[] { Path.Combine(bin, file), Path.Combine(bin, "native", file), Path.Combine(bin, "runtimes", "win-x64", "native", file) })
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        };
    }

    /// <summary>The runtime's engine folder, or null when no runtime of this version is found.</summary>
    private static string Bin()
    {
        lock (Gate)
        {
            if (!_sought)
            {
                _sought = true;
                _bin = Find();
            }

            return _bin;
        }
    }

    private static string Find()
    {
        string version = typeof(RuntimeLocator).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var roots = new List<string>();
        if (Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } named)
        {
            roots.Add(named);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (Microsoft.Win32.RegistryKey hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                try
                {
                    using Microsoft.Win32.RegistryKey key = hive.OpenSubKey(RegistryKey + "\\" + version);
                    if (key?.GetValue("Location") is string { Length: > 0 } location)
                    {
                        roots.Add(location);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // An unreadable key names no runtime.
                }
            }
        }

        foreach (string root in roots)
        {
            try
            {
                using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, MarkerFile)));
                if (marker.RootElement.TryGetProperty("version", out JsonElement v) && v.GetString() == version)
                {
                    return Path.Combine(root, "bin", "win64");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
            {
                // Not a runtime: try the next place.
            }
        }

        return null;
    }
}
