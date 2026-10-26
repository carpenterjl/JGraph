using System.Diagnostics;
using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// C++ shared libraries for the MATLAB Data API (compiler and SDK plan, stage K12): <c>mcc -W
/// cpplib:</c> over <see cref="CppSharedLibrary"/> and <see cref="TypedInterfaceCpp"/>; and
/// <c>compiler.build.cppSharedLibrary</c> and <c>compiler.build.CppSharedLibraryOptions</c>,
/// transcribed from R2025b's toolbox/compiler_sdk/cli/build (cppSharedLibrary.m,
/// CppSharedLibraryOptions.m, the CppSharedLibrary builder and CppLibWriter.m) over
/// SharedLibraryOptions and InterfaceManager, their refusals as <c>probe_k12_build</c> recorded them.
/// The legacy mwArray interface is not built (decision KD7).
/// </summary>
internal static partial class JgsBuiltins
{
    private const string CppLibraryOptionsClassName = JgsBuiltinClasses.CppLibraryOptions;

    /// <summary>MATLAB's own classes, which R2025b's generator passes as arrays (CPPTypeConverter.isMathWorksRestricted).</summary>
    private static readonly HashSet<string> MatlabClassNames = new(StringComparer.Ordinal)
    {
        "double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "logical", "char", "string",
        "cell", "struct", "function_handle", "table", "timetable", "datetime", "duration", "calendarDuration", "categorical",
        "dictionary", "containers.Map", "handle", "missing",
    };

