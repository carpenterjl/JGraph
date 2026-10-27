using System.Diagnostics;
using System.Globalization;

namespace JGraph.Deploy;

/// <summary>
/// The Production Server Archive Compiler's test client's server (compiler and SDK plan, stage
/// K19): a JGraph Production Server (<c>jgraph-mps</c>, stage K14) run as a process over an instance
/// made for the archive alone, as R2025b's Test Client starts a server in MATLAB for the functions
/// of its task. Its lines (the log the window shows) are read off its standard output; it is ended
/// by the window, and its instance folder deleted.
/// </summary>
public sealed class LocalTestServer : IDisposable
{
    /// <summary>The environment variable naming <c>jgraph-mps.exe</c>, which wins over every other place.</summary>
    public const string ProgramVariable = "JGRAPH_MPS";

    private readonly Process _process;
    private bool _disposed;

    private LocalTestServer(Process process, string folder, string url, string archiveName)
    {
        _process = process;
        Folder = folder;
        Url = url;
        ArchiveName = archiveName;
    }

    /// <summary>The instance folder the server runs over.</summary>
    public string Folder { get; }

    /// <summary>The server's address, <c>http://127.0.0.1:&lt;port&gt;</c>.</summary>
    public string Url { get; }

    /// <summary>The deployed archive's name, the first segment of a request's path.</summary>
    public string ArchiveName { get; }

    /// <summary>Whether the server process is still running.</summary>
    public bool IsRunning => !_disposed && !_process.HasExited;

    /// <summary>
    /// Finds <c>jgraph-mps.exe</c>: the file <c>JGRAPH_MPS</c> names, the program beside this one
    /// (where the JGraph Runtime and the developer install keep it), then a source checkout's build
    /// above this program; null when none is found.
    /// </summary>
    public static string? FindProgram()
    {
        if (Environment.GetEnvironmentVariable(ProgramVariable) is { Length: > 0 } named && File.Exists(named))
        {
            return Path.GetFullPath(named);
        }

        string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string beside = Path.Combine(here, "jgraph-mps.exe");
        if (File.Exists(beside))
        {
            return beside;
        }

        for (string? folder = here; folder is not null; folder = Path.GetDirectoryName(folder))
        {
            foreach (string configuration in new[] { "Release", "Debug" })
            {
                string built = Path.Combine(folder, "src", "JGraph.ProductionServer", "bin", configuration, "net8.0", "jgraph-mps.exe");
                if (File.Exists(built))
                {
                    return built;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Starts a server for <paramref name="archive"/> (a <c>.ctf</c> a build wrote) on
    /// <paramref name="port"/> (0 for any free one), its lines going to <paramref name="log"/>, and
    /// returns once it listens.
    /// </summary>
    /// <exception cref="InvalidOperationException">The program is not found, or the server did not come up.</exception>
    public static LocalTestServer Start(string archive, int port, Action<string> log, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(log);
        if (!File.Exists(archive))
        {
            throw new InvalidOperationException($"The archive '{archive}' does not exist; build the task first.");
        }

        string program = FindProgram()
            ?? throw new InvalidOperationException("jgraph-mps.exe, the JGraph Production Server, was not found beside this program, in a checkout's build, or where JGRAPH_MPS points.");
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-testclient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string portText = port.ToString(CultureInfo.InvariantCulture);
        RunToEnd(program, ["new", folder, "--port", portText], log);
        File.Copy(archive, Path.Combine(folder, "auto_deploy", Path.GetFileName(archive)));

        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add(folder);
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(portText);
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { log(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { log(e.Data); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        string endpoint = Path.Combine(folder, "endpoint", "http");
        var clock = Stopwatch.StartNew();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(60);
        while (clock.Elapsed < limit)
        {
            if (process.HasExited)
            {
                TryDelete(folder);
                throw new InvalidOperationException($"jgraph-mps ended with exit code {process.ExitCode} before it listened; see its lines.");
            }

            if (File.Exists(endpoint) && File.ReadAllText(endpoint).Trim() is { Length: > 0 } url)
            {
                return new LocalTestServer(process, folder, url, Path.GetFileNameWithoutExtension(archive));
            }

            Thread.Sleep(100);
        }

        End(process);
        TryDelete(folder);
        throw new InvalidOperationException($"jgraph-mps did not listen within {limit.TotalSeconds:F0} s.");
    }

    /// <summary>Ends the server and deletes its instance folder.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        End(_process);
        _process.Dispose();
        TryDelete(Folder);
    }

    private static void RunToEnd(string program, IReadOnlyList<string> arguments, Action<string> log)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            log(line.TrimEnd('\r'));
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"jgraph-mps {arguments[0]} failed with exit code {process.ExitCode}: {output.Trim()}");
        }
    }

    private static void End(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
