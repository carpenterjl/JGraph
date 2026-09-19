namespace JGraph.Scripting.Jgs;

/// <summary>
/// MException-lite (M62): an error that knows what it is, not merely what it said. The identifier is
/// the part scripts branch on — <c>if strcmp(ME.identifier, 'MATLAB:badsubscript')</c> — and until
/// this milestone <c>error('id:sub', …)</c> parsed the identifier and then threw it away, so every
/// such branch silently took the wrong arm.
/// </summary>
/// <remarks>
/// An MException is a struct carrying a class name, not a new value type. Every field access, every
/// <c>isfield</c>, every display already works on a struct, so the milestone spends nothing on them;
/// M68 turns the same three fields into a real object without moving one of them.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name an MException value carries, which is what <c>class(ME)</c> answers.</summary>
    private const string ExceptionClass = "MException";

    /// <summary>Builds an MException over an identifier, a message, and the stack it unwound through.</summary>
    internal static JgsValue MakeException(string identifier, string message, JgsValue stack)
    {
        JgsValue exception = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["identifier"] = JgsValue.Str(identifier),
            ["message"] = JgsValue.Str(message),
            ["cause"] = NoCauses(),
            ["stack"] = stack,
        });

        exception.SetClassName(ExceptionClass);
        return exception;
    }

    /// <summary>An MException with no stack — what a script builds when it calls <c>MException</c> itself.</summary>
    internal static JgsValue MakeException(string identifier, string message) =>
        MakeException(identifier, message, StackValue([]));

    /// <summary>The causes a new exception has: none, as the 0-by-0 cell R2025b's <c>cause</c> is.</summary>
    private static JgsValue NoCauses()
    {
        JgsValue none = JgsValue.Cell([]);
        none.Reshape(0, 0);
        return none;
    }

    /// <summary><c>ME.stack</c>: an n-by-1 struct array of file, name and line - 0-by-1 when there is no frame.</summary>
    internal static JgsValue StackValue(IReadOnlyList<(string Name, string File, int Line)> listed)
    {
        var frames = new Dictionary<string, JgsValue>[listed.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["file"] = JgsValue.Str(listed[i].File),
                ["name"] = JgsValue.Str(listed[i].Name),
                ["line"] = JgsValue.Number(listed[i].Line),
            };
        }

        return JgsValue.StructArray(new JgsStructArray(frames, ["file", "name", "line"]), frames.Length, 1);
    }

    /// <summary>
    /// The value a <c>catch</c> binds for a thrown MException (V6): the exception that was thrown -
    /// identifier, message and causes, each cause a share (M2) - with <paramref name="stack"/> in
    /// place of its own unless it keeps the one it has (<c>rethrow</c>).
    /// </summary>
    internal static JgsValue CaughtException(JgsValue thrown, JgsValue? stack)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JgsValue> field in thrown.AsStruct)
        {
            fields[field.Key] = JgsValue.Share(field.Value);
        }

        if (!fields.ContainsKey("cause"))
        {
            fields["cause"] = NoCauses();
        }

        if (stack is not null || !fields.ContainsKey("stack"))
        {
            fields["stack"] = stack ?? StackValue([]);
        }

        JgsValue caught = JgsValue.Struct(fields);
        caught.SetClassName(ExceptionClass);
        return caught;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a MATLAB error identifier rather than a message. MATLAB's own
    /// rule: at least one colon, no whitespace, and no format escape — which is why
    /// <c>error('Value: %d', n)</c> is a message and <c>error('pkg:bad', 'oops')</c> is not.
    /// </summary>
    private static bool IsErrorIdentifier(string text) =>
        text.Contains(':', StringComparison.Ordinal)
        && !text.Contains(' ', StringComparison.Ordinal)
        && !text.Contains('%', StringComparison.Ordinal)
        && !text.EndsWith(":", StringComparison.Ordinal)
        && !text.StartsWith(":", StringComparison.Ordinal);

    /// <summary>
    /// Reads the identifier and message out of an error value: an MException, or the plain struct with
    /// <c>message</c>/<c>identifier</c> fields that <c>lasterror</c> hands back and old code still
    /// builds by hand.
    /// </summary>
    private static (string Identifier, string Message) ReadErrorValue(
        string builtin, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                $"{builtin} takes an MException or an error struct, but got a {value.TypeName}.");
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        if (!fields.TryGetValue("message", out JgsValue? message))
        {
            throw new JgsRuntimeException(line, col, $"{builtin}: the error has no message field.");
        }

        string identifier = fields.TryGetValue("identifier", out JgsValue? id) && id.Type == JgsType.String
            ? id.AsString
            : string.Empty;

        return (identifier, message.Type == JgsType.String ? message.AsString : message.Display());
    }

    /// <summary>Declares <c>error</c>, <c>MException</c>, and the three throwing verbs.</summary>
    private static void RegisterErrorObjects(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define, Interpreter interpreter,
        JgsEnvironment env, JGraphScriptGlobals host, JgsDialect dialect)
    {
        // error is re-declared here rather than edited where it was: the identifier-carrying form and
        // the MException form both need the shape this file defines, and one implementation of
        // "what did the caller mean by these arguments" is one fewer place for the two to disagree.
        Define("error", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "error");
            }

            // error(errorStruct) re-raises something that already exists. An MException is not a
            // message structure, and R2025b refuses it by name (V6, measured); throw is its verb.
            if (args[0].Type == JgsType.Struct && args[0].ClassName == ExceptionClass && dialect.IsMatlab)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:error:invalidMessageType",
                    "The message must be specified as either a character vector, string scalar, or a message structure.");
            }

            if (args[0].Type == JgsType.Struct)
            {
                Arity("error", args, 1, line, col);
                (string existingId, string existingMessage) = ReadErrorValue("error", args[0], line, col);
                throw new JgsRuntimeException(line, col, existingId, existingMessage);
            }

            string first = Str("error", args, 0, line, col);
            bool hasIdentifier = args.Count > 1 && IsErrorIdentifier(first);
            throw new JgsRuntimeException(line, col,
                hasIdentifier ? first : string.Empty,
                FormatMessage(env, host, dialect, "error", args, hasIdentifier ? 1 : 0, hasIdentifier, line, col));
        });

        Define("MException", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MException needs an identifier.");
            }

            // MException insists on an identifier where error only accepts one, because an exception
            // built to be thrown later has nothing else to be recognised by.
            string identifier = Str("MException", args, 0, line, col);
            if (!IsErrorIdentifier(identifier))
            {
                throw new JgsRuntimeException(line, col,
                    $"MException: '{identifier}' is not an identifier — it must read component:mnemonic, with no spaces.");
            }

            // An identifier always came first here, so the message is a format even with no data
            // after it — MException('a:b', 'x \ y') warns and truncates in R2025b, as error does.
            return MakeException(
                identifier, FormatMessage(env, host, dialect, "MException", args, 1, identified: true, line, col));
        });

        // throw, rethrow and throwAsCaller differ in MATLAB only by which frame the report points at,
        // and JGraph reports the line the script is on either way — so the three are one behaviour
        // under three names, which ADR 0062 records rather than pretending otherwise.
        // V6 (ADR 0167): they carry the whole exception - its causes with it - and differ in the
        // stack the catch reports: throw's is where it threw from, rethrow keeps the one the
        // exception has, throwAsCaller leaves out the frame that called it.
        void DefineThrow(string name) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                (string identifier, string message) = ReadErrorValue(name, args[0], line, col);
                bool whole = args[0].ClassName == ExceptionClass;
                throw new JgsRuntimeException(line, col, identifier, message)
                {
                    Carried = whole ? args[0] : null,
                    KeepsStack = whole && name == "rethrow",
                    DropsThrowingFrame = name == "throwAsCaller",
                };
            });

        DefineThrow("throw");
        DefineThrow("rethrow");
        DefineThrow("throwAsCaller");

        // getReport(ME): the message, under the line that names where it was thrown from when the
        // exception has a stack; 'basic' is the message alone (measured).
        Define("getReport", (args, line, col) =>
        {
            ArityRange("getReport", args, 1, 8, line, col);
            (_, string message) = ReadErrorValue("getReport", args[0], line, col);
            bool basic = args.Count > 1 && args[1].Type == JgsType.String
                && string.Equals(args[1].AsString, "basic", StringComparison.OrdinalIgnoreCase);
            JgsValue? stack = Field(args[0], "stack");
            if (basic || stack is not { Type: JgsType.Struct } || stack.AsStructArray.Length == 0)
            {
                return JgsValue.Str(message);
            }

            Dictionary<string, JgsValue> top = stack.AsStructArray.Elements[0];
            return JgsValue.Str($"Error using {top["name"].AsString} (line {top["line"].AsNumber:0})\n{message}");
        });

        Define("lasterror", (args, line, col) =>
        {
            ArityRange("lasterror", args, 0, 1, line, col);
            return MakeException(interpreter.LastErrorIdentifier, interpreter.LastError);
        });
    }
}
