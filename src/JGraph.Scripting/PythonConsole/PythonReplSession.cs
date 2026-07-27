using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using JGraph.Api;

namespace JGraph.Scripting.PythonConsole;

/// <summary>
/// A live Python session hosted in a child process. The host launches
/// <c>python -u -X utf8 jgraph_console.py</c> and speaks newline-delimited JSON to it; the child keeps
/// one namespace alive across statements, and its <c>jgraph</c> module turns plotting calls into
/// messages this class executes against the host's real figures.
/// </summary>
/// <remarks>
/// Out of process, rather than the in-process pythonnet the <see cref="PythonScriptEngine"/> script
/// path uses, for two reasons. Cancellation actually works: an interrupted statement kills the child,
/// which is the only reliable way to stop arbitrary CPython on Windows. And a segfaulting C extension
/// takes down a child process instead of JGraph. The cost is that the two Python paths — a script run
/// and the console — cannot share state; see ADR 0035.
/// </remarks>
internal sealed class PythonReplSession : IScriptSession
{
    /// <summary>How long to wait for the child to announce itself before giving up on it.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly ScriptContext _context;
    private readonly PythonRuntimeInfo? _runtime;
    private readonly object _writeLock = new();

    private Process? _child;
    private PythonHostBridge _bridge;
    private JGraphScriptGlobals _globals;
    private TaskCompletionSource<PythonConsoleMessage>? _pendingDone;
    private TaskCompletionSource<PythonConsoleMessage>? _pendingVars;
    private TaskCompletionSource<bool>? _pendingReady;
    private IReadOnlyList<ScriptVariable> _variables = Array.Empty<ScriptVariable>();
    private string? _startFailure;
    private int _nextId;
    private bool _disposed;

