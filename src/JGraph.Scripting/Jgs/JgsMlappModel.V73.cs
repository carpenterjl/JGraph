using JGraph.Scripting.MatFile;
using JGraph.Scripting.MatFile.Hdf5;

namespace JGraph.Scripting.Jgs;

/// <content>
/// The version 7.3 form of the model (open item 57): HDF5, where <c>code</c> is a root group whose
/// fields are datasets and groups, a cell a dataset of references to its elements, a struct array
/// a group of such datasets and a one-element struct a group of its values (R2025b, probe
/// oi57_build). A field this did not change keeps its object, and a line of a cell this did not
/// change keeps its object too; the new <c>code</c> group lists the old objects beside the new
/// ones, and the splice appends the lot and re-points the root entry.
/// </content>
internal static partial class JgsMlappModel
{
    private sealed class V73Copy : ICodeCopy
    {
        private readonly byte[] _mat;
        private readonly IReadOnlyList<(string Name, FieldValue Value)> _held;

        private V73Copy(byte[] mat, CodeFields fields)
        {
            _mat = mat;
            _held = [.. fields.All];
            Fields = fields;
        }

        public CodeFields Fields { get; }

        /// <summary>The file's <c>code</c> struct; null when the bytes are not a version 7.3 file this can splice, or it has no such struct.</summary>
        public static V73Copy? Open(byte[] mat)
        {
            if (!MatV73Splice.CanSplice(mat))
            {
                return null;
            }

            Hdf5Object? code = Hdf5File.Open(mat).Root.Child(Variable);
            if (code is null || !code.IsGroup || code.Attributes.ContainsKey("MATLAB_empty")
                || (code.Attributes.TryGetValue("MATLAB_class", out Hdf5Attribute? tag) && tag.AsText() != "struct"))
            {
                return null;
            }

            var fields = new List<(string, FieldValue)>();
            foreach (string name in FieldNames(code))
            {
                if (code.Child(name) is not { } child)
                {
                    return null; // a field the group declares and does not hold
                }

                fields.Add((name, Decode(child)));
            }

            return new V73Copy(mat, new CodeFields(fields));
        }

        public byte[] Write()
        {
            var splice = new MatV73Splice(_mat);
            var entries = new List<(string Name, long Header)>();
            foreach ((string name, FieldValue value) in Fields.All)
            {
                entries.Add((name, Address(splice, name, value)));
            }

            return splice.Finish(Variable, splice.Struct(entries));
        }

        // --- Between the model and the file's objects -------------------------------------------

        /// <summary>A struct's field names in MATLAB's order, which the group's attribute keeps; the group's own order failing that.</summary>
        private static IReadOnlyList<string> FieldNames(Hdf5Object group) =>
            group.Attributes.TryGetValue("MATLAB_fields", out Hdf5Attribute? declared) ? declared.Texts() : [.. group.ChildNames];

        private static bool IsEmpty(Hdf5Object node) =>
            node.Attributes.TryGetValue("MATLAB_empty", out Hdf5Attribute? empty) && empty.AsNumber() != 0;

        private static string ClassOf(Hdf5Object node) =>
            node.Attributes.TryGetValue("MATLAB_class", out Hdf5Attribute? tag) ? tag.AsText() : node.IsGroup ? "struct" : "double";

        private static FieldValue Decode(Hdf5Object node)
        {
            long origin = node.Address;
            if (IsEmpty(node))
            {
                return new EmptyValue(origin);
            }

            if (node.IsGroup)
            {
                return FunctionsOf(node) is { } entries ? new FunctionsValue(entries, origin) : new OpaqueValue(origin);
            }

            switch (ClassOf(node))
            {
                case "char":
                    return TextOf(node) is { } text ? new TextValue(text, origin) : new OpaqueValue(origin);
                case "cell":
                    return LinesOf(node) is { } lines ? new LinesValue(lines.Lines, origin, lines.Origins) : new OpaqueValue(origin);
                default:
                    return new OpaqueValue(origin);
            }
        }

