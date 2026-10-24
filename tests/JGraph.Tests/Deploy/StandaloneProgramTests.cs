using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// A standalone program as JGraph writes one (compiler and SDK plan, stage K6): a copy of the launcher
/// template with its resources replaced and its archive appended, against what R2025b's builds held
/// (<c>recordings/k6_pe.out.txt</c>, <c>k6_build.out.txt</c>). The resources are read back through
/// Windows itself, as Explorer and the launcher read them.
/// </summary>
public sealed class StandaloneProgramTests : IDisposable
{
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k6p-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private CtfBuild Build(string name = "k6p", CtfTargetType target = CtfTargetType.StandaloneApplication)
    {
        string main = Path.Combine(_root, "src", name + ".m");
        Directory.CreateDirectory(Path.GetDirectoryName(main)!);
        File.WriteAllText(main, $"function {name}(varargin)\ndisp('hello')\nend\n");
        return new CtfBuild
        {
            ComponentName = name,
            TargetType = target,
            Files = [(main, $"{name}/{name}.m")],
            MatlabPath = [name],
            MainFunction = name,
            PublicFunctions = [new CtfFunction(name, $"{name}/{name}.m", [], [])],
        };
    }

    private StandaloneProgramOptions Options(string name = "k6p") => new() { OutputFolder = Path.Combine(_root, "out"), ExecutableName = name };

    [Fact]
    public void TheTemplatesTravelWithTheBuild()
    {
        string? folder = StandaloneProgram.TemplateFolder;
        Assert.NotNull(folder);
        Assert.Equal(3, Subsystem(Path.Combine(folder, StandaloneProgram.ConsoleTemplate)));
        Assert.Equal(2, Subsystem(Path.Combine(folder, StandaloneProgram.WindowedTemplate)));
    }

    [Fact]
    public void AProgramCarriesItsArchiveVersionHelpAndRuntime()
    {
        (IReadOnlyList<string> files, CtfManifest manifest) = StandaloneProgram.Write(Build(), Options() with
        {
            Version = ProgramVersion.Parse("2.3")!.Value,
            HelpText = "k6 custom help, first line\n  second line, indented\n",
        });

        string program = Path.Combine(_root, "out", "k6p.exe");
        Assert.Equal([program, Path.Combine(_root, "out", "readme.txt")], files);
        Assert.Equal(3, Subsystem(program));

        // The archive is read from the program, as the runtime reads it.
        using (CtfArchiveReader archive = CtfArchiveReader.Open(program))
        {
            Assert.Equal(manifest.Uuid, archive.Manifest.Uuid);
            Assert.Equal("k6p", archive.Manifest.Main?.Name);
        }

        // k6_pe, m_version: 2.3 is 2.3.0.0, in the fixed part and both strings.
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(program);
        Assert.Equal("2.3.0.0", version.FileVersion);
        Assert.Equal("2.3.0.0", version.ProductVersion);
        Assert.Equal((2, 3, 0, 0), (version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart));

        Assert.Equal(RuntimeLayout.CurrentVersion, Encoding.UTF8.GetString(Resource(program, 10, StandaloneProgram.RuntimeVersionResource)!));
        Assert.Equal("k6 custom help, first line\n  second line, indented\n", Encoding.UTF8.GetString(Resource(program, 10, StandaloneProgram.HelpResource)!));
    }

    [Fact]
    public void AnIconReplacesTheLaunchersOwnWithAGroupThatDescribesIt()
    {
        // R2025b adds a second icon group whose entries read 13 by 0 pixels (k6_pe); JGraph's one group names each image.
        IconImage[] images = [.. IconFile.BuildSizes.Select(Png)];
        StandaloneProgram.Write(Build(), Options() with { IconImages = images });
        string program = Path.Combine(_root, "out", "k6p.exe");
        byte[] group = Resource(program, 14, 1)!;
        Assert.Equal(6, BitConverter.ToUInt16(group, 4));
        for (int i = 0; i < images.Length; i++)
        {
            int at = 6 + 14 * i;
            int size = IconFile.BuildSizes[i];
            Assert.Equal(size >= 256 ? 0 : size, group[at]);
            Assert.Equal(size >= 256 ? 0 : size, group[at + 1]);
            Assert.Equal(32, BitConverter.ToUInt16(group, at + 6));
            Assert.Equal(images[i].Data.Length, (int)BitConverter.ToUInt32(group, at + 8));
            Assert.Equal(images[i].Data, Resource(program, 3, BitConverter.ToUInt16(group, at + 12)));
        }
    }

