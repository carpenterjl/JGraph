using System.IO;
using System.Runtime.CompilerServices;
using JGraph.Scripting.MatFile;
using JGraph.Scripting.MatFile.Hdf5;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matfile</c> (V6, ADR 0167, appendix A #112): a handle to a MAT-file whose variables are
/// read from disk at every mention and written back at every set — <c>x = m.v</c>,
/// <c>m.v(1, 2) = 8</c>, <c>m.w = 5</c>, <c>who(m)</c>, <c>whos(m)</c>, <c>size(m, 'v')</c>,
/// <c>m.Properties.Writable = true</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The object.</b> A struct wearing <c>matlab.io.MatFile</c> and a handle (<c>m2 = m</c> is the
/// same object), holding one field, <c>Properties</c> — a struct wearing
/// <c>matlab.io.matfile.Properties</c> with <c>Source</c> (the full path) and <c>Writable</c>. Its
/// private half, in a weak table beside the value, is the interpreter and host a read needs to
/// build a saved object or say a class is missing.
/// </para>
/// <para>
/// <b>Reads and writes.</b> <c>m.v</c> is a field read the interpreter routes here: the one
/// variable is decoded from the file and handed back fresh, so a later write to it reaches
/// nothing (<c>x = m.v; x(1) = 7; m.v</c> is unchanged, R2025b). A write is get, modify, set at
/// the variable's level (the computed-level road every other storage-backed dot takes): the
/// variable is read, the ordinary roads write the slot, and the slot goes back into the file —
/// which, the file being a flat run of elements, is a read, a merge and a rewrite
/// (<see cref="MatFileWriter.Append"/>). R2025b does the same on a file that is not v7.3, with a
/// warning about the inefficiency this build does not repeat: the file it would advise
/// (<c>-v7.3</c>) is one this build cannot write, so a v7.3 file is read here and never changed.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name a <c>matfile</c> object answers to.</summary>
    internal const string MatFileClassName = "matlab.io.MatFile";

    /// <summary>The class name its <c>Properties</c> answers to.</summary>
    internal const string MatFilePropertiesClassName = "matlab.io.matfile.Properties";

    private const string PropertiesField = "Properties";
    private const string SourceField = "Source";
    private const string WritableField = "Writable";
    private const string WritableNotLogical = "Writable parameter must be a scalar logical.";

    /// <summary>What a read needs that the struct does not hold: the interpreter, for the classes a saved object names, and the host, for a warning.</summary>
    private sealed class JgsMatFileState
    {
        public required Interpreter Interpreter { get; init; }

        public required JGraphScriptGlobals Host { get; init; }
    }

    private static readonly ConditionalWeakTable<JgsStructArray, JgsMatFileState> MatFileStates = new();

    /// <summary>Whether a value is a <c>matfile</c> object.</summary>
    internal static bool IsMatFile(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName == MatFileClassName;

    /// <summary>Records a warning for <c>lastwarn</c> and writes it when its identifier is on — the shape every builtin's warning takes.</summary>
    internal static void Warn(JGraphScriptGlobals host, string identifier, string text)
    {
        host.Warnings.Record(identifier, text);
        if (host.Warnings.IsOn(identifier))
        {
            host.WriteErr("Warning: " + text);
        }
    }

    /// <summary>Registers <c>matfile</c>.</summary>
    internal static void RegisterMatFileBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(host);
        env.Builtins.Register("matfile", JgsValue.Function(new BuiltinFunction("matfile",
            (args, line, col) => NewMatFile(interpreter, host, args, line, col))
        {
            KeepsStringArguments = true,
        }));
    }

    /// <summary><c>m = matfile(name)</c>, <c>matfile(name, 'Writable', true)</c>: a name without an extension gets <c>.mat</c>; the file need not exist yet.</summary>
    private static JgsValue NewMatFile(
        Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "Not enough input arguments.");
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col,
                "Invalid input for argument 1 (rhs1): Value must be a character vector or a string scalar.");
        }

        string path = TextOf(args[0]);
        if (!Path.HasExtension(path))
        {
            path += ".mat";
        }

        bool writable = false;
        for (int i = 1; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count || !IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col,
                    "matfile takes its options as name/value pairs: matfile(name, 'Writable', true).");
            }

            string option = TextOf(args[i]);
            if (!option.Equals(WritableField, StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"'{option}' is not a matfile option; the one option is 'Writable'.");
            }

            writable = LogicalScalar(args[i + 1]) ?? throw new JgsRuntimeException(line, col, WritableNotLogical);
        }

        // The full path, as R2025b's Source is: an existing file where a read would find it, and
        // otherwise where a write would put it.
        string existing = host.Resolve(path);
        string source = File.Exists(existing) ? Path.GetFullPath(existing) : Path.GetFullPath(host.ResolveForWrite(path));

        JgsValue matfile = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [PropertiesField] = PropertiesOf(source, writable),
        });
        matfile.SetClassName(MatFileClassName);
        matfile.AsStructArray.External = true; // V10: a file, not a container the count releases
        MatFileStates.Add(matfile.AsStructArray, new JgsMatFileState { Interpreter = interpreter, Host = host });
        return matfile;
    }

    private static JgsValue PropertiesOf(string source, bool writable)
    {
        JgsValue properties = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [SourceField] = JgsValue.Str(source),
            [WritableField] = JgsValue.Bool(writable),
        });
        properties.SetClassName(MatFilePropertiesClassName);
        return properties;
    }

    /// <summary>A logical or numeric scalar as the flag it means, or null for anything else.</summary>
    private static bool? LogicalScalar(JgsValue value)
    {
        if (value.Type is JgsType.Bool or JgsType.Number)
        {
            return value.AsNumber != 0;
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsStringArray
            && value.ElementAt(0).Type is JgsType.Bool or JgsType.Number)
        {
            return value.ElementAt(0).AsNumber != 0;
        }

        return null;
    }

    private static (string Source, bool Writable) MatFileSettings(JgsValue matfile)
    {
        Dictionary<string, JgsValue> properties = matfile.AsStruct[PropertiesField].AsStruct;
        return (properties[SourceField].AsString, properties[WritableField].AsNumber != 0);
    }

    private static JgsMatFileState MatFileStateOf(JgsValue matfile, int line, int col) =>
        MatFileStates.TryGetValue(matfile.AsStructArray, out JgsMatFileState? state)
            ? state
            : throw new JgsRuntimeException(line, col,
                "this matfile has lost track of its run — it was copied out of the run that made it.");

    // --- reads ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>m.Properties</c>, or <c>m.v</c>: the one variable, decoded from the file now. A file that
    /// is not there and a variable that is not in it are refused in R2025b's words.
    /// </summary>
    internal static JgsValue GetMatFileMember(JgsValue matfile, string field, int line, int col)
    {
        if (field == PropertiesField)
        {
            return matfile.AsStruct[PropertiesField];
        }

        (string source, _) = MatFileSettings(matfile);
        if (!File.Exists(source))
        {
            throw new JgsRuntimeException(line, col, $"Cannot access '{field}' because '{source}' does not exist.");
        }

        JgsMatFileState state = MatFileStateOf(matfile, line, col);
        try
        {
            var binder = new MatWorkspaceBinder(state.Interpreter, state.Host, line, col);
            IReadOnlyList<(string Name, JgsValue Value)> read = MatFileReader.Read(source, new HashSet<string>(StringComparer.Ordinal) { field }, binder);
            return read.Count > 0
                ? read[0].Value
                : throw new JgsRuntimeException(line, col, $"'{field}' does not exist in '{source}'.");
        }
        catch (InvalidDataException ex)
        {
            throw new JgsRuntimeException(line, col, $"matfile: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new JgsRuntimeException(line, col, $"matfile: {ex.Message}");
        }
    }

    /// <summary>Whether the file holds a variable of that name, by header alone; false for a file that is not there.</summary>
    internal static bool MatFileHasVariable(JgsValue matfile, string field, int line, int col) =>
        DescribeMatFile(matfile, line, col).Any(v => string.Equals(v.Name, field, StringComparison.Ordinal));

    /// <summary>Each variable's name, shape and class, from the file's headers; none for a file that is not there.</summary>
    private static IReadOnlyList<(string Name, int[] Dims, string Class)> DescribeMatFile(JgsValue matfile, int line, int col)
    {
        (string source, _) = MatFileSettings(matfile);
        if (!File.Exists(source))
        {
            return [];
        }

        try
        {
            return MatFileReader.Describe(source);
        }
        catch (InvalidDataException ex)
        {
            throw new JgsRuntimeException(line, col, $"matfile: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new JgsRuntimeException(line, col, $"matfile: {ex.Message}");
        }
    }

    /// <summary><c>who(m)</c>: the variable names, a cell column.</summary>
    internal static JgsValue MatFileWho(JgsValue matfile, int line, int col) =>
        CellColumn(DescribeMatFile(matfile, line, col).Select(static v => v.Name));

    /// <summary><c>whos(m)</c>: a struct array with a row per variable — name, size, bytes, class, and the flags whos reports.</summary>
    internal static JgsValue MatFileWhos(JgsValue matfile, int line, int col)
    {
        IReadOnlyList<(string Name, int[] Dims, string Class)> described = DescribeMatFile(matfile, line, col);
        var rows = new Dictionary<string, JgsValue>[described.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            (string name, int[] dims, string className) = described[i];
            long count = dims.Aggregate(1L, static (total, dim) => total * dim);
            int width = className switch
            {
                "double" or "int64" or "uint64" => 8,
                "single" or "int32" or "uint32" => 4,
                "char" or "int16" or "uint16" => 2,
                "int8" or "uint8" or "logical" => 1,
                _ => 0,
            };
            rows[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str(name),
                ["size"] = Numbers([.. dims.Select(static d => (double)d)]),
                ["bytes"] = JgsValue.Number(count * width),
                ["class"] = JgsValue.Str(className),
                ["global"] = JgsValue.False,
                ["sparse"] = JgsValue.False,
                ["complex"] = JgsValue.False,
                ["persistent"] = JgsValue.False,
            };
        }

        return JgsValue.StructArray(
            new JgsStructArray(rows, ["name", "size", "bytes", "class", "global", "sparse", "complex", "persistent"]),
            rows.Length, rows.Length == 0 ? 0 : 1);
    }

    /// <summary><c>size(m, 'v')</c>: the variable's shape from its header.</summary>
    internal static int[] MatFileVariableDims(JgsValue matfile, string variable, int line, int col)
    {
        foreach ((string name, int[] dims, _) in DescribeMatFile(matfile, line, col))
        {
            if (string.Equals(name, variable, StringComparison.Ordinal))
            {
                return dims;
            }
        }

        (string source, _) = MatFileSettings(matfile);
        throw new JgsRuntimeException(line, col, $"'{variable}' does not exist in '{source}'.");
    }

    /// <summary><c>properties(m)</c>: <c>Properties</c>, then the variables, as R2025b lists them.</summary>
    internal static IEnumerable<string> MatFilePropertyNames(JgsValue matfile, int line, int col) =>
        [PropertiesField, .. DescribeMatFile(matfile, line, col).Select(static v => v.Name)];

    // --- writes ---------------------------------------------------------------------------------

    /// <summary>The refusal a write to a read-only matfile meets, before anything is read (R2025b's order and words).</summary>
    internal static void RequireMatFileWritable(JgsValue matfile, string field, int line, int col)
    {
        if (field == PropertiesField)
        {
            return;
        }

        (_, bool writable) = MatFileSettings(matfile);
        if (!writable)
        {
            throw new JgsRuntimeException(line, col,
                $"Cannot change '{field}' because Properties.Writable is false.  To modify '{field}', set Properties.Writable to true.");
        }
    }

    /// <summary>
    /// <c>m.v = value</c>, or the slot a deeper write filled: the variable goes into the file,
    /// replacing one of that name and keeping the rest; <c>m.Properties = p</c> is the settings.
    /// </summary>
    internal static void WriteMatFileVariable(JgsValue matfile, string field, JgsValue value, int line, int col)
    {
        if (field == PropertiesField)
        {
            SetMatFileProperties(matfile, value, line, col);
            return;
        }

        RequireMatFileWritable(matfile, field, line, col);
        (string source, _) = MatFileSettings(matfile);
        if (MatFileWriter.WhyNotWritable(value) is string why)
        {
            throw new JgsRuntimeException(line, col, $"matfile: '{field}' is {why}.");
        }

        try
        {
            if (File.Exists(source) && Hdf5File.Looks(File.ReadAllBytes(source)))
            {
                throw new JgsRuntimeException(line, col,
                    $"'{source}' is a version 7.3 MAT-file, which this build reads but does not write.");
            }

            MatFileWriter.Append(source, [(field, value)]);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            throw new JgsRuntimeException(line, col, $"matfile: {ex.Message}");
        }
    }

    /// <summary><c>m.Properties.Writable = tf</c>: checked (a scalar logical; <c>Source</c> is read-only), then stored on the one object every alias holds.</summary>
    private static void SetMatFileProperties(JgsValue matfile, JgsValue properties, int line, int col)
    {
        if (properties.Type != JgsType.Struct || properties.IsStructArray)
        {
            throw new JgsRuntimeException(line, col,
                "Properties must be a matlab.io.matfile.Properties, with Source and Writable.");
        }

        (string source, bool writable) = MatFileSettings(matfile);
        foreach ((string name, JgsValue value) in properties.AsStruct)
        {
            switch (name)
            {
                case WritableField:
                    writable = LogicalScalar(value) ?? throw new JgsRuntimeException(line, col, WritableNotLogical);
                    break;
                case SourceField:
                    if (!IsTextScalar(value) || !string.Equals(TextOf(value), source, StringComparison.Ordinal))
                    {
                        throw new JgsRuntimeException(line, col,
                            "Source is a read-only property of a matfile; open another file with matfile(name).");
                    }

                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"Unrecognized property '{name}' for class '{MatFilePropertiesClassName}'.");
            }
        }

        matfile.WritableStruct()[PropertiesField] = PropertiesOf(source, writable);
    }
}
