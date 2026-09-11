using System.IO;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What <c>.m</c> files the search folders hold, kept so that "is this built-in name shadowed by a
/// file" can be answered without touching the disk (M145, step 3). Per folder the index keeps the
/// set of file stems and the folder's write time as of the read; from those it derives one
/// <see cref="Shadowing"/> set — the stems that are also built-in names — which is empty in almost
/// every session and is the only thing a built-in call will ever consult.
/// </summary>
/// <remarks>
/// A stale index must never <em>silently</em> choose, so it refreshes on two triggers. Every file
/// mutation the interpreter itself performs reaches <see cref="Invalidate"/> through the host's
/// <c>FileChanging</c> seam, so a file written in one statement is seen in that statement. A change
/// made by another program is seen at the next top-level statement: the folder times are re-read at
/// most once per statement epoch, so a loop pays nothing per iteration. A name that is not a
/// built-in never asks the index at all — a miss still probes the disk, which is what keeps
/// "written in this statement, called in the same loop" true without any refresh.
/// </remarks>
internal sealed class JgsFileIndex
{
    /// <summary>
    /// One folder the index knows. Handed out by <see cref="Track"/> so that a caller asking about
    /// the same folder on every call — the resolver, about a file's <c>private/</c> — keeps the
    /// entry and pays no path hashing; the entry is never removed, so a kept one stays valid.
    /// </summary>
    internal sealed class Folder
    {
        public readonly HashSet<string> Stems = new(StringComparer.Ordinal);
        public string Path = "";
        public DateTime Written;
        public bool Dirty = true;
        public bool Read;
        public bool Warned;
        public int CheckedEpoch = -1;
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly Dictionary<string, Folder> _folders = new(PathComparer);
    private readonly Func<IEnumerable<string>> _searchFolders;
    private readonly Func<int> _statementEpoch;
    private readonly Func<string, bool> _isBuiltin;
    private readonly Action<string> _warn;

    private HashSet<string>? _shadowing;
    private List<string> _lastSearched = [];
    private int _checkedEpoch = -1;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<string> _unreported = [];

    /// <summary>
    /// Told each built-in name a search folder is seen to shadow — when the set is rebuilt (its
    /// first build, <c>addpath</c>, <c>cd</c>, a file appearing) and again on every later read
    /// until it answers true. The function path raises MATLAB's own warning from it and answers
    /// true once it has; a name is remembered as reported only then, so a build under a dialect
    /// that does not warn (the JGS statement a batch starts with) leaves the warning for the
    /// MATLAB script that follows, which raises it at its first built-in call.
    /// </summary>
    internal Func<string, bool>? ShadowingFound { get; set; }

    /// <summary>
    /// Creates the index over <paramref name="searchFolders"/> (the folders a bare name is looked
    /// for in, in search order, read live because <c>cd</c> and <c>addpath</c> move them),
    /// refreshed by time at most once per <paramref name="statementEpoch"/>, filtering stems by
    /// <paramref name="isBuiltin"/> and reporting an unreadable folder through <paramref name="warn"/>.
    /// </summary>
    public JgsFileIndex(
        Func<IEnumerable<string>> searchFolders, Func<int> statementEpoch,
        Func<string, bool> isBuiltin, Action<string> warn)
    {
        _searchFolders = searchFolders;
        _statementEpoch = statementEpoch;
        _isBuiltin = isBuiltin;
        _warn = warn;
    }

    /// <summary>How a folder is listed; a test can stand in for a folder that cannot be read.</summary>
    internal Func<string, IEnumerable<string>> EnumerateFiles { get; set; } =
        static folder => Directory.EnumerateFiles(folder, "*.m");

    /// <summary>How a folder's write time is read; a test can freeze it.</summary>
    internal Func<string, DateTime> LastWrite { get; set; } = Directory.GetLastWriteTimeUtc;

