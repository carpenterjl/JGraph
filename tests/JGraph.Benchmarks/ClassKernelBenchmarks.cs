using BenchmarkDotNet.Attributes;
using JGraph.Numerics;

namespace JGraph.Benchmarks;

/// <summary>
/// M97: what a numeric class costs. <see cref="RoundClamp"/> is the integer conversion on its own and
/// <see cref="ToSingle"/> the float one; <see cref="FusedAdd"/> is an addition that finishes each
/// element in the class as it writes it, against <see cref="AddThenRoundClamp"/>, which is the same
/// arithmetic followed by a second sweep over the whole array — the pair the milestone exists to
/// separate. <see cref="Add"/> is the control: the same addition with no class on it at all, and
/// what the fused form should cost.
/// </summary>
[MemoryDiagnoser(false)]
public class ClassKernelBenchmarks
{
    private NumericBuffer _a = null!;
    private NumericBuffer _b = null!;
    private NumericBuffer _dest = null!;
    private int _restore;

    [Params(1 << 16, 1 << 22)]
    public int Count { get; set; }

    [Params(1, 16)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _restore = ParallelKernels.MaxDegree;
        _a = new ManagedBuffer(Count);
        _b = new ManagedBuffer(Count);
        _dest = new ManagedBuffer(Count);
        Span<double> a = _a.AsSpan();
        Span<double> b = _b.AsSpan();
        for (int i = 0; i < Count; i++)
        {
            // Astride the uint8 range, so the saturation arms are not all one way.
            a[i] = (((i * 0.618033988749895) % 1.0) - 0.4) * 400;
            b[i] = ((i * 0.7548776662466927) % 1.0) * 40;
        }
    }

    [IterationSetup]
    public void Each() => ParallelKernels.MaxDegree = Threads;

    [GlobalCleanup]
    public void Cleanup()
    {
        ParallelKernels.MaxDegree = _restore;
        _a.Dispose();
        _b.Dispose();
        _dest.Dispose();
    }

    /// <summary>The conversion on its own — what uint8(x) of a named array runs.</summary>
    [Benchmark]
    public void RoundClamp() =>
        PackedMath.Round(_a, _dest, PackedMath.Rounding.Between(0, byte.MaxValue));

    /// <summary>The same for single, which rounds instead of clamping.</summary>
    [Benchmark]
    public void ToSingle() => PackedMath.Round(_a, _dest, PackedMath.Rounding.ToSingle);

    /// <summary>The control: an addition wearing no class.</summary>
    [Benchmark(Baseline = true)]
    public void Add() => PackedMath.Binary(PackedMath.BinaryOp.Add, _a, _b, _dest);

    /// <summary>The addition and its class in one sweep — what the interpreter now runs.</summary>
    [Benchmark]
    public void FusedAdd() =>
        PackedMath.Binary(PackedMath.BinaryOp.Add, _a, _b, _dest,
                          PackedMath.Rounding.Between(0, byte.MaxValue));

    /// <summary>The two sweeps it replaces.</summary>
    [Benchmark]
    public void AddThenRoundClamp()
    {
        PackedMath.Binary(PackedMath.BinaryOp.Add, _a, _b, _dest);
        PackedMath.Round(_dest, _dest, PackedMath.Rounding.Between(0, byte.MaxValue));
    }

    /// <summary>And the road both of those replaced: one delegate call per element.</summary>
    [Benchmark]
    public void AddThenMapPerElement()
    {
        PackedMath.Binary(PackedMath.BinaryOp.Add, _a, _b, _dest);
        PackedMath.Map(_dest, _dest, static x =>
            double.IsNaN(x) ? 0 : Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), 0, 255));
    }
}
