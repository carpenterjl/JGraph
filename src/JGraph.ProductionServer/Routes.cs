using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using JGraph.Deploy;

namespace JGraph.ProductionServer;

/// <summary>
/// The web handlers a request may be routed to (compiler and SDK plan, stage K15), as R2025b's muserve
/// routes (k15_server: route.*): the instance's routes file (<c>--routes-file</c>), each entry's
/// <c>match</c> a regular expression tried against the request target, query included, in order,
/// the first match winning, matching anywhere in the target unless anchored (route.anywhere), case
/// as written (route.case); then the routes an archive carries (<c>ROUTES:</c>), tried against the
/// target after its first segment when that names the archive (<c>/k15comp/arch/web</c> matches
/// <c>^/arch/web$</c>), and only while the instance has a routes file of its own (the limited
/// session: none of the archive's routes answered without one). An instance route wins over an
/// archive's for the same target (route.inst).
/// </summary>
internal sealed class RouteTable
{
    private readonly ConcurrentDictionary<string, Entry[]> _archives = new(StringComparer.Ordinal);

    /// <summary>One route: the pattern and the handler.</summary>
    private sealed record Entry(Regex Pattern, string Component, string Function);

    /// <summary>What a request is routed to.</summary>
    /// <param name="Component">The archive named.</param>
    /// <param name="Function">The web handler function.</param>
    public sealed record Target(string Component, string Function);

    private Entry[] _instance = [];

    /// <summary>Whether the instance has a routes file (an empty one counts).</summary>
    public bool HasInstanceFile { get; private set; }

    /// <summary>Reads the instance's routes file; R2025b's words for one that is not as it should be.</summary>
    /// <exception cref="RoutesException">The file's fault.</exception>
    public void LoadInstanceFile(string path)
    {
        RoutesFile file = RoutesFile.Read(path, componentName: null);
        _instance = [.. file.Routes.Select(static r => new Entry(Compile(r.Match), r.Component ?? "", r.Function))];
        HasInstanceFile = true;
    }

    /// <summary>Keeps the routes <paramref name="archive"/> carries, read from its manifest's file; none when it carries none.</summary>
    public void SetArchiveRoutes(string name, CtfArchiveReader archive)
    {
        CtfFileEntry? entry = archive.Manifest.Files.FirstOrDefault(static f => f.Path.Replace('\\', '/') == RoutesFile.ArchivePath);
        if (entry is null)
        {
            _archives.TryRemove(name, out _);
            return;
        }

        try
        {
            string text = System.Text.Encoding.UTF8.GetString(archive.ReadFile(entry));
            RoutesFile file = RoutesFile.Parse(text, name);
            _archives[name] = [.. file.Routes.Select(r => new Entry(Compile(r.Match), name, r.Function))];
        }
        catch (Exception ex) when (ex is RoutesException or InvalidDataException)
        {
            _archives.TryRemove(name, out _);
        }
    }

    public void RemoveArchive(string name) => _archives.TryRemove(name, out _);

    /// <summary>The handler for <paramref name="target"/> (the request target, query included), or null when no route matches.</summary>
    public Target? Match(string target)
    {
        foreach (Entry entry in _instance)
        {
            if (entry.Pattern.IsMatch(target))
            {
                return new Target(entry.Component, entry.Function);
            }
        }

        if (!HasInstanceFile || target.Length < 2 || target[0] != '/')
        {
            return null;
        }

        int slash = target.IndexOf('/', 1);
        if (slash < 0)
        {
            return null;
        }

        string archive = target[1..slash];
        if (!_archives.TryGetValue(archive, out Entry[]? routes))
        {
            return null;
        }

        string rest = target[slash..];
        foreach (Entry entry in routes)
        {
            if (entry.Pattern.IsMatch(rest))
            {
                return new Target(entry.Component, entry.Function);
            }
        }

        return null;
    }

    private static Regex Compile(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.ECMAScript);
}