    /// <summary>
    /// The built-in names a file on some search folder also claims. Refreshed on the way in, so
    /// it is current as of this statement for the interpreter's own writes and as of the last
    /// statement boundary for anyone else's.
    /// </summary>
    public IReadOnlySet<string> Shadowing
    {
        get
        {
            Refresh();
            if (_unreported.Count > 0)
            {
                Report();
            }

            return _shadowing!;
        }
    }

    /// <summary>The <c>.m</c> stems <paramref name="folder"/> holds, read now if it has not been.</summary>
    public IReadOnlySet<string> StemsOf(string folder) => StemsOfFullPath(Path.GetFullPath(folder));

    /// <summary>
    /// The same for a folder already given as a full path — a file's <c>private/</c> folder, which
    /// the resolver asks about on the way to every built-in call from that file, so nothing here
    /// touches the path string. Refreshed by the same two rules as the search folders: at once after
    /// an invalidation, and by write time at most once per statement epoch.
    /// </summary>
    internal IReadOnlySet<string> StemsOfFullPath(string folder) => StemsOf(EntryFor(folder));

    /// <summary>The entry for <paramref name="fullFolder"/>, to hand back to <see cref="StemsOf(Folder)"/> on every call.</summary>
    internal Folder Track(string fullFolder) => EntryFor(fullFolder);

    /// <summary>The stems of a tracked folder, refreshed as <see cref="StemsOfFullPath"/> refreshes.</summary>
    internal IReadOnlySet<string> StemsOf(Folder entry)
    {
        int epoch = _statementEpoch();
        bool checkTime = epoch != entry.CheckedEpoch;
        entry.CheckedEpoch = epoch;
        if (entry.Dirty || !entry.Read || (checkTime && TimeMoved(entry.Path, entry)))
        {
            ReadFolder(entry.Path, entry);
        }

        return entry.Stems;
    }

    /// <summary>
    /// Tells the index that <paramref name="path"/> is about to change — a file written, deleted,
    /// moved or copied, or a folder made or removed. Null means everything: <c>rehash</c>, or a
    /// change to what the search folders are. Only a folder the index knows is marked; the rest
    /// are read fresh when they are first asked about.
    /// </summary>
    public void Invalidate(string? path)
    {
        if (path is null)
        {
            foreach (Folder entry in _folders.Values)
            {
                entry.Dirty = true;
            }

            _shadowing = null;
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return; // not a path the file system will accept either; the write itself will say so
        }

        // Only a .m file can change what a name means. A write with any other extension — an image
        // in a loop, a diary, a .mat — leaves the stems alone, unless the path names a folder the
        // index knows (a folder called v1.2 has an "extension" too).
        string extension = Path.GetExtension(full);
        if (extension.Length > 0
            && !extension.Equals(".m", StringComparison.OrdinalIgnoreCase)
            && !_folders.ContainsKey(full))
        {
            return;
        }

        // The folder the path sits in gains or loses a file; the path itself, when it names a folder
        // the index knows (movefile into it, rmdir of it), changes as a whole.
        MarkDirty(Path.GetDirectoryName(full));
        MarkDirty(full);
    }

    private void MarkDirty(string? folder)
    {
        if (folder is not null && _folders.TryGetValue(folder, out Folder? entry))
        {
            entry.Dirty = true;
            _shadowing = null;
        }
    }

    private Folder EntryFor(string folder)
    {
        if (!_folders.TryGetValue(folder, out Folder? entry))
        {
            entry = new Folder { Path = folder };
            _folders[folder] = entry;
        }

        return entry;
    }