    [Fact]
    public void AWindowedProgramIsBuiltFromTheWindowedLauncher()
    {
        StandaloneProgram.Write(Build(target: CtfTargetType.WindowsStandaloneApplication), Options() with { Windowed = true });
        Assert.Equal(2, Subsystem(Path.Combine(_root, "out", "k6p.exe")));
    }

    [Fact]
    public void ASeparateArchiveGoesBesideTheProgram()
    {
        // mcc -C: <name>.ctf beside a program that carries none, listed last (k6_build: api_m_separate).
        (IReadOnlyList<string> files, _) = StandaloneProgram.Write(Build("k6_sep"), Options("k6_sep") with { EmbedArchive = false });
        string folder = Path.Combine(_root, "out");
        Assert.Equal([Path.Combine(folder, "k6_sep.exe"), Path.Combine(folder, "readme.txt"), Path.Combine(folder, "k6_sep.ctf")], files);
        Assert.Throws<InvalidDataException>(() => CtfArchiveReader.Open(files[0]).Dispose());
        using CtfArchiveReader archive = CtfArchiveReader.Open(files[2]);
        Assert.Equal("k6_sep", archive.Manifest.ComponentName);
        Assert.Contains("-k6_sep.ctf (component technology file)\r\n-k6_sep.exe\r\n", File.ReadAllText(Path.Combine(folder, "readme.txt")));
    }

    [Fact]
    public void TheReadmeHasR2025bsTitleAndSections()
    {
        // k6_build: readme.m_plain - the title, three numbered sections, CR LF throughout, the list's lines.
        string text = StandaloneProgram.Readme(Options("k6_hello"));
        Assert.StartsWith("k6_hello Executable\r\n\r\n1. Prerequisites for Deployment \r\n", text);
        Assert.Contains("\r\n2. Files to Deploy and Package\r\n\r\nFiles to Package for Standalone \r\n================================\r\n-k6_hello.exe\r\n", text);
        Assert.Contains("\r\n-This readme file \r\n", text);
        Assert.Contains("\r\n3. Definitions\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIconFileRoundTrips()
    {
        IconImage[] images = [Png(256), Png(16)];
        string file = Path.Combine(_root, "icon.ico");
        File.WriteAllBytes(file, IconFile.Write(images));
        IReadOnlyList<IconImage> read = IconFile.Read(file);
        Assert.Equal([256, 16], read.Select(i => i.Width));
        Assert.Equal(images[0].Data, read[0].Data);
        Assert.Equal(0, File.ReadAllBytes(file)[6]); // a 256-pixel image's size is written as 0 (R2025b's makeico writes 255)
        File.WriteAllText(file, "not an icon");
        Assert.Throws<InvalidDataException>(() => IconFile.Read(file));
    }

    /// <summary>A PNG file's header and an empty body: enough for an icon's directory.</summary>
    private static IconImage Png(int size)
    {
        var png = new byte[33 + size];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
        png[18] = (byte)(size >> 8);
        png[19] = (byte)size;
        png[22] = (byte)(size >> 8);
        png[23] = (byte)size;
        return IconImage.FromPng(png);
    }

    /// <summary>The PE subsystem a program declares: 2 for a windowed one, 3 for a console one.</summary>
    private static int Subsystem(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        return BitConverter.ToUInt16(bytes, pe + 24 + 68);
    }

    private static byte[]? Resource(string file, int type, object name)
    {
        IntPtr module = LoadLibraryExW(file, IntPtr.Zero, 0x2 | 0x20);
        Assert.NotEqual(IntPtr.Zero, module);
        try
        {
            IntPtr found = name is string text ? FindResourceW(module, text, type) : FindResourceW(module, (nint)Convert.ToInt32(name), type);
            if (found == IntPtr.Zero)
            {
                return null;
            }

            IntPtr bytes = LockResource(LoadResource(module, found));
            var copy = new byte[SizeofResource(module, found)];
            Marshal.Copy(bytes, copy, 0, copy.Length);
            return copy;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResourceW(IntPtr module, string name, nint type);

    [DllImport("kernel32.dll")]
    private static extern IntPtr FindResourceW(IntPtr module, nint name, nint type);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr loaded);

    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
}