        /// <summary>A char row's text; null when the object is anything else. An empty value is the empty text.</summary>
        private static string? TextOf(Hdf5Object node)
        {
            if (IsEmpty(node))
            {
                return string.Empty;
            }

            if (node.IsGroup || ClassOf(node) != "char")
            {
                return null;
            }

            JgsValue value = MatV73Reader.ValueOf(node, Variable);
            return value.Type == JgsType.String ? value.AsString : null;
        }

        /// <summary>A cell of char rows as lines, each with the address of its object; null when the object is anything else. An empty value is no lines.</summary>
        private static (List<string> Lines, List<object> Origins)? LinesOf(Hdf5Object node)
        {
            if (IsEmpty(node))
            {
                return ([], []);
            }

            if (node.IsGroup || ClassOf(node) != "cell" || node.Datatype.Class != Hdf5Datatype.ClassReference)
            {
                return null;
            }

            var lines = new List<string>();
            var origins = new List<object>();
            foreach (long address in ReferencesOf(node))
            {
                if (TextOf(node.At(address)) is not { } line)
                {
                    return null;
                }

                lines.Add(line);
                origins.Add(address);
            }

            return (lines, origins);
        }

        /// <summary>The addresses a dataset of references holds, in the order they are stored.</summary>
        private static List<long> ReferencesOf(Hdf5Object node)
        {
            Hdf5Datatype type = node.Datatype;
            byte[] data = node.ReadData();
            var addresses = new List<long>((int)node.ElementCount);
            for (int i = 0; i < node.ElementCount; i++)
            {
                addresses.Add(Hdf5File.ReadWord(data, i * type.Size, type.Size));
            }

            return addresses;
        }

        /// <summary>
        /// A struct array with the fields <c>Name</c> and <c>Code</c>; null when the group is anything
        /// else. More than one element is a group of two reference datasets; one element is a group of
        /// the two values, which is how MATLAB writes a 1-by-1 struct.
        /// </summary>
        private static List<FunctionEntry>? FunctionsOf(Hdf5Object group)
        {
            if (ClassOf(group) != "struct" || !FieldNames(group).SequenceEqual(["Name", "Code"], StringComparer.Ordinal))
            {
                return null;
            }

            if (group.Child("Name") is not { } names || group.Child("Code") is not { } codes)
            {
                return null;
            }

            var entries = new List<FunctionEntry>();
            bool array = !names.IsGroup && !names.Attributes.ContainsKey("MATLAB_class") && !IsEmpty(names)
                && names.Datatype.Class == Hdf5Datatype.ClassReference;
            if (!array)
            {
                if (TextOf(names) is not { } name)
                {
                    return null;
                }

                entries.Add(Entry(name, codes));
                return entries;
            }

            if (codes.IsGroup || codes.Datatype.Class != Hdf5Datatype.ClassReference)
            {
                return null;
            }

            List<long> nameRefs = ReferencesOf(names);
            List<long> codeRefs = ReferencesOf(codes);
            if (nameRefs.Count != codeRefs.Count)
            {
                return null;
            }

            for (int i = 0; i < nameRefs.Count; i++)
            {
                if (TextOf(group.At(nameRefs[i])) is not { } name)
                {
                    return null;
                }

                entries.Add(Entry(name, group.At(codeRefs[i])));
            }

            return entries;
        }

        /// <summary>A function and its body: a cell of char rows, or the empty char a body with no lines is, or something else kept as it is.</summary>
        private static FunctionEntry Entry(string name, Hdf5Object body)
        {
            if (IsEmpty(body))
            {
                return new FunctionEntry(name, [], body.Address, []);
            }

            (List<string> Lines, List<object> Origins)? lines = LinesOf(body);
            return new FunctionEntry(name, lines?.Lines, body.Address, lines?.Origins);
        }

        // --- Writing ---------------------------------------------------------------------------------

