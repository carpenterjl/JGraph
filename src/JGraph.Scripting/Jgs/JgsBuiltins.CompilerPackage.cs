using JGraph.Deploy;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Installers (compiler and SDK plan, stage K18): <c>compiler.package.installer</c> and
/// <c>compiler.package.InstallerOptions</c>, transcribed from R2025b's toolbox/compiler/cli/package
/// (installer.m, InstallerOptions.m, OptionsBase.m and the shared inputParser.m), their refusals as
/// <c>probe_k18_package</c> recorded them; <c>compiler.runtime.customInstaller</c>; and the runtime's
/// functions <c>mcrinstaller</c>, <c>compiler.runtime.version</c> and <c>compiler.runtime.download</c>,
/// answering for the JGraph Runtime's installer (<see cref="RuntimeInstaller"/>). The installers are
/// built by <see cref="ApplicationInstaller"/> over WiX (decision KO7); none is run here.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string InstallerOptionsClassName = JgsBuiltinClasses.InstallerOptions;

    /// <summary>R2025b's RuntimeDeliveryValues, in enumeration order.</summary>
    private static readonly string[] RuntimeDeliveryValues = ["installer", "none", "web"];

    /// <summary>R2025b's PackageTypes.</summary>
    private static readonly string[] PackageTypeValues = ["auto", "zip"];

    /// <summary>R2025b's OptionalProductsValues.</summary>
    private static readonly string[] OptionalProductsValues = ["all", "none"];

    /// <summary>The runtime's name in the messages R2025b's name the MATLAB Runtime in.</summary>
    private const string RuntimeProductName = "JGraph Runtime";

    private const string DownloadCommandText = RuntimeProductName + " installer cannot be found. Download it using the command: compiler.runtime.download\n";

    private static void RegisterPackageBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(InstallerOptionsClassName, JgsValue.Function(new BuiltinFunction(InstallerOptionsClassName,
            (args, line, col) => JgsValue.Object(NewInstallerOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        const string package = "compiler.package.installer";
        env.Builtins.Register(package, JgsValue.Function(new BuiltinFunction(package, (args, line, col) =>
        {
            PackageInstaller(interpreter, host, args, line, col);
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

                PackageInstaller(interpreter, host, args, line, col);
                return [];
            },
        }));

        const string custom = "compiler.runtime.customInstaller";
        env.Builtins.Register(custom, JgsValue.Function(new BuiltinFunction(custom, (args, line, col) =>
        {
            CustomInstaller(interpreter, host, args, line, col);
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

                CustomInstaller(interpreter, host, args, line, col);
                return [];
            },
        }));

        const string version = "compiler.runtime.version";
        env.Builtins.Register(version, JgsValue.Function(new BuiltinFunction(version, (args, line, col) =>
        {
            TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
            return JgsValue.Number(ScriptDeployment.RuntimeVersion[0]);
        })
        {
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) =>
            {
                TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                return [JgsValue.Number(ScriptDeployment.RuntimeVersion[0])];
            },
        }));

        const string download = "compiler.runtime.download";
        env.Builtins.Register(download, JgsValue.Function(new BuiltinFunction(download, (args, line, col) =>
        {
            DownloadRuntimeInstaller(host, args, line, col);
            return JgsValue.Null;
        })
        {
            AutoCallsBare = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                DownloadRuntimeInstaller(host, args, line, col);
                return [];
            },
        }));

        env.Builtins.Register("mcrinstaller", JgsValue.Function(new BuiltinFunction("mcrinstaller",
            (args, line, col) => McrInstaller(host, args, 1, line, col)[0])
        {
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) => McrInstaller(host, args, wanted, line, col),
        }));
    }

    // --- The runtime's functions ---------------------------------------------------------

    /// <summary>
    /// <c>[installer, major, minor, update, platform] = mcrinstaller</c>: the JGraph Runtime installer
    /// on this machine with the release's numbers and <c>WIN64</c>, or, printing R2025b's
    /// downloadCommand line, an empty char and empties (k18_package: mcrinstaller.*).
    /// </summary>
    private static JgsValue[] McrInstaller(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
        if (wanted > 5)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        string? found = RuntimeInstaller.Find(RuntimeLayout.CurrentVersion);
        var outputs = new JgsValue[System.Math.Max(wanted, 1)];
        if (found is null)
        {
            host.WriteOut(DownloadCommandText + "\n");
        }

        for (int i = 0; i < outputs.Length; i++)
        {
            outputs[i] = i switch
            {
                0 => JgsValue.Str(found ?? ""),
                4 => JgsValue.Str(found is null ? "" : "WIN64"),
                _ => found is null ? JgsMatrix.FromColumnMajor([], 0, 0) : JgsValue.Number(ScriptDeployment.RuntimeVersion[i - 1]),
            };
        }

        return outputs;
    }

    /// <summary>
    /// <c>compiler.runtime.download</c>: the JGraph Runtime installer of this version fetched from its
    /// release (decision KO8) into the user's local application data, in R2025b's words; one already
    /// on the machine is named and nothing is fetched.
    /// </summary>
    private static void DownloadRuntimeInstaller(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
        string version = RuntimeLayout.CurrentVersion;
        if (RuntimeInstaller.Find(version) is { } existing)
        {
            host.WriteOut($"An existing {RuntimeProductName} installer was found at:\n \"{existing}\"\n");
            return;
        }

        host.WriteOut($"Downloading {RuntimeProductName} installer. It may take several minutes...\n");
        string downloaded;
        try
        {
            downloaded = RuntimeInstaller.DownloadAsync(version).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:runtime:couldNotDownload",
                $"A connection could not be established to download the Runtime Installer.\nDownload the runtime from:\n{RuntimeInstaller.ReleaseUrl(version)}\nand update the runtime location in Compiler Settings.");
        }

        host.WriteOut($"{RuntimeProductName} installer has been downloaded to:\n \"{downloaded}\"\n");
    }

    // --- The options ------------------------------------------------------------------------

    /// <summary>A stored property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue InstallerOptionDefault(string name) => name switch
    {
        "RuntimeDelivery" => JgsValue.Str("web"),
        "InstallerSplash" => JgsValue.Str(PackagingResource("default_splash.png")),
        "InstallerIcon" => JgsValue.Str(PackagingResource("default_icon_48.png")),
        "InstallerLogo" => JgsValue.Str(PackagingResource("default_logo.png")),
        "PackageType" => JgsValue.Str("auto"),
        "AdditionalFiles" => EmptyCellValue(),
        "OptionalDependencies" => JgsValue.Str("all"),
        "Verbose" => OnOffSwitchStateValue.Of(false),
        "Version" => JgsValue.Str("1.0"),
        _ => JgsValue.Str(""),
    };

    /// <summary>
    /// R2025b's get.InstallerName (<c>MyAppInstaller</c> when none was given), get.OutputDir
    /// (<c>.\&lt;scrubbed name&gt;installer</c>) and get.DefaultInstallationDir
    /// (<c>%ProgramFiles%\&lt;scrubbed name&gt;</c>) (k18_package: derived.*).
    /// </summary>
    internal static JgsValue InstallerOptionDerived(string name, JgsObject options)
    {
        string application = TextOf(options.Fields["ApplicationName"]);
        switch (name)
        {
            case "InstallerName":
            {
                string given = TextOf(options.Fields[JgsBuiltinClasses.GivenInstallerName]);
                return JgsValue.Str(given.Length > 0 ? given : "MyAppInstaller");
            }

            case "OutputDir":
            {
                string given = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
                return JgsValue.Str(given.Length > 0 ? given : @".\" + ApplicationInstaller.ScrubName(application) + "installer");
            }

            default:
            {
                string given = TextOf(options.Fields[JgsBuiltinClasses.GivenDefaultInstallationDir]);
                return JgsValue.Str(given.Length > 0 ? given : @"%ProgramFiles%\" + ApplicationInstaller.ScrubName(application));
            }
        }
    }

    /// <summary>Whether <paramref name="args"/> name <paramref name="property"/> at an odd position (R2025b's nameValuePairsHaveProperty).</summary>
    private static bool NamesProperty(IReadOnlyList<JgsValue> args, string property, int from)
    {
        for (int i = from; i < args.Count; i += 2)
        {
            if (IsTextScalar(args[i]) && !args[i].IsCharMatrix && TextOf(args[i]).Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A <c>compiler.build.Results</c> object, or null: the one argument, which must be a single one (k18_package: opts.results.two).</summary>
    private static JgsObject? ResultsOf(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Object && !value.IsObjectArray && value.AsObject.Class.Name == JgsBuiltinClasses.BuildResults)
        {
            return value.AsObject;
        }

        if (value.IsObjectArray && value.AsObjectArray.Class.Name == JgsBuiltinClasses.BuildResults)
        {
            int n = value.AsObjectArray.Length;
            if (n == 1)
            {
                return value.AsObjectArray.Elements[0];
            }

            throw new JgsRuntimeException(line, col, "MATLAB:index:expected_one_output_from_intermediate_indexing",
                $"Intermediate dot '.' indexing produced a comma-separated list with {n} values, but it must produce a single value when followed by subsequent indexing operations.");
        }

        return null;
    }

    /// <summary>The name a build's options object gives its product (R2025b's hidden getName of each options class).</summary>
    private static string BuildOptionsName(JgsObject options) => options.Class.Name switch
    {
        JgsBuiltinClasses.StandaloneOptions => TextOf(StandaloneOptionDerived("ExecutableName", options)),
        JgsBuiltinClasses.DotNetOptions => DotNetShortName(options),
        JgsBuiltinClasses.CLibraryOptions => TextOf(CLibraryOptionDerived("LibraryName", options)),
        JgsBuiltinClasses.CppLibraryOptions => TextOf(CppLibraryOptionDerived("LibraryName", options)),
        JgsBuiltinClasses.PythonPackageOptions => PythonPackageShortName(TextOf(PythonPackageOptionDerived("PackageName", options))),
        JgsBuiltinClasses.ProductionServerArchiveOptions => TextOf(ProductionServerOptionDerived("ArchiveName", options)),
        _ => "",
    };

    /// <summary>
    /// <c>compiler.package.InstallerOptions(results, Name, Value, ...)</c> or
    /// <c>(Name, Value, ...)</c>: with a results object, R2025b's updateInputsFromResultsObject puts
    /// ApplicationName (the build's name), AddRemoveProgramsIcon (a standalone build's ExecutableIcon)
    /// and Shortcut (its program) before the names given; ApplicationName must be among them; then
    /// each name is set in the order setdiff leaves them (sorted).
    /// </summary>
    private static JgsObject NewInstallerOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        var pairs = new List<JgsValue>();
        int from = 0;
        if (args.Count > 0 && ResultsOf(args[0], line, col) is { } results)
        {
            from = 1;
            JgsObject built = results.Fields["Options"].AsObject;
            if (!NamesProperty(args, "ApplicationName", 1))
            {
                pairs.AddRange([JgsValue.Str("ApplicationName"), JgsValue.Str(BuildOptionsName(built))]);
            }

            if (built.Class.Name == JgsBuiltinClasses.StandaloneOptions && OperatingSystem.IsWindows())
            {
                string icon = TextOf(built.Fields["ExecutableIcon"]);
                if (!NamesProperty(args, "AddRemoveProgramsIcon", 1) && File.Exists(icon))
                {
                    pairs.AddRange([JgsValue.Str("AddRemoveProgramsIcon"), JgsValue.Str(icon)]);
                }

                if (!NamesProperty(args, "Shortcut", 1))
                {
                    string exe = "\\" + TextOf(StandaloneOptionDerived("ExecutableName", built)) + ".exe";
                    string[] matches = [.. results.Fields["Files"].AsCell.Select(TextOf).Where(f => f.EndsWith(exe, StringComparison.Ordinal))];
                    if (matches.Length == 1 && File.Exists(matches[0]))
                    {
                        pairs.AddRange([JgsValue.Str("Shortcut"), JgsValue.Str(matches[0])]);
                    }
                }
            }
        }

        for (int i = from; i < args.Count; i++)
        {
            pairs.Add(args[i]);
        }

        if (!NamesProperty(pairs, "ApplicationName", 0))
        {
            throw new JgsRuntimeException(line, col, "Compiler:package:shared:mustSetProperty", "'ApplicationName' has no default and must be set.");
        }

        if (pairs.Count > 1 + (2 * JgsBuiltinClasses.InstallerOptionNames.Length))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(InstallerOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{InstallerOptionsClassName}' is not available in this build.");
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

            given[ParameterName(asked, JgsBuiltinClasses.InstallerOptionNames, line, col)] = pairs[i + 1];
        }

        foreach ((string property, JgsValue value) in given)
        {
            SetInstallerOption(interpreter, options, property, value, line, col);
        }

        return options;
    }

    /// <summary>R2025b's mustBeMember refusal, the set listed one member per line.</summary>
    private static JgsRuntimeException NotAMember(IReadOnlyList<string> members, int line, int col) =>
        new(line, col, "MATLAB:validators:mustBeMember",
            "Value must be a member of this set:\n" + string.Concat(members.Select(static m => "    '" + m + "'\n")));

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method for it checks it (k18_package: set.*).</summary>
    internal static void SetInstallerOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        bool Resets(JgsValue v) => HasNoElements(v) || (v.IsStringArray && v.ArrayLength == 1 && TextOf(v).Length == 0) || (v.Type == JgsType.String && v.AsString.Length == 0);
        string ImageFile(string property)
        {
            // propertyIsScalarText, mustBeNonzeroLengthText, fileMustExist (the text as given), then the
            // absolute path, whose bytes must be an image and not an animated GIF.
            string file = ScalarText(value, property, line, col);
            string absolute = Absolute(file);
            if (!File.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{file}");
            }

            string format = ImageFormatOf(absolute)
                ?? throw new JgsRuntimeException(line, col, "Compiler:utils:shared:notAnImage", $"The file:\n\n{absolute}\n\nis not an image file.");
            if (format == ".gif" && IsAnimated(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:noAnimatedGIFs", "Animated '.gif' files are not supported.");
            }

            return absolute;
        }

        switch (name)
        {
            case "ApplicationName":
                options.Fields[name] = JgsValue.Str(CliName(value, name, line, col));
                return;

            case "InstallerName":
                options.Fields[JgsBuiltinClasses.GivenInstallerName] = JgsValue.Str(CliName(value, name, line, col));
                return;

            case "Version":
            {
                string version = ScalarText(value, name, line, col);
                if (!ApplicationInstaller.IsVersionText(version))
                {
                    throw BadProperty(name, version, line, col);
                }

                options.Fields[name] = JgsValue.Str(version);
                return;
            }

            case "RuntimeDelivery":
            {
                string delivery = ScalarTextOnly(value, "RuntimeDeliveryValues", line, col);
                if (!RuntimeDeliveryValues.Contains(delivery, StringComparer.Ordinal))
                {
                    throw NotAMember(RuntimeDeliveryValues, line, col);
                }

                options.Fields[name] = JgsValue.Str(delivery);
                return;
            }

            case "PackageType":
            {
                string type = ScalarTextOnly(value, name, line, col);
                if (!PackageTypeValues.Contains(type, StringComparer.Ordinal))
                {
                    throw NotAMember(PackageTypeValues, line, col);
                }

                options.Fields[name] = JgsValue.Str(type);
                return;
            }

            case "OptionalDependencies":
                options.Fields[name] = JgsValue.Str(OptionalProducts(value, line, col));
                return;

            case "Verbose":
                options.Fields[name] = Switch(value, name, InstallerOptionsClassName, line, col);
                return;

            case "InstallerSplash":
            case "InstallerIcon":
            case "InstallerLogo":
                options.Fields[name] = JgsValue.Str(ImageFile(name));
                return;

            case "AddRemoveProgramsIcon":
                options.Fields[name] = JgsValue.Str(Resets(value) ? "" : ImageFile(name));
                return;

            case "Shortcut":
            {
                if (Resets(value))
                {
                    options.Fields[name] = JgsValue.Str("");
                    return;
                }

                string shortcut = ScalarTextOnly(value, name, line, col);
                string absolute = Absolute(shortcut);
                if (!File.Exists(absolute) && !Directory.Exists(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:package:shared:cannotFindFileOrFolder", $"Cannot find file or folder:\n\n{shortcut}");
                }

                options.Fields[name] = JgsValue.Str(absolute);
                return;
            }

            case "AdditionalFiles":
            {
                if (value.Type == JgsType.Cell && value.AsCell.Any(static c => !IsTextValue(c)))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "Value must be a character vector, string array, or cell array of character vectors.");
                }

                options.Fields[name] = ItemList(value, name, here, line, col);
                return;
            }

            case "OutputDir":
            {
                string given = ScalarText(value, name, line, col);
                NoBadFolderCharacters(given, name, path: true, line, col);
                string absolute = Absolute(given);
                if (File.Exists(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:build:outputdir:mustBeFolder",
                        $"The value specified for '{name}':\n\n{absolute}\n\nis not a valid folder path.");
                }

                options.Fields[JgsBuiltinClasses.GivenOutputDir] = JgsValue.Str(given == "." || given.StartsWith(@".\", StringComparison.Ordinal) ? given : absolute);
                return;
            }

            case "DefaultInstallationDir":
            {
                // The property's own {mustBeNonempty} runs before the set method's propertyIsScalarText.
                if (HasNoElements(value))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonempty",
                        $"Error setting property '{name}' of class '{InstallerOptionsClassName}'. Value must not be empty.");
                }

                options.Fields[JgsBuiltinClasses.GivenDefaultInstallationDir] = JgsValue.Str(ScalarTextOnly(value, name, line, col));
                return;
            }

            case "AuthorName":
            case "AuthorEmail":
            case "AuthorCompany":
            case "Summary":
            case "Description":
            case "InstallationNotes":
                options.Fields[name] = JgsValue.Str(ScalarTextOnly(value, name, line, col));
                return;

            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{InstallerOptionsClassName}'.");
        }
    }

    /// <summary>R2025b's validate.optionalProducts: scalar text that is <c>all</c> or <c>none</c>.</summary>
    private static string OptionalProducts(JgsValue value, int line, int col)
    {
        string products = ScalarTextOnly(value, "OptionalDependencies", line, col);
        if (!OptionalProductsValues.Contains(products, StringComparer.Ordinal))
        {
            throw NotAMember(OptionalProductsValues, line, col);
        }

        return products;
    }

    // --- compiler.package.installer ----------------------------------------------------------

    /// <summary>
    /// R2025b's validateItems: the files and folders to package, as a cell, char row or string vector
    /// (or the Files of a results object), each existing, canonical, no two of one name.
    /// </summary>
    private static List<string> ValidateItems(JgsValue value, string here, int line, int col)
    {
        string kind = ClassOf(value, JgsDialect.Matlab);
        if (!(value.Type == JgsType.Cell || kind is "char" or "string"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                $"Expected input number 1, Files, to be one of these types:\n\ncell, char, string, compiler.build.Results\n\nInstead its type was {kind}.");
        }

        if (HasNoElements(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedNonempty", "Expected input number 1, Files, to be nonempty.");
        }

        int[] dims = SizeDims(value);
        if (dims.Length != 2 || (dims[0] != 1 && dims[1] != 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedVector", "Expected input number 1, Files, to be a vector.");
        }

        if (value.Type == JgsType.Cell && value.AsCell.Any(static c => !(ClassOf(c, JgsDialect.Matlab) == "char" && !c.IsCharMatrix)))
        {
            throw new JgsRuntimeException(line, col, "Compiler:package:shared:mustBeCellstr",
                "When specifying items to be packaged, the item list must be convertible to a cell array of char row vectors.");
        }

        string[] given = value.Type == JgsType.Cell ? [.. value.AsCell.Select(TextOf)]
            : value.IsStringArray ? [.. TextElementsOf(value) ?? []]
            : [TextOf(value)];
        var items = new List<string>(given.Length);
        var names = new List<string>(given.Length);
        foreach (string item in given)
        {
            string absolute = Path.GetFullPath(Path.IsPathRooted(item) ? item : Path.Combine(here, item));
            if (!File.Exists(absolute) && !Directory.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:package:shared:cannotFindFileOrFolder", $"Cannot find file or folder:\n\n{item}");
            }

            items.Add(Path.TrimEndingDirectorySeparator(absolute));
            names.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(absolute)));
        }

        var repeated = names.GroupBy(static n => n, StringComparer.Ordinal).Where(static g => g.Count() > 1).Select(static g => g.Key).Order(StringComparer.Ordinal).ToList();
        if (repeated.Count > 0)
        {
            var shown = new List<string>();
            foreach (string repeat in repeated)
            {
                shown.AddRange(given.Where((_, i) => names[i] == repeat));
            }

            throw new JgsRuntimeException(line, col, "Compiler:package:shared:noRepeats",
                $"Cannot package items with the same name.  The following inputs have the same name:\n\n{string.Join("\n", shown)}");
        }

        return items;
    }

    /// <summary>
    /// R2025b's resultsDataFromUserInput: the runtime products a results object, or a
    /// <c>requiredMCRProducts.txt</c> (warned about, as R2025b warns) or <c>buildresults.json</c>
    /// file names; several files or results only where allowed.
    /// </summary>
    private static List<int> RuntimeProducts(JGraphScriptGlobals host, JgsValue value, bool allowMultiple, string here, int line, int col)
    {
        var products = new List<int>();
        if (value.Type == JgsType.Object && !value.IsObjectArray && value.AsObject.Class.Name == JgsBuiltinClasses.BuildResults)
        {
            products.Add(BuildLogs.RuntimeProductId);
            return products;
        }

        if (value.IsObjectArray && value.AsObjectArray.Class.Name == JgsBuiltinClasses.BuildResults)
        {
            if (!allowMultiple && value.AsObjectArray.Length != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:expectedScalar", "Expected RuntimeDefinition to be scalar.");
            }

            products.Add(BuildLogs.RuntimeProductId);
            return products;
        }

        string kind = ClassOf(value, JgsDialect.Matlab);
        if (!(kind is "char" or "string" || value.Type == JgsType.Cell))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                $"Expected RuntimeDefinition to be one of these types:\n\nchar, string, cell, compiler.build.Results\n\nInstead its type was {kind}.");
        }

        string[] files = value.Type == JgsType.Cell ? [.. value.AsCell.Select(TextOf)]
            : value.IsStringArray ? [.. TextElementsOf(value) ?? []]
            : [TextOf(value)];
        if (!allowMultiple && files.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedScalartext", "Expected RuntimeDefinition to be a non-missing string scalar or character vector.");
        }

        bool warned = false;
        foreach (string file in files)
        {
            string absolute = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(here, file));
            if (!File.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{absolute}");
            }

            string text = File.ReadAllText(absolute);
            if (Path.GetExtension(absolute).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"ID\"\\s*:\\s*(\\d+)"))
                {
                    products.Add(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                }

                continue;
            }

            // R2025b warns once a call, however many files (custom.files.two).
            if (!warned)
            {
                warned = true;
                Warn(host, "Compiler:build:shared:mcrProductsTxtGoingAway",
                    "Using \"requiredMCRProducts.txt\" as an input will not be supported in a future release. Use \"buildresults.json\" instead.");
            }

            foreach (string entry in text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(entry, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:badProductEntry", $"The entry '{entry}' in the file:\n\n{absolute}\n\nis invalid.");
                }

                products.Add(id);
            }
        }

        return products;
    }

    /// <summary>The WiX tool, or R2025b-style refusal when this machine has none.</summary>
    private static WixTool WixOrRefuse(int line, int col) =>
        WixTool.Find() ?? throw new JgsRuntimeException(line, col, "JGraph:installer:noWix",
            $"WiX 6 was not found: it is looked for in toolbox\\compiler\\wix beside JGraph, in the folder {WixTool.FolderVariable} names and in the NuGet package cache (wixtoolset.sdk).");

    /// <summary>The runtime installer the delivery needs, or R2025b's couldNotFindRuntimeInstaller.</summary>
    private static string RuntimeInstallerOrRefuse(int line, int col) =>
        RuntimeInstaller.Find(RuntimeLayout.CurrentVersion) ?? throw new JgsRuntimeException(line, col, "Compiler:installer:couldNotFindRuntimeInstaller",
            $"Could not find {RuntimeProductName} installer.\n\nDownload it using the command: compiler.runtime.download");

    /// <summary>An options image as the installer's icon file: an <c>.ico</c> as it is, any other at the icon sizes (R2025b's processIcon).</summary>
    private static string InstallerIcon(string image, string folder, string name, int line, int col)
    {
        if (ImageFormatOf(image) == ".ico")
        {
            return image;
        }

        string own = Path.Combine(folder, name);
        return BuildIcon(image, own, line, col);
    }

    /// <summary>An image resized for the installer's pages, as PNG, written to <paramref name="target"/>.</summary>
    private static string InstallerPng(string image, int height, int width, string target, string scratch, string property, int line, int col)
    {
        AllowedImage(image, property, line, col);
        File.WriteAllBytes(target, ResizedPng(image, height, width, scratch));
        return target;
    }

    /// <summary>The splash as the 400-by-400 bitmap the installer program shows while it starts (the codec encodes no BMP, so the file is written here: 24 bits a pixel, rows bottom up).</summary>
    private static string InstallerSplashBmp(string image, string target, string scratch, int line, int col)
    {
        AllowedImage(image, "InstallerSplash", line, col);
        string png = Path.Combine(scratch, "splash_source.png");
        File.WriteAllBytes(png, ResizedPng(image, 400, 400, scratch));
        using ImageBuffer buffer = ImageCodec.Read(png);
        bool gray = buffer.Channels == 1;
        byte[] rgb = new byte[buffer.Height * buffer.Width * 3];
        for (int r = 0; r < buffer.Height; r++)
        {
            for (int c = 0; c < buffer.Width; c++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    rgb[(((r * buffer.Width) + c) * 3) + channel] =
                        (byte)System.Math.Clamp((int)System.Math.Round(buffer[r, c, gray ? 0 : channel] * 255.0), 0, 255);
                }
            }
        }

        ApplicationInstaller.WriteBmp(target, buffer.Width, buffer.Height, rgb);
        return target;
    }

    /// <summary>
    /// <c>compiler.package.installer(results | files, filepath, Name, Value | 'Options', opts)</c>:
    /// R2025b's installer.m over the shared inputParser, every check in its order (k18_package:
    /// web, named, zip, files.*, shortcut.*, options.*, icon.deleted, outdir.*), then the installer
    /// built by <see cref="ApplicationInstaller"/>.
    /// </summary>
    private static void PackageInstaller(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 2 + (2 * JgsBuiltinClasses.InstallerOptionNames.Length))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        string here = host.CurrentDirectory;
        List<string> items;
        JgsValue prodspec;
        List<JgsValue> optional;
        JgsObject? results = ResultsOf(args[0], line, col);
        if (results is not null)
        {
            items = ValidateItems(results.Fields["Files"], here, line, col);
            prodspec = args[0];
            optional = [.. args.Skip(1)];
        }
        else
        {
            items = ValidateItems(args[0], here, line, col);
            if (args.Count < 3)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            prodspec = args[1];
            optional = [.. args.Skip(2)];
        }

        List<int> products = RuntimeProducts(host, prodspec, allowMultiple: false, here, line, col);
        JgsObject io;
        if (NamesProperty(optional, "Options", 0))
        {
            if (optional.Count > 2)
            {
                throw new JgsRuntimeException(line, col, "Compiler:package:shared:onlyOptions", "No other name-value arguments can be used with the 'Options' parameter.");
            }

            JgsValue given = optional[1];
            if (!(given.Type == JgsType.Object && given.AsObject.Class.Name == InstallerOptionsClassName))
            {
                string kind = given.Type == JgsType.Object ? given.AsObject.Class.Name : ClassOf(given, JgsDialect.Matlab);
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                    $"The value of 'Options' is invalid. Expected input to be one of these types:\n\n{InstallerOptionsClassName}\n\nInstead its type was {kind}.");
            }

            io = given.AsObject;
        }
        else
        {
            io = NewInstallerOptions(interpreter, results is not null ? args : optional, line, col);
        }

        string Text(string property) => TextOf(io.Fields[property]);
        bool On(string property) => io.Fields[property].AsExternalOrNull() is OnOffSwitchStateValue { AllOn: true };
        items = [.. items.Concat(io.Fields["AdditionalFiles"].AsCell.Select(TextOf)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // The shortcut must be one of the items or inside a folder among them; its name is then relative.
        string shortcut = "";
        if (Text("Shortcut").Length > 0)
        {
            string wanted = Text("Shortcut");
            foreach (string item in items)
            {
                if (string.Equals(item, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    shortcut = Path.GetFileName(item);
                    break;
                }

                if (Directory.Exists(item) && wanted.StartsWith(item + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && (File.Exists(wanted) || Directory.Exists(wanted)))
                {
                    shortcut = Path.Combine(Path.GetFileName(item), wanted[(item.Length + 1)..]);
                    break;
                }
            }

            if (shortcut.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "Compiler:installer:shortcutNotPackaged",
                    "'Shortcut' should be either one of files to be packaged, or one of the folders owning a file to be packaged.");
            }
        }

        if (shortcut.Length == 0)
        {
            string[] exes = [.. items.Where(static i => i.EndsWith(".exe", StringComparison.Ordinal))];
            if (exes.Length > 0)
            {
                Warn(host, "Compiler:installer:exeButNoShortcut",
                    $"The following executables are being packaged:\n\n{string.Join("\n", exes)}\n\nIn order to show the application in the Windows Apps & Features list, set the 'Shortcut' option to the name of the executable that launches the application.");
            }
        }

        string delivery = Text("RuntimeDelivery");
        if (delivery != "none" && products.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "Compiler:installer:emptyRequiredMCRProducts", "The required runtime product information is either empty or invalid.");
        }

        foreach (string property in new[] { "InstallerIcon", "InstallerLogo", "InstallerSplash", "AddRemoveProgramsIcon" })
        {
            string file = Text(property);
            if ((property != "AddRemoveProgramsIcon" || file.Length > 0) && !File.Exists(file))
            {
                throw new JgsRuntimeException(line, col, "Compiler:installer:propDoesNotExist", $"The file\n\n'{file}'\n\nin the property '{property}' does not exist.");
            }
        }

        string outputDir = Path.GetFullPath(Path.Combine(here, TextOf(InstallerOptionDerived("OutputDir", io))));
        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:package:shared:badPermissions", $"Unable to read or write to the folder:\n\n    {outputDir}");
        }

        WixTool wix = WixOrRefuse(line, col);
        string? runtimeMsi = delivery == "none" ? null : RuntimeInstallerOrRefuse(line, col);
        if (On("Verbose"))
        {
            host.WriteOut("Packaging files\n");
            if (delivery == "installer")
            {
                host.WriteOut($"Packaging the {RuntimeProductName}\n");
            }
            else if (delivery == "none")
            {
                host.WriteOut($"No {RuntimeProductName} will be installed during application installation.\n");
            }
        }

        string scratch = Path.Combine(Path.GetTempPath(), "jgraph-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var spec = new InstallerSpec
            {
                ApplicationName = Text("ApplicationName"),
                InstallerName = TextOf(InstallerOptionDerived("InstallerName", io)),
                Version = Text("Version"),
                RuntimeDelivery = delivery switch { "installer" => InstallerRuntimeDelivery.Installer, "none" => InstallerRuntimeDelivery.None, _ => InstallerRuntimeDelivery.Web },
                PackageType = Text("PackageType") == "zip" ? InstallerPackageType.Zip : InstallerPackageType.Auto,
                Items = items,
                Shortcut = shortcut,
                DefaultInstallationDir = TextOf(InstallerOptionDerived("DefaultInstallationDir", io)),
                IconFile = InstallerIcon(Text("InstallerIcon"), scratch, "icon", line, col),
                AddRemoveProgramsIcon = Text("AddRemoveProgramsIcon").Length > 0 ? InstallerIcon(Text("AddRemoveProgramsIcon"), scratch, "arp", line, col) : null,
                LogoFile = InstallerPng(Text("InstallerLogo"), 112, 290, Path.Combine(scratch, "logo.png"), scratch, "InstallerLogo", line, col),
                SplashFile = InstallerSplashBmp(Text("InstallerSplash"), Path.Combine(scratch, "splash.bmp"), scratch, line, col),
                AuthorName = Text("AuthorName"),
                AuthorEmail = Text("AuthorEmail"),
                AuthorCompany = Text("AuthorCompany"),
                Summary = Text("Summary"),
                Description = Text("Description"),
                InstallationNotes = Text("InstallationNotes"),
                OutputDir = outputDir,
                RuntimeVersion = RuntimeLayout.CurrentVersion,
                RuntimeInstallerFile = runtimeMsi,
            };
            string installer = BuildInstaller(spec, wix, line, col);
            if (On("Verbose"))
            {
                host.WriteOut($"Installer created at {installer}\n");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string BuildInstaller(InstallerSpec spec, WixTool wix, int line, int col)
    {
        try
        {
            return ApplicationInstaller.Build(spec, wix);
        }
        catch (WixException ex)
        {
            string tail = ex.Output.Length > 4000 ? ex.Output[^4000..] : ex.Output;
            throw new JgsRuntimeException(line, col, "JGraph:installer:wixFailed", $"{ex.Message}\n{tail}".TrimEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:installer:fileCreateFailed", $"Failed to create file:\n\n{Path.Combine(spec.OutputDir, spec.InstallerName)}\n\n{ex.Message}");
        }
    }

    // --- compiler.runtime.customInstaller ------------------------------------------------------

    private static readonly string[] CustomInstallerParameters = ["OptionalDependencies", "OutputDir", "PackageType", "RuntimeDelivery"];

    /// <summary>
    /// <c>compiler.runtime.customInstaller(installerName, results | filepath, Name, Value)</c>: a
    /// runtime-only installer, R2025b's checks in its order (k18_package: custom.*): the name as
    /// cliName checks it, the runtimes a results object or vector or the files naming them, the four
    /// parameters, 'none' refused, zip refused with web, the runtime's MSI needed either way.
    /// </summary>
    private static void CustomInstaller(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        // R2025b's narginchk(2, 2 + 2 * 3): its numNVPairs counts three of the four parameters (custom.nargs).
        if (args.Count > 8)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        string here = host.CurrentDirectory;
        string installerName = CliName(args[0], "InstallerName", line, col);
        _ = RuntimeProducts(host, args[1], allowMultiple: true, here, line, col);
        var given = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 2; i < args.Count; i += 2)
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

            given[ParameterName(asked, CustomInstallerParameters, line, col)] = args[i + 1];
        }

        if (given.TryGetValue("OptionalDependencies", out JgsValue? optional))
        {
            _ = OptionalProducts(optional, line, col);
        }

        string outputDir = @".\" + installerName;
        if (given.TryGetValue("OutputDir", out JgsValue? dir))
        {
            string text = ScalarText(dir, "OutputDir", line, col);
            NoBadFolderCharacters(text, "OutputDir", path: true, line, col);
            string absolute = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(here, text));
            if (File.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:build:outputdir:mustBeFolder", $"The value specified for 'OutputDir':\n\n{absolute}\n\nis not a valid folder path.");
            }

            outputDir = absolute;
        }

        string folder = Path.GetFullPath(Path.Combine(here, outputDir));
        Directory.CreateDirectory(folder);
        string packageType = "auto";
        if (given.TryGetValue("PackageType", out JgsValue? type))
        {
            packageType = ScalarTextOnly(type, "PackageType", line, col);
            if (!PackageTypeValues.Contains(packageType, StringComparer.Ordinal))
            {
                throw NotAMember(PackageTypeValues, line, col);
            }
        }

        string delivery = "web";
        if (given.TryGetValue("RuntimeDelivery", out JgsValue? deliveryValue))
        {
            delivery = ScalarTextOnly(deliveryValue, "RuntimeDeliveryValues", line, col);
            if (delivery == "none")
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:noneNotSupported",
                    "'none' is not a supported value of 'RuntimeDelivery' for the function 'compiler.runtime.customInstaller'.");
            }

            if (!RuntimeDeliveryValues.Contains(delivery, StringComparer.Ordinal))
            {
                throw NotAMember(["installer", "web"], line, col);
            }
        }

        if (packageType == "zip" && delivery == "web")
        {
            throw new JgsRuntimeException(line, col, "Compiler:package:shared:noZipWithWeb", "Cannot set \"PackageType\" to \"zip\" when \"RuntimeDelivery\" is set to \"web\".");
        }

        WixTool wix = WixOrRefuse(line, col);
        string runtimeMsi = RuntimeInstallerOrRefuse(line, col);
        string scratch = Path.Combine(Path.GetTempPath(), "jgraph-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var spec = new InstallerSpec
            {
                ApplicationName = RuntimeProductName,
                InstallerName = installerName,
                Version = RuntimeLayout.CurrentVersion,
                RuntimeDelivery = delivery == "installer" ? InstallerRuntimeDelivery.Installer : InstallerRuntimeDelivery.Web,
                PackageType = packageType == "zip" ? InstallerPackageType.Zip : InstallerPackageType.Auto,
                IconFile = InstallerIcon(PackagingResource("default_icon_48.png"), scratch, "icon", line, col),
                LogoFile = InstallerPng(PackagingResource("default_logo.png"), 112, 290, Path.Combine(scratch, "logo.png"), scratch, "InstallerLogo", line, col),
                SplashFile = InstallerSplashBmp(PackagingResource("default_splash.png"), Path.Combine(scratch, "splash.bmp"), scratch, line, col),
                OutputDir = folder,
                RuntimeVersion = RuntimeLayout.CurrentVersion,
                RuntimeInstallerFile = runtimeMsi,
                RuntimeOnly = true,
            };
            _ = BuildInstaller(spec, wix, line, col);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
