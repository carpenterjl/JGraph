namespace JGraph.Scripting;

/// <summary>
/// Runs script work on a dedicated thread with a deliberately large stack. The interpreter spends
/// roughly fifteen native frames (a few kilobytes) per script-level call, so a recursive script at
/// MATLAB-ish depths dies of a real stack overflow — uncatchable, process-killing — long before the
/// interpreter's own recursion limit can fire on a 1 MB thread-pool stack. Every script entry point
/// (engines, sessions, the debugger) funnels through here so the recursion limit is always the thing
/// that trips first, and trips as an ordinary catchable script error.
/// </summary>
internal static class ScriptThread
{
    /// <summary>
    /// 16 MB: the interpreter's recursion limit needs a few kilobytes of native stack per script
    /// frame, so this leaves the limit an order of magnitude of headroom.
    /// </summary>
    private const int StackBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Runs <paramref name="work"/> on a fresh big-stack background thread and completes the returned
    /// task with its result. Mirrors <see cref="Task.Run{TResult}(Func{TResult})"/> semantics: an
    /// exception faults the task, and a <paramref name="cancellationToken"/> already cancelled at call
    /// time cancels the task without starting the work.
    /// </summary>
    public static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
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
        }, StackBytes)
        {
            IsBackground = true,
            Name = "JGraph script",
        };
        thread.Start();
        return completion.Task;
    }
}
