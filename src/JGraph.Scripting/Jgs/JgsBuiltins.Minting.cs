namespace JGraph.Scripting.Jgs;

/// <summary>
/// V2.2 (ADR 0163): the builtins whose answer a binding may adopt instead of sharing.
/// </summary>
/// <remarks>
/// <para>
/// M2 counts a holder for every wrapper an entry can reach. A call's answer might be a wrapper the
/// builtin was handed or had stored — <c>deal</c> hands its argument back, <c>getappdata</c> hands
/// back what a figure holds — so the safe reading of a call at a binding is that its answer is
/// someone's, and the binding takes a counted share, at the cost of one copy on the name's first
/// write. That copy is wasted when the builtin minted its answer: <c>x = zeros(1, n); x(1) = 1</c>
/// would copy <c>n</c> doubles for a holder that does not exist.
/// </para>
/// <para>
/// A name on this list is adopted: the binding keeps the wrapper the builtin returned, so the first
/// write lands in place. The list is opt-in, and it is verified rather than trusted:
/// <c>tools/ownership/audit-ownership.py</c> reads every builtin's returns and fails the gate when
/// a name here can return anything but a wrapper it minted (an argument, an element of one, a
/// wrapper a helper on the audit's borrowing list handed back). A helper the scan cannot settle on
/// its own — a switch expression, a recursion — carries <c>// audit: mints</c> above its
/// declaration, which is a person's assertion the audit then believes and the ADR lists.
/// </para>
/// <para>
/// Nothing on the list makes a wrong answer possible if it were wrong about a builtin: the audit
/// is what makes it right, and a builtin that starts handing back an argument fails the gate the
/// day it does. The list is deliberately short; a stage that measures a copy worth saving adds the
/// name, with the audit's verdict beside it.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The builtins a binding adopts (V2.2, M2): each returns only wrappers it minted.</summary>
    internal static readonly HashSet<string> MintingBuiltins = new(StringComparer.Ordinal)
    {
        // constructors: every element is written here
        "zeros", "ones", "rand", "colon", "magic", "cell", "struct",
        // shape and container verbs whose answer is a fresh container over shared children
        "repmat", "sort", "num2cell", "fieldnames", "struct2cell", "cell2struct",
        "setfield", "rmfield", "values", "keys",
        // text and tests that mint their answer (`logical` is not here: it hands a logical
        // argument back as it is, which the audit caught)
        "sprintf", "isempty",
    };
}
