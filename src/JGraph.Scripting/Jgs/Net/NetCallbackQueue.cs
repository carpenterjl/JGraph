using System.Collections.Concurrent;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// Script work that .NET asked for from a thread other than the script's (interop plan, stage 5,
/// ADR 0178): a listener's callback for an event raised on a thread-pool thread, or a delegate made
/// from a function handle that such a thread invoked. The work waits here until the script thread
/// reaches a drain point — <c>pause</c> with a positive time, <c>drawnow</c> (not
/// <c>'nocallbacks'</c>), <c>getframe</c>, and the app's idle prompt — and runs there, on the script
/// thread; never between two statements, never during a busy loop, never at <c>pause(0)</c>
/// (R2025b, net_events_delegates and probe5f). An event raised on the script thread itself, inside a
/// .NET call the script made, does not come here: it runs at once.
/// </summary>
/// <remarks>
/// <para>
/// There is one queue per script thread, found through a thread-static: a listener or a delegate
/// takes the queue of the thread that made it, and a drain point empties the queue of the thread it
/// runs on. A test lane, a batch run and the app's script thread each have their own.
/// </para>
/// <para>
/// <b>Deadlock.</b> A delegate invoked on another thread blocks that thread until the script thread
/// drains it. When the script thread is itself blocked inside a .NET call — waiting, most likely, on
/// that very thread — no drain can come, and R2025b hangs for good. JGraph counts the script
/// thread's depth in .NET calls (zero again while a callback runs inline) and, once the waiting
/// delegate has seen the script thread blocked for <see cref="DeadlockTimeout"/>, withdraws the
/// request and fails the delegate with <see cref="NetDelegateDeadlockException"/>, which the call
/// the script is blocked in reports as <c>JGraph:NET:DelegateDeadlock</c> (a pre-registered divergence).
/// </para>
/// </remarks>
internal sealed class NetCallbackQueue
{
    [ThreadStatic]
    private static NetCallbackQueue? t_current;

    /// <summary>Requests waiting in every queue, for a host deciding whether an idle drain is worth starting.</summary>
    private static int s_pending;

    private readonly ConcurrentQueue<Request> _requests = new();
    private int _netDepth;

    private NetCallbackQueue(int thread) => ScriptThreadId = thread;

    /// <summary>How long a delegate waits on a script thread blocked in .NET before it gives up.</summary>
    public static TimeSpan DeadlockTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Whether any script thread has .NET work waiting.</summary>
    public static bool AnyPending => Volatile.Read(ref s_pending) > 0;

    /// <summary>The queue of the calling thread, made on first use; the calling thread is its script thread.</summary>
    public static NetCallbackQueue ForCurrentThread() => t_current ??= new NetCallbackQueue(Environment.CurrentManagedThreadId);

    /// <summary>Runs what the calling thread's queue holds, when it has one: what every drain point calls.</summary>
    public static void DrainCurrent() => t_current?.Drain();

    /// <summary>The thread that makes and drains this queue's work.</summary>
    public int ScriptThreadId { get; }

    /// <summary>Whether the caller is on the script thread, where .NET's callbacks run at once.</summary>
    public bool OnScriptThread => Environment.CurrentManagedThreadId == ScriptThreadId;

    /// <summary>Whether the script thread is inside a .NET call and running no script of its own.</summary>
    public bool BlockedInNet => Volatile.Read(ref _netDepth) > 0;

