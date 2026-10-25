using System.Diagnostics;
using JGraph.Deploy;
using JGraph.Scripting.Jgs.Native;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// C shared libraries (compiler and SDK plan, stage K11): <c>mcc -W lib:</c> and <c>-l</c> over
/// <see cref="CSharedLibrary"/>, compiled with the MSVC <c>loadlibrary</c>'s discovery finds
/// (<see cref="CCompilers"/>); and <c>compiler.build.cSharedLibrary</c> and
/// <c>compiler.build.CSharedLibraryOptions</c>, transcribed from R2025b's toolbox/compiler_sdk/cli/build
/// (cSharedLibrary.m, CSharedLibraryOptions.m, SharedLibraryOptions.m, the CSharedLibrary builder and
/// CLibWriter.m), their refusals as <c>probe_k11_build</c> recorded them.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string CLibraryOptionsClassName = JgsBuiltinClasses.CLibraryOptions;

    /// <summary>R2025b's identifier when the library's compiler cannot be run or fails (k11_nocompiler, k11_build: clash).</summary>
    private const string CompilerFailedIdentifier = "Compiler:compiler:COM_SYSTEM_ERROR";

    private static void RegisterCLibraryBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(CLibraryOptionsClassName, JgsValue.Function(new BuiltinFunction(CLibraryOptionsClassName,
            (args, line, col) => JgsValue.Object(NewCLibraryOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string name = "compiler.build.cSharedLibrary";
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => BuildCLibrary(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildCLibrary(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));
    }

    // --- mcc -W lib --------------------------------------------------------------------------

    /// <summary>
    /// <c>mcc -W lib:&lt;name&gt;</c> and <c>-l</c> (stage K11): the archive with every file's function
    /// public, and the library <see cref="CSharedLibrary"/> writes, named by <c>-W</c>, <c>-o</c> or the
    /// first file, its compiler found as <c>loadlibrary</c> finds one.
    /// </summary>
    private static MccBuild CLibraryBuild(MccCommandLine command, IReadOnlyList<string> exported, string main, DependencyReport report,
        string folder, string here, IReadOnlyList<string> words, int line, int col, JGraphScriptGlobals host)
    {
        string name = command.ComponentName ?? command.OutputName ?? Path.GetFileNameWithoutExtension(main);
        if (!MccCommandLine.IsOutputName(name))
        {
            throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_ERR_ILLEGAL_OUTPUT_NAME",
                $"Illegal output name: '{name}' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')");
        }

        List<CLibraryFunction> functions = [.. exported.Select(CLibraryFunctionOf)];
        CtfBuild build = CtfLayout.FromReport(name, main, report, rootFolder: here) with
        {
            TargetType = CtfTargetType.CSharedLibrary,
            RuntimeOptions = command.RuntimeOptions,
            MainFunction = null,
            PublicFunctions = [.. functions.Select(f => new CtfFunction(f.Name, CtfLayout.ArchivePath(f.File, here, name), f.Inputs, f.Outputs))],
        };
        if (command.PCode)
        {
            build = JgsPcode.PcodeBuild(build);
        }

        CLibraryStage stage = command.LibraryStage;
        CToolchain? toolchain = null;
        if (stage != CLibraryStage.Codegen)
        {
            // R2025b's mbuild fails when it finds no compiler, after the source is generated, and leaves
            // the .def (k11_nocompiler); JGraph looks first and writes nothing.
            CCompiler compiler = (OperatingSystem.IsWindows() ? CCompilers.SelectedMsvc() : null)
                ?? throw new JgsRuntimeException(line, col, CompilerFailedIdentifier,
                    "No supported C compiler was found: JGraph builds a C shared library with Microsoft Visual C++ (Visual Studio 2019 "
                    + "or later, with its C++ tools for x64), and found none. Install it, or build with -T codegen to write the library's "
                    + "source and archive alone.");
            toolchain = new CToolchain(compiler.Name, (tool, arguments, at) =>
                OperatingSystem.IsWindows() ? CCompilers.RunTool(compiler, tool, arguments, at) : (1, "C libraries are built on Windows."));
        }

        if (command.Verbose)
        {
            foreach (string file in new[] { name + ".h", name + ".c", name + ".exports", "readme.txt" })
            {
                host.WriteOut($"Generating file \"{Path.Combine(folder, file)}\".\n");
            }
        }

        if (toolchain is not null)
        {
            host.WriteOut($"Building with '{toolchain.Name}'. \n");
        }

        CSharedLibraryBuild built;
        try
        {
            built = CSharedLibrary.Write(build, new CSharedLibraryOptions
            {
                OutputFolder = folder,
                LibraryName = name,
                Functions = functions,
                Version = command.Version,
                EmbedArchive = !command.SeparateArchive,
                Debug = command.DebugBuild,
                Stage = stage,
                MccArguments = words,
                Toolchain = toolchain,
            });
        }
        catch (CLibraryBuildException failed)
        {
            if (command.Verbose)
            {
                host.WriteOut(failed.Transcript);
            }

            throw new JgsRuntimeException(line, col, CompilerFailedIdentifier, failed.Message);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The C shared library could not be written: {ex.Message}");
        }

        if (command.Verbose)
        {
            host.WriteOut(built.Transcript);
        }

        BuildLogs.WriteAll(folder, report, file => CtfLayout.ArchivePath(file, here, name).Replace('/', '\\'));
        return new MccBuild(built.Files, built.Manifest.Uuid);
    }

    /// <summary>
    /// What a file wraps as: a function's inputs and outputs as it declares them (<c>~</c> kept), a
    /// script none, a class its constructor's any number in and out (k11_build: script, classdef); a
    /// P-file's by their places, <c>i0</c>, <c>o0</c> and on, its names being hidden (k11_build: pfile).
    /// </summary>
    private static CLibraryFunction CLibraryFunctionOf(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        bool pcode = Path.GetExtension(file).Equals(".p", StringComparison.OrdinalIgnoreCase);
        try
        {
            IReadOnlyList<Stmt> parsed = pcode ? JgsPcode.Load(file) : Parser.Parse(File.ReadAllText(file), file, JgsDialect.Matlab);
            if (parsed.OfType<ClassdefStmt>().Any())
            {
                return new CLibraryFunction(name, ["varargin"], ["varargout"], file);
            }

            if (parsed.OfType<FnStmt>().FirstOrDefault() is { } declared)
            {
                return pcode
                    ? new CLibraryFunction(name, Placed(declared.Parameters, "i", "varargin"), Placed(declared.Outputs, "o", "varargout"), file)
                    : new CLibraryFunction(name, declared.Parameters, declared.Outputs, file);
            }
        }
        catch (Exception ex) when (ex is JgsException or IOException or InvalidDataException)
        {
            // A file that does not parse is refused when it is called; its wrappers take no arguments.
        }

        return new CLibraryFunction(name, [], [], file);
    }

    /// <summary>Arguments named by their places (<c>i0</c>, <c>i1</c>), a trailing varargin or varargout kept.</summary>
    private static List<string> Placed(IReadOnlyList<string> names, string prefix, string variable) =>
        [.. names.Select((n, k) => k == names.Count - 1 && n == variable ? n : prefix + k.ToString(System.Globalization.CultureInfo.InvariantCulture))];

    // --- The options -----------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's classes declare it.</summary>
    internal static JgsValue CLibraryOptionDefault(string name) => name switch
    {
        "EmbedArchive" or "AutoDetectDataFiles" => OnOffSwitchStateValue.Of(true),
        "DebugBuild" or "ObfuscateArchive" or "Verbose" => OnOffSwitchStateValue.Of(false),
        "LibraryVersion" => JgsValue.Str("1.0.0.0"),
        "SecretsManifest" => JgsValue.Str(""),
        "AdditionalFiles" => EmptyCellValue(),
        "ExternalEncryptionKey" => EmptyEncryptionKey(),
        "SupportPackages" => OptionCellColumn(["autodetect"]),
        _ => JgsMatrix.FromColumnMajor([], 0, 0),
    };

    /// <summary>
    /// R2025b's get.LibraryName (the name given, or the first file's made a name by
    /// defaultOutputFilename) and OutputDir (the folder given, or <c>.\&lt;name&gt;cSharedLibrary</c>).
    /// </summary>
    internal static JgsValue CLibraryOptionDerived(string name, JgsObject options)
    {
        string library = TextOf(options.Fields[JgsBuiltinClasses.GivenLibraryName]);
        if (library.Length == 0 && options.Fields["FunctionFiles"] is { Type: JgsType.Cell } files && files.AsCell.Length > 0)
        {
            library = DefaultOutputFilename(Path.GetFileNameWithoutExtension(TextOf(files.AsCell[0])));
        }

        if (name == "LibraryName")
        {
            return JgsValue.Str(library);
        }

        string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
        return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(library) + "cSharedLibrary");
    }

    /// <summary>
    /// <c>compiler.build.CSharedLibraryOptions(files, Name, Value, ...)</c>: the files set first, then
    /// every other name in the order <c>setdiff</c> leaves them (sorted), as SharedLibraryOptions does.
    /// </summary>
    private static JgsObject NewCLibraryOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.CLibraryOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(CLibraryOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{CLibraryOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetCLibraryOption(interpreter, options, "FunctionFiles", args[0], line, col);
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

            given[ParameterName(asked, JgsBuiltinClasses.CLibraryOptionNames, line, col)] = args[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetCLibraryOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it.</summary>
    internal static void SetCLibraryOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        switch (name)
        {
            case "FunctionFiles":
                options.Fields[name] = FunctionFileList(value, here, line, col);
                return;
            case "LibraryName":
                options.Fields[JgsBuiltinClasses.GivenLibraryName] = JgsValue.Str(CliName(value, name, line, col));
                return;
            case "LibraryVersion":
                options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));
                return;
            case "EmbedArchive":
                options.Fields[name] = Switch(value, name, CLibraryOptionsClassName, line, col);
                return;
            case "DebugBuild":
                options.Fields[name] = Switch(value, name, "compiler.internal.build.SharedLibraryOptions", line, col);
                return;
            case "AdditionalFiles" or "AutoDetectDataFiles" or "ExternalEncryptionKey" or "ObfuscateArchive" or "SecretsManifest"
                or "SupportPackages" or "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{CLibraryOptionsClassName}'.");
        }
    }

    /// <summary>
    /// R2025b's fileList for <c>FunctionFiles</c>: text or a cell of it, nonempty, every file there and
    /// of a code file's extension, as absolute paths in a column (k11_build: opts.*, set.FunctionFiles.*).
    /// </summary>
    private static JgsValue FunctionFileList(JgsValue value, string here, int line, int col)
    {
        const string name = "FunctionFiles";
        if (!(IsTextValue(value) || value.Type == JgsType.Cell))
        {
            throw InvalidType(name, ["char", "string", "cell"], value, line, col);
        }

        if (HasNoElements(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedNonempty", $"Expected {name} to be nonempty.");
        }

        string[] items = value.Type == JgsType.Cell ? [.. value.AsCell.Select(TextOf)]
            : value.IsStringArray ? [.. TextElementsOf(value) ?? []]
            : [TextOf(value)];
        var paths = new List<string>(items.Length);
        foreach (string item in items)
        {
            string absolute = Path.GetFullPath(Path.IsPathRooted(item) ? item : Path.Combine(here, item));
            if (!File.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{item}");
            }

            if (!AppFileExtensions.Contains(Path.GetExtension(absolute), StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(line, col, "Compiler:clishared:badExtension",
                    $"Expected '{name}' to have one of the following extensions:\n\n{string.Join("\n", AppFileExtensions)}");
            }

            paths.Add(absolute);
        }

        return OptionCellColumn(paths);
    }

    /// <summary>R2025b's cliName: text, not empty, a scalar, no bad folder character, no white space but a blank, no separator.</summary>
    private static string CliName(JgsValue value, string name, int line, int col)
    {
        if (!IsTextValue(value))
        {
            throw InvalidType(name, ["char", "string"], value, line, col);
        }

        NonzeroLength(value, line, col);
        if (!(IsTextScalar(value) && !value.IsCharMatrix))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar", "Value must be a character vector or string scalar.");
        }

        string text = TextOf(value);
        NoBadFolderCharacters(text, name, path: false, line, col);
        if (text.Any(c => char.IsWhiteSpace(c) && c != ' '))
        {
            throw new JgsRuntimeException(line, col, "Compiler:clishared:noWhiteSpaceInProperty", $"'{name}' cannot contain any white space characters.");
        }

        if (text.IndexOfAny(['/', '\\']) >= 0)
        {
            throw BadProperty(name, text, line, col);
        }

        return text;
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.cSharedLibrary(files | opts, Name, Value, ...)</c>: R2025b's builder
    /// and CLibWriter over JGraph's mcc, then the getting-started page.
    /// </summary>
    private static JgsValue? BuildCLibrary(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.CLibraryOptionNames.Length) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == CLibraryOptionsClassName)
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
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{CLibraryOptionsClassName}' instead.");
        }
        else
        {
            options = NewCLibraryOptions(interpreter, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        string library = TextOf(CLibraryOptionDerived("LibraryName", options));
        string outputDir = TextOf(CLibraryOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        // CLibWriter: -W lib:name,version=v and -T link:lib, -d, the dependency options, -g and -C, then the files.
        var words = new List<string> { "-W", $"lib:{library},version={Text("LibraryVersion")}", "-T", "link:lib", "-d", outputDir };
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

        // compiler.build adds the getting-started page, last among the files (k11_build: api_default.Files).
        built = built with { Files = [.. built.Files, CSharedLibrary.WriteGettingStarted(folder, library)] };
        if (wanted == 0)
        {
            return null;
        }

        JgsValue results = Results(interpreter, windowed: false, built.Files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str("cSharedLibrary");
        return results;
    }
}
