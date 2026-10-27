using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The commands that open the compiler apps (compiler and SDK plan, stage K19), as R2025b's open
/// theirs (probe <c>k19_apps</c>): the target-specific apps of R2025a and later
/// (<c>standaloneApplicationCompiler</c>, <c>productionServerArchiveCompiler</c>,
/// <c>dotNetAssemblyCompiler</c>, <c>pythonPackageCompiler</c>, <c>cppSharedLibraryCompiler</c>,
/// <c>cSharedLibraryCompiler</c>, <c>excelAddinCompiler</c>), the legacy ones with their warnings
/// (<c>applicationCompiler</c>, <c>libraryCompiler</c>, <c>productionServerCompiler</c>,
/// <c>deploytool</c>, <c>webAppCompiler</c>) and the removed <c>hadoopCompiler</c>. Each hands the
/// host's <see cref="IScriptCompilerApps"/> the target and the file it was given; a session without
/// windows refuses. R2025b opens a project file in the app and any other file in the editor; JGraph
/// has no projects yet (stage K20), so a MATLAB file opens the app with it as the main file, and a
/// project is refused.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The target-specific apps that take a file, by their command.</summary>
    private static readonly Dictionary<string, CompilerTarget> FileTakingApps = new(StringComparer.Ordinal)
    {
        ["standaloneApplicationCompiler"] = CompilerTarget.StandaloneApplication,
        ["productionServerArchiveCompiler"] = CompilerTarget.ProductionServerArchive,
    };

    /// <summary>The target-specific apps that take nothing, by their command.</summary>
    private static readonly Dictionary<string, CompilerTarget> PlainApps = new(StringComparer.Ordinal)
    {
        ["excelAddinCompiler"] = CompilerTarget.ExcelAddIn,
        ["dotNetAssemblyCompiler"] = CompilerTarget.DotNetAssembly,
        ["pythonPackageCompiler"] = CompilerTarget.PythonPackage,
        ["cppSharedLibraryCompiler"] = CompilerTarget.CppSharedLibrary,
        ["cSharedLibraryCompiler"] = CompilerTarget.CSharedLibrary,
    };

    /// <summary>R2025b's apps for targets outside JGraph's scope (decisions KD1 and KD2), by their command.</summary>
    private static readonly Dictionary<string, string> OutOfScopeApps = new(StringComparer.Ordinal)
    {
        ["javaPackageCompiler"] = "Java packages are out of JGraph's scope (decision KD1)",
        ["comComponentCompiler"] = "COM components are out of JGraph's scope (decision KD2)",
        ["webAppCompiler"] = "web apps are out of JGraph's scope (decision KD2)",
    };

    /// <summary>The legacy apps (<c>toolbox/compiler/java</c>), and the target each opens; null for the gallery.</summary>
    private static readonly (string Name, CompilerTarget? Target, string? Successor)[] LegacyApps =
    [
        ("applicationCompiler", CompilerTarget.StandaloneApplication, "standaloneApplicationCompiler"),
        ("productionServerCompiler", CompilerTarget.ProductionServerArchive, "productionServerArchiveCompiler"),
        ("libraryCompiler", null, "deploytool"),
        ("deploytool", null, null),
        ("webAppCompiler", null, null),
    ];

    /// <summary>Every command this file declares, for the catalog and the compiler's name list (a fresh list: a static field here could initialize after the name list that reads it).</summary>
    internal static IReadOnlyList<string> CompilerAppNames =>
    [
        "standaloneApplicationCompiler", "productionServerArchiveCompiler", "excelAddinCompiler", "dotNetAssemblyCompiler",
        "pythonPackageCompiler", "cppSharedLibraryCompiler", "cSharedLibraryCompiler", "javaPackageCompiler", "comComponentCompiler",
        "applicationCompiler", "libraryCompiler", "productionServerCompiler", "deploytool", "webAppCompiler", "hadoopCompiler",
    ];

    private static void RegisterCompilerAppBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        foreach ((string name, CompilerTarget target) in FileTakingApps)
        {
            Register(env, name, (args, line, col) =>
            {
                // R2025b: function f(prjFile); an arguments-style check of the one input.
                if (args.Count > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                string? file = args.Count == 1 ? AppFileArgument(interpreter, host, args[0], line, col) : null;
                OpenCompilerApp(env, interpreter, host, name, target, file, line, col);
            });
        }

        foreach ((string name, CompilerTarget target) in PlainApps)
        {
            Register(env, name, (args, line, col) =>
            {
                if (args.Count > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                OpenCompilerApp(env, interpreter, host, name, target, null, line, col);
            });
        }

        foreach ((string name, string reason) in OutOfScopeApps)
        {
            if (name == "webAppCompiler")
            {
                continue; // a legacy app, declared below with its forms
            }

            Register(env, name, (args, line, col) =>
            {
                if (args.Count > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                throw OutOfScope(name, reason, line, col);
            });
        }

        foreach ((string name, CompilerTarget? target, string? successor) in LegacyApps)
        {
            Register(env, name, (args, line, col) => LegacyCompilerApp(env, interpreter, host, name, target, successor, args, line, col));
        }

        Register(env, "hadoopCompiler", (_, line, col) => throw new JgsRuntimeException(line, col, "Compiler:deploytool:HadoopCompilerRemoved",
            "hadoopCompiler has been removed.  To create standalone MATLAB MapReduce applications, or deployable archives from MATLAB map and reduce functions, use the mcc command."));
    }

    private static void Register(JgsEnvironment env, string name, Action<IReadOnlyList<JgsValue>, int, int> body)
    {
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
        {
            body(args, line, col);
            return JgsValue.Null;
        })
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
        }));
    }

    /// <summary>
    /// A legacy app's call as <c>launchui</c> takes it: two inputs at most, the warning that it is going
    /// away (or that <c>-build</c> and <c>-package</c> are), each input scalar text, then the app for
    /// none, the help for <c>-?</c>, a project for one input, a build or a package of one for two.
    /// </summary>
    private static void LegacyCompilerApp(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, string name, CompilerTarget? target, string? successor,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        // compiler.internal.displayDeploymentUIWarning, before the inputs are checked.
        if (args.Count < 2)
        {
            if (successor is not null)
            {
                Warn(host, "Compiler:deploytool:JavaAPIToBeRemoved",
                    $"{name} is scheduled for removal in a future release. To package MATLAB code using the MATLAB Compiler apps, use {successor} instead.");
            }
        }
        else if (IsTextScalar(args[0]) && !args[0].IsCharMatrix && IsBuildOrPackageSwitch(TextOf(args[0])))
        {
            if (name is "webAppCompiler" or "deploytool")
            {
                Warn(host, "Compiler:deploytool:DeploytoolBuildPackageToBeRemoved",
                    $"{name} will not accept -build or -package in a future release. Use mcc, deployment scenario specific functions such as compiler.build.standaloneApplication, and compiler.package.installer instead.");
            }
            else
            {
                Warn(host, "Compiler:deploytool:DeploytoolFunctionalityToBeRemoved",
                    $"{name} is scheduled for removal in a future release. To build and package MATLAB code programmatically, use the mcc command or target-specific functions such as compiler.build.standaloneApplication and compiler.package.installer.");
            }
        }

        // launchui's validateattributes: each input char or string, and scalar text.
        var words = new string[args.Count];
        for (int n = 0; n < args.Count; n++)
        {
            JgsValue arg = args[n];
            // R2025b's validateattributes answers expectedScalartext for a cell before the class check (recorded).
            if (arg.Type != JgsType.Cell && !(arg.Type == JgsType.String || arg.IsCharMatrix || arg.IsStringArray))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidType",
                    $"Expected input number {n + 1} to be one of these types:\n\nchar, string\n\nInstead its type was {ClassOf(arg, JgsDialect.Matlab)}.");
            }

            if (!IsTextScalar(arg) || (arg.IsCharMatrix && arg.Rows > 1) || IsMissingString(arg))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:expectedScalartext",
                    $"Expected input number {n + 1} to be a non-missing string scalar or character vector.");
            }

            words[n] = TextOf(arg);
        }

        switch (words.Length)
        {
            case 0:
                if (OutOfScopeApps.TryGetValue(name, out string? reason))
                {
                    throw OutOfScope(name, reason, line, col);
                }

                OpenCompilerApp(env, interpreter, host, name, target, null, line, col);
                return;
            case 1:
                if (words[0] is "-?" or "/?")
                {
                    PrintHelp(host, name);
                    return;
                }

                // isValidProject: the file exists and readPRJStruct reads it. JGraph reads no legacy
                // project until stage K21, so every file is R2025b's refusal of one that is not a project.
                throw new JgsRuntimeException(line, col, "Compiler:deploytool:invalidFirstInputOneInput",
                    $"{words[0]} is an invalid input argument. When using {name} with one input argument, the input must be a valid MATLAB Compiler or MATLAB Compiler SDK Project (.prj) file.\n");
            default:
                if (IsBuildOrPackageSwitch(words[0]))
                {
                    throw LegacyProjectRefusal(host, words[1], line, col);
                }

                throw new JgsRuntimeException(line, col, "Compiler:deploytool:invalidFirstInputTwoInputs",
                    $"{words[0]} is an invalid input argument.  When using {name} with two input arguments, the first input must be either \"-build\" or \"-package\" and the second input must be a valid MATLAB Compiler or MATLAB Compiler SDK Project (.prj) file.\n");
        }
    }

    private static bool IsBuildOrPackageSwitch(string word) => word is "-build" or "/build" or "-package" or "/package";

    /// <summary>
    /// What <c>compiler.internal.build.legacyProject</c> answers for <paramref name="file"/>: a missing
    /// file, or one that is not a legacy project (which none is to JGraph until stage K21 reads them).
    /// </summary>
    internal static JgsRuntimeException LegacyProjectRefusal(JGraphScriptGlobals host, string file, int line, int col)
    {
        string resolved = host.Resolve(file);
        return File.Exists(resolved)
            ? new JgsRuntimeException(line, col, "Compiler:build:compatibility:notCompatibilityPRJ",
                $"Input project file {file} is not a valid MATLAB Compiler or MATLAB Compiler SDK project.")
            : new JgsRuntimeException(line, col, "Compiler:build:compatibility:projectDoesNotExist",
                $"Input project file {file} does not exist.");
    }

    /// <summary>R2025b prints the function's help block for <c>-?</c>; here it is the catalog's entry, as <c>help</c> prints it.</summary>
    private static void PrintHelp(JGraphScriptGlobals host, string name)
    {
        if (JgsBuiltinCatalog.Find(name) is { } info)
        {
            host.print(info.Signature);
            host.print("  " + info.Summary);
        }
    }

    private static JgsRuntimeException OutOfScope(string name, string reason, int line, int col) =>
        new(line, col, "JGraph:compilerApps:targetOutOfScope",
            $"{name} opens an app for a target JGraph's compiler does not build: {reason}. The apps JGraph has are "
            + string.Join(", ", CompilerTargets.All.Select(CompilerTargets.Command)) + ".");

    /// <summary>
    /// The file a target-specific app is given, as its <c>arguments</c> check and <c>open</c> take it:
    /// scalar text with characters, a file that exists in the current folder or on the path (R2025b's
    /// <c>open</c>: "File 'x' not found."), spelled as the folder spells it.
    /// </summary>
    private static string AppFileArgument(Interpreter interpreter, JGraphScriptGlobals host, JgsValue value, int line, int col)
    {
        if (!IsTextScalar(value) || (value.IsCharMatrix && value.Rows > 1) || IsMissingString(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar",
                "Invalid argument at position 1. Value must be a character vector or string scalar.");
        }

        string typed = TextOf(value);
        if (typed.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                "Invalid argument at position 1. Value must be text with one or more characters.");
        }

        string resolved = host.Resolve(typed);
        string? found = File.Exists(resolved) ? Path.GetFullPath(resolved)
            : !Path.IsPathRooted(typed) && typed.IndexOfAny(['/', '\\']) < 0 && interpreter.FunctionPath?.FindFile(typed) is { } onPath ? onPath
            : null;
        if (found is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:open:fileNotFound", $"File '{typed}' not found.");
        }

        string folder = Path.GetDirectoryName(found)!;
        string? spelled = Directory.EnumerateFiles(folder, "*" + Path.GetExtension(found))
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(found), StringComparison.OrdinalIgnoreCase));
        return spelled ?? found;
    }

    /// <summary>
    /// Brings up the app for <paramref name="target"/> (the gallery for null) through the host, with
    /// <paramref name="file"/> as its main file when it is a MATLAB file; a project file is refused
    /// until JGraph has projects (stage K20), any other file as not one the app takes.
    /// </summary>
    private static void OpenCompilerApp(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, string name, CompilerTarget? target, string? file, int line, int col)
    {
        if (file is not null)
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(".prj", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "JGraph:compilerApps:NoProject",
                    $"'{file}' is a project, and JGraph has no MATLAB projects yet (compiler and SDK plan, stage K20): {name} opens its app on a "
                    + "MATLAB file given as the main file, or with nothing, and Create Build Script keeps the task as a script.");
            }

            if (!AppFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "JGraph:compilerApps:NotAMatlabFile",
                    $"'{file}' is not a MATLAB file: {name} takes a main file (.m, .mlapp, .mlx or .p) or a project (.prj).");
            }
        }

        if (host.CompilerApps is not { } apps)
        {
            string title = target is { } t ? "the " + CompilerTargets.AppTitle(t) + " window" : "the compiler apps' gallery";
            throw new JgsRuntimeException(line, col, "JGraph:compilerApps:NoWindow",
                $"{name} opens {title} in the JGraph app, and this session has no window. A batch run builds with mcc or the compiler.build functions instead.");
        }

        var services = new CompilerAppServices
        {
            Analyze = roots => AnalyzeDependencies(env, interpreter, host, roots, topOnly: false),
            CurrentFolder = host.CurrentDirectory,
        };
        if (apps.Open(target, file, services) is { } refused)
        {
            throw new JgsRuntimeException(line, col, "JGraph:compilerApps:CannotOpen", refused);
        }
    }
}