        /// <summary>The address of a field's object: the one the file holds when the value is unchanged, a new one otherwise.</summary>
        private long Address(MatV73Splice splice, string field, FieldValue value) => value.Origin is long origin ? origin : value switch
        {
            TextValue text => splice.Value(JgsValue.Str(text.Text)),
            LinesValue lines => Cell(splice, lines.Lines, Reusable(HeldLines(field))),
            FunctionsValue functions => Functions(splice, field, functions.Functions),
            _ => throw new InvalidOperationException("a value this did not read must be text, lines or functions"),
        };

        /// <summary>A <c>Name</c>/<c>Code</c> array: one element as a group of its values, more as a group of reference datasets, as MATLAB writes them.</summary>
        private long Functions(MatV73Splice splice, string field, IReadOnlyList<FunctionEntry> functions)
        {
            IReadOnlyList<FunctionEntry> held = HeldFunctions(field);
            if (functions.Count == 1)
            {
                long name = splice.Value(JgsValue.Str(functions[0].Name));
                long body = Body(splice, functions[0], held);
                return splice.Struct([("Name", name), ("Code", body)]).Header;
            }

            var names = new List<long>();
            var bodies = new List<long>();
            foreach (FunctionEntry function in functions)
            {
                names.Add(splice.Value(JgsValue.Str(function.Name)));
                bodies.Add(Body(splice, function, held));
            }

            int[] dims = [1, functions.Count];
            return splice.Struct([("Name", splice.References(names, dims)), ("Code", splice.References(bodies, dims))]).Header;
        }

        /// <summary>A body's address: the one the file holds, or a new cell of its lines reusing the lines the function's old body held.</summary>
        private static long Body(MatV73Splice splice, FunctionEntry entry, IReadOnlyList<FunctionEntry> held) => entry.Body is long address
            ? address
            : entry.Lines is { Count: > 0 } lines
                ? Cell(splice, lines, Reusable(held.FirstOrDefault(h => h.Name == entry.Name)))
                : splice.Value(JgsValue.Str(string.Empty));

        /// <summary>
        /// A 1-by-N cell of char rows: a reference dataset over one object per line, where a line
        /// whose text the file already holds as a line of the same field refers to that object
        /// (an app's editable section can run to thousands of lines; an edit rewrites the few that changed).
        /// </summary>
        private static long Cell(MatV73Splice splice, IReadOnlyList<string> lines, Dictionary<string, long> reusable)
        {
            var addresses = new List<long>(lines.Count);
            foreach (string line in lines)
            {
                addresses.Add(reusable.TryGetValue(line, out long held) ? held : splice.Value(JgsValue.Str(line)));
            }

            return splice.Cell(addresses, [1, lines.Count]);
        }

        /// <summary>The lines a value held in the file, by text, each with its object's address; empty when it held none as objects.</summary>
        private static Dictionary<string, long> Reusable(FunctionEntry? entry) =>
            Reusable(entry?.Lines, entry?.LineOrigins);

        private static Dictionary<string, long> Reusable(LinesValue? lines) =>
            Reusable(lines?.Lines, lines?.LineOrigins);

        private static Dictionary<string, long> Reusable(IReadOnlyList<string>? lines, IReadOnlyList<object>? origins)
        {
            var reusable = new Dictionary<string, long>(StringComparer.Ordinal);
            if (lines is null || origins is null)
            {
                return reusable;
            }

            for (int i = 0; i < lines.Count && i < origins.Count; i++)
            {
                if (origins[i] is long address)
                {
                    reusable.TryAdd(lines[i], address);
                }
            }

            return reusable;
        }

        private LinesValue? HeldLines(string field) =>
            _held.FirstOrDefault(h => h.Name == field).Value as LinesValue;

        private IReadOnlyList<FunctionEntry> HeldFunctions(string field) =>
            (_held.FirstOrDefault(h => h.Name == field).Value as FunctionsValue)?.Functions ?? [];
    }
}
