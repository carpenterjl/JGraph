using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace JGraph.ProductionServer;

/// <summary>
/// <c>jgraph-mps</c>, the JGraph Production Server's command (compiler and SDK plan, stages K14 and
/// K15), shaped like MATLAB Production Server's <c>mps-*</c> commands: <c>new</c> makes an instance
/// (<c>mps-new</c>), <c>start</c>, <c>stop</c>, <c>restart</c> and <c>status</c> run it detached and
/// report it as <c>mps-start</c>, <c>mps-stop</c>, <c>mps-restart</c> and <c>mps-status</c> do
/// (<c>'&lt;path&gt;' STARTED</c>), <c>check</c> asks a running server for its health as <c>mps-check</c>
/// does, <c>deploy</c> and <c>undeploy</c> put archives in and out of <c>auto_deploy</c>, <c>run</c>
/// serves in the foreground, and <c>worker</c> is the process the server starts for each worker.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class Program
{
    private const string Usage =
        "jgraph-mps  The JGraph Production Server: runs the functions of archives built with mcc -W CTF\n"
        + "            or compiler.build.productionServerArchive, answering MATLAB Production Server's REST requests.\n\n"
        + "  jgraph-mps new <folder> [--port <n>]        Make a server instance: config\\main_config, auto_deploy, log\n"
        + "  jgraph-mps start [-C] <folder> [-f]         Start the instance detached (-f: succeed when it already runs)\n"
        + "  jgraph-mps stop [-C] <folder> [-f] [-k] [--timeout hh:mm:ss]\n"
        + "                                              Stop it (-f: succeed when stopped; -k: end its processes at once)\n"
        + "  jgraph-mps restart [-C] <folder> [-f]       Stop it and start it again\n"
        + "  jgraph-mps status [-C] <folder>             '<folder>' STARTED with its address, or STOPPED\n"
        + "  jgraph-mps check <host>:<port> [--timeout <seconds>]\n"
        + "                                              Ask a running server for its health\n"
        + "  jgraph-mps deploy <folder> <archive.ctf>... Copy archives into the instance's auto_deploy folder\n"
        + "  jgraph-mps undeploy <folder> <name>...      Remove archives from it\n"
        + "  jgraph-mps run <folder> [--port <n>]        Serve the instance in the foreground until Ctrl+C\n"
        + "  jgraph-mps worker                           (started by the server for each of its workers)\n";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-?" or "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        using var ending = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            ending.Cancel();
        };

        try
        {
            switch (args[0])
            {
                case "worker":
                    return await WorkerMain.RunAsync(ending.Token).ConfigureAwait(false);
                case "new":
                {
                    if (args.Length < 2)
                    {
                        return Usage2("new needs the instance folder");
                    }

                    ServerInstance made = ServerInstance.New(args[1], Port(args) ?? MainConfig.DefaultPort);
                    Console.Out.WriteLine($"jgraph-mps: made the instance {made.Root}");
                    Console.Out.WriteLine($"  edit {ServerInstance.ConfigPath(made.Root)}, put archives in {made.AutoDeployFolder}, then: jgraph-mps start \"{made.Root}\"");
                    return 0;
                }

                case "run":
                    return args.Length < 2 ? Usage2("run needs the instance folder") : await RunAsync(args, ending).ConfigureAwait(false);
                case "start":
                    return Folder(args) is { } startFolder ? await StartAsync(startFolder, Has(args, "-f"), Port(args)).ConfigureAwait(false) : Usage2("start needs the instance folder");
                case "stop":
                    return Folder(args) is { } stopFolder ? await StopAsync(stopFolder, Has(args, "-f"), Has(args, "-k") || Has(args, "--kill"), TimeoutOf(args)).ConfigureAwait(false) : Usage2("stop needs the instance folder");
                case "restart":
                {
                    if (Folder(args) is not { } folder)
                    {
                        return Usage2("restart needs the instance folder");
                    }

                    int stopped = await StopAsync(folder, Has(args, "-f"), kill: false, TimeoutOf(args)).ConfigureAwait(false);
                    return stopped != 0 ? stopped : await StartAsync(folder, force: false, Port(args)).ConfigureAwait(false);
                }

                case "status":
                    return Folder(args) is { } statusFolder ? Status(statusFolder) : Usage2("status needs the instance folder");
                case "check":
                    return args.Length < 2 ? Usage2("check needs host:port") : await CheckAsync(args[1], TimeoutOf(args)).ConfigureAwait(false);
                case "deploy":
                    return args.Length < 3 ? Usage2("deploy needs the instance folder and an archive") : Deploy(args[1], args[2..]);
                case "undeploy":
                    return args.Length < 3 ? Usage2("undeploy needs the instance folder and an archive name") : Undeploy(args[1], args[2..]);
                default:
                    return Usage2($"'{args[0]}' is not a jgraph-mps command");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or HttpRequestException)
        {
            Console.Error.WriteLine("jgraph-mps: " + ex.Message);
            return 1;
        }
    }

    // --- run ----------------------------------------------------------------------------------------

    private static async Task<int> RunAsync(string[] args, CancellationTokenSource ending)
    {
        ServerInstance instance = ServerInstance.Open(args[1]);
        Directory.CreateDirectory(instance.LogFolder);
        string logFile = Path.Combine(instance.LogFolder, "main.log");
        // Started detached by jgraph-mps start, the server has no console to write to: main.log alone.
        bool detached = Has(args, "--detached");
        var logLock = new object();
        void Log(string line)
        {
            string stamped = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {line}";
            lock (logLock)
            {
                if (!detached)
                {
                    Console.Out.WriteLine(stamped);
                }

                try
                {
                    File.AppendAllText(logFile, stamped + Environment.NewLine);
                }
                catch (IOException)
                {
                    // The console has it.
                }
            }
        }

        // jgraph-mps stop asks this process to end through a named event.
        using var stopSignal = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName(Environment.ProcessId));
        _ = Task.Run(() =>
        {
            WaitHandle.WaitAny([stopSignal, ending.Token.WaitHandle]);
            ending.Cancel();
        });

        await using ServerHost server = await ServerHost.StartAsync(instance, Port(args), Log, ending.Token).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(instance.EndpointFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(instance.PidFile)!);
        File.WriteAllText(instance.EndpointFile, server.Url);
        File.WriteAllText(instance.PidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Log($"listening on {server.Url} with {instance.Config.Workers} worker(s); Ctrl+C or jgraph-mps stop ends it");
        try
        {
            await Task.Delay(Timeout.Infinite, ending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log("stopping");
        }
        finally
        {
            TryDelete(instance.EndpointFile);
            TryDelete(instance.PidFile);
        }

        return 0;
    }

    private static string StopEventName(int pid) => "Local\\jgraph-mps-stop-" + pid.ToString(CultureInfo.InvariantCulture);

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start to overwrite.
        }
    }

    // --- start, stop, status ----------------------------------------------------------------------------

    /// <summary>The running server's process, by the pid file, or null when the instance is stopped (a stale file counts as stopped).</summary>
    private static Process? Running(ServerInstance instance)
    {
        if (!File.Exists(instance.PidFile) || !int.TryParse(File.ReadAllText(instance.PidFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return null;
        }

        try
        {
            Process process = Process.GetProcessById(pid);
            if (process.HasExited || !process.ProcessName.StartsWith("jgraph-mps", StringComparison.OrdinalIgnoreCase) && !process.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<int> StartAsync(string folder, bool force, int? port)
    {
        ServerInstance instance = ServerInstance.Open(folder);
        using (Process? already = Running(instance))
        {
            if (already is not null)
            {
                if (force)
                {
                    Console.Out.WriteLine($"'{instance.Root}' STARTED");
                    return 0;
                }

                Console.Error.WriteLine($"jgraph-mps: '{instance.Root}' is already running (process {already.Id}); use -f to succeed anyway.");
                return 1;
            }
        }

        TryDelete(instance.EndpointFile);
        TryDelete(instance.PidFile);
        (string fileName, string arguments) = Worker.WorkerCommand();
        string runArguments = arguments.Replace("worker", "run", StringComparison.Ordinal) + $" \"{instance.Root}\" --detached" + (port is { } p ? $" --port {p.ToString(CultureInfo.InvariantCulture)}" : "");
        // A console of its own, hidden, so the server inherits no handle from this command (or from
        // whatever ran it) and writes its lines to main.log alone.
        var start = new ProcessStartInfo(fileName, runArguments)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = instance.Root,
        };
        Process process = Process.Start(start) ?? throw new InvalidOperationException("The server process did not start.");
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(120))
        {
            if (process.HasExited)
            {
                Console.Error.WriteLine($"jgraph-mps: the server ended at once with {process.ExitCode}; see {Path.Combine(instance.LogFolder, "main.log")}");
                return 1;
            }

            if (File.Exists(instance.EndpointFile) && File.Exists(instance.PidFile))
            {
                Console.Out.WriteLine($"'{instance.Root}' STARTED");
                Console.Out.WriteLine(File.ReadAllText(instance.EndpointFile).Trim());
                return 0;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        Console.Error.WriteLine("jgraph-mps: the server did not listen within two minutes; it goes on starting.");
        return 1;
    }

    private static async Task<int> StopAsync(string folder, bool force, bool kill, TimeSpan? timeout)
    {
        ServerInstance instance = ServerInstance.Open(folder);
        using Process? process = Running(instance);
        if (process is null)
        {
            TryDelete(instance.EndpointFile);
            TryDelete(instance.PidFile);
            if (force)
            {
                Console.Out.WriteLine($"'{instance.Root}' STOPPED");
                return 0;
            }

            Console.Error.WriteLine($"jgraph-mps: '{instance.Root}' is not running; use -f to succeed anyway.");
            return 1;
        }

        if (!kill)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(StopEventName(process.Id));
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                kill = true;
            }
        }

        if (kill)
        {
            process.Kill(entireProcessTree: true);
        }

        using var waited = new CancellationTokenSource(timeout ?? Timeout.InfiniteTimeSpan);
        try
        {
            await process.WaitForExitAsync(waited.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"jgraph-mps: '{instance.Root}' is still stopping after {timeout}.");
            return 1;
        }

        TryDelete(instance.EndpointFile);
        TryDelete(instance.PidFile);
        Console.Out.WriteLine($"'{instance.Root}' STOPPED");
        return 0;
    }

    private static int Status(string folder)
    {
        ServerInstance instance = ServerInstance.Open(folder);
        using Process? process = Running(instance);
        if (process is null)
        {
            Console.Out.WriteLine($"'{instance.Root}' STOPPED");
            return 0;
        }

        Console.Out.WriteLine($"'{instance.Root}' STARTED");
        if (File.Exists(instance.EndpointFile))
        {
            Console.Out.WriteLine(File.ReadAllText(instance.EndpointFile).Trim());
        }

        return 0;
    }

    private static async Task<int> CheckAsync(string hostPort, TimeSpan? timeout)
    {
        string url = hostPort.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? hostPort.TrimEnd('/') : "http://" + hostPort.TrimEnd('/');
        using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(2) };
        Console.Out.WriteLine($"Checking the JGraph Production Server at {url}");
        using HttpResponseMessage response = await client.GetAsync(url + "/api/health").ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.IsSuccessStatusCode && body.Contains("\"ok\"", StringComparison.Ordinal))
        {
            Console.Out.WriteLine("Check completed successfully");
            return 0;
        }

        Console.Error.WriteLine($"Check failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        return 1;
    }

    private static int Deploy(string folder, string[] archives)
    {
        ServerInstance instance = ServerInstance.Open(folder);
        Directory.CreateDirectory(instance.AutoDeployFolder);
        foreach (string archive in archives)
        {
            if (!File.Exists(archive))
            {
                Console.Error.WriteLine($"jgraph-mps: '{archive}' is not a file.");
                return 1;
            }

            string target = Path.Combine(instance.AutoDeployFolder, Path.GetFileName(archive));
            File.Copy(archive, target, overwrite: true);
            Console.Out.WriteLine($"deployed {Path.GetFileNameWithoutExtension(archive)} to {target}");
        }

        return 0;
    }

    private static int Undeploy(string folder, string[] names)
    {
        ServerInstance instance = ServerInstance.Open(folder);
        foreach (string name in names)
        {
            string file = Path.Combine(instance.AutoDeployFolder, Path.GetFileNameWithoutExtension(name) + ".ctf");
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"jgraph-mps: '{Path.GetFileNameWithoutExtension(name)}' is not deployed in {instance.AutoDeployFolder}.");
                return 1;
            }

            File.Delete(file);
            Console.Out.WriteLine($"retired {Path.GetFileNameWithoutExtension(name)}");
        }

        return 0;
    }

    // --- the command line ---------------------------------------------------------------------------------

    /// <summary>The instance folder: the first argument after the command that is not a switch, <c>-C &lt;folder&gt;</c> included.</summary>
    private static string? Folder(string[] args)
    {
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-C")
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }

            if (!args[i].StartsWith('-'))
            {
                return args[i];
            }

            if (args[i] is "--port" or "--timeout")
            {
                i++;
            }
        }

        return null;
    }

    private static bool Has(string[] args, string flag) => args.Skip(1).Contains(flag, StringComparer.Ordinal);

    private static int? Port(string[] args)
    {
        for (int i = 1; i + 1 < args.Length; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            {
                return port;
            }
        }

        return null;
    }

    /// <summary><c>--timeout hh:mm:ss</c> (stop) or <c>--timeout seconds</c> (check); null when not given.</summary>
    private static TimeSpan? TimeoutOf(string[] args)
    {
        for (int i = 1; i + 1 < args.Length; i++)
        {
            if (args[i] == "--timeout")
            {
                string text = args[i + 1];
                if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan span) && text.Contains(':'))
                {
                    return span;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }

        return null;
    }

    private static int Usage2(string problem)
    {
        Console.Error.WriteLine("jgraph-mps: " + problem);
        Console.Error.Write(Usage);
        return 2;
    }
}
