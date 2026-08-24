using BenchmarkDotNet.Attributes;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Benchmarks;

/// <summary>
/// The dense linear-algebra ladder: every provider-backed operation at the head-to-head suite's
/// sizes, managed vs native. M88 lands gemm; each following milestone adds its rows (LU, solve,
/// inv, chol, then qr/svd/eig) so the table grows with the provider surface.
/// </summary>
[MemoryDiagnoser]
public class DenseLinalgBenchmarks
{
    private readonly ManagedLinalg _managed = new();
    private OpenBlasLinalg? _native;
    private double[] _a = [];
    private double[] _b = [];
    private double[] _c = [];

    [Params(100, 400, 1000, 2000)]
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        _native = LinalgProvider.NativeAvailable ? new OpenBlasLinalg() : null;
        _a = new double[N * N];
        _b = new double[N * N];
        _c = new double[N * N];
        for (int i = 0; i < _a.Length; i++)
        {
            _a[i] = System.Math.Sin(0.7 * (i + 1));
            _b[i] = System.Math.Cos(1.3 * (i + 1));
        }
    }

    [Benchmark(Baseline = true)]
    public void GemmManaged() => _managed.Gemm(false, false, N, N, N, _a, N, _b, N, _c, N);

    [Benchmark]
    public void GemmNative()
    {
        if (_native is null)
        {
            throw new NotSupportedException("OpenBLAS is not available on this machine.");
        }

        _native.Gemm(false, false, N, N, N, _a, N, _b, N, _c, N);
    }
}
