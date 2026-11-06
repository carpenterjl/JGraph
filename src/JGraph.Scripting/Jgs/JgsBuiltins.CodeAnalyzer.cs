using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JGraph.Data;
using JGraph.Scripting.Analysis;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>checkcode</c>, <c>mlint</c> and <c>codeIssues</c> (App Designer window plan W10): the Code Analyzer run over
/// files from the prompt, in R2025b's forms as measured headless (<c>w10_analyzer</c>, <c>w10_rules</c>) -
/// <c>checkcode</c>'s display (<c>L 14 (C 10-15): message</c>, a file header when several files are named), its
/// <c>-id</c>, <c>-string</c>, <c>-struct</c>, <c>-fullpath</c>, <c>-text</c>, <c>-cyc</c>, <c>-modcyc</c>,
/// <c>-notok</c> and <c>-severity</c>, the struct's fields, the two outputs, the cell of files and the refusals in
/// R2025b's identifiers and words; <c>codeIssues</c>' object over files and folders with its <c>Issues</c> and
/// <c>SuppressedIssues</c> tables and <c>fix</c>. The display prints no <c>opentoline</c> links, as a session with no
/// hot links prints none.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterCodeAnalyzerBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> CodeAnalyzerBuiltinNames { get; } = ["checkcode", "mlint", JgsBuiltinClasses.CodeIssues];

    private static readonly string[] CodeFileExtensions = [".m", ".mlx", ".mlapp", ".mapp"];

    private static void RegisterCodeAnalyzerBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        foreach (string name in (string[])["checkcode", "mlint"])
        {
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => FirstOf(Checkcode(interpreter, host, args, 1, line, col)))
            {
                KeepsStringArguments = true,
                // Without an output the messages are displayed; with one they are answered: the count is part of what it does.
                BindsAnsAsStatement = false,
                KnowsWhenDiscarded = true,
                MultiOutput = (args, wanted, line, col) => Checkcode(interpreter, host, args, wanted, line, col),
            }));
        }

        env.Builtins.Register(JgsBuiltinClasses.CodeIssues, JgsValue.Function(new BuiltinFunction(JgsBuiltinClasses.CodeIssues,
            (args, line, col) => JgsValue.Object(ConstructCodeIssues(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));
    }

    // --- checkcode and mlint ------------------------------------------------------------------------------------

    private const string MlintMustBeText = "Arguments must be character vectors or string scalars. File name arguments can also be string arrays or cell arrays of character vectors.";

    private sealed record CheckedFile(string Shown, IReadOnlyList<CodeMessage> Messages);

    private static JgsValue[] Checkcode(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (wanted > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
        }

        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        // The arguments: words (names and options) and at most one list of files.
        var words = new List<string>();
        List<string>? listed = null;
        foreach (JgsValue argument in args)
        {
            if (argument.Type == JgsType.Cell || (argument.IsStringArray && argument.ArrayLength != 1))
            {
                if (listed is not null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:mlint:TooManyCellArgs",
                        "Multiple cell arrays of character vectors or string arrays not supported. Specify file name as one or more character vectors, one string array, or one cell array of character vectors. All other arguments must be either a character vector or a scalar string.");
                }

                listed = [];
                JgsValue[] elements = argument.Type == JgsType.Cell ? argument.AsCell : argument.AsArray;
                foreach (JgsValue element in elements)
                {
                    if (element.Type == JgsType.Cell)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:mlint:NestedCell", "Input arguments to checkcode should not contain nested cell arrays.");
                    }

                    listed.Add(MlintText(element, line, col));
                }

                continue;
            }

            words.Add(MlintText(argument, line, col));
        }

        string outputType = wanted == 0 ? "display" : "struct";
        bool ids = false, fullPath = false, asText = false, cyc = false, modcyc = false, notok = false, severity = false;
        var ignored = new List<string>();
        var rest = new List<string>();
        foreach (string word in words)
        {
            if (word.Length > 1 && word[0] == '-')
            {
                switch (word)
                {
                    case "-string": outputType = "string"; break;
                    case "-disp" or "-display": outputType = "display"; break;
                    case "-struct": outputType = "struct"; break;
                    case "-id": ids = true; break;
                    case "-fullpath": fullPath = true; break;
                    case "-text": asText = true; break;
                    case "-cyc": cyc = true; break;
                    case "-modcyc": modcyc = true; break;
                    case "-notok": notok = true; break;
                    case "-severity": severity = true; break;
                    case "-eml" or "-codegen" or "-m0" or "-m1" or "-m2" or "-m3": break;
                    default:
                        if (!word.StartsWith("-config", StringComparison.Ordinal))
                        {
                            ignored.Add(word);
                        }

                        break;
                }
            }
            else
            {
                rest.Add(word);
            }
        }

        if (outputType == "display" && wanted > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mlint:TooManyOutputArguments", "Too many output arguments.");
        }

        var files = new List<string>();
        string? textSource = null;
        bool asList = listed is not null;
        if (listed is not null)
        {
            files.AddRange(listed);
            if (rest.Count > 0)
            {
                host.Warnings.Record("MATLAB:mlint:UnusedArgs", "The following arguments were ignored: " + string.Join(" ", rest) + ".");
            }
        }
        else if (asText)
        {
            if (rest.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:mlint:InvalidInput", "-TEXT requires one argument with text, and a second one with a .m filename");
            }

            textSource = rest[0];
            files.Add(rest[0]);
            files.Add(rest[1]);
        }
        else
        {
            files.AddRange(rest);
            if (files.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:mlint:NoFile", "No file name provided.");
            }

            asList = files.Count > 1;
        }

        // The analysis of each.
        var results = new List<CheckedFile>();
        if (textSource is not null)
        {
            string name = files[1];
            results.Add(Path.GetExtension(name).Equals(".m", StringComparison.OrdinalIgnoreCase)
                ? new CheckedFile(name, Describe(JgsCodeAnalyzer.Analyze(textSource, name), cyc, modcyc, notok, ignored))
                : new CheckedFile(name, Notes(ignored, $"Unable to run code analysis. '{name}' has an invalid file extension.")));
        }
        else
        {
            for (int i = 0; i < files.Count; i++)
            {
                string asked = files[i];
                string shown;
                string path;
                if (fullPath)
                {
                    shown = asked;
                    path = Path.IsPathRooted(asked) ? asked : Path.Combine(host.CurrentDirectory, asked);
                    if (!CodeFileExtensions.Contains(Path.GetExtension(asked), StringComparer.OrdinalIgnoreCase))
                    {
                        results.Add(new CheckedFile(shown, Notes(ignored, $"Unable to run code analysis. '{asked}' has an invalid file extension.")));
                        continue;
                    }

                    if (!File.Exists(path))
                    {
                        throw MlintFileNotFound(asked, line, col);
                    }
                }
                else
                {
                    path = ResolveCodeFile(interpreter, host, asked, partialPaths: true) ?? throw MlintFileNotFound(asked, line, col);
                    shown = path;
                    files[i] = path;
                }

                string text = ReadCode(path, line, col);
                results.Add(new CheckedFile(shown, Describe(JgsCodeAnalyzer.Analyze(text, path), cyc, modcyc, notok, ignored)));
            }
        }

        switch (outputType)
        {
            case "display":
            {
                string text = CheckcodeText(results, asList, ids);
                if (text.Length > 0)
                {
                    host.print(text);
                }

                return [];
            }

            case "string":
            {
                string text = CheckcodeText(results, asList, ids);
                return Outputs(JgsValue.Str(text), files, wanted);
            }

            default:
            {
                var structs = results.Select(r => MessageStruct(r.Messages, ids, severity)).ToList();
                JgsValue first = asList ? CellColumnOf(structs) : structs[0];
                return Outputs(first, files, wanted);
            }
        }
    }

    /// <summary>A cell column, the shape <c>checkcode</c> answers a list of files in.</summary>
    private static JgsValue CellColumnOf(IReadOnlyList<JgsValue> cells)
    {
        JgsValue column = JgsValue.Cell([.. cells]);
        column.Reshape(cells.Count, cells.Count == 0 ? 0 : 1);
        return column;
    }

    private static JgsValue[] Outputs(JgsValue first, IReadOnlyList<string> files, int wanted) =>
        wanted >= 2 ? [first, CellColumnOf([.. files.Select(JgsValue.Str)])] : [first];

    private static string MlintText(JgsValue value, int line, int col)
    {
        if (!IsTextScalar(value) || value.IsCharMatrix)
        {
            if (value.IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:mlint:NotCharacterRowVector", "Character vector arguments must be row vectors.");
            }

            throw new JgsRuntimeException(line, col, "MATLAB:mlint:CheckCodeInputMustBeOfCharType", MlintMustBeText);
        }

        string text = TextOf(value);
        if (text.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mlint:EmptyInput", "Empty arguments not supported.");
        }

        return text;
    }

    private static JgsRuntimeException MlintFileNotFound(string asked, int line, int col) =>
        new(line, col, "MATLAB:mlint:FileNotFound", $"Unable to open file \"{asked}\". File is not found or is not a supported MATLAB code file.");

    /// <summary>
    /// A code file named to the prompt: a path as given or against the current folder, or a name on the path, with or
    /// without its extension. With <paramref name="partialPaths"/>, the forms <c>checkcode</c> and <c>edit</c> take as well
    /// (stage P5, measured): <c>Thing/extent</c> (a class folder's method file by its slash name), <c>+pkg/g</c> and
    /// <c>@Thing/extent</c> (a relative path under a search folder, with or without its extension) and <c>extent</c> (a
    /// bare method name, the first class folder's file as <c>which</c> names it) - where <c>codeIssues</c> refuses them all,
    /// and a dotted <c>pkg.f</c> or <c>Thing.extent</c> is a file for neither.
    /// </summary>
    internal static string? ResolveCodeFile(Interpreter interpreter, JGraphScriptGlobals host, string asked, bool partialPaths = false)
    {
        string? found = ResolveCodeFileByName(interpreter, host, asked);
        if (found is not null || !partialPaths || interpreter.FunctionPath is not { } search)
        {
            return found;
        }

        if (JgsFunctionPath.SlashForm(asked) is { } slashed && search.ClassMethodFile(slashed.Class, slashed.Member) is { } methodFile)
        {
            return methodFile;
        }

        if (search.FindRelative(asked) is { } relative)
        {
            return relative;
        }

        if (Path.GetExtension(asked).Length == 0)
        {
            foreach (string extension in CodeFileExtensions)
            {
                if (search.FindRelative(asked + extension) is { } withExtension)
                {
                    return withExtension;
                }
            }
        }

        return search.MethodFilesByBareName(asked).FirstOrDefault();
    }

    private static string? ResolveCodeFileByName(Interpreter interpreter, JGraphScriptGlobals host, string asked)
    {
        if (asked.Length == 0)
        {
            return null;
        }

        string resolved = host.Resolve(asked);
        if (File.Exists(resolved))
        {
            return Path.GetFullPath(resolved);
        }

        if (Path.GetExtension(asked).Length == 0)
        {
            foreach (string extension in CodeFileExtensions)
            {
                if (File.Exists(resolved + extension))
                {
                    return Path.GetFullPath(resolved + extension);
                }
            }
        }

        if (Path.IsPathRooted(asked))
        {
            return null;
        }

        try
        {
            string? onPath = interpreter.FunctionPath?.FindFile(asked);
            if (onPath is not null && File.Exists(onPath))
            {
                return onPath;
            }
        }
        catch (ArgumentException)
        {
            // a name the path cannot take
        }

        return null;
    }

    private static string ReadCode(string path, int line, int col)
    {
        try
        {
            return JgsMlapp.ReadSource(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mlint:FileNotFound", $"Unable to open file \"{path}\". File is not found or is not a supported MATLAB code file.");
        }
    }

    private static List<CodeMessage> Notes(IReadOnlyList<string> ignored, params string[] texts)
    {
        var notes = new List<CodeMessage>();
        foreach (string option in ignored)
        {
            notes.Add(new CodeMessage(string.Empty, CodeSeverity.Info, 0, 0, 0, $"Option '{option}' is ignored because it is invalid."));
        }

        foreach (string text in texts)
        {
            notes.Add(new CodeMessage(string.Empty, CodeSeverity.Info, 0, 0, 0, text));
        }

        return notes;
    }

    /// <summary>The messages <c>checkcode</c> lists for one analysis: with the complexity lines asked for; with <c>-notok</c> every message, the <c>%#ok</c> comments unread.</summary>
    private static List<CodeMessage> Describe(CodeAnalysis analysis, bool cyc, bool modcyc, bool notok, IReadOnlyList<string> ignored)
    {
        List<CodeMessage> messages = Notes(ignored);
        if (notok)
        {
            var all = analysis.Messages.Where(static m => m.Id is not ("MSNU" or "MSNE")).Concat(analysis.Suppressed.Select(static m => m with { Suppression = CodeSuppression.No })).ToList();
            foreach (CodeComplexity c in analysis.Complexity)
            {
                if (cyc)
                {
                    all.Add(c.McCabeMessage);
                }

                if (modcyc)
                {
                    all.Add(c.ModifiedMessage);
                }
            }

            messages.AddRange(CodeAnalysis.Sort(all));
        }
        else
        {
            messages.AddRange(analysis.With(cyc, modcyc));
        }

        return messages;
    }

    private static string CheckcodeText(IReadOnlyList<CheckedFile> results, bool asList, bool ids)
    {
        var text = new StringBuilder();
        foreach (CheckedFile result in results)
        {
            if (asList)
            {
                text.Append("========== ").Append(result.Shown).Append(" ==========\n");
            }

            foreach (CodeMessage m in result.Messages)
            {
                text.Append("L ").Append(m.Line.ToString(CultureInfo.InvariantCulture)).Append(" (").Append(m.Columns).Append("): ");
                if (ids && m.Id.Length > 0)
                {
                    text.Append(m.Id).Append(": ");
                }

                text.Append(m.Text).Append('\n');
            }
        }

        return text.ToString();
    }

    private static readonly string[] CheckcodeFields = ["message", "fix", "line", "column"];

    private static JgsValue MessageStruct(IReadOnlyList<CodeMessage> messages, bool ids, bool severity)
    {
        if (messages.Count == 0)
        {
            // Empty: R2025b's field order differs from a filled struct's (measured).
            var fields = new List<string> { "message", "line", "column", "fix" };
            if (ids)
            {
                fields.Add("id");
            }

            if (severity)
            {
                fields.Add("severity");
            }

            return JgsValue.StructArray(new JgsStructArray([], [.. fields]), 0, 0);
        }

        var order = new List<string>();
        if (ids)
        {
            order.Add("id");
        }

        order.AddRange(CheckcodeFields.Take(2));
        if (severity)
        {
            order.Add("severity");
        }

        order.AddRange(CheckcodeFields.Skip(2));
        var rows = new Dictionary<string, JgsValue>[messages.Count];
        for (int i = 0; i < messages.Count; i++)
        {
            CodeMessage m = messages[i];
            // The fields in R2025b's order, which is the order they are added in.
            var row = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach (string field in order)
            {
                row[field] = field switch
                {
                    "id" => JgsValue.Str(m.Id),
                    "message" => JgsValue.Str(m.Text),
                    "fix" => JgsValue.Number(m.Fixable ? 1 : 0),
                    "severity" => JgsValue.Number((int)m.Severity),
                    "line" => JgsValue.Number(m.Line),
                    _ => JgsMatrix.FromColumnMajor([m.StartColumn, m.EndColumn], 1, 2),
                };
            }

            rows[i] = row;
        }

        return JgsValue.StructArray(new JgsStructArray(rows, [.. order]), messages.Count, 1);
    }

    // --- codeIssues --------------------------------------------------------------------------------------------

    internal sealed record IssueRow(string Path, CodeMessage Message);

    private static JgsRuntimeException TooManyPositional(int line, int col) =>
        new(line, col, "MATLAB:TooManyInputs", "Invalid argument at position 2. Function requires 0 to 1 positional input(s).");

    /// <summary><c>codeIssues(names, IncludeSubfolders=, CodeAnalyzerConfiguration=)</c>: the files named, or every code file of the folders named (the current folder when none), analyzed.</summary>
    internal static JgsObject ConstructCodeIssues(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JGraphScriptGlobals host = interpreter.Host as JGraphScriptGlobals ?? throw new JgsRuntimeException(line, col, "The session has no host.");
        var names = new List<string>();
        bool includeSubfolders = true;
        string configuration = "active";
        int i = 0;
        if (args.Count > 0 && !(IsTextScalar(args[0]) && args.Count > 1 && IsOptionName(TextOf(args[0]))))
        {
            names.AddRange(NamesOf(args[0], line, col));
            i = 1;
        }

        for (; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]) || !IsOptionName(TextOf(args[i])))
            {
                throw TooManyPositional(line, col);
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue", $"Expected a value after the name '{TextOf(args[i])}'.");
            }

            string option = TextOf(args[i]);
            JgsValue value = args[i + 1];
            if (option.Equals("IncludeSubfolders", StringComparison.OrdinalIgnoreCase))
            {
                includeSubfolders = value.Type is JgsType.Bool or JgsType.Number ? value.AsNumber != 0
                    : throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNumericOrLogical", "Invalid argument at position 3. Value must be logical.");
            }
            else
            {
                if (!IsTextScalar(value) || TextOf(value).Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "Invalid argument at position 3. Value must be text with one or more characters.");
                }

                configuration = TextOf(value);
            }
        }

        if (names.Count == 0)
        {
            names.Add(host.CurrentDirectory);
        }

        var files = new List<string>();
        foreach (string name in names)
        {
            string resolved = host.Resolve(name);
            if (Directory.Exists(resolved))
            {
                files.AddRange(CodeFilesUnder(Path.GetFullPath(resolved), includeSubfolders));
                continue;
            }

            string? found = ResolveCodeFile(interpreter, host, name);
            if (found is not null)
            {
                files.Add(found);
                continue;
            }

            string extension = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);
            if (extension.Length > 0 && (!CodeFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || !Regex.IsMatch(stem, "^[A-Za-z]\\w*$")))
            {
                throw new JgsRuntimeException(line, col, "codeanalysis:reports:ccrAnalysis:InvalidFile",
                    $"Unable to open file '{name}'. MATLAB file names (*.m, *.mlx, *.mlapp) must start with a letter, and can only contain letters, digits, or underscores.");
            }

            throw new JgsRuntimeException(line, col, "codeanalysis:reports:ccrAnalysis:FileNotFound", $"Unable to open file '{name}'. File is not found or is not a supported MATLAB code file.");
        }

        return BuildCodeIssues(interpreter, files, configuration, line, col);
    }

    private static bool IsOptionName(string text) => text.Equals("IncludeSubfolders", StringComparison.OrdinalIgnoreCase) || text.Equals("CodeAnalyzerConfiguration", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> NamesOf(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Cell)
        {
            foreach (JgsValue element in value.AsCell)
            {
                if (!IsTextScalar(element))
                {
                    throw NotText(line, col);
                }

                yield return NonEmpty(TextOf(element), line, col);
            }

            yield break;
        }

        if (value.IsStringArray)
        {
            foreach (JgsValue element in value.AsArray)
            {
                yield return NonEmpty(TextOf(element), line, col);
            }

            yield break;
        }

        if (!IsTextScalar(value) || value.IsCharMatrix)
        {
            throw NotText(line, col);
        }

        yield return NonEmpty(TextOf(value), line, col);
    }

    private static JgsRuntimeException NotText(int line, int col) =>
        new(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "Invalid argument at position 1. Value must be a character vector, string array, or cell array of character vectors.");

    private static string NonEmpty(string text, int line, int col) => text.Length > 0
        ? text
        : throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "Invalid argument at position 1. Value must be text with one or more characters.");

    /// <summary>The code files of a folder, named in R2025b's order: by name, case set aside.</summary>
    private static List<string> CodeFilesUnder(string folder, bool includeSubfolders)
    {
        var files = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*", includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                if (CodeFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && Regex.IsMatch(Path.GetFileNameWithoutExtension(file), "^[A-Za-z]\\w*$"))
                {
                    files.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // what could be read
        }

        files.Sort(static (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        return files;
    }

    private static JgsObject BuildCodeIssues(Interpreter interpreter, IReadOnlyList<string> files, string configuration, int line, int col)
    {
        JgsClass definition = interpreter.BuiltinClass(JgsBuiltinClasses.CodeIssues)
            ?? throw new JgsRuntimeException(line, col, $"The class '{JgsBuiltinClasses.CodeIssues}' is not available in this build.");
        var active = new List<IssueRow>();
        var suppressed = new List<IssueRow>();
        // R2025b lists the issues by file name in ordinal order (A3Class before a1), the Files in case-blind order.
        foreach (string file in files.OrderBy(static f => f, StringComparer.Ordinal))
        {
            string text;
            try
            {
                text = JgsMlapp.ReadSource(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }

            CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(text, file);
            active.AddRange(analysis.Messages.Select(m => new IssueRow(file, m)));
            suppressed.AddRange(analysis.Suppressed.Select(m => new IssueRow(file, m)));
        }

        JgsObject issues = definition.NewDefault(line, col);
        double now = JgsTime.FromDateTime(DateTime.Now);
        issues.Fields["Date"] = JgsValue.Array([JgsValue.Number(now)]).MarkTime(JgsTime.DatetimeTag([now]));
        issues.Fields["Release"] = JgsValue.StringScalar(typeof(JGraphScriptGlobals).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        issues.Fields["Files"] = JgsValue.StringArray([.. files.Select(JgsValue.Str)], files.Count, 1);
        issues.Fields["CodeAnalyzerConfiguration"] = JgsValue.StringScalar(configuration);
        issues.Fields["Issues"] = IssuesTable(active, suppressed: false);
        issues.Fields["SuppressedIssues"] = IssuesTable(suppressed, suppressed: true);
        return issues;
    }

    /// <summary>R2025b's <c>Issues</c> table (and <c>SuppressedIssues</c>, with <c>Suppression</c> before <c>CheckID</c>).</summary>
    internal static JgsValue IssuesTable(IReadOnlyList<IssueRow> rows, bool suppressed)
    {
        int n = rows.Count;
        var columns = new List<TableColumn>
        {
            new JgsValueColumn("Location", Strings(rows.Select(r => Path.GetFileName(r.Path)))),
            new JgsValueColumn("Severity", Categories(rows.Select(r => r.Message.Severity.ToString().ToLowerInvariant()))),
            new JgsValueColumn("Fixability", Categories(rows.Select(r => r.Message.Fixable ? "auto" : "manual"))),
            new JgsValueColumn("Description", Strings(rows.Select(r => r.Message.Text))),
        };
        if (suppressed)
        {
            columns.Add(new JgsValueColumn("Suppression", Categories(rows.Select(r => r.Message.Suppression.ToString().ToLowerInvariant()))));
        }

        columns.Add(new JgsValueColumn("CheckID", Categories(rows.Select(r => r.Message.Id))));
        columns.Add(new NumberColumn("LineStart", [.. rows.Select(r => (double)r.Message.Line)]));
        columns.Add(new NumberColumn("LineEnd", [.. rows.Select(r => (double)r.Message.Line)]));
        columns.Add(new NumberColumn("ColumnStart", [.. rows.Select(r => (double)r.Message.StartColumn)]));
        columns.Add(new NumberColumn("ColumnEnd", [.. rows.Select(r => (double)r.Message.EndColumn)]));
        columns.Add(new JgsValueColumn("FullFilename", Strings(rows.Select(r => r.Path))));
        return JgsValue.Table(new Table(columns, n));
    }

    private static JgsValue Strings(IEnumerable<string> texts)
    {
        JgsValue[] elements = [.. texts.Select(JgsValue.Str)];
        return JgsValue.StringArray(elements, elements.Length, 1);
    }

    private static JgsValue Categories(IEnumerable<string> texts)
    {
        string[] values = [.. texts];
        string[] categories = [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        double[] codes = [.. values.Select(v => (double)(Array.IndexOf(categories, v) + 1))];
        return JgsCategorical.FromCodes(codes, values.Length, 1, new JgsCategoricalTag(categories, Ordinal: false, Protected: false));
    }

    /// <summary>The lines under a <c>codeIssues</c> display: "Issues table preview" and the table, when it has rows.</summary>
    internal static string CodeIssuesFooter(JgsObject instance)
    {
        if (!instance.Fields.TryGetValue("Issues", out JgsValue? issues) || issues.Type != JgsType.Table || issues.AsTable.RowCount == 0)
        {
            return string.Empty;
        }

        return "\n\n    Issues table preview\n\n" + IssuesTableText(issues.AsTable);
    }

    /// <summary>The issues table as text in R2025b's layout: the names, a rule under each, the rows, four spaces in.</summary>
    internal static string IssuesTableText(Table table)
    {
        int width = table.ColumnCount;
        int rows = table.RowCount;
        var cells = new string[rows][];
        var strings = new bool[width];
        var numbers = new bool[width];
        for (int j = 0; j < width; j++)
        {
            TableColumn column = table.Columns[j];
            strings[j] = column is JgsValueColumn { Value.IsStringArray: true };
            numbers[j] = column is NumberColumn;
        }

        for (int r = 0; r < rows; r++)
        {
            cells[r] = new string[width];
            for (int j = 0; j < width; j++)
            {
                TableColumn column = table.Columns[j];
                cells[r][j] = column switch
                {
                    NumberColumn numeric => numeric.Values[r].ToString(CultureInfo.InvariantCulture),
                    JgsValueColumn { Value.IsStringArray: true } quoted => "\"" + quoted.Value.AsArray[r].AsString + "\"",
                    JgsValueColumn held when held.Value.IsCategorical => JgsCategorical.Elements(held.Value)[r].Display(),
                    JgsValueColumn held => held.Value.AsArray[r].Display(),
                    _ => column.GetText(r),
                };
            }
        }

        int[] widths = new int[width];
        for (int j = 0; j < width; j++)
        {
            widths[j] = table.Columns[j].Name.Length;
            for (int r = 0; r < rows; r++)
            {
                widths[j] = Math.Max(widths[j], cells[r][j].Length);
            }
        }

        var text = new StringBuilder();
        string Cell(string value, int j) => numbers[j] ? value.PadLeft(widths[j]) : value.PadRight(widths[j]);
        text.Append("    ").Append(string.Join("    ", table.Columns.Select((c, j) => Centered(c.Name, widths[j])))).Append('\n');
        text.Append("    ").Append(string.Join("    ", widths.Select(static w => new string('_', w)))).Append("\n\n");
        for (int r = 0; r < rows; r++)
        {
            text.Append("    ").Append(string.Join("    ", cells[r].Select((v, j) => Cell(v, j))).TrimEnd()).Append('\n');
        }

        return text.ToString();
    }

    private static string Centered(string name, int width)
    {
        int pad = width - name.Length;
        int left = pad / 2;
        return new string(' ', left) + name + new string(' ', pad - left);
    }

    // --- fix ----------------------------------------------------------------------------------------------------

    /// <summary><c>[obj, status] = fix(obj, checkIDs, filenames)</c>: the fixable messages of those checks applied to the files and the files written back; the object built again over them.</summary>
    internal static JgsValue[] FixCodeIssues(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsObject issues = args[0].AsObject;
        if (args[1].Type == JgsType.Table)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:codeanalyzer:FixByTableNotAvailable", "fix by a table of issues is not available in JGraph; name the check IDs to fix.");
        }

        var ids = new HashSet<string>(NamesOf(args[1], line, col), StringComparer.Ordinal);
        List<string> files = issues.Fields.TryGetValue("Files", out JgsValue? held) && held.IsStringArray ? [.. held.AsArray.Select(static v => v.AsString)] : [];
        if (args.Count == 3)
        {
            var chosen = new HashSet<string>(NamesOf(args[2], line, col).Select(static n => Path.GetFullPath(n)), StringComparer.OrdinalIgnoreCase);
            var byName = new HashSet<string>(NamesOf(args[2], line, col).Select(static n => Path.GetFileName(n)), StringComparer.OrdinalIgnoreCase);
            files = [.. files.Where(f => chosen.Contains(f) || byName.Contains(Path.GetFileName(f)))];
        }

        var status = new List<(bool Success, string Error, string File, string Id, int Line, int C1, int C2, string ErrorId)>();
        foreach (string file in files)
        {
            string text;
            try
            {
                text = JgsMlapp.ReadSource(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }

            CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(text, file);
            List<CodeMessage> fixable = [.. analysis.Messages.Where(m => ids.Contains(m.Id) && m.Fix is not null)];
            if (fixable.Count == 0)
            {
                continue;
            }

            bool app = JgsMlapp.IsMlapp(file);
            string? error = app ? "A fix inside an .mlapp file is not written by JGraph; open the app in App Designer to fix it." : null;
            if (error is null)
            {
                try
                {
                    string fixedText = CodeFixer.Apply(text, fixable.Select(static m => m.Fix!));
                    File.WriteAllText(file, fixedText, new UTF8Encoding(false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = ex.Message;
                }
            }

            foreach (CodeMessage m in fixable)
            {
                status.Add((error is null, error ?? string.Empty, file, m.Id, m.Line, m.StartColumn, m.EndColumn, error is null ? string.Empty : "JGraph:codeIssues:fixNotWritten"));
            }
        }

        string configuration = issues.Fields.TryGetValue("CodeAnalyzerConfiguration", out JgsValue? c) && IsTextScalar(c) ? TextOf(c) : "active";
        List<string> all = issues.Fields.TryGetValue("Files", out JgsValue? allHeld) && allHeld.IsStringArray ? [.. allHeld.AsArray.Select(static v => v.AsString)] : files;
        JgsObject rebuilt = BuildCodeIssues(interpreter, all, configuration, line, col);
        JgsValue statusTable = status.Count == 0
            ? JgsValue.Table(new Table([], 0))
            : JgsValue.Table(new Table(
            [
                new JgsValueColumn("Success", JgsValue.Shaped([.. status.Select(s => JgsValue.Bool(s.Success))], status.Count, 1, JgsPackedKind.Number)),
                new JgsValueColumn("ErrorMessage", Strings(status.Select(s => s.Error))),
                new JgsValueColumn("FullFilename", Strings(status.Select(s => s.File))),
                new JgsValueColumn("CheckID", Categories(status.Select(s => s.Id))),
                new NumberColumn("LineStart", [.. status.Select(s => (double)s.Line)]),
                new NumberColumn("LineEnd", [.. status.Select(s => (double)s.Line)]),
                new NumberColumn("ColumnStart", [.. status.Select(s => (double)s.C1)]),
                new NumberColumn("ColumnEnd", [.. status.Select(s => (double)s.C2)]),
                new JgsValueColumn("ErrorID", Strings(status.Select(s => s.ErrorId))),
            ]));
        return wanted >= 2 ? [JgsValue.Object(rebuilt), statusTable] : [JgsValue.Object(rebuilt)];
    }
}
