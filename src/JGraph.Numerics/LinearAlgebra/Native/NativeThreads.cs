using System.Diagnostics;
using System.Globalization;

namespace JGraph.Numerics.LinearAlgebra.Native;

/// <summary>
/// How many OpenBLAS threads one native call gets, decided per call from the routine and its size.
/// </summary>
/// <remarks>
/// <para>
/// A single count fixed at load was wrong for every LAPACK driver at every size the head2head_v3
/// gap analysis measured. The pthread build's barrier costs more than a small factorization does:
/// <c>dsyevd</c> at n = 400 runs in 3.9 ms on one thread and 14.5 ms on fourteen, <c>dgetrf</c> on
/// the same matrix in 1.7 ms against 10.7. The crossover is routine-dependent — a blocked
/// factorization pays off from about n = 800, a symmetric eigensolver later still, and only the
/// level-3 products want the whole machine from a few hundred up — so the count is a pure function
/// of what is being called and how big it is. Pure, so that a given call gets the same thread count
/// on every run of every machine of the same shape. This removes one source of variability;
/// numerical results can still vary with the native library, hardware and reduction order.
/// </para>
/// <para>
/// <c>JGRAPH_BLAS_THREADS</c> (or <c>JGRAPH_THREADS</c>) pins the count for the process instead,
/// exactly as before, for a machine that disagrees with the policy.
/// </para>
/// <para>
/// The count is set through <c>openblas_set_num_threads</c>, which is a process-wide switch and not
/// safe to flip while another call is in flight, so every native call holds one lock for its
/// duration. The lock makes the setting and the call one step, at the cost of serializing
/// independent callers through this provider.
/// </para>
/// </remarks>
internal static class NativeThreads
{
    private static readonly object Gate = new();
    private static int _current;

    /// <summary>
    /// Where a per-call trace line goes, or null for no tracing. <c>JGRAPH_NATIVE_TRACE=1</c> sends
    /// <c>native|routine|threads=n|size=s|seconds</c> to standard error for every native call; the
    /// stopwatch runs inside the lock the call already holds, so it costs the call nothing it was
    /// not already paying. A test may set a sink of its own.
    /// </summary>
    internal static Action<string>? Trace =
        Environment.GetEnvironmentVariable("JGRAPH_NATIVE_TRACE") == "1"
            ? static line => Console.Error.WriteLine(line)
            : null;

    /// <summary>What a routine spends its time in, which decides where threads start to pay.</summary>
    internal enum Work
    {
        /// <summary>A level-3 product — <c>dgemm</c>, <c>dsyrk</c> — where the work is m·n·k flops.</summary>
        Level3,

        /// <summary>A blocked factorization or the solves and inverses built on one.</summary>
        Factor,

        /// <summary>An eigenvalue, Schur or singular-value driver: thousands of short level-2 calls.</summary>
        Spectral,
    }

    /// <summary>
    /// Takes the native lock and sets the thread count for one call. Dispose to release. For
    /// <see cref="Work.Level3"/> <paramref name="size"/> is the flop count m·n·k; otherwise it is the
    /// smaller matrix dimension.
    /// </summary>
    internal static Scope Use(Work work, long size, [System.Runtime.CompilerServices.CallerMemberName] string routine = "")
    {
        Monitor.Enter(Gate);
        try
        {
            int wanted = CountFor(work, size);
            if (wanted != _current)
            {
                OpenBlasNative.SetNumThreads(wanted);
                _current = wanted;
            }

            return Trace is null ? default : new Scope(routine, wanted, size, Stopwatch.GetTimestamp());
        }
        catch
        {
            Monitor.Exit(Gate);
            throw;
        }
    }

    /// <summary>The count the policy gives a call: pinned by environment, or by routine and size.</summary>
    internal static int CountFor(Work work, long size)
    {
        int full = OpenBlasLoader.MaxThreads;
        if (OpenBlasLoader.PinnedByEnvironment || full <= 1)
        {
            return full;
        }

        return work switch
        {
            // dgemm at n = 200 (8M flops) already prefers the machine; below that the barrier wins.
            Work.Level3 => size < 2_000_000 ? 1 : full,

            // dgetrf/dpotrf/dgeqrf: one thread to n ≈ 500, a few to n ≈ 1000, all of them past that.
            Work.Factor => size < 512 ? 1 : size < 1024 ? Math.Min(4, full) : full,

            // dsyevd/dgesdd/dgeev: the reductions to tridiagonal and bidiagonal form are level-2
            // work, and every one of their thousands of calls crosses the barrier.
            _ => size < 512 ? 1 : size < 1024 ? Math.Min(2, full) : size < 2048 ? Math.Min(4, full) : full,
        };
    }

    /// <summary>Releases the native lock taken by <see cref="Use"/>, reporting the call when tracing.</summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly string? _routine;
        private readonly int _threads;
        private readonly long _size;
        private readonly long _started;

        internal Scope(string routine, int threads, long size, long started)
        {
            _routine = routine;
            _threads = threads;
            _size = size;
            _started = started;
        }

        public void Dispose()
        {
            if (_routine is not null && Trace is { } sink)
            {
                double seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
                sink(string.Create(
                    CultureInfo.InvariantCulture,
                    $"native|{_routine}|threads={_threads}|size={_size}|{seconds:F6}"));
            }

            Monitor.Exit(Gate);
        }
    }
}
