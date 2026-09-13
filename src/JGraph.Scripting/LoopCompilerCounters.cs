using JGraph.Scripting.Jgs;

namespace JGraph.Scripting;

/// <summary>
/// The hot-loop compiler's process-wide counters, read by a host that reports them (the CLI's
/// batch stats line, ADR 0160): how many loops ran compiled and how many times a compiled loop
/// handed a statement, condition or bound back to the tree walk. The two-process parity
/// contract of ADR 0160 reads them to prove a run really took the compiled road.
/// </summary>
public static class LoopCompilerCounters
{
    /// <summary>How many loops have run compiled in this process.</summary>
    public static long CompiledLoops => JgsLoopJit.CompiledRuns;

    /// <summary>How many bails compiled loops have taken in this process.</summary>
    public static long Bails => JgsLoopJit.Bails;
}
