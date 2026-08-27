using BenchmarkDotNet.Attributes;
using JGraph.Numerics;

namespace JGraph.Benchmarks;

/// <summary>
/// M96: what a transform and a filter cost in each of the shapes the demo scripts ask for. One long
/// signal is <c>fft(sig)</c> and takes the factored road; a batch of short ones is the 32×64K loop;
/// a matrix down its columns is what <c>fft2</c> runs twice. The filter rows are the feed-forward
/// kernel against the recurrence it replaces, and the convolution rows the separable pass against
/// the outer product it no longer builds.
/// </summary>
[MemoryDiagnoser(false)]
public class TransformKernelBenchmarks
{
    private NumericBuffer _re = null!;
    private NumericBuffer _im = null!;
    private NumericBuffer _outRe = null!;
    private NumericBuffer _outIm = null!;
    private double[] _signal = null!;
    private double[] _taps = null!;
    private double[] _one = null!;
    private double[,] _image = null!;
    private double[,] _kernel = null!;
    private double[] _row = null!;
    private int _restore;

    [Params(1 << 16, 1 << 22)]
    public int Count { get; set; }

    [Params(1, 16)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _restore = ParallelKernels.MaxDegree;
        _re = new ManagedBuffer(Count);
        _im = new ManagedBuffer(Count);
        _outRe = new ManagedBuffer(Count);
        _outIm = new ManagedBuffer(Count);
        Span<double> r = _re.AsSpan();
        for (int i = 0; i < Count; i++)
        {
            r[i] = (((i * 0.618033988749895) % 1.0) - 0.5) + Math.Sin(i * 0.001);
        }

        _signal = new double[10_000_000];
        for (int i = 0; i < _signal.Length; i++)
        {
            _signal[i] = (((i * 0.618033988749895) % 1.0) - 0.5);
        }

        _taps = new double[64];
        Array.Fill(_taps, 1.0 / 64.0);
        _one = [1.0];

        const int Side = 1024;
        _image = new double[Side, Side];
        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                _image[y, x] = Math.Sin((x * 0.01) + (y * 0.013));
            }
        }

        _row = new double[21];
        double total = 0;
        for (int i = 0; i < _row.Length; i++)
        {
            _row[i] = Math.Exp(-((i - 10.0) * (i - 10.0)) / 18.0);
            total += _row[i];
        }

        for (int i = 0; i < _row.Length; i++)
        {
            _row[i] /= total;
        }

        _kernel = new double[21, 21];
        for (int y = 0; y < 21; y++)
        {
            for (int x = 0; x < 21; x++)
            {
                _kernel[y, x] = _row[y] * _row[x];
            }
        }
    }

    [IterationSetup]
    public void Each() => ParallelKernels.MaxDegree = Threads;

    [GlobalCleanup]
    public void Cleanup()
    {
        ParallelKernels.MaxDegree = _restore;
        _re.Dispose();
        _im.Dispose();
        _outRe.Dispose();
        _outIm.Dispose();
    }

    /// <summary>One whole signal — the road the 4M row takes.</summary>
    [Benchmark]
    public void OneTransform() =>
        FftKernels.TransformAlong(
            _re, null, _outRe, _outIm, new ReduceKernels.Split(1, Count, 1), Count,
            inverse: false, symmetric: false);

    /// <summary>The same samples cut into 2048-point columns, which is what fft2 walks.</summary>
    [Benchmark]
    public void ColumnsOf2048() =>
        FftKernels.TransformAlong(
            _re, null, _outRe, _outIm, new ReduceKernels.Split(1, 2048, Count / 2048), 2048,
            inverse: false, symmetric: false);

    /// <summary>And cut into rows, so every slice is read through a stride.</summary>
    [Benchmark]
    public void RowsOf2048() =>
        FftKernels.TransformAlong(
            _re, null, _outRe, _outIm, new ReduceKernels.Split(Count / 2048, 2048, 1), 2048,
            inverse: false, symmetric: false);

    /// <summary>Ten million samples through a sixty-four-tap smoother, the way the d02 row asks.</summary>
    [Benchmark]
    public double[] FeedForwardFilter() =>
        JGraph.Signal.DigitalFilter.Filter(_taps, _one, _signal);

    /// <summary>A 21-tap separable blur of a 1024-square field.</summary>
    [Benchmark]
    public double[,] SeparableBlur() =>
        JGraph.Imaging.Filters.SeparableConvolve2(_image, _row, _row, JGraph.Imaging.Conv2Shape.Same);

    /// <summary>The same blur through the kernel the separable pass no longer builds.</summary>
    [Benchmark]
    public double[,] BuiltKernelBlur() =>
        JGraph.Imaging.Filters.Convolve2(_image, _kernel, JGraph.Imaging.Conv2Shape.Same);
}
