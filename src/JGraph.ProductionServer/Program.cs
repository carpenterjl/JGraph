using System.Globalization;

namespace JGraph.ProductionServer;

/// <summary>
/// <c>jgraph-mps</c>, the JGraph Production Server's command (compiler and SDK plan, stage K14):
/// <c>new &lt;folder&gt;</c> makes an instance, <c>run &lt;folder&gt;</c> serves it in the foreground, and
/// <c>worker</c> is the process the server starts for each worker. Stage K15 adds the detached
/// start, stop and status MATLAB Production Server's <c>mps-*</c> commands give.
/// </summary>
internal static class Program
{
    private const string Usage =
        "jgraph-mps  The JGraph Production Server: runs the functions of archives built with mcc -W CTF\n"
        + "            or compiler.build.productionServerArchive, answering MATLAB Production Server's REST requests.\n\n"
        + "  jgraph-mps new <folder> [--port <n>]   Make a server instance: config\\main_config, auto_deploy, log\n"
        + "  jgraph-mps run <folder> [--port <n>]   Serve the instance until Ctrl+C; archives go in its auto_deploy folder\n"
        + "  jgraph-mps worker                      (started by the server for each of its workers)\n";

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
                    Console.Out.WriteLine($"  edit {ServerInstance.ConfigPath(made.Root)}, put archives in {made.AutoDeployFolder}, then: jgraph-mps run \"{made.Root}\"");
                    return 0;
                }

                case "run":
                {
                    if (args.Length < 2)
                    {
                        return Usage2("run needs the instance folder");
                    }

                    ServerInstance instance = ServerInstance.Open(args[1]);
                    Directory.CreateDirectory(instance.LogFolder);
                    string logFile = Path.Combine(instance.LogFolder, "main.log");
                    var logLock = new object();
                    void Log(string line)
                    {
                        string stamped = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {line}";
                        lock (logLock)
                        {
                            Console.Out.WriteLine(stamped);
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

                    await using ServerHost server = await ServerHost.StartAsync(instance, Port(args), Log, ending.Token).ConfigureAwait(false);
                    File.WriteAllText(Path.Combine(instance.Root, "endpoint", "http"), server.Url);
                    File.WriteAllText(Path.Combine(instance.Root, "pid", "main.pid"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                    Log($"listening on {server.Url} with {instance.Config.Workers} worker(s); Ctrl+C stops");
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ending.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Log("stopping");
                    }

                    return 0;
                }

                default:
                    return Usage2($"'{args[0]}' is not a jgraph-mps command");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("jgraph-mps: " + ex.Message);
            return 1;
        }
    }

    private static int? Port(string[] args)
    {
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            {
                return port;
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
