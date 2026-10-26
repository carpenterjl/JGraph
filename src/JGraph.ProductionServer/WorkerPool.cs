using System.Collections.Concurrent;
using System.Diagnostics;

namespace JGraph.ProductionServer;

/// <summary>
/// The server's worker processes (compiler and SDK plan, stage K14): <c>--num-workers</c> of them, each
/// <c>jgraph-mps worker</c> hosting a JGraph Runtime with every deployed archive open, taking one request
/// at a time, as MATLAB Production Server's workers do. A worker that ends (a crash, a request given up
/// on) is started again with the archives deployed to it; the request it held answers 500. A request
/// cancelled by its client (stage K15's asynchronous API) is stopped at the function's next statement
/// and its worker kept.
/// </summary>
internal sealed class WorkerPool : IAsyncDisposable
{
    private readonly int _count;
    private readonly Action<string> _log;
    private readonly Action<string>? _printed;
    private readonly List<Worker> _workers = [];
    private readonly ConcurrentDictionary<string, (string Path, string Folder, string CacheRoot)> _deployed = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _free;
    private readonly ConcurrentQueue<Worker> _idle = new();
    private readonly CancellationTokenSource _ending = new();

    /// <param name="count">How many workers.</param>
    /// <param name="log">Where the server's lines go.</param>
    /// <param name="printed">Hears every line a worker prints (a function's output, a metric event), before it is logged.</param>
    public WorkerPool(int count, Action<string> log, Action<string>? printed = null)
    {
        _count = Math.Max(1, count);
        _log = log;
        _printed = printed;
        _free = new SemaphoreSlim(0, _count);
    }

    /// <summary>The functions of each deployed archive, by name, as the workers reported them.</summary>
    public ConcurrentDictionary<string, string[]> Functions { get; } = new(StringComparer.Ordinal);

    /// <summary>Starts the workers.</summary>
    public async Task StartAsync()
    {
        for (int i = 0; i < _count; i++)
        {
            Worker worker = await Worker.StartAsync(i, Heard, _ending.Token).ConfigureAwait(false);
            _workers.Add(worker);
            _idle.Enqueue(worker);
            _free.Release();
        }
    }

    private void Heard(int index, string line)
    {
        _printed?.Invoke(line);
        _log($"worker {index}: {line}");
    }

    /// <summary>
    /// Deploys an archive to every worker, extracted into the instance's cache <paramref name="cacheRoot"/>
    /// under the folder <paramref name="folder"/> (<c>&lt;name&gt;_&lt;n&gt;</c>, which <c>ctfroot</c> ends with as
    /// MPS's does); answers the component's functions, or null when no worker could open it.
    /// </summary>
    public async Task<string[]?> DeployAsync(string name, string path, string folder, string cacheRoot)
    {
        _deployed[name] = (path, folder, cacheRoot);
        string[]? functions = null;
        foreach (Worker worker in _workers.ToArray())
        {
            WorkerProtocol.Reply reply = await worker.AskAsync(new WorkerProtocol.Request(0, "deploy", name, path, Folder: folder, CacheRoot: cacheRoot), Timeout.InfiniteTimeSpan, _ending.Token).ConfigureAwait(false);
            if (reply.Status == 200)
            {
                functions = reply.Functions ?? [];
            }
            else
            {
                _log($"worker {worker.Index} could not deploy {name}: {reply.Reason}");
            }
        }

        if (functions is not null)
        {
            Functions[name] = functions;
        }
        else
        {
            _deployed.TryRemove(name, out _);
        }

        return functions;
    }

