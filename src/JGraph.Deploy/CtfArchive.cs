using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace JGraph.Deploy;

/// <summary>What goes into an archive: the component, its files and where each one goes, its path and functions.</summary>
public sealed record CtfBuild
{
    /// <summary>The component's name.</summary>
    public required string ComponentName { get; init; }

    /// <summary>What the component is built as.</summary>
    public CtfTargetType TargetType { get; init; } = CtfTargetType.StandaloneApplication;

    /// <summary>The archive's uuid; a fresh one (<see cref="CtfManifest.NewUuid"/>) when not given.</summary>
    public string? Uuid { get; init; }

    /// <summary>Each file's source on disk and its place under the file root (forward slashes).</summary>
    public required IReadOnlyList<(string Source, string ArchivePath)> Files { get; init; }

    /// <summary>Files the build made rather than read (a P-file for <c>mcc -j</c>), by their place under the file root.</summary>
    public IReadOnlyList<(byte[] Contents, string ArchivePath)> MadeFiles { get; init; } = [];

    /// <summary>The structs a Production Server discovery file describes (stage K14).</summary>
    public IReadOnlyList<CtfTypedef> Typedefs { get; init; } = [];

    /// <summary>How the archive's code is stored; <see cref="CtfProtection.PCode"/> when the code files are P-files.</summary>
    public CtfProtection Protection { get; init; } = CtfProtection.Open;

    /// <summary>The folders on the frozen path, relative to the file root, in order.</summary>
    public IReadOnlyList<string> MatlabPath { get; init; } = [];

    /// <summary>The functions a client may call.</summary>
    public IReadOnlyList<CtfFunction> PublicFunctions { get; init; } = [];

    /// <summary>The function a standalone program starts in.</summary>
    public string? MainFunction { get; init; }

    /// <summary>Whether the main's arguments go through <c>mcc -n</c>'s rule.</summary>
    public bool NumericArguments { get; init; }

    /// <summary>The runtime options (<c>-R</c>).</summary>
    public IReadOnlyList<string> RuntimeOptions { get; init; } = [];

    /// <summary>Files the build writes into <c>.META/</c> under the file root (its logs), by name.</summary>
    public IReadOnlyDictionary<string, byte[]> MetaFiles { get; init; } = new Dictionary<string, byte[]>();

    /// <summary>When the archive was made, written into its core properties.</summary>
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Where a build's files go in the archive, by R2025b's rule (stage K2's <c>k2_mcc.out.txt</c> and
/// K0's <c>env_plain</c>): a file under the main function's folder keeps its place under a folder
/// named after the component, and any other file goes under its full path without the drive.
/// </summary>
public static class CtfLayout
{
    /// <summary>The place under the file root of <paramref name="file"/>, with forward slashes.</summary>
    public static string ArchivePath(string file, string mainFolder, string componentName)
    {
        string full = Path.GetFullPath(file);
        string main = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mainFolder)) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string relative = full.StartsWith(main, comparison)
            ? componentName + "/" + full[main.Length..]
            : full[(Path.GetPathRoot(full)?.Length ?? 0)..];
        return relative.Replace('\\', '/');
    }

    /// <summary>
    /// A build from a dependency report: every file the report takes, placed by <see cref="ArchivePath"/>,
    /// the main function's folder first on the path and then each other folder that holds a taken file,
    /// in the report's order (a <c>private</c> folder and a class or package folder are never on a path).
    /// </summary>
    /// <param name="componentName">The component's name.</param>
    /// <param name="mainFile">The main function's file, one of the report's roots.</param>
    /// <param name="report">What the dependency analysis took.</param>
    /// <param name="signature">The inputs and outputs of a code file's function, when known.</param>
    /// <param name="rootFolder">
    /// The folder whose files go under the component's folder: the one mcc runs in (stage K6,
    /// k6_roots: a main built from its parent folder went to <c>fsroot/k6r_main/app/</c>, and one built
    /// from elsewhere under its full path); the main's own folder when not given.
    /// </param>
    public static CtfBuild FromReport(
        string componentName,
        string mainFile,
        DependencyReport report,
        Func<string, (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)?>? signature = null,
        string? rootFolder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(componentName);
        ArgumentNullException.ThrowIfNull(report);
        string main = Path.GetFullPath(mainFile);
        string mainFolder = rootFolder is null ? Path.GetDirectoryName(main) ?? main : Path.GetFullPath(rootFolder);
        var files = report.Files.Select(f => (f.Path, ArchivePath(f.Path, mainFolder, componentName))).ToList();
        string mainPlace = ArchivePath(main, mainFolder, componentName);
        var path = new List<string> { mainPlace.Contains('/') ? mainPlace[..mainPlace.LastIndexOf('/')] : componentName };
        foreach ((_, string archivePath) in files)
        {
            string folder = archivePath.Contains('/') ? archivePath[..archivePath.LastIndexOf('/')] : "";
            if (folder.Length > 0 && !folder.Split('/').Any(IsOffPathFolder) && !path.Contains(folder, StringComparer.Ordinal))
            {
                path.Add(folder);
            }
        }

        string mainName = Path.GetFileNameWithoutExtension(main);
        (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs) parameters = signature?.Invoke(main) ?? ([], []);
        return new CtfBuild
        {
            ComponentName = componentName,
            Files = files,
            MatlabPath = path,
            MainFunction = mainName,
            PublicFunctions = [new CtfFunction(mainName, mainPlace, parameters.Inputs, parameters.Outputs)],
        };
    }

