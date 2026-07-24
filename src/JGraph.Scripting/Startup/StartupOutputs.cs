namespace JGraph.Scripting.Startup;

/// <summary>
/// Writes script output to the process's standard streams: normal output to stdout, errors to stderr,
/// so a shell can redirect them apart. Used by the headless launcher, and by the application when a
/// parent process has handed it a pipe.
/// </summary>
public sealed class ConsoleScriptOutput : IScriptOutput
{
    /// <summary>The shared instance; <see cref="Console"/> is already synchronized.</summary>
    public static ConsoleScriptOutput Instance { get; } = new();

    /// <inheritdoc />
    public void Write(string text) => Console.Out.Write(text);

    /// <inheritdoc />
    public void WriteLine(string text) => Console.Out.WriteLine(text);

    /// <inheritdoc />
    public void WriteError(string text) => Console.Error.WriteLine(text);
}

/// <summary>
/// Appends script output to a text file — the <c>-logfile</c> sink. Errors are tagged so a log read
/// later still distinguishes them, which a file (unlike a console) cannot do by stream. Writes are
/// flushed immediately: a log that survives only a clean exit is no use when diagnosing a crash.
/// </summary>
public sealed class FileScriptOutput : IScriptOutput, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    /// <summary>Opens (creating or appending to) the log at <paramref name="path"/>.</summary>
    public FileScriptOutput(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        if (Path.GetDirectoryName(full) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(full, append: true) { AutoFlush = true };
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        lock (_gate)
        {
            _writer.Write(text);
        }
    }

    /// <inheritdoc />
    public void WriteLine(string text)
    {
        lock (_gate)
        {
            _writer.WriteLine(text);
        }
    }

    /// <inheritdoc />
    public void WriteError(string text)
    {
        lock (_gate)
        {
            _writer.WriteLine("[error] " + text);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _writer.Dispose();
}

/// <summary>
/// Fans one script's output out to several sinks — how <c>-logfile</c> works in every host: the
/// console (or the editor's console pane) and the log file see the same text in the same order.
/// </summary>
public sealed class TeeScriptOutput : IScriptOutput, IDisposable
{
    private readonly IReadOnlyList<IScriptOutput> _sinks;

    /// <summary>Creates a tee over <paramref name="sinks"/>, in write order.</summary>
    public TeeScriptOutput(params IScriptOutput[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks.ToArray();
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        foreach (IScriptOutput sink in _sinks)
        {
            sink.Write(text);
        }
    }

    /// <inheritdoc />
    public void WriteLine(string text)
    {
        foreach (IScriptOutput sink in _sinks)
        {
            sink.WriteLine(text);
        }
    }

    /// <inheritdoc />
    public void WriteError(string text)
    {
        foreach (IScriptOutput sink in _sinks)
        {
            sink.WriteError(text);
        }
    }

    /// <summary>Disposes the sinks that own a resource (the log file); the others are left alone.</summary>
    public void Dispose()
    {
        foreach (IScriptOutput sink in _sinks)
        {
            (sink as IDisposable)?.Dispose();
        }
    }
}
