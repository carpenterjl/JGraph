using System.Diagnostics;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Native;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// A C shared library built by JGraph's mcc (compiler and SDK plan, stage K11) and called as R2025b's
/// is: the probe's client (<c>tools/matlab-checklist/compiler-probes/src/k11client</c>) compiled with
/// MSVC against the library's own generated header and import library, run in a process of its own
/// with jgrt.dll and the engine on its PATH, against R2025b's run of the same client against the
/// library R2025b's mcc built (<c>recordings/k11_client.out.txt</c>). <c>run-k11.ps1</c> runs the
/// whole probe; these are its landmarks.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CSharedLibraryBuildTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private static readonly string Probes = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes");
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k11-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The probe's main build, in its command line's order.</summary>
    private static readonly string[] MainFiles =
    [
        "k11plain.m", "k11Mixed.m", "K11upper.m", "k11_under.m", "k11long.m", "k11wide.m",
        "k11_a_function_name_long_enough_to_make_generated_lines_wrap.m", "k11sub.m", "k11noparen.m", "k11outonly.m",
        "k11varin.m", "k11quiet.m", "k11fail.m",
    ];

    public CSharedLibraryBuildTests()
    {
        JG.Reset();
        Directory.CreateDirectory(Src);
        foreach (string file in Directory.GetFiles(Path.Combine(Probes, "src", "k11"), "*.m"))
        {
            File.Copy(file, Path.Combine(Src, Path.GetFileName(file)));
        }
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A library the client's process still held; the temp folder clears it.
        }
    }

    private string Src => Path.Combine(_root, "src");

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    private async Task<ScriptRunResult> Run(string code)
    {
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        return await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
    }

    [MsvcFact]
    public async Task AClientCallsJGraphsLibraryAsR2025bsClientCallsItsOwn()
    {
        string lib = Path.Combine(_root, "lib");
        ScriptRunResult built = await Run($"mcc('-W', 'lib:k11lib', '-T', 'link:lib', '-d', '{lib}', {string.Join(", ", MainFiles.Select(static f => $"'{f}'"))});");
        Assert.True(built.Success, built.Message + _output.ErrorText);
        Assert.Contains("Building with 'Microsoft Visual C++", _output.NormalText, StringComparison.Ordinal);
        foreach (string file in new[] { "k11lib.h", "k11lib.c", "k11lib.def", "k11lib.exports", "k11lib.dll", "k11lib.lib", "k11lib.exp", "readme.txt" })
        {
            Assert.True(File.Exists(Path.Combine(lib, file)), file + " was not written");
        }

        Assert.False(File.Exists(Path.Combine(lib, "k11lib.ctf")), "the archive belongs inside the library");

        // The client, compiled as record-k11.ps1 compiles it against R2025b's build (the separate model).
        string client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        CCompiler compiler = CCompilers.SelectedMsvc()!;
        string include = Path.Combine(AppContext.BaseDirectory, "extern", "include");
        string libraries = Path.Combine(AppContext.BaseDirectory, "extern", "lib", "win64", "microsoft");
        (int exit, string said) = CCompilers.RunTool(compiler, "cl.exe",
            $"/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /DMATLAB_DEFAULT_RELEASE=R2017b /I\"{include}\" /I\"{Path.Combine(Probes, "src", "k10client")}\" /I\"{lib}\" "
            + $"\"{Path.Combine(Probes, "src", "k11client", "k11client.c")}\" /Fe:k11client.exe /link /LIBPATH:\"{lib}\" /LIBPATH:\"{libraries}\" k11lib.lib jgrt.lib",
            client);
        Assert.True(exit == 0, said);
        File.Copy(Path.Combine(lib, "k11lib.dll"), Path.Combine(client, "k11lib.dll"));

        string[] lines = RunClient(Path.Combine(client, "k11client.exe"));
        string[] recorded = Section("handlers");
        Assert.Equal(recorded, lines);
    }

    /// <summary>A section of R2025b's client recording, its lines as the client printed them, the runner's own lines aside.</summary>
    private static string[] Section(string title)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", "k11_client.out.txt"));
        int at = Array.IndexOf(all, "== " + title);
        return
        [
            .. all.Skip(at + 1).TakeWhile(static l => !l.StartsWith("== ", StringComparison.Ordinal))
                .Select(static l => l[2..])
                .Where(static l => !l.StartsWith("STDERR\t", StringComparison.Ordinal) && !l.StartsWith("EXIT\t", StringComparison.Ordinal)
                    && !l.StartsWith("SECONDS\t", StringComparison.Ordinal)),
        ];
    }

    /// <summary>Runs the client with the test's own folder, where jgrt.dll sits beside the engine, first on its PATH.</summary>
    private string[] RunClient(string exe)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        start.Environment["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH");
        start.Environment["MCR_CACHE_ROOT"] = Path.Combine(_root, "cache");
        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("k11client did not finish in three minutes.");
        }

        Assert.True(process.ExitCode == 0, $"k11client ended with {process.ExitCode}:\n{output}\n{error.Result}");

        // PrintStackTrace and the default handlers write standard error, as R2025b's did (k11_client: STDERR lines).
        Assert.Contains("name deeper, line 7.", error.Result, StringComparison.Ordinal);
        return output.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
    }

    [MsvcFact]
    public async Task CompilerBuildAnswersR2025bsResults()
    {
        // k11_build: api_default and api_separate - the header, the library and its import library,
        // then the getting-started page; -C's archive and -g's symbols beside them, not among the files.
        string code = "r = compiler.build.cSharedLibrary('k11plain.m'); fprintf('R1|%s|%s\\n', r.BuildType, strjoin(cellfun(@(f) strrep(f, [pwd filesep], ''), r.Files', 'UniformOutput', false), ' '));"
            + " o = compiler.build.CSharedLibraryOptions('k11plain.m', 'LibraryName', 'K11Sep', 'EmbedArchive', 'off', 'DebugBuild', 'on', 'LibraryVersion', '3.1', 'OutputDir', 'sep');"
            + " r = compiler.build.cSharedLibrary(o); d = dir('sep'); fprintf('R2|%d|%s\\n', numel(r.Files), strjoin(sort({d(~[d.isdir]).name}), ' '));";
        ScriptRunResult result = await Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        string[] printed = _output.NormalText.Split('\n');
        Assert.Equal(
            "R1|cSharedLibrary|k11plaincSharedLibrary\\k11plain.h k11plaincSharedLibrary\\k11plain.dll k11plaincSharedLibrary\\k11plain.lib k11plaincSharedLibrary\\GettingStarted.html",
            printed.Single(static l => l.StartsWith("R1|", StringComparison.Ordinal)));
        Assert.Equal(
            "R2|4|GettingStarted.html K11Sep.c K11Sep.ctf K11Sep.def K11Sep.dll K11Sep.exp K11Sep.exports K11Sep.h K11Sep.lib K11Sep.pdb buildresult.json "
            + "includedSupportPackages.txt mccExcludedFiles.log readme.txt requiredMCRProducts.txt unresolvedSymbols.txt",
            printed.Single(static l => l.StartsWith("R2|", StringComparison.Ordinal)));
        Assert.Equal(2, printed.Count(static l => l.StartsWith("Building with 'Microsoft Visual C++", StringComparison.Ordinal)));

        // The version block names the version and a DLL (k11_pe: api_named's 3.1.0.0).
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(Path.Combine(Src, "sep", "K11Sep.dll"));
        Assert.Equal("3.1.0.0", version.FileVersion);
    }

    [Fact]
    public async Task WithNoCompilerALinkedBuildIsRefusedAndCodegenStillWrites()
    {
        // k11_nocompiler: R2025b's mbuild finds no compiler and its mcc fails with COM_SYSTEM_ERROR;
        // JGraph says why in its own words, before writing anything. -T codegen needs no compiler.
        CCompilers.SubstituteForTests([]);
        try
        {
            string code = "try, mcc('-W', 'lib:k11nc', '-T', 'link:lib', '-d', 'nc', 'k11plain.m'); catch e, fprintf('%s|', e.identifier); end;"
                + " fprintf('%d|', exist('nc', 'dir') && numel(dir('nc')) > 2);"
                + " mcc('-W', 'lib:k11cg', '-T', 'codegen', '-d', 'cg', 'k11plain.m'); d = dir('cg'); fprintf('%s', strjoin(sort({d(~[d.isdir]).name}), ' '));";
            ScriptRunResult result = await Run(code);
            Assert.True(result.Success, result.Message + _output.ErrorText);
            Assert.Equal(
                "Compiler:compiler:COM_SYSTEM_ERROR|0|buildresult.json includedSupportPackages.txt k11cg.c k11cg.ctf k11cg.exports k11cg.h "
                + "mccExcludedFiles.log readme.txt requiredMCRProducts.txt unresolvedSymbols.txt",
                _output.NormalText.Trim());
        }
        finally
        {
            CCompilers.SubstituteForTests(null);
        }
    }

    /// <summary>A fact that is skipped, saying why, where no MSVC is found to build a C shared library with.</summary>
    private sealed class MsvcFactAttribute : FactAttribute
    {
        public MsvcFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || CCompilers.SelectedMsvc() is null)
            {
                Skip = "No Microsoft Visual C++ was found: mcc -W lib compiles a C shared library with it (ADR 0267).";
            }
        }
    }
}
