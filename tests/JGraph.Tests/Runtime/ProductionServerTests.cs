using System.Diagnostics;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.ProductionServer;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// The JGraph Production Server answering MATLAB Production Server's requests (compiler and SDK plan,
/// stage K14): an archive built in process by compiler.build.productionServerArchive from the probe's
/// functions (<c>src/k14</c>), deployed to an instance made here and served by <see cref="ServerHost"/>
/// on a free port with one worker process; the probe's client
/// (<c>tools/matlab-checklist/compiler-probes/src/k14client/k14client.py</c>), unchanged, run by
/// CPython against it, case for case against what R2025b's in-MATLAB test server answered it
/// (<c>recordings/k14_server.out.txt</c>), the cases ADR 0270 records as divergences left out.
/// <c>run-k14.ps1</c> runs the whole probe against jgraph-mps itself.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProductionServerTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private static readonly string Probes = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes");
    private static readonly string? Interpreter = PythonLocator.Find()?.Executable;
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k14-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The functions the in-MATLAB server exported (probe_k14_server), in its order; k14struct was left out.</summary>
    private static readonly string[] Exported =
    [
        "k14add", "k14echo", "k14multi", "k14err", "k14noout", "k14print", "k14slow", "k14nargs", "k14state", "k14quiet", "k14describe", "k14make",
    ];

    /// <summary>The cases ADR 0270 records (compare-k14.py's KNOWN_SERVER).</summary>
    private static readonly HashSet<string> Divergences =
    [
        "error.plain", "error.id", "error.nested", "error.cause", "error.builtin", "error.undefined", "error.multiline", "error.quote",
        "error.id.large", "error.id.nargout0", "error.too.many.in", "error.too.few.in", "error.make.unknown", "out.nosuchkind",
        "out.large.nosuchkind", "rhs.number", "rhs.object", "nargout.huge", "nargout.two.add", "noout.nargout1", "multi.4", "echo.0.1",
        "echo.3.4", "post.origin.error", "out.int64", "out.large.int64", "out.uint64", "out.large.uint64", "out.sparselogical",
        "out.large.sparselogical", "get.health", "get.origin.health", "in.big.number", "in.tiny.number", "path.space", "quiet",
    ];

    public ProductionServerTests()
    {
        JG.Reset();
        string from = Path.Combine(Probes, "src", "k14");
        Directory.CreateDirectory(Src);
        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(Src, Path.GetFileName(file)), overwrite: true);
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
            // A worker's extraction still held; the temp folder clears it.
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
    public async Task TheServerAnswersTheClientAsR2025bsTestServerDid()
    {
        string outDir = Path.Combine(_root, "build");
        string files = string.Join(", ", Exported.Select(f => $"'{Path.Combine(Src, f + ".m").Replace('\\', '/')}'"));
        ScriptRunResult built = await Run(
            $"r = compiler.build.productionServerArchive({{{files}}}, 'ArchiveName', 'k14comp', 'FunctionSignatures', '{Path.Combine(Src, "k14sig.json").Replace('\\', '/')}', 'OutputDir', '{outDir.Replace('\\', '/')}');");
        Assert.True(built.Success, built.Message + _output.ErrorText);
        string archive = Path.Combine(outDir, "k14comp.ctf");
        Assert.True(File.Exists(archive), "the archive was not written");

        // With CORS and discovery on, the client's three modes; then with both off, two.
        SameCases(Recorded("cors+discovery"), await Serve(archive, ["--cors-allowed-origins *", "--enable-discovery"], ["sync", "cors", "api"]));
        SameCases(Recorded("plain"), await Serve(archive, [], ["cors", "api"]));
    }

    /// <summary>Every case R2025b's server answered, JGraph's answers the same, the recorded divergences left out; the first that differs is shown whole.</summary>
    private static void SameCases(List<(string Label, string Value)> recorded, List<(string Label, string Value)> mine)
    {
        Dictionary<string, string> answers = mine.ToDictionary(static c => c.Label, static c => c.Value, StringComparer.Ordinal);
        foreach ((string label, string value) in recorded.Where(c => !Divergences.Contains(c.Label)))
        {
            Assert.True(answers.TryGetValue(label, out string? answer), $"{label}: the client did not reach it\nR2025b: {value}");
            Assert.True(answer == value, $"{label}:\nR2025b: {value}\nJGraph: {answer}");
        }

        Assert.Equal(recorded.Count, mine.Count);
    }

    /// <summary>Makes an instance with <paramref name="options"/> added to its config, deploys the archive, serves it and runs the client.</summary>
    private async Task<List<(string Label, string Value)>> Serve(string archive, string[] options, string[] modes)
    {
        string folder = Path.Combine(_root, "inst-" + Guid.NewGuid().ToString("N")[..8]);
        ServerInstance instance = ServerInstance.New(folder, 0);
        File.AppendAllLines(ServerInstance.ConfigPath(folder), options);
        instance = ServerInstance.Open(folder);
        File.Copy(archive, Path.Combine(instance.AutoDeployFolder, "k14comp.ctf"));
        var log = new List<string>();
        await using ServerHost server = await ServerHost.StartAsync(instance, 0, line => { lock (log) { log.Add(line); } });
        Assert.Contains(log, static l => l.Contains("k14comp: deployed", StringComparison.Ordinal));
        return RunClient(server.Url, modes);
    }

    private static List<(string Label, string Value)> Recorded(string section)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", "k14_server.out.txt"));
        int at = Array.IndexOf(all, "== " + section);
        Assert.True(at >= 0, $"the recording has no section {section}");
        return Labelled(all.Skip(at + 1).TakeWhile(static l => !l.StartsWith("== ", StringComparison.Ordinal)).Select(static l => l.TrimStart()));
    }

    private static List<(string Label, string Value)> Labelled(IEnumerable<string> lines) =>
    [
        .. lines.Where(static l => l.Length > 0 && !l.StartsWith("== ", StringComparison.Ordinal))
            .Select(static l => l.Split('\t', 2))
            .Where(static p => p.Length == 2 && p[0] is not ("EXIT" or "SECONDS"))
            .Select(static p => (p[0], p[1])),
    ];

    private List<(string Label, string Value)> RunClient(string url, string[] modes)
    {
        var start = new ProcessStartInfo(Interpreter!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = _root,
        };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(Path.Combine(Probes, "src", "k14client", "k14client.py"));
        start.ArgumentList.Add(url.TrimEnd('/'));
        start.ArgumentList.Add("k14comp");
        foreach (string mode in modes)
        {
            start.ArgumentList.Add(mode);
        }

        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("k14client did not finish in five minutes.");
        }

        Assert.True(process.ExitCode == 0, $"k14client ended with {process.ExitCode}:\n{output}\n{error.Result}");
        return Labelled(output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
    }

    /// <summary>A fact that is skipped, saying why, where no CPython is found to run the client with.</summary>
    private sealed class PythonFactAttribute : FactAttribute
    {
        public PythonFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Interpreter is null)
            {
                Skip = "No CPython was found to run the HTTP client with (ADR 0270).";
            }
        }
    }
}
