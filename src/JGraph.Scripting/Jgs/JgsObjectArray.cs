namespace JGraph.Scripting.Jgs;

/// <summary>
/// The storage behind a <see cref="JgsType.Object"/> value that is not one object (open item 65):
/// a flat, column-major list of instances of one value class, as <see cref="JgsStructArray"/> is
/// for a struct. A lone object keeps its <see cref="JgsObject"/> payload, so the ~200 call sites
/// that read <see cref="JgsValue.AsObject"/> go on reading one instance; an array has none or
/// several, which is what <c>[s s2]</c>, <c>T.Style</c> and <c>a(3) = s</c> make.
/// </summary>
/// <remarks>
/// Each element is a payload of its own with a holder count, so a selection (<c>a(2)</c>) shares
/// the instance and a later property write through either side copies first — the value
/// semantics R2025b gives an array of a value class (probe <c>probe_65</c>: <c>a(1).FontWeight =
/// 'normal'</c> leaves <c>s</c> bold). A handle class's elements are the objects themselves, so a
/// write through any array or alias lands on the one object (open item 116).
/// </remarks>
internal sealed class JgsObjectArray
{
    // How many entries hold this payload (M1). Zero and one both mean one holder.
    private int _holders;

    public JgsObjectArray(JgsClass definition, JgsObject[] elements)
    {
        Class = definition;
        Elements = elements;
    }

    /// <summary>The holder count's storage (M1); zero means one holder.</summary>
    public ref int HolderSlot => ref _holders;

    /// <summary>The class every element is an instance of — the array's own class even when it is empty.</summary>
    public JgsClass Class { get; }

    /// <summary>The elements, column-major. Empty for an array with none.</summary>
    public JgsObject[] Elements { get; }

    /// <summary>The number of elements.</summary>
    public int Length => Elements.Length;

    /// <summary>
    /// Element <paramref name="index"/>, ready to be written: an instance another holder shares is
    /// copied first, shallowly, and the copy put in this array's slot, so a write through one
    /// holder is never seen through another (M3, M7).
    /// </summary>
    public JgsObject WritableElement(int index)
    {
        JgsObject element = Elements[index];
        if (Class.IsHandle || !JgsHolders.IsShared(element))
        {
            return element; // a handle is written where it is, for every alias (open item 116)
        }

        JgsObject copy = SharedCopy(element);
        Elements[index] = copy;
        JgsHolders.Release(element);
        return copy;
    }

    /// <summary>A new instance holding a counted share of each of <paramref name="source"/>'s properties.</summary>
    public static JgsObject SharedCopy(JgsObject source)
    {
        var copy = new JgsObject(source.Class);
        foreach ((string name, JgsValue held) in source.Fields)
        {
            copy.Fields[name] = JgsValue.Share(held);
        }

        return copy;
    }

    /// <summary>
    /// The array as the value a script sees: one element is the object itself, as a 1-by-1 is in
    /// MATLAB; anything else is the array of the given shape.
    /// </summary>
    public static JgsValue ValueOf(JgsClass definition, JgsObject[] elements, int rows, int cols) =>
        elements.Length == 1 && rows == 1 && cols == 1
            ? JgsValue.Object(elements[0])
            : JgsValue.ObjectArray(new JgsObjectArray(definition, elements), rows, cols);

    /// <summary>
    /// <see cref="ValueOf(JgsClass, JgsObject[], int, int)"/> in the shape of <paramref name="shapeOf"/>,
    /// a third dimension and past included (open item 121).
    /// </summary>
    public static JgsValue ValueLike(JgsClass definition, JgsObject[] elements, JgsValue shapeOf)
    {
        JgsValue made = ValueOf(definition, elements, shapeOf.Rows, shapeOf.Cols);
        if (shapeOf.IsNd && made.IsObjectArray)
        {
            made.ReshapeDims(shapeOf.Dims);
        }

        return made;
    }

    /// <summary>
    /// A numeric stand-in for an object value: the positions <paramref name="offset"/> onward of its
    /// elements, in its shape (2-D or N-D), for a verb or a subscript to rearrange (open item 121).
    /// </summary>
    public static JgsValue PositionsLike(JgsValue objects, int offset)
    {
        int count = objects.IsObjectArray ? objects.ArrayLength : 1;
        var positions = new double[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = offset + i;
        }

        int[] dims = objects.IsObjectArray ? objects.Dims : [1, 1];
        return JgsMatrix.FromColumnMajorDims(positions, dims);
    }
}
