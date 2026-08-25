using BenchmarkDotNet.Attributes;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Benchmarks;

/// <summary>
/// The orthogonal half of the dense ladder (M90): QR, the singular value decomposition, and the two
/// eigensolvers — managed against native, at the head-to-head suite's sizes. Every one of these
/// overwrites what it is given, so each iteration starts from a pristine copy set up outside the
/// measurement.
/// </summary>
/// <remarks>
/// The managed rows for <c>svd</c> and general <c>eig</c> are here to be looked at rather than
/// admired: one-sided Jacobi sweeps and per-eigenvalue inverse iteration are asymptotically worse
/// than what LAPACK does, not merely slower by a constant, and the numbers say by how much. The
/// sizes match d01 (eig 400, svd 800, qr 1200) so the ratio here is the ratio the suite reports.
/// </remarks>
[MemoryDiagnoser]
public class OrthogonalFactorizationBenchmarks
{
    private readonly ManagedLinalg _managed = new();
    private OpenBlasLinalg? _native;
    private double[] _pristine = [];      // a well-conditioned general matrix
    private double[] _symmetric = [];
    private double[] _work = [];
    private double[] _tau = [];
    private double[] _values = [];
    private double[] _left = [];
    private double[] _right = [];
    private double[] _imaginary = [];
    private int[] _pivots = [];

    [Params(200, 400, 800)]
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        _native = LinalgProvider.NativeAvailable ? new OpenBlasLinalg() : null;
        _pristine = new double[N * N];
        _symmetric = new double[N * N];
        _work = new double[N * N];
        _tau = new double[N];
        _values = new double[N];
        _left = new double[N * N];
        _right = new double[N * N];
        _imaginary = new double[N];
        _pivots = new int[N];

        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                _pristine[(c * N) + r] = System.Math.Sin(0.7 * (r + 1)) + System.Math.Cos(1.3 * (c + 1))
                    + (r == c ? 2.0 * N : 0);
            }
        }

        for (int c = 0; c < N; c++)
        {
            for (int r = c; r < N; r++)
            {
                double value = System.Math.Cos(0.4 * ((r * c) + 1)) + (r == c ? 2.0 * N : 0);
                _symmetric[(c * N) + r] = value;
                _symmetric[(r * N) + c] = value;
            }
        }
    }

    [IterationSetup(Targets = [nameof(ManagedQr), nameof(NativeQr), nameof(ManagedPivotedQr),
        nameof(NativePivotedQr), nameof(ManagedSvd), nameof(NativeSvd),
        nameof(ManagedGeneralEigen), nameof(NativeGeneralEigen)])]
    public void ResetGeneral() => System.Array.Copy(_pristine, _work, _work.Length);

    [IterationSetup(Targets = [nameof(ManagedSymmetricEigen), nameof(NativeSymmetricEigen)])]
    public void ResetSymmetric() => System.Array.Copy(_symmetric, _work, _work.Length);

    [Benchmark(Baseline = true)]
    public int ManagedQr() => _managed.Geqrf(N, N, _work, N, _tau);

    [Benchmark]
    public int NativeQr() => _native?.Geqrf(N, N, _work, N, _tau) ?? 0;

    [Benchmark]
    public int ManagedPivotedQr() => _managed.Geqp3(N, N, _work, N, _pivots, _tau);

    [Benchmark]
    public int NativePivotedQr() => _native?.Geqp3(N, N, _work, N, _pivots, _tau) ?? 0;

    [Benchmark]
    public int ManagedSvd() =>
        _managed.Gesdd(SvdVectors.None, N, N, _work, N, _values, [], 1, [], 1);

    [Benchmark]
    public int NativeSvd() =>
        _native?.Gesdd(SvdVectors.None, N, N, _work, N, _values, [], 1, [], 1) ?? 0;

    [Benchmark]
    public int ManagedSymmetricEigen() =>
        _managed.Syevd(vectors: true, lower: true, N, _work, N, _values);

    [Benchmark]
    public int NativeSymmetricEigen() =>
        _native?.Syevd(vectors: true, lower: true, N, _work, N, _values) ?? 0;

    [Benchmark]
    public int ManagedGeneralEigen() =>
        _managed.Geev(vectors: true, N, _work, N, _values, _imaginary, _right, N);

    [Benchmark]
    public int NativeGeneralEigen() =>
        _native?.Geev(vectors: true, N, _work, N, _values, _imaginary, _right, N) ?? 0;

    /// <summary>Expanding the reflectors into Q, which is what a two-output <c>qr</c> pays for.</summary>
    [Benchmark]
    public int NativeExpandQ()
    {
        System.Array.Copy(_pristine, _left, _left.Length);
        return _native?.Orgqr(N, N, N, _left, N, _tau) ?? 0;
    }
}
