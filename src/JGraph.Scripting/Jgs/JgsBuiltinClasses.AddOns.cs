namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.addons.toolbox.ToolboxOptions</c> (App Designer window plan W13): R2025b's properties in the order its
/// display lists them (probe <c>w13_apps</c>), each set checked by <see cref="JgsBuiltins.SetToolboxOption"/>;
/// <c>Identifier</c> and <c>ToolboxFolder</c> are set by the constructor alone.
/// </summary>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The toolbox options class.</summary>
    public const string ToolboxOptions = "matlab.addons.toolbox.ToolboxOptions";

    /// <summary>The options' public properties, in the order R2025b lists them.</summary>
    public static readonly string[] ToolboxOptionNames =
    [
        "AppGalleryFiles", "AuthorCompany", "AuthorEmail", "AuthorName", "Description", "OutputFile", "RequiredAdditionalSoftware",
        "RequiredAddons", "Summary", "SupportedPlatforms", "ToolboxGettingStartedGuide", "ToolboxImageFile", "ToolboxFiles",
        "ToolboxJavaPath", "ToolboxMatlabPath", "ToolboxName", "ToolboxVersion", "MinimumMatlabRelease", "MaximumMatlabRelease",
        "Identifier", "ToolboxFolder",
    ];

    /// <summary>The names a name-value pair may set on construction (the immutable two excluded).</summary>
    public static readonly string[] ToolboxOptionSettable = [.. ToolboxOptionNames.Where(static n => n is not ("Identifier" or "ToolboxFolder"))];

    private static ClassdefStmt ToolboxOptionsDeclaration(Interpreter interpreter)
    {
        var properties = new List<ClassProperty>();
        var methods = new List<ClassMethod>();
        foreach (string name in ToolboxOptionNames)
        {
            properties.Add(new ClassProperty(new ArgumentSpec(name, null, null, [], new PreEvaluated(JgsBuiltins.ToolboxOptionDefault(name))), Constant: false));
            string captured = name;
            methods.Add(Setter(name, (copy, value, line, col) => JgsBuiltins.SetToolboxOption(interpreter, copy, captured, value, line, col)));
        }

        // The constructor in C#, as ComponentContainer's (U10): once the class is declared, the dotted name resolves to the
        // class, so its constructor must take (path, identifier, Name, Value, ...) as the builtin function does.
        methods.Add(new ClassMethod(new FnStmt(ToolboxOptions, ["varargin"], [], ["obj"]) { Dialect = JgsDialect.Matlab }, Static: false)
        {
            Native = new BuiltinFunction(ToolboxOptions, (args, line, col) => JgsValue.Object(JgsBuiltins.ConstructToolboxOptions(interpreter, [.. args.Skip(1)], line, col)))
            {
                KeepsStringArguments = true,
            },
        });

        return new ClassdefStmt(ToolboxOptions, isHandle: false, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
        };
    }
}
