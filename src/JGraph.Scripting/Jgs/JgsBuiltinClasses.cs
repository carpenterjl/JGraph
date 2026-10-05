namespace JGraph.Scripting.Jgs;

/// <summary>
/// The classes this build supplies for a user class to inherit from (U6 of the app-building plan,
/// ADR 0203): <c>matlab.mixin.Copyable</c> and <c>matlab.mixin.SetGet</c>. Each is an ordinary
/// <see cref="JgsClass"/> built from a declaration made here instead of parsed from a file, whose
/// methods carry a body written in C#. A subclass therefore inherits, overrides, lists and reaches
/// them (<c>copyElement@matlab.mixin.Copyable(obj)</c>) by the rules every other class follows.
/// </summary>
/// <remarks>
/// <c>handle</c> and <c>event.EventData</c> are not here: they are names a class header may carry,
/// answered by <see cref="JgsClass.IsHandle"/> and <see cref="JgsClass.IsEventData"/>, as they were
/// before a class could inherit from anything else. <c>matlab.apps.AppBase</c> is U7's to add.
/// </remarks>
internal static class JgsBuiltinClasses
{
    /// <summary>The mixin that gives a handle class <c>copy</c>.</summary>
    public const string Copyable = "matlab.mixin.Copyable";

    /// <summary>The mixin that gives a handle class <c>set</c> and <c>get</c> by property name.</summary>
    public const string SetGet = "matlab.mixin.SetGet";

    /// <summary>The declaration of the built-in class <paramref name="name"/>, or null when this build supplies none.</summary>
    public static ClassdefStmt? DeclarationOf(string name, Interpreter interpreter) => name switch
    {
        Copyable => Class(name,
            Method("copy", MemberAccess.Public, isSealed: true, (args, line, col) => Copy(args, line, col)),
            Method("copyElement", new MemberAccess(MemberAccessKind.Protected), isSealed: false,
                (args, line, col) => CopyElement(interpreter, args, line, col))),
        SetGet => Class(name,
            Method("set", MemberAccess.Public, isSealed: true, (args, line, col) => Set(interpreter, args, line, col), bindsAns: false),
            Method("get", MemberAccess.Public, isSealed: true, (args, line, col) => Get(interpreter, args, line, col))),
        _ => null,
    };

    private static ClassdefStmt Class(string name, params ClassMethod[] methods) =>
        new(name, isHandle: true, [], methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = ["handle"],
            Abstract = true,
        };

    private static ClassMethod Method(
        string name, MemberAccess access, bool isSealed, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
        bool bindsAns = true) =>
        new(new FnStmt(name, ["obj"], [], []) { Dialect = JgsDialect.Matlab }, Static: false)
        {
            Access = access,
            Sealed = isSealed,
            Native = new BuiltinFunction(name, body) { KeepsStringArguments = true, BindsAnsAsStatement = bindsAns },
        };

    private static JgsObject Receiver(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        JgsObject instance = args[0].AsObject;
        if (instance.Deleted)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");
        }

