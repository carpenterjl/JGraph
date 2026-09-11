using System.Reflection;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Whether a built-in class method answers a call and so outranks a user file of the same name
/// (M145, step 6). MATLAB decides this per name and per argument pattern by no rule its documentation
/// states — <c>numel(1, "x")</c> reaches a user <c>numel.m</c> while <c>numel("x", 1)</c> keeps the
/// built-in, <c>height(t)</c> reaches <c>height.m</c> while <c>minus(d, 1)</c> keeps its
/// <c>datetime</c> method — so the decision is a measurement: every catalog name shadowed by a file
/// and called under 37 argument patterns in R2025b (<c>tools/matlab-checklist/matlab-r2025b-dispatch.csv</c>,
/// built and checked by <c>build-dispatch-table.py</c> and <c>verify-method-classes.py</c>). This
/// class reads that table, embedded in the assembly, and maps a call to its pattern.
/// </summary>
/// <remarks>
/// <para>
/// The pattern of a call is <c>(class of the first argument, first high class among the later
/// arguments, or none)</c>: among numeric, logical and char arguments the leftmost decides and a
/// second such argument never blocks, while a string, cell, struct, handle, table, time, categorical
/// or map argument later in the list can. A pattern the sweep has no column for reads the
/// <c>double</c> pairing when the first class is numeric, logical or char (the leftmost of those
/// decides alike: <c>max(int8(3), {1})</c> is the file's, as <c>max(2, {1})</c> is) and the column
/// for the first argument alone otherwise — the third-position evidence says a later argument does
/// not change the verdict for the names measured; the ADR records both as extrapolations. A class the sweep
/// never saw maps to <c>struct</c>, the column under which the built-in wins least often, so an
/// unknown class errs towards the file. A user <c>classdef</c> object is never asked about here: the
/// resolver gives it the user-method layer before this table is consulted.
/// </para>
/// <para>
/// A name with no row — a JGraph-only built-in — goes to the file under every pattern, which is what
/// a plain <c>.m</c> in MATLAB's toolbox would do.
/// </para>
/// </remarks>
internal static class JgsDispatchTable
{
    private const string TableResource = "JGraph.Scripting.dispatch-table.csv";
    private const string ClassMapResource = "JGraph.Scripting.dispatch-class-map.csv";

    /// <summary>The classes that can block a built-in from a later argument position.</summary>
    private static readonly HashSet<string> LowClasses = new(StringComparer.Ordinal)
    {
        "double", "int8", "logical", "char",
    };

    private static readonly Lazy<Loaded> Table = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed class Loaded
    {
        public readonly Dictionary<string, int> Columns = new(StringComparer.Ordinal);
        public readonly Dictionary<string, ulong> Verdicts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> ClassMap = new(StringComparer.Ordinal);
    }

    /// <summary>Whether the table has a row for <paramref name="name"/> at all.</summary>
    public static bool Knows(string name) => Table.Value.Verdicts.ContainsKey(name);

    /// <summary>
    /// Whether the built-in <paramref name="name"/> answers a call with <paramref name="arguments"/>
    /// ahead of a file of the same name — the measured verdict for the call's pattern.
    /// </summary>
    public static bool KeepsBuiltin(string name, IReadOnlyList<JgsValue> arguments, JgsDialect dialect)
    {
        Loaded table = Table.Value;
        if (!table.Verdicts.TryGetValue(name, out ulong verdicts))
        {
            return false;
        }

        if (arguments.Count == 0)
        {
            return Keeps(table, verdicts, "none");
        }

        string first = PatternClassOf(table, arguments[0], dialect);
        for (int i = 1; i < arguments.Count; i++)
        {
            string later = PatternClassOf(table, arguments[i], dialect);
            if (!LowClasses.Contains(later))
            {
                // The first high class later in the list decides with the first argument, when the
                // sweep measured that pair. A low first class the sweep did not pair with this one
                // (int8, logical, char) behaves as a double does — `max(int8(3), {1})` reaches the
                // file exactly as `max(2, {1})` does — so the double pairing stands in; a high first
                // class with an unmeasured pair reads its own column alone.
                if (table.Columns.ContainsKey(first + "," + later))
                {
                    return Keeps(table, verdicts, first + "," + later);
                }

                if (LowClasses.Contains(first) && table.Columns.ContainsKey("double," + later))
                {
                    return Keeps(table, verdicts, "double," + later);
                }

                return Keeps(table, verdicts, first);
            }
        }

        return Keeps(table, verdicts, first);
    }

