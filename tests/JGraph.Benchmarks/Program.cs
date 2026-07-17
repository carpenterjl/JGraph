using BenchmarkDotNet.Running;

namespace JGraph.Benchmarks;

/// <summary>Entry point that runs all benchmarks in this assembly. Usage: <c>dotnet run -c Release</c>.</summary>
public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
