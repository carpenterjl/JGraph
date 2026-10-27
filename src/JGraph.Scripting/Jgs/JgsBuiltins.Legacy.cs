using JGraph.Deploy;
using JGraph.Deploy.Projects;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The legacy deployment projects of R2024b and before as a session takes them (compiler and SDK
/// plan, stage K21; probe <c>k21_legacy</c>): <c>createDeploymentScript</c> writing the
/// <c>compiler.build</c> script a <c>&lt;deployment-project&gt;</c> stands for,
/// <c>compiler.internal.readPRJStruct</c> reading one as <c>readstruct</c> does, the
/// <c>-build</c> and <c>-package</c> roads of <c>deploytool</c> and the legacy apps
/// (<c>compiler.internal.build.legacyProject</c>, <c>compiler.internal.package.legacyProject</c>:
/// the script run in a workspace of its own, then "Build Complete" or "Packaging Complete"), the
/// upgrade into a MATLAB project with a task
/// (<c>matlab.internal.deployment.createProjectModelFromPRJ</c>: the road <c>deploytool prj</c>,
/// the legacy apps and <c>open</c> take, leaving the project open), and the
/// <c>compiler.internal.ui.get*DeploymentScript</c> wrappers over a project's task.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name this file declares, for the catalog's check.</summary>
    internal static IReadOnlyList<string> LegacyProjectBuiltinNames =>
    [
        "createDeploymentScript", "compiler.internal.readPRJStruct", "matlab.internal.deployment.readPRJStruct",
        "compiler.internal.build.legacyProject", "compiler.internal.package.legacyProject", "matlab.internal.deployment.createProjectModelFromPRJ",
        .. ScriptWrappers.Select(static w => "compiler.internal.ui." + w.Name),
    ];

    /// <summary>The script wrappers R2025b's apps call, each over one task type, with or without a package argument.</summary>
    private static readonly (string Name, string Type, bool TakesPackage)[] ScriptWrappers =
    [
        ("getStandaloneDeploymentScript", "StandaloneDesktopApp", true), ("getCSharedLibraryDeploymentScript", "CSharedLib", true),
        ("getCppSharedLibraryDeploymentScript", "CppSharedLib", true), ("getDotNETDeploymentScript", "DotNETAssembly", true),
        ("getPythonDeploymentScript", "PythonPackage", true), ("getExcelDeploymentScript", "ExcelAddIn", true),
        ("getPSADeploymentScript", "ProductionServerArchive", false), ("getWebAppDeploymentScript", "WebAppArchive", false),
        ("getJavaDeploymentScript", "JavaPackage", true), ("getCOMLibraryDeploymentScript", "COMComponent", true),
    ];

    private static void RegisterLegacyProjectBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true, BindsAnsAsStatement = false }));

        Define("createDeploymentScript", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Invalid argument list. Function requires 1 more input(s).");
            }

            if (args.Count > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            CreateDeploymentScript(interpreter, host, LegacyTextArgument(args[0]), LegacyTextArgument(args[1]), line, col);
            return JgsValue.Null;
        });

        foreach (string reader in (string[])["compiler.internal.readPRJStruct", "matlab.internal.deployment.readPRJStruct"])
        {
            Define(reader, (args, line, col) =>
            {
                Arity(reader, args, 1, line, col);
                if (!IsTextScalar(args[0]) || (args[0].IsCharMatrix && args[0].Rows > 1))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:fileparts:MustBeChar",
                        "Input must be a row vector of characters, or a string scalar, or a cellstr, or a string matrix.");
                }

                string given = TextOf(args[0]);
                string file = FromCurrentFolder(host, given);
                if (Path.GetExtension(file).Length == 0)
                {
                    file += ".prj";
                }

                string full = Path.GetFullPath(file);
                if (!File.Exists(full))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:textio:textio:FileNotFound",
                        $"Unable to find or open '{full}'. Check the path and filename or file permissions.");
                }

                LegacyDeploymentProject? project;
                try
                {
                    project = LegacyDeploymentProject.ReadStruct(full);
                }
                catch (System.Xml.XmlException)
                {
                    project = null;
                }

                if (project is null)
                {
                    if (LooksLikeXml(full))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:nonExistentField", "Unrecognized field name \"configuration\".");
                    }

                    throw new JgsRuntimeException(line, col, "MATLAB:io:xml:common:InvalidXMLFile", $"Invalid XML file: {full}.");
                }

                return StructValue(project.Resolved);
            });
        }

        env.Builtins.Register("compiler.internal.build.legacyProject", JgsValue.Function(new BuiltinFunction("compiler.internal.build.legacyProject",
            (args, line, col) => LegacyBuild(env, interpreter, host, args, package: false, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                JgsValue? result = LegacyBuild(env, interpreter, host, args, package: false, line, col);
                return wanted == 0 || result is null ? [] : [result];
            },
        }));

        Define("compiler.internal.package.legacyProject", (args, line, col) =>
        {
            LegacyBuild(env, interpreter, host, args, package: true, line, col);
            return JgsValue.Null;
        });

        env.Builtins.Register("matlab.internal.deployment.createProjectModelFromPRJ", JgsValue.Function(new BuiltinFunction("matlab.internal.deployment.createProjectModelFromPRJ",
            (args, line, col) => UpgradeLegacyProject(env, interpreter, host, args, line, col)[0])
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 3)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                JgsValue[] outputs = UpgradeLegacyProject(env, interpreter, host, args, line, col);
                return outputs[..Math.Max(wanted, 1)];
            },
        }));

        // open('x.prj'): R2025b's openprj opens a MATLAB project, upgrades a legacy deployment project
        // into one (the task's app follows in a session with windows), and edits any other file.
        if (env.Builtins.TryGet("open", out JgsValue original) && original.Type == JgsType.Function)
        {
            BuiltinFunction? was = original.AsCallable as BuiltinFunction;
            env.Builtins.Register("open", JgsValue.Function(new BuiltinFunction("open", (args, line, col) =>
            {
                if (args.Count == 1 && IsTextScalar(args[0]) && !(args[0].IsCharMatrix && args[0].Rows > 1)
                    && Path.GetExtension(TextOf(args[0])).Equals(".prj", StringComparison.OrdinalIgnoreCase))
                {
                    string given = TextOf(args[0]);
                    string file = FromCurrentFolder(host, given);
                    if (!File.Exists(file))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:open:fileNotFound", $"File '{given}' not found.");
                    }

                    if (ProjectDefinitionFiles.IsProjectFile(file))
                    {
                        OpenProject(env, interpreter, host, file, line, col);
                        return JgsValue.Null;
                    }

                    if (LegacyDeploymentProject.IsLegacyProjectFile(file) && LegacyDeploymentProject.ReadStruct(file) is { } legacy)
                    {
                        OpenLegacyProject(env, interpreter, host, "open", legacy, line, col);
                        return JgsValue.Null;
                    }

                    throw new JgsRuntimeException(line, col, "JGraph:open:NotAProject",
                        $"'{given}' is neither a MATLAB project nor a deployment project; R2025b opens such a file in the editor, which a script has not.");
                }

                return original.AsCallable.Call(args, line, col);
            })
            {
                AutoCallsBare = was?.AutoCallsBare ?? true,
                BindsAnsAsStatement = was?.BindsAnsAsStatement ?? true,
                KeepsStringArguments = was?.KeepsStringArguments ?? false,
            }));
        }

        foreach ((string name, string type, bool takesPackage) in ScriptWrappers)
        {
            string full = "compiler.internal.ui." + name;
            Define(full, (args, line, col) =>
            {
                if (args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                if (args.Count > (takesPackage ? 3 : 2))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                foreach (int n in (int[])[0, 1])
                {
                    if (!IsTextScalar(args[n]) || (args[n].IsCharMatrix && args[n].Rows > 1))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar",
                            $"Invalid argument at position {n + 1}. Value must be a character vector or string scalar.");
                    }
                }

                bool package = !takesPackage || args.Count < 3 || args[2].IsTruthy;
                string root = host.Resolve(TextOf(args[0]));
                if (!Directory.Exists(root) && !File.Exists(root))
                {
                    throw new JgsRuntimeException(line, col, "compiler_ui_common:deploymentscript:notAProjectPath",
                        $"\"{TextOf(args[0])}\" is not a valid MATLAB Project path nor a path to a valid Project root folder.");
                }

                ProjectObject owner = OpenProject(env, interpreter, host, root, line, col);
                MatlabProject project = host.Projects.Current!;
                CompilerTaskKind kind = CompilerTaskKinds.ByType(type)!;
                if (kind.Target is null)
                {
                    throw new JgsRuntimeException(line, col, "JGraph:compilerApps:targetOutOfScope",
                        $"{full} writes the script of a {kind.CategoryTitle} task, a target JGraph's compiler does not build (decisions KD1 and KD2).");
                }

                CompilerTaskConfiguration task;
                try
                {
                    task = project.GetOrCreateTask(type, TextOf(args[1]));
                }
                catch (ProjectException ex)
                {
                    throw ProjectDisplay.Refusal(ex, line, col);
                }

                _ = owner;
                CompilerTaskModel model = task.ToTaskModel()!;
                if (model.SourceFiles.Count == 0)
                {
                    throw new JgsRuntimeException(line, col, "compiler_ui_common:deploymentscript:missingCriticalFile",
                        "Unable to generate deployment script for target. Target is missing the Application File or Exported Function(s).");
                }

                return JgsValue.Str(DeploymentScript.Generate(model, package).Replace("\n", "\r\n", StringComparison.Ordinal));
            });
        }
    }

    /// <summary>
    /// A file argument as R2025b's legacy functions read it: text, a one-element cell of text, a
    /// number as the characters of its codes (R2025b's <c>char</c> of it), else "".
    /// </summary>
    private static string LegacyTextArgument(JgsValue value)
    {
        if (IsTextScalar(value) && !(value.IsCharMatrix && value.Rows > 1))
        {
            return TextOf(value);
        }

        if (value.Type == JgsType.Cell && value.ArrayLength == 1 && value.ElementAt(0) is { } element && IsTextScalar(element))
        {
            return TextOf(element);
        }

        if (value.Type == JgsType.Number)
        {
            return new string((char)Math.Max(0, (int)value.AsNumber), 1);
        }

        if (value.Type == JgsType.Array && !value.IsStringArray && value.ArrayLength >= 1 && value.ElementAt(0).Type == JgsType.Number)
        {
            return new string(Enumerable.Range(0, value.ArrayLength).Select(i => (char)Math.Max(0, (int)value.ElementAt(i).AsNumber)).ToArray());
        }

        return "";
    }

    /// <summary>A path against the session's current folder: the folder a script's <c>cd</c> set, whether or not the file exists yet.</summary>
    private static string FromCurrentFolder(JGraphScriptGlobals host, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(host.CurrentDirectory, path);

    /// <summary>Whether a bare file name is on the search path and not in the current folder (R2025b's <c>exist</c> finds it, its reader does not).</summary>
    private static bool OnPathAlone(Interpreter interpreter, JGraphScriptGlobals host, string given) =>
        !File.Exists(FromCurrentFolder(host, given)) && !Path.IsPathRooted(given) && given.IndexOfAny(['/', '\\']) < 0
        && interpreter.FunctionPath is { } path && path.Folders.Any(folder => File.Exists(Path.Combine(folder, given)));

    private static bool LooksLikeXml(string file)
    {
        try
        {
            using var reader = new StreamReader(file);
            char[] head = new char[256];
            int read = reader.Read(head, 0, head.Length);
            return new string(head, 0, read).TrimStart().StartsWith('<');
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a legacy project as its builders do, with R2025b's refusals (<c>projectDoesNotExist</c>,
    /// <c>notCompatibilityPRJ</c>, <c>hadoopNotSupported</c>) in the file's given spelling. A relative
    /// name is the current folder's; one found on the path alone reads as R2025b's does: as not a project.
    /// </summary>
    private static LegacyDeploymentProject ReadLegacyProject(Interpreter interpreter, JGraphScriptGlobals host, string given, int line, int col)
    {
        string resolved = FromCurrentFolder(host, given);
        try
        {
            if (OnPathAlone(interpreter, host, given))
            {
                throw new LegacyProjectException("Compiler:build:compatibility:notCompatibilityPRJ",
                    $"Input project file {given} is not a valid MATLAB Compiler or MATLAB Compiler SDK project.");
            }

            return LegacyDeploymentProject.Read(resolved, given);
        }
        catch (LegacyProjectException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }
    }

    /// <summary>The settings of a legacy project, its checks run, as a refusal in R2025b's words.</summary>
    private static LegacyTaskSettings LegacySettings(LegacyDeploymentProject project, int line, int col)
    {
        LegacyTaskSettings settings = LegacyTaskSettings.From(project)
            ?? throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:notCompatibilityPRJ",
                $"Input project file {project.File} is not a valid MATLAB Compiler or MATLAB Compiler SDK project.");
        try
        {
            settings.Check();
        }
        catch (LegacyProjectException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }

        return settings;
    }

    /// <summary>
    /// <c>createDeploymentScript(prj, out)</c> in R2025b's order: the project must exist, the
    /// output's name must be a variable name and its extension <c>.m</c> (or none, when <c>.m</c>
    /// is added), the output must not exist, the project must read and check, then the script is
    /// written, with the warnings on the way.
    /// </summary>
    private static void CreateDeploymentScript(Interpreter interpreter, JGraphScriptGlobals host, string prj, string output, int line, int col)
    {
        string resolvedPrj = FromCurrentFolder(host, prj);
        if (!File.Exists(resolvedPrj) && !Directory.Exists(resolvedPrj) && !OnPathAlone(interpreter, host, prj))
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:projectDoesNotExist", $"Input project file {prj} does not exist.");
        }

        string name = Path.GetFileNameWithoutExtension(output);
        if (!IsValidVariableName(name))
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:invalidOutputFileName", $"The output file {name} must be a valid MATLAB variable name.");
        }

        string extension = Path.GetExtension(output);
        if (extension.Length == 0)
        {
            output += ".m";
        }
        else if (!extension.Equals(".m", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:incompatibleScriptExtension", $"The output file {output} must use the \".m\" extension.");
        }

        string target = FromCurrentFolder(host, output);
        if (File.Exists(target))
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:scriptFileCannotExist",
                $"Output file {target} exists.  MATLAB will not overwrite an existing file when creating the deployment script.");
        }

        LegacyDeploymentProject project = ReadLegacyProject(interpreter, host, prj, line, col);
        LegacyTaskSettings settings = LegacySettings(project, line, col);
        string text = LegacyDeploymentScript.Generate(settings, out IReadOnlyList<(string Id, string Message)> warnings);
        foreach ((string id, string message) in warnings)
        {
            Warn(host, id, message);
        }

        try
        {
            File.WriteAllText(target, text.Replace("\n", "\r\n", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:failedToWriteScript",
                $"Error writing to output file {target}.  Verify you have write permissions to the file.");
        }
    }

    /// <summary>
    /// <c>deploytool -build</c> and <c>-package</c> over a legacy project: the script generated for
    /// it runs in a workspace of its own, "Build Complete" (and "Packaging Complete") follows, and
    /// the build's results object is answered. A kind JGraph does not build is refused before anything runs.
    /// </summary>
    private static JgsValue? LegacyBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, bool package, int line, int col)
    {
        string verb = package ? "compiler.internal.package.legacyProject" : "compiler.internal.build.legacyProject";
        Arity(verb, args, 1, line, col);
        string prj = LegacyTextArgument(args[0]);
        LegacyDeploymentProject project = ReadLegacyProject(interpreter, host, prj, line, col);
        LegacyTaskSettings settings = LegacySettings(project, line, col);
        if (settings.JGraphTarget is null)
        {
            throw new JgsRuntimeException(line, col, "JGraph:compilerApps:targetOutOfScope",
                $"{prj} is a {settings.Project.TargetName} project for a target JGraph's compiler does not build ({settings.Target}; decisions KD1 and KD2). "
                + "JGraph builds standalone applications, Production Server archives, .NET assemblies, Python packages, C++ and C shared libraries and Excel add-ins.");
        }

        string script = LegacyDeploymentScript.Generate(settings, out IReadOnlyList<(string Id, string Message)> warnings);
        foreach ((string id, string message) in warnings)
        {
            Warn(host, id, message);
        }

        string buildPart = script;
        string? packagePart = null;
        int cut = script.IndexOf("\n\n", StringComparison.Ordinal);
        if (cut >= 0)
        {
            buildPart = script[..cut];
            packagePart = script[(cut + 2)..];
        }

        var scope = new JgsEnvironment(env);
        try
        {
            interpreter.EvaluateSource(buildPart, scope, line, col, asStatement: true);
        }
        finally
        {
            // The help text's file is held open by the script's onCleanup objects until they go; a function's workspace ends here.
            if (scope.TryGet("ensureFileCloseObject", out _))
            {
                interpreter.EvaluateSource("clear ensureFileCloseObject ensureFileDeleteObject", scope, line, col, asStatement: true);
            }
        }

        host.print("Build Complete");
        JgsValue result = scope.TryGet("buildResult", out JgsValue built) ? built : JgsValue.Null;
        if (package)
        {
            if (packagePart is { Length: > 0 } && settings.HasInstaller)
            {
                interpreter.EvaluateSource(packagePart, scope, line, col, asStatement: true);
            }
            else if (settings.Target == LegacyTarget.ProductionServerArchive && settings.PackageFolder.Length > 0)
            {
                // R2025b copies an archive to its redistribution folder.
                string archive = Path.Combine(settings.OutputFolder, settings.AppName + ".ctf");
                if (File.Exists(archive))
                {
                    Directory.CreateDirectory(settings.PackageFolder);
                    File.Copy(archive, Path.Combine(settings.PackageFolder, Path.GetFileName(archive)), overwrite: true);
                }
            }

            host.print("Packaging Complete");
        }

        return result;
    }

    /// <summary>
    /// <c>matlab.internal.deployment.createProjectModelFromPRJ(prj)</c>: the upgrade, the project
    /// left open, the task's configuration handle, the file warnings and the unsupported settings
    /// as string arrays. A file that is not a legacy project (missing, text, a MATLAB project, a
    /// Hadoop project) upgrades into nothing: an empty double and empty strings.
    /// </summary>
    private static JgsValue[] UpgradeLegacyProject(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("matlab.internal.deployment.createProjectModelFromPRJ", args, 1, line, col);
        string file = FromCurrentFolder(host, LegacyTextArgument(args[0]));
        JgsValue[] nothing = [JgsValue.Array([]), JgsValue.StringArray([], 0, 0), JgsValue.StringArray([], 0, 0)];
        if (!File.Exists(file) || !LegacyDeploymentProject.IsLegacyProjectFile(file))
        {
            return nothing;
        }

        LegacyDeploymentProject? project = LegacyDeploymentProject.ReadStruct(file);
        bool compilerProject = project?.Target is LegacyDeploymentProject.StandaloneTarget or LegacyDeploymentProject.LibraryTarget
            or LegacyDeploymentProject.ProductionServerTarget or LegacyDeploymentProject.WebAppTarget;
        if (project is null || !compilerProject || project.SubTarget == "subtarget.hadoop")
        {
            return nothing; // a Hadoop project, or no Compiler project at all, upgrades into nothing (a library one with an unknown sub-target into a project without a task)
        }

        if (!project.Params.ContainsKey("param.user.defined.mcr.options"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nonExistentField", "Unrecognized field name \"param_user_defined_mcr_options\".");
        }

        LegacyProjectUpgradeResult upgraded = UpgradeAndOpen(env, interpreter, host, project, line, col, out TaskConfigurationObject? handle);
        JgsValue config = handle is not null ? JgsValue.External(handle) : JgsValue.Array([]);
        return [config, Strings(upgraded.FileWarnings), Strings(upgraded.UnsupportedWarnings)];

        static JgsValue Strings(IReadOnlyList<string> items) => items.Count == 0 ? JgsValue.StringArray([], 0, 0) : ProjectDisplay.StringRow(items);
    }

    /// <summary>
    /// The upgrade and what follows it in R2025b: the project opened as the current one, and the
    /// task's handle made for the caller (the apps open their window over it).
    /// </summary>
    internal static LegacyProjectUpgradeResult UpgradeAndOpen(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, LegacyDeploymentProject project, int line, int col, out TaskConfigurationObject? handle)
    {
        if (!project.Params.ContainsKey("param.user.defined.mcr.options"))
        {
            // R2025b's upgrade reads this element unguarded; a file without it (a stub) fails there in its words.
            throw new JgsRuntimeException(line, col, "MATLAB:nonExistentField", "Unrecognized field name \"param_user_defined_mcr_options\".");
        }

        if (host.Projects.Current is not null)
        {
            CloseProject(env, interpreter, host, line, col);
        }

        LegacyProjectUpgradeResult upgraded;
        try
        {
            upgraded = LegacyProjectUpgrade.Upgrade(project) ?? throw new JgsRuntimeException(line, col, "Compiler:build:compatibility:notCompatibilityPRJ",
                $"Input project file {project.File} is not a valid MATLAB Compiler or MATLAB Compiler SDK project.");
        }
        catch (ProjectException ex)
        {
            throw ProjectDisplay.Refusal(ex, line, col);
        }

        ProjectObject owner = OpenProject(env, interpreter, host, upgraded.Project.RootFolder, line, col);
        MatlabProject live = host.Projects.Current!;
        CompilerTaskConfiguration? task = upgraded.Task is { } made ? live.FindTask(made.Name) : null;
        handle = task is not null ? new TaskConfigurationObject(host.Devices, interpreter, owner, task) : null;
        return upgraded;
    }

    /// <summary>A readstruct-shaped value as a struct: strings scalar, string rows, structs and struct rows.</summary>
    private static JgsValue StructValue(LegacyXmlStruct xml) => JgsValue.Struct(StructFields(xml));

    private static Dictionary<string, JgsValue> StructFields(LegacyXmlStruct xml)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string name, object value) in xml.Fields)
        {
            fields[name] = value switch
            {
                string s => JgsValue.StringScalar(s),
                string[] a => ProjectDisplay.StringRow(a),
                LegacyXmlStruct s => StructValue(s),
                LegacyXmlStruct[] a => JgsValue.StructArray(a.Select(StructFields).ToArray()),
                _ => JgsValue.Null,
            };
        }

        return fields;
    }
}
