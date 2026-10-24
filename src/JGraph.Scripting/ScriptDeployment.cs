using JGraph.Deploy;

namespace JGraph.Scripting;

/// <summary>
/// What makes a session a deployed one (compiler and SDK plan, stage K1): the folder its archive
/// was extracted to, the frozen search path, and the command line its main function is called
/// with. A host that runs a compiled component puts one on the <see cref="ScriptContext"/>; the
/// session then answers <c>isdeployed</c> true and behaves as R2025b's MATLAB Runtime was recorded
/// behaving in stage K0 (<c>tools/matlab-checklist/compiler-probes/recordings</c>).
/// </summary>
/// <remarks>
/// <see cref="FromArchive"/> makes one from a JGraph archive (stage K3), extracted into the runtime
/// cache, and holds the cache's entry until the deployment is disposed. <see cref="FromFolder"/>, for
/// the hidden <c>jgraph -batch main.m --deployed &lt;folder&gt;</c> switch, stands a plain folder in
/// for the extracted archive.
/// </remarks>
public sealed class ScriptDeployment : IDisposable
{
    /// <summary>
    /// The exit code of a deployed program that ended on an uncaught error: R2025b's, whatever the
    /// error was (K0, exit_error, exit_nested, exit_builtin, exit_cause).
    /// </summary>
    public const int UncaughtErrorExitCode = 249;

    /// <summary>The MATLAB Runtime version <c>mcrversion</c> answers: R2025b's, which the MATLAB dialect follows (decision KO2).</summary>
    public static readonly IReadOnlyList<int> RuntimeVersion = [25, 2, 0];

    /// <summary>The launcher's switch that hands the next argument to <c>getmcruserdata</c> as <c>key:value</c>.</summary>
    public const string UserDataSwitch = "-mcruserdata";

