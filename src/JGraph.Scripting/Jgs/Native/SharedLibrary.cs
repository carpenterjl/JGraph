using System.Runtime.Versioning;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// A library <c>loadlibrary</c> loaded (ADR 0181): the module in the session's native host, the
/// prototype model it was loaded with, and the exports of that model the module has — the functions
/// <c>libfunctions</c> lists and <c>calllib</c> calls. What the model declares and the module does
/// not export is the load's <c>notfound</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SharedLibrary(string name, string path, NativeHostProcess host, long module, LibraryModel model)
{
    /// <summary>The name a script knows it by: its file's name, or its alias.</summary>
    public string Name { get; } = name;

    /// <summary>The file it was loaded from, or the name the system search was given.</summary>
    public string Path { get; } = path;

    /// <summary>The host it is loaded in; when that host dies, so does the library.</summary>
    public NativeHostProcess Host { get; } = host;

    /// <summary>The module handle in <see cref="Host"/>.</summary>
    public long Module { get; } = module;

    /// <summary>The prototype model it was loaded with.</summary>
    public LibraryModel Model { get; } = model;

    /// <summary>The functions (and exported variables) found in the module, by the name a script calls them by.</summary>
    public Dictionary<string, LibFunction> Functions { get; } = new(StringComparer.Ordinal);

    /// <summary>The address of each of <see cref="Functions"/> in <see cref="Host"/>.</summary>
    public Dictionary<string, long> Addresses { get; } = new(StringComparer.Ordinal);
}
