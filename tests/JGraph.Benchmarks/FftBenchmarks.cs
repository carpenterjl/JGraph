using System.Numerics;
using BenchmarkDotNet.Attributes;
using JGraph.Signal;

namespace JGraph.Benchmarks;

/// <summary>
/// FFT cost at audio scale: a power-of-two length (pure radix-2 with the M22 twiddle table) and a
/// million-sample non-power-of-two length (Bluestein over pooled scratch) — the transforms the
/// MATLAB-style lab scripts run on whole recordings.
/// </summary>
[MemoryDiagnoser]
public class FftBenchmarks
{
    private double[] _powerOfTwo = null!;
    private double[] _million = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(99);
        _powerOfTwo = new double[1 << 20];
        _million = new double[1_000_000];
        for (int i = 0; i < _powerOfTwo.Length; i++)
        {
            _powerOfTwo[i] = random.NextDouble() - 0.5;
        }

        for (int i = 0; i < _million.Length; i++)
        {
            _million[i] = random.NextDouble() - 0.5;
        }
    }

    [Benchmark]
    public Complex[] Radix2_1M() => Fft.Forward(_powerOfTwo);

    [Benchmark]
    public Complex[] Bluestein_1M() => Fft.Forward(_million);
}
