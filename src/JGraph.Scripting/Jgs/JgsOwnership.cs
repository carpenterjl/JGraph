namespace JGraph.Scripting.Jgs;

/// <summary>
/// The switch for the ownership model (V1, ADR 0162): whether binding a name in the MATLAB dialect
/// shares the payload under a holder count (M2) and lets the first write copy it (M3), or copies
/// eagerly as every binding did before.
/// </summary>
/// <remarks>
/// The gates themselves (M3's detach, M6's disposal rule) run whatever this says, because they are
/// how a payload stays correct once anything has shared it — the switch only decides whether
/// <see cref="Interpreter.CopyForBinding"/> shares or copies. Turning it off therefore restores the
/// old cost, not old behaviour with a new hazard. <c>JGRAPH_COW=0</c> is the kill switch the plan
/// asks for, kept for one milestone.
/// </remarks>
internal static class JgsOwnership
{
    /// <summary>The built-in default before any environment override.</summary>
    public const bool DefaultEnabled = true;

    /// <summary>Whether a MATLAB-dialect binding shares its payload instead of copying it.</summary>
    public static bool Enabled { get; set; } = ReadEnvironmentOverride() ?? DefaultEnabled;

    private static bool? ReadEnvironmentOverride() =>
        Environment.GetEnvironmentVariable("JGRAPH_COW") switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
}
