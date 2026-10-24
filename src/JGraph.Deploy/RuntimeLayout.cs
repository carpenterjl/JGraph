using System.Text.Json;

namespace JGraph.Deploy;

/// <summary>Where a JGraph Runtime was found.</summary>
public enum RuntimeLocationSource
{
    /// <summary>The <c>JGRAPH_RUNTIME_ROOT</c> environment variable.</summary>
    Environment,

    /// <summary>The registry key the runtime's installer writes.</summary>
    Registry,

    /// <summary>The application's own folder: a self-contained application carries its runtime.</summary>
    ApplicationFolder,
}

/// <summary>
/// An installed JGraph Runtime's folders (compiler and SDK plan, stage K4), the counterpart of
/// R2025b's <c>MATLAB Runtime\R2025b</c> folder with its <c>bin</c>, <c>runtime</c> and
/// <c>extern</c>:
/// <list type="table">
/// <item><term><c>jgraph-runtime.json</c></term><description>The marker: the JGraph version and the release it follows.</description></item>
/// <item><term><c>bin\win64</c></term><description>The engine: <c>jgraph.exe</c>, this library and everything they load.</description></item>
/// <item><term><c>runtime\win64</c></term><description>The native bridge the C, C++, Python and Excel targets load (stage K10).</description></item>
/// <item><term><c>include</c></term><description>The C and C++ headers (stages K11, K12).</description></item>
/// <item><term><c>python</c></term><description>The <c>matlab</c> array module and the package runtime (stage K13).</description></item>
/// <item><term><c>launchers\win64</c></term><description>The console and windowed launcher templates <c>mcc -m</c> and <c>-e</c> copy (stage K6).</description></item>
/// </list>
/// It lives with the build side (moved from <c>JGraph.Runtime</c> in stage K6): a build looks here
/// for the runtime a program carries with it, and the runtime looks here for itself.
/// </summary>
public sealed class RuntimeLayout
{
    /// <summary>The marker file at a runtime's root.</summary>
    public const string MarkerFile = "jgraph-runtime.json";

    /// <summary>The environment variable that names a runtime's root, which wins over the registry.</summary>
    public const string RootVariable = "JGRAPH_RUNTIME_ROOT";

    /// <summary>The registry key, under HKEY_LOCAL_MACHINE or HKEY_CURRENT_USER, with a subkey per version holding <c>Location</c>.</summary>
    public const string RegistryKey = @"SOFTWARE\JGraph\JGraph Runtime";

    /// <summary>The platform folder the binaries sit in.</summary>
    public const string Platform = "win64";

    private RuntimeLayout(string root, string version, string release, RuntimeLocationSource source)
    {
        Root = root;
        Version = version;
        Release = release;
        Source = source;
    }

    /// <summary>The runtime's root folder.</summary>
    public string Root { get; }

    /// <summary>The JGraph version of the runtime.</summary>
    public string Version { get; }

    /// <summary>The MATLAB release the runtime's dialect follows, <c>25.2</c>.</summary>
    public string Release { get; }

    /// <summary>Where it was found.</summary>
    public RuntimeLocationSource Source { get; }

    /// <summary>The engine's folder.</summary>
    public string BinFolder => Path.Combine(Root, "bin", Platform);

    /// <summary>The native bridge's folder.</summary>
    public string NativeFolder => Path.Combine(Root, "runtime", Platform);

    /// <summary>The C and C++ headers.</summary>
    public string IncludeFolder => Path.Combine(Root, "include");

    /// <summary>The Python modules.</summary>
    public string PythonFolder => Path.Combine(Root, "python");

    /// <summary>The launcher templates.</summary>
    public string LaunchersFolder => Path.Combine(Root, "launchers", Platform);

    /// <summary>The version of JGraph this library is, which a runtime must match to be found for it.</summary>
    public static string CurrentVersion { get; } = typeof(RuntimeLayout).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Finds the runtime of <paramref name="version"/> (this library's when not given) the way a
    /// launcher does: <c>JGRAPH_RUNTIME_ROOT</c>, then the registry key the installer writes (the
    /// machine's, then the user's), then <paramref name="applicationFolder"/>, for an application that
    /// carries its runtime. A candidate counts only when its marker names the version.
    /// </summary>
    /// <param name="version">The JGraph version wanted.</param>
    /// <param name="applicationFolder">The application's folder; the process's base folder when not given.</param>
    /// <returns>The runtime, or null when none of the places holds it.</returns>
    public static RuntimeLayout? Find(string? version = null, string? applicationFolder = null)
    {
        version ??= CurrentVersion;
        if (System.Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } fromEnvironment
            && Read(fromEnvironment, RuntimeLocationSource.Environment) is { } named && named.Version == version)
        {
            return named;
        }

        foreach (string registered in RegisteredRoots(version))
        {
            if (Read(registered, RuntimeLocationSource.Registry) is { } installed && installed.Version == version)
            {
                return installed;
            }
        }

        return Read(applicationFolder ?? AppContext.BaseDirectory, RuntimeLocationSource.ApplicationFolder) is { } carried
            && carried.Version == version
            ? carried
            : null;
    }

    /// <summary>The runtime at <paramref name="root"/>, or null when its marker is missing or unreadable.</summary>
    public static RuntimeLayout? Read(string root, RuntimeLocationSource source)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        string marker = Path.Combine(full, MarkerFile);
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(marker));
            JsonElement top = document.RootElement;
            return top.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.String
                && top.TryGetProperty("release", out JsonElement r) && r.ValueKind == JsonValueKind.String
                ? new RuntimeLayout(full, v.GetString()!, r.GetString()!, source)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The marker's text for a runtime of <paramref name="version"/>, as the layout's publish writes it.</summary>
    public static string MarkerText(string version) =>
        JsonSerializer.Serialize(new { version, release = RuntimeOptions.Release }, new JsonSerializerOptions { WriteIndented = true });

    private static IEnumerable<string> RegisteredRoots(string version)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (Microsoft.Win32.RegistryKey hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            string? location;
            try
            {
                using Microsoft.Win32.RegistryKey? key = hive.OpenSubKey(RegistryKey + "\\" + version);
                location = key?.GetValue("Location") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                location = null;
            }

            if (location is { Length: > 0 })
            {
                yield return location;
            }
        }
    }
}
