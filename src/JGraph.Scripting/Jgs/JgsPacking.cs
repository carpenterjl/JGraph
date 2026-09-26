using System.Runtime.CompilerServices;
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

    // A test-scoped allocator and allocation counter (V1, ADR 0162). The ownership contract is
    // about which backend a payload sits on and how many copies a write makes, and neither can be
    // asserted against a process-wide allocator: AsyncLocal keeps one test's choice out of the
    // next's, including when xUnit runs them in parallel.
    private static readonly AsyncLocal<BufferAllocator?> Chosen = new();
    private static readonly AsyncLocal<StrongBox<long>?> Counter = new();

    /// <summary>
    /// Allocates a packed buffer through the process-wide dual-strategy allocator. A request the
    /// allocator refuses (M6's mapped budget, ADR 0162) is MATLAB's <c>Out of memory.</c> error,
    /// which a script's <c>try</c> may catch, not a defect of this build.
    /// </summary>
    public static NumericBuffer Allocate(long elementCount)
    {
        if (Counter.Value is { } counted)
        {
            Interlocked.Increment(ref counted.Value);
        }

        try
        {
            return (Chosen.Value ?? BufferAllocator.Shared).Allocate(elementCount);
        }
        catch (OutOfMemoryException refused)
        {
            throw new JgsRuntimeException(0, 0, "MATLAB:nomem", refused.Message);
        }
    }

    /// <summary>
    /// Allocates a packed buffer a kernel will write in full before anything reads it (Z2e, ADR
    /// 0173): the contents are unspecified when <see cref="JgsReuse.UninitializedDestinations"/> is
    /// on, zero otherwise. Counted like <see cref="Allocate"/>.
    /// </summary>
    public static NumericBuffer AllocateForOverwrite(long elementCount)
    {
        if (!JgsReuse.UninitializedDestinations)
        {
            return Allocate(elementCount);
        }

        if (Counter.Value is { } counted)
        {
            Interlocked.Increment(ref counted.Value);
        }

        try
        {
            return (Chosen.Value ?? BufferAllocator.Shared).AllocateForOverwrite(elementCount);
        }
        catch (OutOfMemoryException refused)
        {
            throw new JgsRuntimeException(0, 0, "MATLAB:nomem", refused.Message);
        }
    }

    /// <summary>How many packed buffers this scope has allocated, or zero outside one.</summary>
    internal static long Allocations => Counter.Value?.Value ?? 0;

    /// <summary>
    /// Allocates through <paramref name="allocator"/> until the returned scope is disposed, and
    /// counts what is allocated meanwhile. Tests only.
    /// </summary>
    internal static IDisposable Use(BufferAllocator allocator) => new Scope(allocator);

    private sealed class Scope : IDisposable
    {
        private readonly BufferAllocator? _previous = Chosen.Value;
        private readonly StrongBox<long>? _previousCounter = Counter.Value;

        public Scope(BufferAllocator allocator)
        {
            Chosen.Value = allocator;
            Counter.Value = new StrongBox<long>(0);
        }

        public void Dispose()
        {
            Chosen.Value = _previous;
            Counter.Value = _previousCounter;
        }
    }

    private static bool? ReadEnvironmentOverride() =>
        Environment.GetEnvironmentVariable("JGRAPH_JGS_PACKED") switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
}
