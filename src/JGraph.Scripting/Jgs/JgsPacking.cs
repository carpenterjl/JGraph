using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The switch and allocator for JGS packed numeric arrays. While enabled, numeric array producers
/// (ranges, numeric literals, builtin outputs) create packed values and the interpreter takes the
/// SIMD fast paths; while disabled, everything runs the classic boxed representation. The default
/// is on (since M22.5, after the parity suite burned the machinery in); either way the environment
/// variable <c>JGRAPH_JGS_PACKED=1|0</c> forces the mode, which is also the parity-test lever.
/// </summary>
internal static class JgsPacking
{
    /// <summary>The built-in default before any environment override (on since M22.5).</summary>
    public const bool DefaultEnabled = true;

    /// <summary>Whether numeric array producers create packed values.</summary>
    public static bool Enabled { get; set; } = ReadEnvironmentOverride() ?? DefaultEnabled;

    /// <summary>Allocates a packed buffer through the process-wide dual-strategy allocator.</summary>
    public static NumericBuffer Allocate(long elementCount) => BufferAllocator.Shared.Allocate(elementCount);

    private static bool? ReadEnvironmentOverride() =>
        Environment.GetEnvironmentVariable("JGRAPH_JGS_PACKED") switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
}
