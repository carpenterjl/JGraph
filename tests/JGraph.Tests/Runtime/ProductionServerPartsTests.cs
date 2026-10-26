using System.Diagnostics;
using JGraph.Deploy;
using JGraph.ProductionServer;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// The JGraph Production Server's parts of stage K15 (compiler and SDK plan, ADR 0271) on their own:
/// the metrics registry reading a worker's metric events and writing Prometheus text as muserve
/// does (<c>k15_server</c>: metrics.*), the config's request size limit, the route table's precedence,
/// the asynchronous request registry's states, and the admin commands <c>jgraph-mps new</c>,
/// <c>start</c>, <c>status</c>, <c>check</c>, <c>deploy</c>, <c>undeploy</c> and <c>stop</c> run as processes.
/// </summary>
public sealed class ProductionServerPartsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k15-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp folder clears it.
        }
    }

    [Fact]
    public void MetricsReadEventsAndWriteThemAsMuserveDoes()
    {
        var metrics = new MetricsRegistry();
        // metrics.after: counters add, gauges set, a type word that is neither is untyped, a gauge event on a counter's name is passed over.
        Assert.True(metrics.ReadLine("[metric:counter] k15comp_1|k15_calls_total 1"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp_1|k15_calls_total 1"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp_1|k15_calls_total 2.5"));
        Assert.True(metrics.ReadLine("[metric:gauge] k15comp_1|k15_level 4"));
        Assert.True(metrics.ReadLine("[metric:gauge] k15comp_1|k15_level -1.25"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp_1|k15_big 123456789"));
        Assert.True(metrics.ReadLine("[metric:gauge] k15comp_1|k15_small 1.2345e-05"));
        Assert.True(metrics.ReadLine("[metric:gauge] k15comp_1|k15_nan NaN"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp_1|k15_inf Inf"));
        Assert.True(metrics.ReadLine("[metric:counter] k15_rawnoarchive 1"));
        Assert.True(metrics.ReadLine("  [metric:counter] k15comp|k15_rawspace 1"));
        Assert.False(metrics.ReadLine("text [metric:counter] k15comp|k15_rawmid 1"));
        Assert.False(metrics.ReadLine("[metric:counter] k15comp|k15_rawbad x"));
        Assert.True(metrics.ReadLine("[metric:histogram] k15comp|k15_rawtype 1"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp|k15_rawtwo 1"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp|k15_rawtwo 1"));
        Assert.True(metrics.ReadLine("[metric:counter] k15comp|k15_calls_total -5"));
        Assert.True(metrics.ReadLine("[metric:gauge] k15comp|k15_calls_total 1"));
        Assert.True(metrics.ReadLine("[metric:gauge] other|k15_rawgauge 9"));
        Assert.False(metrics.ReadLine("line 1"));

        string text = metrics.Text();
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("# TYPE matlabprodserver_up_time_seconds counter", lines[0]);
        Assert.Equal("# TYPE matlabprodserver_requests_canceled_total counter", lines[18]);
        string custom = string.Join("\n", lines.Skip(20));
        Assert.Equal(
            "# TYPE k15_rawnoarchive counter\nk15_rawnoarchive 1\n"
            + "# TYPE k15_big counter\nk15_big{archive=\"k15comp_1\"} 1.23457e+08\n"
            + "# TYPE k15_calls_total counter\nk15_calls_total{archive=\"k15comp_1\"} 4.5\n"
            + "# TYPE k15_inf counter\nk15_inf{archive=\"k15comp_1\"} inf\n"
            + "# TYPE k15_level gauge\nk15_level{archive=\"k15comp_1\"} -1.25\n"
            + "# TYPE k15_nan gauge\nk15_nan{archive=\"k15comp_1\"} nan\n"
            + "# TYPE k15_small gauge\nk15_small{archive=\"k15comp_1\"} 1.2345e-05\n"
            + "# TYPE k15_calls_total counter\nk15_calls_total{archive=\"k15comp\"} -5\n"
            + "# TYPE k15_rawspace counter\nk15_rawspace{archive=\"k15comp\"} 1\n"
            + "# TYPE k15_rawtwo counter\nk15_rawtwo{archive=\"k15comp\"} 2\n"
            + "# TYPE k15_rawtype untyped\nk15_rawtype{archive=\"k15comp\"} 1\n"
            + "# TYPE k15_rawgauge gauge\nk15_rawgauge{archive=\"other\"} 9",
            custom);
        Assert.Equal("1e+06", MetricsRegistry.G(1e6));
        Assert.Equal("7", MetricsRegistry.G(7));
        Assert.Equal("4.55647e+08", MetricsRegistry.G(455647000));
    }

    [Fact]
    public void TheConfigReadsSizesAndTheLastValueOfAnOption()
    {
        Assert.Equal(1024, MainConfig.ParseSize("1KB"));
        Assert.Equal(64L * 1024 * 1024, MainConfig.ParseSize("64MB"));
        Assert.Equal(1024L * 1024 * 1024, MainConfig.ParseSize("1GB"));
        Assert.Equal(512, MainConfig.ParseSize("512"));
        Assert.Equal(512, MainConfig.ParseSize("512B"));
        Assert.Null(MainConfig.ParseSize("big"));
        Assert.Null(MainConfig.ParseSize("0"));
        MainConfig config = MainConfig.Parse(["--request-size-limit 64MB", "--request-size-limit 1KB", "--enable-metrics", "--routes-file config/routes.json"]);
        Assert.Equal(1024, config.RequestSizeLimit);
        Assert.True(config.Metrics);
        Assert.Equal("config/routes.json", config.RoutesFile);
        Assert.Equal(MainConfig.DefaultRequestSizeLimit, MainConfig.Parse([]).RequestSizeLimit);
        Assert.False(MainConfig.Parse([]).Metrics);
    }

    [Fact]
    public void TheRouteTableTriesTheInstanceFileThenTheArchives()
    {
        var routes = new RouteTable();
        Assert.Null(routes.Match("/k15/web"));
        string file = Path.Combine(_root, "routes.json");
        File.WriteAllText(file, """{"version":"1.0.0","pathmap":[{"match":"^/k15/web$","webhandler":{"component":"k15comp","function":"k15web"}},{"match":"k15/anywhere","webhandler":{"component":"other","function":"f"}},{"match":"^/k15/first$","webhandler":{"component":"k15comp","function":"one"}},{"match":"^/k15/first$","webhandler":{"component":"k15comp","function":"two"}}]}""");
        routes.LoadInstanceFile(file);
        Assert.Equal(new RouteTable.Target("k15comp", "k15web"), routes.Match("/k15/web"));
        Assert.Null(routes.Match("/k15/web?x=1"));
        Assert.Null(routes.Match("/K15/WEB"));
        Assert.Equal(new RouteTable.Target("other", "f"), routes.Match("/x/k15/anywhere/y"));
        Assert.Equal("one", routes.Match("/k15/first")!.Function);
        File.WriteAllText(Path.Combine(_root, "bad.json"), """{"version":"1.0.0","pathmap":[{"match":"^/a[$","webhandler":{"function":"f"}}]}""");
        Assert.Throws<RoutesException>(() => new RouteTable().LoadInstanceFile(Path.Combine(_root, "bad.json")));
    }

    [Fact]
    public void AsynchronousRequestsMoveThroughTheirStates()
    {
        var registry = new AsyncRequestRegistry();
        AsyncRequest request = registry.Create("k15c");
        Assert.Equal(AsyncState.Reading, request.State);
        Assert.Equal("/~" + registry.Instance + "/requests", registry.Up);
        Assert.Equal(registry.Up + "/" + request.Id, request.Self);
        long first = request.LastModifiedSeq;
        request.Advance(AsyncState.InQueue);
        Assert.True(request.LastModifiedSeq > first);
        request.Advance(AsyncState.Processing);
        request.Advance(AsyncState.Ready, new WorkerProtocol.Reply(1, 200, "OK", "{\"lhs\":[3]}"));
        Assert.True(request.Terminal);
        request.Advance(AsyncState.Cancelled);
        Assert.Equal(AsyncState.Ready, request.State);
        Assert.Contains("\"state\":\"READY\"", request.RepresentationJson(), StringComparison.Ordinal);
        Assert.Contains("\"request\":\"" + request.Self + "\"", request.InfoJson(), StringComparison.Ordinal);
        Assert.Single(registry.Select(["k15c"], [], 0));
        Assert.Empty(registry.Select(["k15c"], [], request.LastModifiedSeq));
        Assert.True(registry.KnowsClient("k15c"));
        Assert.False(registry.KnowsClient("other"));
        request.MarkDeleted();
        Assert.Empty(registry.Select(["k15c"], [], 0));
        Assert.Same(request, registry.Find(request.Id));
        Assert.Contains("\"createdSeq\":", registry.CollectionJson([]), StringComparison.Ordinal);
    }

    [MpsFact]
    public async Task TheAdminCommandsRunAnInstance()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "jgraph-mps.exe");
        string folder = Path.Combine(_root, "inst");
        (int code, string output) = await RunAsync(exe, "new", folder, "--port", "0");
        Assert.Equal(0, code);
        Assert.True(File.Exists(ServerInstance.ConfigPath(folder)), output);

        (code, output) = await RunAsync(exe, "status", folder);
        Assert.Equal(0, code);
        Assert.Contains("STOPPED", output, StringComparison.Ordinal);

        (code, output) = await RunAsync(exe, "stop", folder);
        Assert.Equal(1, code);
        (code, output) = await RunAsync(exe, "stop", folder, "-f");
        Assert.Equal(0, code);

        (code, output) = await RunAsync(exe, "start", "-C", folder);
        Assert.True(code == 0, output);
        Assert.Contains("STARTED", output, StringComparison.Ordinal);
        try
        {
            string url = File.ReadAllText(Path.Combine(folder, "endpoint", "http")).Trim();
            Assert.StartsWith("http://", url, StringComparison.Ordinal);

            (code, output) = await RunAsync(exe, "status", folder);
            Assert.Equal(0, code);
            Assert.Contains("STARTED", output, StringComparison.Ordinal);
            Assert.Contains(url, output, StringComparison.Ordinal);

            (code, output) = await RunAsync(exe, "start", folder);
            Assert.Equal(1, code);
            (code, output) = await RunAsync(exe, "start", folder, "-f");
            Assert.Equal(0, code);

            (code, output) = await RunAsync(exe, "check", new Uri(url).Authority, "--timeout", "30");
            Assert.True(code == 0, output);
            Assert.Contains("Check completed successfully", output, StringComparison.Ordinal);

            string archive = Path.Combine(_root, "k15x.ctf");
            File.WriteAllText(archive, "not an archive");
            (code, output) = await RunAsync(exe, "deploy", folder, archive);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(folder, "auto_deploy", "k15x.ctf")));
            (code, output) = await RunAsync(exe, "undeploy", folder, "k15x");
            Assert.Equal(0, code);
            Assert.False(File.Exists(Path.Combine(folder, "auto_deploy", "k15x.ctf")));
            (code, output) = await RunAsync(exe, "undeploy", folder, "k15x");
            Assert.Equal(1, code);
        }
        finally
        {
            (code, output) = await RunAsync(exe, "stop", folder, "--timeout", "0:01:00");
            Assert.True(code == 0, output);
        }

        Assert.Contains("STOPPED", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "pid", "main.pid")));
        (code, output) = await RunAsync(exe, "status", folder);
        Assert.Contains("STOPPED", output, StringComparison.Ordinal);
    }

    private static async Task<(int Code, string Output)> RunAsync(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return (process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
    }

    /// <summary>A fact that is skipped where the server program is not beside the tests.</summary>
    private sealed class MpsFactAttribute : FactAttribute
    {
        public MpsFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(AppContext.BaseDirectory, "jgraph-mps.exe")))
            {
                Skip = "jgraph-mps.exe is not beside the tests.";
            }
        }
    }
}
