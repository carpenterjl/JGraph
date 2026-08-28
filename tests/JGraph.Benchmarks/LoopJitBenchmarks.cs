using BenchmarkDotNet.Attributes;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;

namespace JGraph.Benchmarks;

/// <summary>
/// The interpreter hot-loop compiler (M98) against the tree walk, on the head-to-head suite's own
/// 2M-iteration scalar loop and on a nested pair. The gate is the compiled loop at or under 0.08 s
/// for the 2M row; the walk is the price a refused loop still pays.
/// </summary>
[MemoryDiagnoser]
public class LoopJitBenchmarks
{
    private const string BenchLoop = """
        acc = 0;
        v = 1.0;
        for k = 1:2e6
            v = mod(v * 1.0000001 + 0.001, 2);
            if v > 1
                acc = acc + v;
            end
        end
        """;

    private const string NestedLoops = """
        tot = 0;
        for k = 1:2000
            for j = 1:500
                tot = tot + j * 1e-3;
            end
        end
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
    public bool Jit { get; set; }

    private static bool Run(string script)
    {
        var context = new ScriptContext(new NullOutput(), (_, _) => { }, null);
        return JgsRunner.Run(script, context, default, sourceId: "", hook: null, JgsDialect.Matlab).Success;
    }

    [Benchmark]
    public bool ScalarLoop2M()
    {
        JgsLoopJit.Enabled = Jit;
        return Run(BenchLoop);
    }

    [Benchmark]
    public bool NestedLoops1M()
    {
        JgsLoopJit.Enabled = Jit;
        return Run(NestedLoops);
    }
}
