using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>whos</c> in the MATLAB dialect (open item 128, probe_128, probe_128b, probe_128c, probe_128d):
/// over the workspace or, with <c>-file</c>, over a MAT-file's variables, a struct array of name,
/// size, bytes, class, the four flags and the nesting when an answer is asked for, and R2025b's
/// listing when it is not - names in ordinal order, sizes aligned on their first <c>x</c>, an object
/// in a file sized <c>-</c>. Bytes follow R2025b's measured rules, the same in memory and on file:
/// the elements of an array, 120 more for each element of a cell or each field of each element of a
/// struct, 16 plus 8 a field plus the names' letters for a struct's field names, 8 for a cell
/// element never set. An object's bytes are a sum of its parts here, which R2025b's are not.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>What <c>whos</c> says of one variable.</summary>
    private sealed record WhosEntry(string Name, int[]? Size, long Bytes, string Class, bool Global, bool Sparse, bool Complex);

    private static readonly string[] WhosFields = ["name", "size", "bytes", "class", "global", "sparse", "complex", "nesting", "persistent"];

    /// <summary>
    /// <c>whos</c>, <c>whos(patterns...)</c>, <c>whos('-file', f, patterns...)</c> in the MATLAB dialect:
    /// the struct array for <paramref name="wanted"/> one, the listing printed for none.
    /// </summary>
    internal static JgsValue[] WhosMatlab(
        IReadOnlyList<JgsValue> args, int wanted, IEnumerable<(string Name, JgsValue Value)> workspace, Func<string, bool> isGlobal,
        string function, Action<string> print, Func<string, string> resolve, int line, int col)
    {
        var words = new List<string>();
        foreach (JgsValue arg in args)
        {
            words.Add(arg.Type == JgsType.String ? arg.AsString
                : IsStringScalar(arg) ? arg.ElementAt(0).AsString
                : throw new JgsRuntimeException(line, col, "MATLAB:whos:InputsMustBeText", "Inputs must be character vectors or string scalars."));
        }

        List<WhosEntry> entries;
        int fileAt = words.FindIndex(static w => w.Equals("-file", StringComparison.OrdinalIgnoreCase));
        if (fileAt >= 0)
        {
            if (fileAt + 1 >= words.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:whos:noFileName", "The -FILE option must be followed by a file name.");
            }

            string path = words[fileAt + 1];
            words.RemoveRange(fileAt, 2);
            entries = WhosFileEntries(path, resolve, line, col);
        }
        else
        {
            entries = [.. workspace.Select(v => WhosEntryOf(v.Name, v.Value, inFile: false, isGlobal(v.Name)))];
        }

        List<Regex> selectors = WhosSelectors(words);
        entries = [.. entries
            .Where(e => selectors.Count == 0 || selectors.Any(s => s.IsMatch(e.Name)))
            .OrderBy(static e => e.Name, StringComparer.Ordinal)];
        if (wanted == 0)
        {
            if (entries.Count > 0)
            {
                print(WhosListing(entries)[..^1]);
            }

            return [];
        }

        // A workspace's variables nest in the function listing them (probe_128c: whwsf); a file's in none.
        return [WhosStruct(entries, fileAt >= 0 ? string.Empty : function)];
    }

    /// <summary>The names a pattern list keeps: wildcards, or after <c>-regexp</c> expressions.</summary>
    private static List<Regex> WhosSelectors(IEnumerable<string> words)
    {
        var selectors = new List<Regex>();
        bool regexp = false;
        foreach (string word in words)
        {
            if (word == "-regexp")
            {
                regexp = true;
                continue;
            }

            string pattern = regexp ? word : "^" + Regex.Escape(word).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            selectors.Add(new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        }

        return selectors;
    }

    /// <summary>A MAT-file's variables, by their values where the file can be read and by their headers otherwise.</summary>
    private static List<WhosEntry> WhosFileEntries(string path, Func<string, string> resolve, int line, int col)
    {
        string named = Path.HasExtension(path) ? path : path + ".mat";
        string source = resolve(named);
        if (!File.Exists(source))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:whos:fileIsNotThere", $"Could not find {named}.");
        }

        try
        {
            IReadOnlyList<(string Name, int[] Dims, string Class)> described = MatFileReader.Describe(source);
            // Each variable read on its own, so one this build cannot read (an object whose class is not
            // here, a handle) keeps its header's answer without costing the others theirs.
            var values = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach ((string name, _, _) in described)
            {
                try
                {
                    if (MatFileReader.Read(source, new HashSet<string>(StringComparer.Ordinal) { name }) is [var (_, read)])
                    {
                        values[name] = read;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or JgsRuntimeException or NotSupportedException)
                {
                }
            }

            var entries = new List<WhosEntry>();
            foreach ((string name, int[] dims, string className) in described)
            {
                entries.Add(values.TryGetValue(name, out JgsValue? value)
                    ? WhosEntryOf(name, value, inFile: true, global: false)
                    : new WhosEntry(name, IsObjectClass(className) ? null : dims, 0, className, false, false, false));
            }

            return entries;
        }
        catch (InvalidDataException ex)
        {
            throw new JgsRuntimeException(line, col, $"whos: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new JgsRuntimeException(line, col, $"whos: {ex.Message}");
        }
    }

    /// <summary>Whether a class is kept in a MAT-file as an object, whose size whos -file does not know.</summary>
    private static bool IsObjectClass(string className) =>
        !(className is "double" or "single" or "logical" or "char" or "cell" or "struct" or "function_handle"
            || IntegerClassNames.Contains(className, StringComparer.Ordinal));

    private static WhosEntry WhosEntryOf(string name, JgsValue value, bool inFile, bool global)
    {
        string className = ClassOf(value, JgsDialect.Matlab);
        bool sparse = value.Type == JgsType.Sparse;
        bool complex = value.Type == JgsType.Complex || (value.Type == JgsType.Array && (value.IsPackedComplex || HasComplexElements(value)));
        int[]? size = inFile && IsObjectClass(className) ? null : SizeDims(value);
        return new WhosEntry(name, size, WhosBytes(value), className, global, sparse, complex);
    }

    /// <summary>The bytes R2025b's whos reports for a value (probe_128c's rules).</summary>
    private static long WhosBytes(JgsValue value)
    {
        switch (value.Type)
        {
            case JgsType.Number:
                return ClassWidth(value.NumericClass);
            case JgsType.Bool:
                return 1;
            case JgsType.Complex:
                return 2 * ClassWidth(value.NumericClass);
            case JgsType.String:
                return 2L * value.AsString.Length;
            case JgsType.Sparse:
            {
                var matrix = value.AsSparse;
                long nzmax = Math.Max(matrix.NonZeroCount, 1);
                return (nzmax * 16) + ((matrix.Cols + 1L) * 8);
            }

            case JgsType.Cell:
                return value.AsCell.Sum(static e => JgsEmpty.IsEmptyArray(e) && e.Rows == 0 && e.Cols == 0 && e.Type == JgsType.Array
                    ? 8L
                    : 120L + WhosBytes(e));
            case JgsType.Struct:
            {
                JgsStructArray payload = value.AsStructArray;
                if (payload.FieldNames.Length == 0)
                {
                    return 0;
                }

                long names = 16L + (8L * payload.FieldNames.Length) + payload.FieldNames.Sum(static n => (long)n.Length);
                return names + payload.Elements.Sum(element => element.Values.Sum(static held => 120L + WhosBytes(held)));
            }

            case JgsType.Table:
            {
                var table = value.AsTable;
                return table.Columns.Sum(c => WhosBytes(TableColumnValue(table, c.Name, 0, 0)));
            }

            case JgsType.Array:
            {
                if (value.IsCategorical)
                {
                    return value.ArrayLength + value.CategoricalTag!.Categories.Sum(static c => 2L * c.Length);
                }

                if (value.IsStringArray)
                {
                    return Enumerable.Range(0, value.ArrayLength).Sum(i => 2L * value.ElementAt(i).AsString.Length);
                }

                if (value.IsCharMatrix)
                {
                    return 2L * value.ArrayLength;
                }

                if (IsLogicalValue(value))
                {
                    return value.ArrayLength;
                }

                long width = ClassWidth(value.NumericClass);
                return value.ArrayLength * width * (value.IsPackedComplex || HasComplexElements(value) ? 2 : 1);
            }

            default:
                return 0;
        }
    }

    private static long ClassWidth(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Int8 or JgsNumericClass.UInt8 => 1,
        JgsNumericClass.Int16 or JgsNumericClass.UInt16 => 2,
        JgsNumericClass.Single or JgsNumericClass.Int32 or JgsNumericClass.UInt32 => 4,
        _ => 8,
    };

    /// <summary>The struct array whos answers: a column, one element a variable.</summary>
    private static JgsValue WhosStruct(IReadOnlyList<WhosEntry> entries, string function)
    {
        var elements = new Dictionary<string, JgsValue>[entries.Count];
        for (int i = 0; i < elements.Length; i++)
        {
            WhosEntry entry = entries[i];
            elements[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str(entry.Name),
                ["size"] = entry.Size is { } dims
                    ? JgsMatrix.FromColumnMajor([.. dims.Select(static d => (double)d)], 1, dims.Length)
                    : JgsMatrix.FromColumnMajor([], 0, 0),
                ["bytes"] = JgsValue.Number(entry.Bytes),
                ["class"] = JgsValue.Str(entry.Class),
                ["global"] = JgsValue.Bool(entry.Global),
                ["sparse"] = JgsValue.Bool(entry.Sparse),
                ["complex"] = JgsValue.Bool(entry.Complex),
                ["nesting"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["function"] = JgsValue.Str(function),
                    ["level"] = JgsValue.Number(1),
                }),
                ["persistent"] = JgsValue.Bool(false),
            };
        }

        return JgsValue.StructArray(new JgsStructArray(elements, [.. WhosFields]), elements.Length, 1);
    }

    /// <summary>
    /// R2025b's listing (probe_128c): the name column four letters or more and six to spare, sizes
    /// aligned on their first <c>x</c> (an object in a file is a <c>-</c> there) in a column twelve
    /// wider than the widest size, bytes right-aligned under their heading, the class column four
    /// wider than its longest, and the attributes; a blank line after the heading and at the end.
    /// </summary>
    private static string WhosListing(IReadOnlyList<WhosEntry> entries)
    {
        static (string Left, string Right) Split(int[]? size)
        {
            if (size is null)
            {
                return (string.Empty, string.Empty);
            }

            return (size[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join("x", size.Skip(1).Select(static d => d.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        static string Attributes(WhosEntry entry) =>
            string.Join(", ", new[] { entry.Global ? "global" : null, entry.Complex ? "complex" : null, entry.Sparse ? "sparse" : null }.Where(static a => a is not null));

        int nameWidth = Math.Max(4, entries.Max(static e => e.Name.Length)) + 6;
        int left = Math.Max(1, entries.Max(e => Split(e.Size).Left.Length));
        int right = entries.Max(e => Split(e.Size).Right.Length);
        int sizeStart = 2 + nameWidth;
        int bytesStart = sizeStart + Math.Max(left + 1 + right, 4) + 12;
        int bytesWidth = Math.Max(5, entries.Max(static e => e.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture).Length));
        int classWidth = Math.Max(5, entries.Max(static e => e.Class.Length)) + 4;
        int attributesWidth = Math.Max(10, entries.Max(e => Attributes(e).Length));

        var text = new StringBuilder();
        var heading = new StringBuilder("  Name");
        heading.Append(' ', sizeStart + left - 1 - heading.Length).Append("Size");
        heading.Append(' ', bytesStart + bytesWidth - 5 - heading.Length).Append("Bytes");
        heading.Append("  ").Append("Class".PadRight(classWidth)).Append("Attributes");
        text.Append(heading).Append("\n\n");
        foreach (WhosEntry entry in entries)
        {
            var row = new StringBuilder("  ").Append(entry.Name.PadRight(nameWidth));
            (string l, string r) = Split(entry.Size);
            if (entry.Size is null)
            {
                row.Append(' ', left).Append('-');
            }
            else
            {
                row.Append(l.PadLeft(left)).Append('x').Append(r);
            }

            string bytes = entry.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row.Append(' ', Math.Max(1, bytesStart + bytesWidth - bytes.Length - row.Length)).Append(bytes);
            row.Append("  ").Append(entry.Class.PadRight(classWidth)).Append(Attributes(entry).PadRight(attributesWidth));
            text.Append(row).Append('\n');
        }

        return text.Append('\n').ToString();
    }
}
