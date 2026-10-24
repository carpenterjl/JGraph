using System.Collections.Concurrent;

namespace JGraph.Runtime;

/// <summary>
/// The one thread every component's work runs on, with the 16 MB stack the interpreter is written
/// for. Work from any thread is queued and runs in order, one item at a time: R2025b's runtime
/// serializes calls the same way, across instances and across components (<c>k4_client</c>: two
/// one-second calls from two threads took two seconds either way).
/// </summary>
internal sealed class EngineThread : IDisposable
{
    private const int StackBytes = 16 * 1024 * 1024;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public EngineThread()
    {
        _thread = new Thread(Pump, StackBytes) { IsBackground = true, Name = "JGraph Runtime engine" };
        _thread.Start();
    }

    /// <summary>Whether the caller is already on the engine thread.</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>
    /// Runs <paramref name="work"/> on the engine thread after everything queued before it. Work
    /// queued from the engine thread itself runs at once, since waiting for it there could never end.
    /// A token cancelled before the work starts cancels the task without running it.
    /// </summary>
    public Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsCurrent)
        {
            try
            {
                return Task.FromResult(work());
            }
            catch (OperationCanceledException cancelled)
            {
                return Task.FromCanceled<T>(cancelled.CancellationToken.IsCancellationRequested ? cancelled.CancellationToken : new CancellationToken(true));
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(work());
                }
                catch (OperationCanceledException cancelled)
                {
                    completion.TrySetCanceled(cancelled.CancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(EngineThread), "The JGraph Runtime has terminated.");
        }

        return completion.Task;
    }

    /// <summary>Runs <paramref name="work"/> on the engine thread and waits for it.</summary>
    public T Invoke<T>(Func<T> work, CancellationToken cancellationToken = default) =>
        Run(work, cancellationToken).GetAwaiter().GetResult();

    /// <summary>Lets the work already queued finish, then ends the thread.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!IsCurrent)
        {
            _thread.Join();
            _queue.Dispose();
        }
    }

    private void Pump()
    {
        foreach (Action work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
