using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using JGraph.NativeHost;
using JGraph.Scripting.Jgs.Native;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's shared-library interface (interop plan, stage 8, ADR 0181): <c>loadlibrary</c> by header
/// (a C compiler preprocesses it and <see cref="CHeaderParser"/> reads it) or by prototype file,
/// <c>unloadlibrary</c>, <c>libisloaded</c>, <c>libfunctions</c>, <c>mexext</c>,
/// <c>mex.getCompilerConfigurations</c>, and <c>calllib</c>, whose arguments, pointers and structs
/// are stage 9's (ADR 0182, <c>JgsBuiltins.LibPointers.cs</c>). Every sentence and identifier is
/// R2025b's (probe_shrlib_load, probe_shrlib_parse, probe_shrlib_warnids).
/// </summary>
/// <remarks>
/// A struct returned by value is a JGraph extension: R2025b cannot build a thunk for it, lists the
/// function in <c>notfound</c> and warns <c>MATLAB:loadlibrary:InvalidFunctionReturnType</c>. The
/// native host calls it through the x64 hidden return buffer, so JGraph loads it, lists it, and
/// answers the struct.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Declares the shared-library builtins into <paramref name="env"/>.</summary>
    internal static void RegisterSharedLibraryBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        env.Builtins.Register("loadlibrary", JgsValue.Function(new BuiltinFunction("loadlibrary",
            (args, line, col) => OperatingSystem.IsWindows() ? FirstOf(LoadLibrary(interpreter, SessionHost(interpreter, line, col), args, 1, line, col)) : throw NotOnWindows(line, col))
        {
            KeepsStringArguments = true,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => OperatingSystem.IsWindows() ? LoadLibrary(interpreter, SessionHost(interpreter, line, col), args, wanted, line, col) : throw NotOnWindows(line, col),
        }));
        env.Builtins.Register("unloadlibrary", JgsValue.Function(new BuiltinFunction("unloadlibrary",
            (args, line, col) => OperatingSystem.IsWindows() ? FirstOf(UnloadLibrary(SessionHost(interpreter, line, col), args, line, col)) : throw NotOnWindows(line, col))
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
        }));
        env.Builtins.Register("libisloaded", JgsValue.Function(new BuiltinFunction("libisloaded", (args, line, col) =>
        {
            if (args.Count != 1 || !IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:libisloaded:LibraryNameRequired", "Library name must be a string or a character vector.");
            }

            return JgsValue.Bool(OperatingSystem.IsWindows() && interpreter.Host is { } host && LoadedLibrary(host, TextOf(args[0])) is not null);
        })
        {
            KeepsStringArguments = true,
        }));
        env.Builtins.Register("libfunctions", JgsValue.Function(new BuiltinFunction("libfunctions",
            (args, line, col) => OperatingSystem.IsWindows() ? FirstOf(LibFunctions(interpreter, args, 1, line, col)) : throw NotOnWindows(line, col))
        {
            KeepsStringArguments = true,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => OperatingSystem.IsWindows() ? LibFunctions(interpreter, args, wanted, line, col) : throw NotOnWindows(line, col),
        }));
        env.Builtins.Register("calllib", JgsValue.Function(new BuiltinFunction("calllib",
            (args, line, col) => OperatingSystem.IsWindows() ? FirstOf(CallLib(interpreter, SessionHost(interpreter, line, col), args, 1, line, col)) : throw NotOnWindows(line, col))
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) => OperatingSystem.IsWindows() ? CallLib(interpreter, SessionHost(interpreter, line, col), args, wanted, line, col) : throw NotOnWindows(line, col),
        }));
        RegisterLibPointerBuiltins(env, interpreter);
        RegisterMethodsViewBuiltins(env, interpreter);
        env.Builtins.Register("mexext", JgsValue.Function(new BuiltinFunction("mexext", (args, line, col) => MexExt(args, line, col))
        {
            KeepsStringArguments = true,
            AutoCallsBare = true,
        }));
        env.Builtins.RegisterConstant("mex", JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["getCompilerConfigurations"] = JgsValue.Function(new BuiltinFunction("mex.getCompilerConfigurations",
                (args, line, col) => CompilerConfigurations(args, line, col))
            {
                KeepsStringArguments = true,
                AutoCallsBare = true,
            }),
        }));
    }

    /// <summary>The session a shared-library builtin runs in, whose native host it uses.</summary>
    private static JGraphScriptGlobals SessionHost(Interpreter interpreter, int line, int col) =>
        interpreter.Host ?? throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:NotSupported", "This session has no native host.");

    private static JgsRuntimeException NotOnWindows(int line, int col) =>
        new(line, col, "JGraph:loadlibrary:NotSupported", "Shared libraries are loaded on Windows only.");

    private static JgsValue FirstOf(JgsValue[] outputs) => outputs is [var first, ..] ? first : JgsValue.Null;

    [SupportedOSPlatform("windows")]
    private static SharedLibrary? LoadedLibrary(JGraphScriptGlobals host, string name) =>
        host.Native.Library(name.StartsWith("lib.", StringComparison.Ordinal) ? name[4..] : name);

    // ------------------------------------------------------------------------------------------
    // loadlibrary
    // ------------------------------------------------------------------------------------------

    private static readonly string[] LoadLibraryOptions = ["addheader", "includepath", "alias", "mfilename", "thunkfilename"];

    /// <summary>
    /// <c>[notfound, warnings] = loadlibrary(lib, header | @protofile, Name, Value, …)</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue[] LoadLibrary(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:NotEnoughInputs", "The library name must be specified.");
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
        }

        string library = TextOf(args[0]);
        var addHeaders = new List<string>();
        var includes = new List<string>();
        string? alias = null, mfile = null;
        int optionCount = Math.Max(0, args.Count - 2);
        for (int i = 2; i < args.Count; i += 2)
        {
            string option = IsTextScalar(args[i]) ? TextOf(args[i]) : "";
            if (!LoadLibraryOptions.Contains(option, StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:InvalidOption", $"Option {option} is not a valid loadlibrary option.");
            }

            if (i + 1 >= args.Count)
            {
                // R2025b indexes past its option list here (probe_shrlib_parse: load.odd.options).
                throw new JgsRuntimeException(line, col, "MATLAB:badsubscript",
                    $"Index exceeds the number of array elements. Index must not exceed {optionCount}.");
            }

            JgsValue value = args[i + 1];
            if (!IsTextScalar(value))
            {
                throw option == "alias"
                    ? new JgsRuntimeException(line, col, "MATLAB:libisloaded:LibraryNameRequired", "Library name must be a string or a character vector.")
                    : new JgsRuntimeException(line, col, "MATLAB:perl:InputsMustBeStrings", "All arguments must be character vectors or string scalars.");
            }

            string text = TextOf(value);
            switch (option)
            {
                case "addheader":
                    addHeaders.Add(text);
                    break;
                case "includepath":
                    includes.Add(text);
                    break;
                case "alias":
                    alias = text;
                    break;
                case "mfilename":
                    mfile = text;
                    break;
                    // thunkfilename: no thunk is built (the host calls through calli), so it names nothing.
            }
        }

        string name = alias ?? Path.GetFileNameWithoutExtension(library);
        NativeSession session = host.Native;
        if (session.Library(name) is not null)
        {
            Warn(host, "MATLAB:loadlibrary:ClassIsLoaded", $"The library class '{name}' already exists.  Use a classname alias.");
            return [JgsEmpty.Zero(), JgsValue.Str("")];
        }

        IEnumerable<string> pathFolders = interpreter.FunctionPath?.Folders ?? (IEnumerable<string>)[];
        string folder = host.CurrentDirectory;
        string libraryPath = NativeSession.FindLibrary(library, folder, pathFolders) ?? library;

        // Which road: a function handle, or text naming a prototype function rather than a header.
        JgsValue? second = args.Count > 1 ? args[1] : null;
        IJgsCallable? prototype = null;
        string? header = null;
        if (second is { Type: JgsType.Function })
        {
            prototype = second.AsCallable;
        }
        else if (second is not null && !IsTextScalar(second))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
        }
        else
        {
            string given = second is null ? Path.GetFileNameWithoutExtension(library) + ".h" : TextOf(second);
            prototype = second is null ? null : PrototypeFunction(interpreter, given, folder, pathFolders);
            if (prototype is null)
            {
                header = FindHeader(given, folder, Path.GetDirectoryName(libraryPath), pathFolders)
                    ?? throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:FileNotFound", $"Could not find file {given}.");
            }
        }

        // The library first: a load that cannot find it fails before any compiler runs.
        NativeHostProcess process;
        long module;
        try
        {
            process = session.Host;
            module = process.Load(libraryPath);
        }
        catch (NativeHostFailure failure)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:LoadFailed",
                $"There was an error loading the library \"{libraryPath}\"\n{failure.Message.Replace("%1", libraryPath, StringComparison.Ordinal).TrimEnd()}");
        }
        catch (NativeHostExitedException exited)
        {
            session.Forget();
            throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:HostExited", exited.Sentence($"loadlibrary('{library}')"));
        }

        LibraryModel model;
        string warningsText = "";
        int parseWarnings = 0;
        try
        {
            if (prototype is not null)
            {
                model = ReadPrototype(interpreter, prototype, libraryPath, line, col);
            }
            else
            {
                CHeaderParser.Result parsed = ParseHeader(interpreter, header!, addHeaders, includes, folder, line, col);
                model = parsed.Model;
                warningsText = parsed.Warnings;
                parseWarnings = parsed.WarningCount;
                if (mfile is not null)
                {
                    WritePrototype(model, mfile, header!, name, folder);
                }
            }
        }
        catch
        {
            if (process.IsAlive)
            {
                process.Free(module);
            }

            throw;
        }

        var loaded = new SharedLibrary(name, libraryPath, process, module, model);
        var notFound = new List<string>();
        foreach (LibFunction function in model.Functions)
        {
            long address;
            try
            {
                address = process.Symbol(module, function.Name);
            }
            catch (NativeHostFailure)
            {
                notFound.Add(function.Name);
                continue;
            }

            loaded.Functions[function.CallName] = function;
            loaded.Addresses[function.CallName] = address;
        }

        // R2025b's warnings, in its order (probe_shrlib_parse, probe_shrlib_warnids).
        if (header is not null && parseWarnings > 0 && wanted < 2)
        {
            Warn(host, "MATLAB:loadlibrary:parsewarnings",
                "Warnings messages were produced while parsing.  Check the functions you intend to use for correctness.  Warning text can be viewed using:\n [notfound,warnings]=loadlibrary(...)");
        }

        foreach (LibFunction function in model.Functions)
        {
            if (function.Lhs == "error" || function.Rhs.Contains("error"))
            {
                Warn(host, "MATLAB:loadlibrary:TypeNotFound", $"The data type 'error' used by function {function.Name} does not exist.");
            }
        }

        foreach (LibStruct type in model.Structs)
        {
            foreach (LibMember member in type.Members)
            {
                if (UnusableMember(model, member.Type))
                {
                    Warn(host, "MATLAB:loadlibrary:TypeNotFoundForStructure",
                        $"The data type '{member.Type}' used by structure {type.Name} does not exist.  The structure may not be usable.");
                }
            }
        }

        foreach (SharedLibrary other in session.Libraries.Values)
        {
            foreach (LibEnum type in model.Enums.Where(e => other.Model.Enum(e.Name) is not null))
            {
                Warn(host, "MATLAB:loadlibrary:EnumExists", $"The enumeration 'lib.{type.Name}' already exists and will not be created");
            }

            foreach (LibStruct type in model.Structs.Where(s => other.Model.Struct(s.Name) is not null))
            {
                Warn(host, "MATLAB:loadlibrary:StructTypeExists", $"The structure type '{type.Name}' already exists.\nThe existing type will be reused.");
            }
        }

        foreach (string missing in notFound)
        {
            Warn(host, "MATLAB:loadlibrary:FunctionNotFound", $"The function '{missing}' was not found in the library");
        }

        if (header is not null && loaded.Functions.Count == 0)
        {
            Warn(host, "MATLAB:loadlibrary:nofunctions", "No functions found in library.");
        }

        session.Libraries[name] = loaded;
        interpreter.NoteNet(); // its pointers and libstructs dispatch methods (ADR 0182)
        JgsValue notFoundValue;
        if (notFound.Count == 0)
        {
            notFoundValue = JgsValue.Cell([]);
            notFoundValue.Reshape(0, 0);
        }
        else
        {
            notFoundValue = JgsValue.Cell([.. notFound.Select(JgsValue.Str)]);
            notFoundValue.Reshape(1, notFound.Count);
        }

        return [notFoundValue, JgsValue.Str(warningsText)];
    }

    /// <summary>
    /// Whether a struct member's type is one R2025b warns a struct "may not be usable" for: a type
    /// nothing named (<c>error</c>), a function pointer, or an array of structs.
    /// </summary>
    private static bool UnusableMember(LibraryModel model, string type)
    {
        if (type is "error" or "FcnPtr")
        {
            return true;
        }

        int hash = type.LastIndexOf('#');
        return hash > 0 && model.Struct(type[..hash]) is not null;
    }

    /// <summary>
    /// The prototype function text names, when it names one rather than a header: <c>name.m</c>, or
    /// a name without an extension that is no header file and resolves to a function. R2025b takes
    /// <c>loadlibrary(lib, 'jgtestlib_proto')</c> as the prototype file (probe_shrlib_parse).
    /// </summary>
    private static IJgsCallable? PrototypeFunction(Interpreter interpreter, string text, string folder, IEnumerable<string> pathFolders)
    {
        string extension = Path.GetExtension(text);
        string stem;
        if (extension.Equals(".m", StringComparison.OrdinalIgnoreCase))
        {
            stem = Path.GetFileNameWithoutExtension(text);
        }
        else if (extension.Length == 0 && FindHeader(text + ".h", folder, null, pathFolders) is null && FindHeader(text, folder, null, pathFolders) is null)
        {
            stem = text;
        }
        else
        {
            return null;
        }

        Resolution found = interpreter.Resolver.Invoke(stem, interpreter.CurrentFrame);
        return found.Found && found.Value.Type == JgsType.Function ? found.Value.AsCallable : null;
    }

    /// <summary>A header's file: as given, from the current folder, the library's folder, then the path.</summary>
    private static string? FindHeader(string header, string folder, string? libraryFolder, IEnumerable<string> pathFolders)
    {
        if (Path.IsPathRooted(header))
        {
            return File.Exists(header) ? header : null;
        }

        IEnumerable<string> folders = pathFolders.Prepend(libraryFolder ?? "").Prepend(folder);
        foreach (string candidate in folders.Where(f => f.Length > 0).Select(f => Path.Combine(f, header)))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static LibraryModel ReadPrototype(Interpreter interpreter, IJgsCallable prototype, string libraryPath, int line, int col)
    {
        JgsValue[] outputs;
        try
        {
            outputs = prototype is IJgsMultiCallable several ? several.CallMultiple([], 4, line, col) : [prototype.Call([], line, col)];
        }
        catch (JgsRuntimeException failure)
        {
            // R2025b keeps the failure's identifier and puts its own sentence first (probe_shrlib_parse).
            throw new JgsRuntimeException(line, col, failure.Identifier.Length > 0 ? failure.Identifier : "MATLAB:loadlibrary:LoadFailed",
                $"There was an error loading the library \"{libraryPath}\"\n{failure.Message}");
        }

        JgsValue Output(int i) => i < outputs.Length ? outputs[i] : JgsValue.Null;
        try
        {
            return PrototypeFile.Read(Output(0), Output(1), Output(2));
        }
        catch (FormatException bad)
        {
            throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:BadPrototype",
                $"There was an error loading the library \"{libraryPath}\"\nThe prototype file's {bad.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static CHeaderParser.Result ParseHeader(
        Interpreter interpreter, string header, IReadOnlyList<string> addHeaders, IReadOnlyList<string> includes, string folder, int line, int col)
    {
        CCompiler compiler = CCompilers.Selected()
            ?? throw new JgsRuntimeException(line, col, CCompilers.NoCompilerIdentifier, CCompilers.NoCompilerMessage);
        CCompilers.Preprocessed preprocessed;
        try
        {
            preprocessed = CCompilers.Preprocess(compiler, header, includes, folder, interpreter.Cancellation);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:cppfailure",
                $"Failed to preprocess the input file.\nOutput from preprocessor is:{ex.Message}");
        }

        if (!preprocessed.Succeeded)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:cppfailure",
                $"Failed to preprocess the input file.\nOutput from preprocessor is:{preprocessed.Diagnostics.Replace("\r\n", "\n", StringComparison.Ordinal)}");
        }

        var named = new List<string> { Path.GetFileName(header) };
        named.AddRange(addHeaders);
        return CHeaderParser.Parse(preprocessed.Text, named, Path.GetFileName(header));
    }

    /// <summary>
    /// Writes <paramref name="model"/> as a prototype file. The folder <paramref name="mfile"/> names is
    /// kept — R2025b writes into the current folder whatever folder it is given, a defect recorded as a
    /// divergence (plan step 0, finding 9).
    /// </summary>
    private static void WritePrototype(LibraryModel model, string mfile, string header, string library, string folder)
    {
        string path = Path.IsPathRooted(mfile) ? mfile : Path.Combine(folder, mfile);
        if (!path.EndsWith(".m", StringComparison.OrdinalIgnoreCase))
        {
            path += ".m";
        }

        string name = Path.GetFileNameWithoutExtension(path);
        File.WriteAllText(path, PrototypeFile.Write(model, name, Path.GetFileNameWithoutExtension(header), library, DateTime.Now) + "\n");
    }

    // ------------------------------------------------------------------------------------------
    // unloadlibrary, libfunctions
    // ------------------------------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static JgsValue[] UnloadLibrary(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || !IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unloadlibrary:NameMustBeSpecified", "Library name must be specified.");
        }

        SharedLibrary library = LoadedLibrary(host, TextOf(args[0]))
            ?? throw new JgsRuntimeException(line, col, "MATLAB:unloadlibrary:ClassNotFound", "Could not find library class to unload it.");

        // A libstruct holds its library; a plain lib.pointer does not (R2025b, shrlib_pointers).
        if (library.HasLiveStructs())
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unloadlibrary:IsInUse", "Cannot unload a library that has outstanding objects.");
        }

        host.Native.Libraries.Remove(library.Name);
        if (library.Host.IsAlive)
        {
            try
            {
                library.Host.Free(library.Module);
            }
            catch (NativeHostFailure)
            {
                // The module is gone from the host already; the library is unloaded either way.
            }
            catch (NativeHostExitedException)
            {
                host.Native.Forget();
            }
        }

        return [JgsValue.Null];
    }

    /// <summary>
    /// <c>libfunctions(lib)</c> and <c>libfunctions(lib, '-full')</c>: R2025b builds both on
    /// <c>methods('lib.&lt;name&gt;')</c>, so the listing is the 180-column <c>methods</c> layout
    /// headed "Functions in library", and the answer is a cell column (libfunctions.m).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue[] LibFunctions(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        if (!IsTextScalar(args[0]))
        {
            // libfunctions.m hands anything else to methods, which lists the value's class.
            if (interpreter.Globals.Builtins.TryGet("methods", out JgsValue methods) && methods.Type == JgsType.Function)
            {
                return methods.AsCallable is IJgsMultiCallable several ? several.CallMultiple(args, wanted, line, col) : [methods.AsCallable.Call(args, line, col)];
            }

            return [JgsEmpty.Zero()];
        }

        bool full = false;
        if (args.Count == 2)
        {
            if (!IsTextScalar(args[1]) || TextOf(args[1]) != "-full")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:badopt", "Unknown command option.");
            }

            full = true;
        }

        string name = TextOf(args[0]).Replace("\"", "", StringComparison.Ordinal);
        if (name.StartsWith("lib.", StringComparison.Ordinal))
        {
            name = name[4..];
        }

        SharedLibrary? library = interpreter.Host is { } host ? LoadedLibrary(host, name) : null;
        if (library is null)
        {
            if (wanted == 0)
            {
                interpreter.Host?.WriteOut($"\nNo class 'lib.{name}'.\n\n");
                return [];
            }

            return [JgsEmpty.Zero()];
        }

        string[] names = [.. LibraryFunctionNames(library)];
        string[] lines = full ? [.. names.Select(n => library.Model.Signature(library.Functions[n]))] : names;
        if (wanted > 0)
        {
            return [CellColumn(lines)];
        }

        var text = new StringBuilder("\nFunctions in library ").Append(name).Append(":\n\n");
        if (full)
        {
            text.AppendJoin("\n", lines);
        }
        else
        {
            int width = names.Length == 0 ? 2 : names.Max(n => n.Length) + 2;
            int columns = Math.Max(1, 180 / width);
            int rows = (names.Length + columns - 1) / columns;
            for (int r = 0; r < rows; r++)
            {
                for (int i = r; i < names.Length; i += rows)
                {
                    text.Append(names[i].PadRight(width));
                }

                text.Append('\n');
            }
        }

        interpreter.Host?.WriteOut(text.Append("\n\n").ToString());
        return [];
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> LibraryFunctionNames(SharedLibrary library) =>
        library.Functions.Keys.OrderBy(n => n, StringComparer.Ordinal);

    // ------------------------------------------------------------------------------------------
    // calllib
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[x1, …, xN] = calllib(lib, fn, args…)</c>: the return value, then each pointer argument's
    /// value after the call (stage 9, ADR 0182; <see cref="Argument"/> converts each argument). An
    /// exported variable answers a <c>lib.pointer</c> to itself.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue[] CallLib(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2 || !IsTextScalar(args[0]) || !IsTextScalar(args[1]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calllib:NameAndFunctionNeeded",
                "To call a function both the library name and function name are needed.");
        }

        string libraryName = TextOf(args[0]);
        string functionName = TextOf(args[1]);
        SharedLibrary library = LoadedLibrary(host, libraryName)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:calllib:NotFound", "Library was not found");
        if (!library.Functions.TryGetValue(functionName, out LibFunction? function))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calllib:MethodNotFound", "Method was not found.");
        }

        if (function.IsData)
        {
            // The variable itself, through a pointer (R2025b, probe_shrlib_pointers2 msg.data.export).
            if (args.Count != 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:calllib:NoMatchingSignatureFound", "No method with matching signature.");
            }

            return [ReturnedPointer(library.Host, library.Model, function.Lhs ?? "voidPtr", library.Addresses[functionName])];
        }

        IReadOnlyList<string> parameters = function.Rhs;
        int outputs = (function.Lhs is null ? 0 : 1) + parameters.Count(LibTypes.IsOutputArgument);
        if (args.Count - 2 != parameters.Count || wanted > Math.Max(outputs, function.Lhs is null ? 0 : 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calllib:NoMatchingSignatureFound", "No method with matching signature.");
        }

        LibraryModel model = library.Model;
        NativeHostProcess process = library.Host;
        var slots = new Slot[parameters.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = ParameterSlot(model, parameters[i]);
        }

        byte[] cells = new byte[slots.Sum(s => s.WireSize)];
        var call = new LibCall(library, interpreter.Dialect, line, col);
        string during = $"calllib('{libraryName}', '{functionName}')";
        try
        {
            int offset = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                Argument(call, parameters[i], args[i + 2], cells.AsSpan(offset, slots[i].WireSize));
                offset += slots[i].WireSize;
            }

            Slot result = function.Lhs is { } returns ? ReturnSlot(model, returns, functionName, line, col) : new Slot(SlotKind.Void);
            process.Sync(host.CurrentDirectory);
            byte[] answer = process.Call(library.Addresses[functionName], result, slots, cells, interpreter.Cancellation);
            var values = new List<JgsValue>();
            if (function.Lhs is { } lhs)
            {
                values.Add(ReturnValue(host, process, model, lhs, answer));
            }

            foreach (Func<JgsValue> output in call.Outputs)
            {
                values.Add(output());
            }

            return [.. values];
        }
        catch (NativeHostExitedException exited)
        {
            call.Temporaries.Clear();
            host.Native.Forget();
            string sentence = exited.Sentence(during);
            if (exited.Cancelled)
            {
                host.WriteErr(sentence + "\n");
                throw new OperationCanceledException(sentence);
            }

            throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:HostExited", sentence);
        }
        finally
        {
            if (process.IsAlive)
            {
                foreach (long buffer in call.Temporaries)
                {
                    process.Release(buffer);
                }
            }
        }
    }

    /// <summary>The host slot a scalar or an enum travels in.</summary>
    private static Slot ScalarSlot(LibraryModel model, string type) => type switch
    {
        "int8" => new Slot(SlotKind.Int8),
        "uint8" or "bool" => new Slot(SlotKind.UInt8),
        "int16" => new Slot(SlotKind.Int16),
        "uint16" => new Slot(SlotKind.UInt16),
        "int32" or "long" => new Slot(SlotKind.Int32),
        "uint32" or "ulong" => new Slot(SlotKind.UInt32),
        "int64" => new Slot(SlotKind.Int64),
        "uint64" => new Slot(SlotKind.UInt64),
        "single" => new Slot(SlotKind.Single),
        "double" => new Slot(SlotKind.Double),
        _ when model.Enum(type) is not null => new Slot(SlotKind.Int32),
        _ => throw new ArgumentException($"'{type}' is not a scalar type.", nameof(type)),
    };

    /// <summary>The host slot a return of <paramref name="type"/> comes back in.</summary>
    private static Slot ReturnSlot(LibraryModel model, string type, string function, int line, int col)
    {
        if (LibTypes.IsScalar(type) || model.Enum(type) is not null)
        {
            return ScalarSlot(model, type);
        }

        if (LibTypes.IsPointer(type))
        {
            return new Slot(SlotKind.Pointer);
        }

        if (model.Struct(type) is { } structure && model.StructLayout(structure) is { } layout)
        {
            return new Slot(SlotKind.Struct, layout.Size);
        }

        throw new JgsRuntimeException(line, col, "JGraph:calllib:UnsupportedReturn",
            $"calllib cannot return the type '{type}' that function {function} returns.");
    }

    private static void WriteScalar(Span<byte> cell, SlotKind kind, double number)
    {
        double rounded = Math.Round(number, MidpointRounding.AwayFromZero);
        switch (kind)
        {
            case SlotKind.Single:
                BinaryPrimitives.WriteSingleLittleEndian(cell, (float)number);
                break;
            case SlotKind.Double:
                BinaryPrimitives.WriteDoubleLittleEndian(cell, number);
                break;
            case SlotKind.Int8:
                BinaryPrimitives.WriteInt64LittleEndian(cell, (long)Math.Clamp(rounded, sbyte.MinValue, sbyte.MaxValue));
                break;
            case SlotKind.UInt8:
                BinaryPrimitives.WriteInt64LittleEndian(cell, (long)Math.Clamp(rounded, byte.MinValue, byte.MaxValue));
                break;
            case SlotKind.Int16:
                BinaryPrimitives.WriteInt64LittleEndian(cell, (long)Math.Clamp(rounded, short.MinValue, short.MaxValue));
                break;
            case SlotKind.UInt16:
                BinaryPrimitives.WriteInt64LittleEndian(cell, (long)Math.Clamp(rounded, ushort.MinValue, ushort.MaxValue));
                break;
            case SlotKind.Int32:
                BinaryPrimitives.WriteInt64LittleEndian(cell, (long)Math.Clamp(rounded, int.MinValue, int.MaxValue));
                break;
            case SlotKind.UInt32:
                BinaryPrimitives.WriteInt64LittleEndian(cell, (long)Math.Clamp(rounded, uint.MinValue, uint.MaxValue));
                break;
            case SlotKind.UInt64:
                BinaryPrimitives.WriteUInt64LittleEndian(cell, rounded <= 0 ? 0 : rounded >= 18446744073709551615.0 ? ulong.MaxValue : (ulong)rounded);
                break;
            default:
                BinaryPrimitives.WriteInt64LittleEndian(cell, rounded <= long.MinValue ? long.MinValue : rounded >= 9223372036854775807.0 ? long.MaxValue : (long)rounded);
                break;
        }
    }

    /// <summary>
    /// A return value as R2025b answers it (plan step 0, finding 8): every number as double,
    /// <c>bool</c> as logical, a <c>cstring</c> as char, an enum as its member's name, a pointer as a
    /// <c>lib.pointer</c>, and — JGraph's own — a struct by value as a MATLAB struct of its members.
    /// A 64-bit integer past 2^53 is held to a double's precision with the interop warning (plan,
    /// question 2); UINT64_MAX is 1.8447e19, where R2025b answers -1 (ADR 0182).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue ReturnValue(JGraphScriptGlobals host, NativeHostProcess process, LibraryModel model, string type, byte[] answer)
    {
        if (type == "cstring")
        {
            return JgsValue.Str(ReadNativeString(process, BinaryPrimitives.ReadInt64LittleEndian(answer)));
        }

        if (LibTypes.IsPointer(type))
        {
            return ReturnedPointer(process, model, type, BinaryPrimitives.ReadInt64LittleEndian(answer));
        }

        if (model.Enum(type) is { } enumeration)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(answer);
            foreach ((string name, long number) in enumeration.Members)
            {
                if (number == value)
                {
                    return JgsValue.Str(name);
                }
            }

            return JgsValue.Number(value);
        }

        if (model.Struct(type) is { } structure)
        {
            return StructOfBytes(process, model, structure, answer);
        }

        if (type is "int64" or "uint64")
        {
            string exact = type == "int64"
                ? BinaryPrimitives.ReadInt64LittleEndian(answer).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : BinaryPrimitives.ReadUInt64LittleEndian(answer).ToString(System.Globalization.CultureInfo.InvariantCulture);
            double held = GetElement(answer, type);
            if (Math.Abs(held) > 9007199254740992.0)
            {
                Warn(host, "JGraph:interop:int64Precision",
                    $"The 64-bit integer {exact} is past 2^53 and is held to the nearest value a double represents.");
            }
        }

        double x = GetElement(answer, type);
        return type == "bool" ? JgsValue.Bool(x != 0) : JgsValue.Number(x);
    }

    // ------------------------------------------------------------------------------------------
    // mexext, mex.getCompilerConfigurations
    // ------------------------------------------------------------------------------------------

    /// <summary><c>mexext</c> and <c>mexext('all')</c>, R2025b's four platforms in its order.</summary>
    private static JgsValue MexExt(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        if (args.Count == 0)
        {
            return JgsValue.Str("mexw64");
        }

        if (!IsTextScalar(args[0]) || TextOf(args[0]) != "all")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mexext:inputType", "Input must be 'all' or \"all\".");
        }

        (string Arch, string Ext)[] platforms = [("glnxa64", "mexa64"), ("maca64", "mexmaca64"), ("maci64", "mexmaci64"), ("win64", "mexw64")];
        return JgsValue.StructArray([.. platforms.Select(p => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["ext"] = JgsValue.Str(p.Ext),
            ["arch"] = JgsValue.Str(p.Arch),
        })]);
    }

    /// <summary>
    /// <c>mex.getCompilerConfigurations(lang, list)</c>: the compilers <c>loadlibrary</c> can use, as
    /// <c>mex.CompilerConfiguration</c>-shaped structs with R2025b's eleven properties. JGraph builds
    /// no MEX files, so <c>'Supported'</c> answers what is installed, and <c>Details</c> describes the
    /// preprocessing <c>loadlibrary</c> runs.
    /// </summary>
    private static JgsValue CompilerConfigurations(IReadOnlyList<JgsValue> args, int line, int col)
    {
        string language = args.Count > 0 && IsTextScalar(args[0]) ? TextOf(args[0]) : "Any";
        string list = args.Count > 1 && IsTextScalar(args[1]) ? TextOf(args[1]) : "Selected";
        string? canonicalLanguage = language.ToUpperInvariant() switch
        {
            "C" => "C",
            "C++" or "CPP" => "C++",
            "FORTRAN" => "Fortran",
            "ANY" => "Any",
            _ => null,
        };
        if (canonicalLanguage is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mex:UnknownSwitch", $"Unknown MEX argument '{language}'.");
        }

        if (list.ToUpperInvariant() is not ("SELECTED" or "INSTALLED" or "SUPPORTED"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mex:UnknownSwitch", $"Unknown MEX argument '{list}'.");
        }

        var compilers = new List<CCompiler>();
        if (OperatingSystem.IsWindows())
        {
            if (list.Equals("Selected", StringComparison.OrdinalIgnoreCase))
            {
                if (CCompilers.Selected() is { } selected)
                {
                    compilers.Add(selected);
                }
            }
            else
            {
                compilers.AddRange(CCompilers.Discover());
            }
        }

        var configurations = new List<Dictionary<string, JgsValue>>();
        foreach (CCompiler compiler in compilers)
        {
            if (canonicalLanguage is "C++" or "Any")
            {
                configurations.Add(Configuration(compiler, cpp: true));
            }

            if (canonicalLanguage is "C" or "Any")
            {
                configurations.Add(Configuration(compiler, cpp: false));
            }
        }

        JgsValue value = configurations.Count == 1
            ? JgsValue.Struct(configurations[0])
            : JgsValue.StructArray([.. configurations]);
        value.SetClassName("mex.CompilerConfiguration");
        return value;
    }

    private static Dictionary<string, JgsValue> Configuration(CCompiler compiler, bool cpp)
    {
        string name = cpp ? compiler.Name.Replace(" (C)", "", StringComparison.Ordinal) : compiler.Name;
        string shortName = cpp
            ? compiler.IsMsvc ? compiler.ShortName.Replace("MSVC", "MSVCPP", StringComparison.Ordinal) : compiler.ShortName + "-g++"
            : compiler.ShortName;
        var details = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["CompilerExecutable"] = JgsValue.Str(compiler.IsMsvc ? "cl" : "gcc"),
            ["CompilerFlags"] = JgsValue.Str(compiler.IsMsvc ? "/nologo /E /TC /Zp8" : "-E -x c"),
            ["OptimizationFlags"] = JgsValue.Str(""),
            ["DebugFlags"] = JgsValue.Str(""),
            ["LinkerExecutable"] = JgsValue.Str(compiler.IsMsvc ? "link" : "gcc"),
            ["LinkerFlags"] = JgsValue.Str(""),
            ["LinkerOptimizationFlags"] = JgsValue.Str(""),
            ["LinkerDebugFlags"] = JgsValue.Str(""),
            ["SetEnv"] = JgsValue.Str(""),
            ["CommandLineShell"] = JgsValue.Str(compiler.Executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ? compiler.Executable : ""),
            ["CommandLineShellArg"] = JgsValue.Str(""),
        });
        details.SetClassName("mex.CompilerConfigurationDetails");
        return new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(name),
            ["Manufacturer"] = JgsValue.Str(compiler.Manufacturer),
            ["Language"] = JgsValue.Str(cpp ? "C++" : "C"),
            ["Version"] = JgsValue.Str(compiler.Version),
            ["Location"] = JgsValue.Str(compiler.Location),
            ["ShortName"] = JgsValue.Str(shortName),
            ["Priority"] = JgsValue.Str("A"),
            ["Details"] = details,
            ["LinkerName"] = JgsValue.Str(compiler.IsMsvc ? "link" : "gcc"),
            ["LinkerVersion"] = JgsValue.Str(""),
            ["MexOpt"] = JgsValue.Str(""),
        };
    }
}
