namespace JGraph.Scripting.Completion;

/// <summary>
/// Names only a live session knows, offered by the editor's completion in a MATLAB buffer (interop
/// plan, stage 10, ADR 0183): what follows a dotted .NET name, and the libraries and functions a
/// <c>calllib('…'</c> string can name. A capability, not part of <see cref="IScriptSession"/>: a host
/// asks its console session with <c>is IScriptCompletionSource</c> and falls back to
/// <see cref="JGraph.Scripting.Jgs.Completion.InteropCompletion.Framework"/>, which knows the .NET
/// framework and no library.
/// </summary>
/// <remarks>
/// Called on the UI thread while the session may be running a statement on its own thread. An
/// implementation reads what it can and answers less, never throws, and runs none of the script's
/// code.
/// </remarks>
public interface IScriptCompletionSource
{
    /// <summary>
    /// What may follow <paramref name="qualifier"/> and a dot: a namespace's namespaces and types, or a
    /// type's static members and an enum's members. Empty when the qualifier names neither.
    /// </summary>
    IReadOnlyList<CompletionItem> Members(string qualifier);

    /// <summary>The libraries <c>loadlibrary</c> has loaded, by the name <c>calllib</c> takes.</summary>
    IReadOnlyList<string> Libraries();

    /// <summary>The functions of a loaded library, each with its <c>libfunctions -full</c> signature.</summary>
    IReadOnlyList<CompletionItem> LibraryFunctions(string library);
}
