using System.Globalization;
using System.IO;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>save</c>/<c>load</c> builtins: workspace variables to and from level-5 MAT-files (plus
/// <c>-ascii</c> text). They are declared by whoever owns the workspace — the console session and
/// the one-shot runner — because only the owner knows which names the user created, the same reason
/// <c>clear</c> and <c>whos</c> live there.
/// </summary>
/// <remarks>
/// V6 (ADR 0167, appendix A #110, #111, #113): <c>save -struct</c> writes a scalar struct's fields
/// as the variables; an instance of a user class and a function handle are saved and loaded through
/// <see cref="MatWorkspaceBinder"/>, which builds an instance from its class (no constructor runs,
/// as in R2025b) and re-makes a handle from its text and captured workspace. A loaded handle object
/// is a new instance — <c>S.h == h</c> is false — and two names saved over one handle load as one.
/// </remarks>
internal static class JgsWorkspaceIo
{
    /// <summary>
    /// Declares <c>save</c> and <c>load</c> into <paramref name="environment"/>.
    /// <paramref name="userVariables"/> yields the user-created names (save's default set);
    /// <c>load</c> declares what it reads straight into the environment, and asks
    /// <paramref name="interpreter"/> for the classes a saved object needs.
    /// </summary>
    public static void DefineSaveLoad(
        JgsEnvironment environment,
        JGraphScriptGlobals host,
        Interpreter interpreter,
        Func<IEnumerable<(string Name, JgsValue Value)>> userVariables,
        Func<JgsEnvironment>? activeWorkspace = null)
    {
        // Bare statements stay silent, like MATLAB: neither verb binds ans.
        environment.Builtins.Register("save", JgsValue.Function(
            new BuiltinFunction("save", (args, line, col) => Save(host, userVariables, args, line, col))
            {
                BindsAnsAsStatement = false,
            }));

        environment.Builtins.Register("load", JgsValue.Function(
            new BuiltinFunction("load", (args, line, col) => Load(activeWorkspace?.Invoke() ?? environment, host, interpreter, args, line, col))
            {
                BindsAnsAsStatement = false,
            }));
    }

    private const string StructOptionNeedsName = "The -STRUCT option must be followed by the name of a scalar structure variable.";
    private const string StructArgumentNotStruct = "The argument to -STRUCT must be the name of a scalar structure variable.";

    private static JgsValue Save(
        JGraphScriptGlobals host,
        Func<IEnumerable<(string Name, JgsValue Value)>> userVariables,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        string? path = null;
        bool ascii = false;
        bool append = false;
        string? structName = null;
        bool structNamePending = false;
        var names = new List<string>();
        foreach (JgsValue arg in args)
        {
            if (arg.Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, "save expects a file name and variable names as text.");
            }

            string word = arg.AsString;
            if (structNamePending)
            {
                // -struct takes the very next word, and an option is not a name (R2025b's words).
                if (word.StartsWith('-'))
                {
                    throw new JgsRuntimeException(line, col, StructOptionNeedsName);
                }

                structName = word;
                structNamePending = false;
            }
            else if (word.Equals("-struct", StringComparison.OrdinalIgnoreCase))
            {
                structNamePending = true;
            }
            else if (word.Equals("-ascii", StringComparison.OrdinalIgnoreCase))
            {
                ascii = true;
            }
            else if (word.Equals("-append", StringComparison.OrdinalIgnoreCase))
            {
                append = true;
            }
            else if (word.Equals("-v7.3", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    "save writes version 5 MAT-files only; version 7.3 is an HDF5 format that can be read but not written.");
            }
            else if (word is "-v6" or "-v7" or "-V6" or "-V7" || word.Equals("-mat", StringComparison.OrdinalIgnoreCase))
            {
                // Version 5 is what every one of these asks for or is happy with; nothing to change.
            }
            else if (word.StartsWith('-'))
            {
                throw new JgsRuntimeException(line, col, $"save does not support the option '{word}'.");
            }
            else if (path is null)
            {
                path = word;
            }
            else
            {
                names.Add(word);
            }
        }

        if (structNamePending)
        {
            throw new JgsRuntimeException(line, col, StructOptionNeedsName);
        }

        path ??= "matlab.mat";
        if (!ascii && !Path.HasExtension(path))
        {
            path += ".mat";
        }

        // A function's nargin and nargout are the call's, not the workspace's: save(fn) inside a
        // function writes the variables, as R2025b does, and not the two counts.
        var all = userVariables()
            .Where(static v => v.Name is not ("nargin" or "nargout"))
            .OrderBy(static v => v.Name, StringComparer.Ordinal)
            .ToList();
        List<(string Name, JgsValue Value)> selected;
        if (structName is not null)
        {
            // save(fn, '-struct', 'st', fields…) (V6, #110): the fields of a scalar struct are the
            // variables, all of them or the ones named, in the struct's own order.
            int at = all.FindIndex(v => v.Name == structName);
            if (at < 0 || all[at].Value.Type != JgsType.Struct || all[at].Value.IsStructArray
                || all[at].Value.ClassName is not null)
            {
                throw new JgsRuntimeException(line, col, StructArgumentNotStruct);
            }

            Dictionary<string, JgsValue> fields = all[at].Value.AsStruct;
            selected = new List<(string, JgsValue)>();
            foreach (string field in names.Count == 0 ? [.. fields.Keys] : names)
            {
                if (!fields.TryGetValue(field, out JgsValue? held))
                {
                    throw new JgsRuntimeException(line, col,
                        $"The variable '{structName}' does not contain a field named '{field}'.");
                }

                selected.Add((field, held));
            }
        }
        else if (names.Count == 0)
        {
            selected = all;
        }
        else
        {
            selected = new List<(string, JgsValue)>();
            foreach (string name in names)
            {
                int at = all.FindIndex(v => v.Name == name);
                if (at < 0)
                {
                    throw new JgsRuntimeException(line, col, $"save: variable '{name}' does not exist.");
                }

                selected.Add(all[at]);
            }
        }

        if (selected.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "save: there are no variables to save.");
        }

        string target = host.ResolveForWrite(path);
        try
        {
            if (ascii)
            {
                SaveAscii(target, selected, append, line, col);
            }
            else
            {
                foreach ((string name, JgsValue value) in selected)
                {
                    if (MatFileWriter.WhyNotWritable(value) is string why)
                    {
                        throw new JgsRuntimeException(line, col, $"save: '{name}' is {why}.");
                    }
                }

                if (append)
                {
                    MatFileWriter.Append(target, selected);
                }
                else
                {
                    MatFileWriter.Write(target, selected);
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            // InvalidDataException reaches here only from -append, which has to read the file it is
            // about to rewrite; saying so plainly beats "save: ..." with no hint that a read failed.
            throw new JgsRuntimeException(line, col, $"save: {ex.Message}");
        }

        return JgsValue.Null;
    }

    /// <summary>MATLAB's -ascii layout: each variable's rows as lines of %.8g values.</summary>
    private static void SaveAscii(
        string path, List<(string Name, JgsValue Value)> variables, bool append, int line, int col)
    {
        using var writer = new StreamWriter(path, append);
        foreach ((string name, JgsValue value) in variables)
        {
            foreach (double[] row in NumericRows(name, value, line, col))
            {
                writer.WriteLine(string.Join(' ',
                    row.Select(static v => v.ToString("G8", CultureInfo.InvariantCulture))));
            }
        }
    }

    private static IEnumerable<double[]> NumericRows(string name, JgsValue value, int line, int col)
    {
        switch (value.Type)
        {
            case JgsType.Number or JgsType.Bool:
                yield return [value.AsNumber];
                break;
            case JgsType.Array when JgsMatrix.IsMatrix(value):
                int cols = JgsMatrix.ColCount(value);
                for (int r = 0; r < JgsMatrix.RowCount(value); r++)
                {
                    var values = new double[cols];
                    for (int c = 0; c < cols; c++)
                    {
                        values[c] = JgsMatrix.At(value, r, c).AsNumber;
                    }

                    yield return values;
                }

                break;
            case JgsType.Array:
                var flat = new double[value.ArrayLength];
                for (int i = 0; i < flat.Length; i++)
                {
                    flat[i] = value.ElementAt(i).AsNumber;
                }

                yield return flat;
                break;
            default:
                throw new JgsRuntimeException(line, col,
                    $"save -ascii only writes numbers, but '{name}' is a {value.TypeName}.");
        }
    }

    private static JgsValue Load(
        JgsEnvironment environment, JGraphScriptGlobals host, Interpreter interpreter,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        string path = args.Count >= 1 ? StrArg(args[0], line, col) : "matlab.mat";
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            wanted.Add(StrArg(args[i], line, col));
        }

        if (!Path.HasExtension(path))
        {
            path += ".mat";
        }

        string source = host.Resolve(path);
        if (!File.Exists(source))
        {
            throw new JgsRuntimeException(line, col, $"load: '{path}' was not found.");
        }

        try
        {
            if (!Path.GetExtension(source).Equals(".mat", StringComparison.OrdinalIgnoreCase))
            {
                return LoadAscii(environment, source);
            }

            var loaded = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            var binder = new MatWorkspaceBinder(interpreter, host, line, col);
            foreach ((string name, JgsValue value) in MatFileReader.Read(source, wanted.Count > 0 ? wanted : null, binder))
            {
                // M2 (appendix A #109, the storage half): the workspace binding adopts the fresh
                // wrapper and the returned struct's field takes a counted share, so a later write to
                // either cannot reach the other. Whether the binding happens at all is V9's.
                environment.Declare(name, value);
                loaded[name] = JgsValue.Share(value);
            }

            // The struct MATLAB's S = load(...) form hands back; a bare load statement discards it.
            return JgsValue.Struct(loaded);
        }
        catch (InvalidDataException ex)
        {
            throw new JgsRuntimeException(line, col, $"load: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new JgsRuntimeException(line, col, $"load: {ex.Message}");
        }
    }

    /// <summary>Loads a whitespace-separated numeric text file as one matrix named after the file.</summary>
    private static JgsValue LoadAscii(JgsEnvironment environment, string source)
    {
        var rows = new List<JgsValue>();
        foreach (string lineText in File.ReadLines(source))
        {
            string[] pieces = lineText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0)
            {
                continue;
            }

            var row = new JgsValue[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                if (!double.TryParse(pieces[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    throw new InvalidDataException($"'{Path.GetFileName(source)}' is not a numeric text file.");
                }

                row[i] = JgsValue.Number(v);
            }

            rows.Add(JgsValue.Array(row));
        }

        JgsValue value = rows.Count == 1 ? rows[0] : JgsValue.Array(rows.ToArray());
        string name = SanitizeName(Path.GetFileNameWithoutExtension(source));
        environment.Declare(name, value);
        return value;
    }

    private static string SanitizeName(string stem)
    {
        var sb = new System.Text.StringBuilder(stem.Length);
        foreach (char c in stem)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, 'x');
        }

        return sb.ToString();
    }

    private static string StrArg(JgsValue value, int line, int col) =>
        value.Type == JgsType.String
            ? value.AsString
            : throw new JgsRuntimeException(line, col, "load expects a file name and variable names as text.");
}