    private static bool IsOffPathFolder(string leaf) =>
        leaf == "private" || leaf.StartsWith('@') || leaf.StartsWith('+') || leaf.StartsWith('.');
}

/// <summary>
/// Writes a JGraph archive: an OPC package (a ZIP) laid out as R2025b's <c>.ctf</c> is, with
/// <c>[Content_Types].xml</c>, <c>_rels/.rels</c>, <c>.META/manifest.xml</c>, the <c>metadata/</c>
/// properties and the files under <c>fsroot/</c>. It is JGraph's own format (section 7.2 of the
/// plan): no signature, no encryption at the open level, and a content type that is JGraph's, so
/// neither runtime mistakes the other's archive for its own. Every entry is dated 1 January 1980 as
/// R2025b dates them, so the same inputs make the same bytes but for the uuid and the creation time.
/// </summary>
public static class CtfArchiveWriter
{
    /// <summary>The package's content type, where R2025b writes <c>application/vnd.mathworks.matlab.compiler</c>.</summary>
    public const string ContentType = "application/vnd.jgraph.compiler";

    /// <summary>The package's friendly name, where R2025b writes "MATLAB Compiler Archive".</summary>
    public const string FriendlyName = "JGraph Compiler Archive";

    /// <summary>The folder in the package that holds the component's files.</summary>
    public const string FileRoot = "fsroot/";

    private static readonly DateTimeOffset EntryDate = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Writes <paramref name="build"/> to <paramref name="path"/>, replacing any file there, and answers its manifest.</summary>
    public static CtfManifest Write(string path, CtfBuild build)
    {
        using FileStream stream = File.Create(path);
        return Write(stream, build);
    }

    /// <summary>Writes <paramref name="build"/> to <paramref name="output"/>, which is left open, and answers its manifest.</summary>
    /// <exception cref="ArgumentException">Two files go to one place, or a place leaves the file root.</exception>
    public static CtfManifest Write(Stream output, CtfBuild build)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(build);

