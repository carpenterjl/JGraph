namespace JGraph.ProductionServer;

/// <summary>
/// A server instance's folder, laid out as MATLAB Production Server's <c>mps-new</c> lays one out
/// (compiler and SDK plan, stage K14): <c>config\main_config</c>, <c>auto_deploy</c>, <c>log</c>,
/// <c>pid</c> and <c>endpoint</c>.
/// </summary>
public sealed class ServerInstance
{
    private ServerInstance(string root, MainConfig config)
    {
        Root = root;
        Config = config;
    }

    /// <summary>The instance's folder.</summary>
    public string Root { get; }

    /// <summary>Its <c>config\main_config</c>.</summary>
    public MainConfig Config { get; }

    /// <summary>The folder watched for archives.</summary>
    public string AutoDeployFolder => Path.GetFullPath(Path.Combine(Root, Config.AutoDeployRoot));

    /// <summary>The folder the log is written in.</summary>
    public string LogFolder => Path.GetFullPath(Path.Combine(Root, Config.LogRoot));

    /// <summary>The instance's routes file (<c>--routes-file</c>), relative to the instance's folder when not absolute; null when it has none.</summary>
    public string? RoutesFilePath => Config.RoutesFile is { } file ? Path.GetFullPath(Path.Combine(Root, file)) : null;

    /// <summary>The file <c>run</c> writes the server's address to.</summary>
    public string EndpointFile => Path.Combine(Root, "endpoint", "http");

    /// <summary>The file <c>run</c> writes its process id to.</summary>
    public string PidFile => Path.Combine(Root, "pid", "main.pid");

    /// <summary>The config file's path.</summary>
    public static string ConfigPath(string root) => Path.Combine(root, "config", "main_config");

    /// <summary>Makes an instance: the folders and a <c>main_config</c> with the defaults; refuses a folder that already is one.</summary>
    public static ServerInstance New(string root, int port = MainConfig.DefaultPort)
    {
        string full = Path.GetFullPath(root);
        if (File.Exists(ConfigPath(full)))
        {
            throw new InvalidOperationException($"'{full}' is a server instance already: it has config\\main_config.");
        }

        foreach (string folder in new[] { "config", "auto_deploy", "log", "pid", "endpoint" })
        {
            Directory.CreateDirectory(Path.Combine(full, folder));
        }

        File.WriteAllText(ConfigPath(full), MainConfig.DefaultText(port));
        return Open(full);
    }

    /// <summary>Opens an instance folder and reads its config.</summary>
    public static ServerInstance Open(string root)
    {
        string full = Path.GetFullPath(root);
        string config = ConfigPath(full);
        if (!File.Exists(config))
        {
            throw new FileNotFoundException($"'{full}' is not a server instance: it has no config\\main_config. Make one with: jgraph-mps new \"{full}\"", config);
        }

        return new ServerInstance(full, MainConfig.Read(config));
    }
}
