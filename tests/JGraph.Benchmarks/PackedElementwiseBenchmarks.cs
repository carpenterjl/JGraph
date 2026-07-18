using BenchmarkDotNet.Attributes;
using JGraph.Numerics;

namespace JGraph.Benchmarks;

/// <summary>
/// The M22 headline: elementwise math over flat packed buffers (TensorPrimitives SIMD) versus the
/// classic boxed representation (one heap object per element, per operation). The chain mirrors a
/// typical script line: <c>y = a .* x + b</c> followed by <c>sin(y)</c>.
/// </summary>
[MemoryDiagnoser]
public class PackedElementwiseBenchmarks
{
    private NumericBuffer _x = null!;
    private NumericBuffer _scratch = null!;
    private object[] _boxedX = null!;

    [Params(1_000_000, 10_000_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _x = new ManagedBuffer(Count);
        _scratch = new ManagedBuffer(Count);
        var span = _x.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = i * 0.001;
        }

        // The boxed baseline: one heap object per element, like a JgsValue.Number per sample.
        _boxedX = new object[Count];
        for (int i = 0; i < Count; i++)
        {
            _boxedX[i] = i * 0.001;
        }
    }

    [Benchmark(Baseline = true)]
    public object[] Boxed_MulAddSin()
    {
        var result = new object[Count];
        for (int i = 0; i < Count; i++)
        {
            double y = ((double)_boxedX[i] * 2.5) + 1.0;
            result[i] = Math.Sin(y);
        }

        return result;
    }

    [Benchmark]
    public NumericBuffer Packed_MulAddSin()
    {
        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Multiply, _x, 2.5, _scratch);
        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Add, _scratch, 1.0, _scratch);
        PackedMath.Unary(PackedMath.UnaryOp.Sin, _scratch, _scratch);
        return _scratch;
    }

    [Benchmark]
    public double Packed_SumReduction() => PackedMath.Sum(_x);
}