        // Every file read once, hashed, and held until the package is written.
        var contents = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string name, byte[] bytes) in build.MetaFiles)
        {
            Add(contents, ".META/" + name, bytes);
        }

        foreach ((string source, string archivePath) in build.Files)
        {
            Add(contents, archivePath, File.ReadAllBytes(source));
        }

        foreach ((byte[] made, string archivePath) in build.MadeFiles)
        {
            Add(contents, archivePath, made);
        }

        string uuid = build.Uuid ?? CtfManifest.NewUuid(build.ComponentName);
        var manifest = new CtfManifest(
            build.ComponentName, uuid, build.TargetType, build.MatlabPath, build.PublicFunctions,
            [.. contents.Select(c => new CtfFileEntry(c.Key, c.Value.LongLength, Sha256(c.Value)))])
        {
            MainFunction = build.MainFunction,
            NumericArguments = build.NumericArguments,
            RuntimeOptions = build.RuntimeOptions,
            Protection = build.Protection,
            Typedefs = build.Typedefs,
        };

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        Entry(zip, "[Content_Types].xml", ContentTypes(contents.Keys));
        Entry(zip, "_rels/.rels", Relationships());
        Entry(zip, ".META/manifest.xml", manifest.ToXml());
        foreach ((string name, byte[] bytes) in contents)
        {
            Entry(zip, FileRoot + name, bytes);
        }

        string created = build.Created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Entry(zip, "metadata/coreProperties.xml", Xml(new XElement(Cp + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", Cp), new XAttribute(XNamespace.Xmlns + "dcterms", DcTerms),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XElement(DcTerms + "created", new XAttribute(Xsi + "type", "dcterms:W3CDTF"), created))));
        Entry(zip, "metadata/filesystemManifest.xml", Xml(new XElement(FsManifest + "fileEntries",
            new XAttribute("createdByEncoding", "UTF-8"), new XAttribute("createdByPlatform", "win64"), new XAttribute("version", "2.0"),
            contents.Keys.Select(name => new XElement(FsManifest + "fileEntry",
                new XAttribute("content", "/" + FileRoot + name), new XAttribute("date", created),
                new XAttribute("name", "/" + name), new XAttribute("permissions", "010666"), new XAttribute("type", "File"))))));
        Entry(zip, "metadata/mwcoreProperties.xml", Xml(new XElement(CoreProperties + "mwcoreProperties",
            new XElement(CoreProperties + "contentType", ContentType),
            new XElement(CoreProperties + "contentTypeFriendlyName", FriendlyName),
            new XElement(CoreProperties + "matlabRelease", "R2025b"))));
        Entry(zip, "metadata/mwcorePropertiesExtension.xml", Xml(new XElement(CorePropertiesExtension + "mwcoreProperties",
            new XElement(CorePropertiesExtension + "uuid", uuid))));
        Entry(zip, "metadata/mwcorePropertiesReleaseInfo.xml", Xml(new XElement("JGraph_version_info",
            new XElement("version", manifest.JGraphVersion),
            new XElement("release", "R2025b"))));
        return manifest;
    }

    /// <summary>The SHA-256 of <paramref name="bytes"/>, lowercase hex.</summary>
    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace FsManifest = "http://schemas.mathworks.com/package/2013/filesystemManifest";
    private static readonly XNamespace CoreProperties = "http://schemas.mathworks.com/package/2012/coreProperties";
    private static readonly XNamespace CorePropertiesExtension = "http://schemas.mathworks.com/package/2014/corePropertiesExtension";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static void Add(SortedDictionary<string, byte[]> contents, string archivePath, byte[] bytes)
    {
        string name = CtfArchiveReader.CheckedEntryName(archivePath)
            ?? throw new ArgumentException($"The archive place '{archivePath}' leaves the archive's file root.", nameof(archivePath));
        if (!contents.TryAdd(name, bytes))
        {
            throw new ArgumentException($"Two files go to '{name}' in the archive.", nameof(archivePath));
        }
    }

    private static void Entry(ZipArchive zip, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = EntryDate;
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] ContentTypes(IEnumerable<string> names)
    {
        var defaults = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rels"] = "application/vnd.openxmlformats-package.relationships+xml",
            ["xml"] = "application/vnd.jgraph.compiler.fileManifest+xml",
        };
        foreach (string name in names)
        {
            string extension = Path.GetExtension(name).TrimStart('.');
            if (extension.Length > 0)
            {
                defaults.TryAdd(extension, DeploymentRules.CodeExtensions.Contains("." + extension) || extension.Equals("jgs", StringComparison.OrdinalIgnoreCase)
                    ? "application/vnd.jgraph.compiler.code"
                    : "application/vnd.jgraph.compiler.data");
            }
        }

        var overrides = new (string Part, string Type)[]
        {
            ("/metadata/coreProperties.xml", "application/vnd.openxmlformats-package.core-properties+xml"),
            ("/metadata/filesystemManifest.xml", "application/vnd.mathworks.package.filesystemManifest+xml"),
            ("/metadata/mwcoreProperties.xml", "application/vnd.mathworks.package.coreProperties+xml"),
            ("/metadata/mwcorePropertiesExtension.xml", "application/vnd.mathworks.package.corePropertiesExtension+xml"),
            ("/metadata/mwcorePropertiesReleaseInfo.xml", "application/vnd.mathworks.package.corePropertiesReleaseInfo+xml"),
        };
        return Xml(new XElement(ContentTypesNs + "Types",
            defaults.Select(d => new XElement(ContentTypesNs + "Default", new XAttribute("ContentType", d.Value), new XAttribute("Extension", d.Key))),
            overrides.Select(o => new XElement(ContentTypesNs + "Override", new XAttribute("ContentType", o.Type), new XAttribute("PartName", o.Part)))));
    }

    private static byte[] Relationships()
    {
        var targets = new (string Target, string Type)[]
        {
            (".META/manifest.xml", "http://schemas.jgraph.dev/compiler/2026/relationships/configurationManifest"),
            ("metadata/mwcoreProperties.xml", "http://schemas.mathworks.com/package/2012/relationships/coreProperties"),
            ("metadata/mwcorePropertiesExtension.xml", "http://schemas.mathworks.com/package/2014/relationships/corePropertiesExtension"),
            ("metadata/mwcorePropertiesReleaseInfo.xml", "http://schemas.mathworks.com/package/2019/relationships/corePropertiesReleaseInfo"),
            ("metadata/coreProperties.xml", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
            ("metadata/filesystemManifest.xml", "http://schemas.mathworks.com/package/2013/relationships/filesystemManifest"),
        };
        return Xml(new XElement(RelationshipsNs + "Relationships", targets.Select((t, i) => new XElement(RelationshipsNs + "Relationship",
            new XAttribute("Id", "rId" + (i + 1).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("Target", t.Target), new XAttribute("Type", t.Type)))));
    }

    /// <summary>
    /// <paramref name="root"/> as UTF-8 without a byte order mark, under the declaration as R2025b's
    /// parts write it (<c>&lt;?xml version="1.0" encoding="UTF-8" standalone="yes" ?&gt;</c>), which
    /// .NET's writer would spell in lower case and without the space.
    /// </summary>
    internal static byte[] Xml(XElement root, string standalone = "yes")
    {
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = true, IndentChars = "  ", OmitXmlDeclaration = true,
        };
        using var buffer = new MemoryStream();
        buffer.Write(Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"{standalone}\" ?>\n"));
        using (var writer = System.Xml.XmlWriter.Create(buffer, settings))
        {
            root.Save(writer);
        }

        return buffer.ToArray();
    }
}

