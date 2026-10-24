using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace JGraph.Deploy;

/// <summary>How a standalone program gets the JGraph Runtime it runs on (decision KD4).</summary>
public enum RuntimeDelivery
{
    /// <summary>The program runs on a JGraph Runtime installed on the machine: R2025b's model, and the default.</summary>
    Installed,

    /// <summary>The runtime is copied beside the program, which then runs wherever its folder is copied.</summary>
    Bundled,
}

/// <summary>
/// A program's version as R2025b's mcc reads it from <c>-W main:name,version=...</c> and
/// <c>ExecutableVersion</c>: at most four integers from 0 to 65534, separated by dots, the missing
/// ones zero (<c>2.3</c> is <c>2.3.0.0</c>, k6_pe).
/// </summary>
/// <param name="Major">The first part.</param>
/// <param name="Minor">The second part.</param>
/// <param name="Build">The third part.</param>
/// <param name="Revision">The fourth part.</param>
public readonly record struct ProgramVersion(int Major, int Minor, int Build, int Revision)
{
    /// <summary>The version a build without one gets, <c>1.0.0.0</c>.</summary>
    public static readonly ProgramVersion Default = new(1, 0, 0, 0);

    /// <summary>The version <paramref name="text"/> names, or null when R2025b refuses it.</summary>
    public static ProgramVersion? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] parts = text.Split('.');
        if (parts.Length is 0 or > 4)
        {
            return null;
        }

        var values = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)
                || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) || values[i] > 65534)
            {
                return null;
            }
        }

        return new ProgramVersion(values[0], values[1], values[2], values[3]);
    }

    /// <summary>R2025b's refusal of a version string (<c>Compiler:compiler:COM_ERR_INVALID_VERSION</c>).</summary>
    public static string Refusal(string text) =>
        $"Version string \"{text}\" is invalid. Use at most four integers (0 to 65534), separated by \".\", to specify the version.";

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Build}.{Revision}");
}

/// <summary>What a standalone program is built with besides its archive.</summary>
public sealed record StandaloneProgramOptions
{
    /// <summary>The folder the program and its readme are written to; made when missing, as R2025b's mcc makes <c>-d</c>'s.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The program's name, without <c>.exe</c>.</summary>
    public required string ExecutableName { get; init; }

    /// <summary>Whether the program is a Windows program with no console (<c>mcc -e</c>).</summary>
    public bool Windowed { get; init; }

    /// <summary>False for <c>mcc -C</c>: the archive is written beside the program as <c>&lt;name&gt;.ctf</c>.</summary>
    public bool EmbedArchive { get; init; } = true;

    /// <summary>The icon's images (<c>mcc -r</c>'s icon file), largest first; none keeps the launcher's own.</summary>
    public IReadOnlyList<IconImage> IconImages { get; init; } = [];

    /// <summary>The program's version resource.</summary>
    public ProgramVersion Version { get; init; } = ProgramVersion.Default;

    /// <summary>What the program prints for a first argument of <c>-?</c> or <c>/?</c> (<c>mcc -h</c>); null for R2025b's usage line.</summary>
    public string? HelpText { get; init; }

    /// <summary>Where the program finds its runtime.</summary>
    public RuntimeDelivery RuntimeDelivery { get; init; } = RuntimeDelivery.Installed;

    /// <summary>The runtime copied beside the program for <see cref="RuntimeDelivery.Bundled"/>.</summary>
    public RuntimeLayout? Runtime { get; init; }

    /// <summary>The folder holding the launcher templates; <see cref="StandaloneProgram.TemplateFolder"/> when not given.</summary>
    public string? TemplateFolder { get; init; }
}

/// <summary>
/// Writes a standalone program (compiler and SDK plan, stage K6) as R2025b's mcc writes one: a copy of
/// the launcher (<c>native/jglaunch</c>, its console build for <c>mcc -m</c> and its windowed build for
/// <c>-e</c>) with the program's icon, version and help text in its resources, and the archive
/// appended after its last byte, or written beside it as <c>&lt;name&gt;.ctf</c> for <c>-C</c>; then
/// <c>readme.txt</c>. The launcher reads the JGraph Runtime version it needs from the resource
/// <c>JGRAPH_RUNTIME</c> and its help from <c>JGRAPH_HELP</c>.
/// </summary>
/// <remarks>
/// R2025b adds the icon it is given as a second, language-neutral icon group beside its launcher's
/// own, and every entry of that group carries five bytes of the image's PNG header where the image's
/// size belongs (k6_pe: 13 by 0 pixels, 100 colours); JGraph replaces the launcher's icon with a
/// group that describes its images. The resources are written through the Windows resource update
/// API, so a program can be built only on Windows (decision KO5).
/// </remarks>
public static class StandaloneProgram
{
    /// <summary>The console launcher template's file name.</summary>
    public const string ConsoleTemplate = "jglaunch-console.exe";

