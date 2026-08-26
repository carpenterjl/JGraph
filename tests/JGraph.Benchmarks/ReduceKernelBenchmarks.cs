using BenchmarkDotNet.Attributes;
using JGraph.Numerics;

namespace JGraph.Benchmarks;

/// <summary>
/// M94: what the reduction kernels cost in each layout, at one thread and at all of them. The
/// matrix is 8000 rows by <c>Count/8000</c> columns, the d03 dimreduce shape scaled — so the
/// contiguous case is <c>sum(A, 1)</c>, the panel case <c>max(A, [], 2)</c>, and the serial floor
/// is the one cumulative sweep no thread can help.
/// </summary>
[MemoryDiagnoser(false)]
public class ReduceKernelBenchmarks
{
    private const int Rows = 8_000;

    private NumericBuffer _data = null!;
    private NumericBuffer _bySlice = null!;
    private NumericBuffer _indices = null!;
    private NumericBuffer _running = null!;
    private int _columns;
    private int _restore;

    [Params(4_000_000, 40_000_000)]
    public int Count { get; set; }

    [Params(1, 8)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _restore = ParallelKernels.MaxDegree;
        _columns = Count / Rows;
        _data = new ManagedBuffer(Rows * _columns);
        Span<double> d = _data.AsSpan();
        for (int i = 0; i < d.Length; i++)
        {
            d[i] = (i % 9_973) * 0.001_37 + 0.25;
        }

        _bySlice = new ManagedBuffer(Rows);
        _indices = new ManagedBuffer(Rows);
        _running = new ManagedBuffer(Rows * _columns);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ParallelKernels.MaxDegree = _restore;
        _data.Dispose();
        _bySlice.Dispose();
        _indices.Dispose();
        _running.Dispose();
    }

    [IterationSetup]
    public void Pin() => ParallelKernels.MaxDegree = Threads;

    [Benchmark]
    public void SumContiguous() => ReduceKernels.Sum(
        _data, _bySlice, new ReduceKernels.Split(1, _columns, Rows), omitNan: false);

    [Benchmark]
    public void SumPanel() => ReduceKernels.Sum(
        _data, _bySlice, new ReduceKernels.Split(Rows, _columns, 1), omitNan: false);

    [Benchmark]
    public void ExtremePanel() => ReduceKernels.Extreme(
        _data, _bySlice, _indices, new ReduceKernels.Split(Rows, _columns, 1),
        takeMin: false, omitNan: true);

    [Benchmark]
    public void VariancePanel() => ReduceKernels.Variance(
        _data, _bySlice, new ReduceKernels.Split(Rows, _columns, 1),
        omitNan: false, population: false, takeRoot: true);

    [Benchmark]
    public void CumulativeSumOneSlice() => ReduceKernels.CumulativeSum(
        _data, _running, new ReduceKernels.Split(1, Rows * _columns, 1),
        omitNan: false, reverse: false);

    [Benchmark]
    public void CumulativeSumColumns() => ReduceKernels.CumulativeSum(
        _data, _running, new ReduceKernels.Split(1, _columns, Rows),
        omitNan: false, reverse: false);
}
