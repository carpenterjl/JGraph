using System.Diagnostics;
using JGraph.Deploy;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>compiler.build.standaloneApplication</c>, <c>compiler.build.standaloneWindowsApplication</c> and
/// <c>compiler.build.StandaloneApplicationOptions</c> (compiler and SDK plan, stage K6), transcribed
/// from R2025b's toolbox/compiler/cli/build and toolbox/compiler/utils: the options and each
/// property's check (their messages from resources/Compiler/en), the mcc command a build writes in
/// the order R2025b's writers write it, the icon made at six sizes and the windowed program's
/// <c>splash.png</c> at 400 by 400 beside it, and the <c>compiler.build.Results</c> answered.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string OptionsClassName = JgsBuiltinClasses.StandaloneOptions;

    /// <summary>The image formats R2025b's image copy reads and writes (CLIConstants.AllowedImageFormats).</summary>
    private static readonly string[] AllowedImageFormats = [".bmp", ".gif", ".jpg", ".jpeg", ".png"];

    /// <summary>The extensions an <c>AppFile</c> may have, in R2025b's order (requirementsConstants.executableMatlabFileExt).</summary>
    private static readonly string[] AppFileExtensions = [".mexw64", ".mlapp", ".mlx", ".p", ".m"];

    /// <summary>The characters no name or folder may hold (CLIConstants.BadFolderCharacters).</summary>
    private static readonly char[] BadFolderCharacters = ['<', '>', ':', '"', '*', '|', '?', '&'];

    private static void RegisterCompilerBuild(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(OptionsClassName, JgsValue.Function(new BuiltinFunction(OptionsClassName,
            (args, line, col) => JgsValue.Object(NewStandaloneOptions(interpreter, args, line, col)))
        {
            KeepsStringArguments = true,
        }));

        foreach ((string name, bool windowed) in new[] { ("compiler.build.standaloneApplication", false), ("compiler.build.standaloneWindowsApplication", true) })
        {
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => BuildStandalone(env, interpreter, host, args, windowed, 1, line, col) ?? JgsValue.Null)
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = false,
                MultiOutput = (args, wanted, line, col) =>
                {
                    if (wanted > 1)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
                    }

                    JgsValue? results = BuildStandalone(env, interpreter, host, args, windowed, wanted, line, col);
                    return wanted == 0 || results is null ? [] : [results];
                },
            }));
        }

        RegisterDotNetBuild(env, interpreter, host);
        RegisterCLibraryBuild(env, interpreter, host);
    }

    // --- The options -----------------------------------------------------------------------

    /// <summary>A property's value before anything sets it, as R2025b's class declares it.</summary>
    internal static JgsValue StandaloneOptionDefault(string name) => name switch
    {
        "CustomHelpTextFile" or "SecretsManifest" => JgsValue.Str(""),
        "EmbedArchive" or "AutoDetectDataFiles" => OnOffSwitchStateValue.Of(true),
        "TreatInputsAsNumeric" or "ObfuscateArchive" or "Verbose" => OnOffSwitchStateValue.Of(false),
        "ExecutableIcon" => JgsValue.Str(PackagingResource("default_icon_48.png")),
        "ExecutableSplashScreen" => JgsValue.Str(PackagingResource("default_splash.png")),
        "ExecutableVersion" => JgsValue.Str("1.0.0.0"),
        "AdditionalFiles" => EmptyCellValue(),
        "ExternalEncryptionKey" => EmptyEncryptionKey(),
        "SupportPackages" => OptionCellColumn(["autodetect"]),
        _ => JgsMatrix.FromColumnMajor([], 0, 0),
    };

    /// <summary>compiler.build's images, where R2025b keeps its own: <c>matlabroot\toolbox\compiler\packagingResources</c>.</summary>
    private static string PackagingResource(string file) =>
        Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "toolbox", "compiler", "packagingResources", file);

    /// <summary>
    /// <c>ExecutableName</c> and <c>OutputDir</c>: the value given, or the one derived from the app file
    /// (R2025b's defaultOutputFilename and OutputDirManager.getOutputDir).
    /// </summary>
    internal static JgsValue StandaloneOptionDerived(string name, JgsObject options)
    {
        string executable = TextOf(options.Fields[JgsBuiltinClasses.GivenExecutableName]);
        if (executable.Length == 0)
        {
            executable = DefaultOutputFilename(Path.GetFileNameWithoutExtension(TextOf(options.Fields["AppFile"])));
        }

        if (name == "ExecutableName")
        {
            return JgsValue.Str(executable);
        }

        string folder = TextOf(options.Fields[JgsBuiltinClasses.GivenOutputDir]);
        return JgsValue.Str(folder.Length > 0 ? folder : @".\" + ScrubName(executable) + "standaloneApplication");
    }

    /// <summary>R2025b's defaultOutputFilename: a leading <c>+</c> dropped, anything but a letter, digit or <c>_</c> made <c>_</c>, and <c>_</c> before a digit.</summary>
    private static string DefaultOutputFilename(string name)
    {
        if (name.Length > 1 && name[0] == '+')
        {
            name = name[1..];
        }

        var text = new System.Text.StringBuilder(name.Length + 1);
        foreach (char c in name)
        {
            text.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return text.Length > 0 && char.IsAsciiDigit(text[0]) ? "_" + text : text.ToString();
    }

    /// <summary>R2025b's scrubName: the bad folder characters, the separators and blanks made <c>_</c>.</summary>
    private static string ScrubName(string name) =>
        new([.. name.Select(c => BadFolderCharacters.Contains(c) || c is '/' or '\\' or ' ' ? '_' : c)]);

    /// <summary>
    /// <c>compiler.build.StandaloneApplicationOptions(appFile, Name, Value, ...)</c>: the app file set
    /// first, then each named property in the order R2025b's <c>setdiff</c> leaves them (sorted).
    /// </summary>
    private static JgsObject NewStandaloneOptions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int most = 2 * JgsBuiltinClasses.StandaloneOptionNames.Length - 1;
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > most)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsClass definition = interpreter.BuiltinClass(OptionsClassName)
            ?? throw new JgsRuntimeException(line, col, $"The class '{OptionsClassName}' is not available in this build.");
        JgsObject options = definition.NewDefault(line, col);
        SetStandaloneOption(interpreter, options, "AppFile", args[0], line, col);
        var given = new SortedDictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]) || args[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar",
                    "Expected a string scalar or character vector for the parameter name.");
            }

            string asked = TextOf(args[i]);
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{asked}'. Name-value pair arguments require a name followed by a value.");
            }

            given[OptionName(asked, line, col)] = args[i + 1];
        }

        foreach ((string name, JgsValue value) in given)
        {
            SetStandaloneOption(interpreter, options, name, value, line, col);
        }

        return options;
    }

    /// <summary>The property a name-value name means, as inputParser matches it (<see cref="ParameterName"/>).</summary>
    private static string OptionName(string asked, int line, int col) => ParameterName(asked, JgsBuiltinClasses.StandaloneOptionNames, line, col);

    /// <summary>
    /// Sets <paramref name="name"/> on <paramref name="options"/> (which is the copy a set method
    /// answers), checked as R2025b's set method for it checks it.
    /// </summary>
    internal static void SetStandaloneOption(Interpreter interpreter, JgsObject options, string name, JgsValue value, int line, int col)
    {
        string here = interpreter.Host?.CurrentDirectory ?? Directory.GetCurrentDirectory();
        string Absolute(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(here, path));
        void MustExist(string path)
        {
            if (!File.Exists(Absolute(path)))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{path}");
            }
        }

        switch (name)
        {
            case "AppFile":
            {
                string file = ScalarText(value, name, line, col);
                MustExist(file);
                if (!AppFileExtensions.Contains(Path.GetExtension(file), StringComparer.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:clishared:badExtension",
                        $"Expected 'AppFile' to have one of the following extensions:\n\n{string.Join("\n", AppFileExtensions)}");
                }

                options.Fields[name] = JgsValue.Str(Absolute(file));
                return;
            }

            case "ExecutableName":
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

                string given = TextOf(value);
                NoBadFolderCharacters(given, name, path: false, line, col);
                if (given.Any(c => char.IsWhiteSpace(c) && c != ' '))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:clishared:noWhiteSpaceInProperty", $"'{name}' cannot contain any white space characters.");
                }

                if (given.IndexOfAny(['/', '\\']) >= 0)
                {
                    throw BadProperty(name, given, line, col);
                }

                options.Fields[JgsBuiltinClasses.GivenExecutableName] = JgsValue.Str(given);
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

            case "ExecutableVersion":
                options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));
                return;

            case "ExecutableIcon":
            case "ExecutableSplashScreen":
            {
                string file = ScalarText(value, name, line, col);
                MustExist(file);
                string absolute = Absolute(file);
                string format = ImageFormatOf(absolute)
                    ?? throw new JgsRuntimeException(line, col, "Compiler:utils:shared:notAnImage", $"The file:\n\n{absolute}\n\nis not an image file.");
                if (format == ".gif" && IsAnimated(absolute))
                {
                    throw new JgsRuntimeException(line, col, "Compiler:utils:shared:noAnimatedGIFs", "Animated '.gif' files are not supported.");
                }

                options.Fields[name] = JgsValue.Str(absolute);
                return;
            }

            case "CustomHelpTextFile":
            case "SecretsManifest":
            {
                if (IsEmptyValue(value) || (value.IsStringArray && TextOf(value).Length == 0))
                {
                    options.Fields[name] = JgsValue.Str("");
                    return;
                }

                string file = ScalarTextOnly(value, name, line, col);
                MustExist(file);
                options.Fields[name] = JgsValue.Str(Absolute(file));
                return;
            }

            case "EmbedArchive":
            case "TreatInputsAsNumeric":
                options.Fields[name] = Switch(value, name, OptionsClassName, line, col);
                return;
            case "AutoDetectDataFiles":
            case "ObfuscateArchive":
                options.Fields[name] = Switch(value, name, "compiler.internal.build.DependencyOptions", line, col);
                return;
            case "Verbose":
                options.Fields[name] = Switch(value, name, "compiler.internal.build.AllOptions", line, col);
                return;

            case "AdditionalFiles":
                options.Fields[name] = ItemList(value, name, here, line, col);
                return;

            case "ExternalEncryptionKey":
                options.Fields[name] = EncryptionKey(value, MustExist, Absolute, line, col);
                return;

            case "SupportPackages":
                options.Fields[name] = SupportPackageList(value, line, col);
                return;

            case JgsBuiltinClasses.RuntimeDeliveryOption:
            {
                string delivery = ScalarText(value, name, line, col).ToLowerInvariant();
                options.Fields[name] = delivery switch
                {
                    "installed" or "bundled" => JgsValue.Str(delivery),
                    "singlefile" => throw new JgsRuntimeException(line, col, "JGraph:compiler:singleFileNotBuilt",
                        "JGraph does not build single-file programs yet: use 'bundled', which carries the runtime in the program's folder."),
                    _ => throw new JgsRuntimeException(line, col, "JGraph:compiler:badRuntimeDelivery",
                        $"'RuntimeDelivery' must be 'installed' or 'bundled', not '{delivery}'."),
                };
                return;
            }

            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not a property of '{OptionsClassName}'.");
        }
    }

    /// <summary>R2025b's propertyIsScalarText then mustBeNonzeroLengthText: the text of a char row or a string scalar with something in it.</summary>
    private static string ScalarText(JgsValue value, string name, int line, int col)
    {
        string text = ScalarTextOnly(value, name, line, col);
        NonzeroLength(value, line, col);
        return text;
    }

    /// <summary>R2025b's propertyIsScalarText: a char row or a string scalar, else validateattributes' refusals.</summary>
    private static string ScalarTextOnly(JgsValue value, string name, int line, int col)
    {
        if (!IsTextValue(value))
        {
            throw InvalidType(name, ["char", "string"], value, line, col);
        }

        if (!(IsTextScalar(value) && !value.IsCharMatrix) || (value.IsStringArray && IsMissingString(value)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedScalartext", $"Expected {name} to be a non-missing string scalar or character vector.");
        }

        return TextOf(value);
    }

    private static bool IsTextValue(JgsValue value) => ClassOf(value, JgsDialect.Matlab) is "char" or "string";

    private static bool IsMissingString(JgsValue value) => value.IsStringArray && value.ArrayLength == 1 && IsMissingText(TextOf(value));

    private static void NonzeroLength(JgsValue value, int line, int col)
    {
        bool empty = value.Type == JgsType.Cell
            ? value.AsCell.Any(static c => !IsTextValue(c) || TextOf(c).Length == 0)
            : !IsTextValue(value) || (IsTextScalar(value) && TextOf(value).Length == 0) || IsEmptyValue(value);
        if (empty)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "Value must be text with one or more characters.");
        }
    }

    private static JgsRuntimeException InvalidType(string name, string[] classes, JgsValue value, int line, int col) =>
        new(line, col, "MATLAB:invalidType",
            $"Expected {name} to be one of these types:\n\n{string.Join(", ", classes)}\n\nInstead its type was {ClassOf(value, JgsDialect.Matlab)}.");

    private static JgsRuntimeException BadProperty(string name, string value, int line, int col) =>
        new(line, col, "Compiler:clishared:badProperty", $"Invalid '{name}': '{value}'");

    /// <summary>R2025b's noBadFolderCharacters; for a path, a drive's colon is not one of them.</summary>
    private static void NoBadFolderCharacters(string text, string name, bool path, int line, int col)
    {
        string tested = path && text.Length >= 2 && text[1] == ':' && Path.IsPathFullyQualified(text) ? text.Remove(1, 1) : text;
        if (tested.IndexOfAny(BadFolderCharacters) >= 0)
        {
            throw BadProperty(name, text, line, col);
        }
    }

    /// <summary>An on/off property: whatever <c>matlab.lang.OnOffSwitchState</c> takes, else R2025b's conversion refusal.</summary>
    private static JgsValue Switch(JgsValue value, string name, string owner, int line, int col)
    {
        try
        {
            return OnOffSwitchStateValue.Construct([value], line, col);
        }
        catch (JgsRuntimeException)
        {
            string shown = IsTextScalar(value) ? TextOf(value) : value.Display().Trim();
            throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                $"Error setting property '{name}' of class '{owner}'. '{shown}' is invalid. Value must be 'off' or 'on'.");
        }
    }

    /// <summary>
    /// <c>AdditionalFiles</c> (R2025b's itemList, folders allowed): text or a cell of it, every item a
    /// file or folder that exists, as absolute paths in a column; empty for nothing.
    /// </summary>
    private static JgsValue ItemList(JgsValue value, string name, string here, int line, int col)
    {
        if (IsEmptyValue(value) || (value.IsStringArray && value.ArrayLength == 1 && TextOf(value).Length == 0))
        {
            return EmptyCellValue();
        }

        if (!(IsTextValue(value) || value.Type == JgsType.Cell))
        {
            throw InvalidType(name, ["char", "string", "cell"], value, line, col);
        }

        int[] dims = SizeDims(value);
        if (dims.Length != 2 || (dims[0] != 1 && dims[1] != 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedVector", $"Expected {name} to be a vector.");
        }

        NonzeroLength(value, line, col);
        string[] items = value.Type == JgsType.Cell ? [.. value.AsCell.Select(TextOf)]
            : value.IsStringArray ? [.. TextElementsOf(value) ?? []]
            : [TextOf(value)];
        var paths = new List<string>(items.Length);
        foreach (string item in items)
        {
            string absolute = Path.GetFullPath(Path.IsPathRooted(item) ? item : Path.Combine(here, item));
            if (!File.Exists(absolute) && !Directory.Exists(absolute))
            {
                throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFileOrFolder", $"Cannot find file or folder:\n\n{item}");
            }

            paths.Add(absolute);
        }

        return OptionCellColumn(paths);
    }

    /// <summary><c>ExternalEncryptionKey</c>: a scalar struct of exactly its two files, both existing (R2025b's DependencyOptions); empty for none.</summary>
    private static JgsValue EncryptionKey(JgsValue value, Action<string> mustExist, Func<string, string> absolute, int line, int col)
    {
        if (HasNoElements(value))
        {
            return EmptyEncryptionKey();
        }

        if (value.Type != JgsType.Struct || value.IsStructArray)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeA", "Value must be of the following type: 'struct'.");
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        string[] names = ["EncryptionKeyFile", "RuntimeKeyLoaderFile"];
        if (fields.Count != 2 || !names.All(fields.ContainsKey))
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:externalencryptionkey:badFields",
                "\"ExternalEncryptionKey\" must be a struct with exactly two fields named \"EncryptionKeyFile\" and \"RuntimeKeyLoaderFile\".");
        }

        var key = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in names)
        {
            JgsValue held = fields[field];
            if (!IsTextValue(held) || !(IsTextScalar(held) && !held.IsCharMatrix))
            {
                throw new JgsRuntimeException(line, col, "Compiler:build:externalencryptionkey:fieldNotScalarText",
                    $"The field \"{field}\" of the \"ExternalEncryptionKey\" property must be a character vector or string scalar.");
            }

            string file = TextOf(held);
            if (file.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "Compiler:build:externalencryptionkey:fieldIsZeroLength",
                    $"The field \"{field}\" of the \"ExternalEncryptionKey\" property must be text with one or more characters.");
            }

            mustExist(file);
            key[field] = JgsValue.Str(absolute(file));
        }

        return JgsValue.Struct(key);
    }

    /// <summary><c>SupportPackages</c>: <c>{'autodetect'}</c> or <c>{'none'}</c> (nothing given is none); JGraph has no support packages to name.</summary>
    private static JgsValue SupportPackageList(JgsValue value, int line, int col)
    {
        if (IsEmptyValue(value) || (value.IsStringArray && value.ArrayLength == 1 && TextOf(value).Length == 0))
        {
            return OptionCellColumn(["none"]);
        }

        string[] words = value.Type == JgsType.Cell ? [.. value.AsCell.Select(TextOf)]
            : value.IsStringArray ? [.. TextElementsOf(value) ?? []]
            : [TextOf(value)];
        if (words.Length == 1 && words[0].ToLowerInvariant() is "autodetect" or "none")
        {
            return OptionCellColumn([words[0].ToLowerInvariant()]);
        }

        throw new JgsRuntimeException(line, col, "JGraph:compiler:noSupportPackages",
            $"JGraph has no support packages, so 'SupportPackages' is 'autodetect' or 'none'; '{string.Join("', '", words)}' cannot be included.");
    }

    /// <summary>Whether a value has no elements: a 0-by-0 struct array included, which <see cref="IsEmptyValue"/> does not count.</summary>
    private static bool HasNoElements(JgsValue value) => IsEmptyValue(value) || SizeDims(value).Contains(0);

    private static JgsValue EmptyEncryptionKey() =>
        JgsValue.StructArray(new JgsStructArray([], ["EncryptionKeyFile", "RuntimeKeyLoaderFile"]), 0, 0);

    private static JgsValue EmptyCellValue()
    {
        JgsValue empty = JgsValue.Cell([]);
        empty.Reshape(0, 0);
        return empty;
    }

    /// <summary>A column cell of char rows (a single one is 1-by-1).</summary>
    private static JgsValue OptionCellColumn(IReadOnlyList<string> items)
    {
        JgsValue cell = JgsValue.Cell([.. items.Select(JgsValue.Str)]);
        cell.Reshape(items.Count, 1);
        return cell;
    }

    // --- The images ------------------------------------------------------------------------

    /// <summary>The format a file's bytes say it is, as <c>imfinfo</c>'s Format with a dot, or null when it is no image.</summary>
    private static string? ImageFormatOf(string path)
    {
        byte[] head = new byte[12];
        try
        {
            using FileStream file = File.OpenRead(path);
            if (file.Read(head) < 4)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return head switch
        {
            [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => ".png",
            [0xFF, 0xD8, ..] => ".jpg",
            [(byte)'B', (byte)'M', ..] => ".bmp",
            [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ".gif",
            [0, 0, 1, 0, ..] => ".ico",
            [(byte)'I', (byte)'I', 42, 0, ..] or [(byte)'M', (byte)'M', 0, 42, ..] => ".tif",
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P'] => ".webp",
            _ => null,
        };
    }

    private static bool IsAnimated(string gif)
    {
        try
        {
            using ImageBuffer _ = ImageCodec.Read(gif, 1);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>R2025b's image copy's format check: the file's format must be one it reads.</summary>
    private static void AllowedImage(string path, string property, int line, int col)
    {
        string format = ImageFormatOf(path)
            ?? throw new JgsRuntimeException(line, col, "Compiler:utils:shared:notAnImage", $"The file:\n\n{path}\n\nis not an image file.");
        if (!AllowedImageFormats.Contains(format))
        {
            throw new JgsRuntimeException(line, col, "Compiler:utils:shared:invalidInputExt",
                $"Invalid input image format for '{property}':\n\n{path}\n\nThe format of the input file is '{format}'. Allowable formats are:\n\n{string.Join("\n", AllowedImageFormats)}");
        }
    }

    /// <summary>An image resized as R2025b's image copy resizes one (imresize's bicubic), as PNG bytes, its transparency kept.</summary>
    private static byte[] ResizedPng(string source, int height, int width, string scratch)
    {
        (ImageBuffer image, ImageBuffer? alpha) = ImageCodec.ReadWithAlpha(source);
        using (image)
        using (alpha)
        {
            using ImageBuffer resized = image.Height == height && image.Width == width
                ? Geometry.Resize(image, height, width, Geometry.Interpolation.Nearest)
                : Geometry.Resize(image, height, width);
            using ImageBuffer? resizedAlpha = alpha is null ? null : Geometry.Resize(alpha, height, width);
            string file = Path.Combine(scratch, $"icon_{width}.png");
            ImageCodec.Write(file, resized, new CodecWriteOptions(Alpha: resizedAlpha));
            return File.ReadAllBytes(file);
        }
    }

    /// <summary>
    /// The icon a build stamps, R2025b's processIcon: the image at 256, 128, 48, 32, 24 and 16 pixels,
    /// written as an icon file in a folder of its own, which mcc's <c>-r</c> is given.
    /// </summary>
    private static string BuildIcon(string image, string folder, int line, int col)
    {
        AllowedImage(image, "Image", line, col);
        Directory.CreateDirectory(folder);
        var images = IconFile.BuildSizes.Select(size => IconImage.FromPng(ResizedPng(image, size, size, folder))).ToList();
        string icon = Path.Combine(folder, "icon.ico");
        File.WriteAllBytes(icon, IconFile.Write(images));
        return icon;
    }

    /// <summary>A windowed program's <c>splash.png</c>, 400 by 400 (R2025b's StandaloneWindowsApplication.preProcessSplashScreen).</summary>
    private static string BuildSplash(string image, string folder, string scratch, int line, int col)
    {
        AllowedImage(image, "ExecutableSplashScreen", line, col);
        string target = Path.Combine(folder, "splash.png");
        (int width, int height) = ImageSize(image);
        if (width == 400 && height == 400 && ImageFormatOf(image) == ".png")
        {
            File.Copy(image, target, overwrite: true);
        }
        else
        {
            File.WriteAllBytes(target, ResizedPng(image, 400, 400, scratch));
        }

        return target;
    }

    private static (int Width, int Height) ImageSize(string path)
    {
        using ImageBuffer image = ImageCodec.Read(path);
        return (image.Width, image.Height);
    }

    // --- The build -------------------------------------------------------------------------

    /// <summary>
    /// <c>results = compiler.build.standaloneApplication(appFile | opts, Name, Value, ...)</c> and the
    /// Windows one: R2025b's builder (Generic, StandaloneShared and the writers) over JGraph's mcc.
    /// </summary>
    private static JgsValue? BuildStandalone(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, bool windowed, int wanted, int line, int col)
    {
        int most = 2 * JgsBuiltinClasses.StandaloneOptionNames.Length - 1;
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > most)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsObject options;
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.Name == OptionsClassName)
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
                $"'{args[0].AsObject.Class.Name}' is not a valid options object for this function. Use '{OptionsClassName}' instead.");
        }
        else
        {
            options = NewStandaloneOptions(interpreter, args, line, col);
        }

        var watch = Stopwatch.StartNew();
        string here = host.CurrentDirectory;
        string Text(string property) => TextOf(options.Fields[property]);
        bool On(string property) => options.Fields[property].Type == JgsType.External
            && options.Fields[property].AsExternal is OnOffSwitchStateValue { AllOn: true };
        string name = TextOf(StandaloneOptionDerived("ExecutableName", options));
        string outputDir = TextOf(StandaloneOptionDerived("OutputDir", options));
        string folder = Path.GetFullPath(Path.Combine(here, outputDir));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "Compiler:build:shared:failedToMakeFolder", $"Unable to create the following folder:\n\n{outputDir}");
        }

        string scratch = Path.Combine(Path.GetTempPath(), "tp" + Guid.NewGuid().ToString().Replace('-', '_'));
        try
        {
            string icon = BuildIcon(Text("ExecutableIcon"), scratch, line, col);
            string? splash = windowed ? BuildSplash(Text("ExecutableSplashScreen"), folder, scratch, line, col) : null;

            // The command R2025b's writers write: -W, -T, -d, -v, the dependency options, the target's, the file.
            string wrapper = $"{(windowed ? "winmain" : "main")}:{name},version={Text("ExecutableVersion")}";
            if (Text(JgsBuiltinClasses.RuntimeDeliveryOption) == "bundled")
            {
                wrapper += ",runtime=bundled";
            }

            var words = new List<string> { "-W", wrapper, "-T", "link:exe", "-d", outputDir };
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

            words.AddRange(["-o", name]);
            if (On("TreatInputsAsNumeric"))
            {
                words.Add("-n");
            }

            if (!On("EmbedArchive"))
            {
                words.Add("-C");
            }

            if (Text("CustomHelpTextFile").Length > 0)
            {
                words.AddRange(["-h", Text("CustomHelpTextFile")]);
            }

            words.AddRange(["-r", icon, Text("AppFile")]);
            if (On("Verbose"))
            {
                host.WriteOut(MccCommandString(words) + "\n");
            }

            MccBuild built = Mcc(env, interpreter, host, words, line, col)
                ?? throw new JgsRuntimeException(line, col, "The build printed mcc's help and built nothing.");
            var files = new List<string>(built.Files);
            if (splash is not null)
            {
                files.Insert(1, splash);
            }

            return wanted == 0 ? null : Results(interpreter, windowed, files, options, built.Uuid, watch.Elapsed.TotalSeconds, line, col);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still open in the scratch folder; the system's temporary folder clears it.
            }
        }
    }

    /// <summary>R2025b's getMccCommandString: <c>mcc</c> and each argument, in single quotes when it holds a comma, a blank or a semicolon.</summary>
    internal static string MccCommandString(IEnumerable<string> words) =>
        "mcc" + string.Concat(words.Select(static w => " " + (w.IndexOfAny([',', ' ', ';']) >= 0 ? $"'{w}'" : w)));

    /// <summary>The <c>compiler.build.Results</c> a build answers.</summary>
    private static JgsValue Results(
        Interpreter interpreter, bool windowed, IReadOnlyList<string> files, JgsObject options, string uuid, double seconds, int line, int col)
    {
        JgsClass resultsClass = interpreter.BuiltinClass(JgsBuiltinClasses.BuildResults)
            ?? throw new JgsRuntimeException(line, col, $"The class '{JgsBuiltinClasses.BuildResults}' is not available in this build.");
        JgsClass dependenciesClass = interpreter.BuiltinClass(JgsBuiltinClasses.RuntimeDependencies)
            ?? throw new JgsRuntimeException(line, col, $"The class '{JgsBuiltinClasses.RuntimeDependencies}' is not available in this build.");

        JgsObject dependencies = dependenciesClass.NewDefault(line, col);
        JgsValue displayName = OptionCellColumn([BuildLogs.RuntimeDisplayName]);
        JgsValue id = JgsMatrix.FromColumnMajor([BuildLogs.RuntimeProductId], 1, 1);
        dependencies.Fields["Required"] = JgsValue.Table(new JGraph.Data.Table(
        [
            TableColumnFrom("compiler.runtime.Dependencies", "DisplayName", displayName, line, col),
            TableColumnFrom("compiler.runtime.Dependencies", "ID", id, line, col),
        ]));
        dependencies.Fields["Optional"] = JgsValue.Table(new JGraph.Data.Table([]));

        JgsObject results = resultsClass.NewDefault(line, col);
        results.Fields["BuildType"] = JgsValue.Str(windowed ? "standaloneWindowsApplication" : "standaloneApplication");
        results.Fields["Files"] = OptionCellColumn(files);
        results.Fields["IncludedSupportPackages"] = EmptyCellValue();
        results.Fields["Options"] = JgsValue.Object(options);
        results.Fields["RuntimeDependencies"] = JgsValue.Object(dependencies);
        results.Fields["BuildID"] = JgsValue.StringArray([JgsValue.Str(uuid)], 1, 1);
        results.Fields["BuildDuration"] = JgsValue.Number(seconds);
        return JgsValue.Object(results);
    }
}
