using System.Reflection;
using System.Runtime.InteropServices;
using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>NET.*</c> names, <c>dotnetenv</c> and <c>meta.class.fromName</c> (interop plan, stage 1,
/// ADR 0174). Registered in both dialects; the MATLAB dialect is the one the fixtures pin.
/// </summary>
/// <remarks>
/// <para>
/// <b>The runtime is JGraph's own.</b> R2025b chooses between .NET Framework and .NET (core) with
/// <c>dotnetenv</c> before the first .NET call, and the fixtures pin it to .NET 8 so both engines call
/// the same base class library. JGraph runs on that runtime and cannot host another, so
/// <c>dotnetenv</c> reports it as loaded, accepts a request that names it, and refuses a switch with
/// R2025b's own <c>MATLAB:netenv:NETLoaded</c> — the answer R2025b gives once .NET is loaded.
/// </para>
/// <para>
/// <c>NET.addAssembly</c> takes a path in stage 1 (a name and an <c>AssemblyName</c> are stage 3's),
/// and <c>NET.createArray</c> and <c>NET.createGeneric</c> take the forms the stage 1 fixtures build
/// their inputs with; stage 4 completes them.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Declares the .NET builtins into <paramref name="env"/>.</summary>
    internal static void RegisterNetBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        JgsValue Builtin(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body, bool bare = false) =>
            JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = bare, KeepsStringArguments = true });

        var net = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["isNETSupported"] = Builtin("NET.isNETSupported", (args, line, col) =>
            {
                Arity("NET.isNETSupported", args, 0, line, col);
                interpreter.NoteNet(); // R2025b loads the runtime to answer it (net_assembly)
                return JgsValue.True;
            }, bare: true),
            ["addAssembly"] = Builtin("NET.addAssembly", (args, line, col) => AddAssembly(interpreter, args, line, col)),
            ["createArray"] = Builtin("NET.createArray", (args, line, col) => CreateNetArray(interpreter, args, line, col)),
            ["createGeneric"] = Builtin("NET.createGeneric", (args, line, col) => CreateGeneric(interpreter, args, line, col)),
            ["GenericClass"] = Builtin("NET.GenericClass", (args, line, col) => GenericClass(interpreter, args, line, col)),
            ["invokeGenericMethod"] = JgsValue.Function(new BuiltinFunction("NET.invokeGenericMethod",
                (args, line, col) => InvokeGenericMethod(interpreter, args, 1, line, col) is [var first, ..] ? first : JgsValue.Null)
            {
                KeepsStringArguments = true,
                MultiOutput = (args, wanted, line, col) => InvokeGenericMethod(interpreter, args, wanted, line, col),
            }),
            ["setStaticProperty"] = Builtin("NET.setStaticProperty", (args, line, col) => SetStaticProperty(interpreter, args, line, col)),
            ["explicitCast"] = Builtin("NET.explicitCast", (args, line, col) => ExplicitCast(interpreter, args, line, col)),
            ["interfaceView"] = Builtin("NET.interfaceView", (args, line, col) => ExplicitCast(interpreter, args, line, col)),
            ["convertArray"] = Builtin("NET.convertArray", (args, line, col) => ConvertArray(interpreter, args, line, col)),
            ["disableAutoRelease"] = Builtin("NET.disableAutoRelease", (args, line, col) => AutoRelease("NET.disableAutoRelease", args, line, col)),
            ["enableAutoRelease"] = Builtin("NET.enableAutoRelease", (args, line, col) => AutoRelease("NET.enableAutoRelease", args, line, col)),
        };
        env.Builtins.RegisterConstant("NET", JgsValue.Struct(net));

        env.Builtins.Register("dotnetenv", Builtin("dotnetenv", (args, line, col) => DotNetEnv(interpreter, args, line, col), bare: true));

        // import with no argument lists what the running code imported; with names it adds them
        // (ADR 0176). The statement form 'import System.IO.*' is the parser's ImportStmt.
        env.Builtins.Register("import", Builtin("import", (args, line, col) =>
            args.Count == 0 ? interpreter.ImportList() : interpreter.ImportByCall(args, line, col), bare: true));

        // enumeration(type) prints a .NET enum's members, and answers them only when there is one
        // (ADR 0177). JGraph's classdef has no enumeration block, so a .NET type is all it can name.
        env.Builtins.Register("enumeration", JgsValue.Function(new BuiltinFunction("enumeration", (args, line, col) =>
            Enumeration(interpreter, args, 1, line, col)[0])
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) => Enumeration(interpreter, args, wanted, line, col),
        }));

        // isjava: JGraph has no Java, so nothing is a Java object — a .NET one included (R2025b: false).
        env.Builtins.Register("isjava", Builtin("isjava", (args, line, col) =>
        {
            Arity("isjava", args, 1, line, col);
            return JgsValue.False;
        }));

        var metaClass = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["fromName"] = Builtin("meta.class.fromName", (args, line, col) =>
            {
                Arity("meta.class.fromName", args, 1, line, col);
                return interpreter.MetaClassFromName(TextOf(args[0]));
            }),
        };
        env.Builtins.RegisterConstant("meta", JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["class"] = JgsValue.Struct(metaClass),
        }));

        // A .NET call sees the folder cd moved to (ADR 0176).
        interpreter.NetTypes.Folder = () => interpreter.Host?.OwnDirectory;

        // The int64 precision warning (ADR 0174) goes through this session's warning state.
        interpreter.NetTypes.Warn = (identifier, message) =>
        {
            if (interpreter.Host is { } host)
            {
                Warn(host, identifier, message);
            }
        };
    }

    /// <summary><c>NET.addAssembly(path)</c>: load and make visible an assembly; answer its <c>NET.Assembly</c>.</summary>
    private static JgsValue AddAssembly(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        // Any other count is no overload of R2025b's method, Unloadable=true included: its
        // name-value pair is two more arguments (probe3).
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                "No method 'NET.addAssembly' with matching signature found.");
        }

        Assembly assembly;

        // A System.Reflection.AssemblyName: loaded by name, and a failure is the loader's exception
        // (probe3), not the short-name refusal below.
        if (args[0].AsExternalOrNull() is NetObject { Target: AssemblyName byName })
        {
            try
            {
                assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(byName);
            }
            catch (Exception fault) when (fault is not JgsException)
            {
                throw NetInvoke.Raise(fault, "AddAssembly", line, col);
            }

            return Added(assembly);
        }

        if (args[0].Type != JgsType.String && !(args[0].IsStringArray && args[0].ArrayLength == 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:AddAssembly:InvalidAssemblyName",
                "Input to NET.addAssembly must be a character vector or an instance of 'System.Reflection.AssemblyName' class.");
        }

        string path = TextOf(args[0]);
        if (path.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:AddAssembly:EmptyAssemblyName", "Assembly name cannot be empty.");
        }

        // An argument that is not a rooted path is an assembly name, whatever it looks like: R2025b
        // reads 'JGraph.Interop.TestAssembly.dll' as a name and fails to find it (net_assembly).
        if (!Path.IsPathRooted(path))
        {
            try
            {
                assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(path));
            }
            catch (Exception fault) when (fault is FileNotFoundException or FileLoadException or ArgumentException)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:NET:AddAssembly:NetCoreShortNameLoadError",
                    $"'{path}' could not be found by .NET Core probing logic.");
            }

            return Added(assembly);
        }

        try
        {
            assembly = interpreter.NetTypes.AddFromPath(path);
        }
        catch (Exception fault) when (fault is not JgsException)
        {
            throw NetInvoke.Raise(fault, "AddAssembly", line, col);
        }

        return Added(assembly);

        JgsValue Added(Assembly loaded)
        {
            interpreter.NetTypes.Add(loaded);
            interpreter.NoteNet();
            return JgsValue.External(interpreter.NetTypes.HandleOf(loaded));
        }
    }

    /// <summary>
    /// <c>enumeration(x)</c>: asked for nothing, prints the members of the enum <paramref name="args"/>
    /// names (by name or by a member), or why there are none; asked for outputs, answers them as
    /// <see cref="NetEnums.Outputs"/> does (net_enums, probe4).
    /// </summary>
    private static JgsValue[] Enumeration(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("enumeration", args, 1, line, col);
        if (MidiTypeEnumeration(interpreter, args[0], wanted) is { } midi)
        {
            return midi;
        }

        Type? type = NetTypeNamed(args[0], interpreter);
        if (type is null && args[0].Type is not (JgsType.String or JgsType.External) && !(args[0].IsStringArray && args[0].ArrayLength == 1))
        {
            throw new JgsRuntimeException(line, col, "enumeration expects a class name or an enumeration member.");
        }

        if (wanted == 0)
        {
            string text = type is null ? NetEnums.NoClass(TextOf(args[0])) : NetEnums.Listing(type);
            interpreter.Host?.print(text[..^1]); // print ends the line itself
            return [];
        }

        if (type is null)
        {
            throw new JgsRuntimeException(line, col, NetEnums.NoClass(TextOf(args[0])).TrimEnd('\n'));
        }

        return NetEnums.Outputs(type, wanted, line, col);
    }

    /// <summary>
    /// <c>NET.createArray(type, dims…)</c>: a .NET array of the element type — named by text
    /// (<c>'System.Double[]'</c> for a jagged one) or by a <c>NET.GenericClass</c> — with the lengths
    /// given one by one or as a vector, filled with defaults. A type that does not resolve is R2025b's
    /// <c>CLRException:TypeError</c> (net_arrays, probe4).
    /// </summary>
    private static JgsValue CreateNetArray(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("NET.createArray", args, 2, int.MaxValue, line, col);
        Type element = NetTypeOf(interpreter, args[0])
            ?? throw NetInvoke.Raise(new TypeLoadException($"Could not resolve type '{TextOf(args[0])}'."), "TypeError", line, col);
        var lengths = new List<int>();
        for (int i = 1; i < args.Count; i++)
        {
            lengths.AddRange(ToDoubles("NET.createArray", args[i], line, col).Select(static n => (int)n));
        }

        interpreter.NoteNet();
        return NetConvert.ToMatlab(Array.CreateInstance(element, lengths.ToArray()), typeof(Array), line, col);
    }

    /// <summary><c>NET.createGeneric(definition, {typeArgs}, ctorArgs…)</c>: construct a closed generic type.</summary>
    private static JgsValue CreateGeneric(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("NET.createGeneric", args, 2, int.MaxValue, line, col);
        if (args[1].Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireClass",
                "Invalid input for argument 2 (paramTypes):\nValue must be 'cell vector'.");
        }

        Type closed = ClosedGeneric(interpreter, TextOf(args[0]), args[1].AsCell, line, col);
        interpreter.NoteNet();
        try
        {
            return NetInvoke.Construct(closed, args.Skip(2).ToArray(), line, col, interpreter.NetTypes);
        }
        catch (JgsRuntimeException refused) when (refused.Identifier == "MATLAB:dispatcher:noMatchingConstructor")
        {
            // R2025b names the definition, arity and all (probe5b: 'System.Func`2').
            throw new JgsRuntimeException(line, col, "MATLAB:NET:GenericObjectCreationError",
                $"No constructor '{closed.GetGenericTypeDefinition().FullName}' with matching signature found.");
        }
    }

    /// <summary>
    /// <c>NET.GenericClass(definition, typeArg…)</c>: a closed generic type as a value, for a type argument
    /// that is itself generic (probe4: a definition that is not text, too few arguments and a type that
    /// does not resolve are refused as R2025b refuses them).
    /// </summary>
    private static JgsValue GenericClass(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || !(args[0].Type == JgsType.String || (args[0].IsStringArray && args[0].ArrayLength == 1)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireString",
                "Invalid input for argument 1 (className):\nValue must be a character vector or a string scalar.");
        }

        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        return JgsValue.External(new NetGenericClass(ClosedGeneric(interpreter, TextOf(args[0]), [.. args.Skip(1)], line, col)));
    }

    /// <summary>
    /// A generic definition closed over type arguments named by text or by <c>NET.GenericClass</c>. A
    /// type argument or a definition of that arity that does not resolve is R2025b's
    /// <c>CLRException:BuildGenericType</c>, a generic argument written in text its
    /// <c>NET:InvalidClassName</c>; a broken constraint, the runtime's exception (net_generics, probe4, probe4c).
    /// </summary>
    private static Type ClosedGeneric(Interpreter interpreter, string name, IReadOnlyList<JgsValue> typeArguments, int line, int col)
    {
        var resolved = new Type[typeArguments.Count];
        for (int i = 0; i < resolved.Length; i++)
        {
            resolved[i] = NetTypeOf(interpreter, typeArguments[i]) ?? throw UnresolvedTypeArgument(typeArguments[i], line, col);
        }

        string arity = $"{name}`{resolved.Length}";
        Type definition = interpreter.NetTypes.TypeNamed(arity)
            ?? throw NetInvoke.Raise(new TypeLoadException($"Could not resolve type '{arity}'."), "BuildGenericType", line, col);
        try
        {
            return definition.MakeGenericType(resolved);
        }
        catch (Exception fault) when (fault is not JgsException)
        {
            throw NetInvoke.Raise(fault, "", line, col);
        }
    }

    /// <summary>A type argument no type answers to: a generic one in text is no class name to R2025b.</summary>
    private static JgsRuntimeException UnresolvedTypeArgument(JgsValue named, int line, int col)
    {
        string text = TextOf(named);
        return text.Contains('<', StringComparison.Ordinal)
            ? new JgsRuntimeException(line, col, "MATLAB:NET:InvalidClassName", $"Could not find class '{text}'.")
            : NetInvoke.Raise(new TypeLoadException($"Could not resolve type '{text}'."), "BuildGenericType", line, col);
    }

    /// <summary>
    /// <c>NET.invokeGenericMethod(objOrType, name, {typeArgs}, args…)</c>: a generic method of an object
    /// or of a type named by text, closed over type arguments named by text — R2025b refuses a
    /// <c>NET.GenericClass</c> there (probe4) — and called.
    /// </summary>
    private static JgsValue[] InvokeGenericMethod(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("NET.invokeGenericMethod", args, 3, int.MaxValue, line, col);
        NetObject? receiver = args[0].AsExternalOrNull() as NetObject;
        Type type = receiver?.Type ?? NetTypeOf(interpreter, args[0])
            ?? throw NetInvoke.Raise(new TypeLoadException($"Could not resolve type '{TextOf(args[0])}'."), "TypeError", line, col);

        // The type arguments are a cell of names: anything else, a NET.GenericClass among them, is
        // R2025b's InvalidGenericParameterType, and a name no type answers to the runtime's (probe4c).
        if (args[2].Type != JgsType.Cell || args[2].AsCell.Any(static a => a.Type == JgsType.External))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:InvalidGenericParameterType", "Invalid generic parameter type argument.");
        }

        var typeArguments = new Type[args[2].AsCell.Length];
        for (int i = 0; i < typeArguments.Length; i++)
        {
            typeArguments[i] = NetTypeOf(interpreter, args[2].AsCell[i])
                ?? throw NetInvoke.Raise(new TypeLoadException($"Could not resolve type '{TextOf(args[2].AsCell[i])}'."), "InvokeGenericMethod", line, col);
        }

        interpreter.NoteNet();
        return NetInvoke.CallGeneric(type, TextOf(args[1]), receiver, typeArguments, [.. args.Skip(3)], wanted, line, col, interpreter.NetTypes);
    }

    /// <summary>
    /// <c>NET.setStaticProperty('Type.Name', value)</c>: writes a static property or field, the one road
    /// to a static write (stage 2, ADR 0175; <c>Type.Name = v</c> assigns a struct, as in R2025b).
    /// </summary>
    private static JgsValue SetStaticProperty(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("NET.setStaticProperty", args, 2, line, col);
        string dotted = TextOf(args[0]);
        int dot = dotted.LastIndexOf('.');
        if (dot <= 0 || !interpreter.TryNetName(dotted[..dot], interpreter.CurrentFrame, out Type? type, out string? member) || member is not null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:InvalidClassName", $"Could not find class '{(dot <= 0 ? dotted : dotted[..dot])}'.");
        }

        NetInvoke.SetStatic(type!, dotted[(dot + 1)..], args[1], line, col);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>NET.explicitCast(obj, 'Interface')</c>: the object seen through one of its interfaces, whose
    /// explicit implementations it then reaches (class <c>NET.view.Interface</c>, measured in
    /// net_members). Only an interface: R2025b refuses a cast to a class, to a type the object does not
    /// implement, and of a value that is not a .NET object, each in its own words (probe2).
    /// </summary>
    private static JgsValue ExplicitCast(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("NET.explicitCast", args, 2, line, col);
        if (args[0].AsExternalOrNull() is not NetObject { Target: { } held })
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:interfaceView:RequireNetObject", "First argument must be a valid .NET object.");
        }

        string name = TextOf(args[1]);
        Type? target = interpreter.NetTypes.TypeNamed(name);
        if (target is not null && !target.IsInterface)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:interfaceView:UnsupportedClassToClass",
                "Conversion of objects to objects of another class is not allowed.");
        }

        if (target is null || !target.IsInstanceOfType(held))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:interfaceView:InvalidCast",
                $"Unable to convert the object of '{NetNames.ClassName(held.GetType())}' type to object of '{name}' type.\n"
                + "Please make sure that the input object implements an interface type supplied for conversion.");
        }

        return JgsValue.External(new NetObject(held, target, isView: true));
    }

    /// <summary>
    /// <c>NET.convertArray(A, type, dims)</c>: a MATLAB array as a .NET array — the element type named,
    /// or the one the array's class maps to; a vector as one dimension and a matrix as two, unless
    /// the dimensions are named. Stage 2 takes the forms net_members passes; stage 4 completes it.
    /// </summary>
    private static JgsValue ConvertArray(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("NET.convertArray", args, 1, 3, line, col);
        JgsValue value = args[0];
        if (value.Type == JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:UnsupportedInputArrayTypeError", "Conversion from 'cell' array type is not supported.");
        }

        Type element = args.Count > 1
            ? NetTypeOf(interpreter, args[1])
                ?? throw new JgsRuntimeException(line, col, "MATLAB:NET:UnsupportedOutputArrayTypeError",
                    $"Conversion to '{TextOf(args[1])}' array type is not supported.")
            : NetConvert.ElementTypeOf(value)
                ?? throw new JgsRuntimeException(line, col, $"NET.convertArray: a value of class '{ClassOf(value, JgsDialect.Matlab)}' has no .NET element type.");
        int rank = args.Count > 2
            ? Math.Max(1, args[2].Type == JgsType.Array ? args[2].ArrayLength : 1)
            : value.Type == JgsType.Array && value.Rows != 1 && value.Cols != 1 ? 2 : 1;
        interpreter.NoteNet();
        return NetConvert.ToMatlab(NetConvert.ConvertArray(value, element, rank, line, col), typeof(Array), line, col);
    }

    /// <summary>
    /// <c>NET.disableAutoRelease(obj)</c> and <c>NET.enableAutoRelease(obj)</c>: lock and unlock a COM
    /// object's runtime-callable wrapper. JGraph holds every .NET object it hands out until the last
    /// holder goes, so for a COM object there is nothing to change; any other object is R2025b's
    /// <c>MATLAB:badargs</c>, and a value that is no handle its <c>RequireClass</c> (probe3).
    /// </summary>
    private static JgsValue AutoRelease(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"No method '{name}' with matching signature found.");
        }

        if (args[0].AsExternalOrNull() is NetObject { Target: { } held } && Marshal.IsComObject(held))
        {
            return JgsValue.Null;
        }

        if (args[0].Type is JgsType.External or JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:badargs", "Mismatched arguments.");
        }

        throw new JgsRuntimeException(line, col, "MATLAB:class:RequireClass",
            "Invalid input for argument 1 (rhs1):\nValue must be 'handle scalar'.");
    }

    /// <summary>
    /// A type named for an array element or a generic argument: a <c>NET.GenericClass</c>'s, or one
    /// named by text, an array of one written with its brackets (<c>'System.Double[]'</c>,
    /// <c>'System.Int32[,]'</c>). Null when the text names no type.
    /// </summary>
    private static Type? NetTypeOf(Interpreter interpreter, JgsValue value)
    {
        if (value.AsExternalOrNull() is NetGenericClass generic)
        {
            return generic.Type;
        }

        if (!(value.Type == JgsType.String || (value.IsStringArray && value.ArrayLength == 1)))
        {
            return null;
        }

        string name = TextOf(value);
        if (name.EndsWith(']') && name.LastIndexOf('[') is > 0 and int open && name[(open + 1)..^1].All(static c => c == ','))
        {
            int rank = name.Length - open - 1;
            Type? element = NetTypeOf(interpreter, JgsValue.Str(name[..open]));
            return element is null ? null : rank == 1 ? element.MakeArrayType() : element.MakeArrayType(rank);
        }

        return interpreter.NetTypes.TypeNamed(name);
    }

    /// <summary>
    /// <c>dotnetenv</c>: the runtime JGraph runs on, as a <c>NETEnvironment</c>-shaped value;
    /// <c>dotnetenv("core", Version=v)</c> accepts the running runtime and refuses any other.
    /// </summary>
    /// <remarks>
    /// The status is R2025b's: <c>notloaded</c> until the session first reaches .NET (a .NET name, an
    /// added assembly, <c>NET.isNETSupported</c>), <c>loaded</c> after, and the version and location are
    /// empty until then (net_assembly, probe3). A request for another runtime is R2025b's
    /// <c>MATLAB:netenv:NETLoaded</c> once loaded; before, R2025b would switch and JGraph cannot, so it
    /// says why under its own identifier (ADR 0176).
    /// </remarks>
    private static JgsValue DotNetEnv(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            string runtime = TextOf(args[0]);
            if (runtime is not ("core" or "framework"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:netenv:InvalidRuntime",
                    $"'{runtime}' is not a valid .NET runtime. Specify \"core\" or \"framework\".");
            }

            string? version = null;
            for (int i = 1; i + 1 < args.Count; i += 2)
            {
                if (string.Equals(TextOf(args[i]), "Version", StringComparison.OrdinalIgnoreCase))
                {
                    version = TextOf(args[i + 1]);
                }
            }

            Version running = Environment.Version;
            bool same = runtime == "core" && (version is null
                || version == running.Major.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || version == $"{running.Major}.{running.Minor}"
                || version == running.ToString());
            if (!same && interpreter.AnyNet)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:netenv:NETLoaded",
                    ".NET is loaded. To change the environment, restart MATLAB then call dotnetenv.");
            }

            if (!same)
            {
                throw new JgsRuntimeException(line, col, "JGraph:netenv:UnsupportedRuntime",
                    $"JGraph runs on .NET {running} and cannot load another runtime.");
            }

            return JgsValue.Null;
        }

        bool loaded = interpreter.AnyNet;
        JgsValue env = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Version"] = JgsValue.StringScalar(loaded ? Environment.Version.ToString() : ""),
            ["RuntimeLocation"] = JgsValue.StringScalar(loaded ? RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\', '/') : ""),
            ["Runtime"] = JgsValue.StringScalar("core"),
            ["Status"] = JgsValue.StringScalar(loaded ? "loaded" : "notloaded"),
        });
        env.SetClassName("NETEnvironment");
        return env;
    }
}
