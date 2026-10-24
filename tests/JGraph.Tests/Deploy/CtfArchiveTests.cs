using System.IO.Compression;
using System.Text;
using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The archive (compiler and SDK plan, stage K3): the manifest, the package's layout against what
/// R2025b's archives hold (the research session's <c>ctf_hello_exe</c> and K2's <c>k2_mcc.out.txt</c>),
/// reading a <c>.ctf</c> and a program carrying one, refusing MathWorks' archives and damaged ones,
/// and <c>extractCTF</c>'s messages as R2025b's utility prints them.
/// </summary>
public sealed class CtfArchiveTests : IDisposable
{
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k3a-" + Guid.NewGuid().ToString("N"))).FullName;

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

    private string Put(string relative, string text)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>An archive of a main, a helper beside it, a private file and a library file elsewhere.</summary>
    private (string Archive, CtfManifest Manifest) Build(string name = "hello")
    {
        string main = Put("app/hello.m", "function hello(varargin)\ndisp(helper(1))\nend\n");
        string helper = Put("app/helper.m", "function r = helper(a)\nr = a + 1;\nend\n");
        string priv = Put("app/private/inner.m", "function inner\nend\n");
        string lib = Put("lib/libfn.m", "function libfn\nend\n");
        string data = Put("app/data.txt", "payload");
        var report = new DependencyReport(
            [
                new DependencyFile(main, DependencyReason.Root, null),
                new DependencyFile(data, DependencyReason.Data, main),
                new DependencyFile(helper, DependencyReason.Code, main),
                new DependencyFile(priv, DependencyReason.Code, helper),
                new DependencyFile(lib, DependencyReason.Code, main),
            ],
            [], [], []);
        CtfBuild build = CtfLayout.FromReport(name, main, report, _ => (["varargin"], []));
        string archive = Path.Combine(_root, name + ".ctf");
        return (archive, CtfArchiveWriter.Write(archive, build));
    }

    private static string NoDrive(string path)
    {
        string full = Path.GetFullPath(path);
        return full[Path.GetPathRoot(full)!.Length..].Replace('\\', '/');
    }

    [Fact]
    public void Layout_PutsTheMainFolderUnderTheComponentAndOtherFilesUnderTheirPathWithoutTheDrive()
    {
        string app = Path.Combine(_root, "app");
        Assert.Equal("hello/hello.m", CtfLayout.ArchivePath(Path.Combine(app, "hello.m"), app, "hello"));
        Assert.Equal("hello/private/inner.m", CtfLayout.ArchivePath(Path.Combine(app, "private", "inner.m"), app, "hello"));
        Assert.Equal(NoDrive(Path.Combine(_root, "lib", "libfn.m")), CtfLayout.ArchivePath(Path.Combine(_root, "lib", "libfn.m"), app, "hello"));

        // A folder whose name merely starts with the main folder's is not under it.
        Assert.Equal(NoDrive(Path.Combine(_root, "app2", "x.m")), CtfLayout.ArchivePath(Path.Combine(_root, "app2", "x.m"), app, "hello"));
    }

    [Fact]
    public void FromReport_PutsTheMainFolderFirstOnThePath_ThenEachOtherFolder_NeverAPrivateOne()
    {
        (_, CtfManifest manifest) = Build();
        Assert.Equal(["hello", NoDrive(Path.Combine(_root, "lib"))], manifest.MatlabPath);
        Assert.Equal("hello", manifest.MainFunction);
        CtfFunction main = Assert.Single(manifest.PublicFunctions);
        Assert.Equal(("hello", "hello/hello.m"), (main.Name, main.File));
        Assert.Equal(["varargin"], main.Inputs);
        Assert.Empty(main.Outputs);
    }

    [Fact]
    public void Package_HoldsR2025bsParts_AndTheFilesUnderTheFileRoot_DatedAsR2025bDatesThem()
    {
        (string archive, CtfManifest manifest) = Build();
        using ZipArchive zip = ZipFile.OpenRead(archive);
        string[] names = [.. zip.Entries.Select(e => e.FullName)];
        Assert.Equal("[Content_Types].xml", names[0]);
        Assert.Equal("_rels/.rels", names[1]);
        Assert.Equal(".META/manifest.xml", names[2]);
        Assert.Contains("fsroot/hello/hello.m", names);
        Assert.Contains("fsroot/hello/data.txt", names);
        Assert.Contains("fsroot/hello/private/inner.m", names);
        Assert.Contains("fsroot/" + NoDrive(Path.Combine(_root, "lib", "libfn.m")), names);
        foreach (string part in new[] { "coreProperties", "filesystemManifest", "mwcoreProperties", "mwcorePropertiesExtension", "mwcorePropertiesReleaseInfo" })
        {
            Assert.Contains($"metadata/{part}.xml", names);
        }

        Assert.DoesNotContain(names, n => n.StartsWith("_xmlsignatures/", StringComparison.Ordinal));
        Assert.All(zip.Entries, e => Assert.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime));

