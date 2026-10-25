using System.Diagnostics;
using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>compiler.build.dotNETAssembly</c> and <c>compiler.build.DotNETAssemblyOptions</c> (compiler and
/// SDK plan, stage K8), transcribed from R2025b's toolbox/compiler_sdk/cli/build (dotNETAssembly.m,
/// DotNETAssemblyOptions.m, DotNetWriter.m, ClassMapWriter.m) and toolbox/compiler/utils
/// (parseClassMapInputs.m, classMap.m, cliName.m, InterfaceManager.m), their refusals as
/// <c>probe_k8_build</c> recorded them: the class map from a <c>containers.Map</c> or the files given,
/// the derived assembly name (<c>example.&lt;file&gt;</c>) and output folder, the interface and its
/// warnings, and the mcc command a build writes. The MATLAB Data API interface builds its archive
/// and strongly typed interface (stage K9, ADR 0265); remoting is out of scope and refused at the build.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string DotNetOptionsClassName = JgsBuiltinClasses.DotNetOptions;

    private static void RegisterDotNetBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(DotNetOptionsClassName, JgsValue.Function(new BuiltinFunction(DotNetOptionsClassName,
            (args, line, col) => JgsValue.Object(NewDotNetOptions(interpreter, host, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string name = "compiler.build.dotNETAssembly";
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => BuildDotNet(env, interpreter, host, args, 1, line, col) ?? JgsValue.Null)
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                }

                JgsValue? results = BuildDotNet(env, interpreter, host, args, wanted, line, col);
                return wanted == 0 || results is null ? [] : [results];
            },
        }));
    }

    // --- The options -----------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue DotNetOptionDefault(string name) => name switch
    {
        "DebugBuild" or "EnableRemoting" or "ObfuscateArchive" or "Verbose" => OnOffSwitchStateValue.Of(false),
        "AutoDetectDataFiles" => OnOffSwitchStateValue.Of(true),
        "FrameworkVersion" => JgsValue.Str("4.0"),
        "StrongNameKeyFile" or "SecretsManifest" => JgsValue.Str(""),
        "SampleGenerationFiles" or "AdditionalFiles" => EmptyCellValue(),
        "ExternalEncryptionKey" => EmptyEncryptionKey(),
        "SupportPackages" => OptionCellColumn(["autodetect"]),
        _ => JgsMatrix.FromColumnMajor([], 0, 0),
    };

    /// <summary>
    /// The dependent properties: the assembly name given or <c>example.&lt;first file&gt;</c>, the output
    /// folder given or <c>.\&lt;name&gt;dotNETAssembly</c>, and the interface manager's three values.
    /// </summary>
    internal static JgsValue DotNetOptionDerived(string name, JgsObject options)
    {
        switch (name)
        {
            case "AssemblyName":
                return JgsValue.Str(DotNetAssemblyName(options));
            case "OutputDir":
            {
                string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
                return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(DotNetShortName(options)) + "dotNETAssembly");
            }

            case "AssemblyVersion":
                return options.Fields[JgsBuiltinClasses.InterfaceVersion];
            case "EmbedArchive":
                return options.Fields[JgsBuiltinClasses.InterfaceEmbed];
            default:
                return options.Fields[JgsBuiltinClasses.InterfaceValue];
        }
    }

    /// <summary>R2025b's get.AssemblyName: the name given, or <c>example.</c> and the first class's first file.</summary>
    private static string DotNetAssemblyName(JgsObject options)
    {
        string given = TextOf(options.Fields[JgsBuiltinClasses.GivenAssemblyName]);
        if (given.Length > 0 || ClassMapEntries(options.Fields["ClassMap"]) is not [var first, ..] || first.Files.Count == 0)
        {
            return given;
        }

        string file = Path.GetFileNameWithoutExtension(first.Files[0]);
        return "example." + file.TrimStart('+');
    }

    /// <summary>R2025b's getName: the assembly name's last part.</summary>
    internal static string DotNetShortName(JgsObject options)
    {
        string name = DotNetAssemblyName(options);
        return name[(name.LastIndexOf('.') + 1)..];
    }

    /// <summary>A class map's classes in its key order (sorted, as a <c>containers.Map</c>'s keys are) and their files.</summary>
    internal static List<(string Name, IReadOnlyList<string> Files)> ClassMapEntries(JgsValue map)
    {
        var entries = new List<(string, IReadOnlyList<string>)>();
        if (!IsKeyedCollection(map))
        {
            return entries;
        }

        Dictionary<string, JgsValue> fields = map.AsStruct;
        JgsValue[] keys = fields["keys"].AsCell;
        JgsValue[] values = fields["values"].AsCell;
        foreach (int i in Enumerable.Range(0, keys.Length).OrderBy(i => TextOf(keys[i]), StringComparer.Ordinal))
        {
            JgsValue held = values[i];
            IReadOnlyList<string> files = held.Type == JgsType.Cell ? [.. held.AsCell.Select(TextOf)] : [TextOf(held)];
            entries.Add((TextOf(keys[i]), files));
        }

        return entries;
    }

    /// <summary>The names inputParser takes for the options: the public properties and ClassName.</summary>
    private static readonly string[] DotNetParameterNames = [.. JgsBuiltinClasses.DotNetOptionNames, "ClassName"];

    /// <summary>
    /// <c>compiler.build.DotNETAssemblyOptions(files | classMap, Name, Value, ...)</c>, R2025b's
    /// constructor and parseClassMapInputs: the map or the files checked, the class map made, then
    /// every other name set in the order <c>setdiff</c> leaves them (sorted).
    /// </summary>
    private static JgsObject NewDotNetOptions(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.DotNetOptionNames.Length) + 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(DotNetOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{DotNetOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        string here = host.CurrentDirectory;
        JgsValue first = args[0];
        bool isMap = first.Type == JgsType.Struct && first.ClassName == MapClassName;
        if (!isMap && !(IsTextValue(first) || first.Type == JgsType.Cell))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                $"Expected input number 1 to be one of these types:\n\ncontainers.Map, char, string, cell\n\nInstead its type was {ClassOf(first, JgsDialect.Matlab)}.");
        }

        if ((isMap && EntryCount(first) == 0) || (!isMap && HasNoElements(first)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedNonempty", "Expected input number 1 to be nonempty.");
        }

        // The name, value pairs as inputParser reads them: a name exactly or by a unique start.
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

            given[ParameterName(asked, DotNetParameterNames, line, col)] = args[i + 1];
        }

        if (given.TryGetValue("ClassName", out JgsValue? className))
        {
            if (isMap)
            {
                throw new JgsRuntimeException(line, col, "Compiler:clishared:badUseOfClassName",
                    "The value of 'ClassName' is invalid. Cannot specify 'ClassName' when the first input is a 'containers.Map'.");
            }

            if (!IsTextValue(className) || TextOf(className).Length == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                    "The value of 'ClassName' is invalid. Value must be text with one or more characters.");
            }
        }

        if (given.ContainsKey("ClassMap"))
        {
            throw new JgsRuntimeException(line, col, "Compiler:clishared:doNotSetRequiredPropByNVPair", "Cannot specify 'ClassMap' as a name-value pair.");
        }

        if (isMap)
        {
            SetDotNetOption(interpreter, host, options, "ClassMap", first, line, col);
        }
        else
        {
            JgsValue files = ItemList(first, "Files and Folders", here, line, col);
            foreach (JgsValue file in files.AsCell)
            {
                if (!AppFileExtensions.Contains(Path.GetExtension(TextOf(file)), StringComparer.Ordinal) && !Directory.Exists(TextOf(file)))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:clishared:badExtension",
                        $"Expected 'Files and Folders' to have one of the following extensions:\n\n{string.Join("\n", AppFileExtensions)}");
                }
            }

            string cls = className is not null ? TextOf(className)
                : Path.GetFileNameWithoutExtension(TextOf(files.AsCell[0])).TrimStart('+') + "Class";
            JgsValue map = NewKeyed(MapClassName, [], sharesOnStore: false, line, col);
            Put(map, JgsValue.Str(cls), files, line, col);
            options.Fields["ClassMap"] = map;
        }

        foreach ((string property, JgsValue value) in given)
        {
            if (property != "ClassName")
            {
                SetDotNetOption(interpreter, host, options, property, value, line, col);
            }
        }

        // An mwarray build is version 1.0.0.0 unless given one, the interface read once every option is
        // set (R2025b's constructor; k8_build: mda.AssemblyVersion is '').
        if (!given.ContainsKey("AssemblyVersion") && TextOf(options.Fields[JgsBuiltinClasses.InterfaceValue]) == "mwarray")
        {
            options.Fields[JgsBuiltinClasses.InterfaceVersion] = JgsValue.Str("1.0.0.0");
        }

        if (className is not null && TextOf(options.Fields[JgsBuiltinClasses.InterfaceValue]) == "matlab-data")
        {
            Warn(host, "Compiler:build:shared:badInterfaceClassNamePair",
                "The property \"ClassName\" will be ignored when using an \"Interface\" value of 'matlab-data'. To avoid this warning, either do not specify a \"ClassName\", or use 'mwarray' for the \"Interface\" property.");
        }

        return options;
    }

    /// <summary>The property a name-value name means, as inputParser matches it: exactly, or by a unique start, either case.</summary>
    private static string ParameterName(string asked, IReadOnlyList<string> names, int line, int col)
    {
        string? exact = names.FirstOrDefault(n => n.Equals(asked, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        string[] starts = [.. names.Where(n => asked.Length > 0 && n.StartsWith(asked, StringComparison.OrdinalIgnoreCase))];
        return starts.Length switch
        {
            1 => starts[0],
            0 => throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                $"'{asked}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function."),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:InputParser:AmbiguousParam",
                $"Expected '{asked}' to match one of these values:\n\n{string.Join(", ", starts)}\n\nThe input, '{asked}', matched more than one valid value."),
        };
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it.</summary>
    internal static void SetDotNetOption(Interpreter interpreter, JGraphScriptGlobals? host, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        void InterfaceWarnings()
        {
            if (host is not null && TextOf(options.Fields[JgsBuiltinClasses.InterfaceValue]) == "matlab-data"
                && options.Fields[JgsBuiltinClasses.InterfaceEmbed].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true })
            {
                Warn(host, "Compiler:build:shared:badInterfaceEmbedArchivePair",
                    "An 'on' value of \"EmbedArchive\" will be ignored when using an \"Interface\" value of 'matlab-data'. To avoid this warning, either set \"EmbedArchive\" to 'off', or use 'mwarray' for the \"Interface\" property.");
            }
        }

        switch (name)
        {
            case "ClassMap":
            {
                if (value.Type != JgsType.Struct || value.ClassName != MapClassName)
                {
                    throw InvalidType(name, ["containers.Map"], value, line, col);
                }

                if (EntryCount(value) == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonempty", "Value must not be empty.");
                }

                if (TextOf(value.AsStruct["KeyType"]) != "char")
                {
                    throw new JgsRuntimeException(line, col, "Compiler:clishared:wrongClassMapKeyType", "The 'KeyType' of 'ClassMap' must be 'char'.");
                }

                JgsValue map = NewKeyed(MapClassName, [], sharesOnStore: false, line, col);
                foreach ((string cls, IReadOnlyList<string> _) in ClassMapEntries(value))
                {
                    if (cls.Any(char.IsWhiteSpace))
                    {
                        throw new JgsRuntimeException(line, col, "Compiler:clishared:noWhiteSpaceInProperty", "'ClassMap Key' cannot contain any white space characters.");
                    }
                }

                Dictionary<string, JgsValue> fields = value.AsStruct;
                JgsValue[] keys = fields["keys"].AsCell;
                JgsValue[] values = fields["values"].AsCell;
                for (int i = 0; i < keys.Length; i++)
                {
                    Put(map, keys[i], ItemList(values[i], "Items in ClassMap", here, line, col), line, col);
                }

                options.Fields[name] = map;
                return;
            }

            case "DebugBuild":
            case "EnableRemoting":
                options.Fields[name] = Switch(value, name, DotNetOptionsClassName, line, col);
                return;

            case "FrameworkVersion":
                options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));
                return;

            case "SampleGenerationFiles":
            {
                if (HasNoElements(value) || (value.IsStringArray && TextOf(value).Length == 0))
                {
                    options.Fields[name] = EmptyCellValue();
                    return;
                }

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

            case "StrongNameKeyFile":
            {
                if (HasNoElements(value) || (value.IsStringArray && TextOf(value).Length == 0))
                {
                    options.Fields[name] = JgsValue.Str("");
                    return;
                }

                string file = ScalarTextOnly(value, name, line, col);
                if (!File.Exists(Absolute(file)))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{file}");
                }

                options.Fields[name] = JgsValue.Str(Absolute(file));
                return;
            }

            case "AssemblyName":
            {
                // R2025b's cliName: text, not empty, a scalar, no bad folder character, no white space but a blank, no separator.
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

                options.Fields[JgsBuiltinClasses.GivenAssemblyName] = JgsValue.Str(text);
                return;
            }

            case "AssemblyVersion":
            {
                string text = ScalarTextOnly(value, name, line, col);
                options.Fields[JgsBuiltinClasses.InterfaceVersion] = JgsValue.Str(text);
                return;
            }

            case "EmbedArchive":
                options.Fields[JgsBuiltinClasses.InterfaceEmbed] = Switch(value, name, "compiler.internal.build.InterfaceManager", line, col);
                InterfaceWarnings();
                return;

            case "Interface":
            {
                string text = ScalarText(value, name, line, col);
                string? member = new[] { "matlab-data", "mwarray" }.FirstOrDefault(v => v.Equals(text, StringComparison.OrdinalIgnoreCase));
                if (member is null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeMember", "Value must be a member of this set:\n    'matlab-data'\n    'mwarray'\n");
                }

                options.Fields[JgsBuiltinClasses.InterfaceValue] = JgsValue.Str(member);
                InterfaceWarnings();
                return;
            }

            // The properties every compiler option class shares, checked as the standalone options check them.
            case "AdditionalFiles" or "AutoDetectDataFiles" or "ExternalEncryptionKey" or "ObfuscateArchive" or "SecretsManifest"
                or "SupportPackages" or "Verbose" or "OutputDir":
                SetStandaloneOption(interpreter, options, name, value, line, col);
                return;

            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{DotNetOptionsClassName}'.");
        }
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.dotNETAssembly(files | classMap | opts, Name, Value, ...)</c>:
    /// R2025b's builder and DotNetWriter over JGraph's mcc.
    /// </summary>
    private static JgsValue? BuildDotNet(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > (2 * JgsBuiltinClasses.DotNetOptionNames.Length) + 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == DotNetOptionsClassName)
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
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{DotNetOptionsClassName}' instead.");
        }
        else
        {
            options = NewDotNetOptions(interpreter, host, args, line, col);
        }

        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        bool dataApi = Text(JgsBuiltinClasses.InterfaceValue) == "matlab-data";
        if (On("EnableRemoting"))
        {
            throw new JgsRuntimeException(line, col, "JGraph:compiler:targetOutOfScope",
                "JGraph does not build remotable .NET assemblies ('EnableRemoting', 'on'): .NET 8 has no remoting.");
        }

        var watch = Stopwatch.StartNew();
        string assemblyName = DotNetAssemblyName(options);
        string outputDir = TextOf(DotNetOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(host.CurrentDirectory, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        // DotNetWriter: -W, -T, -d, the dependency options, -C and -g, then a class{} per class, its files sorted.
        List<(string Name, IReadOnlyList<string> Files)> classes = ClassMapEntries(options.Fields["ClassMap"]);
        string security = Text("StrongNameKeyFile").Length == 0 ? "Private" : Text("StrongNameKeyFile");
        string wrapper = $"dotnet:{assemblyName},api={Text(JgsBuiltinClasses.InterfaceValue)},{classes[0].Name},{Text("FrameworkVersion")},{security},local";
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

        // A MATLAB Data API archive is always beside the assembly, and R2025b writes no -C for it (k9_build: main.printed).
        if (!dataApi && !(options.Fields[JgsBuiltinClasses.InterfaceEmbed].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true }))
        {
            words.Add("-C");
        }

        if (On("DebugBuild"))
        {
            words.Add("-g");
        }

        foreach ((string cls, IReadOnlyList<string> files) in classes)
        {
            words.Add($"class{{{cls}:{string.Join(",", files.Order(StringComparer.Ordinal))}}}");
        }

        if (On("Verbose"))
        {
            host.WriteOut(MccCommandString(words) + "\n");
        }

        MccBuild built = Mcc(env, interpreter, host, words, line, col)
            ?? throw new JgsRuntimeException(line, col, "The build printed mcc's help and built nothing.");

        // compiler.build adds the getting-started page, last among the files (k8_build: main.Files).
        built = built with { Files = [.. built.Files, DotNetAssembly.WriteGettingStarted(folder, DotNetShortName(options), dataApi)] };
        return wanted == 0 ? null : DotNetResults(interpreter, built, options, watch.Elapsed.TotalSeconds, line, col);
    }

    private static JgsValue DotNetResults(Interpreter interpreter, MccBuild built, JgsObject options, double seconds, int line, int col)
    {
        JgsValue results = Results(interpreter, windowed: false, built.Files, options, built.Uuid, seconds, line, col);
        results.AsObject.Fields["BuildType"] = JgsValue.Str("dotNETAssembly");
        return results;
    }
}
