using System.Globalization;

namespace JGraph.ProductionServer;

/// <summary>
/// An instance's <c>config\main_config</c>, in MATLAB Production Server's form (compiler and SDK plan,
/// stage K14): one <c>--option value</c> a line, <c>#</c> comments, the option names MPS's
/// <c>mps-new</c> writes. The ones the JGraph server reads are listed here; the others are kept and
/// reported as not read, so an MPS instance's file works as it is.
/// </summary>
public sealed class MainConfig
{
    /// <summary>The port a new instance listens on, MPS's default.</summary>
    public const int DefaultPort = 9910;

    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    private MainConfig()
    {
    }

    /// <summary>The options the server reads, each with what it does with it.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownOptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["--http"] = "the address to listen on: a port, or host:port",
        ["--num-workers"] = "how many worker processes run functions",
        ["--auto-deploy-root"] = "the folder watched for archives (auto_deploy)",
        ["--log-root"] = "the folder the log is written in (log)",
        ["--request-timeout"] = "how long a function may run, h:mm:ss (1:00:00)",
        ["--cors-allowed-origins"] = "the origins browsers may call from, comma-separated, or *",
        ["--hide-matlab-error-stack"] = "leave the M-code frames out of an error reply",
        ["--request-size-limit"] = "the largest request body, in bytes (67108864)",
        ["--enable-discovery"] = "answer GET /api/discovery with the archives' signatures",
        ["--log-severity"] = "error, warning, information or trace",
        ["--mcr-root"] = "ignored: the server runs the JGraph Runtime it was installed with",
        ["--routes-file"] = "a routes file that applies to every archive (read, not yet routed: stage K15)",
    };

    /// <summary>Every option in the file, by name, with the values given in order.</summary>
    public IReadOnlyDictionary<string, List<string>> Values => _values;

    /// <summary>The options the file names that the server does not read.</summary>
    public IReadOnlyList<string> Unread => [.. _values.Keys.Where(k => !KnownOptions.ContainsKey(k))];

    /// <summary>The port, and the host when <c>--http</c> gives one.</summary>
    public (string Host, int Port) Http
    {
        get
        {
            string? value = First("--http");
            if (value is null)
            {
                return ("127.0.0.1", DefaultPort);
            }

            int colon = value.LastIndexOf(':');
            string host = colon < 0 ? "127.0.0.1" : value[..colon];
            string portText = colon < 0 ? value : value[(colon + 1)..];
            return (host.Length == 0 ? "127.0.0.1" : host, int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : DefaultPort);
        }
    }

    public int Workers => int.TryParse(First("--num-workers"), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 1;

    public string AutoDeployRoot => First("--auto-deploy-root") ?? "auto_deploy";

    public string LogRoot => First("--log-root") ?? "log";

    public TimeSpan RequestTimeout => First("--request-timeout") is { } text && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan span) && span > TimeSpan.Zero
        ? span
        : TimeSpan.FromHours(1);

    public long RequestSizeLimit => long.TryParse(First("--request-size-limit"), NumberStyles.None, CultureInfo.InvariantCulture, out long n) && n > 0 ? n : 64L * 1024 * 1024;

    public bool HideErrorStack => _values.ContainsKey("--hide-matlab-error-stack");

    public bool Discovery => _values.ContainsKey("--enable-discovery");

    public string? RoutesFile => First("--routes-file");

    /// <summary>The origins allowed, lowercased; <c>*</c> means every origin; empty means CORS is off.</summary>
    public IReadOnlyList<string> CorsOrigins =>
        [.. (_values.GetValueOrDefault("--cors-allowed-origins") ?? []).SelectMany(static v => v.Split(',')).Select(static o => o.Trim().ToLowerInvariant()).Where(static o => o.Length > 0)];

    private string? First(string option) => _values.TryGetValue(option, out List<string>? values) && values.Count > 0 ? values[0] : null;

    /// <summary>Reads a config file.</summary>
    public static MainConfig Read(string path) => Parse(File.ReadAllLines(path));

    /// <summary>Reads config text: a line's first word is the option, the rest its value; blank lines and <c>#</c> comments are skipped.</summary>
    public static MainConfig Parse(IEnumerable<string> lines)
    {
        var config = new MainConfig();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int space = line.IndexOfAny([' ', '\t']);
            string option = space < 0 ? line : line[..space];
            string value = space < 0 ? "" : line[(space + 1)..].Trim();
            if (!config._values.TryGetValue(option, out List<string>? values))
            {
                config._values[option] = values = [];
            }

            values.Add(value);
        }

        return config;
    }

    /// <summary>The file <c>jgraph-mps new</c> writes: MPS's option names with JGraph's defaults, the rest as comments.</summary>
    public static string DefaultText(int port) =>
        $"""
        # The JGraph Production Server's instance configuration, in MATLAB Production Server's form:
        # one --option a line. jgraph-mps reads the options below; an MPS instance's other options are
        # taken as written and reported as not read when the server starts.

        # The address to listen on: a port, or host:port.
        --http {port.ToString(CultureInfo.InvariantCulture)}

        # How many worker processes run the archives' functions, each a JGraph Runtime of its own.
        --num-workers 1

        # The folder watched for archives (.ctf files built with mcc -W CTF or compiler.build.productionServerArchive).
        --auto-deploy-root auto_deploy

        # Where the log is written.
        --log-root log

        # How long a function may run before its request is given up and its worker restarted.
        --request-timeout 1:00:00

        # Uncomment to let browsers call from these origins (comma-separated, or * for any).
        # --cors-allowed-origins http://localhost:3000

        # Uncomment to answer GET /api/discovery with the deployed archives' function signatures.
        # --enable-discovery

        # Uncomment to leave the M-code stack out of an error reply.
        # --hide-matlab-error-stack

        """.Replace("\n", "\r\n", StringComparison.Ordinal);
}