/// <summary>
/// Reads a JGraph archive, from a <c>.ctf</c> file or from a program with the archive appended after
/// its last byte (R2025b's standalone layout: the ZIP's end record is found from the end of the file
/// and its offsets are relative to where the ZIP starts). An archive built by R2025b is refused by
/// name, since its code is signed and encrypted with MathWorks' keys.
/// </summary>
public sealed class CtfArchiveReader : IDisposable
{
    private readonly ZipArchive zip;

    private CtfArchiveReader(ZipArchive zip, CtfManifest manifest)
    {
        this.zip = zip;
        Manifest = manifest;
    }

    /// <summary>The archive's manifest.</summary>
    public CtfManifest Manifest { get; }

    /// <summary>Opens the archive in <paramref name="path"/>: a <c>.ctf</c>, or a program carrying one.</summary>
    /// <exception cref="InvalidDataException">The file holds no JGraph archive.</exception>
    public static CtfArchiveReader Open(string path)
    {
        FileStream file = File.OpenRead(path);
        try
        {
            return Open(file, leaveOpen: false);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Opens the archive in <paramref name="stream"/>, which must be seekable.</summary>
    /// <exception cref="InvalidDataException">The stream holds no JGraph archive.</exception>
    public static CtfArchiveReader Open(Stream stream, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long start = ZipStart(stream) ?? throw new InvalidDataException("The file holds no archive.");
        Stream view = start == 0 ? stream : new WindowStream(stream, start, leaveOpen);
        ZipArchive zip;
        try
        {
            // The window owns the file it views (unless the caller keeps it), so the archive owns the window.
            zip = new ZipArchive(view, ZipArchiveMode.Read, leaveOpen: start == 0 && leaveOpen, Encoding.UTF8);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("The file holds no archive.");
        }

        try
        {
            if (zip.GetEntry("_xmlsignatures/origin.sigs") is not null || IsMathWorksPackage(zip))
            {
                throw new InvalidDataException(
                    "The archive was built by MATLAB Compiler. JGraph cannot read it: its code is signed and encrypted with MathWorks' keys.");
            }

            ZipArchiveEntry manifestEntry = zip.GetEntry(".META/manifest.xml") ?? throw new InvalidDataException("The archive has no manifest.");
            using Stream manifestStream = manifestEntry.Open();
            return new CtfArchiveReader(zip, CtfManifest.Parse(manifestStream));
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    /// <summary>Reads one file under the file root, checked against the manifest's length and hash.</summary>
    /// <exception cref="InvalidDataException">The file is missing or not what the manifest says.</exception>
    public byte[] ReadFile(CtfFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        ZipArchiveEntry entry = zip.GetEntry(CtfArchiveWriter.FileRoot + file.Path)
            ?? throw new InvalidDataException($"The archive is damaged: '{file.Path}' is missing.");
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        if (bytes.LongLength != file.Length || CtfArchiveWriter.Sha256(bytes) != file.Sha256)
        {
            throw new InvalidDataException($"The archive is damaged: '{file.Path}' is not the file it was built with.");
        }

        return bytes;
    }

    /// <summary>
    /// Writes every file under the file root into <paramref name="folder"/>, creating it, and answers
    /// what it wrote: each file's place under the folder, its length and its last write time.
    /// Nothing outside the folder is ever written.
    /// </summary>
    public IReadOnlyList<(string Path, long Length, DateTime Written)> ExtractTo(string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, CtfManifest.PreferencesFolder));
        var written = new List<(string, long, DateTime)>();
        foreach (CtfFileEntry file in Manifest.Files)
        {
            string target = Within(root, file.Path)
                ?? throw new InvalidDataException($"The archive is damaged: '{file.Path}' leaves the archive's file root.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, ReadFile(file));
            written.Add((file.Path, file.Length, File.GetLastWriteTimeUtc(target)));
        }

        return written;
    }

    /// <summary>Closes the archive.</summary>
    public void Dispose() => zip.Dispose();

    /// <summary>
    /// <paramref name="name"/> as a place under the file root with forward slashes, or <see langword="null"/>
    /// when it would leave it: a rooted name, a drive, an empty segment, <c>.</c> or <c>..</c>.
    /// </summary>
    internal static string? CheckedEntryName(string name)
    {
        string forward = name.Replace('\\', '/');
        if (forward.Length == 0 || forward.StartsWith('/') || forward.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        return forward.Split('/').Any(s => s.Length == 0 || s == "." || s == "..") ? null : forward;
    }

    /// <summary><paramref name="name"/> under <paramref name="root"/>, or <see langword="null"/> when it would leave it.</summary>
    internal static string? Within(string root, string name)
    {
        if (CheckedEntryName(name) is not { } checkedName)
        {
            return null;
        }

        string target = Path.GetFullPath(Path.Combine(root, checkedName));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return target.StartsWith(root + Path.DirectorySeparatorChar, comparison) ? target : null;
    }

    private static bool IsMathWorksPackage(ZipArchive zip)
    {
        if (zip.GetEntry("metadata/mwcoreProperties.xml") is not { } entry)
        {
            return false;
        }

        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Contains("application/vnd.mathworks.matlab.compiler", StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the ZIP in <paramref name="stream"/> starts: the end-of-central-directory record found
    /// from the end, less the central directory's size and offset. A plain ZIP starts at 0; a program
    /// carrying one starts it after its own bytes.
    /// </summary>
    internal static long? ZipStart(Stream stream)
    {
        const int EndRecordLength = 22;
        long length = stream.Length;
        if (length < EndRecordLength)
        {
            return null;
        }

        int window = (int)Math.Min(length, EndRecordLength + ushort.MaxValue);
        var tail = new byte[window];
        stream.Seek(length - window, SeekOrigin.Begin);
        stream.ReadExactly(tail);
        for (int at = window - EndRecordLength; at >= 0; at--)
        {
            if (tail[at] == 0x50 && tail[at + 1] == 0x4b && tail[at + 2] == 0x05 && tail[at + 3] == 0x06
                && at + EndRecordLength + BitConverter.ToUInt16(tail, at + 20) == window)
            {
                long directorySize = BitConverter.ToUInt32(tail, at + 12);
                long directoryOffset = BitConverter.ToUInt32(tail, at + 16);
                long start = length - window + at - directorySize - directoryOffset;
                return start >= 0 ? start : null;
            }
        }

        return null;
    }

    /// <summary>A read-only view of a stream from an offset to its end, so a ZIP appended to a program reads as a ZIP.</summary>
    private sealed class WindowStream(Stream inner, long start, bool leaveOpen) : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => inner.Length - start;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            inner.Position = start + position;
            int read = inner.Read(buffer);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => Length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