        _ = verb;
        return instance;
    }

    /// <summary>
    /// <c>b = copy(a)</c>: what the object's <c>copyElement</c> answers - the class's own, when it
    /// overrides the one below, which is how a class customises its copy.
    /// </summary>
    private static JgsValue Copy(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject source = Receiver("copy", args, line, col);
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        return source.Class.TryMethod("copyElement", out ClassMethod? element)
            ? source.Class.Callable(element).Call([args[0]], line, col)
            : throw new JgsRuntimeException(line, col, $"'{source.Class.Name}' has no copyElement to copy it with.");
    }

    /// <summary>
    /// The copy <c>matlab.mixin.Copyable</c> makes (measured in R2025b): another object of the same
    /// class holding the same property values - a handle held by a property is shared, not copied -
    /// except that a <c>NonCopyable</c> property starts again from its default. Listeners stay
    /// with the original.
    /// </summary>
    private static JgsValue CopyElement(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject source = Receiver("copyElement", args, line, col);
        JgsClass definition = source.Class;
        var clone = new JgsObject(definition);
        foreach ((string name, JgsValue held) in source.Fields)
        {
            JgsValue copied = definition.Property(name) is { NonCopyable: true } property
                ? definition.DefaultOf(property, line, col)
                : interpreter.CopyForBinding(held);
            clone.Fields[name] = copied;
        }

        return JgsValue.Object(clone);
    }

    /// <summary>
    /// <c>set(obj, name, value, …)</c> and <c>set(obj, struct)</c> on a <c>matlab.mixin.SetGet</c>
    /// object: each name matched without regard to case and by any unambiguous beginning, each
    /// write made as a dot would make it.
    /// </summary>
    private static JgsValue Set(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject target = Receiver("set", args, line, col);
        var pairs = new List<(string Name, JgsValue Value)>();
        if (args.Count == 2 && args[1].Type == JgsType.Struct && args[1].ClassName is null && !args[1].IsStructArray)
        {
            foreach ((string name, JgsValue value) in args[1].AsStruct)
            {
                pairs.Add((name, value));
            }
        }
        else
        {
            if (args.Count % 2 == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:BadParamValuePairs", "Invalid parameter/value pair arguments.");
            }

            for (int i = 1; i + 1 < args.Count; i += 2)
            {
                if (!JgsBuiltins.IsTextScalar(args[i]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:class:BadParamValuePairs", "Invalid parameter/value pair arguments.");
                }

                pairs.Add((JgsBuiltins.TextOf(args[i]), args[i + 1]));
            }
        }

        foreach ((string asked, JgsValue value) in pairs)
        {
            string name = Match(target.Class, asked)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidProperty",
                    $"The name '{asked}' is not an accessible property for an instance of class '{target.Class.Name}'.");
            interpreter.WriteProperty(args[0], name, value, line, col);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>get(obj, name)</c>, <c>get(obj, {names})</c> and <c>get(obj)</c> on a
    /// <c>matlab.mixin.SetGet</c> object: one value, a cell row of them, or a struct of every
    /// property a listing shows.
    /// </summary>
    private static JgsValue Get(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject target = Receiver("get", args, line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue One(string asked)
        {
            string name = Match(target.Class, asked)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:class:setgetPropertyNotFound",
                    $"Property {asked} not found in class {target.Class.Name}, or is not present in all elements of the "
                    + $"array of class {target.Class.Name}.");
            return interpreter.ReadProperty(args[0], name, line, col);
        }

        if (args.Count == 1)
        {
            var all = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach (ClassProperty property in target.Class.ListedProperties)
            {
                all[property.Spec.Name] = JgsValue.Share(interpreter.ReadProperty(args[0], property.Spec.Name, line, col));
            }

            return JgsValue.Struct(all);
        }

        if (args[1].Type == JgsType.Cell)
        {
            return JgsValue.Cell([.. args[1].AsCell.Select(name => JgsValue.Share(One(JgsBuiltins.TextOf(name))))]);
        }

        return One(JgsBuiltins.TextOf(args[1]));
    }

    /// <summary>
    /// The property a name given to <c>set</c> or <c>get</c> means: the exact name, else the one
    /// property it matches without regard to case, else the one it begins. Null for none or several.
    /// </summary>
    private static string? Match(JgsClass definition, string asked)
    {
        if (definition.Property(asked) is not null)
        {
            return asked;
        }

        string[] names = [.. definition.Properties.Select(static p => p.Spec.Name)];
        string[] same = [.. names.Where(n => string.Equals(n, asked, StringComparison.OrdinalIgnoreCase))];
        if (same.Length == 1)
        {
            return same[0];
        }

        string[] begun = [.. names.Where(n => asked.Length > 0 && n.StartsWith(asked, StringComparison.OrdinalIgnoreCase))];
        return begun.Length == 1 ? begun[0] : null;
    }
}
