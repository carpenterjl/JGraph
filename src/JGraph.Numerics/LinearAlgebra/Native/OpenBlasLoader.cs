using System.Runtime.InteropServices;

namespace JGraph.Numerics.LinearAlgebra.Native;

/// <summary>
/// Finds, loads and configures the bundled OpenBLAS. The library ships in the application's
/// <c>native\</c> subfolder (copied there by JGraph.Numerics' build item), so a resolver maps the
/// import name to that path; any failure — missing file, wrong architecture, a blocked load —
/// degrades to a not-loaded status whose description says exactly why, and the managed kernels
/// carry on. The thread ceiling is fixed once at load — one per performance core, capped at 16, or
/// <c>JGRAPH_BLAS_THREADS</c> — and <see cref="NativeThreads"/> gives each call its share of that
/// ceiling by routine and size, which keeps native results identical run to run.
/// </summary>
internal static class OpenBlasLoader
{
    private static readonly Lazy<LoadStatus> Load = new(Initialize);
    private static nint _handle;

    /// <summary>The load outcome; touching it triggers the one-time load attempt.</summary>
    internal static LoadStatus Status => Load.Value;

    /// <summary>The most threads any native call may take; <see cref="NativeThreads"/> sizes each call under it.</summary>
    internal static int MaxThreads { get; private set; } = 1;

    /// <summary>Whether the count came from the environment, in which case every call takes it as given.</summary>
    internal static bool PinnedByEnvironment { get; private set; }

    internal sealed record LoadStatus(bool Loaded, string Description);

    private static LoadStatus Initialize()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return new LoadStatus(false,
                $"managed fallback: the process is {RuntimeInformation.ProcessArchitecture} and the bundled OpenBLAS is x64-only");
        }

        string expected = Path.Combine(AppContext.BaseDirectory, "native", "libopenblas.dll");
        try
        {
            if (!NativeLibrary.TryLoad(expected, out _handle)
                && !NativeLibrary.TryLoad(OpenBlasNative.Library, typeof(OpenBlasLoader).Assembly, null, out _handle))
            {
                return new LoadStatus(false,
                    $"managed fallback: libopenblas.dll was not found beside the application (expected at {expected})");
            }

            NativeLibrary.SetDllImportResolver(typeof(OpenBlasLoader).Assembly, Resolve);

            int? asked = ThreadCountFromEnvironment();
            PinnedByEnvironment = asked is not null;
            MaxThreads = asked ?? DefaultThreadCount();
            OpenBlasNative.SetNumThreads(MaxThreads);
            MaxThreads = OpenBlasNative.GetNumThreads();
            string threads = PinnedByEnvironment
                ? $"{MaxThreads} threads"
                : $"up to {MaxThreads} threads, sized per call";
            return new LoadStatus(true, $"{ConfigSummary()} (native, {threads})");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException)
        {
            _handle = 0;
            return new LoadStatus(false, $"managed fallback: OpenBLAS failed to load ({ex.Message})");
        }
    }

    private static nint Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath) =>
        libraryName == OpenBlasNative.Library ? _handle : 0;

    /// <summary>"OpenBLAS 0.3.34" from the build-configuration string, or a plain fallback.</summary>
    private static string ConfigSummary()
    {
        string config = Marshal.PtrToStringAnsi(OpenBlasNative.GetConfig())?.Trim() ?? string.Empty;
        string[] tokens = config.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 && tokens[0] == "OpenBLAS" ? $"{tokens[0]} {tokens[1]}" : "OpenBLAS";
    }

    /// <summary>
    /// One thread per performance core, capped at 16. Hyperthread siblings share the multiply-add
    /// units a blocked factorization spends all its time in, so counting logical processors makes
    /// <c>dgetrf</c> and <c>dgetri</c> measurably slower rather than faster, and an efficiency core
    /// given an equal share of a panel update holds the whole call at its pace — see
    /// <see cref="ProcessorTopology"/> for the numbers.
    /// </summary>
    private static int DefaultThreadCount() =>
        Math.Clamp(ProcessorTopology.PerformanceCoreCount() ?? Environment.ProcessorCount, 1, 16);

    /// <summary>
    /// <c>JGRAPH_BLAS_THREADS</c> if this machine wants the native side counted differently from the
    /// managed one; otherwise <c>JGRAPH_THREADS</c>, which is the single answer to "how much of this
    /// machine may JGraph's arithmetic take" and is what <see cref="ParallelKernels"/> reads too.
    /// </summary>
    private static int? ThreadCountFromEnvironment() =>
        Asked("JGRAPH_BLAS_THREADS") ?? Asked("JGRAPH_THREADS");

    private static int? Asked(string variable) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out int count) && count > 0
            ? Math.Min(count, 64)
            : null;
}
