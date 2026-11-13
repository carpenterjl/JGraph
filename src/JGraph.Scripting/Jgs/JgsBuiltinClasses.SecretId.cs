namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>secretID</c>, the secret API's value class (open item 173), declared here as the compiler's
/// option classes are: Sealed, a read-only <c>Name</c> string, a constructor that checks its one
/// argument as R2025b's <c>validateattributes(name, ["string", "char"], ["scalartext", "nonempty"])</c>
/// does, and <c>getSecret</c>, which asks the secret function for <c>obj.Name</c> (R2025b's
/// <c>toolbox/matlab/authnz/secretapis/publicBuiltins/secretID.m</c>, read in place; every answer
/// measured with probe <c>oi173_secretid</c>).
/// </summary>
/// <remarks>
/// R2025b's class inherits <c>matlab.mixin.CustomDisplay</c> and
/// <c>matlab.mixin.CustomCompactDisplayProvider</c>, which this build does not supply as classes; the
/// two names are answered by <c>isa</c> and <c>superclasses</c> (<see cref="ShownSuperclassesOf"/>) and
/// nothing is inherited from them. Its three public display methods are listed, and
/// <c>annotation</c> answers as R2025b's does; the two compact-representation methods need a
/// display configuration object no script can make, so calling one is a refusal.
/// </remarks>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The secret API's class.</summary>
    public const string SecretId = "secretID";

    /// <summary>The superclasses R2025b names for <c>secretID</c>, in the order <c>superclasses</c> lists them.</summary>
    private static readonly string[] SecretIdSuperclasses = ["matlab.mixin.CustomDisplay", "matlab.mixin.CustomCompactDisplayProvider"];

    /// <summary>
    /// The superclass names a built-in class answers to without inheriting anything from them - mixins
    /// MATLAB ships and this build does not supply as classes - or empty.
    /// </summary>
    public static IReadOnlyList<string> ShownSuperclassesOf(string className) =>
        string.Equals(className, SecretId, StringComparison.Ordinal) ? SecretIdSuperclasses : [];

    private static ClassdefStmt SecretIdDeclaration(Interpreter interpreter)
    {
        var name = new ClassProperty(new ArgumentSpec("Name", null, "string", [], new PreEvaluated(JgsValue.StringScalar(string.Empty))), Constant: false)
        {
            SetAccess = new MemberAccess(MemberAccessKind.Private),
        };

        ClassMethod Body(string method, string[] parameters, string[] outputs, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            new(new FnStmt(method, parameters, [], outputs) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction(method, body) { KeepsStringArguments = true },
            };

        ClassMethod[] methods =
        [
            Body(SecretId, ["name"], ["obj"], static (args, line, col) =>
            {
                JgsObject made = args[0].AsObject;
                made.Fields["Name"] = JgsBuiltins.SecretIdName(args.Skip(1).ToList(), line, col);
                return args[0];
            }),
            Body("getSecret", ["obj"], ["val"], (args, line, col) => JgsBuiltins.SecretOfIds(interpreter.Host, args, line, col)),
            Body("annotation", ["obj"], ["res"], static (args, line, col) => SecretIdAnnotation(args, line, col)),
            Body("compactRepresentationForSingleLine", ["obj", "displayConfiguration", "width"], ["displayRep"], static (_, line, col) =>
                throw CompactRefusal("compactRepresentationForSingleLine", line, col)),
            Body("compactRepresentationForColumn", ["obj", "displayConfiguration", "width"], ["displayRep"], static (_, line, col) =>
                throw CompactRefusal("compactRepresentationForColumn", line, col)),
        ];

        return new ClassdefStmt(SecretId, isHandle: false, [name], methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
            Sealed = true,
        };
    }

    /// <summary><c>res = annotation(obj)</c>: a column of <c>"secretID"</c>, one per row of the array (R2025b's own body).</summary>
    private static JgsValue SecretIdAnnotation(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        int rows = JgsBuiltins.SizeDims(args[0])[0];
        var cells = new JgsValue[rows];
        System.Array.Fill(cells, JgsValue.Str(SecretId));
        return JgsValue.StringArray(cells, rows, 1);
    }

    private static JgsRuntimeException CompactRefusal(string method, int line, int col) =>
        new(line, col, "MATLAB:class:MethodNotSupported",
            $"'{method}' is called by MATLAB's compact display with a display configuration object, which this build does not make.");
}
