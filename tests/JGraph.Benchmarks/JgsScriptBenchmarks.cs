using BenchmarkDotNet.Attributes;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;

namespace JGraph.Benchmarks;

/// <summary>
/// End-to-end interpreter cost of a realistic DSP-flavored script (range, elementwise chain,
/// reduction) with packed arrays on versus off — the user-visible speedup of M22.
/// </summary>
[MemoryDiagnoser]
public class JgsScriptBenchmarks
{
    private const string Script = """
        let t = 0:0.000001:1;
        let y = sin(t .* 6.28318) .* 0.5 + 0.5;
        let energy = sum(y .* y);
        """;

    private sealed class NullOutput : IScriptOutput
    {
        public void Write(string text)
        {
        }

        public void WriteLine(string text)
        {
        }

        public void WriteError(string text)
        {
        }
    }

    [Params(true, false)]
    public bool Packed { get; set; }

    [Benchmark]
    public bool MillionSampleChain()
    {
        JgsPacking.Enabled = Packed;
        var engine = new JgsScriptEngine();
        ScriptRunResult result = engine
            .RunAsync(Script, new ScriptContext(new NullOutput(), (_, _) => { }, null), default)
            .GetAwaiter()
            .GetResult();
        return result.Success;
    }
}
