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
        foreach (string given in command.Files)
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
                continue;
            }

            roots.Add(found);
        }

        // The ignored switches are warned about once the files are found (k7_switches: M_end warned of nothing).
        foreach (string ignored in command.IgnoredSwitches)
        {
            McWarn("Compiler:compiler:COM_WARN_SWITCH_IGNORED", $"The option -{ignored} is ignored in standalone target mode (specify -? for help).\n");
        }

        // A startup.m beside the main is compiled in with it, and runs before it (K0's startup, k7_switches' w_start).
        string startup = Path.Combine(Path.GetDirectoryName(roots[0])!, "startup.m");
        if (File.Exists(startup) && !roots.Contains(startup, StringComparer.OrdinalIgnoreCase))
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
}
