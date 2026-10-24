using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>mcc</c> (compiler and SDK plan, stage K6): a standalone program built from a session, as R2025b's
/// mcc builds one. The command line is <see cref="MccCommandLine"/>'s; the main and its dependencies are
/// found by <see cref="JgsDependencyAnalyzer"/> from the session's current folder and path, with the
/// main's own folder searched as R2025b's mcc searches it (k6_roots); the archive is
/// <see cref="CtfLayout.FromReport"/>'s, its files placed by the folder mcc runs in; the program is
/// <see cref="StandaloneProgram"/>'s; and the log files are <see cref="BuildLogs"/>'.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterCompilerBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> CompilerBuiltinNames { get; } =
    [
        "mcc", "compiler.build.standaloneApplication", "compiler.build.standaloneWindowsApplication",
        "compiler.build.StandaloneApplicationOptions",
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
        MccCommandLine command;
        try
        {
            command = MccCommandLine.Parse(words);
        }
        catch (MccException refused)
        {
            throw new JgsRuntimeException(line, col, refused.Identifier, refused.Message);
        }

        if (command.ShowHelp)
        {
            host.WriteOut(MccCommandLine.HelpText);
            return null;
        }

        string here = host.CurrentDirectory;
        string Within(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));

        // The main and any other file named: as written, or by name on the path.
        var roots = new List<string>();
        foreach (string given in command.Files)
        {
            string? found = DependencyRoot(given, interpreter, host);
            if (found is null || !File.Exists(found))
            {
                throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_M_FILE_NOT_FOUND",
                    $"Could not find the MATLAB file '{given}' on the MATLAB path.");
            }

            string extension = Path.GetExtension(found).ToLowerInvariant();
            if (extension == ".mlx")
            {
                throw new JgsRuntimeException(line, col, "JGraph:mcc:liveScript",
                    $"'{given}' is a live script, and JGraph has no live scripts: save it as a .m file to build it.");
            }

            if (extension is not (".m" or ".p" or ".mlapp"))
            {
                throw new JgsRuntimeException(line, col, "JGraph:mcc:notCode",
                    $"'{given}' is not a MATLAB code file (.m, .p or .mlapp) JGraph can build.");
            }

            roots.Add(found);
        }

        string main = roots[0];
        string? help = null;
        if (command.HelpFile is { } helpFile)
        {
            string path = Within(helpFile);
            help = File.Exists(path)
                ? File.ReadAllText(path)
                : throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_HELP_FILE_NOT_EXIST",
                    $"The help file \n  '{helpFile}'\ndoes not exist.");
        }

        IReadOnlyList<IconImage> icon = [];
        if (command.IconFile is { } iconFile)
        {
            try
            {
                icon = IconFile.Read(Within(iconFile));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new JgsRuntimeException(line, col, "Compiler:compiler:EMBED_ICON_FAILED",
                    $"Failed to embed icon file \"{iconFile}\" - Failed to open file ReadIconFromICOFile with error File not found..");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new JgsRuntimeException(line, col, "Compiler:compiler:EMBED_ICON_FAILED",
                    $"Failed to embed icon file \"{iconFile}\" - Error reading icon file.");
            }
        }

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

        string component = command.ComponentName ?? Path.GetFileNameWithoutExtension(main);
        string program = command.OutputName ?? Path.GetFileNameWithoutExtension(main);
        if (!MccCommandLine.IsOutputName(program))
        {
            throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME",
                $"Illegal output name: '{program}' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')");
        }

        string folder = Within(command.OutputFolder ?? ".");
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
            Warn(host, "Compiler:compiler:COM_WARN_EXCLUDED_FILE", $"Excluded \"{excluded.Name}\"; reason: {excluded.Reason}");
        }

        if (report.Files.Any(static f => Path.GetFileName(f.Path).Equals("startup.m", StringComparison.OrdinalIgnoreCase)))
        {
            Warn(host, "Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED",
                "Your deployed application may fail because file or folder paths\nnot present in the deployed environment may be included in your MATLAB startup\n"
                + "file. Use the MATLAB function \"isdeployed\" in your MATLAB startup file to\ndetermine the appropriate execution environment when including file and folder\n"
                + "paths, and recompile your application.\n");
        }

        if (SecretReaders.FirstOrDefault(named.Contains) is { } secret)
        {
            Warn(host, "Compiler:compiler:COM_WARN_CONFIG_SECRETS_FOR_DESKTOP",
                $"Your app reads secret keys with '{secret}'. A JGraph program reads each one from the environment variable of the same name "
                + "on the computer it runs on: set those variables there. JGraph's mcc does not embed secrets in the archive (-J).\n");
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

        IReadOnlyList<string> files;
        CtfManifest manifest;
        try
        {
            (files, manifest) = StandaloneProgram.Write(build, new StandaloneProgramOptions
            {
                OutputFolder = folder,
                ExecutableName = program,
                Windowed = command.Target == MccTarget.Windowed,
                EmbedArchive = !command.SeparateArchive,
                IconImages = icon,
                Version = command.Version,
                HelpText = help,
                RuntimeDelivery = command.RuntimeDelivery,
                Runtime = runtime,
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or PlatformNotSupportedException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The program could not be written: {ex.Message}");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, component).Replace('/', '\\'));
        if (command.Verbose)
        {
            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
        }

        return new MccBuild(files, manifest.Uuid);
    }
}
