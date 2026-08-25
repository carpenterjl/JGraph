using BenchmarkDotNet.Attributes;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Benchmarks;

/// <summary>
/// The factorization half of the dense ladder (M89): LU, the solve against it, the inverse, and
/// Cholesky — managed against native, at the head-to-head suite's sizes. Every one of these
/// overwrites what it is given, so each iteration starts from a pristine copy; the copy is set up
/// outside the measurement.
/// </summary>
[MemoryDiagnoser]
public class DenseFactorizationBenchmarks
{
    private readonly ManagedLinalg _managed = new();
    private OpenBlasLinalg? _native;
    private double[] _pristine = [];      // a well-conditioned general matrix
    private double[] _definite = [];      // symmetric and diagonally dominant
    private double[] _work = [];
    private double[] _rhs = [];
    private int[] _pivots = [];

    [Params(400, 1000, 2000)]
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        _native = LinalgProvider.NativeAvailable ? new OpenBlasLinalg() : null;
        _pristine = new double[N * N];
        _definite = new double[N * N];
        _work = new double[N * N];
        _rhs = new double[N];
        _pivots = new int[N];
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                double value = System.Math.Sin(0.7 * (r + 1)) + System.Math.Cos(1.3 * (c + 1));
                _pristine[(c * N) + r] = value + (r == c ? 2.0 * N : 0);
                _definite[(c * N) + r] = System.Math.Cos(0.4 * ((r * c) + 1)) + (r == c ? 2.0 * N : 0);
            }

            _rhs[c] = System.Math.Cos(0.5 * (c + 1)) + 1;
        }

        // Symmetry by construction: the Cholesky kernels read one triangle, but a lopsided input
        // would make the two backends answer different questions.
        for (int c = 0; c < N; c++)
        {
            for (int r = c + 1; r < N; r++)
            {
                _definite[(c * N) + r] = _definite[(r * N) + c];
            }
        }
    }

    [IterationSetup]
    public void Reset() => System.Array.Copy(_pristine, _work, _work.Length);

    [Benchmark(Baseline = true)]
    public int GetrfManaged() => _managed.Getrf(N, N, _work, N, _pivots);

    [Benchmark]
    public int GetrfNative() => Native.Getrf(N, N, _work, N, _pivots);

    [Benchmark]
    public int GetriManaged()
    {
        _managed.Getrf(N, N, _work, N, _pivots);
        return _managed.Getri(N, _work, N, _pivots);
    }

    [Benchmark]
    public int GetriNative()
    {
        Native.Getrf(N, N, _work, N, _pivots);
        return Native.Getri(N, _work, N, _pivots);
    }

    [Benchmark]
    public double SolveManaged() => Solve(_managed);

    [Benchmark]
    public double SolveNative() => Solve(Native);

    [Benchmark]
    public int PotrfManaged()
    {
        System.Array.Copy(_definite, _work, _work.Length);
        return _managed.Potrf(lower: false, N, _work, N);
    }

    [Benchmark]
    public int PotrfNative()
    {
        System.Array.Copy(_definite, _work, _work.Length);
        return Native.Potrf(lower: false, N, _work, N);
    }

    private DenseLinalg Native => _native
        ?? throw new NotSupportedException("OpenBLAS is not available on this machine.");

    private double Solve(DenseLinalg provider)
    {
        var b = new double[N];
        _rhs.CopyTo(b, 0);
        provider.Getrf(N, N, _work, N, _pivots);
        provider.Getrs(transpose: false, N, 1, _work, N, _pivots, b, N);
        return b[0];
    }
}
