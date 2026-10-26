using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Deploy;

/// <summary>
/// The folder compiled programs' archives are extracted to (<c>mcrcachedir</c>), kept as R2025b's
/// MATLAB Runtime keeps its own (stage K3's <c>record-k3-cache.ps1</c>, <c>recordings/k3_cache.out.txt</c>):
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>At the cache's root: <c>.deploy_lock</c>, held while the index is read and changed and an
/// archive extracted; <c>.max_size</c>, the limit in bytes; and <c>.mcr_cache_index</c>, four CR LF
/// lines per entry: the archive's uuid, its folder, its size, and when it was last used (UTC, as
/// <c>2026-Oct-24 01:35:35.463113</c>).</item>
/// <item>An entry's folder is the uuid's first six characters and the smallest number that names no
/// folder yet (<c>k0_env0</c>, <c>hello_1</c>). It holds its own <c>.deploy_lock</c>, which a running
/// program keeps open, and <c>.created.ts</c>, written when extraction is complete; an entry without
/// it, or whose folder is gone, is extracted again into the same folder.</item>
/// <item>An archive the index does not know gets a new folder; a folder the index does not know is
/// listed with an empty uuid so it can be evicted.</item>
/// <item>After an extraction, the least recently used entries other than the one starting are deleted
/// while the cache is over its limit; an entry a running program holds is left.</item>
/// </list>
/// Where R2025b was measured to be wrong, JGraph does what its documentation says and records the
/// divergence (ADR 0258): <c>MCR_CACHE_SIZE</c>'s unit counts (R2025b writes <c>150M</c> as 150 bytes),
/// a value that is not a size means the default (R2025b: 0), the variable is honoured on every start
/// (R2025b: only when the cache is first made), and a cached file that was deleted or changed is
/// extracted again (R2025b runs without it, or fails on it). Sizes in the index are bytes on disk.
/// </remarks>
public sealed class RuntimeCache
{
    /// <summary>The limit when nothing sets one: R2025b's 1024 MB.</summary>
    public const long DefaultMaxSize = 1L << 30;

    /// <summary>The lock file, at the root and in each entry.</summary>
    public const string LockFile = ".deploy_lock";

    /// <summary>The limit's file.</summary>
    public const string MaxSizeFile = ".max_size";

    /// <summary>The index.</summary>
    public const string IndexFile = ".mcr_cache_index";

    /// <summary>The marker an entry gets once extraction is complete.</summary>
    public const string CreatedFile = ".created.ts";

    /// <summary>JGraph's list of what an entry's extraction wrote, which a later start checks the entry against.</summary>
    public const string ExtractedFile = ".jgraph_extracted";

    private static readonly TimeSpan LockWait = TimeSpan.FromMinutes(2);

    /// <summary>A cache at <paramref name="directory"/> (the release's folder, which <c>mcrcachedir</c> answers).</summary>
    public RuntimeCache(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    /// <summary>The cache's folder.</summary>
    public string Directory { get; }

    /// <summary>
    /// <c>MCR_CACHE_SIZE</c> as bytes: a number, optionally with a fraction, then optionally
    /// <c>K</c>, <c>M</c> or <c>G</c> (either case, binary multiples) and a <c>B</c>, blanks around it
    /// allowed; <see langword="null"/> for anything else.
    /// </summary>
    public static long? ParseSize(string? text)
    {
        if (text is null || Regex.Match(text, @"^\s*(\d+(?:\.\d+)?)\s*([kKmMgG]?)[bB]?\s*$") is not { Success: true } match)
        {
            return null;
        }

        double number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double scale = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => 1024d,
            "M" => 1024d * 1024,
            "G" => 1024d * 1024 * 1024,
            _ => 1d,
        };
        double bytes = Math.Floor(number * scale);
        return bytes >= long.MaxValue ? long.MaxValue : (long)bytes;
    }

