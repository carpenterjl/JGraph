using JGraph.Scripting.Designer;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>visdiff</c> (App Designer window plan W14): <c>visdiff(name1, name2)</c> opens the host's Comparison Tool on two
/// files, <c>visdiff(name1, name2, type)</c> with the type <c>'text'</c>, and <c>c = visdiff(...)</c> answers a
/// <c>comparisons.TextComparison</c> or, for two apps, a <c>comparisons.MlappComparison</c> whose <c>publish</c> writes
/// the report. The checks and refusals are R2025b's (probe <c>w14_compare</c>, <c>mldesktop.xml</c>): a missing file,
/// a type the files do not take, folders, a comparison with neither an output nor a window. JGraph compares an app with
/// a plain file as text where R2025b refuses without a window, and compares no folders.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterComparisonBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> ComparisonBuiltinNames { get; } = ["visdiff"];

    private static void RegisterComparisonBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register("visdiff", JgsValue.Function(new BuiltinFunction("visdiff",
            (args, line, col) => FirstOf(Visdiff(interpreter, host, args, 1, line, col)))
        {
            KeepsStringArguments = true,
            // Without an output the Comparison Tool opens; with one the comparison is answered: the count is part of what it does.
            BindsAnsAsStatement = false,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => Visdiff(interpreter, host, args, wanted, line, col),
        }));
    }

    private static JgsValue[] Visdiff(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (wanted > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", $"Invalid argument list. Function requires {2 - args.Count} more input(s).");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        for (int i = 0; i < 2; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                    $"The value of 'filename{i + 1}' is invalid. Expected input to be one of these types:\n\nchar, string\n\nInstead its type was {ClassOf(args[i], interpreter.Dialect)}.");
            }
        }

        string first = ComparisonFile(interpreter, host, TextOf(args[0]), line, col);
        string second = ComparisonFile(interpreter, host, TextOf(args[1]), line, col);
        if (Directory.Exists(first) || Directory.Exists(second))
        {
            // R2025b compares folders in its tool and refuses them with an output; JGraph has no folder comparison.
            throw wanted > 0
                ? new JgsRuntimeException(line, col, "comparisons:mldesktop:CannotCompareWithoutDisplay", $"Unable to compare '{first}' and '{second}' without opening the Comparison Tool.")
                : new JgsRuntimeException(line, col, "comparisons:mldesktop:UnknownComparisonType", "Comparison type 'folder' not available for the specified files.");
        }

        if (args.Count == 3)
        {
            if (!IsTextScalar(args[2]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                    $"The value of 'type' is invalid. Expected input to be one of these types:\n\nchar, string\n\nInstead its type was {ClassOf(args[2], interpreter.Dialect)}.");
            }

            string type = TextOf(args[2]);
            if (!type.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                throw wanted > 0
                    ? new JgsRuntimeException(line, col, "comparisons:mldesktop:UnknownComparisonTypeForNoDisplay", $"Comparison type '{type}' not available for the specified files without opening the Comparison Tool.")
                    : new JgsRuntimeException(line, col, "comparisons:mldesktop:UnknownComparisonType", $"Comparison type '{type}' not available for the specified files.");
            }
        }

        if (wanted == 0)
        {
            if (host.ComparisonTool is not { } tool)
            {
                throw new JgsRuntimeException(line, col, "comparisons:mldesktop:UnableToCompareWithNoDisplay",
                    $"Unable to compare '{first}' and '{second}' without a display. Try returning a comparison object and run the comparison again.");
            }

            if (tool.Compare(first, second) is { } refused)
            {
                throw new JgsRuntimeException(line, col, "comparisons:mldesktop:UnableToCompare", refused);
            }

            return [];
        }

        bool apps = args.Count < 3 && DesignCompare.IsApp(first) && DesignCompare.IsApp(second);
        string className = apps ? JgsBuiltinClasses.MlappComparison : JgsBuiltinClasses.TextComparison;
        JgsClass definition = interpreter.BuiltinClass(className)
            ?? throw new JgsRuntimeException(line, col, $"The class '{className}' is not available in this build.");
        JgsObject comparison = definition.NewDefault(line, col);
        comparison.Fields["Left"] = JgsValue.Str(first);
        comparison.Fields["Right"] = JgsValue.Str(second);
        return [JgsValue.Object(comparison)];
    }

    /// <summary>The full path of a file named to <c>visdiff</c>: as given, against the current folder, or on the path; a folder as it is.</summary>
    private static string ComparisonFile(Interpreter interpreter, JGraphScriptGlobals host, string name, int line, int col)
    {
        string resolved = host.Resolve(name);
        if (File.Exists(resolved) || Directory.Exists(resolved))
        {
            return Path.GetFullPath(resolved);
        }

        if (!Path.IsPathRooted(name) && interpreter.FunctionPath?.FindFile(name) is { } onPath)
        {
            return onPath;
        }

        throw new JgsRuntimeException(line, col, "comparisons:mldesktop:FileNotFound", $"Unable to load file '{Path.GetFullPath(resolved)}'.");
    }

    /// <summary>
    /// <c>publish(c, Name, Value, ...)</c>: the report written as HTML (R2025b also writes Word and PDF, which JGraph does
    /// not) into <c>OutputFolder</c> (the current folder; one that must exist) under <c>Name</c> (the two files' names
    /// joined by an underscore), answering the report's path.
    /// </summary>
    internal static JgsValue PublishComparison(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        JgsObject comparison = args[0].AsObject;
        string left = TextOf(comparison.Fields["Left"]);
        string right = TextOf(comparison.Fields["Right"]);
        JGraphScriptGlobals host = interpreter.Host as JGraphScriptGlobals ?? throw new JgsRuntimeException(line, col, "The session has no host.");
        string format = "html";
        string folder = host.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(left) + "_" + Path.GetFileNameWithoutExtension(right);
        if ((args.Count - 1) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue", "Expected a value to follow the parameter name.");
        }

        for (int i = 1; i + 1 < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string option = ParameterName(TextOf(args[i]), ["Format", "OutputFolder", "Name"], line, col);
            JgsValue value = args[i + 1];
            if (!IsTextScalar(value))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType", $"The value of '{option}' is invalid. Expected input to be one of these types:\n\nchar, string\n\nInstead its type was {ClassOf(value, interpreter.Dialect)}.");
            }

            string text = TextOf(value);
            switch (option)
            {
                case "Format":
                    string[] formats = ["html", "docx", "pdf"];
                    string? chosen = formats.FirstOrDefault(f => f.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (chosen is null)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:unrecognizedStringChoice",
                            $"Expected input to match one of these values:\n\n'html', 'docx', 'pdf'\n\nThe input, '{text}', did not match any of the valid values.");
                    }

                    format = chosen;
                    break;
                case "OutputFolder":
                    string full = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(host.CurrentDirectory, text));
                    if (!Directory.Exists(full))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeFolder", $"Invalid argument at position 2. These folders do not exist: '{full}'.");
                    }

                    folder = full;
                    break;
                default:
                    name = text;
                    break;
            }
        }

        if (format != "html")
        {
            throw new JgsRuntimeException(line, col, "JGraph:visdiff:FormatNotSupported", $"JGraph publishes a comparison to HTML; the '{format}' format is not supported.");
        }

        string reportPath = Path.Combine(folder, name + ".html");
        try
        {
            FileComparison compared = FileComparison.Compare(left, right);
            File.WriteAllText(reportPath, compared.HtmlReport());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "comparisons:mldesktop:FileWriteError", $"Unable to write file '{reportPath}'. {ex.Message}.");
        }

        return JgsValue.Str(reportPath);
    }
}
