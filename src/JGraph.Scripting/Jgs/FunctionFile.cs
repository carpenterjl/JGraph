namespace JGraph.Scripting.Jgs;

/// <summary>
/// The file-level functions of one source — the main script, a path file, a script run by name —
/// kept with the file and in no workspace (M145, step 5). A name a file mentions is answered from
/// the file's own storage by the resolver, so a path function never finds the calling script's
/// local <c>sum</c>, two scripts' same-named <c>helper</c>s coexist, and a called script's functions
/// no longer overwrite the caller's.
/// </summary>
/// <remarks>
/// Every function here closes over <see cref="Scope"/>, whose parent is the built-in layer rather
/// than the base workspace: the walk from a function's frame reaches its parameters, its nested
/// functions and the built-ins, and never a variable the script happened to set — MATLAB's rule for
/// a path function and for a script's local function alike. Nested functions do not live here; they
/// are declared into the call frame of the function that holds them, one closure per invocation.
/// </remarks>
internal sealed class FunctionFile
{
    private readonly Dictionary<string, JgsValue> _functions = new(StringComparer.Ordinal);

    /// <summary>Creates empty storage for <paramref name="sourceId"/> over <paramref name="scope"/>.</summary>
    public FunctionFile(string sourceId, JgsEnvironment scope)
    {
        SourceId = sourceId;
        Scope = scope;
    }

    /// <summary>The source the functions came from — the file's path, or "" for code with no file.</summary>
    public string SourceId { get; }

    /// <summary>The scope the file's functions close over: a child of the built-in layer, holding nothing itself.</summary>
    public JgsEnvironment Scope { get; }

    /// <summary>The functions by name.</summary>
    public IReadOnlyDictionary<string, JgsValue> Functions => _functions;

    /// <summary>Stores (or replaces) the function <paramref name="name"/>.</summary>
    public void Declare(string name, JgsValue function) => _functions[name] = function;

    /// <summary>The function <paramref name="name"/>, when this file has one.</summary>
    public bool TryGet(string name, out JgsValue value) => _functions.TryGetValue(name, out value!);

    /// <summary>
    /// Whether <paramref name="declaration"/> itself is what this file holds under its name — how the
    /// statement executor tells a hoisted file-level declaration it can skip from a nested one it
    /// must declare into the frame.
    /// </summary>
    public bool Holds(FnStmt declaration) =>
        _functions.TryGetValue(declaration.Name, out JgsValue? held)
        && held.Type == JgsType.Function
        && held.AsCallable is UserFunction user
        && ReferenceEquals(user.Declaration, declaration);
}
