namespace JGraph.Scripting.Jgs;

/// <summary>
/// What <c>@name</c> and <c>str2func('name')</c> produce in the MATLAB dialect (M145, step 4): the
/// name, the layer that answered it when the handle was made, and the target that layer held. Calling
/// the handle — from a written <c>h(x)</c>, <c>feval</c>, <c>cellfun</c>, a callback — goes through
/// <see cref="JgsNameResolver.InvokeHandle"/>, the same walk a written call of the name makes with
/// the captured target standing in for its layer, so that a handle and the written call agree.
/// </summary>
/// <remarks>
/// Equality is structural, not interned: two handles are the same value when they name the same
/// thing from the same layer and hold the same target by reference, so <c>isequal(@max, @max)</c>
/// is true, and a handle taken before a file was reloaded differs from one taken after — the
/// reloaded file is a new target. No table of handles exists to root a discarded closure.
/// </remarks>
internal sealed class NamedHandle : IJgsCallable, IJgsMultiCallable
{
    private readonly JgsNameResolver _resolver;

    /// <summary>
    /// Creates the handle over what <paramref name="resolver"/> found for <paramref name="name"/>,
    /// with <paramref name="builtin"/> the built-in of that name if the layer holds one — the target
    /// the built-in method layer can pick on an invocation whatever the handle captured.
    /// </summary>
    public NamedHandle(
        string name, ResolutionLayer layer, IJgsCallable captured, string? file,
        IJgsCallable? builtin, JgsNameResolver resolver)
    {
        Name = name;
        Layer = layer;
        Captured = captured;
        File = file;
        Builtin = builtin;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The layer that answered the name when the handle was made.</summary>
    public ResolutionLayer Layer { get; }

    /// <summary>The target that layer held: a nested, local or file function, or the built-in.</summary>
    public IJgsCallable Captured { get; }

    /// <summary>The file the target came from, or null for a built-in — what <c>functions(h).file</c> reports.</summary>
    public string? File { get; }

    /// <summary>
    /// The built-in of this name, when the layer holds one, whatever the handle captured: the
    /// dispatch table can send an invocation with a double to <c>@max</c>'s built-in even though the
    /// handle captured <c>max.m</c> (R2025b: <c>hm({1})</c> is the file's answer and <c>hm([1 5 3])</c>
    /// the built-in's, from one handle). The layer is sealed, so this never goes stale.
    /// </summary>
    public IJgsCallable? Builtin { get; }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        JgsValue[] outputs = CallMultiple(arguments, wanted: 1, line, column);
        return outputs.Length > 0 ? outputs[0] : JgsValue.Null;
    }

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
        _resolver.InvokeHandle(this, arguments, wanted, line, column);

    /// <summary>Whether <paramref name="other"/> is the same handle value — see the class remarks.</summary>
    public bool SameAs(NamedHandle other) =>
        string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Layer == other.Layer
        && ReferenceEquals(Captured, other.Captured);
}