    /// <summary>Retires an archive from every worker.</summary>
    public async Task UndeployAsync(string name)
    {
        _deployed.TryRemove(name, out _);
        Functions.TryRemove(name, out _);
        foreach (Worker worker in _workers.ToArray())
        {
            await worker.AskAsync(new WorkerProtocol.Request(0, "undeploy", name), Timeout.InfiniteTimeSpan, _ending.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one request on the next free worker: the reply, or a 500 when the worker ended, or
    /// a 500 "Request Timeout" when <paramref name="timeout"/> passed, after which that worker is
    /// restarted with the archives deployed again. <paramref name="started"/> hears when a worker
    /// takes the request; <paramref name="cancellationToken"/> stops the function and answers
    /// <c>RequestCancelled</c>.
    /// </summary>
    public Task<WorkerProtocol.Reply> CallAsync(string component, string function, string body, bool hideStack, TimeSpan timeout, CancellationToken cancellationToken, Action? started = null) =>
        RunAsync(new WorkerProtocol.Request(0, "call", component, Function: function, Body: body, HideStack: hideStack), $"{component}/{function}", timeout, cancellationToken, started);

    /// <summary>Runs a web request on a web handler (stage K15).</summary>
    public Task<WorkerProtocol.Reply> CallWebAsync(string component, string function, WorkerProtocol.WebRequest web, TimeSpan timeout, CancellationToken cancellationToken, Action? started = null) =>
        RunAsync(new WorkerProtocol.Request(0, "web", component, Function: function, Web: web), $"{component}/{function} (web)", timeout, cancellationToken, started);

    private async Task<WorkerProtocol.Reply> RunAsync(WorkerProtocol.Request request, string what, TimeSpan timeout, CancellationToken cancellationToken, Action? started)
    {
        await _free.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_idle.TryDequeue(out Worker? worker))
        {
            _free.Release();
            return new WorkerProtocol.Reply(0, 503, "Service Unavailable");
        }

        started?.Invoke();
        bool broken = false;
        try
        {
            WorkerProtocol.Reply reply = await worker.AskAsync(request, timeout, cancellationToken).ConfigureAwait(false);
            broken = reply.Status == 500 && reply.Reason.StartsWith("Request Timeout", StringComparison.Ordinal) || worker.HasExited;
            return reply;
        }
        catch (WorkerEndedException ended)
        {
            broken = true;
            _log($"worker {worker.Index} ended while running {what}: {ended.Message}");
            return new WorkerProtocol.Reply(0, 500, "Internal Server Error Worker process ended.");
        }
        finally
        {
            if (broken)
            {
                _ = RestartAsync(worker);
            }
            else
            {
                _idle.Enqueue(worker);
                _free.Release();
            }
        }
    }

    private async Task RestartAsync(Worker broken)
    {
        await broken.KillAsync().ConfigureAwait(false);
        if (_ending.IsCancellationRequested)
        {
            return;
        }

        try
        {
            Worker fresh = await Worker.StartAsync(broken.Index, Heard, _ending.Token).ConfigureAwait(false);
            foreach ((string name, (string path, string folder, string cacheRoot)) in _deployed)
            {
                await fresh.AskAsync(new WorkerProtocol.Request(0, "deploy", name, path, Folder: folder, CacheRoot: cacheRoot), Timeout.InfiniteTimeSpan, _ending.Token).ConfigureAwait(false);
            }

            lock (_workers)
            {
                _workers[_workers.IndexOf(broken)] = fresh;
            }

            _idle.Enqueue(fresh);
            _free.Release();
            _log($"worker {fresh.Index} restarted");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            _log($"worker {broken.Index} could not be restarted: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ending.Cancel();
        foreach (Worker worker in _workers.ToArray())
        {
            await worker.StopAsync().ConfigureAwait(false);
        }

        _ending.Dispose();
    }
}

/// <summary>A worker process ended while a request was with it.</summary>
internal sealed class WorkerEndedException(string message) : Exception(message);

/// <summary>One worker process and the frames to and from it.</summary>
internal sealed class Worker
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Action<int, string> _heard;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<WorkerProtocol.Reply>> _pending = new();
    private long _nextId;

    private Worker(int index, Process process, Action<int, string> heard)
    {
        Index = index;
        _process = process;
        _heard = heard;
        _input = process.StandardInput.BaseStream;
    }

    public int Index { get; }

    public bool HasExited => _process.HasExited;

    /// <summary>The command that starts a worker: this program itself, or <c>dotnet</c> with its assembly where there is no program (the tests).</summary>
    public static (string FileName, string Arguments) WorkerCommand()
    {
        string? exe = Environment.ProcessPath;
        string assembly = typeof(Worker).Assembly.Location;
        if (exe is not null && Path.GetFileNameWithoutExtension(exe).Equals("jgraph-mps", StringComparison.OrdinalIgnoreCase))
        {
            return (exe, "worker");
        }

        string beside = Path.Combine(Path.GetDirectoryName(assembly) ?? "", "jgraph-mps.exe");
        return File.Exists(beside) ? (beside, "worker") : ("dotnet", $"\"{assembly}\" worker");
    }

    public static async Task<Worker> StartAsync(int index, Action<int, string> heard, CancellationToken cancellationToken)
    {
        (string fileName, string arguments) = WorkerCommand();
        var start = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        Process process = Process.Start(start) ?? throw new InvalidOperationException("The worker process did not start.");
        var worker = new Worker(index, process, heard);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                heard(index, line);
            }
        };
        process.BeginErrorReadLine();
        _ = worker.ReadRepliesAsync(process.StandardOutput.BaseStream, cancellationToken);
        await Task.CompletedTask.ConfigureAwait(false);
        return worker;
    }

    private async Task ReadRepliesAsync(Stream output, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                WorkerProtocol.Reply? reply = await WorkerProtocol.ReadAsync<WorkerProtocol.Reply>(output, cancellationToken).ConfigureAwait(false);
                if (reply is null)
                {
                    break;
                }

                if (_pending.TryRemove(reply.Id, out TaskCompletionSource<WorkerProtocol.Reply>? waiting))
                {
                    waiting.TrySetResult(reply);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // The worker is gone or its stream broken; the waiters below hear of it, and the log says which.
            _heard(Index, $"[reply stream] {ex.GetType().Name}: {ex.Message}");
        }

        string ended = "the worker's reply stream ended";
        try
        {
            if (_process.HasExited)
            {
                ended += $" (exit code {_process.ExitCode})";
            }
        }
        catch (InvalidOperationException)
        {
            // Disposed already.
        }

        _heard(Index, "[ended] " + ended);
        foreach (TaskCompletionSource<WorkerProtocol.Reply> waiting in _pending.Values)
        {
            waiting.TrySetException(new WorkerEndedException(ended));
        }
    }

    /// <summary>
    /// Sends a request and waits for its reply. When <paramref name="timeout"/> passes the worker's
    /// call is cancelled and, if it does not answer, the worker is given up on (a 500 "Request
    /// Timeout"); when <paramref name="cancellationToken"/> fires the call is cancelled and its reply
    /// (<c>RequestCancelled</c>) awaited, the worker kept.
    /// </summary>
    public async Task<WorkerProtocol.Reply> AskAsync(WorkerProtocol.Request request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        long id = Interlocked.Increment(ref _nextId);
        request = request with { Id = id };
        var waiting = new TaskCompletionSource<WorkerProtocol.Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = waiting;
        if (_process.HasExited)
        {
            throw new WorkerEndedException("the worker process has exited");
        }

        await SendAsync(request, CancellationToken.None).ConfigureAwait(false);

        using var timer = new CancellationTokenSource();
        Task delay = timeout == Timeout.InfiniteTimeSpan ? Task.Delay(Timeout.Infinite, timer.Token) : Task.Delay(timeout, timer.Token);
        Task cancelled = cancellationToken.CanBeCanceled ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.Delay(Timeout.Infinite, timer.Token);
        Task finished = await Task.WhenAny(waiting.Task, delay, cancelled).ConfigureAwait(false);
        timer.Cancel();
        if (finished == waiting.Task)
        {
            return await waiting.Task.ConfigureAwait(false);
        }

        // The function is asked to stop at its next statement and given a moment to answer.
        try
        {
            await SendAsync(new WorkerProtocol.Request(Interlocked.Increment(ref _nextId), "cancel", Target: id), CancellationToken.None).ConfigureAwait(false);
            Task grace = Task.Delay(TimeSpan.FromSeconds(5));
            if (await Task.WhenAny(waiting.Task, grace).ConfigureAwait(false) == waiting.Task)
            {
                return await waiting.Task.ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or WorkerEndedException)
        {
            // The worker is gone either way.
        }

        _pending.TryRemove(id, out _);
        // A cancelled request whose worker does not answer is treated as one past its time: the worker is restarted.
        return new WorkerProtocol.Reply(id, 500, "Request Timeout The request ran past the instance's --request-timeout; its worker is restarted.");
    }

    private async Task SendAsync(WorkerProtocol.Request request, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WorkerProtocol.WriteAsync(_input, request, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new WorkerEndedException(ex.Message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await SendAsync(new WorkerProtocol.Request(Interlocked.Increment(ref _nextId), "shutdown"), CancellationToken.None).ConfigureAwait(false);
                using var waited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(waited.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or WorkerEndedException)
        {
            // Then it is killed.
        }

        await KillAsync().ConfigureAwait(false);
    }

    public async Task KillAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        foreach (TaskCompletionSource<WorkerProtocol.Reply> waiting in _pending.Values)
        {
            waiting.TrySetException(new WorkerEndedException("the worker was ended"));
        }

        _process.Dispose();
    }
}
