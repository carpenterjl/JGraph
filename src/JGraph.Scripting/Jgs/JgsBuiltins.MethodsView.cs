using System.Runtime.Versioning;
using System.Text;
using JGraph.Scripting.Jgs.Native;
using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>methodsview</c> and <c>libfunctionsview</c> (interop plan, stage 10, ADR 0183), and what
/// <c>methods</c> says of a library, a <c>lib.pointer</c> and a libstruct, which both are built on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transcribed.</b> R2025b's <c>methodsview.m</c> and <c>libfunctionsview.m</c> are readable, and
/// the table here is theirs step by step: the <c>-full</c> lines split into name, return type,
/// arguments, qualifiers and the class a method is inherited from; the columns reordered; the rows
/// sorted by name; only the columns some row fills kept; and <c>libfunctionsview</c> dropping the
/// inheritance column and saying "library". The <c>'noUI'</c> form answers the headers and rows as
/// string arrays, as the file does. One step is left out: the file blanks the inheritance of the
/// lines named like whichever line R2025b happens to list first, which changes from run to run.
/// </para>
/// <para>
/// <b>The window.</b> R2025b shows the table in a <c>uifigure</c>. The app shows it in a window of
/// its own through the host's <see cref="IScriptTableViewer"/>; a host without windows (the CLI, a
/// batch run) prints the same table, which R2025b has no counterpart for.
/// </para>
/// <para>
/// R2025b's rows for a .NET type are not stable from run to run (the order <c>methods -full</c>
/// lists overloads of one name in changes); JGraph lists them in its <c>methods -full</c> order, so
/// its table is the same every time.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly string[] MethodsViewHeaders =
        ["Name", "Return Type", "Arguments", "Qualifiers", "Other", "Inherited From"];

    /// <summary>Declares <c>methodsview</c> and <c>libfunctionsview</c> into <paramref name="env"/>.</summary>
    internal static void RegisterMethodsViewBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        env.Builtins.Register("methodsview", JgsValue.Function(new BuiltinFunction("methodsview",
            (args, line, col) => FirstOf(MethodsView(interpreter, args, 1, line, col)))
        {
            KeepsStringArguments = true,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => MethodsView(interpreter, args, wanted, line, col),
        }));
        env.Builtins.Register("libfunctionsview", JgsValue.Function(new BuiltinFunction("libfunctionsview",
            (args, line, col) => FirstOf(LibFunctionsView(interpreter, args, 1, line, col)))
        {
            KeepsStringArguments = true,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => LibFunctionsView(interpreter, args, wanted, line, col),
        }));
    }

    // ------------------------------------------------------------------------------------------
    // methods of a library, a lib.pointer and a libstruct
    // ------------------------------------------------------------------------------------------

    /// <summary>What <c>methods</c> lists for a library, a pointer or a libstruct class.</summary>
    /// <param name="ClassName">The class: <c>lib.jgtestlib</c>, <c>lib.pointer</c>, <c>lib.jg_point</c>.</param>
    /// <param name="Names">The cell <c>methods</c> answers: every name, inherited ones included.</param>
    /// <param name="Own">The names its printed listing shows in columns.</param>
    /// <param name="Lines">The <c>-full</c> lines.</param>
    /// <param name="IsLibrary">Whether it is a library, whose listing is all <c>Static methods:</c>.</param>
    internal sealed record LibListing(string ClassName, IReadOnlyList<string> Names, IReadOnlyList<string> Own, IReadOnlyList<MethodLine> Lines, bool IsLibrary);

    /// <summary>
    /// The listing for a <c>lib.pointer</c> or libstruct value, or for a <c>lib.…</c> name: a loaded
    /// library, <c>lib.pointer</c>, or a struct a loaded library declares. Null for anything else;
    /// <paramref name="unknown"/> says a <c>lib.…</c> name named nothing (R2025b: "No class").
    /// </summary>
    internal static LibListing? LibListingOf(JgsValue value, Interpreter interpreter, out bool unknown)
    {
        unknown = false;
        switch (value.AsExternalOrNull())
        {
            case LibPointer:
                return PointerListing();
            case LibStructValue structure:
                return StructListing(structure.Type.Name);
        }

        if (!IsTextScalar(value) || TextOf(value) is not { } name || !name.StartsWith("lib.", StringComparison.Ordinal))
        {
            return null;
        }

        string rest = name[4..];
        if (rest == "pointer")
        {
            return PointerListing();
        }

        if (OperatingSystem.IsWindows() && interpreter.Host is { } host && LoadedLibraryListing(host, rest) is { } listing)
        {
            return listing;
        }

        unknown = true;
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static LibListing? LoadedLibraryListing(JGraphScriptGlobals host, string name)
    {
        if (host.Native.Library(name) is { } library)
        {
            IReadOnlyList<string> functions = LibMethodsListing.LibraryNames(library);
            return new LibListing("lib." + name, functions, functions, LibMethodsListing.Library(library), IsLibrary: true);
        }

        return host.Native.Libraries.Values.Any(l => l.Model.Struct(name) is not null) ? StructListing(name) : null;
    }

    private static LibListing PointerListing() => new(
        "lib.pointer", LibPointer.MethodNames, ["get", "isNull", "plus", "reshape", "set", "setdatatype"],
        LibMethodsListing.Pointer(), IsLibrary: false);

    private static LibListing StructListing(string name) => new(
        "lib." + name, LibStructValue.MethodNamesOf(name),
        [.. new[] { "get", name, "set", "structsize" }.Order(StringComparer.OrdinalIgnoreCase)],
        LibMethodsListing.Struct(name), IsLibrary: false);

    // ------------------------------------------------------------------------------------------
    // methodsview and libfunctionsview
    // ------------------------------------------------------------------------------------------

    /// <summary><c>methodsview(x)</c>, <c>methodsview(x, 'libfunctionsview')</c> and <c>[h, d] = methodsview(x, 'noUI')</c> (methodsview.m).</summary>
    private static JgsValue[] MethodsView(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:methodsview:nargin", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        bool noUI = false;
        bool library = false;
        if (args.Count == 2)
        {
            switch (IsTextScalar(args[1]) ? TextOf(args[1]).ToLowerInvariant() : null)
            {
                case "noui":
                    if (wanted != 2)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:methodsview:InvalidNumberOfOutputs",
                            "Number of output arguments with noUI must be equal to 2.");
                    }

                    noUI = true;
                    break;
                case "libfunctionsview":
                    library = true;
                    break;
                default:
                    throw new JgsRuntimeException(line, col, "MATLAB:methodsview:InvalidInputOption",
                        "Argument option must be assigned a valid character vector.");
            }
        }

        if (args.Count == 1 && wanted > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:methodsview:TooManyOutputs",
                "There should be no output arguments with only one input argument");
        }

        JgsValue subject = args[0];
        bool isChar = subject.Type == JgsType.String || subject.IsCharMatrix;
        if (subject.IsStringArray && subject.ArrayLength != 1)
        {
            // A string array is an object to isobject, so methodsview.m hands it to methods, which
            // wants one piece of text (probe_views3).
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
        }

        bool notChar = !isChar && !IsStringScalar(subject);
        if ((notChar && !IsObjectValue(subject)) || (subject.IsCharMatrix && JgsMatrix.RowCount(subject) > 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:methodsview:InvalidInput", "Input must be a character vector or object.");
        }

        string className;
        (IReadOnlyList<MethodLine> Lines, bool NamesOnly) found;
        if (notChar)
        {
            className = ClassOf(subject, JgsDialect.Matlab);
            found = FullLinesOf(subject, interpreter);
        }
        else
        {
            className = TextOf(subject);
            found = FullLinesOf(JgsValue.Str(className), interpreter);

            // A name an import reaches (import System.Text.*; methodsview StringBuilder).
            if (found.Lines.Count == 0 && !className.Contains('.', StringComparison.Ordinal)
                && interpreter.ImportFor(className, interpreter.CurrentFrame, head: false) is { Type: { } imported, Method: null })
            {
                found = (FullLines(imported), false);
            }
        }

        string kind = library ? "library" : "class";
        if (!noUI && found.Lines.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:methodsview:UnknownClassOrMethod",
                $"No {kind} {className} can be located or no methods for {kind}");
        }

        (string[] headers, string[][] rows) = MethodsTable(found.Lines, found.NamesOnly, className, library);
        if (noUI)
        {
            return
            [
                JgsValue.StringArray([.. headers.Select(JgsValue.Str)], headers.Length, 1),
                StringMatrix(rows, headers.Length),
            ];
        }

        string title = className.StartsWith("lib.", StringComparison.Ordinal)
            ? "Functions in library " + className.Replace("lib.", "", StringComparison.Ordinal)
            : "Methods for class " + className;
        if (interpreter.Host is { } host)
        {
            if (host.TableViewer is { } viewer)
            {
                viewer.ShowTable(title, headers, rows);
            }
            else
            {
                host.WriteOut(TableText(title, headers, rows));
            }
        }

        return [];
    }

    /// <summary><c>libfunctionsview(lib)</c> or <c>libfunctionsview(obj)</c>: its class's table, headed "library" (libfunctionsview.m).</summary>
    private static JgsValue[] LibFunctionsView(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (wanted > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:LIBFUNCTIONSVIEW:NumberOfInputArguments", "Not enough input arguments.");
        }

        // convertStringsToChars: a string scalar is a char row, a string array a cell (refused below).
        JgsValue subject = args[0];
        bool isChar = subject.Type == JgsType.String || subject.IsCharMatrix || IsStringScalar(subject);
        bool opaque = subject.Type == JgsType.External;
        if ((!isChar && !opaque) || (subject.IsCharMatrix && JgsMatrix.RowCount(subject) > 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:LIBFUNCTIONSVIEW:InputType", "Input must be a character vector or object.");
        }

        string className = isChar ? "lib." + TextOf(subject) : ClassOf(subject, JgsDialect.Matlab);
        return MethodsView(interpreter, [JgsValue.Str(className), JgsValue.Str("libfunctionsview")], 0, line, col);
    }

    /// <summary>
    /// The <c>-full</c> lines <c>methods</c> gives for <paramref name="value"/>: a library's, a
    /// pointer's or a libstruct's, a .NET type's, or for a class that has no <c>-full</c> listing its
    /// method names alone (<c>NamesOnly</c>, methodsview.m's path for an empty <c>d</c>). Empty when it
    /// names nothing.
    /// </summary>
    private static (IReadOnlyList<MethodLine> Lines, bool NamesOnly) FullLinesOf(JgsValue value, Interpreter interpreter)
    {
        if (LibListingOf(value, interpreter, out _) is { } listing)
        {
            return (listing.Lines, false);
        }

        if (NetTypeNamed(value, interpreter) is { } type)
        {
            return (FullLines(type), false);
        }

        if (value.Type == JgsType.Object)
        {
            return ([.. value.AsObject.Class.MethodNames.Select(static n => new MethodLine(n, null))], true);
        }

        if (NamedClass(value, interpreter) is { } definition)
        {
            return ([.. definition.MethodNames.Select(static n => new MethodLine(n, null))], true);
        }

        return ([], false);
    }

    /// <summary>A .NET type's <c>-full</c> lines, the inherited ones split from their note.</summary>
    private static IReadOnlyList<MethodLine> FullLines(Type type)
    {
        const string Note = "  % Inherited from ";
        return
        [
            .. NetMethodsListing.FullLines(type).Select(static text =>
                text.IndexOf(Note, StringComparison.Ordinal) is var at and >= 0
                    ? new MethodLine(text[..at], text[(at + Note.Length)..])
                    : new MethodLine(text, null)),
        ];
    }

    /// <summary>
    /// methodsview.m's table: the headers and the rows, over <paramref name="lines"/> of
    /// <paramref name="className"/>. Each line splits into qualifiers, return type, name, arguments and
    /// the defining class and name (<c>handle.delete</c>), which are methods' own six columns; they
    /// are reordered to name, return type, arguments, qualifiers, other and inherited-from, sorted by
    /// name, and cut to the columns some row fills.
    /// </summary>
    internal static (string[] Headers, string[][] Rows) MethodsTable(
        IReadOnlyList<MethodLine> lines, bool namesOnly, string className, bool library)
    {
        const int Columns = 6;
        int count = lines.Count;
        var d = new string[count][];
        for (int i = 0; i < count; i++)
        {
            d[i] = new string[Columns];
            Array.Fill(d[i], "");
            MethodLine entry = lines[i];
            if (namesOnly)
            {
                d[i][0] = entry.Text;
                continue;
            }

            string rest = entry.Text;
            if (rest.StartsWith("Static ", StringComparison.Ordinal))
            {
                d[i][3] = "Static";
                rest = rest["Static ".Length..];
            }

            int open = rest.IndexOf('(');
            string head = (open < 0 ? rest : rest[..open]).TrimEnd();
            string name = head[(head.LastIndexOf(' ') + 1)..];
            d[i][0] = name;
            d[i][1] = head[..^name.Length].Trim();
            d[i][2] = open < 0 ? "" : rest[open..];
            d[i][5] = (entry.From ?? className) + "." + name;
        }

        // methodsview.m also blanks the inheritance of every line named like the first line whose
        // defining name ends in its own name (its cls). Which line that is follows the order
        // R2025b's methods hands back, which it does not keep: the same lib.pointer table showed
        // addlistener inherited from handle in one run and from nothing in the next (probe_views,
        // views_methods). JGraph names the class every inherited line comes from (ADR 0183).
        foreach (string[] row in d)
        {
            if (row[2] == "()")
            {
                row[2] = "( )";
            }

            string from = row[5].TrimEnd();
            if (from.LastIndexOf('.') is var last and >= 0)
            {
                from = from[..last];
            }

            // "Don't show inheritance value if it's the same as the class name."
            row[5] = from.Replace(className, "", StringComparison.Ordinal);
        }

        bool[] shown = new bool[Columns];
        for (int j = 0; j < Columns; j++)
        {
            shown[j] = d.Any(row => row[j].Length > 0) && !(library && j == 5);
        }

        int[] kept = [.. Enumerable.Range(0, Columns).Where(j => shown[j])];
        string[] headers = [.. kept.Select(j => MethodsViewHeaders[j])];
        string[][] rows =
        [
            .. d.OrderBy(static row => row[0], StringComparer.Ordinal)
                .Select(row => kept.Select(j => row[j]).ToArray()),
        ];
        return (headers, rows);
    }

    /// <summary>An r-by-c string array of <paramref name="rows"/>.</summary>
    private static JgsValue StringMatrix(string[][] rows, int columns)
    {
        if (rows.Length == 0)
        {
            return JgsValue.StringArray([], 0, 0);
        }

        var elements = new JgsValue[rows.Length * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                elements[c * rows.Length + r] = JgsValue.Str(rows[r][c]);
            }
        }

        return JgsValue.StringArray(elements, rows.Length, columns);
    }

    /// <summary>The table as text, for a host with no window: the title, then the columns aligned.</summary>
    internal static string TableText(string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        int[] widths = [.. headers.Select((h, j) => rows.Aggregate(h.Length, (w, row) => Math.Max(w, row[j].Length)))];
        var text = new StringBuilder("\n").Append(title).Append("\n\n");
        void Row(IReadOnlyList<string> cells)
        {
            var line = new StringBuilder();
            for (int j = 0; j < cells.Count; j++)
            {
                line.Append(j == cells.Count - 1 ? cells[j] : cells[j].PadRight(widths[j] + 2));
            }

            text.Append(line.ToString().TrimEnd()).Append('\n');
        }

        Row(headers);
        Row([.. widths.Select(static w => new string('-', w))]);
        foreach (string[] row in rows)
        {
            Row(row);
        }

        return text.Append('\n').ToString();
    }
}
