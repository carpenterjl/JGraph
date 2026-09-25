namespace JGraph.Scripting.Jgs;

/// <summary>
/// The storage behind every <see cref="JgsType.Struct"/> value (M65): a flat, column-major list of
/// elements, each a field dictionary in insertion order. A scalar struct is the one-element case, so
/// there is exactly one representation to reason about rather than two that can disagree.
/// </summary>
/// <remarks>
/// Until M65 a struct array was a <see cref="JgsType.Cell"/> whose elements happened all to be
/// structs, recognised by scanning. That storage could represent a state a struct array cannot be
/// in — elements with different field sets, and a genuine cell of structs indistinguishable from an
/// array of them — which is why this milestone gave the type storage of its own rather than a tag
/// over borrowed storage as M63 and M64 did.
/// <para>
/// Every element carries its own dictionary rather than sharing one field-name list with a value
/// array per element. That costs the key strings once per element, and buys the thing worth far
/// more: the ~70 call sites that already read and write <see cref="JgsValue.AsStruct"/> go on
/// working unchanged, because the scalar case is still exactly a dictionary.
/// </para>
/// </remarks>
internal sealed class JgsStructArray
{
    // How many entries hold this payload (M1). Zero and one both mean one holder.
    private int _holders;

    /// <summary>The holder count's storage (M1); zero means one holder.</summary>
    public ref int HolderSlot => ref _holders;

    /// <summary>V10 (ADR 0171): how many entries hold this array exactly, kept once it is <see cref="Scanned"/>.</summary>
    public int Exact;

    /// <summary>V10: whether every element value's hold was counted for this array, so its death releases them.</summary>
    public bool Scanned;

    /// <summary>V10: whether the array's death has released its element values — once.</summary>
    public bool Released;

    /// <summary>
    /// V10: a builtin handle whose lifetime is outside the model — a timer, an addlistener
    /// listener, a MAT-file, a VideoWriter — whose contents are never released by a count.
    /// </summary>
    public bool External;

    /// <summary>The elements, column-major. Empty for a struct array with no elements.</summary>
    public Dictionary<string, JgsValue>[] Elements;

    /// <summary>
    /// The field names of an <em>empty</em> struct array — <c>struct('a', {})</c> has a field and no
    /// element to read it from. Authoritative only when <see cref="Elements"/> is empty: everywhere
    /// else element zero's keys are the truth, because field writes go through the dictionary and a
    /// second copy of the names could silently drift out of step with them.
    /// </summary>
    public string[] EmptyFields;

    public JgsStructArray(Dictionary<string, JgsValue>[] elements, string[]? emptyFields = null)
    {
        Elements = elements;
        EmptyFields = emptyFields ?? [];
    }

    /// <summary>The number of elements.</summary>
    public int Length => Elements.Length;

    /// <summary>
    /// The field names, in order — element zero's when there is one, and the remembered names when
    /// the array is empty.
    /// </summary>
    public string[] FieldNames => Elements.Length == 0 ? EmptyFields : [.. Elements[0].Keys];

    /// <summary>A fresh element carrying the array's fields, each holding <c>[]</c>.</summary>
    /// <remarks>
    /// Growing a struct array past its end fills the gap with these rather than with empty structs.
    /// MATLAB's rule is that every element of a struct array has every field, so a gap element that
    /// had no fields at all would be a value the type says cannot exist.
    /// </remarks>
    public Dictionary<string, JgsValue> NewElement()
    {
        var element = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in FieldNames)
        {
            element[field] = JgsEmpty.Zero();
        }

        return element;
    }

    /// <summary>
    /// Adds <paramref name="field"/> to every element that lacks it, holding <c>[]</c>, and records
    /// it when the array is empty. This is the invariant the old cell-of-structs could not hold:
    /// writing <c>S(2).b = 1</c> gives element one a <c>b</c> as well.
    /// </summary>
    public void EnsureField(string field)
    {
        if (Elements.Length == 0)
        {
            if (System.Array.IndexOf(EmptyFields, field) < 0)
            {
                EmptyFields = [.. EmptyFields, field];
            }

            return;
        }

        for (int i = 0; i < Elements.Length; i++)
        {
            if (!Elements[i].ContainsKey(field))
            {
                WritableElement(i)[field] = JgsEmpty.Zero(); // [] is 0-by-0, as MATLAB writes it
            }
        }
    }

    /// <summary>
    /// M3/M7: element <paramref name="index"/>'s fields, ready to be written. An element dictionary
    /// is a payload of its own, because two struct arrays can hold the same one — a selection
    /// (<c>S(2:3)</c>) and a shallow detach both leave them shared — so a write into one copies it
    /// first, shares its values, and puts the copy in this array's slot. The gate lives here rather
    /// than at the call sites for the reason M7 gives: a caller cannot forget what it cannot see.
    /// </summary>
    public Dictionary<string, JgsValue> WritableElement(int index)
    {
        Dictionary<string, JgsValue> element = Elements[index];
        if (!JgsHolders.IsShared(element))
        {
            return element;
        }

        Dictionary<string, JgsValue> copy = SharedCopy(element);
        Elements[index] = copy;
        JgsHolders.Release(element);
        return copy;
    }

    /// <summary>
    /// A new dictionary holding a counted share of each of <paramref name="element"/>'s values —
    /// M3's shallow detach of one element, and what a verb that rebuilds a struct from another's
    /// fields starts from (M2: the rebuilt struct's fields are entries of their own).
    /// </summary>
    public static Dictionary<string, JgsValue> SharedCopy(Dictionary<string, JgsValue> element)
    {
        var copy = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string name, JgsValue held) in element)
        {
            copy[name] = JgsValue.Share(held);
        }

        return copy;
    }

    /// <summary>Every element's dictionary, deep-copied — the copy a MATLAB value assignment makes.</summary>
    public JgsStructArray Clone()
    {
        var copies = new Dictionary<string, JgsValue>[Elements.Length];
        for (int i = 0; i < copies.Length; i++)
        {
            copies[i] = new Dictionary<string, JgsValue>(Elements[i], StringComparer.Ordinal);
        }

        return new JgsStructArray(copies, EmptyFields);
    }
}