/// <summary>
/// The reader's way back into the interpreter (V6, ADR 0167, #111 and #113): an object element is
/// an instance of its class with every property at its default — no constructor runs, which is
/// R2025b's order too — then the saved values, each checked against its declaration as a write
/// would be; a function element is re-made from its text, in a static workspace holding what it
/// captured, or from its name where the load stands. A class that is not on the path is R2025b's
/// warning, and the variable comes back as a <c>uint32</c> as it does there.
/// </summary>
internal sealed class MatWorkspaceBinder(Interpreter interpreter, JGraphScriptGlobals host, int line, int col) : IMatObjectBinder
{
    private const string CannotInstantiate = "MATLAB:load:cannotInstantiateLoadedVariable";

    /// <inheritdoc />
    public JgsValue NewObject(string className, bool deleted, string variable)
    {
        JgsClass? definition = interpreter.ClassForLoad(className);
        if (definition is null)
        {
            JgsBuiltins.Warn(host, CannotInstantiate,
                $"Variable '{variable}' originally saved as a {className} cannot be instantiated as an object and will be read in as a uint32.");
            JgsValue stub = JgsValue.Number(0);
            stub.SetNumericClass(JgsNumericClass.UInt32);
            return stub;
        }

        JgsObject instance = definition.NewDefault(line, col);
        if (deleted)
        {
            instance.MarkDeleted();
        }

        return JgsValue.Object(instance);
    }

