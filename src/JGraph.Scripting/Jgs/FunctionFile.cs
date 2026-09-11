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
        PrivateFolder = PrivateFolderOf(sourceId);
    }

    /// <summary>The source the functions came from — the file's path, or "" for code with no file.</summary>
    public string SourceId { get; }

    /// <summary>The scope the file's functions close over: a child of the built-in layer, holding nothing itself.</summary>
    public JgsEnvironment Scope { get; }

    /// <summary>
    /// The name of a function file's main function, or null for a script. The main function is not a
    /// local function of its own file: inside <c>max.m</c> the name <c>max</c> resolves past the
    /// built-in method layer and <c>private/</c> to the file itself, exactly as it does from outside
    /// (R2025b: a handle to <c>max</c> taken inside <c>max.m</c> still lets the built-in answer for a
    /// double), so the resolver treats it as the current-folder layer rather than the local one.
    /// </summary>
    public string? MainName { get; set; }

    /// <summary>
    /// The <c>private/</c> folder this file's code may see, or null for code with no file: the folder
    /// beside the file, or — for a file that is itself private — its own folder, since private
    /// functions call one another. Kept as a full path so the per-call check is a dictionary read.
    /// </summary>
    public string? PrivateFolder { get; }

    /// <summary>
    /// The index's entry for <see cref="PrivateFolder"/>, kept once asked for so that the check a
    /// built-in call makes on its way — "does this file's <c>private/</c> claim the name" — is a
    /// field read and a set lookup, never a path hash.
    /// </summary>
    internal JgsFileIndex.Folder? PrivateEntry { get; set; }

    /// <summary>The functions by name.</summary>
    public IReadOnlyDictionary<string, JgsValue> Functions => _functions;

    private static string? PrivateFolderOf(string sourceId)
    {
        if (sourceId.Length == 0 || !Path.IsPathRooted(sourceId))
        {
            return null;
        }

        try
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(sourceId));
            if (folder is null)
            {
                return null;
            }

            return string.Equals(Path.GetFileName(folder), "private", StringComparison.OrdinalIgnoreCase)
                ? folder
                : Path.Combine(folder, "private");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Stores (or replaces) the function <paramref name="name"/>.</summary>
    public void Declare(string name, JgsValue function) => _functions[name] = function;

    /// <summary>The function <paramref name="name"/>, when this file has one — the main function included.</summary>
    public bool TryGet(string name, out JgsValue value) => _functions.TryGetValue(name, out value!);

    /// <summary>Whether <paramref name="name"/> is this file's main function; see <see cref="MainName"/>.</summary>
    public bool IsMain(string name) => MainName is not null && string.Equals(MainName, name, StringComparison.Ordinal);

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
