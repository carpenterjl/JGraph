using BenchmarkDotNet.Attributes;
using JGraph.Numerics;

namespace JGraph.Benchmarks;

/// <summary>
/// M93: what a thread is worth on an elementwise kernel, and from what size. Every case runs the
/// same kernel over the same buffer at one thread and at all of them, so the pair of rows is the
/// speedup — and the length at which the pair stops being a pair is the threshold
/// <see cref="ParallelKernels.MemoryBoundThreshold"/> and
/// <see cref="ParallelKernels.ComputeBoundThreshold"/> are set from.
/// </summary>
/// <remarks>
/// Two costs are deliberately separated. A kernel that streams memory (multiply, compare, copy) is
/// bounded by how fast the machine can move eight bytes and gains little more than the fork costs
/// until the array is large; a kernel that computes (a transcendental, <c>Math.Pow</c>) has tens of
/// cycles of work per element and pays back from far smaller arrays. Setting one threshold for both
/// would leave one of them wrong.
/// </remarks>
[MemoryDiagnoser(false)]
public class ParallelThresholdBenchmarks
{
    private NumericBuffer _x = null!;
    private NumericBuffer _y = null!;
    private NumericBuffer _dest = null!;
    private int _restore;

    [Params(65_536, 262_144, 2_097_152, 16_777_216)]
    public int Count { get; set; }

    [Params(1, 8)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _restore = ParallelKernels.MaxDegree;
        _x = new ManagedBuffer(Count);
        _y = new ManagedBuffer(Count);
        _dest = new ManagedBuffer(Count);
        Span<double> x = _x.AsSpan();
        Span<double> y = _y.AsSpan();
        for (int i = 0; i < Count; i++)
        {
            x[i] = (i % 9_973) * 0.001_37 + 0.25;
            y[i] = (i % 7_919) * 0.002_11 + 0.5;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ParallelKernels.MaxDegree = _restore;
        _x.Dispose();
        _y.Dispose();
        _dest.Dispose();
    }

    [IterationSetup]
    public void Pin() => ParallelKernels.MaxDegree = Threads;

    /// <summary>Memory-bound: two reads and a write per element, no arithmetic worth the name.</summary>
    [Benchmark(Baseline = true)]
    public void Multiply() => PackedMath.Binary(PackedMath.BinaryOp.Multiply, _x, _y, _dest);

    /// <summary>Memory-bound, and the whole of the mask row: a compare and a select per element.</summary>
    [Benchmark]
    public void Compare() => PackedMath.Compare(PackedMath.CompareOp.Greater, _x, _y, _dest);

    /// <summary>Memory-bound with no read of its own — what a fresh buffer costs to touch.</summary>
    [Benchmark]
    public void FillConstant() => PackedMath.FillConstant(_dest, 1.5);

    /// <summary>Compute-bound, vector kernel: the approximate tier as it now runs.</summary>
    [Benchmark]
    public void SinVector() => PackedMath.Unary(PackedMath.UnaryOp.Sin, _x, _dest);

    /// <summary>Compute-bound, scalar kernel: the approximate tier below its threshold.</summary>
    [Benchmark]
    public void SinScalar() => PackedMath.UnaryScalar(PackedMath.UnaryOp.Sin, _x, _dest);

    /// <summary>Exact and vector at every length — the one a threshold never gates.</summary>
    [Benchmark]
    public void Sqrt() => PackedMath.Unary(PackedMath.UnaryOp.Sqrt, _x, _dest);

    /// <summary>Compute-bound and irreducibly scalar: Math.Pow, which is what <c>x.^2</c> still runs.</summary>
    [Benchmark]
    public void Power() => PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Power, _x, 2, _dest);

    /// <summary>Compute-bound through a delegate — atan2, hypot, mod and the bit family.</summary>
    [Benchmark]
    public void Zip() => PackedMath.Zip(_x, _y, _dest, Math.Atan2);
}
