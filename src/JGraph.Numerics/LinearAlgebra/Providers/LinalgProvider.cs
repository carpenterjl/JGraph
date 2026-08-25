using JGraph.Numerics.LinearAlgebra.Native;

using System.Numerics;

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

        public override int Getrf(int m, int n, Span<double> a, int lda, Span<int> ipiv) =>
            throw new InvalidOperationException(reason);

        public override void Getrs(bool transpose, int n, int nrhs,
            ReadOnlySpan<double> a, int lda, ReadOnlySpan<int> ipiv, Span<double> b, int ldb) =>
            throw new InvalidOperationException(reason);

        public override int Getri(int n, Span<double> a, int lda, ReadOnlySpan<int> ipiv) =>
            throw new InvalidOperationException(reason);

        public override double Gecon(int n, ReadOnlySpan<double> a, int lda, double anorm) =>
            throw new InvalidOperationException(reason);

        public override int Potrf(bool lower, int n, Span<double> a, int lda) =>
            throw new InvalidOperationException(reason);

        public override int Trtrs(bool lower, bool transpose, int n, int nrhs,
            ReadOnlySpan<double> a, int lda, Span<double> b, int ldb) =>
            throw new InvalidOperationException(reason);

        public override int Gels(int m, int n, int nrhs, Span<double> a, int lda, Span<double> b, int ldb) =>
            throw new InvalidOperationException(reason);

        public override int Geqrf(int m, int n, Span<double> a, int lda, Span<double> tau) =>
            throw new InvalidOperationException(reason);

        public override int Geqp3(int m, int n, Span<double> a, int lda, Span<int> jpvt, Span<double> tau) =>
            throw new InvalidOperationException(reason);

        public override int Orgqr(int m, int n, int k, Span<double> a, int lda, ReadOnlySpan<double> tau) =>
            throw new InvalidOperationException(reason);

        public override int Ormqr(bool leftSide, bool transpose, int m, int n, int k,
            ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> tau, Span<double> c, int ldc) =>
            throw new InvalidOperationException(reason);

        public override int Gesdd(SvdVectors job, int m, int n, Span<double> a, int lda,
            Span<double> s, Span<double> u, int ldu, Span<double> vt, int ldvt) =>
            throw new InvalidOperationException(reason);

        public override int Gesvd(SvdVectors job, int m, int n, Span<double> a, int lda,
            Span<double> s, Span<double> u, int ldu, Span<double> vt, int ldvt) =>
            throw new InvalidOperationException(reason);

        public override int Syevd(bool vectors, bool lower, int n, Span<double> a, int lda, Span<double> w) =>
            throw new InvalidOperationException(reason);

        public override int Geev(bool vectors, int n, Span<double> a, int lda,
            Span<double> wr, Span<double> wi, Span<double> vr, int ldvr) =>
            throw new InvalidOperationException(reason);

        public override int Ggev(bool vectors, int n, Span<double> a, int lda, Span<double> b, int ldb,
            Span<double> alphar, Span<double> alphai, Span<double> beta, Span<double> vr, int ldvr) =>
            throw new InvalidOperationException(reason);

        public override int Sygvd(bool vectors, bool lower, int n, Span<double> a, int lda,
            Span<double> b, int ldb, Span<double> w) =>
            throw new InvalidOperationException(reason);

        public override int Gees(bool vectors, int n, Span<double> a, int lda,
            Span<double> wr, Span<double> wi, Span<double> vs, int ldvs) =>
            throw new InvalidOperationException(reason);

        public override int Gges(bool vectors, int n, Span<double> a, int lda, Span<double> b, int ldb,
            Span<double> alphar, Span<double> alphai, Span<double> beta,
            Span<double> vsl, int ldvsl, Span<double> vsr, int ldvsr) =>
            throw new InvalidOperationException(reason);

        public override int Trsen(ReadOnlySpan<bool> select, int n, Span<double> t, int ldt,
            Span<double> q, int ldq, Span<double> wr, Span<double> wi) =>
            throw new InvalidOperationException(reason);

        public override void Zgemm(int m, int n, int k, ReadOnlySpan<Complex> a, int lda,
            ReadOnlySpan<Complex> b, int ldb, Span<Complex> c, int ldc) =>
            throw new InvalidOperationException(reason);

        public override int Zgetrf(int m, int n, Span<Complex> a, int lda, Span<int> ipiv) =>
            throw new InvalidOperationException(reason);

        public override void Zgetrs(int n, int nrhs, ReadOnlySpan<Complex> a, int lda,
            ReadOnlySpan<int> ipiv, Span<Complex> b, int ldb) =>
            throw new InvalidOperationException(reason);

        public override int Zgetri(int n, Span<Complex> a, int lda, ReadOnlySpan<int> ipiv) =>
            throw new InvalidOperationException(reason);

        public override int Zgeev(bool vectors, int n, Span<Complex> a, int lda,
            Span<Complex> w, Span<Complex> vr, int ldvr) =>
            throw new InvalidOperationException(reason);

        public override int Zgesdd(SvdVectors job, int m, int n, Span<Complex> a, int lda,
            Span<double> s, Span<Complex> u, int ldu, Span<Complex> vt, int ldvt) =>
            throw new InvalidOperationException(reason);
    }
}