    /// <summary>Creates a deployment.</summary>
    /// <param name="ctfRoot">The folder the archive was extracted to, which <c>ctfroot</c> answers.</param>
    /// <param name="componentName">The component's name.</param>
    /// <param name="frozenPath">The search path, in order; nothing can change it while the program runs.</param>
    /// <param name="arguments">The arguments the main function is called with, after the launcher's own switches were taken out.</param>
    /// <param name="numericArguments">Whether the program was built with <c>mcc -n</c>, which evaluates the arguments that look numeric.</param>
    /// <param name="userData">What <c>-mcruserdata</c> set, by key.</param>
    public ScriptDeployment(
        string ctfRoot,
        string componentName,
        IReadOnlyList<string> frozenPath,
        IReadOnlyList<string> arguments,
        bool numericArguments = false,
        IReadOnlyDictionary<string, string>? userData = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ctfRoot);
        ArgumentException.ThrowIfNullOrEmpty(componentName);
        ArgumentNullException.ThrowIfNull(frozenPath);
        ArgumentNullException.ThrowIfNull(arguments);
        CtfRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ctfRoot));
        ComponentName = componentName;
        FrozenPath = [.. frozenPath.Select(static folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))];
        Arguments = [.. arguments];
        NumericArguments = numericArguments;
        UserData = new Dictionary<string, string>(userData ?? new Dictionary<string, string>(), StringComparer.Ordinal);
    }

    /// <summary>The folder the archive was extracted to.</summary>
    public string CtfRoot { get; }

    /// <summary>The component's name.</summary>
    public string ComponentName { get; }

    /// <summary>The search path, in order.</summary>
    public IReadOnlyList<string> FrozenPath { get; }

    /// <summary>The arguments the main function is called with, each a char row.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Whether the arguments go through <c>mcc -n</c>'s rule before the main sees them.</summary>
    public bool NumericArguments { get; }

    /// <summary>What <c>-mcruserdata</c> set before the program started, by key (case-sensitive).</summary>
    public IReadOnlyDictionary<string, string> UserData { get; }

    /// <summary>The runtime cache's folder, which <c>mcrcachedir</c> answers.</summary>
    public string CacheFolder { get; private init; } = CacheDirectory();

    /// <summary>The main function's file, when the deployment came from an archive that names one.</summary>
    public string? MainFile { get; private init; }

    /// <summary>The archive's manifest, when the deployment came from an archive.</summary>
    public CtfManifest? Manifest { get; private init; }

    /// <summary>
    /// The runtime options the session runs under: the archive's <c>mcc -R</c> options, or the ones
    /// the runtime was started with (stage K4). Only <see cref="RuntimeOptions.SingleCompThread"/>
    /// reaches the session; the host prints the start lines and keeps the log.
    /// </summary>
    public RuntimeOptions RuntimeOptions { get; init; } = RuntimeOptions.None;

    /// <summary>The runtime cache's entry the archive was extracted to, held while the program runs.</summary>
    private CacheEntry? Entry { get; init; }

    /// <summary>
    /// Holds the process's arithmetic to one thread, native and managed, as <c>-R -singleCompThread</c>
    /// asks (stage K4). It cannot be undone: R2025b refuses to raise the count until a restart.
    /// </summary>
    internal static void UseOneComputationThread()
    {
        JGraph.Numerics.LinearAlgebra.Native.OpenBlasLoader.LimitToOneThread();
        JGraph.Numerics.ParallelKernels.MaxDegree = 1;
    }

    /// <summary>Lets the runtime cache's entry go, so a later start may evict it.</summary>
    public void Dispose() => Entry?.Dispose();

    /// <summary>
    /// A deployment from a JGraph archive (a <c>.ctf</c>, or a program carrying one): the archive is
    /// extracted into the runtime cache at <see cref="CacheDirectory"/> unless the cache already holds
    /// a complete, unchanged copy (<c>MCR_CACHE_SIZE</c> sets its limit), <c>ctfroot</c> is that copy,
    /// the frozen path is the manifest's, and the main function is the manifest's, called as
    /// <c>mcc -n</c> built it when the manifest says so.
    /// </summary>
    /// <param name="archivePath">The archive.</param>
    /// <param name="commandLine">The program's command line, <c>-mcruserdata</c> pairs included.</param>
    /// <param name="cacheDirectory">The runtime cache's folder; <see cref="CacheDirectory"/> when not given.</param>
    /// <param name="runtimeOptions">The options the runtime runs under; the archive's own when not given.</param>
    /// <exception cref="InvalidDataException">The file holds no JGraph archive, or a damaged one.</exception>
    public static ScriptDeployment FromArchive(
        string archivePath, IReadOnlyList<string> commandLine, string? cacheDirectory = null, RuntimeOptions? runtimeOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        ArgumentNullException.ThrowIfNull(commandLine);
        string cache = cacheDirectory ?? CacheDirectory();
        using CtfArchiveReader archive = CtfArchiveReader.Open(archivePath);
        CacheEntry entry = new RuntimeCache(cache).Open(archive, Environment.GetEnvironmentVariable("MCR_CACHE_SIZE"));
        try
        {
            CtfManifest manifest = archive.Manifest;
            string? main = manifest.Main is { } function
                ? CtfArchiveReader.Within(entry.Folder, function.File)
                    ?? throw new InvalidDataException($"The archive's main function file '{function.File}' leaves the archive.")
                : null;
            var path = new List<string>();
            foreach (string folder in manifest.MatlabPath)
            {
                path.Add(folder.Length == 0
                    ? entry.Folder
                    : CtfArchiveReader.Within(entry.Folder, folder)
                        ?? throw new InvalidDataException($"The archive's path folder '{folder}' leaves the archive."));
            }

            (IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> userData) = TakeUserData(commandLine);
            return new ScriptDeployment(entry.Folder, manifest.ComponentName, path, arguments, manifest.NumericArguments, userData)
            {
                MainFile = main,
                Manifest = manifest,
                RuntimeOptions = runtimeOptions ?? RuntimeOptions.Parse(manifest.RuntimeOptions),
                Entry = entry,
                CacheFolder = Path.GetFullPath(cache),
            };
        }
        catch
        {
            entry.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The folder <c>mcrcachedir</c> answers, where archives are extracted: a folder per runtime
    /// release under <c>%LOCALAPPDATA%\JGraph\RuntimeCache</c>, or under <c>MCR_CACHE_ROOT</c> when
    /// it is set, as R2025b's is under <c>MatlabRuntimeCache</c> (K0, env_cache_root). The release is
    /// JGraph's own version, since each JGraph Runtime extracts for itself.
    /// </summary>
    public static string CacheDirectory()
    {
        string release = typeof(ScriptDeployment).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string? root = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");
        return root is { Length: > 0 }
            ? Path.Combine(Path.GetFullPath(root), release)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JGraph", "RuntimeCache", release);
    }

    /// <summary>
    /// Takes the launcher's <c>-mcruserdata key:value</c> pairs out of a command line, as R2025b's
    /// launcher does before the main sees it (K0, env_userdata: <c>nargin</c> dropped by two and
    /// <c>getmcruserdata('ParallelProfile')</c> answered the value). The key ends at the first colon.
    /// A switch with no value after it, or a value with no colon, is left where it is.
    /// </summary>
    public static (IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> UserData) TakeUserData(
        IReadOnlyList<string> commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var arguments = new List<string>();
        var userData = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < commandLine.Count; i++)
        {
            if (commandLine[i] == UserDataSwitch && i + 1 < commandLine.Count
                && commandLine[i + 1].IndexOf(':', StringComparison.Ordinal) is > 0 and int colon)
            {
                userData[commandLine[i + 1][..colon]] = commandLine[i + 1][(colon + 1)..];
                i++;
                continue;
            }

            arguments.Add(commandLine[i]);
        }

        return (arguments, userData);
    }

    /// <summary>
    /// A deployment over a plain folder standing in for an extracted archive: <paramref name="ctfRoot"/>
    /// is <c>ctfroot</c>, the component is named after the main file, and the frozen path is the
    /// main's folder followed by every other folder under the root that holds a file, in name
    /// order. A folder whose name starts with <c>.</c>, a <c>private</c> folder, and a class or
    /// package folder (<c>@</c>, <c>+</c>) are left off, as they are never on MATLAB's path.
    /// </summary>
    /// <param name="ctfRoot">The folder.</param>
    /// <param name="mainFile">The main function's file.</param>
    /// <param name="commandLine">The program's command line, <c>-mcruserdata</c> pairs included.</param>
    /// <param name="numericArguments">Whether the program behaves as one built with <c>mcc -n</c>.</param>
    public static ScriptDeployment FromFolder(string ctfRoot, string mainFile, IReadOnlyList<string> commandLine, bool numericArguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(ctfRoot);
        ArgumentException.ThrowIfNullOrEmpty(mainFile);
        string root = Path.GetFullPath(ctfRoot);
        string mainFolder = Path.GetDirectoryName(Path.GetFullPath(mainFile)) ?? root;
        var path = new List<string> { mainFolder };
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (string folder in Folders(root).Order(comparer))
        {
            if (!path.Contains(folder, comparer) && Directory.EnumerateFiles(folder).Any())
            {
                path.Add(folder);
            }
        }

        (IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> userData) = TakeUserData(commandLine);
        return new ScriptDeployment(root, Path.GetFileNameWithoutExtension(mainFile), path, arguments, numericArguments, userData);
    }

    /// <summary>The root and every folder under it that could be on a path.</summary>
    private static IEnumerable<string> Folders(string root)
    {
        yield return Path.TrimEndingDirectorySeparator(root);
        foreach (string sub in Directory.EnumerateDirectories(root))
        {
            string leaf = Path.GetFileName(sub);
            if (leaf.StartsWith('.') || leaf.StartsWith('@') || leaf.StartsWith('+') || leaf == "private")
            {
                continue;
            }

            foreach (string folder in Folders(sub))
            {
                yield return folder;
            }
        }
    }
}
