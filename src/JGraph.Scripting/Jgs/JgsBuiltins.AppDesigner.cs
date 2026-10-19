namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>appdesigner</c> and <c>appdesigner(filename)</c> (designer plan DS2, decision DD3): bring up the
/// visual designer's window, with the app opened in a tab of its own. The file is found and checked
/// as R2025b's <c>appdesigner.internal.application.getValidatedInputAppFileName</c> does it, in its
/// words (probe <c>ds2_appdesigner</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>R2025b's <c>namelengthmax</c>, as its file-name refusal spells it.</summary>
    private const int AppNameLengthMax = 2048;

    private static void RegisterAppDesignerBuiltin(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        // R2025b's appdesigner takes a file name or nothing and answers nothing.
        env.Builtins.Register("appdesigner", JgsValue.Function(new BuiltinFunction("appdesigner",
            (args, line, col) => FirstOf(AppDesigner(interpreter, host, args, line, col)))
        {
            KeepsStringArguments = true,
            MultiOutput = (args, _, line, col) => AppDesigner(interpreter, host, args, line, col),
        }));
    }

    private static JgsValue[] AppDesigner(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        // R2025b asks first for the web window App Designer is drawn in; here the window is the app's.
        if (host.AppDesigner is not { } designer)
        {
            throw new JgsRuntimeException(line, col, "JGraph:appdesigner:NoWindow",
                "appdesigner opens App Designer's window in the JGraph app, and this session has no window. "
                + "A batch run can run an app (call it by its name) but not design one.");
        }

        string? path = args.Count == 1 ? ValidatedAppFile(interpreter, host, args[0], line, col) : null;
        if (designer.Open(path) is { } refused)
        {
            throw new JgsRuntimeException(line, col, "JGraph:appdesigner:CannotOpen", refused);
        }

        return [];
    }

    /// <summary>
    /// The full path of the app <paramref name="value"/> names: a char row or a string scalar, its
    /// name a valid one, <c>.mlapp</c> added when it has no extension and exactly that extension
    /// otherwise, found in the current folder or on the path, and spelled as the file is on disk.
    /// </summary>
    private static string ValidatedAppFile(Interpreter interpreter, JGraphScriptGlobals host, JgsValue value, int line, int col)
    {
        if (!IsTextScalar(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:InvalidInput",
                "The input argument must be a full or partial file path, or a file name.");
        }

        string typed = TextOf(value);
        string name = Path.GetFileNameWithoutExtension(typed);
        string extension = Path.GetExtension(typed);
        if (Array.IndexOf(MatlabKeywordList, name) >= 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:FileNameFailsIsKeyword",
                "Name cannot be a MATLAB keyword.  Rename file, and then open.");
        }

        if (name.Length == 0 || name.Length > AppNameLengthMax || !char.IsAsciiLetter(name[0])
            || !name.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:FileNameFailsIsVarName",
                $"File name '{name}' must start with a letter, contain only letters, numbers and underscores, and be shorter than {AppNameLengthMax} characters.");
        }

        string candidate = extension.Length == 0 ? typed + ".mlapp" : typed;
        if (extension.Length > 0 && extension != ".mlapp")
        {
            bool caseOnly = extension.Equals(".mlapp", StringComparison.OrdinalIgnoreCase);
            throw new JgsRuntimeException(line, col,
                caseOnly ? "MATLAB:appdesigner:appdesigner:InvalidFileExtensionCase" : "MATLAB:appdesigner:appdesigner:InvalidFileExtension",
                $"'{candidate}' file type must be a MATLAB App (*.mlapp)." + (caseOnly ? "  The extension is case sensitive." : string.Empty));
        }

        string resolved = host.Resolve(candidate);
        string? found = File.Exists(resolved) ? Path.GetFullPath(resolved)
            : !Path.IsPathRooted(candidate) && interpreter.FunctionPath?.FindFile(candidate) is { } onPath ? onPath
            : null;
        if (found is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:InvalidFileName", $"File '{candidate}' not found.");
        }

        // The name as the folder spells it: 'borrowcopy.mlapp' opens BorrowCopy.mlapp.
        string folder = Path.GetDirectoryName(found)!;
        string? spelled = Directory.EnumerateFiles(folder, "*.mlapp")
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(found), StringComparison.OrdinalIgnoreCase));
        return spelled ?? found;
    }
}