    /// <summary>
    /// Marks the script thread as inside a .NET call until the result is disposed; a call that is
    /// not on the script thread (a callback's own .NET call from a drained request is) marks nothing.
    /// </summary>
    public NetCallScope EnterNetCall()
    {
        if (!OnScriptThread)
        {
            return default;
        }

        Interlocked.Increment(ref _netDepth);
        return new NetCallScope(this);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the script thread as a callback: script code, so the thread is
    /// not blocked in .NET while it runs, whatever .NET call it came from.
    /// </summary>
    public T RunInline<T>(Func<T> work)
    {
        int depth = Interlocked.Exchange(ref _netDepth, 0);
        try
        {
            return work();
        }
        finally
        {
            Interlocked.Exchange(ref _netDepth, depth);
        }
    }

    /// <summary>Queues work that no thread waits for — an event raised on another thread.</summary>
    public void Post(Action work) => Enqueue(new Request(work));

    /// <summary>
    /// Queues <paramref name="work"/> and blocks the calling (foreign) thread until the script thread
    /// has run it, answering what it answered or rethrowing what it threw. Gives up with
    /// <see cref="NetDelegateDeadlockException"/> when the script thread stays blocked in .NET.
    /// </summary>
    public object? Call(Func<object?> work, string what)
    {
        var request = new Request(work);
        Enqueue(request);
        long blockedSince = -1;
        while (!request.Done.Wait(25))
        {
            if (!BlockedInNet)
            {
                blockedSince = -1;
                continue;
            }

            long now = Environment.TickCount64;
            if (blockedSince < 0)
            {
                blockedSince = now;
            }
            else if (now - blockedSince >= DeadlockTimeout.TotalMilliseconds && request.TryWithdraw())
            {
                throw new NetDelegateDeadlockException(what);
            }
        }

        if (request.Fault is { } fault)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
        }

        return request.Answer;
    }

    /// <summary>
    /// Runs the requests present when it starts, oldest first, on the script thread. One queued by a
    /// callback it runs waits for the next drain point, as a timer re-armed in its own callback does.
    /// </summary>
    public void Drain()
    {
        if (!OnScriptThread)
        {
            return;
        }

        for (int budget = _requests.Count; budget > 0 && _requests.TryDequeue(out Request? next); budget--)
        {
            Interlocked.Decrement(ref s_pending);
            if (next.TryStart())
            {
                RunInline(() =>
                {
                    next.Run();
                    return 0;
                });
            }
        }
    }

    private void Enqueue(Request request)
    {
        _requests.Enqueue(request);
        Interlocked.Increment(ref s_pending);
        ScriptEventQueue.PokePump();
    }

    /// <summary>The script thread's mark for one .NET call; disposing it ends the call.</summary>
    public readonly struct NetCallScope(NetCallbackQueue? queue) : IDisposable
    {
        public void Dispose()
        {
            if (queue is not null)
            {
                Interlocked.Decrement(ref queue._netDepth);
            }
        }
    }

    /// <summary>One piece of queued work and, for a blocking call, its answer.</summary>
    private sealed class Request
    {
        private readonly Func<object?> _work;
        private int _state; // 0 waiting, 1 started, 2 withdrawn

        public Request(Action work) : this(() =>
        {
            work();
            return null;
        })
        {
        }

        public Request(Func<object?> work) => _work = work;

        public ManualResetEventSlim Done { get; } = new();

        public object? Answer { get; private set; }

        public Exception? Fault { get; private set; }

        public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        public bool TryWithdraw() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;

        public void Run()
        {
            try
            {
                Answer = _work();
            }
            catch (Exception fault)
            {
                // The waiting thread rethrows it; Stop and exit also unwind the drain, as themselves.
                Fault = fault;
                Exception script = fault is NetScriptFault { InnerException: { } inner } ? inner : fault;
                if (script is OperationCanceledException || ScriptExitException.Unwrap(script) is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(script).Throw();
                }
            }
            finally
            {
                Done.Set();
            }
        }
    }
}

/// <summary>
/// A delegate made from a function handle was invoked on another thread while the script thread was
/// blocked inside a .NET call, so nothing could run it (ADR 0178's divergence: R2025b hangs).
/// </summary>
internal sealed class NetDelegateDeadlockException(string what) : Exception(
    $"A function handle passed to .NET as {what} was called on another thread while the script was waiting inside a .NET call, "
    + "so it could never run. .NET code that calls back into a script from another thread must not be waited on; let the script reach pause or drawnow instead.")
{
    /// <summary>The identifier the script sees.</summary>
    public const string Identifier = "JGraph:NET:DelegateDeadlock";
}
