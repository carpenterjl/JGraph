using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The WiX 6 command line (compiler and SDK plan, stage K18, decision KO7): <c>wix.dll</c> run by the
/// .NET host, which <c>compiler.package.installer</c> builds installers with. JGraph's developer install
/// ships it in <c>toolbox\compiler\wix</c> with its extensions in <c>ext</c> beside it (MS-RL licensed);
/// a source checkout takes it from the NuGet package cache the installer projects restore it into.
/// </summary>
public sealed class WixTool
{
    /// <summary>The environment variable naming a folder that holds <c>wix.dll</c>, which wins over every other place.</summary>
    public const string FolderVariable = "JGRAPH_WIX";

    /// <summary>The user interface extension, which the runtime MSI's dialogs come from.</summary>
    public const string UiExtension = "WixToolset.UI.wixext";

    /// <summary>The bootstrapper applications extension, which an installer's own program (its bundle) is built with.</summary>
    public const string BootstrapperExtension = "WixToolset.BootstrapperApplications.wixext";

    private WixTool(string folder, string? packageVersion, string? packagesFolder)
    {
        Folder = folder;
        PackageVersion = packageVersion;
        PackagesFolder = packagesFolder;
    }

    /// <summary>The folder holding <c>wix.dll</c>.</summary>
    public string Folder { get; }

    /// <summary><c>wix.dll</c> itself.</summary>
    public string WixDll => Path.Combine(Folder, "wix.dll");

    /// <summary>The NuGet package version the tool came from, when it came from the package cache.</summary>
    public string? PackageVersion { get; }

    /// <summary>The NuGet package cache the tool came from, when it did.</summary>
    public string? PackagesFolder { get; }

    /// <summary>
    /// Finds the tool: the folder <c>JGRAPH_WIX</c> names, <c>toolbox\compiler\wix</c> beside this
    /// program, then the newest <c>wixtoolset.sdk</c> package in the NuGet cache (<c>NUGET_PACKAGES</c>
    /// or the user's <c>.nuget\packages</c>); null when none holds <c>wix.dll</c>.
    /// </summary>
    public static WixTool? Find()
    {
        if (Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } named && File.Exists(Path.Combine(named, "wix.dll")))
        {
            return new WixTool(Path.GetFullPath(named), null, null);
        }

        string shipped = Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "toolbox", "compiler", "wix");
        if (File.Exists(Path.Combine(shipped, "wix.dll")))
        {
            return new WixTool(shipped, null, null);
        }

        string? packages = PackageCache();
        if (packages is null)
        {
            return null;
        }

        string sdk = Path.Combine(packages, "wixtoolset.sdk");
        if (!Directory.Exists(sdk))
        {
            return null;
        }

        foreach (string version in Directory.GetDirectories(sdk).Select(static p => Path.GetFileName(p)!).OrderByDescending(PackageOrder))
        {
            string tools = Path.Combine(sdk, version, "tools", "net6.0");
            if (File.Exists(Path.Combine(tools, "wix.dll")))
            {
                return new WixTool(tools, version, packages);
            }
        }

        return null;
    }

    /// <summary>
    /// The extension assembly for <paramref name="name"/> (<c>WixToolset.UI.wixext</c> and the like):
    /// <c>ext\&lt;name&gt;.dll</c> beside the tool, else the package of the tool's own version, else the
    /// newest, in the cache the tool came from; null when none is found.
    /// </summary>
    public string? Extension(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string beside = Path.Combine(Folder, "ext", name + ".dll");
        if (File.Exists(beside))
        {
            return beside;
        }

        if (PackagesFolder is null)
        {
            return null;
        }

        string package = Path.Combine(PackagesFolder, name.ToLowerInvariant());
        if (!Directory.Exists(package))
        {
            return null;
        }

        IEnumerable<string> versions = Directory.GetDirectories(package).Select(static p => Path.GetFileName(p)!);
        foreach (string version in versions.OrderByDescending(v => v == PackageVersion ? 1 : 0).ThenByDescending(PackageOrder))
        {
            string dll = Path.Combine(package, version, "wixext6", name + ".dll");
            if (File.Exists(dll))
            {
                return dll;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs <c>wix</c> with <paramref name="arguments"/> in <paramref name="workingFolder"/> and answers
    /// what it printed; a non-zero exit is a <see cref="WixException"/> carrying that text.
    /// </summary>
    public string Run(IReadOnlyList<string> arguments, string workingFolder, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo(DotnetHost())
        {
            WorkingDirectory = workingFolder,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(WixDll);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The tool runs on the .NET host of this process' install, whatever DOTNET_ROOT says elsewhere.
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        var output = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(10)).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new WixException($"wix {string.Join(" ", arguments)} did not finish in time.", output.ToString());
        }

        process.WaitForExit();
        string text = output.ToString();
        if (process.ExitCode != 0)
        {
            string[] errors = [.. text.Split('\n').Where(static l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Select(static l => l.Trim()).Take(3)];
            throw new WixException($"wix {arguments.FirstOrDefault()} failed with exit code {process.ExitCode}."
                + (errors.Length > 0 ? " " + string.Join(" ", errors) : ""), text);
        }

        return text;
    }

    /// <summary>The <c>dotnet</c> host this process runs on: the folder above its shared framework, else <c>DOTNET_ROOT</c>, else the one on the PATH.</summary>
    public static string DotnetHost()
    {
        string exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string runtime = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? shared = Path.GetDirectoryName(Path.GetDirectoryName(runtime));
        if (shared is not null && Path.GetDirectoryName(shared) is { } root && File.Exists(Path.Combine(root, exe)))
        {
            return Path.Combine(root, exe);
        }

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } named && File.Exists(Path.Combine(named, exe)))
        {
            return Path.Combine(named, exe);
        }

        return "dotnet";
    }

    private static string? PackageCache()
    {
        if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } named && Directory.Exists(named))
        {
            return named;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cache = Path.Combine(home, ".nuget", "packages");
        return Directory.Exists(cache) ? cache : null;
    }

    /// <summary>A package version folder's name as a sortable value: each dotted part numeric, a label after a dash last.</summary>
    private static (long, long, long, long, string) PackageOrder(string version)
    {
        string[] core = version.Split('-', 2);
        string[] parts = core[0].Split('.');
        long Part(int i) => parts.Length > i && long.TryParse(parts[i], out long n) ? n : 0;
        return (Part(0), Part(1), Part(2), Part(3), core.Length > 1 ? core[1] : "~");
    }
}

/// <summary>A WiX build that failed, with what the tool printed.</summary>
public sealed class WixException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WixException(string message, string output)
        : base(message)
    {
        Output = output;
    }

    /// <summary>Everything the tool printed.</summary>
    public string Output { get; }
}
