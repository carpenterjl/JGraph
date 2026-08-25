using System.Runtime.InteropServices;

namespace JGraph.Numerics.LinearAlgebra.Native;

/// <summary>
/// Finds, loads and configures the bundled OpenBLAS. The library ships in the application's
/// <c>native\</c> subfolder (copied there by JGraph.Numerics' build item), so a resolver maps the
/// import name to that path; any failure — missing file, wrong architecture, a blocked load —
/// degrades to a not-loaded status whose description says exactly why, and the managed kernels
/// carry on. The thread count is fixed once at load (env <c>JGRAPH_BLAS_THREADS</c> overrides the
/// default of ProcessorCount capped at 16), which keeps native results identical run to run.
/// </summary>
internal static class OpenBlasLoader
{
    private static readonly Lazy<LoadStatus> Load = new(Initialize);
    private static nint _handle;

    /// <summary>The load outcome; touching it triggers the one-time load attempt.</summary>
    internal static LoadStatus Status => Load.Value;

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

            int threads = ThreadCountFromEnvironment() ?? DefaultThreadCount();
            OpenBlasNative.SetNumThreads(threads);
            return new LoadStatus(true, $"{ConfigSummary()} (native, {OpenBlasNative.GetNumThreads()} threads)");
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
    /// One thread per physical core, capped at 16. Hyperthread siblings share the multiply-add
    /// units a blocked factorization spends all its time in, so counting logical processors makes
    /// <c>dgetrf</c> and <c>dgetri</c> measurably slower rather than faster — see
    /// <see cref="ProcessorTopology"/> for the numbers.
    /// </summary>
    private static int DefaultThreadCount() =>
        Math.Clamp(ProcessorTopology.PhysicalCoreCount() ?? Environment.ProcessorCount, 1, 16);

    private static int? ThreadCountFromEnvironment() =>
        int.TryParse(Environment.GetEnvironmentVariable("JGRAPH_BLAS_THREADS"), out int count) && count > 0
            ? Math.Min(count, 64)
            : null;
}