    private void Refresh()
    {
        int epoch = _statementEpoch();
        bool checkTimes = epoch != _checkedEpoch;
        _checkedEpoch = epoch;

        // The common case, once per built-in call: the set is built, nothing has been invalidated
        // since (an invalidation drops it), and this statement's folder times were already read.
        if (!checkTimes && _shadowing is not null)
        {
            return;
        }

        var searched = new List<string>();
        bool changed = _shadowing is null;
        foreach (string folder in _searchFolders())
        {
            string full;
            try
            {
                full = Path.GetFullPath(folder);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (searched.Contains(full, PathComparer))
            {
                continue;
            }

            searched.Add(full);
            Folder entry = EntryFor(full);
            if (entry.Dirty || (checkTimes && entry.Read && TimeMoved(full, entry)))
            {
                ReadFolder(full, entry);
                changed = true;
            }
        }

        // Folders that joined or left the search order change the answer without any folder changing.
        if (!changed && !searched.SequenceEqual(_lastSearched, PathComparer))
        {
            changed = true;
        }

        _lastSearched = searched;
        if (!changed)
        {
            return;
        }

        var shadowing = new HashSet<string>(StringComparer.Ordinal);
        foreach (string folder in searched)
        {
            foreach (string stem in _folders[folder].Stems)
            {
                if (_isBuiltin(stem))
                {
                    shadowing.Add(stem);
                }
            }
        }

        _shadowing = shadowing;
        _unreported.Clear();
        foreach (string name in shadowing)
        {
            if (!_reported.Contains(name))
            {
                _unreported.Add(name);
            }
        }
    }

    /// <summary>Offers every shadowing name not yet warned about to <see cref="ShadowingFound"/>, keeping the ones it declines.</summary>
    private void Report()
    {
        if (ShadowingFound is not { } report)
        {
            return;
        }

        for (int i = _unreported.Count - 1; i >= 0; i--)
        {
            if (report(_unreported[i]))
            {
                _reported.Add(_unreported[i]);
                _unreported.RemoveAt(i);
            }
        }
    }

    private bool TimeMoved(string folder, Folder entry)
    {
        try
        {
            // A folder that is not there answers a fixed time for as long as it is not there; the
            // read below records it, so the folder is not re-read every statement for not existing.
            return LastWrite(folder) != entry.Written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // let the read decide, and warn, below
        }
    }

    /// <summary>
    /// Reads one folder. A folder that cannot be read keeps the set it had and says so once: an
    /// index that treated an outage as "no files here" would let a built-in quietly stand in for
    /// the file that shadows it, which is the one substitution this index exists to prevent.
    /// </summary>
    private void ReadFolder(string folder, Folder entry)
    {
        var stems = new HashSet<string>(StringComparer.Ordinal);
        DateTime written;
        try
        {
            // The time is taken before the listing, so a write that lands during the listing moves
            // the time past what was recorded and the next statement reads the folder again.
            written = LastWrite(folder);
            foreach (string file in EnumerateFiles(folder))
            {
                if (Path.GetExtension(file).Equals(".m", StringComparison.OrdinalIgnoreCase))
                {
                    stems.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // A folder that is not there has no files in it — the run's script folder is often one,
            // and most files have no private/ folder beside them. One that has been removed since it
            // was read is the same case: a loaded file from it is the loader's business, not the index's.
            entry.Written = MissingFolderTime(folder);
            entry.Stems.Clear();
            entry.Dirty = false;
            entry.Read = true;
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!entry.Warned)
            {
                entry.Warned = true;
                _warn($"Warning: the folder '{folder}' could not be read ({ex.Message}); "
                    + (entry.Read ? "its files are taken as they were last seen." : "no files are taken from it."));
            }

            entry.Dirty = false; // asking again before something changes would only warn again
            entry.Read = true;
            return;
        }

        entry.Written = written;
        entry.Stems.Clear();
        entry.Stems.UnionWith(stems);
        entry.Dirty = false;
        entry.Read = true;
        entry.Warned = false;
    }

    /// <summary>What the write-time read answers for a folder that does not exist, so that <see cref="TimeMoved"/> stays false until it does.</summary>
    private DateTime MissingFolderTime(string folder)
    {
        try
        {
            return LastWrite(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
