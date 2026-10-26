using System.Diagnostics;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// A Python package built by JGraph's compiler.build.pythonPackage (compiler and SDK plan, stage K13)
/// and called as R2025b's is: the probe's client
/// (<c>tools/matlab-checklist/compiler-probes/src/k13client/k13client.py</c>), unchanged, run by
/// CPython in a process of its own with the package on its PYTHONPATH and JGRAPH_RUNTIME_ROOT naming
/// the test's own folder, where jgrt.dll, the engine and the runtime's python folder are, against
/// R2025b's run of the same client against the package R2025b built
/// (<c>recordings/k13_client.out.txt</c>). <c>run-k13.ps1</c> runs the whole probe; these are its
/// array, call, conversion and extra modes, the cases ADR 0269 records as divergences left out.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PythonPackageBuildTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private static readonly string Probes = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes");
    private static readonly string? Interpreter = PythonLocator.Find()?.Executable;
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k13-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>probe_k13_build's main build, in its command line's order.</summary>
    private static readonly string[] MainFiles =
    [
        "k9describe.m", "k9make.m", "k9echo.m", "k9multi.m", "k9nv.m", "k9print.m", "k9noout.m", "k9nout.m", "k9nargs.m",
        "k9slow.m", "k9err.m", "k9quiet.m", "k9point.m", "k9counter.m", "k9color.m", "k13state.m",
    ];

    /// <summary>The cases ADR 0269 records, by mode (compare-k13.py's KNOWN_CLIENT).</summary>
    private static readonly Dictionary<string, HashSet<string>> Divergences = new(StringComparer.Ordinal)
    {
        ["arrays"] = ["hash"],
        ["calls"] = ["print.capture", "print.stdout", "error.builtin", "error.undefined"],
        ["convert"] =
        [
            "out.int64", "out.uint64", "out.complexint8", "out.nestedcell", "out.sparsecomplex", "in.int.2^53+1", "in.int.2^62",
            "in.int.max", "in.int.min", "in.complex.zero", "in.range", "in.datetime", "in.m.complex.realvalues", "in.m.int64",
            "in.m.uint64", "in.m.int8.complex", "echo.int.2^62", "echo.m.uint64",
        ],
        ["extra"] = ["dt.tz.in", "dt.offset.in", "dt.micro.in", "dt.delta.echo", "dt.list.in", "in.range.list"],
    };

    public PythonPackageBuildTests()
    {
        JG.Reset();
        foreach (string folder in new[] { "k9", "k13" })
        {
            string from = Path.Combine(Probes, "src", folder);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Src, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
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

    [PythonFact]
    public async Task AClientCallsJGraphsPackageAsR2025bsClientCallsItsOwn()
    {
        string package = Path.Combine(_root, "py", "k13pkg");
        ScriptRunResult built = await Run(
            $"r = compiler.build.pythonPackage({{{string.Join(", ", MainFiles.Select(static f => $"'{f}'"))}}}, 'PackageName', 'k13pkg', 'OutputDir', '{package}');");
        Assert.True(built.Success, built.Message + _output.ErrorText);
        foreach (string file in new[] { @"k13pkg\__init__.py", @"k13pkg\k13pkg.ctf", "setup.py", "pyproject.toml", "GettingStarted.html" })
        {
            Assert.True(File.Exists(Path.Combine(package, file)), file + " was not written");
        }

        foreach (string mode in new[] { "arrays", "calls", "convert", "extra" })
        {
            HashSet<string> recorded = Divergences[mode];
            Assert.Equal(
                Cases(mode).Where(c => !recorded.Contains(c.Label)),
                RunClient(package, mode).Where(c => !recorded.Contains(c.Label)));
        }
    }

    /// <summary>A mode's cases as R2025b's client printed them: each label and what followed its tab, the cache folder read as one.</summary>
    private static IEnumerable<(string Label, string Value)> Cases(string mode)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", "k13_client.out.txt"));
        int at = Array.IndexOf(all, "== " + mode);
        return Labelled(all.Skip(at + 1).TakeWhile(static l => !l.StartsWith("== ", StringComparison.Ordinal)).Select(static l => l[2..]));
    }

    private static List<(string Label, string Value)> Labelled(IEnumerable<string> lines) =>
    [
        .. lines.Select(static l => l.Split('\t', 2))
            .Where(static p => p.Length == 2 && p[0] is not ("STDERR" or "EXIT" or "SECONDS" or "TIMES"))
            .Select(static p => (p[0], SameCache(p[1]))),
    ];

    /// <summary>The extracted archive's folder read as one: R2025b's names its release, JGraph's its version.</summary>
    private static string SameCache(string text) =>
        Regex.Replace(text, @"cache(\\\\|\\)(R2025b|\d+\.\d+\.\d+)(\\\\|\\)", "cache$1<V>$3");

    /// <summary>Runs the client in <paramref name="mode"/> on the package in <paramref name="package"/>, the test's folder its runtime.</summary>
    private List<(string Label, string Value)> RunClient(string package, string mode)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "run")).FullName;
        var start = new ProcessStartInfo(Interpreter!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = folder,
        };
        start.ArgumentList.Add(Path.Combine(Probes, "src", "k13client", "k13client.py"));
        start.ArgumentList.Add(mode);
        start.Environment["JGRAPH_RUNTIME_ROOT"] = AppContext.BaseDirectory.TrimEnd('\\');
        start.Environment["PYTHONPATH"] = package;
        start.Environment["MCR_CACHE_ROOT"] = Path.Combine(_root, "cache");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"k13client {mode} did not finish in three minutes.");
        }

        Assert.True(process.ExitCode == 0, $"k13client {mode} ended with {process.ExitCode}:\n{output}\n{error.Result}");
        string tokens = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace(_root.Replace(@"\", @"\\", StringComparison.Ordinal), "<W>", StringComparison.OrdinalIgnoreCase)
            .Replace(_root, "<W>", StringComparison.OrdinalIgnoreCase);
        return Labelled(tokens.Split('\n'));
    }

    /// <summary>A fact that is skipped, saying why, where no CPython is found to run the client with.</summary>
    private sealed class PythonFactAttribute : FactAttribute
    {
        public PythonFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Interpreter is null)
            {
                Skip = "No CPython was found to run the Python client with (ADR 0269).";
            }
        }
    }
}
