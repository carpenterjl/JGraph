using System.Diagnostics;
using JGraph.Deploy;
using JGraph.Scripting.Jgs.Native;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Excel add-ins (compiler and SDK plan, stage K17b): <c>mcc -W excel:&lt;name&gt;,&lt;class&gt;[,version=v]</c>
/// over <see cref="ExcelAddIn"/>, and <c>compiler.build.excelAddIn</c> with
/// <c>compiler.build.ExcelAddInOptions</c>, transcribed from R2025b's toolbox/compiler/cli/build
/// (ExcelAddInOptions.m, excelAddIn.m, the ExcelAddIn builder, ExcelAddinWriter.m and
/// writeInstallBat.m), their refusals as <c>probe_k17_excel</c> recorded them
/// (<c>recordings/k17_excel.out.txt</c>). The add-in's DLL is stage K17c's and its <c>.bas</c> stage
/// K17e's: this stage writes everything else and says what is pending.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string ExcelOptionsClassName = JgsBuiltinClasses.ExcelOptions;

    private static void RegisterExcelBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(ExcelOptionsClassName, JgsValue.Function(new BuiltinFunction(ExcelOptionsClassName,
            (args, line, col) => JgsValue.Object(NewExcelOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string name = "compiler.build.excelAddIn";
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => BuildExcelAddIn(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildExcelAddIn(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));
    }

    // --- mcc -W excel ----------------------------------------------------------------------

    /// <summary>
    /// <c>mcc -W excel:&lt;name&gt;,&lt;class&gt;[,version=v]</c> (stage K17b): the archive with every
    /// file's function public, written by <see cref="ExcelAddIn"/> beside the DLL stage K17c adds, or
    /// alone for <c>-T codegen</c> (and <c>-T compile:lib</c>, which R2025b's mbuild fails on). A
    /// name that cannot be a type library's or a ProgID's is refused before anything is written,
    /// where R2025b's build fails in mbuild (k17_excel: mcc_badname, mcc_badclass).
    /// </summary>
    private static MccBuild ExcelAddInBuild(MccCommandLine command, IReadOnlyList<string> exported, string main, DependencyReport report,
        string folder, string here, IReadOnlyList<string> words, int line, int col, JGraphScriptGlobals host)
    {
        string name = command.ComponentName ?? Path.GetFileNameWithoutExtension(main);
        string className = command.ExcelClassName ?? name;
        if (!ExcelAddIn.IsComName(name))
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:excelAddInName",
                $"An Excel add-in's name must begin with a letter or '_' and contain only letters, digits and '_', as its type library and "
                + $"ProgIDs do: '{name}' cannot be one.");
        }

        if (!ExcelAddIn.IsComName(className))
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:excelClassName",
                $"An Excel add-in's class must begin with a letter or '_' and contain only letters, digits and '_', as its coclass and "
                + $"ProgIDs do: '{className}' cannot be one.");
        }

        List<ExcelFunction> functions = [.. exported.Select(ExcelFunctionOf)];
        CtfBuild build = CtfLayout.FromReport(name, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.ExcelAddIn,
            RuntimeOptions = command.RuntimeOptions,
            MainFunction = null,
            PublicFunctions = [.. functions.Select(f => new CtfFunction(f.Name, CtfLayout.ArchivePath(f.File, here, name), f.Inputs, f.Outputs))],
        };
        if (command.PCode)
        {
            build = JgsPcode.PcodeBuild(build);
        }

        bool archiveOnly = command.Link != MccLink.Program;
        if (command.Verbose)
        {
            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
        }

        ExcelAddInBuild built;
        try
        {
            built = ExcelAddIn.Write(build, new ExcelAddInOptions
            {
                OutputFolder = folder,
                AddInName = name,
                ClassName = className,
                Functions = functions,
                Version = command.Version,
                EmbedArchive = !command.SeparateArchive,
                Debug = command.DebugBuild,
                VisualBasicFile = command.VisualBasicFile,
                ArchiveOnly = archiveOnly,
                MccArguments = words,
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The Excel add-in could not be written: {ex.Message}");
        }

        if (!archiveOnly && !built.DllWritten)
        {
            string pending = command.VisualBasicFile
                ? $"the DLL {built.DllName} and the module {name}.bas are"
                : $"the DLL {built.DllName} is";
            Warn(host, "JGraph:mcc:excelAddInPending",
                $"This build of JGraph writes the add-in without its COM server: {pending} pending (compiler and SDK plan, stages K17c and K17e), "
                + $"and the archive is beside it as {name}.ctf.");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, name).Replace('/', '\\'));
        return new MccBuild(built.Files, built.Manifest.Uuid);
    }

    /// <summary>
    /// What a file exports as: its function's inputs and outputs as <see cref="CLibraryFunctionOf"/>
    /// reads them (a script none, a class its constructor's any number, a P-file's by their places),
    /// and its H1 line as it is written, byte for byte (R2025b's C++ help map mangles it; ADR 0314).
    /// </summary>
    private static ExcelFunction ExcelFunctionOf(string file)
    {
        CLibraryFunction f = CLibraryFunctionOf(file);
        string h1 = "";
        if (Path.GetExtension(file).Equals(".m", StringComparison.OrdinalIgnoreCase))
        {
            bool declaration = false;
            foreach (string raw in File.ReadAllText(file).Split('\n'))
            {
                string trimmed = raw.Trim();
                if (!declaration)
                {
                    declaration = trimmed.StartsWith("function", StringComparison.Ordinal);
                    if (!declaration && trimmed.StartsWith('%'))
                    {
                        // A script's help is its first comment.
                        h1 = trimmed.TrimStart('%').Trim();
                        break;
                    }

                    continue;
                }

                if (trimmed.StartsWith('%'))
                {
                    h1 = trimmed.TrimStart('%').Trim();
                }

                break;
            }
        }

        return new ExcelFunction(f.Name, f.Inputs, f.Outputs, h1, file);
    }

    // --- The options -----------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's ExcelAddInOptions declares it.</summary>
    internal static JgsValue ExcelOptionDefault(string name) => name switch
    {
        "AddInVersion" => JgsValue.Str("1.0.0.0"),
        "DebugBuild" or "GenerateVisualBasicFile" => OnOffSwitchStateValue.Of(false),
        "EmbedArchive" => OnOffSwitchStateValue.Of(true),
        _ => CLibraryOptionDefault(name),
    };

    /// <summary>
    /// R2025b's get.AddInName (the name given, or the first file's stem as it is), get.ClassName
    /// (the class given, or the stem with <c>Class</c> after it) and OutputDir (the folder given, or
    /// <c>.\&lt;name&gt;excelAddIn</c>) (k17_excel: opts.derived.*, derived.*, nooutdir).
    /// </summary>
    internal static JgsValue ExcelOptionDerived(string name, JgsObject options)
    {
        string stem = "";
        if (options.Fields["FunctionFiles"] is { Type: JgsType.Cell } files && files.AsCell.Length > 0)
        {
            stem = Path.GetFileNameWithoutExtension(TextOf(files.AsCell[0]));
        }

        string addIn = TextOf(options.Fields[JgsBuiltinClasses.GivenAddInName]);
        if (addIn.Length == 0)
        {
            addIn = stem;
        }

        switch (name)
        {
            case "AddInName":
                return JgsValue.Str(addIn);
            case "ClassName":
            {
                string given = TextOf(options.Fields[JgsBuiltinClasses.GivenClass]);
                return JgsValue.Str(given.Length > 0 ? given : stem + "Class");
            }

            default:
            {
                string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
                return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(addIn) + "excelAddIn");
            }
        }
    }

    /// <summary>
    /// <c>compiler.build.ExcelAddInOptions(files, Name, Value, ...)</c>: the files set first, then
    /// every other name in the order <c>setdiff</c> leaves them (sorted), as R2025b's constructor does.
    /// </summary>
    private static JgsObject NewExcelOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.ExcelOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(ExcelOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{ExcelOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetExcelOption(interpreter, options, "FunctionFiles", args[0], line, col);
        var given = new SortedDictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]) || args[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string asked = TextOf(args[i]);
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{asked}'. Name-value pair arguments require a name followed by a value.");
            }

            given[ParameterName(asked, JgsBuiltinClasses.ExcelOptionNames, line, col)] = args[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetExcelOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it.</summary>
    internal static void SetExcelOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        switch (name)
        {
            case "FunctionFiles":
                options.Fields[name] = FunctionFileList(value, here, line, col);
                WarnAboutPFiles(interpreter.Host as JGraphScriptGlobals, options.Fields[name]);
                return;
            case "AddInName":
                options.Fields[JgsBuiltinClasses.GivenAddInName] = JgsValue.Str(CliName(value, name, line, col));
                return;
            case "ClassName":
                options.Fields[JgsBuiltinClasses.GivenClass] = JgsValue.Str(ScalarText(value, name, line, col));
                return;
            case "AddInVersion":
                options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));
                return;
            case "DebugBuild" or "EmbedArchive" or "GenerateVisualBasicFile":
                options.Fields[name] = Switch(value, name, ExcelOptionsClassName, line, col);
                return;
            case "AdditionalFiles" or "AutoDetectDataFiles" or "ExternalEncryptionKey" or "ObfuscateArchive" or "SecretsManifest"
                or "SupportPackages" or "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{ExcelOptionsClassName}'.");
        }
    }

    /// <summary>
    /// R2025b's throwPFileWarning, raised by the options' set.FunctionFiles: the P-files among the
    /// files, each on a line (k17_excel: opts.pfile.warn, set.FunctionFiles.6.warn).
    /// </summary>
    private static void WarnAboutPFiles(JGraphScriptGlobals? host, JgsValue files)
    {
        if (host is null || files.Type != JgsType.Cell)
        {
            return;
        }

        var pfiles = files.AsCell.Select(TextOf).Where(static f => f.EndsWith(".p", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pfiles.Count > 0)
        {
            Warn(host, "Compiler:build:shared:cannotAnalyzePFiles",
                "P-files cannot be analyzed for dependent files. Manually add dependent files for:\n\n" + string.Join("\n", pfiles) + "\n");
        }
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.excelAddIn(files | opts, Name, Value, ...)</c>: R2025b's builder
    /// and ExcelAddinWriter over JGraph's mcc, then its postmcc: <c>_install.bat</c> and the
    /// getting-started page added to the files, the DLL first and the separate archive before the page.
    /// </summary>
    private static JgsValue? BuildExcelAddIn(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.ExcelOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == ExcelOptionsClassName)
        {
            if (args.Count > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
            }

            options = args[0].AsObject;
        }
        else if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name.StartsWith("compiler.", StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, "Compiler:clishared:wrongOptionsObject",
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{ExcelOptionsClassName}' instead.");
        }
        else
        {
            options = NewExcelOptions(interpreter, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        string addIn = TextOf(ExcelOptionDerived("AddInName", options));
        string className = TextOf(ExcelOptionDerived("ClassName", options));
        string outputDir = TextOf(ExcelOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        // ExcelAddinWriter: -W excel:name,class,version=v and -T link:lib, -d, the dependency options, -b, -g and -C, then the files.
        var words = new List<string> { "-W", $"excel:{addIn},{className},version={Text("AddInVersion")}", "-T", "link:lib", "-d", outputDir };
        if (On("Verbose"))
        {
            words.Add("-v");
        }

        if (!On("AutoDetectDataFiles"))
        {
            words.Add("-X");
        }

        foreach (JgsValue file in options.Fields["AdditionalFiles"].AsCell)
        {
            words.AddRange(["-a", TextOf(file)]);
        }

        foreach (JgsValue package in options.Fields["SupportPackages"].AsCell)
        {
            words.AddRange(["-Z", TextOf(package)]);
        }

        if (On("ObfuscateArchive"))
        {
            words.AddRange(["-s", "-j"]);
        }

        if (Text("SecretsManifest").Length > 0)
        {
            words.AddRange(["-J", Text("SecretsManifest")]);
        }

        if (!HasNoElements(options.Fields["ExternalEncryptionKey"]))
        {
            Dictionary<string, JgsValue> key = options.Fields["ExternalEncryptionKey"].AsStruct;
            words.AddRange(["-k", $"file={TextOf(key["EncryptionKeyFile"])};loader={TextOf(key["RuntimeKeyLoaderFile"])}"]);
        }

        if (On("GenerateVisualBasicFile"))
        {
            words.Add("-b");
        }

        if (On("DebugBuild"))
        {
            words.Add("-g");
        }

        if (!On("EmbedArchive"))
        {
            words.Add("-C");
        }

        words.AddRange(options.Fields["FunctionFiles"].AsCell.Select(TextOf));
        if (On("Verbose"))
        {
            host.WriteOut(MccCommandString(words) + "\n");
        }

        MccBuild built = Mcc(env, interpreter, host, words, line, col)
            ?? throw new JgsRuntimeException(line, col, "The build printed mcc's help and built nothing.");

        // R2025b's postmcc: the DLL, _install.bat, the .xla and the .bas when there, the archive when
        // separate, then the getting-started page (k17_excel: plain.Files, noembed.Files).
        ProgramVersion version = ProgramVersion.Parse(Text("AddInVersion")) ?? ProgramVersion.Default;
        var files = new List<string>();
        files.AddRange(built.Files.Where(static f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));
        files.Add(ExcelAddIn.WriteInstallBat(folder, ExcelAddIn.DllName(addIn, version)));
        files.AddRange(built.Files.Where(static f => f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)));
        files.AddRange(built.Files.Where(static f => f.EndsWith(".ctf", StringComparison.OrdinalIgnoreCase)));
        files.Add(ExcelAddIn.WriteGettingStarted(folder, addIn));
        if (wanted == 0)
        {
            return null;
        }

        JgsValue results = Results(interpreter, windowed: false, files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str("excelAddIn");
        return results;
    }
}
