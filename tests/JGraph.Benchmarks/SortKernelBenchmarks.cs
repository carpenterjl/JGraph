using BenchmarkDotNet.Attributes;
using JGraph.Numerics;

namespace JGraph.Benchmarks;

/// <summary>
/// M95: what a sort costs in each of its shapes, at one thread and at all of them. One long slice
/// is the d03 row's own shape and takes the splitter partition; many short slices are
/// <c>sort(A, 1)</c> down a matrix, one thread to a column; a strided slice is <c>sort(A, 2)</c>,
/// which is gathered before it is sorted. The already-ordered case is here because the kernel looks
/// for it, and a script that sorts something twice should not pay twice.
/// </summary>
[MemoryDiagnoser(false)]
public class SortKernelBenchmarks
{
    private const int Rows = 8_000;

    private NumericBuffer _data = null!;
    private NumericBuffer _ordered = null!;
    private NumericBuffer _values = null!;
    private NumericBuffer _positions = null!;
    private int _columns;
    private int _restore;

    [Params(4_000_000, 20_000_000)]
    public int Count { get; set; }

    [Params(1, 8)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _restore = ParallelKernels.MaxDegree;
        _columns = Count / Rows;
        _data = new ManagedBuffer(Rows * _columns);
        _ordered = new ManagedBuffer(Rows * _columns);
        Span<double> d = _data.AsSpan();
        Span<double> o = _ordered.AsSpan();
        for (int i = 0; i < d.Length; i++)
        {
            d[i] = (((i * 7_919) % 9_973) * 0.001_37) - 6.5;
            o[i] = i * 0.5;
        }

        _values = new ManagedBuffer(Rows * _columns);
        _positions = new ManagedBuffer(Rows * _columns);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ParallelKernels.MaxDegree = _restore;
        _data.Dispose();
        _ordered.Dispose();
        _values.Dispose();
        _positions.Dispose();
    }

    [IterationSetup]
    public void Iteration() => ParallelKernels.MaxDegree = Threads;

    private ReduceKernels.Split Whole => new(1, Rows * _columns, 1);

    private ReduceKernels.Split Columns => new(1, Rows, _columns);

    private ReduceKernels.Split Rowwise => new(Rows, _columns, 1);

    [Benchmark(Baseline = true)]
    public void OneSlice() =>
        SortKernels.SortAlong(_data, _values, null, Whole, false, false, 1);

    [Benchmark]
    public void OneSliceDescending() =>
        SortKernels.SortAlong(_data, _values, null, Whole, true, false, 1);

    [Benchmark]
    public void OneSliceWithPositions() =>
        SortKernels.SortAlong(_data, _values, _positions, Whole, false, false, 1);

    [Benchmark]
    public void OneSliceAlreadyOrdered() =>
        SortKernels.SortAlong(_ordered, _values, null, Whole, false, false, 1);

    [Benchmark]
    public void ColumnsOfAMatrix() =>
        SortKernels.SortAlong(_data, _values, null, Columns, false, false, 1);

    [Benchmark]
    public void RowsOfAMatrix() =>
        SortKernels.SortAlong(_data, _values, null, Rowwise, false, false, 1);
}
