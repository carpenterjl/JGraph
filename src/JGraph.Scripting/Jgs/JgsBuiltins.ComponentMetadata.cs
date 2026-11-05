using JGraph.Scripting.Designer;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>appdesigner.customcomponent.configureMetadata</c> and <c>appdesigner.customcomponent.removeMetadata</c>
/// (App Designer window plan W15, chip 162): a custom UI component's registration for App Designer's Component
/// Library, as R2025b's two functions and their <c>getValidatedCustomComponentFile</c> do it (read in place,
/// their refusals measured by probe <c>w15_component</c>). The first opens the host's "App Designer Custom UI
/// Component Metadata" dialog through <see cref="IScriptAppDesigner.ConfigureMetadata"/>; the second edits the
/// <c>resources/appDesigner.json</c> beside the class through <see cref="DesignComponentMetadata"/>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterComponentMetadataBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> ComponentMetadataBuiltinNames { get; } =
    [
        "appdesigner.customcomponent.configureMetadata", "appdesigner.customcomponent.removeMetadata",
    ];

    private const string ComponentMetadataPrefix = "MATLAB:appdesigner:usercomponentmetadata:";

    private static void RegisterComponentMetadataBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        Statement(env, ComponentMetadataBuiltinNames[0], (args, line, col) => ConfigureMetadata(interpreter, host, args, line, col));
        Statement(env, ComponentMetadataBuiltinNames[1], (args, line, col) => RemoveMetadata(interpreter, host, args, line, col));
        // matlab.apps.createCallbackFcn(obj, @method, requiresEventData): R2025b's package function (runtime/+matlab/+apps),
        // which a custom UI component's setup calls for its children's callbacks as an app's createComponents does;
        // an app reaches it through AppBase's method too.
        env.Builtins.Register("matlab.apps.createCallbackFcn", JgsValue.Function(new BuiltinFunction("matlab.apps.createCallbackFcn",
            (args, line, col) => JgsBuiltinClasses.CreateCallbackFcn(interpreter, args, line, col))
        {
            KeepsStringArguments = true,
        }));
    }

    /// <summary>
    /// <c>configureMetadata(file)</c>: the file checked, then the host's dialog. R2025b draws the dialog as a
    /// uifigure even with no desktop; JGraph's is the app's window, so a session without one refuses.
    /// </summary>
    private static void ConfigureMetadata(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string full = CustomComponentFileArgument(interpreter, host, args, line, col);
        if (host.AppDesigner is not { } designer)
        {
            throw new JgsRuntimeException(line, col, "JGraph:appdesigner:NoWindow",
                "appdesigner.customcomponent.configureMetadata opens the App Designer Custom UI Component Metadata dialog in the JGraph app, and this session has no window. "
                + "A batch run can register a component by writing its resources/appDesigner.json, but not through the dialog.");
        }

        if (designer.ConfigureMetadata(full) is { } refused)
        {
            throw new JgsRuntimeException(line, col, "JGraph:appdesigner:CannotConfigure", refused);
        }
    }

    /// <summary>
    /// <c>removeMetadata(file)</c>: the file checked, the registry read - an unreadable one refused under R2025b's
    /// identifier - and the component's record taken out. R2025b fails on its own <c>isKey</c> when there is no
    /// registry file at all (measured); JGraph has nothing to remove and says nothing.
    /// </summary>
    private static void RemoveMetadata(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string full = CustomComponentFileArgument(interpreter, host, args, line, col);
        DesignComponentMetadata model = DesignComponentMetadata.Load(full);
        if (!model.IsValid)
        {
            throw new JgsRuntimeException(line, col, DesignComponentMetadata.InvalidModelIdentifier, DesignComponentMetadata.InvalidModelError);
        }

        try
        {
            model.Deregister();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:appdesigner:CannotWriteMetadata", $"{DesignComponentMetadata.NoWriteAccess.Split('\n')[0]} ({ex.Message})");
        }
    }

    /// <summary>The one argument both functions take (<c>narginchk(1, 1)</c>), validated.</summary>
    private static string CustomComponentFileArgument(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        return ValidatedCustomComponentFile(interpreter, host, args[0], line, col);
    }

    /// <summary>
    /// The full path of the custom UI component class <paramref name="value"/> names
    /// (<c>getValidatedCustomComponentFile</c>): a char row or string scalar; an extension, when given, <c>.m</c>
    /// or <c>.mlapp</c>; the name a valid one; the file found as given, in the current folder or on the path -
    /// <c>.m</c> then <c>.mlapp</c> tried with no extension - and spelled as it is on disk; and the file a class
    /// under <c>matlab.ui.componentcontainer.ComponentContainer</c>, directly or through a class of the user's.
    /// </summary>
    /// <remarks>
    /// R2025b's refusal of another extension says the file "must have a .txt extension" when given
    /// <c>comp1.txt</c>: its message is handed the wrong extension (measured). JGraph names the two it takes.
    /// </remarks>
    internal static string ValidatedCustomComponentFile(Interpreter interpreter, JGraphScriptGlobals host, JgsValue value, int line, int col)
    {
        if (!IsTextScalar(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:InvalidInput",
                "The input argument must be a full or partial file path, or a file name.");
        }

        string typed = TextOf(value);
        string extension = Path.GetExtension(typed);
        if (extension.Length > 0 && extension != ".m" && extension != ".mlapp")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:InvalidGeneralFileExtension",
                $"File '{typed}' must have a .m or .mlapp extension.");
        }

        string name = Path.GetFileNameWithoutExtension(typed);
        if (Array.IndexOf(MatlabKeywordList, name) >= 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:FileNameFailsIsKeyword",
                "Name cannot be a MATLAB keyword.  Rename file, and then open.");
        }

        if (name.Length == 0 || name.Length > AppNameLengthMax || !char.IsAsciiLetter(name[0])
            || !name.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:FileNameFailsIsVarName",
                $"File name '{name}' must start with a letter, contain only letters, numbers and underscores, and be shorter than {AppNameLengthMax} characters.");
        }

        string candidate = typed;
        string? found = null;
        foreach (string ext in extension.Length > 0 ? [extension] : new[] { ".m", ".mlapp" })
        {
            candidate = extension.Length > 0 ? typed : typed + ext;
            string resolved = host.Resolve(candidate);
            found = File.Exists(resolved) ? Path.GetFullPath(resolved)
                : !Path.IsPathRooted(candidate) && interpreter.FunctionPath?.FindFile(candidate) is { } onPath ? onPath
                : null;
            if (found is not null)
            {
                break;
            }
        }

        if (found is null)
        {
            // R2025b names the file it looked for by its full path (fileattrib's name under the current folder), measured.
            string looked = Path.IsPathRooted(candidate) ? candidate : Path.GetFullPath(host.Resolve(candidate));
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:InvalidFileName", $"File '{looked}' not found.");
        }

        string folder = Path.GetDirectoryName(found)!;
        string? spelled = Directory.EnumerateFiles(folder, "*" + Path.GetExtension(found))
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(found), StringComparison.OrdinalIgnoreCase));
        found = spelled ?? found;

        string source;
        try
        {
            source = JgsMlapp.ReadSource(found);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, ComponentMetadataPrefix + "NotValidCustomComponentClass",
                $"File {found} is not a valid custom UI component class.");
        }

        if (!InheritsComponentContainer(interpreter, found, source, out bool isClass))
        {
            throw isClass
                ? new JgsRuntimeException(line, col, ComponentMetadataPrefix + "NotCustomComponentClass",
                    $"File {found} is not a valid custom UI component class because it does not inherit from {DesignComponentMetadata.Superclass}.")
                : new JgsRuntimeException(line, col, ComponentMetadataPrefix + "NotValidCustomComponentClass",
                    $"File {found} is not a valid custom UI component class.");
        }

        return found;
    }

    /// <summary>
    /// Whether the class in <paramref name="source"/> derives from the component container, directly or through
    /// superclasses of the user's found beside it or on the path (<c>isCustomComponentFromMetaClass</c>, which
    /// walks the metaclass; this walks the files' headers).
    /// </summary>
    private static bool InheritsComponentContainer(Interpreter interpreter, string file, string source, out bool isClass)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Inherits(interpreter, Path.GetDirectoryName(file) ?? string.Empty, source, seen, out isClass);
    }

    private static bool Inherits(Interpreter interpreter, string folder, string source, HashSet<string> seen, out bool isClass)
    {
        IReadOnlyList<string> supers = DesignComponentMetadata.Superclasses(source, out isClass);
        if (!isClass)
        {
            return false;
        }

        if (supers.Contains(DesignComponentMetadata.Superclass, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (string super in supers)
        {
            if (!seen.Add(super) || seen.Count > 32)
            {
                continue;
            }

            string shortName = super[(super.LastIndexOf('.') + 1)..];
            string? path = null;
            foreach (string candidate in new[] { Path.Combine(folder, shortName + ".m"), Path.Combine(folder, shortName + ".mlapp"), Path.Combine(folder, "@" + shortName, shortName + ".m") })
            {
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }

            path ??= interpreter.FunctionPath?.FindFile(shortName + ".m") ?? interpreter.FunctionPath?.FindFile(shortName + ".mlapp");
            if (path is null)
            {
                continue;
            }

            string text;
            try
            {
                text = JgsMlapp.ReadSource(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                continue;
            }

            if (Inherits(interpreter, Path.GetDirectoryName(path) ?? folder, text, seen, out _))
            {
                return true;
            }
        }

        return false;
    }
}
