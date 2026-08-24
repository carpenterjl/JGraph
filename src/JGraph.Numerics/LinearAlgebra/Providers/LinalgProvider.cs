using JGraph.Numerics.LinearAlgebra.Native;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The active dense linear-algebra backend. The default is native OpenBLAS when the bundled
/// library loads and the managed kernels otherwise; <c>JGRAPH_LINALG=managed|native</c> forces the
/// choice, mirroring <c>JGRAPH_JGS_PACKED</c> as a parity-test lever. Forcing <c>native</c> when
/// the library is unavailable selects a backend that throws on first use — a CI lane asked to test
/// the native path can never silently test the managed one twice.
/// </summary>
public static class LinalgProvider
{
    private static readonly ManagedLinalg Managed = new();
    private static DenseLinalg _current = CreateDefault();

    /// <summary>The backend every dense operation funnels through.</summary>
    public static DenseLinalg Current => _current;

    /// <summary>Whether the native library loaded (tests skip native assertions when it did not).</summary>
    public static bool NativeAvailable => OpenBlasLoader.Status.Loaded;

    /// <summary>A one-line status for diagnostics and <c>version('-blas')</c>.</summary>
    public static string StatusReport => _current switch
    {
        OpenBlasLinalg => OpenBlasLoader.Status.Description,
        UnavailableLinalg unavailable => unavailable.Description,
        _ when Environment.GetEnvironmentVariable("JGRAPH_LINALG") == "managed" =>
            "managed kernels (forced by JGRAPH_LINALG=managed)",
        _ => OpenBlasLoader.Status.Loaded ? "managed kernels" : OpenBlasLoader.Status.Description,
    };

    /// <summary>
    /// Selects a backend explicitly (the test lever). Asking for OpenBLAS when it did not load
    /// throws with the load status rather than falling back.
    /// </summary>
    public static void Use(LinalgBackend backend)
    {
        _current = backend switch
        {
            LinalgBackend.Managed => Managed,
            LinalgBackend.OpenBlas when OpenBlasLoader.Status.Loaded => new OpenBlasLinalg(),
            LinalgBackend.OpenBlas => throw new InvalidOperationException(
                $"The OpenBLAS backend is unavailable: {OpenBlasLoader.Status.Description}."),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
    }

    private static DenseLinalg CreateDefault() =>
        Environment.GetEnvironmentVariable("JGRAPH_LINALG") switch
        {
            "managed" => Managed,
            "native" => OpenBlasLoader.Status.Loaded
                ? new OpenBlasLinalg()
                : new UnavailableLinalg($"JGRAPH_LINALG=native was set, but {OpenBlasLoader.Status.Description}"),
            _ => OpenBlasLoader.Status.Loaded ? new OpenBlasLinalg() : Managed,
        };

    /// <summary>The refuse-to-run backend behind a forced-but-missing native request.</summary>
    private sealed class UnavailableLinalg(string reason) : DenseLinalg
    {
        public override bool IsNative => true;

        public override string Description => reason;

        public override void Gemm(bool transA, bool transB, int m, int n, int k,
            ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> b, int ldb, Span<double> c, int ldc) =>
            throw new InvalidOperationException(reason);

        public override void Syrk(bool transposeFirst, int n, int k,
            ReadOnlySpan<double> a, int lda, Span<double> c, int ldc) =>
            throw new InvalidOperationException(reason);
    }
}
