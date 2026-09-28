using System.Runtime.Versioning;
using System.Text;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// One line of <c>methods(…, '-full')</c>: the signature, and the class it is inherited from, or null
/// for the class's own (the listing prints <c>  % Inherited from handle</c> after an inherited one; the
/// cell it answers leaves the note off).
/// </summary>
internal readonly record struct MethodLine(string Text, string? From);

/// <summary>
/// What <c>methods</c> says of a loaded library (<c>lib.jgtestlib</c>), of <c>lib.pointer</c> and of a
/// libstruct (<c>lib.jg_point</c>), measured in R2025b (probe_views2; interop plan, stage 10,
/// ADR 0183). <c>methodsview</c> and <c>libfunctionsview</c> are built on the same lines.
/// </summary>
/// <remarks>
/// A library's functions are all <c>Static</c>: the names listing has only its <c>Static methods:</c>
/// part and no line about <c>handle</c>. A pointer and a libstruct are handles with
/// <c>matlab.mixin.SetGet</c>'s <c>get</c> and <c>set</c>; their own lines and the inherited ones come
/// in one list ordered by name, which is R2025b's order.
/// </remarks>
internal static class LibMethodsListing
{
    private const int WindowWidth = 180;
    private const string Handle = "handle";
    private const string SetGet = "matlab.mixin.SetGet";

    /// <summary>The lines <c>handle</c> and <c>matlab.mixin.SetGet</c> give a pointer and a libstruct, by name.</summary>
    private static readonly MethodLine[] HandleLines =
    [
        new("event.listener L addlistener(handle sources, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L addlistener(handle sources, matlab.metadata.Property properties, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L addlistener(handle sources, asciiString propertyname, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L addlistener(handle sources, string propertyname, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L addlistener(handle sources, cell propertynames, char vector eventname, function_handle scalar callback)", Handle),
        new("delete(handle obj)", Handle),
        new("logical TF eq(A, B)", Handle),
        new("handle HM findobj(handle H, varargin)", Handle),
        new("matlab.metadata.Property prop findprop(handle scalar object, asciiString propname)", Handle),
        new("logical TF ge(A, B)", Handle),
        new("varargout get(matlab.mixin.SetGet rhs1, rhs2)", SetGet),
        new("logical TF gt(A, B)", Handle),
        new("logical validity isvalid(handle obj)", Handle),
        new("logical TF le(A, B)", Handle),
        new("event.listener L listener(handle sources, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L listener(handle sources, matlab.metadata.Property properties, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L listener(handle sources, asciiString propertyname, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L listener(handle sources, string propertyname, char vector eventname, function_handle scalar callback)", Handle),
        new("event.proplistener L listener(handle sources, cell propertynames, char vector eventname, function_handle scalar callback)", Handle),
        new("logical TF lt(A, B)", Handle),
        new("logical TF ne(A, B)", Handle),
        new("notify(handle sources, asciiString eventname)", Handle),
        new("notify(handle sources, asciiString eventname, event.EventData scalar eventdata)", Handle),
        new("varargout set(matlab.mixin.SetGet rhs1, rhs2)", SetGet),
    ];

    /// <summary><c>lib.pointer</c>'s own lines.</summary>
    private static readonly MethodLine[] PointerOwn =
    [
        new("logical scalar lhs1 isNull(lib.pointer scalar rhs1)", null),
        new("lib.pointer scalar lhs1 plus(lib.pointer scalar rhs1, int64 scalar rhs2)", null),
        new("reshape(lib.pointer scalar rhs1, double scalar rhs2, double scalar rhs3)", null),
        new("setdatatype(lib.pointer scalar rhs1, string scalar rhs2, double scalar rhs3)", null),
    ];

    /// <summary>The <c>-full</c> lines of <c>lib.pointer</c>.</summary>
    public static IReadOnlyList<MethodLine> Pointer() => ByName(HandleLines.Concat(PointerOwn));

    /// <summary>The <c>-full</c> lines of the libstruct class <c>lib.&lt;name&gt;</c>: two constructors and <c>structsize</c>.</summary>
    public static IReadOnlyList<MethodLine> Struct(string name) => ByName(HandleLines.Concat(
    [
        new($"lib.{name} lhs1 {name}", null),
        new($"lib.{name} lhs1 {name}(InitialValue)", null),
        new($"double lhs1 structsize(lib.{name} rhs1)", null),
    ]));

    /// <summary>The <c>-full</c> lines of a library, one <c>Static</c> line per function, by name.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<MethodLine> Library(SharedLibrary library) =>
    [
        .. library.Functions.Keys.OrderBy(static n => n, StringComparer.Ordinal)
            .Select(n => new MethodLine(library.Model.MethodsSignature(library.Functions[n]), null)),
    ];

    /// <summary>The method names <c>methods</c> answers for a library: its functions, by name.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> LibraryNames(SharedLibrary library) =>
        [.. library.Functions.Keys.OrderBy(static n => n, StringComparer.Ordinal)];

    /// <summary>
    /// The listing <c>methods</c> prints when its answer is not asked for: the class's own names in
    /// columns and the line naming <c>handle</c>'s, or for a library its functions under
    /// <c>Static methods:</c> (probe_views2).
    /// </summary>
    public static string Names(string className, IReadOnlyList<string> own, bool library)
    {
        int width = own.Count == 0 ? 2 : own.Max(static n => n.Length) + 2;
        var text = new StringBuilder("\nMethods for class ").Append(className).Append(":\n\n");
        if (library)
        {
            text.Append("Static methods:\n\n");
        }

        foreach (string row in Columns(own, width))
        {
            text.Append(row).Append('\n');
        }

        if (!library)
        {
            text.Append("\nMethods of ").Append(className).Append(" inherited from handle.\n");
        }

        return text.Append('\n').ToString();
    }

    /// <summary>The listing <c>methods(…, '-full')</c> prints: each line, an inherited one with its note.</summary>
    public static string Full(string className, IReadOnlyList<MethodLine> lines)
    {
        var text = new StringBuilder("\nMethods for class ").Append(className).Append(":\n\n");
        foreach (MethodLine line in lines)
        {
            text.Append(line.Text);
            if (line.From is { } from)
            {
                text.Append("  % Inherited from ").Append(from);
            }

            text.Append('\n');
        }

        return text.Append('\n').ToString();
    }

    /// <summary>The name a <c>-full</c> line is for: the word before its parenthesis, or its last word.</summary>
    public static string NameOf(string text)
    {
        int open = text.IndexOf('(');
        string head = (open < 0 ? text : text[..open]).TrimEnd();
        return head[(head.LastIndexOf(' ') + 1)..];
    }

    /// <summary>Orders lines by name, ignoring case, keeping the order of lines with one name.</summary>
    private static MethodLine[] ByName(IEnumerable<MethodLine> lines) =>
        [.. lines.OrderBy(static l => NameOf(l.Text), StringComparer.OrdinalIgnoreCase)];

    /// <summary>Names in columns filled top to bottom, each padded to <paramref name="width"/> (NetMethodsListing's rule).</summary>
    private static IEnumerable<string> Columns(IReadOnlyList<string> names, int width)
    {
        int columns = Math.Max(1, WindowWidth / width);
        int rows = (names.Count + columns - 1) / columns;
        for (int r = 0; r < rows; r++)
        {
            var line = new StringBuilder();
            for (int i = r; i < names.Count; i += rows)
            {
                line.Append(names[i].PadRight(width));
            }

            yield return line.ToString();
        }
    }
}
