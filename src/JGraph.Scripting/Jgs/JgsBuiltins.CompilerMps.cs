using System.Diagnostics;
using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Production Server archives (compiler and SDK plan, stage K14): <c>mcc -W CTF:&lt;name&gt; -U</c> over
/// <see cref="ProductionServerArchive"/>; and <c>compiler.build.productionServerArchive</c> and
/// <c>compiler.build.ProductionServerArchiveOptions</c>, transcribed from R2025b's toolbox/compiler_sdk/cli/build
/// (productionServerArchive.m, ProductionServerArchiveOptions.m, the ProductionServerArchive builder and
/// ProductionServerArchiveWriter.m), their refusals as the fixture <c>k14_production_server_archive</c>
/// and <c>probe_k14_build</c> recorded them.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string ProductionServerOptionsClassName = JgsBuiltinClasses.ProductionServerArchiveOptions;

    private static void RegisterProductionServerBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(ProductionServerOptionsClassName, JgsValue.Function(new BuiltinFunction(ProductionServerOptionsClassName,
            (args, line, col) => JgsValue.Object(NewProductionServerOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string name = "compiler.build.productionServerArchive";
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => BuildProductionServerArchive(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildProductionServerArchive(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));
    }

    // --- mcc -W CTF -------------------------------------------------------------------------

    /// <summary>
    /// <c>mcc -W CTF:&lt;name&gt; -U</c> (stage K14): the archive with every file's function public, its
    /// discovery signatures in the manifest and its routes file inside, and the readme beside it; the
    /// discovery file's functions checked against the archive's as R2025b checks them when it builds
    /// the archive (k14_build: mcc_disc_wrongcount, mcc_disc_nosuchfn).
    /// </summary>
    private static MccBuild ProductionServerBuild(MccCommandLine command, IReadOnlyList<string> exported, string main,
        DependencyReport report, string folder, string here, int line, int col, JGraphScriptGlobals host)
    {
        string name = command.ComponentName!;
        CtfBuild build = CtfLayout.FromReport(name, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.ProductionServerArchive,
            NumericArguments = command.NumericArguments,
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

        if (command.Signatures is { } signatures)
        {
            try
            {
                build = signatures.ApplyTo(build);
            }
            catch (SignatureException fault)
            {
                string ctf = Path.Combine(folder, name + ".ctf");
                throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_CTF_BUILD_FAILED",
                    $"Failed to build CTF file: '{ctf}'. Details: Error: Function signature file \"{command.FunctionSignaturesGiven}\" could not be parsed. {fault.Message}");
            }
        }

        ProductionServerArchiveBuild built;
        try
        {
            built = ProductionServerArchive.Write(build, folder, name, command.Routes, written: path =>
            {
                if (command.Verbose)
                {
                    host.WriteOut($"Generating file \"{path}\".\n");
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The Production Server archive could not be written: {ex.Message}");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, name).Replace('/', '\\'));
        return new MccBuild(built.Files, built.Manifest.Uuid);
    }

    // --- The options -----------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue ProductionServerOptionDefault(string name) => name switch
    {
        "FunctionSignatures" or "RoutesFile" => JgsValue.Str(""),
        _ => CLibraryOptionDefault(name),
    };

    /// <summary>
    /// R2025b's get.ArchiveName (the name given, else the first file's) and OutputDir (the folder given,
    /// else <c>.\&lt;name&gt;productionServerArchive</c>; k14_production_server_archive: name, outdir, given_name).
    /// </summary>
    internal static JgsValue ProductionServerOptionDerived(string name, JgsObject options)
    {
        string archive = TextOf(options.Fields[JgsBuiltinClasses.GivenArchiveName]);
        if (archive.Length == 0 && options.Fields["FunctionFiles"] is { Type: JgsType.Cell } files && files.AsCell.Length > 0)
        {
            archive = Path.GetFileNameWithoutExtension(TextOf(files.AsCell[0]));
        }

        if (name == "ArchiveName")
        {
            return JgsValue.Str(archive);
        }

        string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
        return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(archive) + "productionServerArchive");
    }

    /// <summary>
    /// <c>compiler.build.ProductionServerArchiveOptions(files, Name, Value, ...)</c>: the files set first, then
    /// every other name in the order <c>setdiff</c> leaves them (sorted), as R2025b's constructor does.
    /// </summary>
    private static JgsObject NewProductionServerOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.ProductionServerArchiveOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(ProductionServerOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{ProductionServerOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetProductionServerOption(interpreter, options, "FunctionFiles", args[0], line, col);
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

            given[ParameterName(asked, JgsBuiltinClasses.ProductionServerArchiveOptionNames, line, col)] = args[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetProductionServerOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it.</summary>
    internal static void SetProductionServerOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        bool Resets(JgsValue v) => HasNoElements(v) || (v.IsStringArray && v.ArrayLength == 1 && TextOf(v).Length == 0) || (v.Type == JgsType.String && v.AsString.Length == 0);
        switch (name)
        {
            case "FunctionFiles":
                options.Fields[name] = FunctionFileList(value, here, line, col);
                return;
            case "ArchiveName":
                // cliName: any text but none, a blank allowed, other white space and separators refused (name_*).
                options.Fields[JgsBuiltinClasses.GivenArchiveName] = JgsValue.Str(CliName(value, name, line, col));
                return;
            case "FunctionSignatures":
            {
                // R2025b checks the text under the name 'RoutesFile' (its set method's copy), a file that is
                // there, then the file's shape and its functions against FunctionFiles (sig_*).
                if (Resets(value))
                {
                    options.Fields[name] = JgsValue.Str("");
                    return;
                }

                string file = Absolute(ScalarText(value, "RoutesFile", line, col));
                if (!File.Exists(file))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{file}");
                }

                FunctionSignatures signatures;
                try
                {
                    signatures = FunctionSignatures.Read(file);
                    signatures.Check([.. options.Fields["FunctionFiles"].AsCell.Select(f =>
                    {
                        string path = TextOf(f);
                        DotNetFunction function = DotNetFunctionOf(path);
                        return new CtfFunction(TypedInterfaceReader.FullName(path), Path.GetFileName(path), function.Inputs, function.Outputs);
                    })]);
                }
                catch (SignatureException fault)
                {
                    throw new JgsRuntimeException(line, col, "CompilerSDK:ProductionServerArchiveOptions:validateFunctionSignatureFile",
                        $"Detected the following issue in the function signature file '{file}':\n\n{fault.Message}");
                }

                options.Fields[name] = JgsValue.Str(file);
                if (signatures.Warnings.Count > 0 && interpreter.Host is { } host)
                {
                    Warn(host, "CompilerSDK:ProductionServerArchiveOptions:validateFunctionSignatureFile",
                        $"Detected the following issue in the function signature file '{file}':\n\n{string.Join("\n", signatures.Warnings)}\n");
                }

                return;
            }

            case "RoutesFile":
            {
                // A file that is there; what it holds is read when the archive is built (routes_*).
                if (Resets(value))
                {
                    options.Fields[name] = JgsValue.Str("");
                    return;
                }

                string file = Absolute(ScalarText(value, name, line, col));
                if (!File.Exists(file))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{file}");
                }

                options.Fields[name] = JgsValue.Str(file);
                return;
            }

            case "AdditionalFiles" or "AutoDetectDataFiles" or "ExternalEncryptionKey" or "ObfuscateArchive" or "SecretsManifest"
                or "SupportPackages" or "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{ProductionServerOptionsClassName}'.");
        }
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.productionServerArchive(files | opts, Name, Value, ...)</c>: R2025b's builder
    /// and ProductionServerArchiveWriter over JGraph's mcc (k14_build: main.*).
    /// </summary>
    private static JgsValue? BuildProductionServerArchive(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.ProductionServerArchiveOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == ProductionServerOptionsClassName)
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
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{ProductionServerOptionsClassName}' instead.");
        }
        else
        {
            options = NewProductionServerOptions(interpreter, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        string archive = TextOf(ProductionServerOptionDerived("ArchiveName", options));
        string outputDir = TextOf(ProductionServerOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        // ProductionServerArchiveWriter: -W CTF:name[,DISCOVERY:file][,ROUTES:file], -d, -v, the dependency
        // options, -U, then the files (k14_build: main.printed).
        string target = "CTF:" + archive;
        if (Text("FunctionSignatures").Length > 0)
        {
            target += ",DISCOVERY:" + Text("FunctionSignatures");
        }

        if (Text("RoutesFile").Length > 0)
        {
            target += ",ROUTES:" + Text("RoutesFile");
        }

        var words = new List<string> { "-W", target, "-d", outputDir };
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

        words.Add("-U");
        words.AddRange(options.Fields["FunctionFiles"].AsCell.Select(TextOf));
        if (On("Verbose"))
        {
            host.WriteOut(MccCommandString(words) + "\n");
        }

        MccBuild built = Mcc(env, interpreter, host, words, line, col)
            ?? throw new JgsRuntimeException(line, col, "The build printed mcc's help and built nothing.");
        if (wanted == 0)
        {
            return null;
        }

        JgsValue results = Results(interpreter, windowed: false, built.Files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str("productionServerArchive");
        return results;
    }
}