    /// <summary>The windowed launcher template's file name.</summary>
    public const string WindowedTemplate = "jglaunch-gui.exe";

    /// <summary>The resource naming the JGraph Runtime version the program runs on.</summary>
    public const string RuntimeVersionResource = "JGRAPH_RUNTIME";

    /// <summary>The resource holding the program's help text.</summary>
    public const string HelpResource = "JGRAPH_HELP";

    /// <summary>
    /// The launcher templates beside this library (<c>launchers\win64</c>, where JGraph's build puts
    /// them), else in the JGraph Runtime's <c>launchers\win64</c>; null when neither has them.
    /// </summary>
    public static string? TemplateFolder
    {
        get
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "launchers", RuntimeLayout.Platform);
            if (File.Exists(Path.Combine(beside, ConsoleTemplate)))
            {
                return beside;
            }

            string? runtime = RuntimeLayout.Find()?.LaunchersFolder;
            return runtime is not null && File.Exists(Path.Combine(runtime, ConsoleTemplate)) ? runtime : null;
        }
    }

    /// <summary>
    /// Writes the program <paramref name="build"/> makes, and answers its files in the order
    /// <c>compiler.build.Results.Files</c> lists them (the program, the readme, the separate archive)
    /// and the archive's manifest.
    /// </summary>
    /// <exception cref="FileNotFoundException">The launcher template is missing.</exception>
    /// <exception cref="PlatformNotSupportedException">This is not Windows.</exception>
    public static (IReadOnlyList<string> Files, CtfManifest Manifest) Write(CtfBuild build, StandaloneProgramOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("A standalone program can be built only on Windows.");
        }

        string templates = options.TemplateFolder ?? TemplateFolder
            ?? throw new FileNotFoundException("The launcher templates were not found beside JGraph or in the JGraph Runtime.");
        string template = Path.Combine(templates, options.Windowed ? WindowedTemplate : ConsoleTemplate);
        if (!File.Exists(template))
        {
            throw new FileNotFoundException($"The launcher template '{template}' is missing.", template);
        }

        string folder = Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string program = Path.Combine(folder, options.ExecutableName + ".exe");
        string separate = Path.Combine(folder, options.ExecutableName + ".ctf");
        string staged = program + ".partial";
        File.Copy(template, staged, overwrite: true);
        CtfManifest manifest;
        try
        {
            var data = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [RuntimeVersionResource] = Encoding.UTF8.GetBytes(RuntimeLayout.CurrentVersion),
            };
            if (options.HelpText is { } help)
            {
                data[HelpResource] = Encoding.UTF8.GetBytes(help);
            }

            PeResources.Stamp(staged, options.IconImages, VersionBlock(options.Version), data);
            using var archive = new MemoryStream();
            manifest = CtfArchiveWriter.Write(archive, build);
            if (options.EmbedArchive)
            {
                using FileStream file = File.Open(staged, FileMode.Append, FileAccess.Write);
                archive.Position = 0;
                archive.CopyTo(file);
                File.Delete(separate);
            }
            else
            {
                File.WriteAllBytes(separate, archive.ToArray());
            }

            File.Move(staged, program, overwrite: true);
        }
        finally
        {
            File.Delete(staged);
        }

        if (options.RuntimeDelivery == RuntimeDelivery.Bundled)
        {
            Bundle(options.Runtime ?? throw new ArgumentException("A bundled program needs the runtime to bundle.", nameof(options)), folder);
        }

        string readme = Path.Combine(folder, "readme.txt");
        File.WriteAllText(readme, Readme(options), new UTF8Encoding(false));
        var files = new List<string> { program, readme };
        if (!options.EmbedArchive)
        {
            files.Add(separate);
        }

        return (files, manifest);
    }

    /// <summary>
    /// <c>readme.txt</c>: JGraph's own text under R2025b's title line and three numbered sections
    /// (k6_build: <c>&lt;name&gt; Executable</c>, then 1. Prerequisites for Deployment, 2. Files to
    /// Deploy and Package with its list, 3. Definitions), with CR LF line ends as R2025b writes them.
    /// The words are JGraph's, since R2025b's are MathWorks'.
    /// </summary>
    public static string Readme(StandaloneProgramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string version = RuntimeLayout.CurrentVersion;
        string name = options.ExecutableName;
        var text = new StringBuilder();
        text.Append(name).Append(" Executable\n\n");
        text.Append("1. Prerequisites for Deployment \n\n");
        if (options.RuntimeDelivery == RuntimeDelivery.Bundled)
        {
            text.Append("This program carries the JGraph Runtime (").Append(version).Append(") in its folder:\n")
                .Append("copy the whole folder, and it runs where it is copied. It needs the .NET 8\n")
                .Append("Desktop Runtime (x64), which most Windows computers already have.\n");
        }
        else
        {
            text.Append("Verify that the JGraph Runtime (").Append(version).Append(") is installed.\n")
                .Append("The program finds it through the JGRAPH_RUNTIME_ROOT environment variable,\n")
                .Append("then the registry key the runtime's installer writes, then its own folder.\n")
                .Append("The JGraph Runtime needs the .NET 8 Desktop Runtime (x64).\n");
        }

        text.Append("\n2. Files to Deploy and Package\n\n");
        text.Append("Files to Package for Standalone \n================================\n");
        if (!options.EmbedArchive)
        {
            text.Append('-').Append(name).Append(".ctf (component technology file)\n");
        }

        text.Append('-').Append(name).Append(".exe\n");
        text.Append(options.RuntimeDelivery == RuntimeDelivery.Bundled
            ? "-jgraph-runtime.json and the bin folder (the JGraph Runtime)\n"
            : "-The JGraph Runtime installer, when the computer has no JGraph Runtime\n");
        text.Append("-This readme file \n\n\n\n");
        text.Append("3. Definitions\n\n");
        text.Append("A standalone program is a launcher with its deployable archive (its code and data)\n")
            .Append("appended; it runs the archive's main function on the JGraph Runtime, which\n")
            .Append("extracts the archive into its runtime cache first.\n\n\n\n");
        return text.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>Copies the runtime's marker and engine beside the program, where its launcher looks last.</summary>
    private static void Bundle(RuntimeLayout runtime, string folder)
    {
        File.Copy(Path.Combine(runtime.Root, RuntimeLayout.MarkerFile), Path.Combine(folder, RuntimeLayout.MarkerFile), overwrite: true);
        string bin = Path.Combine(folder, "bin", RuntimeLayout.Platform);
        foreach (string file in Directory.EnumerateFiles(runtime.BinFolder, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(bin, Path.GetRelativePath(runtime.BinFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// The version resource R2025b writes (k6_pe): the version as the file's and the product's, in
    /// the fixed part and as the two strings of one language-neutral Unicode table.
    /// </summary>
    internal static byte[] VersionBlock(ProgramVersion version)
    {
        var strings = new VersionNode("000004b0", 1, null,
        [
            VersionNode.Text("FileVersion", version.ToString()),
            VersionNode.Text("ProductVersion", version.ToString()),
        ]);
        uint ms = ((uint)version.Major << 16) | (uint)version.Minor;
        uint ls = ((uint)version.Build << 16) | (uint)version.Revision;
        var fixedInfo = new byte[52];
        uint[] fields = [0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 0x40004, 1, 0, 0, 0];
        for (int i = 0; i < fields.Length; i++)
        {
            BitConverter.TryWriteBytes(fixedInfo.AsSpan(4 * i), fields[i]);
        }

        var root = new VersionNode("VS_VERSION_INFO", 0, fixedInfo,
        [
            new VersionNode("StringFileInfo", 1, null, [strings]),
            new VersionNode("VarFileInfo", 1, null, [new VersionNode("Translation", 0, [0, 0, 0xB0, 0x04], [])]),
        ]);
        return root.Bytes();
    }

    /// <summary>One node of a version resource: length, value length, type, key, value, children, each 32-bit aligned.</summary>
    private sealed class VersionNode(string key, ushort type, byte[]? value, IReadOnlyList<VersionNode> children)
    {
        public static VersionNode Text(string key, string text) => new(key, 1, Encoding.Unicode.GetBytes(text + "\0"), []);

        public byte[] Bytes()
        {
            using var buffer = new MemoryStream();
            var writer = new BinaryWriter(buffer);
            writer.Write((ushort)0);
            // A string's value length counts its characters; any other value's, its bytes.
            writer.Write((ushort)(value is null ? 0 : type == 1 ? value.Length / 2 : value.Length));
            writer.Write(type);
            writer.Write(Encoding.Unicode.GetBytes(key + "\0"));
            Align(writer);
            if (value is not null)
            {
                writer.Write(value);
                Align(writer);
            }

            foreach (VersionNode child in children)
            {
                writer.Write(child.Bytes());
                Align(writer);
            }

            writer.Flush();
            byte[] bytes = buffer.ToArray();
            BitConverter.TryWriteBytes(bytes, (ushort)bytes.Length);
            return bytes;
        }

        private static void Align(BinaryWriter writer)
        {
            while (writer.BaseStream.Position % 4 != 0)
            {
                writer.Write((byte)0);
            }
        }
    }
}

/// <summary>A program's resources replaced in place, through the Windows resource update API.</summary>
internal static class PeResources
{
    private const int Icon = 3;
    private const int RcData = 10;
    private const int GroupIcon = 14;
    private const int Version = 16;
    private const ushort Neutral = 0;

    /// <summary>
    /// Replaces the launcher's icon with <paramref name="icons"/> (when there are any), its version
    /// block with <paramref name="version"/>, and adds each of <paramref name="data"/> as RCDATA; all
    /// language-neutral, as the launcher's own resources are.
    /// </summary>
    public static void Stamp(string file, IReadOnlyList<IconImage> icons, byte[] version, IReadOnlyDictionary<string, byte[]> data)
    {
        int[] replaced = icons.Count > 0 ? IconIds(file) : [];
        IntPtr update = BeginUpdateResourceW(file, false);
        if (update == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot write the resources of '{file}'.");
        }

        bool written = false;
        try
        {
            if (icons.Count > 0)
            {
                foreach (int id in replaced)
                {
                    Update(update, Icon, id, null);
                }

                for (int i = 0; i < icons.Count; i++)
                {
                    Update(update, Icon, i + 1, icons[i].Data);
                }

                Update(update, GroupIcon, 1, IconGroup(icons));
            }

            Update(update, Version, 1, version);
            foreach ((string name, byte[] bytes) in data)
            {
                if (!UpdateResourceW(update, RcData, name, Neutral, bytes, (uint)bytes.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot write the resource '{name}'.");
                }
            }

            written = true;
        }
        finally
        {
            if (!EndUpdateResourceW(update, !written) && written)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot write the resources of '{file}'.");
            }
        }
    }

    /// <summary>
    /// The icon group of <paramref name="images"/>: per image what its icon file's directory says (a
    /// size of 256 or more written as 0), its length, and its resource number.
    /// </summary>
    internal static byte[] IconGroup(IReadOnlyList<IconImage> images)
    {
        var group = new byte[6 + 14 * images.Count];
        BitConverter.TryWriteBytes(group.AsSpan(2), (ushort)1);
        BitConverter.TryWriteBytes(group.AsSpan(4), (ushort)images.Count);
        for (int i = 0; i < images.Count; i++)
        {
            IconImage image = images[i];
            int at = 6 + 14 * i;
            group[at] = (byte)(image.Width >= 256 ? 0 : image.Width);
            group[at + 1] = (byte)(image.Height >= 256 ? 0 : image.Height);
            group[at + 2] = (byte)image.ColorCount;
            BitConverter.TryWriteBytes(group.AsSpan(at + 4), (ushort)image.Planes);
            BitConverter.TryWriteBytes(group.AsSpan(at + 6), (ushort)image.BitCount);
            BitConverter.TryWriteBytes(group.AsSpan(at + 8), (uint)image.Data.Length);
            BitConverter.TryWriteBytes(group.AsSpan(at + 12), (ushort)(i + 1));
        }

        return group;
    }

    /// <summary>The resource numbers of the images the program's icon group 1 names.</summary>
    private static int[] IconIds(string file)
    {
        IntPtr module = LoadLibraryExW(file, IntPtr.Zero, 0x2 | 0x20);
        if (module == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            IntPtr found = FindResourceExW(module, GroupIcon, 1, Neutral);
            IntPtr loaded = found == IntPtr.Zero ? IntPtr.Zero : LoadResource(module, found);
            IntPtr bytes = loaded == IntPtr.Zero ? IntPtr.Zero : LockResource(loaded);
            if (bytes == IntPtr.Zero)
            {
                return [];
            }

            var group = new byte[SizeofResource(module, found)];
            Marshal.Copy(bytes, group, 0, group.Length);
            int count = BitConverter.ToUInt16(group, 4);
            return [.. Enumerable.Range(0, count).Select(i => (int)BitConverter.ToUInt16(group, 6 + 14 * i + 12))];
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static void Update(IntPtr update, int type, int name, byte[]? bytes)
    {
        if (!UpdateResourceW(update, type, name, Neutral, bytes, (uint)(bytes?.Length ?? 0)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot write resource {type}/{name}.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResourceW(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(IntPtr update, nint type, nint name, ushort language, byte[]? data, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(IntPtr update, nint type, string name, ushort language, byte[] data, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr update, bool discard);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResourceExW(IntPtr module, nint type, nint name, ushort language);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr loaded);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);
}