    /// <summary>Creates a session. The child process is not started until the first statement, so
    /// opening the console costs nothing until Python is actually used.</summary>
    public PythonReplSession(ScriptContext context, PythonRuntimeInfo? runtime, string language)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _runtime = runtime;
        Language = language ?? throw new ArgumentNullException(nameof(language));
        JG.Reset();
        _globals = new JGraphScriptGlobals(_context);
        _bridge = new PythonHostBridge(_globals);
    }

    /// <inheritdoc />
    public string Language { get; }

    /// <summary>
    /// The console script shipped beside the executable, or null when it is missing (a broken deploy —
    /// the file is a build output of this project).
    /// </summary>
    public static string? FindConsoleScript()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "python", "jgraph_console.py");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <inheritdoc />
    public async Task<ScriptRunResult> ExecuteAsync(string code, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!await EnsureStartedAsync().ConfigureAwait(false))
        {
            return ScriptRunResult.Failed(_startFailure ?? PythonScriptEngine.UnavailableMessage);
        }

        _globals.BeginRun();
        var done = new TaskCompletionSource<PythonConsoleMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDone = done;
        int id = ++_nextId;

        try
        {
            Send(PythonConsoleCodec.Exec(id, code));

            // Cancellation means killing the child: CPython running arbitrary code has no cooperative
            // check we can reach, and Windows' console-control signals do not cross process groups
            // reliably enough to build on.
            using (cancellationToken.Register(Interrupt))
            {
                PythonConsoleMessage result = await done.Task.ConfigureAwait(false);
                await RefreshVariablesAsync().ConfigureAwait(false);
                _globals.ShowTouchedFigures();

                if (result.Exit is { } exitCode)
                {
                    return ScriptRunResult.Exited(exitCode, _globals.FiguresShown, _variables);
                }

                if (result.Ok)
                {
                    return ScriptRunResult.Ok(_globals.FiguresShown, _variables);
                }

                string message = result.Message ?? "The statement failed.";
                return ScriptRunResult.Failed(
                    message, new[] { new ScriptDiagnostic(result.Line, 0, message, IsError: true) });
            }
        }
        catch (SessionRestartedException)
        {
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Failed("Statement was cancelled. The Python session restarted — variables were lost.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return ScriptRunResult.Failed($"The Python session ended unexpectedly: {ex.Message}");
        }
        finally
        {
            _pendingDone = null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptVariable> GetVariables() => _variables;

    /// <inheritdoc />
    public void Clear()
    {
        StopChild();
        JG.Reset();
        _variables = Array.Empty<ScriptVariable>();
        _globals = new JGraphScriptGlobals(_context);
        _bridge = new PythonHostBridge(_globals);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopChild();
        }

        return ValueTask.CompletedTask;
    }

    // --- Child process lifetime -------------------------------------------------------------------

    private async Task<bool> EnsureStartedAsync()
    {
        if (_child is { HasExited: false })
        {
            return true;
        }

        if (_runtime?.Executable is not { Length: > 0 } interpreter)
        {
            _startFailure = PythonScriptEngine.UnavailableMessage;
            return false;
        }

        if (FindConsoleScript() is not { } script)
        {
            _startFailure = "The Python console script (python/jgraph_console.py) is missing from this installation.";
            return false;
        }

        var startInfo = new ProcessStartInfo(interpreter)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = _context.WorkingDirectory is { Length: > 0 } directory && Directory.Exists(directory)
                ? directory
                : AppContext.BaseDirectory,
        };

        startInfo.ArgumentList.Add("-u");        // unbuffered, so a partial line never sits in the child
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("utf8");      // UTF-8 mode, so the encodings above actually match
        startInfo.ArgumentList.Add(script);

        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReady = ready;

        try
        {
            _child = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _startFailure = $"Could not start the Python console: {ex.Message}";
            return false;
        }

        if (_child is null)
        {
            _startFailure = "Could not start the Python console.";
            return false;
        }

        // Both pipes are drained on their own threads. A child that fills an undrained pipe blocks
        // forever, which would look exactly like a hung statement.
        StartPump(_child.StandardOutput, HandleLine);
        StartPump(_child.StandardError, line => _context.Output.WriteError(line + Environment.NewLine));

        Task completed = await Task.WhenAny(ready.Task, Task.Delay(StartTimeout)).ConfigureAwait(false);
        if (completed != ready.Task)
        {
            _startFailure = "The Python console did not start within 30 seconds.";
            StopChild();
            return false;
        }

        _startFailure = null;
        return true;
    }

    private void StartPump(StreamReader reader, Action<string> handle)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (reader.ReadLine() is { } line)
                {
                    handle(line);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The child went away; the pending completion sources are failed by StopChild.
            }
        })
        {
            IsBackground = true,
            Name = "JGraph Python console pump",
        };
        thread.Start();
    }

    private void HandleLine(string line)
    {
        if (!PythonConsoleCodec.TryDecode(line, out PythonConsoleMessage message))
        {
            // Something wrote to the real stdout despite the redirection — surface it rather than
            // dropping it, because it is usually a C extension's diagnostic.
            _context.Output.Write(line + Environment.NewLine);
            return;
        }

        switch (message.Type)
        {
            case "ready":
                _pendingReady?.TrySetResult(true);
                break;
            case "out":
                _context.Output.Write(message.Text ?? string.Empty);
                break;
            case "err":
                _context.Output.WriteError(message.Text ?? string.Empty);
                break;
            case "done":
                _pendingDone?.TrySetResult(message);
                break;
            case "vars":
                _pendingVars?.TrySetResult(message);
                break;
            case "call":
                Send(_bridge.Invoke(message));
                break;
        }
    }

    private async Task RefreshVariablesAsync()
    {
        var vars = new TaskCompletionSource<PythonConsoleMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingVars = vars;
        try
        {
            Send(PythonConsoleCodec.Vars());
            PythonConsoleMessage snapshot = await vars.Task.ConfigureAwait(false);
            _variables = Project(snapshot);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or SessionRestartedException)
        {
            // A snapshot must never fail an otherwise-successful statement.
        }
        finally
        {
            _pendingVars = null;
        }
    }

    private static IReadOnlyList<ScriptVariable> Project(PythonConsoleMessage snapshot)
    {
        if (snapshot.Items is not { Count: > 0 } items)
        {
            return Array.Empty<ScriptVariable>();
        }

        var variables = new List<ScriptVariable>(items.Count);
        foreach (PythonVariablePayload item in items)
        {
            object? raw = item.Type switch
            {
                "array" => item.Data,
                "number" when double.TryParse(item.Repr, out double number) => number,
                "string" => item.Repr,
                _ => null,
            };

            variables.Add(new ScriptVariable(item.Name, item.Type, ScriptVariable.Truncate(item.Repr), raw));
        }

        return variables;
    }

    private void Send(PythonConsoleMessage message)
    {
        Process? child = _child;
        if (child is null || child.HasExited)
        {
            throw new InvalidOperationException("The Python console is not running.");
        }

        // The reader thread answers 'call' messages while the caller's thread may be sending the next
        // statement, so writes are serialised.
        lock (_writeLock)
        {
            child.StandardInput.WriteLine(PythonConsoleCodec.Encode(message));
            child.StandardInput.Flush();
        }
    }

    private void Interrupt() => StopChild(new SessionRestartedException());

    private void StopChild(Exception? reason = null)
    {
        Process? child = Interlocked.Exchange(ref _child, null);
        if (child is null)
        {
            return;
        }

        try
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
        finally
        {
            child.Dispose();
        }

        // Nothing will ever answer the in-flight requests now, so fail them rather than hang.
        Exception failure = reason ?? new InvalidOperationException("The Python console stopped.");
        _pendingDone?.TrySetException(failure);
        _pendingVars?.TrySetException(failure);
        _pendingReady?.TrySetResult(false);
    }

    /// <summary>Signals that the child was killed under a statement, so the caller can say why.</summary>
    private sealed class SessionRestartedException : Exception
    {
        public SessionRestartedException()
            : base("The Python session restarted.")
        {
        }
    }
}