    /// <summary>
    /// The extracted copy of <paramref name="archive"/>, extracted now if the cache has none or its
    /// copy is incomplete, deleted or changed; the entry is held until the answer is disposed.
    /// </summary>
    /// <param name="archive">The archive.</param>
    /// <param name="sizeSetting"><c>MCR_CACHE_SIZE</c>, or <see langword="null"/> when it is not set.</param>
    /// <param name="folderName">
    /// The name the entry's folder gets when it is made now (a Production Server's <c>&lt;archive&gt;_&lt;n&gt;</c>,
    /// stage K15); the cache's own name when null or taken.
    /// </param>
    public CacheEntry Open(CtfArchiveReader archive, string? sizeSetting, string? folderName = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        System.IO.Directory.CreateDirectory(Directory);
        using FileStream rootLock = Lock(Path.Combine(Directory, LockFile));

        long maxSize = MaxSize(sizeSetting);
        List<IndexEntry> index = ReadIndex();
        AddOrphans(index);

        string uuid = archive.Manifest.Uuid;
        IndexEntry? entry = index.FirstOrDefault(e => e.Uuid == uuid);
        bool extracted = false;
        if (entry is null)
        {
            bool wanted = folderName is { Length: > 0 } && folderName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            if (wanted)
            {
                // The name held by another archive's copy that no program runs (an earlier server run's) is taken over.
                IndexEntry? holder = index.FirstOrDefault(e => string.Equals(e.Folder, folderName, StringComparison.OrdinalIgnoreCase));
                string held = Path.Combine(Directory, folderName!);
                if (System.IO.Directory.Exists(held) && !InUse(held))
                {
                    try
                    {
                        System.IO.Directory.Delete(held, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Still held after all; the cache's own name is used.
                    }
                }

                wanted = !System.IO.Directory.Exists(held);
                if (wanted && holder is not null)
                {
                    index.Remove(holder);
                }
            }

            entry = new IndexEntry(uuid, wanted ? folderName! : NewFolderName(uuid, index), 0, DateTime.UtcNow);
            index.Add(entry);
        }

        string folder = Path.Combine(Directory, entry.Folder);
        if (!IsComplete(folder))
        {
            if (System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.Delete(folder, recursive: true);
            }

            System.IO.Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, LockFile), []);
            IReadOnlyList<(string Path, long Length, DateTime Written)> written = archive.ExtractTo(folder);
            File.WriteAllText(Path.Combine(folder, ExtractedFile), string.Concat(written.Select(w => string.Create(
                CultureInfo.InvariantCulture, $"{w.Path}\t{w.Length}\t{w.Written.Ticks}\n"))), new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(folder, CreatedFile), []);
            extracted = true;
        }

        // The entry is held before the root lock is let go, so no other start can evict it in between.
        FileStream entryLock = new(Path.Combine(folder, LockFile), FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        entry.Size = FolderSize(folder);
        entry.LastUsed = DateTime.UtcNow;
        Evict(index, entry, maxSize);
        WriteIndex(index);
        return new CacheEntry(folder, extracted, entryLock);
    }

