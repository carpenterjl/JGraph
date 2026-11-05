using JGraph.Data;
using JGraph.Deploy.Apps;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Toolboxes and apps as add-ons (App Designer window plan W13): <c>matlab.addons.toolbox</c>'s
/// <c>ToolboxOptions</c>, <c>packageToolbox</c>, <c>toolboxVersion</c>, <c>installToolbox</c>,
/// <c>installedToolboxes</c> and <c>uninstallToolbox</c>; <c>matlab.apputil</c>'s <c>create</c>, <c>package</c>,
/// <c>install</c>, <c>uninstall</c>, <c>run</c> and <c>getInstalledAppInfo</c>; and <c>matlab.addons</c>'
/// <c>installedAddons</c>, <c>install</c>, <c>uninstall</c> and <c>isAddonEnabled</c>, their checks and refusals in
/// R2025b's words (its <c>ToolboxOptions.m</c>, <c>packageToolbox.m</c>, <c>toolboxVersion.m</c>, the
/// <c>matlab.apputil</c> and <c>matlab.addons</c> files, read in place, and probe <c>w13_apps</c>). The packages are
/// <see cref="ToolboxPackage"/>'s and <see cref="MlappInstallPackage"/>'s; the installs <see cref="InstalledAddOns"/>'.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterAddOnBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> AddOnBuiltinNames { get; } =
    [
        "matlab.addons.toolbox.ToolboxOptions", "matlab.addons.toolbox.packageToolbox", "matlab.addons.toolbox.toolboxVersion",
        "matlab.addons.toolbox.installToolbox", "matlab.addons.toolbox.installedToolboxes", "matlab.addons.toolbox.uninstallToolbox",
        "matlab.apputil.create", "matlab.apputil.package", "matlab.apputil.install", "matlab.apputil.uninstall", "matlab.apputil.run",
        "matlab.apputil.getInstalledAppInfo",
        "matlab.addons.installedAddons", "matlab.addons.install", "matlab.addons.uninstall", "matlab.addons.isAddonEnabled",
    ];

    private const string ToolboxOptionsClassName = JgsBuiltinClasses.ToolboxOptions;
    private const string Packaging = "MATLAB:toolbox_packaging:packaging:";
    private const string CurrentRelease = "R2025b";
    private const string EarliestRelease = "R2014b";

    private static void RegisterAddOnBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(ToolboxOptionsClassName, JgsValue.Function(new BuiltinFunction(ToolboxOptionsClassName,
            (args, line, col) => JgsValue.Object(NewToolboxOptions(interpreter, host, args, line, col)))
        {
            KeepsStringArguments = true,
        }));
        Statement(env, "matlab.addons.toolbox.packageToolbox", (args, line, col) => PackageToolbox(host, args, line, col));
        Function(env, "matlab.addons.toolbox.toolboxVersion", (args, line, col) => ToolboxVersion(host, args, line, col));
        Function(env, "matlab.addons.toolbox.installToolbox", (args, line, col) => InstallToolbox(interpreter, host, args, line, col));
        Function(env, "matlab.addons.toolbox.installedToolboxes", (_, _, _) => InstalledToolboxes());
        Statement(env, "matlab.addons.toolbox.uninstallToolbox", (args, line, col) => UninstallToolbox(interpreter, host, args, line, col));
        Statement(env, "matlab.apputil.create", (args, line, col) => AppUtilCreate(host, args, line, col));
        Statement(env, "matlab.apputil.package", (args, line, col) => AppUtilPackage(host, args, line, col));
        Function(env, "matlab.apputil.install", (args, line, col) => AppUtilInstall(host, args, line, col));
        Statement(env, "matlab.apputil.uninstall", (args, line, col) => AppUtilUninstall(args, line, col));
        Statement(env, "matlab.apputil.run", (args, line, col) => AppUtilRun(interpreter, args, line, col));
        env.Builtins.Register("matlab.apputil.getInstalledAppInfo", JgsValue.Function(new BuiltinFunction("matlab.apputil.getInstalledAppInfo", (args, line, col) => InstalledAppInfo())
        {
            // With no output, R2025b prints the ids and names; with one it answers the struct: the count is part of what it does,
            // and a bare mention of the dotted name is a call (jgraph.usb.tree's rule).
            BindsAnsAsStatement = false,
            KnowsWhenDiscarded = true,
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                if (wanted == 0)
                {
                    PrintInstalledAppInfo(host);
                    return [];
                }

                return [InstalledAppInfo()];
            },
        }));
        Function(env, "matlab.addons.installedAddons", (_, _, _) => InstalledAddonsTable());
        Function(env, "matlab.addons.install", (args, line, col) => AddonsInstall(interpreter, host, args, line, col));
        Statement(env, "matlab.addons.uninstall", (args, line, col) => AddonsUninstall(interpreter, args, line, col));
        Function(env, "matlab.addons.isAddonEnabled", (args, line, col) => IsAddonEnabled(args, line, col));
    }

    private static void Function(JgsEnvironment env, string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

    private static void Statement(JgsEnvironment env, string name, Action<IReadOnlyList<JgsValue>, int, int> body) =>
        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
        {
            body(args, line, col);
            return JgsValue.Null;
        })
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            // An m-file with no outputs: x = name(...) is refused before the body runs, as R2025b refuses it (W15 found
            // the single-output road reaching the body).
            MatlabOutputCount = 0,
            IsMatlabFile = true,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (wanted > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                body(args, line, col);
                return [];
            },
        }));

    private static JgsRuntimeException Packaged(string key, string text, int line, int col) => new(line, col, Packaging + key, text);

    // --- ToolboxOptions ------------------------------------------------------------------------------------------

    /// <summary>The factory value of a toolbox option (the folder and the identifier come from the constructor).</summary>
    internal static JgsValue ToolboxOptionDefault(string name) => name switch
    {
        "AppGalleryFiles" or "ToolboxFiles" or "ToolboxJavaPath" or "ToolboxMatlabPath" => EmptyStrings(),
        "OutputFile" => JgsValue.StringScalar("mytoolbox.mltbx"),
        "RequiredAdditionalSoftware" or "RequiredAddons" => JgsValue.StructArray(new JgsStructArray([], []), 0, 0),
        "SupportedPlatforms" => Platforms(true, true, true, true),
        "ToolboxName" => JgsValue.StringScalar("My Toolbox"),
        "ToolboxVersion" => JgsValue.StringScalar("1.0.0"),
        _ => JgsValue.StringScalar(string.Empty),
    };

    private static JgsValue EmptyStrings() => JgsValue.StringArray([], 0, 0);

    private static JgsValue Strings(IReadOnlyList<string> texts) =>
        texts.Count == 0 ? EmptyStrings() : JgsValue.StringArray([.. texts.Select(JgsValue.Str)], texts.Count, 1);

    private static JgsValue Platforms(bool win, bool linux, bool mac, bool online) => JgsValue.Struct(new Dictionary<string, JgsValue>
    {
        ["Win64"] = JgsValue.Bool(win),
        ["Glnxa64"] = JgsValue.Bool(linux),
        ["Maci64"] = JgsValue.Bool(mac),
        ["MatlabOnline"] = JgsValue.Bool(online),
    });

    /// <summary>The texts a list option holds: a string array, a cell of texts or one char row.</summary>
    private static List<string> TextsOf(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            return [.. value.AsCell.Select(TextOf)];
        }

        if (value.IsStringArray)
        {
            return [.. value.AsArray.Select(TextOf)];
        }

        if (IsTextScalar(value))
        {
            string text = TextOf(value);
            return text.Length == 0 ? [] : [text];
        }

        return [];
    }

    /// <summary>
    /// <c>ToolboxOptions(path, identifier, Name, Value, ...)</c>: the folder and its identifier checked in R2025b's words, then
    /// <c>ToolboxFiles</c> first (the folder's own files when none are given, the path derived from them only then), the
    /// output file beside the folder, the name the folder's, and the other pairs in order.
    /// </summary>
    /// <summary>The class's own constructor (the dotted name resolves to the class once it is declared): the same as the builtin's.</summary>
    internal static JgsObject ConstructToolboxOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col) =>
        NewToolboxOptions(interpreter, interpreter.Host as JGraphScriptGlobals ?? throw new JgsRuntimeException(line, col, "The session has no host."), args, line, col);

    private static JgsObject NewToolboxOptions(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string here = host.CurrentDirectory;
        if (args.Count == 0 || !IsTextScalar(args[0]) || TextOf(args[0]).Length == 0)
        {
            throw Packaged("ToolboxPathCannotBeEmpty", "Toolbox project or folder must be non-empty.", line, col);
        }

        string path = TextOf(args[0]);
        string absolute = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        if (File.Exists(absolute))
        {
            // A toolbox project file: JGraph has no toolbox projects, so only its kind is told apart.
            if (Path.GetExtension(absolute).Equals(".prj", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count > 1)
                {
                    throw Packaged("ToolboxPRJAdditionalArguments", "No additional arguments are permitted when specifying a toolbox project file.", line, col);
                }

                throw Packaged("InvalidToolboxProjectFile", $"Invalid MATLAB toolbox project '{absolute}'.", line, col);
            }

            throw Packaged("NotValidToolboxPRJ", "Project file must be a toolbox project (PRJ) file.", line, col);
        }

        if (args.Count < 2)
        {
            throw Packaged("ToolboxIdentifierMissing", "Unique toolbox identifier must be specified.", line, col);
        }

        if (!Directory.Exists(absolute))
        {
            throw Packaged("ToolboxRootNotFound", $"Toolbox folder \"{absolute}\" does not exist.", line, col);
        }

        if (!IsTextScalar(args[1]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar", "Value must be a character vector or string scalar.");
        }

        string identifier = TextOf(args[1]);
        if (ToolboxPackageOptions.WhyNotIdentifier(identifier) is { } why)
        {
            throw Packaged("InvalidIdentifier", why, line, col);
        }

        var pairs = new List<(string Name, JgsValue Value)>();
        for (int i = 2; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]) || args[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue", $"No value was given for '{TextOf(args[i])}'. Name-value pair arguments require a name followed by a value.");
            }

            pairs.Add((ParameterName(TextOf(args[i]), JgsBuiltinClasses.ToolboxOptionSettable, line, col), args[i + 1]));
        }

        JgsClass definition = interpreter.BuiltinClass(ToolboxOptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{ToolboxOptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        string folder = absolute.TrimEnd(Path.DirectorySeparatorChar);
        options.Fields["ToolboxFolder"] = JgsValue.StringScalar(folder);
        options.Fields["Identifier"] = JgsValue.StringScalar(identifier);
        options.Fields["ToolboxName"] = JgsValue.StringScalar(Path.GetFileName(folder));
        options.Fields["OutputFile"] = JgsValue.StringScalar(Path.Combine(Path.GetDirectoryName(folder) ?? folder, Path.GetFileName(folder) + ".mltbx"));
        if (pairs.FirstOrDefault(p => p.Name == "ToolboxFiles") is { Name: "ToolboxFiles" } files)
        {
            SetToolboxOption(interpreter, options, "ToolboxFiles", files.Value, line, col);
        }
        else
        {
            // deriveFilesFromRoot and deriveMatlabPathFromRoot: every file under the folder but the project's own, and the folders holding them.
            List<string> found = FolderFiles(folder);
            if (found.Count == 0)
            {
                throw Packaged("ToolboxFolderCannotBeEmpty", "ToolboxFiles cannot be empty.", line, col);
            }

            options.Fields["ToolboxFiles"] = Strings(found);
            if (!pairs.Any(p => p.Name == "ToolboxMatlabPath"))
            {
                options.Fields["ToolboxMatlabPath"] = Strings(PathFoldersOf(found));
            }
        }

        foreach ((string name, JgsValue value) in pairs)
        {
            if (name != "ToolboxFiles")
            {
                SetToolboxOption(interpreter, options, name, value, line, col);
            }
        }

        return options;
    }

    /// <summary>The folder's files for a toolbox with none named (<c>DEFAULT_FILE_EXCLUSIONS</c>: the project's resources, .prj, .git, .svn, .buildtool, .asv).</summary>
    private static List<string> FolderFiles(string folder)
    {
        var files = new List<string>();
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.EndsWith(".prj", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".asv", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("resources/project/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(".git/", StringComparison.Ordinal)
                || relative.Contains("/.git/", StringComparison.Ordinal) || relative.Contains(".svn/", StringComparison.Ordinal) || relative.Contains(".buildtool/", StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(file);
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>The folders holding the files, those a path may hold (not private, class or package folders), each once in order.</summary>
    private static List<string> PathFoldersOf(IReadOnlyList<string> files)
    {
        var folders = new List<string>();
        foreach (string file in files)
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(file));
            if (folder is null || folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = Path.GetFileName(folder);
            if (name.StartsWith('+') || name.StartsWith('@') || name.Equals("private", StringComparison.OrdinalIgnoreCase)
                || folder.Split(Path.DirectorySeparatorChar).Any(static part => part.StartsWith('+') || part.StartsWith('@') || part.Equals("private", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            folders.Add(folder);
        }

        return folders;
    }

    /// <summary>Sets <paramref name="name"/> on <paramref name="options"/>, checked as R2025b's set method checks it.</summary>
    internal static void SetToolboxOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        string Text() => ScalarTextOnly(value, name, line, col);
        List<string> Files() => TextsOf(value).Select(Absolute).ToList();
        switch (name)
        {
            case "Identifier":
            case "ToolboxFolder":
                throw new JgsRuntimeException(line, col, "MATLAB:class:SetProhibited", $"You cannot set the read-only property '{name}' of ToolboxOptions.");

            case "ToolboxName":
            {
                string text = Text().Trim();
                if (text.Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "Value must be text with one or more characters.");
                }

                if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw Packaged("PropertyInvalidFilesystemCharacters", $"Value for 'ToolboxName' contains invalid characters: {text}", line, col);
                }

                options.Fields[name] = JgsValue.StringScalar(text);
                return;
            }

            case "ToolboxVersion":
            {
                string text = Text();
                if (ToolboxPackageOptions.WhyNotVersion(text) is { } why)
                {
                    throw Packaged("InvalidToolboxVersion", why, line, col);
                }

                options.Fields[name] = JgsValue.StringScalar(text);
                return;
            }

            case "AuthorCompany" or "AuthorEmail" or "AuthorName" or "Description" or "Summary":
                options.Fields[name] = JgsValue.StringScalar(IsTextScalar(value) ? TextOf(value) : Text());
                return;

            case "OutputFile":
            {
                if (!IsTextScalar(value) || TextOf(value).Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                        $"Error setting property 'OutputFile' of class '{ToolboxOptionsClassName}'. Value must be text with one or more characters.");
                }

                string file = Absolute(TextOf(value));
                if (!file.EndsWith(".mltbx", StringComparison.OrdinalIgnoreCase))
                {
                    file += ".mltbx";
                }

                options.Fields[name] = JgsValue.StringScalar(file);
                return;
            }

            case "MinimumMatlabRelease" or "MaximumMatlabRelease":
            {
                string text = IsTextScalar(value) ? TextOf(value) : Text();
                if (text.Length > 0 && !ToolboxPackageOptions.IsReleaseName(text))
                {
                    throw Packaged("InvalidReleaseString", "Release must be a string with the format 'RXXXXx', for example 'R2022b'.", line, col);
                }

                string min = name == "MinimumMatlabRelease" ? text : TextOf(options.Fields["MinimumMatlabRelease"]);
                string max = name == "MaximumMatlabRelease" ? text : TextOf(options.Fields["MaximumMatlabRelease"]);
                if (name == "MinimumMatlabRelease" && text.Length > 0 && (!ToolboxPackageOptions.ReleaseAtLeast(text, EarliestRelease) || !ToolboxPackageOptions.ReleaseAtLeast(max.Length > 0 ? max : CurrentRelease, text)))
                {
                    throw Packaged("InvalidMinimumRelease", $"Minimum release must either be between {EarliestRelease} and the specified maximum release ({(max.Length > 0 ? max : CurrentRelease)}), or be empty ('').", line, col);
                }

                if (name == "MaximumMatlabRelease" && text.Length > 0 && (!ToolboxPackageOptions.ReleaseAtLeast(CurrentRelease, text) || !ToolboxPackageOptions.ReleaseAtLeast(text, min.Length > 0 ? min : EarliestRelease)))
                {
                    throw Packaged("InvalidMaximumRelease", $"Maximum release must either be between the specified minimum release ({(min.Length > 0 ? min : EarliestRelease)}) and the current MATLAB release ({CurrentRelease}), or be empty ('').", line, col);
                }

                options.Fields[name] = JgsValue.StringScalar(text);
                return;
            }

            case "ToolboxFiles":
            {
                List<string> files = TextsOf(value);
                if (files.Count == 0)
                {
                    throw Packaged("ToolboxFolderCannotBeEmpty", "ToolboxFiles cannot be empty.", line, col);
                }

                var sorted = files.Select(Absolute).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static f => f, StringComparer.Ordinal).ToList();
                foreach (string file in sorted)
                {
                    if (!File.Exists(file) && !Directory.Exists(file))
                    {
                        throw Packaged("FileMustExist", $"Specified file does not exist: {file}", line, col);
                    }
                }

                options.Fields[name] = Strings(sorted);
                return;
            }

            case "AppGalleryFiles":
            {
                List<string> files = Files().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static f => f, StringComparer.Ordinal).ToList();
                List<string> packaged = TextsOf(options.Fields["ToolboxFiles"]);
                foreach (string file in files)
                {
                    if (!ToolboxPackageOptions.IsGalleryFile(file))
                    {
                        throw Packaged("InvalidAppGalleryFile", $"Invalid file '{file}'. App gallery files must be valid MATLAB code files (.m, .mlapp, .mlx, .p)", line, col);
                    }

                    if (!File.Exists(file))
                    {
                        throw Packaged("FileMustExist", $"Specified file does not exist: {file}", line, col);
                    }

                    if (!packaged.Contains(file, StringComparer.OrdinalIgnoreCase))
                    {
                        throw Packaged("FileMissingFromToolboxFiles", $"File is missing from the ToolboxFiles list: {file}", line, col);
                    }
                }

                options.Fields[name] = Strings(files);
                return;
            }

            case "ToolboxMatlabPath" or "ToolboxJavaPath":
            {
                List<string> folders = Files().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (name == "ToolboxMatlabPath")
                {
                    List<string> packaged = TextsOf(options.Fields["ToolboxFiles"]);
                    foreach (string folder in folders)
                    {
                        string leaf = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
                        if (leaf.StartsWith('+') || leaf.StartsWith('@') || leaf.Equals("private", StringComparison.OrdinalIgnoreCase))
                        {
                            throw Packaged("NotValidPathFolder", $"MATLAB path folder cannot begin with \"+\", \"@\", or be private: {folder}", line, col);
                        }

                        if (!packaged.Any(f => string.Equals(Path.GetDirectoryName(f), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
                        {
                            throw Packaged("ParentFolderMissingFromToolboxFiles", $"MATLAB path folder does not contain any files that will be packaged: {folder}", line, col);
                        }
                    }
                }

                options.Fields[name] = Strings(folders);
                return;
            }

            case "ToolboxImageFile":
            {
                string text = IsTextScalar(value) ? TextOf(value) : Text();
                if (text.Length == 0)
                {
                    options.Fields[name] = JgsValue.StringScalar(string.Empty);
                    return;
                }

                if (!ToolboxPackageOptions.IsImageFile(text))
                {
                    throw Packaged("InvalidToolboxImageFileFormat", $"Unsupported toolbox image file format '{text}'.  Format must be 'bmp', 'gif', 'jpg', 'jpeg', or 'png'.", line, col);
                }

                string file = Absolute(text);
                if (!File.Exists(file))
                {
                    throw Packaged("InvalidToolboxImageFile", $"Unrecognized toolbox image file format: {text}", line, col);
                }

                options.Fields[name] = JgsValue.StringScalar(file);
                return;
            }

            case "ToolboxGettingStartedGuide":
            {
                string text = IsTextScalar(value) ? TextOf(value) : Text();
                if (text.Length == 0)
                {
                    options.Fields[name] = JgsValue.StringScalar(string.Empty);
                    return;
                }

                string file = Absolute(text);
                if (!File.Exists(file) || Path.GetExtension(file).ToLowerInvariant() is not (".m" or ".mlx"))
                {
                    throw Packaged("InvalidGettingStarted", $"Invalid getting started guide.  File '{text}' must be a MATLAB script (M) or Live Script (MLX) file.", line, col);
                }

                options.Fields[name] = JgsValue.StringScalar(file);
                return;
            }

            case "SupportedPlatforms":
            {
                if (value.Type != JgsType.Struct || value.IsStructArray)
                {
                    throw Packaged("InvalidSupportedPlatforms", "SupportedPlatforms must be a scalar struct with Win64, Glnxa64, Maci64, and MatlabOnline fields containing logical values.", line, col);
                }

                Dictionary<string, JgsValue> fields = value.AsStruct;
                foreach (string field in new[] { "Win64", "Glnxa64", "Maci64", "MatlabOnline" })
                {
                    if (!fields.ContainsKey(field))
                    {
                        throw Packaged("RequiredFieldMissing", $"Required field '{field}' missing from SupportedPlatforms struct.", line, col);
                    }
                }

                options.Fields[name] = Platforms(IsTrue(fields["Win64"]), IsTrue(fields["Glnxa64"]), IsTrue(fields["Maci64"]), IsTrue(fields["MatlabOnline"]));
                return;
            }

            case "RequiredAddons" or "RequiredAdditionalSoftware":
                if (value.Type != JgsType.Struct && !HasNoElements(value))
                {
                    throw Packaged("Invalid" + name, name == "RequiredAddons"
                        ? "RequiredAddons must be a struct with Name, Identifier, EarliestVersion, LatestVersion, and DownloadURL fields containing string values."
                        : "RequiredAdditionalSoftware must be a struct with Name, Platform, DownloadURL, and LicenseURL fields containing string values.", line, col);
                }

                options.Fields[name] = value.Type == JgsType.Struct ? JgsValue.Share(value) : ToolboxOptionDefault(name);
                return;

            default:
                throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass", $"'{name}' is not a property of '{ToolboxOptionsClassName}'.");
        }
    }

    /// <summary>A platform flag: a logical or number as MATLAB reads one in a condition, or the word true.</summary>
    private static bool IsTrue(JgsValue value) =>
        IsTextScalar(value) ? TextOf(value).Equals("true", StringComparison.OrdinalIgnoreCase) : !HasNoElements(value) && value.IsTruthy;

    /// <summary>The options object as the writer takes it.</summary>
    private static ToolboxPackageOptions OptionsOf(JgsObject options)
    {
        var packaged = new ToolboxPackageOptions(TextOf(options.Fields["ToolboxFolder"]), TextOf(options.Fields["Identifier"]))
        {
            ToolboxName = TextOf(options.Fields["ToolboxName"]),
            ToolboxVersion = TextOf(options.Fields["ToolboxVersion"]),
            AuthorName = TextOf(options.Fields["AuthorName"]),
            AuthorEmail = TextOf(options.Fields["AuthorEmail"]),
            AuthorCompany = TextOf(options.Fields["AuthorCompany"]),
            Summary = TextOf(options.Fields["Summary"]),
            Description = TextOf(options.Fields["Description"]),
            ToolboxImageFile = TextOf(options.Fields["ToolboxImageFile"]),
            ToolboxGettingStartedGuide = TextOf(options.Fields["ToolboxGettingStartedGuide"]),
            OutputFile = TextOf(options.Fields["OutputFile"]),
            MinimumMatlabRelease = TextOf(options.Fields["MinimumMatlabRelease"]),
            MaximumMatlabRelease = TextOf(options.Fields["MaximumMatlabRelease"]),
        };
        packaged.ToolboxFiles.AddRange(TextsOf(options.Fields["ToolboxFiles"]));
        packaged.ToolboxMatlabPath.AddRange(TextsOf(options.Fields["ToolboxMatlabPath"]));
        packaged.AppGalleryFiles.AddRange(TextsOf(options.Fields["AppGalleryFiles"]));
        if (options.Fields["SupportedPlatforms"] is { Type: JgsType.Struct } platforms && !platforms.IsStructArray)
        {
            Dictionary<string, JgsValue> fields = platforms.AsStruct;
            packaged.Win64 = IsTrue(fields["Win64"]);
            packaged.Glnxa64 = IsTrue(fields["Glnxa64"]);
            packaged.Maci64 = IsTrue(fields["Maci64"]);
            packaged.MatlabOnline = IsTrue(fields["MatlabOnline"]);
        }

        return packaged;
    }

    // --- packageToolbox, toolboxVersion -----------------------------------------------------------------------------

    /// <summary><c>packageToolbox(opts)</c> writes the package; <c>packageToolbox(prj[, out])</c> wants a toolbox project, which JGraph has none of.</summary>
    private static void PackageToolbox(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs", args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == ToolboxOptionsClassName)
        {
            if (args.Count == 2)
            {
                throw Packaged("NoOutputWithOptions", "Specify the output file location in the toolbox options object.", line, col);
            }

            ToolboxPackageOptions options = OptionsOf(args[0].AsObject);
            if (!Directory.Exists(options.ToolboxFolder))
            {
                throw Packaged("ToolboxRootNotFound", $"Toolbox folder \"{options.ToolboxFolder}\" does not exist.", line, col);
            }

            if (options.ToolboxFiles.Count == 0)
            {
                throw Packaged("NoToolboxFiles", "ToolboxFiles must contain at least one file.", line, col);
            }

            string[] missing = [.. options.ToolboxFiles.Where(static f => !File.Exists(f) && !Directory.Exists(f))];
            if (missing.Length == options.ToolboxFiles.Count)
            {
                throw Packaged("NoFilesExist", "ToolboxFiles contains no files that exist.", line, col);
            }

            if (missing.Length > 0)
            {
                Warn(host, Packaging + "FilesDoNotExistWarning", "The following files are omitted from the package because they no longer exist: \n" + string.Join("\n", missing));
            }

            try
            {
                ToolboxPackage.Write(options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                throw Packaged("PackagingError", $"Unable to package the project '{options.ToolboxFolder}'. {ex.Message} ", line, col);
            }

            return;
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType", "Expected input number 1, ProjectFile, to be one of these types:\n\nchar, string\n\nInstead its type was " + ClassNameOf(args[0]) + ".");
        }

        string project = TextOf(args[0]);
        string absolute = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(host.CurrentDirectory, project));
        if (!File.Exists(absolute))
        {
            throw Packaged("ToolboxFileNotFound", $"Unable to find file '{project}'.", line, col);
        }

        if (!absolute.EndsWith(".prj", StringComparison.OrdinalIgnoreCase))
        {
            throw Packaged("NotValidToolboxPRJ", "Project file must be a toolbox project (PRJ) file.", line, col);
        }

        throw Packaged("InvalidToolboxProjectFile", $"Invalid MATLAB toolbox project '{absolute}'.", line, col);
    }

    private static string ClassNameOf(JgsValue value) => value.Type switch
    {
        JgsType.Number => "double",
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Object => value.AsObject.Class.Name,
        _ => value.Type.ToString().ToLowerInvariant(),
    };

    /// <summary><c>toolboxVersion(file)</c>: the version a <c>.mltbx</c> holds; a project file wants a toolbox project.</summary>
    private static JgsValue ToolboxVersion(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs", args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType", "Expected input number 1, ToolboxFile, to be one of these types:\n\nchar, string");
        }

        string file = TextOf(args[0]);
        string absolute = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(host.CurrentDirectory, file));
        if (!File.Exists(absolute))
        {
            throw Packaged("ToolboxFileNotFound", $"Unable to find file '{file}'.", line, col);
        }

        switch (Path.GetExtension(absolute).ToLowerInvariant())
        {
            case ".prj":
                throw Packaged("InvalidToolboxProjectFile", $"Invalid MATLAB toolbox project '{absolute}'.", line, col);
            case ".mltbx":
                if (args.Count == 2)
                {
                    throw Packaged("CannotSetVersionOnMLTBX", "Version is read-only for MATLAB toolbox files (*.mltbx).", line, col);
                }

                try
                {
                    return JgsValue.Str(ToolboxPackage.Read(absolute).Version);
                }
                catch (InvalidDataException)
                {
                    throw Packaged("InvalidToolboxFile", $"Invalid MATLAB toolbox file '{absolute}'.", line, col);
                }

            default:
                throw Packaged("InvalidFile", $"Invalid file type.  File '{absolute}' must be a MATLAB toolbox (MLTBX) or toolbox project (PRJ) file.", line, col);
        }
    }

    // --- installToolbox, installedToolboxes, uninstallToolbox ---------------------------------------------------------

    private static JgsValue ToolboxStruct(InstalledToolbox toolbox) => JgsValue.Struct(new Dictionary<string, JgsValue>
    {
        ["Name"] = JgsValue.Str(toolbox.Name),
        ["Version"] = JgsValue.Str(toolbox.Version),
        ["Guid"] = JgsValue.Str(toolbox.Guid),
    });

    private static JgsValue InstallToolbox(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 2 || !IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:validators:mustBeTextScalar",
                args.Count < 1 ? "Not enough input arguments." : "Value must be a character vector or string scalar.");
        }

        string file = TextOf(args[0]);
        string absolute = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(host.CurrentDirectory, file));
        if (!absolute.EndsWith(".mltbx", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "toolboxmanagement_matlab_api:installToolbox:invalidFileType", "File must be a toolbox file (*.mltbx).");
        }

        if (!File.Exists(absolute))
        {
            throw new JgsRuntimeException(line, col, "toolboxmanagement_matlab_api:installToolbox:invalidFilePath", $"Unable to find toolbox file {file}.");
        }

        InstalledToolbox installed;
        try
        {
            installed = InstalledAddOns.Default.InstallToolbox(absolute);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "toolboxmanagement_matlab_api:installToolbox:invalidToolboxFile", $"Invalid toolbox file: {ex.Message}");
        }

        // R2025b puts the toolbox's folders on the path as it installs (w13_installed).
        foreach (string folder in installed.PathFolders.Reverse())
        {
            interpreter.FunctionPath?.Add(folder, atEnd: false);
        }

        return ToolboxStruct(installed);
    }

    private static JgsValue InstalledToolboxes()
    {
        IReadOnlyList<InstalledToolbox> toolboxes = InstalledAddOns.Default.Toolboxes();
        if (toolboxes.Count == 0)
        {
            // With none installed, R2025b answers a 0-by-0 struct with no fields at all (W16, measured).
            return JgsValue.StructArray(new JgsStructArray([]), 0, 0);
        }

        return JgsValue.StructArray([.. toolboxes.Select(t => ToolboxStruct(t).AsStruct)]);
    }

    private static void UninstallToolbox(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs", args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (args[0].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:matlab:addons:toolbox:uninstallToolbox:invalidType", "Expected input number 1, InstalledToolbox, to be one of these types: /  / struct");
        }

        foreach (Dictionary<string, JgsValue> element in args[0].AsStructArray.Elements)
        {
            if (!element.TryGetValue("Guid", out JgsValue? guid) || !IsTextScalar(guid))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:matlab:addons:toolbox:uninstallToolbox:invalidType", "Expected input number 1, InstalledToolbox, to have a Guid field.");
            }

            InstalledAddOns addons = InstalledAddOns.Default;
            if (addons.FindToolbox(TextOf(guid)) is not { } toolbox)
            {
                Warn(host, "MATLAB:matlab:addons:toolbox:uninstallToolbox:notFound", "Unable to find installed toolbox with specified GUID.");
                continue;
            }

            foreach (string folder in toolbox.PathFolders)
            {
                interpreter.FunctionPath?.Remove(folder);
            }

            addons.UninstallToolbox(toolbox.Guid);
        }
    }

    // --- matlab.apputil -------------------------------------------------------------------------------------------

    /// <summary><c>AppUtil.locateFile</c>: the extension appended when it differs, the file found from the current folder.</summary>
    private static string? LocateAppFile(JGraphScriptGlobals host, string file, string extension)
    {
        string named = file.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? file : file + extension;
        string absolute = Path.GetFullPath(Path.IsPathRooted(named) ? named : Path.Combine(host.CurrentDirectory, named));
        return File.Exists(absolute) ? absolute : null;
    }

    private static string AppUtilText(IReadOnlyList<JgsValue> args, string function, string argument, int line, int col)
    {
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs", args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (!IsTextScalar(args[0]) || args[0].IsCharMatrix && args[0].Rows > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:" + function + ":invalidType", $"Expected {argument} to be one of these types:\n\nchar, string\n\nInstead its type was {ClassNameOf(args[0])}.");
        }

        return TextOf(args[0]);
    }

    private static void AppUtilCreate(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Warn(host, "MATLAB:apps:errorcheck:CreateRemoval", "matlab.apputil.create will be removed in a future release. To package an app, create a MATLAB toolbox.");
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:apputil:noPackagingDialog",
                "JGraph has no toolbox packaging dialog. Package an app with matlab.addons.toolbox.ToolboxOptions and packageToolbox, or with App Designer's Share > MATLAB App.");
        }

        string project = AppUtilText(args, "matlab.apputil.create", "PRJFILE", line, col);
        if (LocateAppFile(host, project, ".prj") is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apputil:create:filenotfound", $"The specified file '{project}' could not be found.");
        }

        throw new JgsRuntimeException(line, col, "MATLAB:apputil:create:invalidproject", "The specified project file is not valid.");
    }

    private static void AppUtilPackage(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string project = AppUtilText(args, "matlab.apputil.package", "PRJFILE", line, col);
        if (LocateAppFile(host, project, ".prj") is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apputil:create:filenotfound", $"The specified file '{project}' could not be found.");
        }

        // JGraph has no apps projects: a .prj that exists is not one of them.
        throw new JgsRuntimeException(line, col, "MATLAB:apputil:create:invalidproject", "The specified project file is not valid.");
    }

    private static JgsValue AppStruct(InstalledApp app) => JgsValue.Struct(new Dictionary<string, JgsValue>
    {
        ["id"] = JgsValue.Str(app.Id),
        ["name"] = JgsValue.Str(app.Name),
        ["status"] = JgsValue.Str(app.Status),
        ["location"] = JgsValue.Str(app.Location),
    });

    private static JgsValue AppUtilInstall(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string file = AppUtilText(args, "matlab.apputil.install", "FILE", line, col);
        string absolute = LocateAppFile(host, file, ".mlappinstall")
            ?? throw new JgsRuntimeException(line, col, "MATLAB:apputil:create:filenotfound", $"The specified file '{file}' could not be found.");
        try
        {
            return AppStruct(InstalledAddOns.Default.InstallApp(absolute));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apputil:install:installfailed", "Unable to install the specified file for the following reason:\n" + ex.Message);
        }
    }

    private static void AppUtilUninstall(IReadOnlyList<JgsValue> args, int line, int col)
    {
        string id = AppUtilText(args, "matlab.apputil.uninstall", "APPID", line, col);
        InstalledAddOns addons = InstalledAddOns.Default;
        if (addons.FindApps(id, strict: true).Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apputil:uninstall:notinstalled",
                "The specified ID cannot be found.  When uninstalling an app, the specified ID must exactly match the installed ID.  Use matlab.apputil.getInstalledAppInfo to determine the installed apps and their IDs.");
        }

        addons.UninstallApp(id);
    }

    /// <summary><c>matlab.apputil.run(id)</c>: the app's folders on the path, then its entry point called as a statement.</summary>
    private static void AppUtilRun(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string id = AppUtilText(args, "matlab.apputil.run", "APPID", line, col);
        IReadOnlyList<InstalledApp> found = InstalledAddOns.Default.FindApps(id, strict: false);
        if (found.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apputil:run:notinstalled",
                $"The specified app \"{id}\" cannot be found. Please install apps before running them. Refer to matlab.apputil.install for information on installing apps.");
        }

        if (found.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apputil:run:findappfailed", "The specified app id is not unique.");
        }

        RunGalleryEntry(interpreter, found[0].EntryPoint, InstalledAddOns.AppPathFolders(found[0].Location), line, col);
    }

    /// <summary>Puts <paramref name="pathFolders"/> on the path (the first first) and calls <paramref name="callback"/> with no output.</summary>
    internal static void RunGalleryEntry(Interpreter interpreter, string callback, IReadOnlyList<string> pathFolders, int line, int col)
    {
        foreach (string folder in pathFolders.Reverse())
        {
            if (Directory.Exists(folder))
            {
                interpreter.FunctionPath?.Add(folder, atEnd: false);
            }
        }

        if (callback.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:apps:runapp:NoEntryPoint", "The app has no entry point to run.");
        }

        Feval(interpreter, [JgsValue.Str(callback)], 0, line, col);
    }

    private static JgsValue InstalledAppInfo()
    {
        IReadOnlyList<InstalledApp> apps = InstalledAddOns.Default.Apps();
        if (apps.Count == 0)
        {
            // R2025b answers an empty double (getAllAppInfo starts from []).
            return JgsValue.Array([]);
        }

        return JgsValue.StructArray([.. apps.Select(a => AppStruct(a).AsStruct)]);
    }

    /// <summary><c>getInstalledAppInfo</c> with no output: the ids and names in R2025b's columns, or that none are installed.</summary>
    private static void PrintInstalledAppInfo(JGraphScriptGlobals host)
    {
        IReadOnlyList<InstalledApp> apps = InstalledAddOns.Default.Apps();
        if (apps.Count == 0)
        {
            host.print("\n\tNo apps are installed.  See matlab.apputil.install for information on installing apps.\n");
            return;
        }

        int idLength = apps.Max(static a => a.Id.Length);
        int nameLength = apps.Max(static a => a.Name.Length);
        const string tab = "    ";
        host.print("ID " + new string(' ', idLength - 3) + tab + " Name");
        host.print(new string('-', idLength) + " " + tab + new string('-', nameLength));
        foreach (InstalledApp app in apps)
        {
            host.print(app.Id + new string(' ', idLength - app.Id.Length) + tab + " " + app.Name);
        }
    }

    // --- matlab.addons ----------------------------------------------------------------------------------------------

    private static (string Name, string Version, string Identifier)[] AllAddons()
    {
        InstalledAddOns addons = InstalledAddOns.Default;
        return [.. addons.Toolboxes().Select(t => (t.Name, t.Version, t.Guid)), .. addons.Apps().Select(a => (a.Name, a.Version, a.Guid))];
    }

    private static JgsValue AddonsTable(IEnumerable<(string Name, string Version, string Identifier)> rows)
    {
        var list = rows.ToList();
        return JgsValue.Table(new Table(
        [
            new TextColumn("Name", [.. list.Select(r => (string?)r.Name)]),
            new TextColumn("Version", [.. list.Select(r => (string?)r.Version)]),
            new NumberColumn("Enabled", [.. list.Select(static _ => 1.0)]),
            new TextColumn("Identifier", [.. list.Select(r => (string?)r.Identifier)]),
        ]));
    }

    /// <summary><c>installedAddons</c>: a table of the installed toolboxes and apps (R2025b lists its products too).</summary>
    private static JgsValue InstalledAddonsTable() => AddonsTable(AllAddons());

    private static JgsValue AddonsInstall(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 3)
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs", args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar", "Value must be a character vector or string scalar.");
        }

        string file = TextOf(args[0]);
        if (!file.EndsWith(".mltbx", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "matlab_addons:install:invalidFileType", "File must be a toolbox file (*.mltbx).");
        }

        string absolute = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(host.CurrentDirectory, file));
        if (!File.Exists(absolute))
        {
            throw new JgsRuntimeException(line, col, "matlab_addons:install:invalidFilePath", $"Unable to find {file}.");
        }

        for (int i = 1; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string option = TextOf(args[i]);
                if (!option.Equals("add", StringComparison.OrdinalIgnoreCase) && !option.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col, "matlab_addons:install:invalidInstallOption", "Specify the install option as 'add' or 'overwrite'.");
                }
            }
            else if (args[i].Type != JgsType.Number && !args[i].IsLogicalFlag())
            {
                throw new JgsRuntimeException(line, col, "matlab_addons:install:invalidAgreeToLicenseOrInstallOption", "Specify a logical value for AgreeToLicense or 'add' or 'overwrite' as the install option.");
            }
        }

        JgsValue installed = InstallToolbox(interpreter, host, [args[0]], line, col);
        Dictionary<string, JgsValue> fields = installed.AsStruct;
        return AddonsTable([(TextOf(fields["Name"]), TextOf(fields["Version"]), TextOf(fields["Guid"]))]);
    }

    private static bool IsLogicalFlag(this JgsValue value) => value.Type == JgsType.Array && value.ArrayLength == 1;

    private static void AddonsUninstall(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 2 || !IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:validators:mustBeTextScalar",
                args.Count < 1 ? "Not enough input arguments." : "Value must be a character vector or string scalar.");
        }

        string nameOrId = TextOf(args[0]);
        InstalledAddOns addons = InstalledAddOns.Default;
        InstalledToolbox? toolbox = addons.Toolboxes().FirstOrDefault(t => t.Guid.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
        if (toolbox is not null)
        {
            foreach (string folder in toolbox.PathFolders)
            {
                interpreter.FunctionPath?.Remove(folder);
            }

            addons.UninstallToolbox(toolbox.Guid);
            return;
        }

        InstalledApp? app = addons.Apps().FirstOrDefault(a => a.Guid.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) || a.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
        if (app is not null)
        {
            addons.UninstallApp(app.Id);
            return;
        }

        throw new JgsRuntimeException(line, col, "matlab_addons:enableDisableManagement:invalidIdentifier", "Unable to find add-on with specified identifier.");
    }

    private static JgsValue IsAddonEnabled(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 2 || !IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:validators:mustBeTextScalar",
                args.Count < 1 ? "Not enough input arguments." : "Value must be a character vector or string scalar.");
        }

        string nameOrId = TextOf(args[0]);
        bool known = AllAddons().Any(a => a.Identifier.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) || a.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
        if (!known)
        {
            throw new JgsRuntimeException(line, col, "matlab_addons:enableDisableManagement:invalidIdentifier", "Unable to find add-on with specified identifier.");
        }

        return JgsValue.Bool(true);
    }
}
