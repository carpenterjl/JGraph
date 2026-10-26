using System.Collections.Concurrent;

namespace JGraph.ProductionServer;

/// <summary>
/// The instance's <c>auto_deploy</c> folder (compiler and SDK plan, stage K14): every <c>.ctf</c> in it is
/// deployed when the server starts, one that appears or changes is deployed while it runs, and one
/// that goes is retired, as MATLAB Production Server's auto-deploy does. An archive's component name
/// is its file name without the extension, the name its URL carries.
/// </summary>
internal sealed class ArchiveWatcher : IDisposable
{
    private readonly string _folder;
    private readonly WorkerPool _pool;
    private readonly Action<string> _log;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly CancellationTokenSource _ending = new();

    public ArchiveWatcher(string folder, WorkerPool pool, Action<string> log)
    {
        _folder = folder;
        _pool = pool;
        _log = log;
        Directory.CreateDirectory(folder);
        _watcher = new FileSystemWatcher(folder, "*.ctf")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Touched(e.FullPath);
        _watcher.Changed += (_, e) => Touched(e.FullPath);
        _watcher.Deleted += (_, e) => Touched(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Touched(e.OldFullPath);
            Touched(e.FullPath);
        };
    }

    /// <summary>The archives deployed, by component name.</summary>
    public ConcurrentDictionary<string, string> Deployed { get; } = new(StringComparer.Ordinal);

    /// <summary>Deploys what the folder holds, then watches it.</summary>
    public async Task StartAsync()
    {
        foreach (string file in Directory.GetFiles(_folder, "*.ctf").Order(StringComparer.OrdinalIgnoreCase))
        {
            await DeployAsync(file).ConfigureAwait(false);
        }

        _watcher.EnableRaisingEvents = true;
        _ = SettleAsync();
    }

    private void Touched(string path)
    {
        if (path.EndsWith(".ctf", StringComparison.OrdinalIgnoreCase))
        {
            _pending[path] = DateTime.UtcNow;
        }
    }

    /// <summary>Acts on a touched file once it has been still for half a second: a copy in progress is not deployed half-written.</summary>
    private async Task SettleAsync()
    {
        while (!_ending.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, _ending.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            foreach ((string path, DateTime when) in _pending.ToArray())
            {
                if (DateTime.UtcNow - when < TimeSpan.FromMilliseconds(500) || !_pending.TryRemove(path, out _))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(path))
                    {
                        await DeployAsync(path).ConfigureAwait(false);
                    }
                    else
                    {
                        await UndeployAsync(path).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
                {
                    _log($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
    }

    private async Task DeployAsync(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        await _one.WaitAsync().ConfigureAwait(false);
        try
        {
            string[]? functions = await _pool.DeployAsync(name, path).ConfigureAwait(false);
            if (functions is null)
            {
                _log($"{name}: not deployed");
                return;
            }

            Deployed[name] = path;
            _log($"{name}: deployed ({functions.Length} function(s))");
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task UndeployAsync(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        await _one.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Deployed.TryRemove(name, out _))
            {
                await _pool.UndeployAsync(name).ConfigureAwait(false);
                _log($"{name}: retired");
            }
        }
        finally
        {
            _one.Release();
        }
    }

    public void Dispose()
    {
        _ending.Cancel();
        _watcher.Dispose();
        _ending.Dispose();
    }
}
