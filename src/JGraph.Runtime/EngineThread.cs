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

    /// <summary>
    /// Queues <paramref name="work"/> that nobody waits for (stage K5): a window's report about a
    /// component that is not the live one, or a poke for <see cref="Idle"/>. Nothing runs once the
    /// runtime has terminated; an exception is reported on standard error.
    /// </summary>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            _queue.Add(work);
        }
        catch (InvalidOperationException)
        {
            // The runtime has terminated: there is no session left to run it in.
        }
    }

    /// <summary>
    /// What the engine does between calls (stage K5): called on the engine thread after each item of
    /// work, and again when the time it answered has passed. It answers how long the engine may
    /// sleep before it is wanted again, or null for until work arrives.
    /// </summary>
    public Func<TimeSpan?>? Idle { get; set; }

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
        TimeSpan? sleep = null;
        while (true)
        {
            Action? work;
            try
            {
                if (!_queue.TryTake(out work, sleep ?? Timeout.InfiniteTimeSpan))
                {
                    if (_queue.IsCompleted)
                    {
                        return;
                    }

                    work = null; // the idle hook's time has come
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (work is not null)
            {
                Guarded(work);
            }

            // After every item, not only when the queue runs dry: a client calling without pause
            // must not starve its windows' callbacks, and one queued before a call runs before it.
            sleep = null;
            if (Idle is { } idle)
            {
                Guarded(() => sleep = idle());
                if (sleep < TimeSpan.Zero)
                {
                    sleep = TimeSpan.Zero;
                }
            }
        }
    }

    /// <summary>Runs posted work, whose failure has nobody to go to but standard error.</summary>
    private static void Guarded(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("JGraph Runtime: " + ex.Message);
        }
    }
}
