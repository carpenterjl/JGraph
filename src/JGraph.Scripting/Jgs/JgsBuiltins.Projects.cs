using JGraph.Deploy;
using JGraph.Deploy.Projects;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB Projects as a session has them (compiler and SDK plan, stage K20): <c>matlab.project.createProject</c>,
/// <c>openProject</c>, <c>currentProject</c>, <c>matlab.project.loadProject</c>, <c>rootProject</c>,
/// <c>deleteProject</c>, <c>isFileInProject</c> and <c>isUnderProjectRoot</c> over <see cref="MatlabProject"/>,
/// the one open project per session (opening it makes its root the current folder, puts its path
/// folders on the search path and runs its startup files; closing runs its shutdown files and undoes
/// the rest), and the compiler tasks a project keeps: <c>compiler.project.get*Configuration</c>
/// for every target, each a handle whose writes land in the project's files at once, as R2025b's
/// do (probe <c>k20_projects</c>). The Compiler Task Manager and the apps open a task through the
/// host's <see cref="IScriptCompilerApps"/>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name this file declares, for the catalog's check.</summary>
    internal static IReadOnlyList<string> ProjectBuiltinNames =>
    [
        "matlab.project.createProject", "openProject", "currentProject", "matlab.project.currentProject", "matlab.project.loadProject",
        "matlab.project.rootProject", "matlab.project.deleteProject", "matlab.project.isFileInProject", "matlab.project.isUnderProjectRoot",
        "matlab.project.LabelDefinition",
        "compiler.project.getStandaloneDesktopAppConfiguration", "compiler.project.getProductionServerArchiveConfiguration",
        "compiler.project.getPythonLibraryConfiguration", "compiler.project.getDotNETLibraryConfiguration",
        "compiler.project.getCppSharedLibraryConfiguration", "compiler.project.getCSharedLibraryConfiguration",
        "compiler.project.getExcelAddInConfiguration", "compiler.project.getJavaLibraryConfiguration",
        "compiler.project.getCOMLibraryConfiguration", "compiler.project.getWebAppConfiguration", "compiler.project.getToolboxConfiguration",
    ];

    /// <summary>Declares the project builtins.</summary>
    internal static void RegisterProjectBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(host);
        ProjectSession projects = host.Projects;

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

        void Bare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true, AutoCallsBare = true }));

        // --- creating, opening, the current project ------------------------------------------------------

        Define("matlab.project.createProject", (args, line, col) =>
        {
            (string folder, string? name) = CreateProjectArguments(host, args, line, col);
            if (File.Exists(folder))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:project:creation:RootFolderIsFile",
                    $"Unable to create the specified project folder because a file \"{Path.GetFullPath(folder)}\" already exists. Delete this file, or specify a different project folder.");
            }

            if (JGraph.Deploy.Projects.ProjectDefinitionFiles.IsProjectRoot(folder))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:project:creation:ProjectAlreadyExists", "The specified folder already contains a project.");
            }

            if (projects.Current is not null)
            {
                CloseProject(env, interpreter, host, line, col); // R2025b closes the open project (its shutdown files run) before making the new one
            }

            try
            {
                MatlabProject.Create(folder, name);
            }
            catch (ProjectException ex)
            {
                throw ProjectDisplay.Refusal(ex, line, col);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:computils:File:UnableToCreateDirectoryWithKnownException",
                    $"Unable to create directory:\n{folder}\nwith exception:\n{ex.Message}");
            }

            return JgsValue.External(OpenProject(env, interpreter, host, folder, line, col));
        });

        foreach (string opener in (string[])["openProject", "matlab.project.loadProject"])
        {
            Define(opener, (args, line, col) =>
            {
                if (args.Count == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
                }

                if (args.Count > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
                }

                if (!IsTextScalar(args[0]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                        $"Expected projectPath to be one of these types:\n\nchar, string\n\nInstead its type was {ClassOf(args[0], JgsDialect.Matlab)}.");
                }

                string location = host.Resolve(TextOf(args[0]));
                if (projects.Current is { } open && MatlabProject.Exists(location) && SameRoot(open, location))
                {
                    return JgsValue.External(projects.CurrentObject!); // already the open one, as R2025b answers it
                }

                return JgsValue.External(OpenProject(env, interpreter, host, location, line, col));
            });
        }

        Define("currentProject", (args, line, col) =>
        {
            if (args.Count > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
            }

            return projects.NewHandle() is { } current
                ? JgsValue.External(current)
                : throw new JgsRuntimeException(line, col, "MATLAB:project:api:NoProjectCurrentlyLoaded", "No project is currently loaded.");
        });

        foreach (string quiet in (string[])["matlab.project.currentProject", "matlab.project.rootProject"])
        {
            Define(quiet, (args, line, col) =>
            {
                if (args.Count > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
                }

                return projects.NewHandle() is { } current
                    ? JgsValue.External(current)
                    : JgsValue.External(new DeviceArray(ProjectObject.DeclaredClass, interpreter, [], 1, 0));
            });
        }

        Define("matlab.project.deleteProject", (args, line, col) =>
        {
            if (args.Count != 1)
            {
                throw new JgsRuntimeException(line, col, args.Count == 0 ? "MATLAB:minrhs" : "MATLAB:TooManyInputs", args.Count == 0 ? "Not enough input arguments." : "Too many input arguments.");
            }

            if (!IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeFolder", "Invalid argument at position 1. Value must be a folder.");
            }

            string folder = host.Resolve(TextOf(args[0]));
            if (projects.Current is { } open && SameRoot(open, folder))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:project:management:DeleteFailLoaded", "Unable to remove a loaded project. Close the project and try again.");
            }

            try
            {
                MatlabProject.Delete(folder);
            }
            catch (ProjectException ex)
            {
                throw ProjectDisplay.Refusal(ex, line, col);
            }

            return JgsValue.Null;
        });

        Define("matlab.project.isFileInProject", (args, line, col) =>
            FileQuestions(args, line, col, file => projects.Current is { } open && open.RelativePath(host.Resolve(file)) is { Length: > 0 } relative && open.FindFile(relative) is not null));

        Define("matlab.project.isUnderProjectRoot", (args, line, col) =>
            FileQuestions(args, line, col, file => projects.Current is { } open && open.IsUnderRoot(host.Resolve(file))));

        Define("matlab.project.LabelDefinition", (args, line, col) =>
        {
            if (args.Count != 2 || !IsTextScalar(args[0]) || !IsTextScalar(args[1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:project:api:LabelAndCategoryNeedSpecifying", "Both a label and its parent category must be specified to find a label.");
            }

            return JgsValue.External(new LabelDefinitionObject(host.Devices, interpreter, TextOf(args[0]), TextOf(args[1])));
        });

        // --- the compiler tasks --------------------------------------------------------------------------

        foreach (string type in CompilerTaskKinds.Types)
        {
            string getter = CompilerTaskKinds.GetterOf(type);
            Define(getter, (args, line, col) =>
            {
                if (args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                if (args.Count > 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                if (args[0].AsExternalOrNull() is not ProjectObject owner)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:project:api:IsNotScalarProject", "The input argument should be a scalar Project.");
                }

                if (!IsTextScalar(args[1]) || TextOf(args[1]).Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "deployment:exception:STRING_CANNOT_BE_EMPTY", "The value for \"Deployment Task Name\" cannot be empty.");
                }

                var call = new DeviceCall { Target = owner, Args = [], Line = line, Column = col };
                MatlabProject project = owner.Live(call);
                CompilerTaskKind kind = CompilerTaskKinds.ByType(type)!;
                if (kind.Target is null)
                {
                    throw new JgsRuntimeException(line, col, "JGraph:compilerApps:targetOutOfScope",
                        $"{getter} makes a {kind.CategoryTitle} task, a target JGraph's compiler does not build (decisions KD1 and KD2). "
                        + "The tasks JGraph has are standalone applications, Production Server archives, .NET assemblies, Python packages, C++ and C shared libraries and Excel add-ins.");
                }

                try
                {
                    CompilerTaskConfiguration task = project.GetOrCreateTask(type, TextOf(args[1]));
                    return JgsValue.External(new TaskConfigurationObject(host.Devices, interpreter, owner, task));
                }
                catch (ProjectException ex)
                {
                    throw ProjectDisplay.Refusal(ex, line, col);
                }
            });
        }

        // --- the enumerations ------------------------------------------------------------------------------

        foreach ((string className, string[] members) in ProjectEnumValue.Members)
        {
            if (className == ProjectEnumValue.SourceControlStatus)
            {
                continue;
            }

            foreach (string member in members)
            {
                Bare(className + "." + member, (args, line, col) =>
                {
                    Arity(className + "." + member, args, 0, line, col);
                    return ProjectEnumValue.Of(host.Devices, interpreter, className, member);
                });
            }
        }
    }

    private static bool SameRoot(MatlabProject project, string location)
    {
        string full = Path.GetFullPath(location);
        string root = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        return string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), project.RootFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The folder and name <c>createProject</c> is given: a path, a name, or Name and Folder pairs.</summary>
    private static (string Folder, string? Name) CreateProjectArguments(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MATLAB", "Projects");
        if (args.Count == 0)
        {
            return (UniqueFolder(defaultRoot, MatlabProject.DefaultName), null);
        }

        if (args.Count == 1)
        {
            if (!IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:computils:File:UnableToCreateDirectoryWithKnownException",
                    $"Unable to create directory:\n{defaultRoot}\\\nwith exception:\nInvalid argument.");
            }

            string text = TextOf(args[0]);
            bool isPath = Path.IsPathRooted(text) || text.IndexOfAny(['\\', '/']) >= 0 || Directory.Exists(host.Resolve(text)) || File.Exists(host.Resolve(text));
            return isPath ? (host.Resolve(text), null) : (UniqueFolder(defaultRoot, text), text);
        }

        string? name = null;
        string? folder = null;
        for (int i = 0; i + 1 < args.Count || i < args.Count; i += 2)
        {
            string option = IsTextScalar(args[i]) ? TextOf(args[i]) : "";
            if (i + 1 >= args.Count || !(option.Equals("Name", StringComparison.OrdinalIgnoreCase) || option.Equals("Folder", StringComparison.OrdinalIgnoreCase)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", $"Invalid argument name '{option}'. Name must be 'Name' or 'Folder'.");
            }

            if (!IsTextScalar(args[i + 1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType", $"Expected {option} to be one of these types:\n\nchar, string\n\nInstead its type was {ClassOf(args[i + 1], JgsDialect.Matlab)}.");
            }

            if (option.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                name = TextOf(args[i + 1]);
            }
            else
            {
                folder = host.Resolve(TextOf(args[i + 1]));
            }
        }

        return (folder ?? UniqueFolder(defaultRoot, name ?? MatlabProject.DefaultName), name);
    }

    private static string UniqueFolder(string root, string name)
    {
        string candidate = Path.Combine(root, name);
        for (int n = 1; Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any(); n++)
        {
            candidate = Path.Combine(root, $"{name}_{n}");
        }

        return candidate;
    }

    /// <summary><c>isFileInProject</c> and <c>isUnderProjectRoot</c>: one logical per file given, in the files' shape.</summary>
    private static JgsValue FileQuestions(IReadOnlyList<JgsValue> args, int line, int col, Func<string, bool> question)
    {
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, args.Count == 0 ? "MATLAB:minrhs" : "MATLAB:TooManyInputs", args.Count == 0 ? "Not enough input arguments." : "Too many input arguments.");
        }

        JgsValue files = args[0];
        if (files.Type == JgsType.String || (files.IsCharMatrix && files.Rows <= 1))
        {
            return JgsValue.Bool(question(TextOf(files)));
        }

        var answers = new List<JgsValue>();
        if (files.IsStringArray)
        {
            for (int i = 0; i < files.ArrayLength; i++)
            {
                answers.Add(JgsValue.Bool(question(files.ElementAt(i).AsString)));
            }
        }
        else if (files.Type == JgsType.Cell && files.AsCell.All(IsTextScalar))
        {
            answers.AddRange(files.AsCell.Select(f => JgsValue.Bool(question(TextOf(f)))));
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:project:api:isUnderProjectRootInvalidArguments",
                "Input must be a file path specified as a string or character vector, or multiple file paths specified as a string array or cell array of character vectors.");
        }

        JgsValue row = JgsValue.Array(answers.ToArray());
        row.Reshape(files.Rows == 0 ? 1 : files.Rows, files.Rows == 0 ? answers.Count : files.Cols);
        return row;
    }

    /// <summary>
    /// Opens the project at <paramref name="location"/> as R2025b opens one: the open project closes
    /// first, the root becomes the current folder, the project path joins the search path, and the
    /// startup files run.
    /// </summary>
    internal static ProjectObject OpenProject(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, string location, int line, int col)
    {
        ProjectSession projects = host.Projects;
        MatlabProject project;
        try
        {
            project = MatlabProject.Open(location);
        }
        catch (ProjectException ex)
        {
            if (projects.Current is not null && ex.Identifier != "MATLAB:project:api:FileDoesNotExist")
            {
                CloseProject(env, interpreter, host, line, col); // R2025b closes the open project on its way to refusing what is not one
            }

            throw ProjectDisplay.Refusal(ex, line, col);
        }

        if (projects.Current is not null)
        {
            CloseProject(env, interpreter, host, line, col);
        }

        var operations = new ProjectOperations
        {
            CurrentFolderGetter = () => host.CurrentDirectory,
            AddToSearchPath = folder => interpreter.FunctionPath?.Add(folder, atEnd: false),
            RemoveFromSearchPath = folder => interpreter.FunctionPath?.Remove(folder),
            Analyze = roots => AnalyzeDependencies(env, interpreter, host, roots, topOnly: false),
            CloseCurrent = (l, c) => CloseProject(env, interpreter, host, l, c),
            Reopen = (root, l, c) => OpenProject(env, interpreter, host, root, l, c),
        };
        var handle = new ProjectObject(host.Devices, interpreter, project, projects, operations);
        projects.FolderBefore = host.CurrentDirectory;
        projects.Current = project;
        projects.CurrentObject = handle;
        projects.Operations = operations;
        projects.AddedFolders.Clear();
        host.ChangeDirectory(project.RootFolder);
        foreach (string relative in project.PathFolders.Reverse())
        {
            string folder = relative.Length == 0 ? project.RootFolder : project.FullPath(relative);
            if (Directory.Exists(folder))
            {
                interpreter.FunctionPath?.Add(folder, atEnd: false);
                projects.AddedFolders.Add(folder);
            }
        }

        foreach (string startup in project.StartupFiles)
        {
            RunProjectFile(env, project.FullPath(startup), line, col);
        }

        return handle;
    }

    /// <summary>Closes the open project: its shutdown files run, its folders leave the search path, the folder before it comes back.</summary>
    internal static void CloseProject(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, int line, int col)
    {
        ProjectSession projects = host.Projects;
        if (projects.Current is not { } project)
        {
            return;
        }

        try
        {
            foreach (string shutdown in project.ShutdownFiles)
            {
                RunProjectFile(env, project.FullPath(shutdown), line, col);
            }
        }
        finally
        {
            IEnumerable<string> pathFolders = project.PathFolders.Select(r => r.Length == 0 ? project.RootFolder : project.FullPath(r));
            foreach (string folder in projects.AddedFolders.Concat(pathFolders).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                interpreter.FunctionPath?.Remove(folder);
            }

            projects.AddedFolders.Clear();
            projects.Current = null;
            projects.CurrentObject = null;
            projects.Operations = null;
            if (projects.FolderBefore is { } before && Directory.Exists(before))
            {
                host.ChangeDirectory(before);
            }

            projects.FolderBefore = null;
        }
    }

    /// <summary>Runs a startup or shutdown file as <c>run</c> runs a file.</summary>
    private static void RunProjectFile(JgsEnvironment env, string file, int line, int col)
    {
        if (!File.Exists(file))
        {
            return;
        }

        if (env.Builtins.TryGet("run", out JgsValue run) && run.Type == JgsType.Function)
        {
            run.AsCallable.Call([JgsValue.Str(file)], line, col);
        }
    }
}
