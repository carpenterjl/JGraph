using System.Runtime.Versioning;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// A session's native host (ADR 0180): none until the first native call, then one host for the rest
/// of the session — <c>clear all</c> and <c>unloadlibrary</c> leave it running — and a new one after
/// a crash or a cancel ended it. It ends with the run, the console session, or JGraph.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NativeSession
{
    private NativeHostProcess? _host;

    /// <summary>The live host, started if the session has none (or its last one died).</summary>
    public NativeHostProcess Host
    {
        get
        {
            if (_host is { IsAlive: true } live)
            {
                return live;
            }

            _host?.Dispose();
            _host = null;
            Modules.Clear();
            Symbols.Clear();
            _host = NativeHostProcess.Start();
            return _host;
        }
    }

    /// <summary>The live host, or null when there is none; never starts one.</summary>
    public NativeHostProcess? Current => _host is { IsAlive: true } live ? live : null;

    /// <summary>
    /// The modules the session has loaded into its live host, by the path (or name) they were loaded
    /// by. Emptied when a host dies, since its modules died with it.
    /// </summary>
    public Dictionary<string, long> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The exports resolved in the live host, by module and name; emptied with <see cref="Modules"/>.</summary>
    public Dictionary<(long Module, string Name), long> Symbols { get; } = [];

    /// <summary>Ends the host, if there is one; the next native call starts another.</summary>
    public void Stop()
    {
        NativeHostProcess? host = _host;
        _host = null;
        Modules.Clear();
        Symbols.Clear();
        host?.Dispose();
    }

    /// <summary>
    /// Where a library named <paramref name="name"/> is, in R2025b's order (probe_shrlib_search): a path
    /// as given, otherwise the current folder and then each folder of the path. Any extension given is
    /// kept (<c>s12.lib</c> loads); left off, the MEX extension is tried before <c>.dll</c> — a folder
    /// holding both <c>s4.mexw64</c> and <c>s4.dll</c> loads the first, which is how
    /// <c>shrlibsample</c> is found. Answers the full path, or null when no folder has it, for the
    /// system search to try the name.
    /// </summary>
    public static string? FindLibrary(string name, string currentFolder, IEnumerable<string> pathFolders)
    {
        if (Path.IsPathRooted(name))
        {
            return Candidates(name).FirstOrDefault(File.Exists);
        }

        foreach (string folder in pathFolders.Prepend(currentFolder))
        {
            if (folder.Length == 0)
            {
                continue;
            }

            foreach (string candidate in Candidates(Path.Combine(folder, name)))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length > 0)
        {
            yield return path;
        }

        if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".mexw64", StringComparison.OrdinalIgnoreCase))
        {
            yield return path + ".mexw64";
            yield return path + ".dll";
        }
    }
}