    /// <summary>
    /// Whether the built-in keeps every one of <paramref name="patterns"/> — what the loop compiler
    /// asks before binding a name to a kernel, for the patterns its registers can produce.
    /// </summary>
    public static bool KeepsBuiltinFor(string name, IEnumerable<string> patterns)
    {
        Loaded table = Table.Value;
        if (!table.Verdicts.TryGetValue(name, out ulong verdicts))
        {
            return false;
        }

        foreach (string pattern in patterns)
        {
            if (!Keeps(table, verdicts, pattern))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The sweep's pattern class for <paramref name="value"/>: what <c>class</c> answers, mapped.</summary>
    internal static string PatternClassOf(JgsValue value, JgsDialect dialect) =>
        PatternClassOf(Table.Value, value, dialect);

    private static string PatternClassOf(Loaded table, JgsValue value, JgsDialect dialect)
    {
        string className = JgsBuiltins.ClassOf(value, dialect);
        if (table.ClassMap.TryGetValue(className, out string? pattern))
        {
            return pattern;
        }

        // A package-qualified distribution object is one row in the map, 'prob.*'.
        int dot = className.IndexOf('.');
        if (dot > 0 && table.ClassMap.TryGetValue(className[..dot] + ".*", out pattern))
        {
            return pattern;
        }

        return "struct";
    }

    private static bool Keeps(Loaded table, ulong verdicts, string pattern) =>
        table.Columns.TryGetValue(pattern, out int column) && (verdicts & (1UL << column)) != 0;

    private static Loaded Load()
    {
        var loaded = new Loaded();
        Assembly assembly = typeof(JgsDispatchTable).Assembly;

        using (StreamReader reader = Open(assembly, TableResource))
        {
            string? header = reader.ReadLine() ?? throw new InvalidOperationException("The dispatch table is empty.");
            List<string> columns = SplitCsv(header);
            const int firstPattern = 4; // name, status, kind, method_classes, then the patterns
            if (columns.Count - firstPattern > 64)
            {
                throw new InvalidOperationException("The dispatch table has more patterns than a verdict mask holds.");
            }

            for (int i = firstPattern; i < columns.Count; i++)
            {
                loaded.Columns[columns[i]] = i - firstPattern;
            }

            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                List<string> row = SplitCsv(line);
                if (row.Count != columns.Count || row[1] != "measured")
                {
                    continue; // an unmeasured row is "file everywhere", the same as no row
                }

                ulong verdicts = 0;
                for (int i = firstPattern; i < row.Count; i++)
                {
                    if (row[i].StartsWith("builtin", StringComparison.Ordinal))
                    {
                        verdicts |= 1UL << (i - firstPattern);
                    }
                }

                loaded.Verdicts[row[0]] = verdicts;
            }
        }

        using (StreamReader reader = Open(assembly, ClassMapResource))
        {
            _ = reader.ReadLine(); // class,pattern,note
            while (reader.ReadLine() is { } line)
            {
                List<string> row = SplitCsv(line);
                if (row.Count >= 2 && row[0].Length > 0)
                {
                    loaded.ClassMap[row[0]] = row[1];
                }
            }
        }

        return loaded;
    }

    private static StreamReader Open(Assembly assembly, string resource)
    {
        Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded resource '{resource}' is missing.");
        return new StreamReader(stream);
    }

    /// <summary>One CSV line, honouring the quotes the pattern columns are written in.</summary>
    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