    private static void RegisterCppLibraryBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(CppLibraryOptionsClassName, JgsValue.Function(new BuiltinFunction(CppLibraryOptionsClassName,
            (args, line, col) => JgsValue.Object(NewCppLibraryOptions(interpreter, host, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string name = "compiler.build.cppSharedLibrary";
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => BuildCppLibrary(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildCppLibrary(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));
    }

    // --- mcc -W cpplib -----------------------------------------------------------------------

    /// <summary>
    /// The strongly typed header for <c>-W cpplib:name,generic</c>: every file's function or class as
    /// R2025b's generator reads it, the count line under <c>-v</c> (k12_build: main.printed).
    /// </summary>
    private static string CppHeader(MccCommandLine command, IReadOnlyList<string> exported, string folder, JgsEnvironment env,
        Interpreter interpreter, JGraphScriptGlobals host, int line, int col)
    {
        (List<TypedFunction> functions, List<TypedClass> classes) = TypedInterfaceReader.Read(exported);
        string file = Path.Combine(folder, CppSharedLibrary.HeaderName(command.ComponentName!));
        CppInterface generated;
        try
        {
            generated = TypedInterfaceCpp.Generate(functions, classes, file, DateTime.Now, CppClassFactsOf(classes, env, interpreter, host),
                RuntimeLayout.CurrentVersion);
        }
        catch (TypedInterfaceException failed)
        {
            throw new JgsRuntimeException(line, col, failed.Identifier, failed.Message);
        }

        foreach ((string identifier, string text) in generated.Warnings)
        {
            Warn(host, identifier, text);
        }

        if (command.Verbose)
        {
            host.WriteOut($"{generated.Classes} class(es) and {generated.Functions} function(s) written to {file}\n\n");
        }

        return generated.Text;
    }

    /// <summary>
    /// What a class an argument or property names is, as R2025b's generator learns it from <c>which</c>:
    /// one of the build's own, MATLAB's own, or one of the user's found on the path.
    /// </summary>
    private static Func<string, CppClassFacts> CppClassFactsOf(IReadOnlyList<TypedClass> built, JgsEnvironment env, Interpreter interpreter,
        JGraphScriptGlobals host)
    {
        var own = new Dictionary<string, TypedClass>(StringComparer.Ordinal);
        foreach (TypedClass cls in built)
        {
            own.TryAdd(cls.FullName, cls);
        }

        var found = new Dictionary<string, CppClassFacts>(StringComparer.Ordinal);
        return name =>
        {
            if (own.TryGetValue(name, out TypedClass? cls))
            {
                return new CppClassFacts(Restricted: false, External: true, cls.Enumeration.Count > 0, cls.IsHandle);
            }

            if (found.TryGetValue(name, out CppClassFacts known))
            {
                return known;
            }

            CppClassFacts facts = default;
            if (MatlabClassNames.Contains(name) || name.StartsWith("matlab.", StringComparison.Ordinal) || IsBuiltinName(env, name))
            {
                facts = new CppClassFacts(Restricted: true, External: false, Enumeration: false, Handle: name == "handle");
            }
            else if (DependencyRoot(name.Replace('.', Path.DirectorySeparatorChar), interpreter, host) is { } file && File.Exists(file)
                && TypedInterfaceReader.Read([file]).Classes is [TypedClass other, ..])
            {
                facts = new CppClassFacts(Restricted: false, External: true, other.Enumeration.Count > 0, other.IsHandle);
            }

            found[name] = facts;
            return facts;
        };
    }

    /// <summary>
    /// <c>mcc -W cpplib:&lt;name&gt;</c> (stage K12): the archive with every file's function or class
    /// public by its package-qualified name, and what <see cref="CppSharedLibrary"/> writes beside it.
    /// </summary>
    private static MccBuild CppLibraryBuild(MccCommandLine command, IReadOnlyList<string> exported, string? header, string main,
        DependencyReport report, string folder, string here, int line, int col, JGraphScriptGlobals host)
    {
        string name = command.ComponentName!;
        CtfBuild build = CtfLayout.FromReport(name, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.CppSharedLibrary,
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

        CppSharedLibraryBuild built;
        try
        {
            built = CppSharedLibrary.Write(build, new CppSharedLibraryOptions
            {
                OutputFolder = folder,
                LibraryName = name,
                Header = header,
                LegacyAsked = command.CppInterface == CppInterfaceKind.All,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The C++ shared library could not be written: {ex.Message}");
        }

        if (command.Verbose)
        {
            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
            host.WriteOut($"Generating file \"{Path.Combine(CppSharedLibrary.InterfaceFolder(folder), "readme.txt")}\".\n");
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, name).Replace('/', '\\'));
        return new MccBuild(built.Files, built.Manifest.Uuid);
    }

    // --- The options -----------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's classes declare it.</summary>
    internal static JgsValue CppLibraryOptionDefault(string name) => name switch
    {
        "SampleGenerationFiles" or "AdditionalFiles" => EmptyCellValue(),
        "Interface" => JgsValue.Str("matlab-data"),
        "LibraryVersion" => JgsValue.Str(""),
        _ => CLibraryOptionDefault(name),
    };

    /// <summary>
    /// The derived properties: the interface and its version as the interface manager keeps them, the
    /// library's name, and the output folder (<c>.\&lt;name&gt;cppSharedLibrary</c> when none was given).
    /// </summary>
    internal static JgsValue CppLibraryOptionDerived(string name, JgsObject options)
    {
        switch (name)
        {
            case "Interface":
                return options.Fields[JgsBuiltinClasses.InterfaceValue];
            case "LibraryVersion":
                return options.Fields[JgsBuiltinClasses.InterfaceVersion];
        }

        string library = TextOf(CLibraryOptionDerived("LibraryName", options));
        if (name == "LibraryName")
        {
            return JgsValue.Str(library);
        }

        string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
        return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(library) + "cppSharedLibrary");
    }

    /// <summary>
    /// <c>compiler.build.CppSharedLibraryOptions(files, Name, Value, ...)</c>: the files set first, then
    /// every other name in the order <c>setdiff</c> leaves them (sorted), as SharedLibraryOptions does.
    /// </summary>
    private static JgsObject NewCppLibraryOptions(Interpreter interpreter, JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.CppLibraryOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(CppLibraryOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{CppLibraryOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetCppLibraryOption(interpreter, host, options, "FunctionFiles", args[0], line, col);
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

            given[ParameterName(asked, JgsBuiltinClasses.CppLibraryOptionNames, line, col)] = args[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetCppLibraryOption(interpreter, host, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it.</summary>
    internal static void SetCppLibraryOption(Interpreter interpreter, JGraphScriptGlobals? host, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        bool Mwarray() => TextOf(options.Fields[JgsBuiltinClasses.InterfaceValue]) == "mwarray";
        void FoldersAllowed(JgsValue files)
        {
            // InterfaceManager.checkIfFoldersAreAllowed (k12_build: opts.folder.mwarray, opts.folder.then.mwarray).
            if (Mwarray() && files.Type == JgsType.Cell && files.AsCell.Any(f => Directory.Exists(TextOf(f))))
            {
                throw new JgsRuntimeException(line, col, "Compiler:build:shared:folderOnlyForMATLABData",
                    "Inputs may only contain folders when \"Interface\" is set to 'matlab-data'.");
            }
        }

        void VersionWarning()
        {
            if (host is not null && !Mwarray() && TextOf(options.Fields[JgsBuiltinClasses.InterfaceVersion]).Length > 0)
            {
                Warn(host, "Compiler:build:shared:badInterfaceVersionPair",
                    "A non-empty \"LibraryVersion\" will be ignored when using an \"Interface\" value of 'matlab-data'. To avoid this warning, "
                    + "either set \"LibraryVersion\" to an empty value, or use 'mwarray' for the \"Interface\" property.");
            }
        }

        switch (name)
        {
            case "FunctionFiles":
            {
                // validateFunctionFiles: R2025b's itemList, folders allowed, a code file's extension.
                JgsValue files = ItemList(value, name, here, line, col);
                foreach (JgsValue file in files.AsCell)
                {
                    if (!AppFileExtensions.Contains(Path.GetExtension(TextOf(file)), StringComparer.Ordinal) && !Directory.Exists(TextOf(file)))
                    {
                        throw new JgsRuntimeException(line, col, "Compiler:clishared:badExtension",
                            $"Expected '{name}' to have one of the following extensions:\n\n{string.Join("\n", AppFileExtensions)}");
                    }
                }

                FoldersAllowed(files);
                options.Fields[name] = files;
                return;
            }

            case "LibraryName":
                options.Fields[JgsBuiltinClasses.GivenLibraryName] = JgsValue.Str(CliName(value, name, line, col));
                return;
            case "LibraryVersion":
            {
                // InterfaceManager.set.Version: text, an empty one the default.
                string text = ScalarTextOnly(value, name, line, col);
                options.Fields[JgsBuiltinClasses.InterfaceVersion] = JgsValue.Str(text);
                VersionWarning();
                return;
            }

            case "Interface":
            {
                string text = ScalarText(value, name, line, col);
                string? member = new[] { "matlab-data", "mwarray" }.FirstOrDefault(v => v.Equals(text, StringComparison.OrdinalIgnoreCase));
                if (member is null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeMember", "Value must be a member of this set:\n    'matlab-data'\n    'mwarray'\n");
                }

                options.Fields[JgsBuiltinClasses.InterfaceValue] = JgsValue.Str(member);
                VersionWarning();
                FoldersAllowed(options.Fields["FunctionFiles"]);
                return;
            }

            case "SampleGenerationFiles":
            {
                // setSampleGenerationFiles: files that are there, absolute (k12_build: opts.samples.bad).
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
                }

                options.Fields[name] = OptionCellColumn([.. files.Select(Absolute)]);
                return;
            }

            case "DebugBuild":
                options.Fields[name] = Switch(value, name, "compiler.internal.build.SharedLibraryOptions", line, col);
                return;
            case "AdditionalFiles" or "AutoDetectDataFiles" or "ExternalEncryptionKey" or "ObfuscateArchive" or "SecretsManifest"
                or "SupportPackages" or "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{CppLibraryOptionsClassName}'.");
        }
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.cppSharedLibrary(files | opts, Name, Value, ...)</c>: R2025b's builder
    /// and CppLibWriter over JGraph's mcc, then the getting-started page.
    /// </summary>
    private static JgsValue? BuildCppLibrary(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.CppLibraryOptionNames.Length) + 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == CppLibraryOptionsClassName)
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
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{CppLibraryOptionsClassName}' instead.");
        }
        else
        {
            options = NewCppLibraryOptions(interpreter, host, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        if (Text(JgsBuiltinClasses.InterfaceValue) == "mwarray")
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:targetOutOfScope",
                "JGraph does not build the legacy mwArray interface of a C++ shared library (Interface 'mwarray'); "
                + "the 'matlab-data' interface builds its MATLAB Data API interface.");
        }

        string library = TextOf(CppLibraryOptionDerived("LibraryName", options));
        string outputDir = TextOf(CppLibraryOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        // CppLibWriter: -W cpplib:name,generic[,version=v] and -T link:lib, -d, the dependency options, -g, then the files.
        string wrapper = $"cpplib:{library},generic";
        if (Text(JgsBuiltinClasses.InterfaceVersion).Length > 0)
        {
            wrapper += ",version=" + Text(JgsBuiltinClasses.InterfaceVersion);
        }

        var words = new List<string> { "-W", wrapper, "-T", "link:lib", "-d", outputDir };
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

        if (On("DebugBuild"))
        {
            words.Add("-g");
        }

        words.AddRange(options.Fields["FunctionFiles"].AsCell.Select(TextOf));
        if (On("Verbose"))
        {
            host.WriteOut(MccCommandString(words) + "\n");
        }

        MccBuild built = Mcc(env, interpreter, host, words, line, col)
            ?? throw new JgsRuntimeException(line, col, "The build printed mcc's help and built nothing.");

        // compiler.build adds the getting-started page, last among the files (k12_build: main.Files).
        built = built with { Files = [.. built.Files, CppSharedLibrary.WriteGettingStarted(folder, library)] };
        if (wanted == 0)
        {
            return null;
        }

        JgsValue results = Results(interpreter, windowed: false, built.Files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str("cppSharedLibrary");
        return results;
    }
}
