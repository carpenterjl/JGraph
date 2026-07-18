using BenchmarkDotNet.Attributes;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Serialization;

namespace JGraph.Benchmarks;

/// <summary>
/// Persisting a million-point figure: the .graph v4 packed base64 format with streamed file I/O.
/// (Console output records the file size once, alongside the timings.)
/// </summary>
[MemoryDiagnoser]
public class GraphFormatBenchmarks
{
    private FigureModel _figure = null!;
    private string _path = null!;

    [GlobalSetup]
    public void Setup()
    {
        var xs = new double[1_000_000];
        var ys = new double[1_000_000];
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i] = i * 0.001;
            ys[i] = Math.Sin(xs[i]) * 100;
        }

        _figure = new FigureModel();
        _figure.AddAxes().AddLine(xs, ys);

        _path = Path.Combine(Path.GetTempPath(), $"jgraph-bench-{Environment.ProcessId}.graph");
        GraphFormat.Save(_figure, _path);
        Console.WriteLine($"// v4 file size for 1M points: {new FileInfo(_path).Length / (1024.0 * 1024.0):F1} MB");
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    [Benchmark]
    public void Save_1MPoints() => GraphFormat.Save(_figure, _path);

    [Benchmark]
    public FigureModel Load_1MPoints() => GraphFormat.Load(_path);
}
