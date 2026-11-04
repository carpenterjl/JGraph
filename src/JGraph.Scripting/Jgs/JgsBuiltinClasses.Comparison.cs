namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>comparisons.TextComparison</c> and <c>comparisons.MlappComparison</c> (App Designer window plan W14), what
/// <c>visdiff</c> answers with an output: handle objects with <c>Left</c> and <c>Right</c>, the files' paths, <c>publish</c>
/// writing the report, and <c>filter</c> refusing as R2025b's do (<c>comparisons:FilterNotSupported</c>).
/// </summary>
internal static partial class JgsBuiltinClasses
{
    /// <summary>Two text files' comparison.</summary>
    public const string TextComparison = "comparisons.TextComparison";

    /// <summary>Two apps' comparison.</summary>
    public const string MlappComparison = "comparisons.MlappComparison";

    private static ClassdefStmt ComparisonDeclaration(string name, Interpreter interpreter)
    {
        var properties = new List<ClassProperty>
        {
            new(new ArgumentSpec("Left", null, null, [], new PreEvaluated(JgsValue.Str(string.Empty))), Constant: false),
            new(new ArgumentSpec("Right", null, null, [], new PreEvaluated(JgsValue.Str(string.Empty))), Constant: false),
        };
        var methods = new List<ClassMethod>
        {
            new(new FnStmt("publish", ["obj", "varargin"], [], ["reportLocation"]) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction("publish", (args, line, col) => JgsBuiltins.PublishComparison(interpreter, args, line, col)) { KeepsStringArguments = true },
            },
            new(new FnStmt("filter", ["obj", "varargin"], [], []) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction("filter", static (_, line, col) => throw new JgsRuntimeException(line, col, "comparisons:FilterNotSupported", "Filtering is not supported for this comparison type."))
                {
                    KeepsStringArguments = true,
                    BindsAnsAsStatement = false,
                },
            },
        };
        return new ClassdefStmt(name, isHandle: true, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = ["handle"],
        };
    }
}