    /// <inheritdoc />
    public void SetProperties(JgsValue instance, IReadOnlyDictionary<string, JgsValue> properties)
    {
        if (instance.Type != JgsType.Object)
        {
            return; // the uint32 that stands for an object whose class is gone
        }

        JgsObject target = instance.AsObject;
        foreach ((string name, JgsValue value) in properties)
        {
            // A property the class no longer declares has nowhere to go and is dropped, as MATLAB
            // drops it; a Constant belongs to the class.
            if (target.Class.Property(name) is { Constant: false } property)
            {
                target.Fields[name] = target.Class.Check(property, value, line, col);
            }
        }
    }

    /// <inheritdoc />
    public JgsValue Function(string text, string type, IReadOnlyDictionary<string, JgsValue>? workspace, string variable)
    {
        if (type == "anonymous" || text.StartsWith('@'))
        {
            // The text is evaluated in a static workspace over the built-in layer holding only what
            // the handle captured — str2func's road, with the captures put back — so a free name
            // in the body is a function resolved at the call, and the handle carries the loading
            // file for its local functions.
            JgsEnvironment defining = interpreter.Dialect.IsMatlab
                ? new JgsEnvironment(interpreter.CurrentFrame.Builtins.Root) { IsStaticWorkspace = true }
                : new JgsEnvironment(interpreter.CurrentFrame);
            if (workspace is not null)
            {
                foreach ((string name, JgsValue value) in workspace)
                {
                    defining.Declare(name, value);
                }
            }

            return interpreter.EvaluateSource(text, defining, line, col);
        }

        return interpreter.TryMakeHandle(text, interpreter.CurrentFrame, out JgsValue handle)
            ? handle
            : throw new JgsRuntimeException(line, col,
                $"load: '{variable}' is a handle to '{text}', which is not a function on the path here.");
    }
}
