using JGraph.Deploy;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// JGraph's P-code file and <c>mcc -j</c>'s build (compiler and SDK plan, stage K3b, ADR 0259): the
/// format's header and determinism, a P-file parsing as its source does with every position 0, the
/// refusals of damaged files and MATLAB's own, and the build that stores an archive's code files as
/// P-files. What a P-file does once on the path is the fixture <c>k3b_pcode_secrets</c>'s.
/// </summary>
public sealed class JgsPcodeTests : IDisposable
{
    private const string Source = "function [a, b] = f(x)\n% help\na = x + 1; % comment\nb = 'two';\nend\n";

    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k3b-" + Guid.NewGuid().ToString("N"))).FullName;

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

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Put(string name, string text)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Compile_StartsWithJGraphsHeader_AndGivesTheSameBytesTwice()
    {
        byte[] first = JgsPcode.Compile(Source, "f.m");
        Assert.Equal("jgraph-pcode-01\n", System.Text.Encoding.ASCII.GetString(first, 0, 16));
        Assert.Equal(first, JgsPcode.Compile(Source, "f.m"));
        Assert.DoesNotContain("comment", System.Text.Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ParsesAsTheSourceDoes_WithEveryPositionZero()
    {
        string path = Write("f.p", JgsPcode.Compile(Source, "f.m"));
        IReadOnlyList<Stmt> program = JgsPcode.Load(path);
        FnStmt function = Assert.IsType<FnStmt>(Assert.Single(program));
        Assert.Equal("f", function.Name);
        Assert.Equal(0, function.Line);
        Assert.All(function.Body, s => Assert.Equal((0, 0), (s.Line, s.Column)));
        Assert.Equal(path, function.SourceId);
        Assert.True(AstEquals.StatementsEqual(Assert.Single(Parser.Parse(Source, "f.m", JgsDialect.Matlab)), function));
    }

    [Fact]
    public void Compile_RaisesTheSourcesSyntaxError_AndMakesNothing()
    {
        var error = Assert.Throws<JgsSyntaxException>(() => JgsPcode.Compile("function g\nx = (1;\nend\n", "g.m"));
        Assert.Equal(2, error.Line);
    }

    [Fact]
    public void Load_RefusesAChangedByte_AShortFile_AndAnotherFile_InR2025bsWords()
    {
        byte[] good = JgsPcode.Compile(Source, "f.m");
        byte[] changed = (byte[])good.Clone();
        changed[^40] ^= 1;
        var corrupt = Assert.Throws<JgsRuntimeException>(() => JgsPcode.Load(Write("c.p", changed)));
        Assert.Equal(("MATLAB:corruptPfile_3", "Unable to run P-code file. The file is damaged."), (corrupt.Identifier, corrupt.Message));

        string truncated = Write("t.p", good[..20]);
        var shortFile = Assert.Throws<JgsRuntimeException>(() => JgsPcode.Load(truncated));
        Assert.Equal(("MATLAB:shortPfileFormat", $"Unable to run P-code file '{truncated}'. The file is damaged."), (shortFile.Identifier, shortFile.Message));

        string junk = Write("j.p", "not a pfile at all"u8.ToArray());
        Assert.Equal("MATLAB:shortPfileFormat", Assert.Throws<JgsRuntimeException>(() => JgsPcode.Load(junk)).Identifier);
    }

    [Fact]
    public void Load_RefusesAP_FileMatlabWrote_ByName()
    {
        // R2025b's header (probe_k3b_pcode), then bytes JGraph cannot read.
        string path = Write("m.p", [.. "v01.00v00.00\0\n"u8.ToArray(), .. new byte[64]]);
        var error = Assert.Throws<JgsRuntimeException>(() => JgsPcode.Load(path));
        Assert.Equal("JGraph:pcode:MatlabPfile", error.Identifier);
        Assert.Contains("written by MATLAB's pcode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PcodeBuild_StoresEachCodeFileAsItsP_File_ButAFileOfComments()
    {
        string main = Put("app/app.m", "function app\nhelper;\nend\n");
        string helper = Put("app/helper.m", "function helper\ndisp(1)\nend\n");
        string contents = Put("app/Contents.m", "% Contents of app\n%   helper - a helper\n");
        string data = Put("app/data.txt", "data");
        var report = new DependencyReport(
            [
                new DependencyFile(main, DependencyReason.Root, null),
                new DependencyFile(helper, DependencyReason.Code, main),
                new DependencyFile(contents, DependencyReason.Code, main),
                new DependencyFile(data, DependencyReason.Data, main),
            ],
            [], [], []);
        CtfBuild built = JgsPcode.PcodeBuild(CtfLayout.FromReport("app", main, report));

        Assert.Equal(CtfProtection.PCode, built.Protection);
        Assert.Equal(["app/Contents.m", "app/data.txt"], built.Files.Select(f => f.ArchivePath).Order(StringComparer.Ordinal));
        Assert.Equal(["app/app.p", "app/helper.p"], built.MadeFiles.Select(f => f.ArchivePath).Order(StringComparer.Ordinal));
        Assert.Equal("app/app.p", Assert.Single(built.PublicFunctions).File);

        string archive = Path.Combine(_root, "app.ctf");
        CtfManifest manifest = CtfArchiveWriter.Write(archive, built);
        using CtfArchiveReader reader = CtfArchiveReader.Open(archive);
        Assert.Equal(CtfProtection.PCode, reader.Manifest.Protection);
        Assert.Equal(manifest.Files.Select(f => f.Path), reader.Manifest.Files.Select(f => f.Path));
        byte[] pfile = reader.ReadFile(reader.Manifest.Files.Single(f => f.Path == "app/helper.p"));
        Assert.Equal(JgsPcode.Compile(File.ReadAllText(helper), helper), pfile);
    }
}
