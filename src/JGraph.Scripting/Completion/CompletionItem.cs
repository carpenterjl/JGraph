namespace JGraph.Scripting.Completion;

/// <summary>What a <see cref="CompletionItem"/> is, so editors can pick an icon and an insert behavior.</summary>
public enum CompletionItemKind
{
    /// <summary>A language keyword (<c>let</c>, <c>for</c>, …).</summary>
    Keyword,

    /// <summary>A builtin function, with a known signature.</summary>
    Builtin,

    /// <summary>A user-defined function harvested from a script buffer.</summary>
    Function,

    /// <summary>A variable (a <c>let</c> binding or loop variable).</summary>
    Variable,

    /// <summary>A workspace file offered inside a path-string argument.</summary>
    File,

    /// <summary>A workspace folder offered inside a path-string argument (text ends with <c>/</c>).</summary>
    Folder,

    /// <summary>A .NET namespace offered after a dotted name (<c>System.</c>; ADR 0183).</summary>
    Namespace,

    /// <summary>A .NET type offered after its namespace (<c>System.Text.</c>).</summary>
    Type,

    /// <summary>A static member of a .NET type, or an enum's member, offered after the type (<c>System.Math.</c>).</summary>
    Member,

    /// <summary>A loaded library, or one of its functions, offered inside <c>calllib('…'</c>.</summary>
    Library,

    /// <summary>A device's name offered inside the string that opens it: a port in <c>serialport("…</c> (ADR 0197).</summary>
    Device,
}

/// <summary>
/// One completion suggestion. <see cref="Signature"/> is the call shape for functions (used for the
/// parameter-placeholder insertion and signature help), null for keywords and variables.
/// </summary>
public sealed record CompletionItem(string Text, CompletionItemKind Kind, string? Signature = null, string? Description = null);
