using System.Diagnostics;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Native;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// A C++ shared library built by JGraph's mcc (compiler and SDK plan, stage K12) and called as R2025b's
/// is: the probe's client (<c>tools/matlab-checklist/compiler-probes/src/k12client</c>) compiled with
/// MSVC against the JGraph Runtime's headers and the strongly typed header JGraph generated, linked
/// with jgrt.lib, and run in a process of its own with jgrt.dll and the engine on its PATH, against
/// R2025b's run of the same client against the libraries R2025b built
/// (<c>recordings/k12_client.out.txt</c>). <c>run-k12.ps1</c> runs the whole probe; these are its
/// landmarks, the cases ADR 0268 records as divergences left out.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CppSharedLibraryBuildTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private static readonly string Probes = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes");
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k12-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The probe's main build, in its command line's order.</summary>
    private static readonly string[] MainFiles =
    [
        "k9describe.m", "k9make.m", "k9echo.m", "k9multi.m", "k9nv.m", "k9print.m", "k9noout.m", "k9slow.m", "k9err.m", "k9quiet.m",
        "k9t_real.m", "k9t_cplx.m", "k9t_ints.m", "k9t_misc.m", "k9t_untyped.m", "k9t_opt.m", "k9t_ref.m", "k9t_keyword.m", "k9t_multi.m",
        "k9t_none.m", "k9t_objects.m", "k9point.m", "k9counter.m", "k9color.m", "k9opt.m", "+k9pkg/k9inner.m",
    ];

    /// <summary>The typed cases ADR 0268 records: the corrected name-value call, complex integers and the package function.</summary>
    private static readonly HashSet<string> TypedDivergences = new(StringComparer.Ordinal) { "t.nv", "t.nv.complex", "t.ints.complex", "t.inner" };

    public CppSharedLibraryBuildTests()
    {
        JG.Reset();
        string k9 = Path.Combine(Probes, "src", "k9");
        foreach (string file in Directory.GetFiles(k9, "*.m", SearchOption.AllDirectories))
        {
            string target = Path.Combine(Src, Path.GetRelativePath(k9, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
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
            // An archive the client's process still held; the temp folder clears it.
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
        string variadic = Path.Combine(_root, "libv");
        ScriptRunResult built = await Run(
            $"mcc('-W', 'cpplib:k12lib,generic', '-T', 'link:lib', '-d', '{lib}', {string.Join(", ", MainFiles.Select(static f => $"'{f}'"))});"
            + $" mcc('-W', 'cpplib:k12v,generic', '-T', 'link:lib', '-d', '{variadic}', 'k9nargs.m', 'k9nout.m', 'k9describe.m');");
        Assert.True(built.Success, built.Message + _output.ErrorText);
        string generic = Path.Combine(lib, "v2", "generic_interface");
        foreach (string file in new[] { "k12lib.ctf", "k12libv2.hpp", "readme.txt" })
        {
            Assert.True(File.Exists(Path.Combine(generic, file)), file + " was not written");
        }

        // The client, unchanged from R2025b's build of it and compiled as run-k12.ps1 compiles it,
        // without optimizing to keep the test short.
        string client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        CCompiler compiler = CCompilers.SelectedMsvc()!;
        string include = Path.Combine(AppContext.BaseDirectory, "extern", "include");
        string jgrt = Path.Combine(AppContext.BaseDirectory, "extern", "lib", "win64", "microsoft", "jgrt.lib");
        (int exit, string said) = CCompilers.RunTool(compiler, "cl.exe",
            $"/nologo /Od /MD /EHsc /std:c++17 /utf-8 /W3 /Zc:__cplusplus /bigobj /D_CRT_SECURE_NO_DEPRECATE /I\"{include}\" /I\"{generic}\" "
            + $"\"{Path.Combine(Probes, "src", "k12client", "k12client.cpp")}\" /Fe:k12client.exe /link \"{jgrt}\"",
            client);
        Assert.True(exit == 0, said);
        File.Copy(Path.Combine(generic, "k12lib.ctf"), Path.Combine(client, "k12lib.ctf"));
        File.Copy(Path.Combine(variadic, "v2", "generic_interface", "k12v.ctf"), Path.Combine(client, "k12v.ctf"));

        // The Data API in the client's own memory: every case but the one ADR 0268 records.
        string exe = Path.Combine(client, "k12client.exe");
        Assert.Equal(Cases("local").Where(static c => c.Label != "cell.nested.index"), RunClient(exe, "local").Where(static c => c.Label != "cell.nested.index"));

        // The strongly typed interface over JGraph's runtime: functions, classes' properties and methods, enumerations.
        Assert.Equal(Cases("typed").Where(static c => !TypedDivergences.Contains(c.Label)), RunClient(exe, "typed").Where(static c => !TypedDivergences.Contains(c.Label)));
    }

    /// <summary>A mode's cases as R2025b's client printed them: each label and what followed its tab.</summary>
    private static IEnumerable<(string Label, string Value)> Cases(string mode)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", "k12_client.out.txt"));
        int at = Array.IndexOf(all, "== " + mode);
        return Labelled(all.Skip(at + 1).TakeWhile(static l => !l.StartsWith("== ", StringComparison.Ordinal)).Select(static l => l[2..]));
    }

    private static List<(string Label, string Value)> Labelled(IEnumerable<string> lines) =>
    [
        .. lines.Select(static l => l.Split('\t', 2))
            .Where(static p => p.Length == 2 && p[0] is not ("STDERR" or "EXIT" or "SECONDS"))
            .Select(static p => (p[0], p[1])),
    ];

    /// <summary>Runs the client with the test's own folder, where jgrt.dll sits beside the engine, first on its PATH.</summary>
    private List<(string Label, string Value)> RunClient(string exe, string mode)
    {
        var start = new ProcessStartInfo(exe, mode)
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
            Assert.Fail($"k12client {mode} did not finish in three minutes.");
        }

        Assert.True(process.ExitCode == 0, $"k12client {mode} ended with {process.ExitCode}:\n{output}\n{error.Result}");
        return Labelled(output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
    }

    /// <summary>A fact that is skipped, saying why, where no MSVC is found to compile the client with.</summary>
    private sealed class MsvcFactAttribute : FactAttribute
    {
        public MsvcFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || CCompilers.SelectedMsvc() is null)
            {
                Skip = "No Microsoft Visual C++ was found to compile the C++ client with (ADR 0268).";
            }
        }
    }
}