        string core = new StreamReader(zip.GetEntry("metadata/mwcoreProperties.xml")!.Open()).ReadToEnd();
        Assert.Contains("<contentType>application/vnd.jgraph.compiler</contentType>", core);
        Assert.Contains("<contentTypeFriendlyName>JGraph Compiler Archive</contentTypeFriendlyName>", core);
        string extension = new StreamReader(zip.GetEntry("metadata/mwcorePropertiesExtension.xml")!.Open()).ReadToEnd();
        Assert.Contains($"<uuid>{manifest.Uuid}</uuid>", extension);
        Assert.Matches("^hello_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", manifest.Uuid);
    }

    [Fact]
    public void Manifest_ReadsBackAsItWasWritten_InR2025bsElementStructure()
    {
        var manifest = new CtfManifest("calc", "calc_0b0e3a1c-0000-4000-8000-000000000001", CtfTargetType.WindowsStandaloneApplication,
            ["calc", "Users/x/lib", "Users_x/lib"],
            [new CtfFunction("calc", "calc/calc.m", ["a", "varargin"], ["r", "varargout"])],
            [new CtfFileEntry("calc/calc.m", 12, new string('a', 64))])
        {
            MainFunction = "calc",
            NumericArguments = true,
            RuntimeOptions = ["-nojvm"],
        };
        byte[] xml = manifest.ToXml();
        string text = Encoding.UTF8.GetString(xml);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\" ?>\n<manifest ", text);
        Assert.Contains("target_type=\"2\"", text);
        Assert.Contains("mcr-major-version=\"25\" mcr-minor-version=\"2\" mcr-release-version=\"0\"", text);
        Assert.Contains("use_native_matlab_types=\"1\"", text);
        Assert.Contains("<directory id=\"DIR_calc\" path=\"calc\" />", text);

        // Two folders whose ids would collide are told apart.
        Assert.Contains("<directory id=\"DIR_Users_x_lib\" path=\"Users/x/lib\" />", text);
        Assert.Contains("<directory id=\"DIR_Users_x_lib_2\" path=\"Users_x/lib\" />", text);
        Assert.Contains("<warning-state>", text);
        Assert.Contains("<option>off:MATLAB:dispatcher:nameConflict</option>", text);
        Assert.Contains("<main-function ref=\"FUNCTION_calc\" />", text);

        CtfManifest back = CtfManifest.Parse(new MemoryStream(xml));
        Assert.Equal(manifest.ComponentName, back.ComponentName);
        Assert.Equal(manifest.Uuid, back.Uuid);
        Assert.Equal(manifest.TargetType, back.TargetType);
        Assert.Equal(manifest.MatlabPath, back.MatlabPath);
        Assert.Equal("calc", back.MainFunction);
        Assert.True(back.NumericArguments);
        Assert.Equal(["-nojvm"], back.RuntimeOptions);
        Assert.Equal([CtfManifest.DefaultWarningState], back.WarningState);
        CtfFunction function = Assert.Single(back.PublicFunctions);
        Assert.Equal(["a", "varargin"], function.Inputs);
        Assert.Equal(["r", "varargout"], function.Outputs);
        Assert.Equal(manifest.Files, back.Files);
        Assert.Equal(CtfProtection.Open, back.Protection);
    }

    [Fact]
    public void Reader_ExtractsEveryFile_AndThePreferencesFolder()
    {
        (string archive, CtfManifest written) = Build();
        using CtfArchiveReader reader = CtfArchiveReader.Open(archive);
        Assert.Equal(written.Uuid, reader.Manifest.Uuid);
        string folder = Path.Combine(_root, "out");
        var extracted = reader.ExtractTo(folder);
        Assert.Equal(written.Files.Count, extracted.Count);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(folder, "hello", "data.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "hello", "private", "inner.m")));
        Assert.True(Directory.Exists(Path.Combine(folder, ".matlab")));
    }

    [Fact]
    public void Reader_FindsTheArchiveAfterAProgramsBytes_AsR2025bsStandaloneLayoutHasIt()
    {
        (string archive, _) = Build();
        string program = Path.Combine(_root, "hello.exe");
        byte[] launcher = new byte[123_457];
        new Random(5).NextBytes(launcher);
        File.WriteAllBytes(program, [.. launcher, .. File.ReadAllBytes(archive)]);
        using CtfArchiveReader reader = CtfArchiveReader.Open(program);
        Assert.Equal("hello", reader.Manifest.ComponentName);
        reader.ExtractTo(Path.Combine(_root, "fromexe"));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(_root, "fromexe", "hello", "data.txt")));
    }

    [Fact]
    public void Reader_RefusesAMathWorksArchive_AndAFileWithNoArchive()
    {
        string mathworks = Path.Combine(_root, "theirs.ctf");
        using (ZipArchive zip = ZipFile.Open(mathworks, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("metadata/mwcoreProperties.xml").Open()))
            {
                w.Write("<mwcoreProperties><contentType>application/vnd.mathworks.matlab.compiler</contentType></mwcoreProperties>");
            }

            zip.CreateEntry(".META/manifest.xml");
        }

        var refused = Assert.Throws<InvalidDataException>(() => CtfArchiveReader.Open(mathworks));
        Assert.Contains("built by MATLAB Compiler", refused.Message);

        string text = Put("plain.txt", "not an archive");
        Assert.Throws<InvalidDataException>(() => CtfArchiveReader.Open(text));
    }

    [Fact]
    public void Reader_RefusesAFileThatIsNotWhatTheManifestSays()
    {
        (string archive, _) = Build();
        string damaged = Path.Combine(_root, "damaged.ctf");
        File.Copy(archive, damaged);
        using (ZipArchive zip = ZipFile.Open(damaged, ZipArchiveMode.Update))
        {
            zip.GetEntry("fsroot/hello/data.txt")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("fsroot/hello/data.txt").Open());
            w.Write("changed");
        }

        using CtfArchiveReader reader = CtfArchiveReader.Open(damaged);
        var refused = Assert.Throws<InvalidDataException>(() => reader.ExtractTo(Path.Combine(_root, "bad")));
        Assert.Contains("'hello/data.txt' is not the file it was built with", refused.Message);
    }

    [Fact]
    public void Writer_RefusesAPlaceOutsideTheFileRoot_AndTwoFilesInOnePlace()
    {
        string file = Put("a.m", "x = 1;");
        Assert.Throws<ArgumentException>(() => CtfArchiveWriter.Write(new MemoryStream(),
            new CtfBuild { ComponentName = "a", Files = [(file, "../a.m")] }));
        Assert.Throws<ArgumentException>(() => CtfArchiveWriter.Write(new MemoryStream(),
            new CtfBuild { ComponentName = "a", Files = [(file, "C:/a.m")] }));
        Assert.Throws<ArgumentException>(() => CtfArchiveWriter.Write(new MemoryStream(),
            new CtfBuild { ComponentName = "a", Files = [(file, "a/a.m"), (file, "a/a.m")] }));
    }

    [Fact]
    public void ExtractCtf_PrintsR2025bsMessages_AndExitCodes()
    {
        (string archive, _) = Build("jgprobe");
        string here = Path.GetDirectoryName(archive)!;

        (int code, string output, string error) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            int code = ExtractCtfCommand.Run(args, here, output, error, "extractCTF");
            return (code, output.ToString(), error.ToString());
        }

        (int code, string output, string error) = Run();
        Assert.Equal(-1, code);
        Assert.Equal("extractCTF takes one mandatory input argument: the name of the CTF file to be extracted and one optional "
            + "input argument: parent directory for the extracted files." + Environment.NewLine, output);
        Assert.Equal("No CTF file specified at the input. " + Environment.NewLine, error);

        (code, _, error) = Run("jgprobe.ctf");
        Assert.Equal(0, code);
        Assert.Equal("jgprobe.ctf CTF file extracted in the current working directory" + Environment.NewLine, error);
        Assert.True(File.Exists(Path.Combine(here, "jgprobe_mcr", "jgprobe", "hello.m")));

        // A folder given takes the files straight in; arguments past it are ignored.
        (code, _, error) = Run("jgprobe.ctf", "outdir", "ignored");
        Assert.Equal(0, code);
        Assert.Equal("jgprobe.ctf CTF file extracted in outdir" + Environment.NewLine, error);
        Assert.True(File.Exists(Path.Combine(here, "outdir", "jgprobe", "hello.m")));

        // No extension is added.
        (code, _, error) = Run("jgprobe");
        Assert.Equal(-1, code);
        Assert.Equal("Could not open the CTF file jgprobe" + Environment.NewLine, error);
    }
}
