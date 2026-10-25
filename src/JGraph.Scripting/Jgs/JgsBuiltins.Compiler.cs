using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>mcc</c> (compiler and SDK plan, stage K6): a standalone program built from a session, as R2025b's
/// mcc builds one. The command line is <see cref="MccCommandLine"/>'s; the main and its dependencies are
/// found by <see cref="JgsDependencyAnalyzer"/> from the session's current folder and path, with the
/// main's own folder searched as R2025b's mcc searches it (k6_roots); the archive is
/// <see cref="CtfLayout.FromReport"/>'s, its files placed by the folder mcc runs in; the program is
/// <see cref="StandaloneProgram"/>'s; and the log files are <see cref="BuildLogs"/>'. Stage K7 adds the
/// rest of R2025b's command line: the obsolete switches' notes, the ignored switches' warnings, the repeated
/// file, <c>-w</c> over every warning mcc gives, <c>-T codegen</c> and <c>compile:exe</c>, <c>-K</c>, and
/// <c>-/t</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterCompilerBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> CompilerBuiltinNames { get; } =
    [
        "mcc", "compiler.build.standaloneApplication", "compiler.build.standaloneWindowsApplication",
        "compiler.build.StandaloneApplicationOptions", "compiler.build.dotNETAssembly", "compiler.build.DotNETAssemblyOptions",
        "compiler.build.cSharedLibrary", "compiler.build.CSharedLibraryOptions",
    ];

    /// <summary>The functions whose use makes mcc warn that secrets come from the machine the program runs on.</summary>
    private static readonly string[] SecretReaders = ["getSecret", "getSecretMetadata", "isSecret"];

    /// <summary>What an mcc build wrote: its files, in compiler.build.Results' order, and its archive's uuid.</summary>
    internal sealed record MccBuild(IReadOnlyList<string> Files, string Uuid);

    /// <summary>Declares mcc and compiler.build; neither exists in a deployed session, as R2025b's runtime ships neither.</summary>
    internal static void RegisterCompilerBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register("mcc", JgsValue.Function(new BuiltinFunction("mcc", (args, line, col) =>
        {
            Mcc(env, interpreter, host, McWords(args, line, col), line, col);
            return JgsValue.Null;
        })
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
        }));

        RegisterCompilerBuild(env, interpreter, host);
    }

    /// <summary>mcc's arguments: each a char row or a string scalar.</summary>
    private static List<string> McWords(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var words = new List<string>(args.Count);
        foreach (JgsValue arg in args)
        {
            words.Add(IsTextScalar(arg) && !arg.IsCharMatrix
                ? TextOf(arg)
                : throw new JgsRuntimeException(line, col, "JGraph:mcc:notText", "mcc's arguments must be character vectors or string scalars."));
        }

        return words;
    }

    /// <summary>Runs mcc over <paramref name="words"/>; null when it only printed its help.</summary>
    internal static MccBuild? Mcc(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<string> words, int line, int col)
    {
        string here = host.CurrentDirectory;
        MccCommandLine command;
        try
        {
            command = MccCommandLine.Parse(words, here);
        }
        catch (MccException refused)
        {
            throw new JgsRuntimeException(line, col, refused.Identifier, refused.Message);
        }

        if (command.PrintsOnly)
        {
            host.WriteOut(command.ShowHelp ? MccCommandLine.HelpText
                : command.ShowTraceHelp ? MccCommandLine.TraceHelpText
                : command.Warnings.ListText());
            return null;
        }

        if (command.Trace)
        {
            host.WriteOut("Processed command line is:\n" + string.Join(' ', command.Processed.Select(static w => '"' + w + '"')) + "\n");
        }

        foreach (string note in command.Notes)
        {
            host.WriteOut(note + "\n");
        }

        // mcc's warnings, through -w: shown unless it hides them (and MATLAB's own warning state
        // allows), and failing the build after showing when it makes them errors (k7_more).
        void McWarn(string identifier, string text)
        {
            if (command.Warnings.Shows(identifier))
            {
                Warn(host, identifier, text);
            }

            if (command.Warnings.Fails(identifier))
            {
                MccException failed = MccWarnings.TreatedAsError();
                throw new JgsRuntimeException(line, col, failed.Identifier, failed.Message);
            }
        }

        string Within(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));

        // The main and any other file named: as written, or by name on the path; a file named twice
        // warned about and taken once, and anything but code refused without -a (k7_switches).
        var roots = new List<string>();
        string? AddRoot(string given)
        {
            string? found = DependencyRoot(given, interpreter, host);
            if (found is null || !File.Exists(found))
            {
                string written = Within(given);
                if (File.Exists(written) && Path.GetExtension(written).ToLowerInvariant() is not (".m" or ".p" or ".mlapp" or ".mlx"))
                {
                    found = written;
                }
                else
                {
                    // R2025b names the file without its folder (k7_switches: M_end).
                    throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_M_FILE_NOT_FOUND",
                        $"Could not find the MATLAB file '{Path.GetFileName(given)}' on the MATLAB path.");
                }
            }

            found = Path.GetFullPath(found);
            string extension = Path.GetExtension(found).ToLowerInvariant();
            if (extension == ".mlx")
            {
                throw new JgsRuntimeException(line, col, "JGraph:mcc:liveScript",
                    $"'{given}' is a live script, and JGraph has no live scripts: save it as a .m file to build it.");
            }

            if (extension is not (".m" or ".p" or ".mlapp"))
            {
                throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERROR_INCLUDE_FILE_WITHOUT_DASH_A",
                    $"Error: You specified the file \"{found}\" without using the \"-a\" option.");
            }

            if (roots.Contains(found, StringComparer.OrdinalIgnoreCase))
            {
                McWarn("Compiler:compiler:COM_WARN_REPEATED_FILE", $"The file \"{found}\" was repeated on the command line.\n");
                return null;
            }

            roots.Add(found);
            return found;
        }

        // A .NET assembly's classes (stage K8): the -W class takes the files named alone, and each
        // class{Name:file,...} its own, a class named twice taking both lists (k8_build: mcc_classes).
        var classes = new List<(string Name, List<string> Files)>();
        List<string> ClassFiles(string name)
        {
            int at = classes.FindIndex(c => c.Name == name);
            if (at < 0)
            {
                classes.Add((name, []));
                at = classes.Count - 1;
            }

            return classes[at].Files;
        }

        // A C library exports each file named, in order (stage K11).
        var exported = new List<string>();
        foreach (string given in command.Files)
        {
            if (AddRoot(given) is { } root)
            {
                exported.Add(root);
                if (command.DotNet is { } wrapper)
                {
                    ClassFiles(wrapper.ClassName).Add(root);
                }
            }
        }

        if (command.DotNet is { } dotNet)
        {
            // The -W class is made even with no file of its own (k8_build: mcc_classonly has First.cs).
            _ = ClassFiles(dotNet.ClassName);

            foreach ((string name, List<string> given) in command.Classes)
            {
                foreach (string file in given)
                {
                    if (AddRoot(file) is { } root)
                    {
                        ClassFiles(name).Add(root);
                    }
                }
            }
        }

        // The ignored switches are warned about once the files are found (k7_switches: M_end warned of nothing).
        foreach (string ignored in command.IgnoredSwitches)
        {
            McWarn("Compiler:compiler:COM_WARN_SWITCH_IGNORED", $"The option -{ignored} is ignored in standalone target mode (specify -? for help).\n");
        }

        // A startup.m beside the main is compiled in with it, and runs before it (K0's startup, k7_switches' w_start).
        string startup = Path.Combine(Path.GetDirectoryName(roots[0])!, "startup.m");
        if ((command.Target is MccTarget.Console or MccTarget.Windowed) && File.Exists(startup) && !roots.Contains(startup, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(startup);
        }

        foreach (string folderGiven in command.IncludeFolders)
        {
            if (!Directory.Exists(Within(folderGiven)))
            {
                throw new JgsRuntimeException(line, col, "Compiler:compiler:DEPFUN_ERR_INVALID_DIR_NAME", $"Invalid directory: {Within(folderGiven)}\n");
            }
        }

        string main = roots[0];
        string? help = command.HelpFile is { } helpFile ? File.ReadAllText(Within(helpFile)) : null; // MccCommandLine found it

        var additional = new List<string>();
        foreach (string given in command.Additional)
        {
            string path = Within(given);
            if (given.IndexOfAny(['*', '?']) < 0 && !File.Exists(path) && !Directory.Exists(path))
            {
                throw new JgsRuntimeException(line, col, "Compiler:compiler:REQ_UNEXPECTED_ERROR",
                    "Error while determining required deployable files. Compilation terminated. Details:\n"
                    + (given.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ? "Error using matlab.depfun.internal.MatFileInspector/determineType (line 32)\n" : "")
                    + $"File, function or class \"{given}\" may not exist. Neither WHICH nor EXIST could find an exact, case-sensitive match. "
                    + "Please check the spelling of the name, and that any\nrequired directories are on the MATLAB path.\n");
            }

            additional.Add(path);
        }

        string component = command.DotNet?.ShortName ?? command.ComponentName ?? Path.GetFileNameWithoutExtension(main);
        string program = command.OutputName ?? Path.GetFileNameWithoutExtension(main);
        if (!MccCommandLine.IsOutputName(program))
        {
            throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME",
                $"Illegal output name: '{program}' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')");
        }

        string folder = Within(command.OutputFolder ?? ".");

        // A MATLAB Data API build generates its strongly typed interface first, as R2025b's mcc does
        // (k9_build: main.printed, the count line before the compiler's version), and makes the
        // archive alone, saying so, when a function takes varargin or gives varargout (variadic).
        IReadOnlyList<(string File, string Text)>? typedInterface = null;
        if (command.DotNet is { DataApi: true } dataApi && !DotNetDataAssembly.StronglyTypedDisabled)
        {
            (List<TypedFunction> typedFunctions, List<TypedClass> typedClasses) = TypedInterfaceReader.Read(classes.SelectMany(static c => c.Files));
            var classNames = new HashSet<string>(typedClasses.Select(static c => c.FullName), StringComparer.Ordinal);
            try
            {
                typedInterface = TypedInterface.Generate(
                    typedFunctions, typedClasses, dataApi.AssemblyName, DateTime.Now, classNames.Contains, RuntimeLayout.CurrentVersion);
                if (command.Verbose)
                {
                    host.WriteOut($"{typedClasses.Count} class(es) and {typedFunctions.Count} function(s) written to "
                        + $"{Path.Combine(folder, DotNetDataAssembly.InterfaceFolder)}\n\n");
                }
            }
            catch (TypedInterfaceException)
            {
                host.WriteOut(DotNetDataAssembly.NotGeneratedNote + "\n");
            }
        }

        if (command.Verbose)
        {
            host.WriteOut($"Compiler version: JGraph {RuntimeLayout.CurrentVersion} (R2025b)\nAnalyzing file dependencies. \n");
            foreach (string root in roots)
            {
                host.WriteOut($"Parsing file \"{root}\"\n\t(referenced from command line).\n");
            }
        }

        // The analysis, noting the built-ins the code names for the warnings below.
        var named = new HashSet<string>(StringComparer.Ordinal);
        var options = new DependencyOptions
        {
            CurrentFolder = here,
            SearchPath = interpreter.FunctionPath?.Folders ?? [],
            SearchRootFolders = true,
            IncludeFolders = [.. command.IncludeFolders.Select(Within)],
            AdditionalFiles = additional,
            DetectDataFiles = !command.NoDataFiles,
            ClearToolboxPath = command.ClearToolboxPath,
            ToolboxFolders = command.ToolboxFolders,
        };
        DependencyReport report = new JgsDependencyAnalyzer(options, name =>
        {
            bool builtin = IsBuiltinName(env, name);
            if (builtin)
            {
                named.Add(name);
            }

            return builtin;
        }).Analyze(roots);

        foreach (ExcludedDependency excluded in report.Excluded)
        {
            McWarn("Compiler:compiler:COM_WARN_EXCLUDED_FILE", $"Excluded \"{excluded.Name}\"; reason: {excluded.Reason}");
        }

        if (report.Files.Any(static f => Path.GetFileName(f.Path).Equals("startup.m", StringComparison.OrdinalIgnoreCase)))
        {
            McWarn("Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED",
                "Your deployed application may fail because file or folder paths\nnot present in the deployed environment may be included in your MATLAB startup\n"
                + "file. Use the MATLAB function \"isdeployed\" in your MATLAB startup file to\ndetermine the appropriate execution environment when including file and folder\n"
                + "paths, and recompile your application.\n");
        }

        if (SecretReaders.FirstOrDefault(named.Contains) is { } secret)
        {
            McWarn("Compiler:compiler:COM_WARN_CONFIG_SECRETS_FOR_DESKTOP",
                $"Your app reads secret keys with '{secret}'. A JGraph program reads each one from the environment variable of the same name "
                + "on the computer it runs on: set those variables there. JGraph's mcc does not embed secrets in the archive (-J).\n");
        }

        if (command.DotNet is { DataApi: true } data)
        {
            return DotNetDataBuild(command, data, classes, typedInterface, main, report, folder, here, line, col, host);
        }

        if (command.DotNet is { } assembly)
        {
            return DotNetBuild(command, assembly, classes, main, report, folder, here, words, line, col, host);
        }

        if (command.Target == MccTarget.CLibrary)
        {
            return CLibraryBuild(command, exported, main, report, folder, here, words, line, col, host);
        }

        CtfBuild build = CtfLayout.FromReport(component, main, report, rootFolder: here) with
        {
            TargetType = command.Target == MccTarget.Windowed ? CtfTargetType.WindowsStandaloneApplication : CtfTargetType.StandaloneApplication,
            NumericArguments = command.NumericArguments,
            RuntimeOptions = command.RuntimeOptions,
        };
        if (command.PCode)
        {
            build = JgsPcode.PcodeBuild(build);
        }

        RuntimeLayout? runtime = null;
        if (command.RuntimeDelivery == RuntimeDelivery.Bundled)
        {
            runtime = RuntimeLayout.Find() ?? throw new JgsRuntimeException(line, col, "JGraph:mcc:noRuntimeToBundle",
                $"A bundled program carries the JGraph Runtime {RuntimeLayout.CurrentVersion}, and none was found to copy: "
                + $"install it, or set {RuntimeLayout.RootVariable} to its folder.");
        }

        var programOptions = new StandaloneProgramOptions
        {
            OutputFolder = folder,
            ExecutableName = program,
            Windowed = command.Target == MccTarget.Windowed,
            EmbedArchive = !command.SeparateArchive,
            ArchiveOnly = command.Link == MccLink.ArchiveOnly,
            Version = command.Version,
            HelpText = help,
            RuntimeDelivery = command.RuntimeDelivery,
            Runtime = runtime,
        };
        void WriteLogs() => BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, component).Replace('/', '\\'));

        // The icon is read last, as R2025b embeds it last: a failed one leaves nothing, or under -K the
        // archive, the readme and the logs (k7_switches: K_fail_icon, noK_fail_icon).
        if (command.IconFile is { } iconFile && !programOptions.ArchiveOnly)
        {
            string? failure = null;
            try
            {
                programOptions = programOptions with { IconImages = IconFile.Read(Within(iconFile)) };
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                failure = $"Failed to embed icon file \"{iconFile}\" - Failed to open file ReadIconFromICOFile with error File not found..";
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                failure = $"Failed to embed icon file \"{iconFile}\" - Error reading icon file.";
            }

            if (failure is not null)
            {
                if (command.KeepPartialOutput)
                {
                    StandaloneProgram.Write(build, programOptions with { ArchiveOnly = true });
                    WriteLogs();
                }

                throw new JgsRuntimeException(line, col, "Compiler:compiler:EMBED_ICON_FAILED", failure);
            }
        }

        IReadOnlyList<string> files;
        CtfManifest manifest;
        try
        {
            (files, manifest) = StandaloneProgram.Write(build, programOptions);
        }
        catch (Exception ex) when (ex is FileNotFoundException or PlatformNotSupportedException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The program could not be written: {ex.Message}");
        }

        WriteLogs();
        if (command.Verbose)
        {
            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
        }

        return new MccBuild(files, manifest.Uuid);
    }

    /// <summary>
    /// <c>-W dotnet:</c> (stage K8): the archive with every class's functions public, and the assembly
    /// <see cref="DotNetAssembly"/> writes, each function's inputs, outputs and help line read from its file.
    /// </summary>
    private static MccBuild DotNetBuild(MccCommandLine command, DotNetWrapper wrapper, List<(string Name, List<string> Files)> classes,
        string main, DependencyReport report, string folder, string here, IReadOnlyList<string> words, int line, int col, JGraphScriptGlobals host)
    {
        var dotNetClasses = classes.Select(c => new DotNetClass(c.Name, [.. c.Files.Select(DotNetFunctionOf)])).ToList();
        CtfBuild build = CtfLayout.FromReport(wrapper.ShortName, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.DotNetAssembly,
            RuntimeOptions = command.RuntimeOptions,
            MainFunction = null,
            PublicFunctions =
            [
                .. dotNetClasses.SelectMany(static c => c.Functions).Select(f =>
                    new CtfFunction(f.Name, CtfLayout.ArchivePath(f.File, here, wrapper.ShortName), f.Inputs, f.Outputs)),
            ],
        };
        if (command.PCode)
        {
            build = JgsPcode.PcodeBuild(build);
        }

        var options = new DotNetAssemblyOptions
        {
            OutputFolder = folder,
            Wrapper = wrapper,
            Classes = dotNetClasses,
            Version = command.Version,
            EmbedArchive = !command.SeparateArchive,
            Debug = command.DebugBuild,
            MccArguments = words,
        };
        if (command.Verbose)
        {
            foreach (DotNetClass cls in dotNetClasses)
            {
                host.WriteOut($"Generating file \"{Path.Combine(folder, cls.Name + "Native.cs")}\".\n");
            }

            foreach (DotNetClass cls in dotNetClasses)
            {
                host.WriteOut($"Generating file \"{Path.Combine(folder, cls.Name + ".cs")}\".\n");
            }

            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
        }

        IReadOnlyList<string> files;
        CtfManifest manifest;
        try
        {
            (files, manifest) = DotNetAssembly.Write(build, options);
        }
        catch (MccException refused)
        {
            throw new JgsRuntimeException(line, col, refused.Identifier, refused.Message);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The assembly could not be written: {ex.Message}");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, wrapper.ShortName).Replace('/', '\\'));
        return new MccBuild(files, manifest.Uuid);
    }

    /// <summary>
    /// <c>-W dotnet:...,api=matlab-data</c> (stage K9): the archive with every file's function or class
    /// public by its package-qualified name, and what <see cref="DotNetDataAssembly"/> writes beside it.
    /// </summary>
    private static MccBuild DotNetDataBuild(MccCommandLine command, DotNetWrapper wrapper, List<(string Name, List<string> Files)> classes,
        IReadOnlyList<(string File, string Text)>? typedInterface, string main, DependencyReport report, string folder, string here,
        int line, int col, JGraphScriptGlobals host)
    {
        CtfBuild build = CtfLayout.FromReport(wrapper.ShortName, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.DotNetAssembly,
            RuntimeOptions = command.RuntimeOptions,
            MainFunction = null,
            PublicFunctions =
            [
                .. classes.SelectMany(static c => c.Files).Distinct(StringComparer.OrdinalIgnoreCase).Select(file =>
                {
                    DotNetFunction f = DotNetFunctionOf(file);
                    return new CtfFunction(TypedInterfaceReader.FullName(file), CtfLayout.ArchivePath(file, here, wrapper.ShortName), f.Inputs, f.Outputs);
                }),
            ],
        };
        if (command.PCode)
        {
            build = JgsPcode.PcodeBuild(build);
        }

        if (command.Verbose)
        {
            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
        }

        DotNetDataBuild built;
        try
        {
            built = DotNetDataAssembly.Write(build, new DotNetDataAssemblyOptions
            {
                OutputFolder = folder,
                Wrapper = wrapper,
                Interface = typedInterface,
                Version = command.Version,
                Debug = command.DebugBuild,
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The archive could not be written: {ex.Message}");
        }

        if (built.Note is { } note)
        {
            host.WriteOut(note + "\n");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, wrapper.ShortName).Replace('/', '\\'));
        return new MccBuild(built.Files, built.Manifest.Uuid);
    }

    /// <summary>A function file's name, inputs, outputs and help line, as the generated classes declare and document it.</summary>
    private static DotNetFunction DotNetFunctionOf(string file)
    {
        string text = File.ReadAllText(file);
        string name = Path.GetFileNameWithoutExtension(file);
        IReadOnlyList<string> inputs = [];
        IReadOnlyList<string> outputs = [];
        try
        {
            if (Parser.Parse(text, file, JgsDialect.Matlab).OfType<FnStmt>().FirstOrDefault() is { } declared)
            {
                inputs = declared.Parameters;
                outputs = declared.Outputs;
            }
        }
        catch (JgsException)
        {
            // A file that does not parse is refused when it is called; its methods take no arguments.
        }

        // The help's first line: the first comment after the declaration, its % and blanks dropped.
        string h1 = string.Empty;
        bool declaration = false;
        foreach (string raw in text.Split('\n'))
        {
            string trimmed = raw.Trim();
            if (!declaration)
            {
                declaration = trimmed.StartsWith("function", StringComparison.Ordinal);
                continue;
            }

            if (trimmed.StartsWith('%'))
            {
                h1 = trimmed.TrimStart('%').Trim();
            }

            break;
        }

        return new DotNetFunction(name, inputs, outputs, h1, file);
    }
}
