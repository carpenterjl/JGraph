using System.IO;
using System.Threading;

namespace JGraph.Scripting.Workspace;

/// <summary>
/// A MATLAB-style script workspace: a root folder whose scripts, sub-folders, and data files are
/// browsable, and against which bare file names in scripts (<c>readcsv("data.csv")</c>,
/// <c>run("helpers.jgs")</c>) resolve. The workspace is UI-free; <see cref="Changed"/> is raised on a
/// thread-pool thread (debounced), so a UI host must marshal it onto its dispatcher.
/// </summary>
public sealed class ScriptWorkspace : IDisposable
{
    /// <summary>The file extensions the workspace treats as scripts, lowercase with the leading dot.</summary>
    public static readonly IReadOnlyList<string> ScriptExtensions = [".jgs", ".csx", ".cs", ".py"];

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private bool _disposed;

    private ScriptWorkspace(string rootPath)
    {
        RootPath = rootPath;
        _debounce = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Opens the workspace rooted at <paramref name="rootPath"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public static ScriptWorkspace Open(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string fullPath = Path.GetFullPath(rootPath);
        return Directory.Exists(fullPath)
            ? new ScriptWorkspace(fullPath)
            : throw new DirectoryNotFoundException($"Workspace folder not found: '{fullPath}'.");
    }

    /// <summary>The absolute path of the workspace root folder.</summary>
    public string RootPath { get; }

    /// <summary>
    /// Raised (debounced, on a thread-pool thread) when files or folders under the root are created,
    /// deleted, renamed, or modified externally.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Enumerates every script file under the root, recursively, ordered by relative path.</summary>
    public IReadOnlyList<WorkspaceEntry> EnumerateScripts()
    {
        var scripts = new List<WorkspaceEntry>();
        foreach (string path in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
        {
            if (IsScript(path))
                scripts.Add(new WorkspaceEntry(path, Path.GetRelativePath(RootPath, path), IsDirectory: false, []));
        }

        scripts.Sort(static (a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return scripts;
    }

    /// <summary>
    /// Enumerates the whole workspace as a tree: the root's entries, each directory carrying its
    /// children, directories before files, both sorted by name.
    /// </summary>
    public IReadOnlyList<WorkspaceEntry> EnumerateAll() => EnumerateDirectory(RootPath);

    /// <summary>
    /// Resolves a script-supplied file path. Probe order: an absolute path is returned as-is; then the
    /// running script's own directory; then the workspace root; finally the original path unchanged
    /// (letting the eventual file open fail with the ordinary not-found error).
    /// </summary>
    /// <param name="path">The path as written in the script.</param>
    /// <param name="currentScriptDirectory">The directory of the running script, or null when unsaved.</param>
    public string Resolve(string path, string? currentScriptDirectory)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Path.IsPathRooted(path))
            return path;

        if (currentScriptDirectory is { Length: > 0 })
        {
            string local = Path.Combine(currentScriptDirectory, path);
            if (File.Exists(local))
                return local;
        }

        string fromRoot = Path.Combine(RootPath, path);
        return File.Exists(fromRoot) ? fromRoot : path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
    }

    private static bool IsScript(string path) =>
        ScriptExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<WorkspaceEntry> EnumerateDirectory(string directory)
    {
        var entries = new List<WorkspaceEntry>();
        foreach (string sub in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            entries.Add(new WorkspaceEntry(sub, Path.GetRelativePath(RootPath, sub), IsDirectory: true, EnumerateDirectory(sub)));
        foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
            entries.Add(new WorkspaceEntry(file, Path.GetRelativePath(RootPath, file), IsDirectory: false, []));
        return entries;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!_disposed)
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
    }
}
