namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>codeIssues</c> (App Designer window plan W10): R2025b's value class over the Code Analyzer's findings -
/// <c>Date</c>, <c>Release</c>, <c>Files</c>, <c>CodeAnalyzerConfiguration</c>, <c>Issues</c> and
/// <c>SuppressedIssues</c> as its <c>codeIssues.m</c> declares them - with <c>fix</c> rewriting the files and
/// <c>export</c> refused (JGraph writes no SARIF).
/// </summary>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The Code Analyzer's issues object.</summary>
    public const string CodeIssues = "codeIssues";

    private static ClassdefStmt CodeIssuesDeclaration(Interpreter interpreter)
    {
        var properties = new List<ClassProperty>
        {
            new(new ArgumentSpec("Date", null, null, [], new PreEvaluated(JgsValue.Str(string.Empty))), Constant: false),
            new(new ArgumentSpec("Release", null, null, [], new PreEvaluated(JgsValue.StringScalar(string.Empty))), Constant: false),
            new(new ArgumentSpec("Files", null, null, [], new PreEvaluated(JgsValue.StringArray([], 0, 1))), Constant: false),
            new(new ArgumentSpec("CodeAnalyzerConfiguration", null, null, [], new PreEvaluated(JgsValue.StringScalar("active"))), Constant: false),
            new(new ArgumentSpec("Issues", null, null, [], new PreEvaluated(JgsBuiltins.IssuesTable([], suppressed: false))), Constant: false),
            new(new ArgumentSpec("SuppressedIssues", null, null, [], new PreEvaluated(JgsBuiltins.IssuesTable([], suppressed: true))), Constant: false),
        };
        var methods = new List<ClassMethod>
        {
            // The constructor in C#, as ToolboxOptions' (W13): once the class is declared, the bare name resolves to the
            // class, so its constructor takes what the builtin function takes.
            new(new FnStmt(CodeIssues, ["varargin"], [], ["obj"]) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction(CodeIssues, (args, line, col) => JgsValue.Object(JgsBuiltins.ConstructCodeIssues(interpreter, [.. args.Skip(1)], line, col)))
                {
                    KeepsStringArguments = true,
                },
            },
            new(new FnStmt("fix", ["obj", "varargin"], [], ["obj", "status"]) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction("fix", (args, line, col) => JgsBuiltins.FixCodeIssues(interpreter, args, 1, line, col)[0])
                {
                    KeepsStringArguments = true,
                    MultiOutput = (args, wanted, line, col) => JgsBuiltins.FixCodeIssues(interpreter, args, wanted, line, col),
                },
            },
            new(new FnStmt("export", ["obj", "varargin"], [], []) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction("export", static (_, line, col) => throw new JgsRuntimeException(line, col, "MATLAB:codeanalyzer:ExportNotAvailable",
                    "export is not available in JGraph: it writes no SARIF or SonarQube file. The Issues and SuppressedIssues tables can be written with writetable."))
                {
                    KeepsStringArguments = true,
                    BindsAnsAsStatement = false,
                },
            },
        };
        return new ClassdefStmt(CodeIssues, isHandle: false, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
            Sealed = true,
        };
    }
}
