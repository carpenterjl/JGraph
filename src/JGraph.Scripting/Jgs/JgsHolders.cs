using System.Runtime.CompilerServices;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M1: how many entries hold a payload, and whether anyone outside the count can still read it.
/// </summary>
/// <remarks>
/// The model (docs/plans, M1–M7) counts holders on the <em>payload</em>, never on the wrapper: two
/// wrappers over one buffer, or two struct arrays over one element dictionary, are two holders of
/// one thing, and a wrapper cannot see the other. A count above one is what makes a write copy
/// first (M3), and a count of exactly one is what makes disposal safe (M6).
/// <para>
/// Four payload kinds are classes and carry the count in a field. Three cannot — a boxed element
/// array and a cell slot array are both <c>JgsValue[]</c>, and a struct element is a
/// <c>Dictionary&lt;string, JgsValue&gt;</c> — so those are keyed in a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> whose absence means one holder. V1.0 measured
/// the table against changing their representation: it costs 2 ns per gate when nothing is shared
/// and 40 ns when a million payloads are, against write roads of 200 ns and up, and the scripts
/// measured run tens of gates apiece. ADR 0162 records the measurement and the tripline at which
/// the fallback would apply.
/// </para>
/// <para>
/// <b>Zero means one.</b> A field that was never touched reads 0, and a payload nobody has shared
/// has one holder, so both are the same answer and a freshly allocated payload needs no
/// initialisation. <b>Counts saturate</b> at <see cref="Saturated"/>: a dead wrapper never
/// decrements (M3), so a long-lived payload shared again and again in a session accumulates a count
/// with no bound, and an <c>int</c> that wrapped negative would pass the shared test as unshared.
/// Once saturated a payload is permanently shared: every write detaches, and disposal skips it.
/// </para>
/// </remarks>
internal static class JgsHolders
{
    /// <summary>A payload that has been shared so often the count stopped moving: shared for ever.</summary>
    public const int Saturated = int.MaxValue;

    private static readonly ConditionalWeakTable<object, StrongBox<int>> Counts = new();

    /// <summary>How many entries hold <paramref name="payload"/>. Never below one.</summary>
    public static int Of(object? payload)
    {
        switch (payload)
        {
            case null:
                return 1;
            case NumericBuffer buffer:
                return Read(buffer.HolderSlot);
            case JgsPackedComplex complex:
                return Read(complex.HolderSlot);
            case JgsStructArray structs:
                return Read(structs.HolderSlot);
            case JgsObject instance:
                return Read(instance.HolderSlot);
            default:
                return Counts.TryGetValue(payload, out StrongBox<int>? box) ? Read(box.Value) : 1;
        }
    }

    /// <summary>Whether a write through one holder could be seen through another.</summary>
    public static bool IsShared(object? payload) => Of(payload) > 1;

    /// <summary>M2's increment: a second entry now holds this payload.</summary>
    public static void Share(object? payload)
    {
        if (payload is null)
        {
            return;
        }

        Increment(ref SlotOf(payload, create: true));
    }

    /// <summary>
    /// M3 and M5's decrement: a holder let go — a wrapper replaced its payload, or a scope ended.
    /// Never moves a saturated payload, and never goes below one holder.
    /// </summary>
    public static void Release(object? payload)
    {
        if (payload is null)
        {
            return;
        }

        ref int slot = ref SlotOf(payload, create: false);
        if (!Unsafe.IsNullRef(ref slot))
        {
            Decrement(ref slot);
        }
    }

    /// <summary>
    /// Sets the count directly. Tests only: saturation is reachable in a session but not in a
    /// test, since getting there means sharing a payload two billion times.
    /// </summary>
    internal static void Seed(object payload, int holders)
    {
        ref int slot = ref SlotOf(payload, create: true);
        Volatile.Write(ref slot, holders);
    }

    private static ref int SlotOf(object payload, bool create)
    {
        switch (payload)
        {
            case NumericBuffer buffer:
                return ref buffer.HolderSlot;
            case JgsPackedComplex complex:
                return ref complex.HolderSlot;
            case JgsStructArray structs:
                return ref structs.HolderSlot;
            case JgsObject instance:
                return ref instance.HolderSlot;
            default:
                if (create)
                {
                    return ref Counts.GetValue(payload, static _ => new StrongBox<int>(1)).Value;
                }

                return ref Counts.TryGetValue(payload, out StrongBox<int>? box)
                    ? ref box.Value
                    : ref Unsafe.NullRef<int>();
        }
    }

    private static int Read(int slot) => slot == 0 ? 1 : slot;

    private static void Increment(ref int slot)
    {
        while (true)
        {
            int seen = Volatile.Read(ref slot);
            if (seen == Saturated)
            {
                return;
            }

            int next = seen == 0 ? 2 : seen + 1;
            if (next < 0)
            {
                next = Saturated;
            }

            if (Interlocked.CompareExchange(ref slot, next, seen) == seen)
            {
                return;
            }
        }
    }

    private static void Decrement(ref int slot)
    {
        while (true)
        {
            int seen = Volatile.Read(ref slot);
            if (seen == Saturated || seen <= 1)
            {
                return;
            }

            int next = seen == 2 ? 1 : seen - 1;
            if (Interlocked.CompareExchange(ref slot, next, seen) == seen)
            {
                return;
            }
        }
    }
}