    /// <summary>
    /// The extracted copy of an archive kept beside its program rather than in a cache: <c>mcc -C</c>'s
    /// <c>&lt;name&gt;.ctf</c>, which R2025b extracts to <c>&lt;name&gt;_mcr</c> in the program's folder
    /// (K0, env_separate_ctf). It is extracted again when it is incomplete or changed, as a cache
    /// entry is, or when it holds another build of the archive (its uuid differs).
    /// </summary>
    public static CacheEntry OpenBeside(CtfArchiveReader archive, string folder)
    {
        ArgumentNullException.ThrowIfNull(archive);
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string uuidFile = Path.Combine(folder, UuidFile);
        bool extracted = false;
        if (!IsComplete(folder) || !File.Exists(uuidFile) || File.ReadAllText(uuidFile) != archive.Manifest.Uuid)
        {
            if (System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.Delete(folder, recursive: true);
            }

            System.IO.Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, LockFile), []);
            IReadOnlyList<(string Path, long Length, DateTime Written)> written = archive.ExtractTo(folder);
            File.WriteAllText(Path.Combine(folder, ExtractedFile), string.Concat(written.Select(w => string.Create(
                CultureInfo.InvariantCulture, $"{w.Path}\t{w.Length}\t{w.Written.Ticks}\n"))), new UTF8Encoding(false));
            File.WriteAllText(uuidFile, archive.Manifest.Uuid, new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(folder, CreatedFile), []);
            extracted = true;
        }

        FileStream entryLock = new(Path.Combine(folder, LockFile), FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new CacheEntry(folder, extracted, entryLock);
    }

    /// <summary>The uuid of the archive a folder beside its program was extracted from (<see cref="OpenBeside"/>).</summary>
    public const string UuidFile = ".jgraph_uuid";

    /// <summary>
    /// The limit: <paramref name="sizeSetting"/> when it is a size (written to <c>.max_size</c>), else
    /// what <c>.max_size</c> holds, else <see cref="DefaultMaxSize"/> (written when the cache has none).
    /// </summary>
    private long MaxSize(string? sizeSetting)
    {
        string file = Path.Combine(Directory, MaxSizeFile);
        if (ParseSize(sizeSetting) is { } set)
        {
            File.WriteAllText(file, set.ToString(CultureInfo.InvariantCulture));
            return set;
        }

        if (File.Exists(file) && long.TryParse(File.ReadAllText(file).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long held))
        {
            return held;
        }

        File.WriteAllText(file, DefaultMaxSize.ToString(CultureInfo.InvariantCulture));
        return DefaultMaxSize;
    }

    /// <summary>Whether <paramref name="folder"/> holds a complete extraction whose files are still as written.</summary>
    private static bool IsComplete(string folder)
    {
        string list = Path.Combine(folder, ExtractedFile);
        if (!File.Exists(Path.Combine(folder, CreatedFile)) || !File.Exists(list))
        {
            return false;
        }

        foreach (string line in File.ReadAllLines(list))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 3 || CtfArchiveReader.Within(folder, fields[0]) is not { } path)
            {
                return false;
            }

            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length.ToString(CultureInfo.InvariantCulture) != fields[1]
                || info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) != fields[2])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The uuid's first six characters and the smallest number that names no folder or index entry.</summary>
    private string NewFolderName(string uuid, List<IndexEntry> index)
    {
        string stem = uuid.Length > 6 ? uuid[..6] : uuid;
        for (int n = 0; ; n++)
        {
            string name = stem + n.ToString(CultureInfo.InvariantCulture);
            if (!System.IO.Directory.Exists(Path.Combine(Directory, name))
                && !index.Any(e => string.Equals(e.Folder, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    /// <summary>Lists every folder the index does not know, with an empty uuid, so it can be evicted.</summary>
    private void AddOrphans(List<IndexEntry> index)
    {
        foreach (string folder in System.IO.Directory.EnumerateDirectories(Directory))
        {
            string name = Path.GetFileName(folder);
            if (!index.Any(e => string.Equals(e.Folder, name, StringComparison.OrdinalIgnoreCase)))
            {
                index.Add(new IndexEntry("", name, FolderSize(folder), System.IO.Directory.GetLastWriteTimeUtc(folder)));
            }
        }

        index.RemoveAll(e => !System.IO.Directory.Exists(Path.Combine(Directory, e.Folder)) && e.Uuid.Length == 0);
    }

    /// <summary>Deletes the least recently used entries other than <paramref name="current"/> while the cache is over <paramref name="maxSize"/>.</summary>
    private void Evict(List<IndexEntry> index, IndexEntry current, long maxSize)
    {
        foreach (IndexEntry candidate in index.Where(e => e != current).OrderBy(e => e.LastUsed).ToList())
        {
            if (index.Sum(e => e.Size) <= maxSize)
            {
                return;
            }

            string folder = Path.Combine(Directory, candidate.Folder);
            if (InUse(folder))
            {
                continue;
            }

            try
            {
                if (System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.Delete(folder, recursive: true);
                }

                index.Remove(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file in it is open after all; it stays for a later start to evict.
            }
        }
    }

    /// <summary>Whether a running program holds the entry in <paramref name="folder"/>.</summary>
    private static bool InUse(string folder)
    {
        string file = Path.Combine(folder, LockFile);
        if (!File.Exists(file))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private List<IndexEntry> ReadIndex()
    {
        string file = Path.Combine(Directory, IndexFile);
        var index = new List<IndexEntry>();
        if (!File.Exists(file))
        {
            return index;
        }

        string[] lines = File.ReadAllText(file).Split("\r\n");
        for (int at = 0; at + 3 < lines.Length; at += 4)
        {
            if (lines[at + 1].Length == 0 || !long.TryParse(lines[at + 2], NumberStyles.None, CultureInfo.InvariantCulture, out long size))
            {
                continue;
            }

            DateTime used = DateTime.TryParseExact(lines[at + 3], TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed) ? parsed : DateTime.MinValue;
            index.Add(new IndexEntry(lines[at], lines[at + 1], size, used));
        }

        return index;
    }

    private void WriteIndex(List<IndexEntry> index)
    {
        var text = new StringBuilder();
        foreach (IndexEntry entry in index)
        {
            text.Append(entry.Uuid).Append("\r\n").Append(entry.Folder).Append("\r\n")
                .Append(entry.Size.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                .Append(entry.LastUsed.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append("\r\n");
        }

        File.WriteAllText(Path.Combine(Directory, IndexFile), text.ToString(), new UTF8Encoding(false));
    }

    private const string TimeFormat = "yyyy-MMM-dd HH:mm:ss.ffffff";

    private static long FolderSize(string folder) =>
        System.IO.Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

    /// <summary>Opens <paramref name="file"/> for this process alone, waiting while another start holds it.</summary>
    private static FileStream Lock(string file)
    {
        DateTime until = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class IndexEntry(string uuid, string folder, long size, DateTime lastUsed)
    {
        public string Uuid { get; } = uuid;

        public string Folder { get; } = folder;

        public long Size { get; set; } = size;

        public DateTime LastUsed { get; set; } = lastUsed;
    }
}

/// <summary>An archive's extracted copy in the runtime cache, held (so it is never evicted) until disposed.</summary>
public sealed class CacheEntry : IDisposable
{
    private readonly FileStream held;

    internal CacheEntry(string folder, bool extracted, FileStream held)
    {
        Folder = folder;
        Extracted = extracted;
        this.held = held;
    }

    /// <summary>The folder the archive was extracted to, which <c>ctfroot</c> answers.</summary>
    public string Folder { get; }

    /// <summary>Whether this start extracted it (the cache had no complete, unchanged copy).</summary>
    public bool Extracted { get; }

    /// <summary>Lets the entry go.</summary>
    public void Dispose() => held.Dispose();
}
