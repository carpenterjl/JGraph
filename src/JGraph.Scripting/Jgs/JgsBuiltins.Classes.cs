namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs that ask a value what class it is and what that class can do (M68): <c>isobject</c>,
/// <c>properties</c>, <c>methods</c>, <c>metaclass</c>, and <c>addCause</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>properties</c> and <c>methods</c> are ordinary builtin names rather than keywords, which is what
/// the <c>classdef</c> parser was careful to preserve: the two words are recognised as block openers
/// only inside a class definition, so they stay available as the names of the two verbs that ask an
/// object what it has.
/// </para>
/// <para>
/// <b>MException stays a tagged struct.</b> The plan for this milestone called for turning it into a
/// real <see cref="JgsType.Object"/>, and re-checking that before doing it showed there was nothing
/// left to gain: <c>class(ME)</c>, <c>isa(ME, 'MException')</c>, every field read, <c>throw</c> and
/// <c>rethrow</c> already answer as an object, and <c>ME.stack</c> became a true struct array the day
/// M65 made struct arrays real. The one thing that answered wrongly was <c>isstruct(ME)</c>, and that
/// is fixed here as a rule about tagged values rather than about MException — which fixes
/// <c>containers.Map</c>, <c>dictionary</c> and the spatial-reference types in the same line.
/// Converting it would have routed the error path — the one that runs when something has already gone
/// wrong — through brand-new machinery to win a single predicate.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name <c>metaclass</c> answers to.</summary>
    private const string MetaClassName = "meta.class";

    /// <summary>Declares the class-introspection builtins into <paramref name="env"/>.</summary>
    internal static void RegisterClassBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

        Define("isobject", (args, line, col) =>
        {
            Arity("isobject", args, 1, line, col);
            return JgsValue.Bool(IsObjectValue(args[0]));
        });

        Define("properties", (args, line, col) =>
        {
            Arity("properties", args, 1, line, col);
            return CellColumn(PropertyNames("properties", args[0], interpreter, line, col));
        });

        Define("methods", (args, line, col) =>
        {
            Arity("methods", args, 1, line, col);
            return CellColumn(MethodNames("methods", args[0], interpreter, line, col));
        });

        Define("metaclass", (args, line, col) =>
        {
            Arity("metaclass", args, 1, line, col);
            JgsValue described = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(ClassOf(args[0], JgsDialect.Matlab)),
                ["PropertyList"] = CellColumn(PropertyNames("metaclass", args[0], interpreter, line, col)),
                ["MethodList"] = CellColumn(MethodNames("metaclass", args[0], interpreter, line, col)),
            });

            described.SetClassName(MetaClassName);
            return described;
        });

        Define("addCause", (args, line, col) =>
        {
            Arity("addCause", args, 2, line, col);
            (string identifier, string message) = ReadErrorValue("addCause", args[0], line, col);
            if (args[1].ClassName != ExceptionClass)
            {
                throw new JgsRuntimeException(line, col,
                    $"addCause: the cause must be an MException, but got a {args[1].TypeName}.");
            }

            // A new exception rather than a write into the old one: MException is a value here, and a
            // script that writes `ME = addCause(ME, cause)` should not also have changed whatever else
            // was holding the original.
            JgsValue[] causes = [.. ExistingCauses(args[0]), args[1]];
            JgsValue built = MakeException(
                identifier, message, Field(args[0], "stack") ?? JgsValue.Cell([]));
            built.AsStruct["cause"] = JgsValue.Cell(causes);
            return built;
        });
    }

    /// <summary>The causes an exception already carries, or none.</summary>
    private static JgsValue[] ExistingCauses(JgsValue exception) =>
        Field(exception, "cause") is { Type: JgsType.Cell } held ? held.AsCell : [];

    /// <summary>One field of a struct-shaped value, or null when it has no such field.</summary>
    private static JgsValue? Field(JgsValue value, string name) =>
        value.Type == JgsType.Struct && value.AsStruct.TryGetValue(name, out JgsValue? held) ? held : null;

    /// <summary>
    /// Whether a value is an object: an instance of a user class, or one of the values that carry a
    /// class name because they stand for a MATLAB object (MException, containers.Map, the spatial
    /// reference types). It is the same question <see cref="IsStructValue"/> answers the other way
    /// round, which is why the two read one property between them.
    /// </summary>
    internal static bool IsObjectValue(JgsValue value) =>
        value.Type == JgsType.Object
        || (value.Type == JgsType.Struct && value.ClassName is not null);

    /// <summary>
    /// Whether a value is a plain struct. A struct carrying a class name is not one: it is the
    /// representation an object is kept in, and <c>isstruct</c> saying otherwise is what let
    /// <c>isstruct(MException('a:b', 'x'))</c> answer true (M68). A struct carrying a time tag is not
    /// one either, for the same reason: a <c>calendarDuration</c> keeps its three components in a
    /// struct array because that storage already knows how to be an array, and the tag is what says
    /// the storage is not the type (M82).
    /// </summary>
    internal static bool IsStructValue(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName is null && value.TimeTag is null;

    /// <summary>The property names of whatever <paramref name="value"/> is, in declaration order.</summary>
    private static IEnumerable<string> PropertyNames(
        string builtin, JgsValue value, Interpreter interpreter, int line, int col)
    {
        if (value.Type == JgsType.Object)
        {
            return value.AsObject.Class.Properties.Select(static p => p.Spec.Name);
        }

        if (NamedClass(value, interpreter) is { } definition)
        {
            return definition.Properties.Select(static p => p.Spec.Name);
        }

        if (value.Type == JgsType.Struct)
        {
            return value.AsStructArray.FieldNames;
        }

        throw new JgsRuntimeException(line, col,
            $"{builtin}: a {value.TypeName} has no properties to list.");
    }

    /// <summary>The method names of whatever <paramref name="value"/> is, in declaration order.</summary>
    private static IEnumerable<string> MethodNames(
        string builtin, JgsValue value, Interpreter interpreter, int line, int col)
    {
        if (value.Type == JgsType.Object)
        {
            return value.AsObject.Class.MethodNames;
        }

        if (NamedClass(value, interpreter) is { } definition)
        {
            return definition.MethodNames;
        }

        // A function handle's one method is calling it, and a value of any other kind has none. Saying
        // so with an empty list rather than an error is what MATLAB does, and it is what lets a script
        // ask about a value it has not looked at yet.
        return value.Type is JgsType.Function or JgsType.Struct
            ? []
            : throw new JgsRuntimeException(line, col,
                $"{builtin}: a {value.TypeName} has no methods to list.");
    }

    /// <summary>
    /// The class a value <em>names</em>: <c>properties('Circle')</c> asks about the class rather than
    /// about the char row. Null when the value is not the name of a loaded class.
    /// </summary>
    private static JgsClass? NamedClass(JgsValue value, Interpreter interpreter) =>
        value.Type == JgsType.String && interpreter.Classes.TryGetValue(value.AsString, out JgsClass? definition)
            ? definition
            : null;

    /// <summary>A cell column of names — the shape MATLAB's <c>properties</c> and <c>methods</c> answer.</summary>
    private static JgsValue CellColumn(IEnumerable<string> names)
    {
        JgsValue[] cells = [.. names.Select(JgsValue.Str)];
        JgsValue column = JgsValue.Cell(cells);
        column.Reshape(cells.Length, cells.Length == 0 ? 0 : 1);
        return column;
    }
}
