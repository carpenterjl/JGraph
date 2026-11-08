using System.Diagnostics;
using System.Globalization;
using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Excel client for the Production Server (compiler and SDK plan, stage K17f):
/// <c>mcc -W mpsxl:&lt;name&gt;,&lt;class&gt;[,version=v]</c> with its <c>class{...}</c> marshalling group
/// over <see cref="ExcelClient"/>, <c>compiler.build.excelClientForProductionServer</c> with
/// <c>compiler.build.ExcelClientForProductionServerOptions</c> and
/// <c>compiler.package.excelClientForProductionServer</c> with
/// <c>compiler.package.ExcelClientForProductionServerOptions</c>, transcribed from R2025b's
/// toolbox/compiler_sdk/cli (ExcelClientForProductionServerOptions.m, excelClientForProductionServer.m,
/// the ExcelClientForProductionServer builder and its ExcelClientForProductionServerWriter, the package
/// function and its options, productionServerClientParser), their refusals as <c>probe_k17_excel</c>
/// recorded them (<c>recordings/k17_excel.out.txt</c>). R2025b's writer has three slips that are not
/// copied (<c>docs/MATLAB_Behavioral_Fixes.md</c>): a switch alone is dropped from the mcc line,
/// ConvertExcelDateToString alone fails on an undefined variable, and two or more files with any switch
/// put a comma in front of the group, which its csc then refuses; JGraph writes the group right.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string ExcelClientOptionsClassName = JgsBuiltinClasses.ExcelClientOptions;
    private const string ExcelClientPackageOptionsClassName = JgsBuiltinClasses.ExcelClientPackageOptions;
    private const string ExcelClientBuildType = "excelClientForProductionServer";

    private static void RegisterExcelClientBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(ExcelClientOptionsClassName, JgsValue.Function(new BuiltinFunction(ExcelClientOptionsClassName,
            (args, line, col) => JgsValue.Object(NewExcelClientOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string build = "compiler.build.excelClientForProductionServer";
        env.Builtins.Register(build, JgsValue.Function(new BuiltinFunction(build,
            (args, line, col) => BuildExcelClient(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildExcelClient(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));

        env.Builtins.Register(ExcelClientPackageOptionsClassName, JgsValue.Function(new BuiltinFunction(ExcelClientPackageOptionsClassName,
            (args, line, col) => JgsValue.Object(NewExcelClientPackageOptions(interpreter, host, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string package = "compiler.package.excelClientForProductionServer";
        env.Builtins.Register(package, JgsValue.Function(new BuiltinFunction(package, (args, line, col) =>
        {
            PackageExcelClient(interpreter, host, args, line, col);
            return JgsValue.Null;
        })
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                PackageExcelClient(interpreter, host, args, line, col);
                return [];
            },
        }));
    }

    // --- mcc -W mpsxl ----------------------------------------------------------------------

    /// <summary>
    /// <c>mcc -W mpsxl:&lt;name&gt;,&lt;class&gt;[,version=v]</c>: the client DLL <see cref="ExcelClient"/>
    /// copies from the template and stamps with the type library and the functions' switches
    /// (<c>class{Class:file -switch ...}</c>), the module and the workbook, registered for the current
    /// user as R2025b's regasm script is. No archive: the functions run on the server.
    /// </summary>
    private static MccBuild ExcelClientBuild(MccCommandLine command, IReadOnlyList<string> exported, string main, string folder,
        IReadOnlyList<string> words, int line, int col, JGraphScriptGlobals host)
    {
        string name = command.ComponentName ?? Path.GetFileNameWithoutExtension(main);
        string className = command.ExcelClassName ?? name;
        if (!ExcelAddIn.IsComName(name))
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:excelClientName",
                $"An Excel client's name must begin with a letter or '_' and contain only letters, digits and '_', as its type library and "
                + $"ProgID do: '{name}' cannot be one.");
        }

        if (!ExcelAddIn.IsComName(className))
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:excelClassName",
                $"An Excel client's class must begin with a letter or '_' and contain only letters, digits and '_', as its coclass and "
                + $"ProgID do: '{className}' cannot be one.");
        }

        List<ExcelFunction> functions = [.. exported.Select(ExcelFunctionOf)];
        if (functions.Count > ExcelClient.MaxFunctions)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:excelClientFunctions",
                $"An Excel client can export at most {ExcelClient.MaxFunctions} functions; this one would export {functions.Count}.");
        }

        // The switches by function: a class{} entry names a file, the function its stem.
        var switches = new Dictionary<string, ExcelClientSwitches>(StringComparer.Ordinal);
        foreach ((string file, ExcelClientSwitches s) in command.ExcelClientSwitches)
        {
            switches[Path.GetFileNameWithoutExtension(file)] = s;
        }

        if (command.Verbose)
        {
            host.WriteOut($"Generating file \"{Path.Combine(folder, name + ".bas")}\".\n");
            host.WriteOut($"Generating file \"{Path.Combine(folder, name + ".xlam")}\".\n");
            host.WriteOut($"Generating file \"{Path.Combine(folder, "readme.txt")}\".\n");
        }

        ExcelClientBuild built;
        try
        {
            built = ExcelClient.Write(new ExcelClientOptions
            {
                OutputFolder = folder,
                AddInName = name,
                ClassName = className,
                Functions = functions,
                Switches = switches,
                Version = command.Version,
                Debug = command.DebugBuild,
                VisualBasicFile = command.VisualBasicFile,
                MccArguments = words,
                TemplatePath = ExcelClient.DefaultTemplatePath,
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:mcc:buildFailed", $"The Excel client could not be written: {ex.Message}");
        }

        if (!built.DllWritten)
        {
            Warn(host, "JGraph:mcc:excelClientPending",
                $"This build of JGraph writes the Excel client without its DLL: the template {ExcelClient.TemplateName} is not beside JGraph "
                + $"or in the JGraph Runtime, so {built.DllName} is pending; the module and the workbook are written.");
        }

        if (built.DllWritten && ExcelAddIn.RegistrationEnabled)
        {
            RegisterExcelClient(host, Path.Combine(folder, built.DllName), name, command.Version, line, col);
        }

        return new MccBuild(built.Files, Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Registers the client as R2025b's build does through regasm's script and reg import: for the
    /// current user, with the per-user warning R2025b prints (k17_excel: fromresults.printed, which
    /// names the component <c>&lt;name&gt;_&lt;major&gt;_&lt;minor&gt;</c> though the DLL has no version in its name).
    /// </summary>
    private static void RegisterExcelClient(JGraphScriptGlobals host, string dll, string name, ProgramVersion version, int line, int col)
    {
        if (OperatingSystem.IsWindows() && !ExcelAddIn.IsElevated)
        {
            Warn(host, "Compiler:compiler:COM_WARN_PER_USER_REGISTRATION",
                $"Component \"{name}_{ExcelAddIn.VersionSuffix(version)}\" will be registered for the current user only. \n"
                + "Applications that run with full administrative privileges cannot access the component. \n"
                + "To register the component for all users, run JGraph or the mcc command as Administrator.");
        }

        try
        {
            ExcelAddIn.Register(dll);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:compiler:COM_SYSTEM_ERROR",
                $"The Excel client {Path.GetFileName(dll)} was built but could not be registered: {ex.Message}\nRegister it with regsvr32.");
        }
    }

    // --- compiler.build.ExcelClientForProductionServerOptions ------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue ExcelClientOptionDefault(string name) => name switch
    {
        "AddInVersion" => JgsValue.Str("1.0.0.0"),
        "GenerateVisualBasicFile" => OnOffSwitchStateValue.Of(true),
        "DebugBuild" or "ReplaceExcelBlankWithNaN" or "ConvertExcelDateToString" or "ReplaceNaNToZeroInExcel" or "ConvertNumericOutToDateInExcel" or "Verbose"
            => OnOffSwitchStateValue.Of(false),
        "ServerArchive" => JgsValue.Str(""),
        _ => JgsMatrix.FromColumnMajor([], 0, 0),
    };

    /// <summary>
    /// R2025b's get.AddInName (the name given, or the first file's stem), get.ClassName (the class given,
    /// or the stem with <c>Class</c> after it) and OutputDir (the folder given, or
    /// <c>.\&lt;name&gt;excelClientForProductionServer</c>) (k17_excel: copts.derived, copts.results).
    /// </summary>
    internal static JgsValue ExcelClientOptionDerived(string name, JgsObject options)
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
                return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(addIn) + ExcelClientBuildType);
            }
        }
    }

    /// <summary>
    /// <c>compiler.build.ExcelClientForProductionServerOptions(results, Name, Value, ...)</c> over a
    /// Production Server archive's results (its FunctionFiles and the one .ctf among its Files), or
    /// <c>(files, serverArchive, Name, Value, ...)</c>; the files and the archive set first, then every
    /// other name in the order setdiff leaves them (sorted), as R2025b's createInstance does.
    /// </summary>
    private static JgsObject NewExcelClientOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int count = JgsBuiltinClasses.ExcelClientOptionNames.Length;
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        JgsValue files;
        JgsValue archive;
        int from;
        if (ResultsOf(args[0], line, col) is { } results)
        {
            if (args.Count > (2 * count) - 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
            }

            if (TextOf(results.Fields["BuildType"]) != "productionServerArchive")
            {
                throw new JgsRuntimeException(line, col, "CompilerSDK:ExcelClientForProductionServerOptions:notAProductionServerResult",
                    "The result object passed as input is not from the function compiler.build.productionServerArchive.");
            }

            files = results.Fields["Options"].AsObject.Fields["FunctionFiles"];
            string[] ctf = [.. results.Fields["Files"].AsCell.Select(TextOf).Where(static f => f.EndsWith(".ctf", StringComparison.OrdinalIgnoreCase))];
            archive = JgsValue.Str(ctf.Length == 1 ? ctf[0] : "");
            from = 1;
        }
        else
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            if (args.Count > (2 * count) - 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
            }

            files = args[0];
            archive = args[1];
            from = 2;
        }

        JgsClass definition = interpreter.BuiltinClass(ExcelClientOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{ExcelClientOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetExcelClientOption(interpreter, options, "FunctionFiles", files, line, col);
        SetExcelClientOption(interpreter, options, "ServerArchive", archive, line, col);
        var given = new SortedDictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = from; i < args.Count; i += 2)
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

            given[ParameterName(asked, JgsBuiltinClasses.ExcelClientOptionNames, line, col)] = args[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetExcelClientOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it (k17_excel: cset.*).</summary>
    internal static void SetExcelClientOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
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
            case "ServerArchive":
            {
                // propertyIsScalarText, mustBeNonzeroLengthText, fileMustExist as given, then .ctf supplied
                // when the text has no extension and refused when it has another (cset.ServerArchive.*).
                string given = ScalarText(value, name, line, col);
                string absolute = Path.GetFullPath(Path.IsPathRooted(given) ? given : Path.Combine(here, given));
                if (!File.Exists(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{given}");
                }

                if (Path.GetExtension(given).Length == 0)
                {
                    given += ".ctf";
                    absolute += ".ctf";
                }

                if (!given.EndsWith(".ctf", StringComparison.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, "CompilerSDK:ExcelClientForProductionServerOptions:notACTFFile",
                        $"The specified input is not a CTF file: {given}\n\n");
                }

                options.Fields[name] = JgsValue.Str(absolute);
                return;
            }

            case "DebugBuild" or "GenerateVisualBasicFile" or "ReplaceExcelBlankWithNaN" or "ConvertExcelDateToString" or "ReplaceNaNToZeroInExcel" or "ConvertNumericOutToDateInExcel":
                options.Fields[name] = Switch(value, name, ExcelClientOptionsClassName, line, col);
                return;
            case "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{ExcelClientOptionsClassName}'.");
        }
    }

    // --- compiler.build.excelClientForProductionServer -------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.excelClientForProductionServer(results | files, serverArchive | opts, Name, Value, ...)</c>:
    /// R2025b's builder and ExcelClientForProductionServerWriter over JGraph's mcc (<c>-W mpsxl:name,class,version=v -T link:lib -d</c>,
    /// <c>-v</c>, <c>-b</c>, <c>-g</c>, the files, then the <c>class{}</c> group when any switch is on; no dependency
    /// options), then its postmcc: the DLL, and under GenerateVisualBasicFile the module and the workbook.
    /// </summary>
    private static JgsValue? BuildExcelClient(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        int count = JgsBuiltinClasses.ExcelClientOptionNames.Length;
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * count) - 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == ExcelClientOptionsClassName)
        {
            if (args.Count > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
            }

            options = args[0].AsObject;
        }
        else if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name.StartsWith("compiler.", StringComparison.Ordinal) && ResultsOf(args[0], line, col) is null)
        {
            throw new JgsRuntimeException(line, col, "Compiler:clishared:wrongOptionsObject",
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{ExcelClientOptionsClassName}' instead.");
        }
        else
        {
            options = NewExcelClientOptions(interpreter, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        string addIn = TextOf(ExcelClientOptionDerived("AddInName", options));
        string className = TextOf(ExcelClientOptionDerived("ClassName", options));
        string outputDir = TextOf(ExcelClientOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        var words = new List<string> { "-W", $"mpsxl:{addIn},{className},version={Text("AddInVersion")}", "-T", "link:lib", "-d", outputDir };
        if (On("Verbose"))
        {
            words.Add("-v");
        }

        if (On("GenerateVisualBasicFile"))
        {
            words.Add("-b");
        }

        if (On("DebugBuild"))
        {
            words.Add("-g");
        }

        string[] files = [.. options.Fields["FunctionFiles"].AsCell.Select(TextOf)];
        words.AddRange(files);
        var switches = new ExcelClientSwitches(On("ReplaceExcelBlankWithNaN"), On("ConvertExcelDateToString"), On("ReplaceNaNToZeroInExcel"), On("ConvertNumericOutToDateInExcel"));
        if (switches.Any)
        {
            // class{Class:file -switch ...,file -switch ...}, every file with every switch on: what R2025b's writer
            // means to write (its three slips not copied).
            string group = string.Join(",", files.Select(f => f + " " + string.Join(" ", switches.MccSwitches)));
            words.Add($"class{{{className}:{group}}}");
        }

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

        // postmcc: the DLL, then the .bas and the workbook under GenerateVisualBasicFile (fromresults.Files, nobas.Files).
        JgsValue results = Results(interpreter, windowed: false, built.Files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str(ExcelClientBuildType);
        return results;
    }

    // --- compiler.package.ExcelClientForProductionServerOptions ----------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue ExcelClientPackageOptionDefault(string name) => name switch
    {
        "InstallerIcon" => JgsValue.Str(PackagingResource("default_icon_48.png")),
        "MaxResponseSize" => JgsValue.Number(750),
        "ServerTimeOut" => JgsValue.Number(60),
        "Version" => JgsValue.Str("1.0"),
        _ => JgsValue.Str(""),
    };

    /// <summary>
    /// R2025b's get.InstallerName (the name given, else <c>&lt;library&gt;ExcelClientInstaller</c>) and
    /// get.OutputDir (the folder given made absolute, else <c>.\&lt;library&gt;ExcelClientInstaller</c>).
    /// </summary>
    internal static JgsValue ExcelClientPackageOptionDerived(string name, JgsObject options)
    {
        string library = Path.GetFileNameWithoutExtension(TextOf(options.Fields["ClientLibrary"]));
        if (name == "InstallerName")
        {
            string given = TextOf(options.Fields[JgsBuiltinClasses.GivenInstallerName]);
            return JgsValue.Str(given.Length > 0 ? given : library + "ExcelClientInstaller");
        }

        string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
        return JgsValue.Str(folder.Length > 0 ? folder : @".\" + library + "ExcelClientInstaller");
    }

    /// <summary>
    /// <c>compiler.package.ExcelClientForProductionServerOptions(results, Name, Value, ...)</c> over an Excel
    /// client's results (ClientLibrary the DLL in its output folder, ServerArchive its archive, unless given),
    /// or <c>(clientLibrary, serverArchive, Name, Value, ...)</c>; each name set in sorted order.
    /// </summary>
    private static JgsObject NewExcelClientPackageOptions(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        var pairs = new List<JgsValue>();
        if (ResultsOf(args[0], line, col) is { } results)
        {
            if (results.Fields["Options"] is not { Type: JgsType.Object } built || built.AsObject.Class.Name != ExcelClientOptionsClassName)
            {
                throw new JgsRuntimeException(line, col, "CompilerSDK:ExcelClientForProductionServerOptions:notAnExcelClientForProductionServerResult",
                    "The result object passed as input is not from the function compiler.build.excelClientForProductionServer.");
            }

            if (!NamesProperty(args, "ClientLibrary", 1))
            {
                string outputDir = TextOf(ExcelClientOptionDerived("OutputDir", built.AsObject));
                string addIn = TextOf(ExcelClientOptionDerived("AddInName", built.AsObject));
                pairs.AddRange([JgsValue.Str("ClientLibrary"), JgsValue.Str(Path.Combine(Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir)), ExcelClient.DllName(addIn)))]);
            }

            if (!NamesProperty(args, "ServerArchive", 1))
            {
                pairs.AddRange([JgsValue.Str("ServerArchive"), built.AsObject.Fields["ServerArchive"]]);
            }

            pairs.AddRange(args.Skip(1));
        }
        else
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            pairs.AddRange([JgsValue.Str("ClientLibrary"), args[0], JgsValue.Str("ServerArchive"), args[1]]);
            pairs.AddRange(args.Skip(2));
        }

        if (pairs.Count > 2 * JgsBuiltinClasses.ExcelClientPackageOptionNames.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(ExcelClientPackageOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{ExcelClientPackageOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        var given = new SortedDictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Count; i += 2)
        {
            if (!IsTextScalar(pairs[i]) || pairs[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string asked = TextOf(pairs[i]);
            if (i + 1 >= pairs.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{asked}'. Name-value pair arguments require a name followed by a value.");
            }

            given[ParameterName(asked, JgsBuiltinClasses.ExcelClientPackageOptionNames, line, col)] = pairs[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetExcelClientPackageOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>A one-element numeric or logical value's number; NaN for anything else.</summary>
    private static double ScalarNumberOf(JgsValue value) => value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber
        : value.Type == JgsType.Array && value.ArrayLength == 1 && value.ElementAt(0).Type is JgsType.Number or JgsType.Bool ? value.ElementAt(0).AsNumber
        : double.NaN;

    /// <summary>R2025b's property validators' refusal: <c>Error setting property 'X' of class 'C'. Value must ...</c></summary>
    private static JgsRuntimeException PropertyRefusal(string identifier, string property, string owner, string must, int line, int col) =>
        new(line, col, "MATLAB:validators:" + identifier, $"Error setting property '{property}' of class '{owner}'. Value must {must}.");

    /// <summary>Sets <paramref name="name"/> on the package <paramref name="options"/>, checked as R2025b's validators and set methods check it (k17_excel: pset.*).</summary>
    internal static void SetExcelClientPackageOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string owner = ExcelClientPackageOptionsClassName;
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        bool TextScalar(JgsValue v) => IsTextValue(v) && IsTextScalar(v) && !v.IsCharMatrix;
        string ExistingFile(string property, string extension)
        {
            // fileMustExist as given, the path made absolute, an extension other than the one wanted refused (set.ClientLibrary, set.ServerArchive).
            string given = TextOf(value);
            string absolute = Absolute(given);
            if (!File.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{given}");
            }

            string ext = Path.GetExtension(absolute);
            if (ext.Length > 0 && !ext.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw BadProperty(property, absolute, line, col);
            }

            return absolute;
        }

        switch (name)
        {
            case "ServerArchive":
                if (!TextScalar(value))
                {
                    throw PropertyRefusal("mustBeTextScalar", name, owner, "be a character vector or string scalar", line, col);
                }

                options.Fields[name] = JgsValue.Str(ExistingFile(name, ".ctf"));
                return;
            case "ClientLibrary":
                if (!TextScalar(value))
                {
                    throw PropertyRefusal("mustBeTextScalar", name, owner, "be a character vector or string scalar", line, col);
                }

                options.Fields[name] = JgsValue.Str(ExistingFile(name, ".dll"));
                return;
            case "InstallerName":
                options.Fields[JgsBuiltinClasses.GivenInstallerName] = JgsValue.Str(CliName(value, name, line, col));
                return;
            case "InstallerIcon":
            {
                if (!TextScalar(value))
                {
                    throw PropertyRefusal("mustBeTextScalar", name, owner, "be a character vector or string scalar", line, col);
                }

                string given = ScalarTextOnly(value, name, line, col);
                string absolute = Absolute(given);
                if (given.Length == 0 || !File.Exists(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{given}");
                }

                if (ImageFormatOf(absolute) == ".gif" && IsAnimated(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:noAnimatedGIFs", "Animated '.gif' files are not supported.");
                }

                options.Fields[name] = JgsValue.Str(absolute);
                return;
            }

            case "MaxResponseSize":
                if (!IsNumericValue(value))
                {
                    throw PropertyRefusal("mustBeNumeric", name, owner, "be numeric", line, col);
                }

                // Minted anew, so the options object never holds a caller's array.
                options.Fields[name] = value.Type is JgsType.Number or JgsType.Bool || value.ArrayLength == 1 ? JgsValue.Number(ScalarNumberOf(value))
                    : JgsMatrix.FromColumnMajor([.. Enumerable.Range(0, value.ArrayLength).Select(i => value.ElementAt(i).AsNumber)], value.Rows, value.Cols);
                return;
            case "ServerTimeOut":
            {
                // mustBeInteger, mustBePositive, mustBeReal, in that order.
                if (!(IsNumericValue(value) || IsLogicalValue(value)))
                {
                    throw PropertyRefusal("mustBeNumericOrLogical", name, owner, "be numeric or logical", line, col);
                }

                double number = ScalarNumberOf(value);
                if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Floor(number) || value.Type == JgsType.Complex)
                {
                    throw PropertyRefusal("mustBeInteger", name, owner, "be integer", line, col);
                }

                if (number <= 0)
                {
                    throw PropertyRefusal("mustBePositive", name, owner, "be positive", line, col);
                }

                options.Fields[name] = JgsValue.Number(number);
                return;
            }

            case "ServerURL":
                if (!TextScalar(value))
                {
                    throw PropertyRefusal("mustBeTextScalar", name, owner, "be a character vector or string scalar", line, col);
                }

                options.Fields[name] = JgsValue.Str(TextOf(value));
                return;
            case "SSLCertificate":
            {
                // The validator first (a number refused), then the set method: empty text is '', else the file as given.
                if (!TextScalar(value) && !IsEmptyValue(value))
                {
                    throw PropertyRefusal("mustBeTextScalar", name, owner, "be a character vector or string scalar", line, col);
                }

                string given = IsEmptyValue(value) ? "" : TextOf(value);
                if (given.Length == 0)
                {
                    options.Fields[name] = JgsValue.Str("");
                    return;
                }

                string absolute = Absolute(given);
                if (!File.Exists(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{given}");
                }

                options.Fields[name] = JgsValue.Str(absolute);
                return;
            }

            case "Version":
            {
                if (HasNoElements(value) || (IsTextScalar(value) && TextOf(value).Length == 0))
                {
                    throw PropertyRefusal("mustBeNonempty", name, owner, "not be empty", line, col);
                }

                string text = ScalarTextOnly(value, name, line, col);
                foreach (string part in text.Split('.'))
                {
                    // str2double of each part must be a number (' 1' is one, 'a' and '0-beta' are not).
                    if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        throw BadProperty(name, text, line, col);
                    }
                }

                options.Fields[name] = JgsValue.Str(text);
                return;
            }

            case "OutputDir":
                if (HasNoElements(value) || (IsTextScalar(value) && TextOf(value).Length == 0))
                {
                    throw PropertyRefusal("mustBeNonempty", name, owner, "not be empty", line, col);
                }

                options.Fields[JgsBuiltinClasses.GivenOutputDir] = JgsValue.Str(Absolute(ScalarText(value, name, line, col)));
                return;
            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{owner}'.");
        }
    }

    // --- compiler.package.excelClientForProductionServer -------------------------------------------

    /// <summary>
    /// <c>compiler.package.excelClientForProductionServer(results | files, clientLibrary, serverArchive, Name, Value | 'Options', opts)</c>:
    /// R2025b's productionServerClientParser, the warning when the files leave the client library out, then the
    /// installer <see cref="ExcelClientInstaller"/> builds: the files (JGraph's server configuration add-in among
    /// them, as R2025b's ServerConfig.dll is), <c>_config.txt</c>'s text, the icon at R2025b's six sizes and the version.
    /// </summary>
    private static void PackageExcelClient(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 1 + (2 * JgsBuiltinClasses.ExcelClientPackageOptionNames.Length))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        string here = host.CurrentDirectory;
        List<string> items;
        List<JgsValue> optional;
        JgsObject? results = ResultsOf(args[0], line, col);
        List<JgsValue> optionArgs;
        if (results is not null)
        {
            if (results.Fields["Options"] is not { Type: JgsType.Object } built || built.AsObject.Class.Name != ExcelClientOptionsClassName)
            {
                throw new JgsRuntimeException(line, col, "CompilerSDK:ExcelClientForProductionServerOptions:notAnExcelClientForProductionServerResult",
                    "The result object passed as input is not from the function compiler.build.excelClientForProductionServer.");
            }

            items = ValidateItems(results.Fields["Files"], here, line, col);
            optional = [.. args.Skip(1)];
            optionArgs = [.. args];
        }
        else
        {
            items = ValidateItems(args[0], here, line, col);
            if (args.Count > 1 && IsTextScalar(args[1]) && TextOf(args[1]) == "Options")
            {
                optional = [.. args.Skip(1)];
                optionArgs = [];
            }
            else
            {
                if (args.Count < 3)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
                }

                optional = [.. args.Skip(3)];
                optionArgs = [args[1], args[2], .. args.Skip(3)];
            }
        }

        JgsObject io;
        if (NamesProperty(optional, "Options", 0))
        {
            if (optional.Count > 2)
            {
                throw new JgsRuntimeException(line, col, "Compiler:package:shared:onlyOptions", "No other name-value arguments can be used with the 'Options' parameter.");
            }

            JgsValue given = optional[1];
            if (!(given.Type == JgsType.Object && given.AsObject.Class.Name == ExcelClientPackageOptionsClassName))
            {
                string kind = given.Type == JgsType.Object ? given.AsObject.Class.Name : ClassOf(given, JgsDialect.Matlab);
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                    $"The value of 'Options' is invalid. Expected input to be one of these types:\n\n{ExcelClientPackageOptionsClassName}\n\nInstead its type was {kind}.");
            }

            io = given.AsObject;
        }
        else
        {
            io = NewExcelClientPackageOptions(interpreter, host, optionArgs, line, col);
        }

        string library = TextOf(io.Fields["ClientLibrary"]);
        string archive = TextOf(io.Fields["ServerArchive"]);
        if (!items.Any(i => i.EndsWith(Path.GetFileName(library), StringComparison.OrdinalIgnoreCase)))
        {
            Warn(host, "CompilerSDK:ExcelClientForProductionServerOptions:missingClientLibraryInPackagedFiles",
                $"Client library {library} is not specified in the files to be packaged list.");
        }

        string icon = TextOf(io.Fields["InstallerIcon"]);
        if (!File.Exists(icon))
        {
            throw new JgsRuntimeException(line, col, "Compiler:installer:propDoesNotExist", $"The file '{icon}' named by 'InstallerIcon' does not exist.");
        }

        // JGraph's server configuration add-in goes along, as R2025b's ServerConfig.dll does.
        if (ExcelClientInstaller.DefaultConfigurationAddInPath is { } configuration && !items.Any(i => Path.GetFileName(i).Equals(ExcelClientInstaller.ConfigurationAddInName, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(configuration);
        }

        string installerName = TextOf(ExcelClientPackageOptionDerived("InstallerName", io));
        string outputDir = Path.GetFullPath(Path.Combine(here, TextOf(ExcelClientPackageOptionDerived("OutputDir", io))));
        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        string scratch = Path.Combine(Path.GetTempPath(), "jgraph-xlclient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string ico = InstallerIcon(icon, scratch, "icon", line, col);
            var spec = new ExcelClientInstallerSpec
            {
                InstallerName = installerName,
                OutputFolder = outputDir,
                Items = items,
                ClientLibrary = library,
                ServerArchive = archive,
                MaxResponseSize = ScalarNumberOf(io.Fields["MaxResponseSize"]),
                ServerTimeOut = (int)ScalarNumberOf(io.Fields["ServerTimeOut"]),
                ServerUrl = TextOf(io.Fields["ServerURL"]),
                SslCertificate = TextOf(io.Fields["SSLCertificate"]),
                Version = TextOf(io.Fields["Version"]),
                Icons = IconFile.Read(ico),
                TemplatePath = ExcelClientInstaller.DefaultTemplatePath,
            };
            ExcelClientInstaller.Build(spec);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            throw new JgsRuntimeException(line, col, "JGraph:package:excelClientFailed", $"The Excel client installer could not be written: {ex.Message}");
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The system's temporary folder clears it.
            }
        }
    }
}
