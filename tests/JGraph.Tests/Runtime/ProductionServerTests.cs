using System.Diagnostics;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.ProductionServer;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Native;
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
/// <c>run-k14.ps1</c> runs the whole probe against jgraph-mps itself. Stage K15's test does the same
/// with <c>k15client.py</c> against what R2025b's muserve answered (<c>recordings/k15_server.out.txt</c>):
/// the routes file's web handlers, the metrics, the health check, the request size limit and the hidden
/// stack; and checks the asynchronous protocol, which muserve lacks, against the RESTful API's documentation.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProductionServerTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private static readonly string Probes = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes");
    private static readonly string? Interpreter = PythonLocator.Find()?.Executable;
    private readonly RecordingScriptOutput _output = new();
    private readonly List<string> _serverLog = [];
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
        "out.large.sparselogical", "get.health", "get.origin.health", "in.big.number", "in.tiny.number", "path.space", "quiet", "get.discovery",
        "path.query.async", "path.query.async.client", "get.origin.discovery",
    ];

    /// <summary>The cases ADR 0271 records (compare-k15.py's KNOWN), stage K14's among them; the asynchronous mode is checked on its own.</summary>
    private static readonly HashSet<string> Divergences15 =
    [
        .. Divergences.Where(static d => d is not ("quiet" or "get.health" or "get.origin.health" or "get.discovery" or "get.origin.discovery")),
        "path.unexported", "path.query.async", "path.query.async.client", "get.metrics", "metrics.accept.json", "route.othercomp", "bad.headerslength",
        "env", "env.again", "env.nosuchkey", "metric.badname", "metric.dash", "metric.emptyname", "metric.cellname", "metric.zero", "metric.negative",
        "metric.textvalue", "metric.vector", "metric.complex", "metric.logical", "metric.noargs", "metric.onearg", "metric.threeargs", "metric.output",
        "metric.nosuchkind", "hidden.builtin",
    ];

    public ProductionServerTests()
    {
        JG.Reset();
        Directory.CreateDirectory(Src);
        foreach (string stage in new[] { "k14", "k15" })
        {
            foreach (string file in Directory.GetFiles(Path.Combine(Probes, "src", stage)))
            {
                File.Copy(file, Path.Combine(Src, Path.GetFileName(file)), overwrite: true);
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
        SameCases(Recorded("k14_server", "cors+discovery"), await Serve(archive, "k14comp", ["--cors-allowed-origins *", "--enable-discovery"], ["sync", "cors", "api"]), Divergences);
        SameCases(Recorded("k14_server", "plain"), await Serve(archive, "k14comp", [], ["cors", "api"]), Divergences);
    }

    [PythonFact]
    public async Task TheServerAnswersTheClientAsMuserveDid()
    {
        string outDir = Path.Combine(_root, "build15");
        string[] names = [.. Exported, "k14struct", "k15web", "k15webbad", "k15metric", "k15env"];
        string files = string.Join(", ", names.Select(f => $"'{Path.Combine(Src, f + ".m").Replace('\\', '/')}'"));
        ScriptRunResult built = await Run(
            $"r = compiler.build.productionServerArchive({{{files}}}, 'ArchiveName', 'k15comp', 'FunctionSignatures', '{Path.Combine(Src, "k15sig.json").Replace('\\', '/')}', "
            + $"'RoutesFile', '{Path.Combine(Src, "k15archroutes.json").Replace('\\', '/')}', 'OutputDir', '{outDir.Replace('\\', '/')}');");
        Assert.True(built.Success, built.Message + _output.ErrorText);
        string archive = Path.Combine(outDir, "k15comp.ctf");
        Assert.True(File.Exists(archive), "the archive was not written");

        // muserve's full session: CORS, discovery and metrics on, an instance routes file; then its limited one.
        string routes = Path.Combine(Src, "k15routes.json");
        List<(string Label, string Value)> full = await Serve(archive, "k15comp", ["--cors-allowed-origins *", "--enable-discovery", "--enable-metrics", "--routes-file " + routes],
            ["sync", "cors", "api", "routes", "metrics", "env", "async"], "k15client");
        SameCases(Recorded("k15_server", "full").Where(static c => !AsyncLabels.Contains(c.Label)).ToList(), full.Where(static c => !AsyncLabels.Contains(c.Label)).ToList(), Divergences15, Canonical);
        SameCases(Recorded("k15_server", "limited"), await Serve(archive, "k15comp", ["--request-size-limit 1KB", "--hide-matlab-error-stack"], ["disabled", "size", "hidden", "routes"], "k15client"), Divergences15, Canonical);

        // The asynchronous protocol, as the RESTful API documents it (ADR 0271): the states and codes of each step.
        Dictionary<string, string> async = full.Where(static c => AsyncLabels.Contains(c.Label)).ToDictionary(static c => c.Label, static c => c.Value, StringComparer.Ordinal);
        Assert.StartsWith("201\t", async["post"], StringComparison.Ordinal);
        Assert.Contains("\"state\": \"READING\"", async["post"], StringComparison.Ordinal);
        Assert.Contains("location=/~<CLIENT>/requests/<ID>", async["post"], StringComparison.Ordinal);
        Assert.Equal("READY", async["ready.state"]);
        Assert.StartsWith("200 OK", async["result"], StringComparison.Ordinal);
        Assert.Contains("\"lhs\"", async["result"], StringComparison.Ordinal);
        Assert.StartsWith("200 OK", async["up.since0.clients"], StringComparison.Ordinal);
        Assert.Contains("\"createdSeq\"", async["up.since0.clients"], StringComparison.Ordinal);
        Assert.StartsWith("404 ClientNotFound", async["up.since0.otherclient"], StringComparison.Ordinal);
        Assert.StartsWith("400 MissingParamSince", async["up.nosince"], StringComparison.Ordinal);
        Assert.StartsWith("400 InvalidParamSince", async["up.badsince"], StringComparison.Ordinal);
        Assert.StartsWith("400 NoMatchForQueryParams", async["up.badid"], StringComparison.Ordinal);
        Assert.StartsWith("410 RequestAlreadyCompleted", async["cancel.ready"], StringComparison.Ordinal);
        Assert.StartsWith("204 No Content", async["delete.ready"], StringComparison.Ordinal);
        Assert.StartsWith("410 RequestAlreadyDeleted", async["result.deleted"], StringComparison.Ordinal);
        Assert.Equal("PROCESSING", async["slow.state"]);
        Assert.StartsWith("409 RequestNotCompleted", async["slow.result.running"], StringComparison.Ordinal);
        Assert.StartsWith("409 RequestNotCompleted", async["slow.delete.running"], StringComparison.Ordinal);
        Assert.StartsWith("204 No Content", async["slow.cancel"], StringComparison.Ordinal);
        Assert.Equal("CANCELLED", async["slow.cancelled.state"]);
        Assert.StartsWith("410 RequestAlreadyCancelled", async["slow.result.cancelled"], StringComparison.Ordinal);
        Assert.StartsWith("204 No Content", async["slow.delete.cancelled"], StringComparison.Ordinal);
        Assert.Equal("READY", async["error.state"]);
        Assert.Contains("\"error\"", async["error.result"], StringComparison.Ordinal);
        Assert.StartsWith("404\t", async["post.nosuchfn"], StringComparison.Ordinal);
        Assert.StartsWith("200\t", async["post.sync"], StringComparison.Ordinal);
        Assert.StartsWith("404 URL not found", async["unknown.client"], StringComparison.Ordinal);
        Assert.StartsWith("200 OK", async["two.up.both"], StringComparison.Ordinal);
    }

    /// <summary>The async mode's labels: everything from the first POST to the two clients' deletes.</summary>
    private static readonly HashSet<string> AsyncLabels =
    [
        "post", "representation", "info", "ready.state", "representation.ready", "info.ready", "result", "result.again", "up.since0.ids", "up.since0.clients",
        "up.since0.both", "up.since0.otherclient", "up.nosince", "up.badsince", "up.negsince", "up.noquery", "up.badid", "up.post", "cancel.ready", "cancel.get",
        "delete.ready", "representation.deleted", "info.deleted", "result.deleted", "cancel.deleted", "delete.deleted", "post.slow", "slow.state",
        "slow.result.running", "slow.delete.running", "slow.cancel", "slow.cancelled.state", "slow.cancel.again", "slow.result.cancelled", "slow.delete.cancelled",
        "post.error", "error.state", "error.result", "error.delete", "post.nosuchfn", "post.nosuchcomp", "post.badbody", "badbody.state", "badbody.result",
        "badbody.delete", "post.noclient", "post.emptyclient", "post.clientspace", "post.sync", "post.modeupper", "post.clientfirst", "post.extraparam",
        "unknown.client", "unknown.request", "unknown.request.result", "unknown.request.info", "unknown.request.cancel", "unknown.request.delete", "malformed.id",
        "tilde.only", "tilde.other", "post.two.a", "post.two.b", "two.up.a", "two.up.b", "two.up.both", "two.up.ids", "two.a.by.b.up", "two.delete.a", "two.delete.b",
        "absent.self", "absent.info", "absent.result", "absent.cancel", "absent.delete", "absent.up",
    ];

    /// <summary>
    /// A web handler's echo as compare-k15.py compares it: the request headers sorted by name (Kestrel keeps
    /// them in its own order) and the length the Host header's port digits set replaced.
    /// </summary>
    private static string Canonical(string value)
    {
        string[] parts = value.Split('\t');
        if (parts.Length < 2 || !parts[1].Contains("x-k15=handled", StringComparison.Ordinal))
        {
            return value;
        }

        parts[1] = Regex.Replace(parts[1], @"content-length=\d+", "content-length=<L>");
        if (parts.Length < 3)
        {
            return string.Join('\t', parts);
        }

        string body = string.Join('\t', parts.Skip(2));
        if (body.StartsWith('<') && body.Contains("bytes md5", StringComparison.Ordinal))
        {
            body = "<echo>";
        }
        else
        {
            body = Regex.Replace(body, "\"Headers\":\\[(.*?)\\]", m =>
            {
                string inner = m.Groups[1].Value;
                if (inner.Length == 0)
                {
                    return m.Value;
                }

                string[] items = inner[1..^1].Split("},{");
                Array.Sort(items, StringComparer.Ordinal);
                return "\"Headers\":[{" + string.Join("},{", items) + "}]";
            });
        }

        return parts[0] + "\t" + parts[1] + "\t" + body;
    }

    // --- stage K16: the protobuf format through MathWorks' own client libraries -------------------------------

    /// <summary>R2025b's MPS client libraries (toolbox\compiler_sdk\mps_clients), when a MATLAB is installed here; the Python, .NET and C clients run from it.</summary>
    private static readonly string? MpsClients = FindMpsClients();

    private static readonly string Csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");

    private static string? FindMpsClients()
    {
        var roots = new List<string>();
        if (Environment.GetEnvironmentVariable("JGRAPH_MATLAB_ROOT") is { Length: > 0 } given)
        {
            roots.Add(given);
        }

        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string matlab = Path.Combine(programs, "MATLAB");
        if (Directory.Exists(matlab))
        {
            roots.AddRange(Directory.GetDirectories(matlab, "R20*").OrderByDescending(static d => d, StringComparer.Ordinal));
        }

        return roots.Select(r => Path.Combine(r, "toolbox", "compiler_sdk", "mps_clients")).FirstOrDefault(c => Directory.Exists(Path.Combine(c, "python", "dist")));
    }

    /// <summary>The cases ADR 0272 records, as mode/label (compare-k16.py's KNOWN and GARBAGE, with its patterns written out).</summary>
    private static readonly HashSet<string> Divergences16 = BuildDivergences16();

    private static HashSet<string> BuildDivergences16()
    {
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            "raw/in.uint64.row", "raw/in.int64.row", "raw/in.sparse.logical", "raw/in.sparse.single", "raw/in.double.precise", "raw/in.single.row",
            "raw/in.count.short", "raw/in.payload.absent", "raw/in.payload.two", "raw/in.type.mismatch", "raw/in.type.mismatch.char",
            "raw/in.complex.imag.short", "raw/in.sparse.badir", "raw/in.struct.badname", "proto/in.int64", "proto/out.int64", "raw/out.int64",
            "raw/out.sparselogical", "raw/in.complex.zero.imag", "raw/in.char.nd", "raw/out.handle", "raw/out.map", "raw/out.table", "raw/out.dict",
            "dotnet/in.longs", "dotnet/out.int64", "dotnet/typed.int64", "c/in.int64", "c/in.uint64", "c/out.int64", "c/in.sparse.logical",
            "c/in.sparse.complex", "c/out.sparselogical", "raw/query.async", "raw/echo.twoenums", "dotnet/state.0", "dotnet/state.1", "c/state.0", "c/state.1",
        };
        foreach (string mode in new[] { "proto", "raw", "dotnet", "c", "hidden" })
        {
            foreach (string kind in new[] { "plain", "id", "nested", "cause", "builtin", "undefined", "multiline", "quote" })
            {
                set.Add(mode + "/err." + kind);
            }

            set.Add(mode + "/out.nosuchkind");
            set.Add(mode + "/err.too.few.in");
            set.Add(mode + "/rest.err");
        }

        return set;
    }

    [MpsClientsFact]
    public async Task TheServerAnswersMathWorksClientsAsMuserveDid()
    {
        string outDir = Path.Combine(_root, "build16");
        string[] names = [.. Exported, "k14struct", "k15web", "k15webbad", "k15metric", "k15env"];
        string files = string.Join(", ", names.Select(f => $"'{Path.Combine(Src, f + ".m").Replace('\\', '/')}'"));
        ScriptRunResult built = await Run(
            $"r = compiler.build.productionServerArchive({{{files}}}, 'ArchiveName', 'k15comp', 'FunctionSignatures', '{Path.Combine(Src, "k15sig.json").Replace('\\', '/')}', "
            + $"'RoutesFile', '{Path.Combine(Src, "k15archroutes.json").Replace('\\', '/')}', 'OutputDir', '{outDir.Replace('\\', '/')}');");
        Assert.True(built.Success, built.Message + _output.ErrorText);
        string archive = Path.Combine(outDir, "k15comp.ctf");
        Assert.True(File.Exists(archive), "the archive was not written");
        string dist = Path.Combine(MpsClients!, "python", "dist");

        // muserve's full session: the Python client through MathWorks' package (proto) and by hand-built messages (raw), then the .NET and C clients.
        Dictionary<string, List<(string Label, string Value)>> recorded = Recorded16("full");
        Dictionary<string, List<(string Label, string Value)>> full = await Serve16(archive, ["--cors-allowed-origins *"], [dist], ["proto", "raw"]);
        foreach (string mode in new[] { "proto", "raw" })
        {
            SameCases16(mode, recorded[mode], full[mode]);
        }

        if (File.Exists(Csc))
        {
            SameCases16("dotnet", recorded["dotnet"], await ServeCompiled16(archive, DotNetClient(), []));
        }

        if (CCompilers.SelectedMsvc() is not null)
        {
            SameCases16("c", recorded["c"], await ServeCompiled16(archive, CClient(), []));
        }

        // The limited session: the error stack hidden, a 1 KB request limit.
        Dictionary<string, List<(string Label, string Value)>> limited = await Serve16(archive, ["--request-size-limit 1KB", "--hide-matlab-error-stack"], [dist], ["hidden", "size"]);
        recorded = Recorded16("limited");
        foreach (string mode in new[] { "hidden", "size" })
        {
            SameCases16(mode, recorded[mode], limited[mode]);
        }

        // The messages that end muserve's worker: JGraph refuses each and answers the next; two zero bytes after a
        // message, which muserve answers and then hangs on the next request, are nothing to JGraph (ADR 0272).
        Dictionary<string, List<(string Label, string Value)>> edge = await Serve16(archive, [], [dist], ["edge:all"]);
        List<(string Label, string Value)> edges = edge.Values.SelectMany(static v => v).ToList();
        Assert.Equal(Recorded16("edge")["edge"].Count, edges.Count);
        foreach ((string label, string value) in edges)
        {
            string expected = label is "edge.body.trailing" or "edge.body.trailing.then" ? "200 OK" : "400 InvalidInputArgs Invalid rhs.";
            Assert.True(value.StartsWith(expected, StringComparison.Ordinal), $"{label}: {value}\n{ServerLog()}");
        }
    }

    /// <summary>The recording's section for a session, its lines by mode.</summary>
    private static Dictionary<string, List<(string Label, string Value)>> Recorded16(string section)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", "k16_server.out.txt"));
        int at = Array.IndexOf(all, "== " + section);
        Assert.True(at >= 0, $"the recording has no section {section}");
        return ByMode(all.Skip(at + 1).TakeWhile(static l => !l.StartsWith("== ", StringComparison.Ordinal)));
    }

    /// <summary>Lines under "== mode" headings, each mode's labelled cases.</summary>
    private static Dictionary<string, List<(string Label, string Value)>> ByMode(IEnumerable<string> lines)
    {
        var modes = new Dictionary<string, List<(string Label, string Value)>>(StringComparer.Ordinal);
        string mode = "";
        foreach (string raw in lines)
        {
            string line = raw.TrimStart();
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                mode = line[3..];
                modes.TryAdd(mode, []);
                continue;
            }

            string[] parts = line.Split('\t', 2);
            if (parts.Length == 2 && parts[0] is not ("EXIT" or "SECONDS" or "COUNT"))
            {
                modes.TryAdd(mode, []);
                modes[mode].Add((parts[0], parts[1]));
            }
        }

        return modes;
    }

    private void SameCases16(string mode, List<(string Label, string Value)> recorded, List<(string Label, string Value)> mine) =>
        SameCases(recorded, mine, [.. Divergences16.Where(d => d.StartsWith(mode + "/", StringComparison.Ordinal)).Select(d => d[(mode.Length + 1)..])], Canonical16);

    /// <summary>An error reply's length follows its frames' file paths, each server's own (compare-k16.py's canonical).</summary>
    private static string Canonical16(string value) =>
        value.Contains("error {", StringComparison.Ordinal) && value.Contains("stack {", StringComparison.Ordinal)
            ? Regex.Replace(value, @"content-length=\d+", "content-length=<L>")
            : value;

    private async Task<Dictionary<string, List<(string Label, string Value)>>> Serve16(string archive, string[] options, string[] before, string[] modes)
    {
        await using ServerHost server = await Start16(archive, options);
        return ByMode(RunClientLines(server.Url, "k15comp", modes, "k16client", before));
    }

    private async Task<List<(string Label, string Value)>> ServeCompiled16(string archive, string exe, string[] options)
    {
        await using ServerHost server = await Start16(archive, options);
        return Labelled(RunProgram(exe, [server.Url.TrimEnd('/'), "k15comp"], Path.GetDirectoryName(exe)!));
    }

    private async Task<ServerHost> Start16(string archive, string[] options)
    {
        string folder = Path.Combine(_root, "inst-" + Guid.NewGuid().ToString("N")[..8]);
        ServerInstance instance = ServerInstance.New(folder, 0);
        File.AppendAllLines(ServerInstance.ConfigPath(folder), options);
        instance = ServerInstance.Open(folder);
        File.Copy(archive, Path.Combine(instance.AutoDeployFolder, "k15comp.ctf"));
        lock (_serverLog)
        {
            _serverLog.Clear();
        }

        ServerHost server = await ServerHost.StartAsync(instance, 0, line => { lock (_serverLog) { _serverLog.Add(line); } });
        lock (_serverLog)
        {
            Assert.Contains(_serverLog, l => l.Contains("k15comp: deployed", StringComparison.Ordinal));
        }

        return server;
    }

    /// <summary>MathWorks' .NET client program, compiled by the .NET Framework's csc.exe against the client assembly copied beside it (as record-k16.ps1 compiles it).</summary>
    private string DotNetClient()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "dotnet")).FullName;
        File.Copy(Path.Combine(MpsClients!, "dotnet", "MathWorks.MATLAB.ProductionServer.Client.dll"), Path.Combine(folder, "MathWorks.MATLAB.ProductionServer.Client.dll"), overwrite: true);
        string exe = Path.Combine(folder, "k16dotnet.exe");
        string said = string.Join("\n", RunProgram(Csc, ["/nologo", "/target:exe", "/out:" + exe, "/reference:" + Path.Combine(folder, "MathWorks.MATLAB.ProductionServer.Client.dll"),
            Path.Combine(Probes, "src", "k16dotnet", "k16dotnet.cs")], folder, exitCodeChecked: false));
        Assert.True(File.Exists(exe), "csc did not compile k16dotnet.cs:\n" + said);
        return exe;
    }

    /// <summary>MathWorks' C client program, compiled by MSVC against mpsclient.lib with the library's DLLs copied beside it (as build-k16c.ps1 compiles it).</summary>
    private string CClient()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "c")).FullName;
        CCompiler compiler = CCompilers.SelectedMsvc()!;
        string include = Path.Combine(MpsClients!, "c", "include");
        string lib = Path.Combine(MpsClients!, "c", "win64", "lib");
        (int exit, string said) = CCompilers.RunTool(compiler, "cl.exe",
            $"/nologo /O2 /MD /W3 /D_CRT_SECURE_NO_WARNINGS /I\"{include}\" \"{Path.Combine(Probes, "src", "k16c", "k16c.c")}\" /Fe:k16c.exe /link \"{Path.Combine(lib, "mpsclient.lib")}\"", folder);
        Assert.True(exit == 0, said);
        foreach (string dll in Directory.GetFiles(lib, "*.dll"))
        {
            File.Copy(dll, Path.Combine(folder, Path.GetFileName(dll)), overwrite: true);
        }

        return Path.Combine(folder, "k16c.exe");
    }

    /// <summary>Runs a program to its end and gives its standard output's lines; a non-zero exit fails the test unless <paramref name="exitCodeChecked"/> is off.</summary>
    private static string[] RunProgram(string exe, string[] arguments, string workingDirectory, bool exitCodeChecked = true)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(600_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(Path.GetFileName(exe) + " did not finish in ten minutes.");
        }

        if (exitCodeChecked)
        {
            Assert.True(process.ExitCode == 0, $"{Path.GetFileName(exe)} ended with {process.ExitCode}:\n{output}\n{error.Result}");
        }

        return output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    /// <summary>A fact that is skipped where no CPython or no MATLAB with its MPS client libraries is found.</summary>
    private sealed class MpsClientsFactAttribute : FactAttribute
    {
        public MpsClientsFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Interpreter is null || MpsClients is null)
            {
                Skip = "No CPython or no R2025b with its MPS client libraries (toolbox\\compiler_sdk\\mps_clients) was found to run the clients from (ADR 0272).";
            }
        }
    }

    /// <summary>Every case R2025b's server answered, JGraph's answers the same, the recorded divergences left out; the first that differs is shown whole, with the server's log.</summary>
    private void SameCases(List<(string Label, string Value)> recorded, List<(string Label, string Value)> mine, HashSet<string> divergences, Func<string, string>? canonical = null)
    {
        canonical ??= static v => v;
        // A label the client used twice (metric.counter) keeps its last answer, as compare-k15.py keeps it.
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string label, string value) in mine)
        {
            answers[label] = value;
        }

        foreach ((string label, string value) in recorded.Where(c => !divergences.Contains(c.Label)))
        {
            Assert.True(answers.TryGetValue(label, out string? answer), $"{label}: the client did not reach it\nR2025b: {value}\n{ServerLog()}");
            Assert.True(canonical(answer!) == canonical(value), $"{label}:\nR2025b: {value}\nJGraph: {answer}\n{ServerLog()}");
        }

        Assert.True(recorded.Count == mine.Count, $"{recorded.Count} cases recorded, {mine.Count} answered\n{ServerLog()}");
    }

    private string ServerLog()
    {
        lock (_serverLog)
        {
            return "server log:\n" + string.Join("\n", _serverLog);
        }
    }

    /// <summary>Makes an instance with <paramref name="options"/> added to its config, deploys the archive as <paramref name="component"/>, serves it and runs the client.</summary>
    private async Task<List<(string Label, string Value)>> Serve(string archive, string component, string[] options, string[] modes, string client = "k14client")
    {
        string folder = Path.Combine(_root, "inst-" + Guid.NewGuid().ToString("N")[..8]);
        ServerInstance instance = ServerInstance.New(folder, 0);
        File.AppendAllLines(ServerInstance.ConfigPath(folder), options);
        instance = ServerInstance.Open(folder);
        File.Copy(archive, Path.Combine(instance.AutoDeployFolder, component + ".ctf"));
        lock (_serverLog)
        {
            _serverLog.Clear();
        }

        await using ServerHost server = await ServerHost.StartAsync(instance, 0, line => { lock (_serverLog) { _serverLog.Add(line); } });
        lock (_serverLog)
        {
            Assert.Contains(_serverLog, l => l.Contains(component + ": deployed", StringComparison.Ordinal));
        }

        return RunClient(server.Url, component, modes, client);
    }

    private static List<(string Label, string Value)> Recorded(string recording, string section)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", recording + ".out.txt"));
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

    private List<(string Label, string Value)> RunClient(string url, string component, string[] modes, string client) =>
        Labelled(RunClientLines(url, component, modes, client, []));

    /// <summary>Runs a Python client against the server with <paramref name="before"/> between the component and the modes, and gives its lines.</summary>
    private string[] RunClientLines(string url, string component, string[] modes, string client, string[] before)
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
        start.ArgumentList.Add(Path.Combine(Probes, "src", client, client + ".py"));
        start.ArgumentList.Add(url.TrimEnd('/'));
        start.ArgumentList.Add(component);
        foreach (string argument in before)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string mode in modes)
        {
            start.ArgumentList.Add(mode);
        }

        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(600_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(client + " did not finish in ten minutes.");
        }

        Assert.True(process.ExitCode == 0, $"{client} ended with {process.ExitCode}:\n{output}\n{error.Result}");
        // The archives' uuids are each build's own; the recordings carry the token record-k15.ps1 writes for them.
        output = Regex.Replace(output, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<UUID>");
        return output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
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
