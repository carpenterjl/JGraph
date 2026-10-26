using System.Diagnostics;
using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Python packages (compiler and SDK plan, stage K13): <c>mcc -W python:</c> over
/// <see cref="PythonPackage"/>; and <c>compiler.build.pythonPackage</c> and
/// <c>compiler.build.PythonPackageOptions</c>, transcribed from R2025b's toolbox/compiler_sdk/cli/build
/// (pythonPackage.m, PythonPackageOptions.m, the PythonPackage builder and PythonWriter.m), their
/// refusals as <c>probe_k13_build</c> recorded them.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string PythonPackageOptionsClassName = JgsBuiltinClasses.PythonPackageOptions;

    private static readonly string[] SampleExtensions = [".m", ".mlx"];

    private static void RegisterPythonPackageBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(PythonPackageOptionsClassName, JgsValue.Function(new BuiltinFunction(PythonPackageOptionsClassName,
            (args, line, col) => JgsValue.Object(NewPythonPackageOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string name = "compiler.build.pythonPackage";
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => BuildPythonPackage(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildPythonPackage(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));
    }

    // --- mcc -W python ---------------------------------------------------------------------

    /// <summary>
    /// <c>mcc -W python:&lt;name&gt;</c> (stage K13): the archive with every file's function or class
    /// public by its package-qualified name, and what <see cref="PythonPackage"/> writes around it;
    /// under <c>-v</c>, R2025b's line for each file it generates (k13_build: main.printed, mcc_verbose).
    /// </summary>
    private static MccBuild PythonPackageBuild(MccCommandLine command, IReadOnlyList<string> exported, string main,
        DependencyReport report, string folder, string here, int line, int col, JGraphScriptGlobals host)
    {
        string full = command.ComponentName!;
        string name = PythonPackage.ShortName(full);
        CtfBuild build = CtfLayout.FromReport(name, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.PythonPackage,
            RuntimeOptions = command.RuntimeOptions,
            MainFunction = null,
            PublicFunctions =
            [
                .. exported.Distinct(StringComparer.OrdinalIgnoreCase).Select(file =>
                {
                    DotNetFunction f = DotNetFunctionOf(file);
                    return new CtfFunction(TypedInterfaceReader.FullName(file), CtfLayout.ArchivePath(file, here, name), f.Inputs, f.Outputs);
                }),
            ],
        };
        if (command.PCode)
        {
            build = JgsPcode.PcodeBuild(build);
        }

        PythonPackageBuild built;
        try
        {
            built = PythonPackage.Write(build, folder, full, written: path =>
            {
                if (command.Verbose)
                {
                    host.WriteOut($"Generating file \"{path}\".\n");
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The Python package could not be written: {ex.Message}");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, name).Replace('/', '\\'));
        return new MccBuild(built.Files, built.Manifest.Uuid);
    }

    // --- The options -----------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue PythonPackageOptionDefault(string name) => name switch
    {
        "SampleGenerationFiles" or "AdditionalFiles" => EmptyCellValue(),
        _ => CLibraryOptionDefault(name),
    };

    /// <summary>
    /// R2025b's get.PackageName (the name given, else <c>example.&lt;first file&gt;</c>) and OutputDir (the
    /// folder given, else <c>.\&lt;getName&gt;pythonPackage</c>, getName the name's last part; k13_build:
    /// opts.PackageName, opts.OutputDir, opts.dotted.OutputDir).
    /// </summary>
    internal static JgsValue PythonPackageOptionDerived(string name, JgsObject options)
    {
        string package = TextOf(options.Fields[JgsBuiltinClasses.GivenPackageName]);
        if (package.Length == 0 && options.Fields["FunctionFiles"] is { Type: JgsType.Cell } files && files.AsCell.Length > 0)
        {
            package = "example." + Path.GetFileNameWithoutExtension(TextOf(files.AsCell[0]));
        }

        if (name == "PackageName")
        {
            return JgsValue.Str(package);
        }

        string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
        return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(PythonPackageShortName(package)) + "pythonPackage");
    }

    /// <summary>R2025b's hidden getName: the package name's last dotted part.</summary>
    internal static string PythonPackageShortName(string package) => package.Split('.')[^1];

    /// <summary>
    /// <c>compiler.build.PythonPackageOptions(files, Name, Value, ...)</c>: the files set first, then every
    /// other name in the order <c>setdiff</c> leaves them (sorted), as R2025b's constructor does.
    /// </summary>
    private static JgsObject NewPythonPackageOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.PythonPackageOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(PythonPackageOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{PythonPackageOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetPythonPackageOption(interpreter, options, "FunctionFiles", args[0], line, col);
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

            given[ParameterName(asked, JgsBuiltinClasses.PythonPackageOptionNames, line, col)] = args[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetPythonPackageOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it.</summary>
    internal static void SetPythonPackageOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        switch (name)
        {
            case "FunctionFiles":
                // fileList: files only, a folder a file not found (k13_build: folder).
                options.Fields[name] = FunctionFileList(value, here, line, col);
                return;
            case "PackageName":
                // cliName: any text but none; a dotted name nests the package (k13_build: opts.name.*).
                options.Fields[JgsBuiltinClasses.GivenPackageName] = JgsValue.Str(CliName(value, name, line, col));
                return;
            case "SampleGenerationFiles":
            {
                // setSampleGenerationFiles: files that are there, of a sample's extension, absolute (opts.samples.*).
                if (HasNoElements(value) || (value.IsStringArray && TextOf(value).Length == 0))
                {
                    options.Fields[name] = EmptyCellValue();
                    return;
                }

                string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
                string[] files = value.Type == JgsType.Cell ? [.. value.AsCell.Select(TextOf)] : value.IsStringArray ? [.. TextElementsOf(value) ?? []] : [TextOf(value)];
                foreach (string file in files)
                {
                    if (!File.Exists(Absolute(file)))
                    {
                        throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{file}");
                    }

                    if (!SampleExtensions.Contains(Path.GetExtension(file), StringComparer.Ordinal))
                    {
                        throw new JgsRuntimeException(line, col, "Compiler:clishared:badExtension",
                            $"Expected '{name}' to have one of the following extensions:\n\n{string.Join("\n", SampleExtensions)}");
                    }
                }

                options.Fields[name] = OptionCellColumn([.. files.Select(Absolute)]);
                return;
            }

            case "AdditionalFiles" or "AutoDetectDataFiles" or "ExternalEncryptionKey" or "ObfuscateArchive" or "SecretsManifest"
                or "SupportPackages" or "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{PythonPackageOptionsClassName}'.");
        }
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.pythonPackage(files | opts, Name, Value, ...)</c>: R2025b's builder and
    /// PythonWriter over JGraph's mcc, then the getting-started page (k13_build: main.*).
    /// </summary>
    private static JgsValue? BuildPythonPackage(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.PythonPackageOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == PythonPackageOptionsClassName)
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
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{PythonPackageOptionsClassName}' instead.");
        }
        else
        {
            options = NewPythonPackageOptions(interpreter, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        string package = TextOf(PythonPackageOptionDerived("PackageName", options));
        string outputDir = TextOf(PythonPackageOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        // PythonWriter: -W python:name and -T link:lib, -d, the dependency options, then the files.
        var words = new List<string> { "-W", "python:" + package, "-T", "link:lib", "-d", outputDir };
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

        foreach (JgsValue supportPackage in options.Fields["SupportPackages"].AsCell)
        {
            words.AddRange(["-Z", TextOf(supportPackage)]);
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

        words.AddRange(options.Fields["FunctionFiles"].AsCell.Select(TextOf));
        if (On("Verbose"))
        {
            host.WriteOut(MccCommandString(words) + "\n");
        }

        MccBuild built = Mcc(env, interpreter, host, words, line, col)
            ?? throw new JgsRuntimeException(line, col, "The build printed mcc's help and built nothing.");

        // compiler.build adds the getting-started page, last among the files (k13_build: main.Files).
        built = built with { Files = [.. built.Files, PythonPackage.WriteGettingStarted(folder, package)] };
        if (wanted == 0)
        {
            return null;
        }

        JgsValue results = Results(interpreter, windowed: false, built.Files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str("pythonPackage");
        return results;
    }
}
