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
                return JgsValue.True;
            }, bare: true),
            ["addAssembly"] = Builtin("NET.addAssembly", (args, line, col) => AddAssembly(interpreter, args, line, col)),
            ["createArray"] = Builtin("NET.createArray", (args, line, col) => CreateNetArray(interpreter, args, line, col)),
            ["createGeneric"] = Builtin("NET.createGeneric", (args, line, col) => CreateGeneric(interpreter, args, line, col)),
        };
        env.Builtins.RegisterConstant("NET", JgsValue.Struct(net));

        env.Builtins.Register("dotnetenv", Builtin("dotnetenv", DotNetEnv, bare: true));

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
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function 'NET.addAssembly' for {args.Count} input arguments.");
        }

        if (args[0].Type != JgsType.String && !(args[0].IsStringArray && args[0].ArrayLength == 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:AddAssembly:InvalidAssemblyName",
                "Input to NET.addAssembly must be a character vector or an instance of 'System.Reflection.AssemblyName' class.");
        }

        string path = TextOf(args[0]);
        Assembly assembly;

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

            interpreter.NetTypes.Add(assembly);
            interpreter.NoteNet();
            return JgsValue.External(new NetAssemblyValue(assembly));
        }

        try
        {
            assembly = interpreter.NetTypes.AddFromPath(path);
        }
        catch (Exception fault) when (fault is not JgsException)
        {
            throw NetInvoke.Raise(fault, "AddAssembly", line, col);
        }

        interpreter.NoteNet();
        return JgsValue.External(new NetAssemblyValue(assembly));
    }

    /// <summary><c>NET.createArray(type, dims…)</c>: a .NET array of the named element type, filled with defaults.</summary>
    private static JgsValue CreateNetArray(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("NET.createArray", args, 2, int.MaxValue, line, col);
        Type element = NetTypeArgument(interpreter, args[0], line, col);
        var lengths = new int[args.Count - 1];
        for (int i = 1; i < args.Count; i++)
        {
            lengths[i - 1] = (int)args[i].AsNumber;
        }

        interpreter.NoteNet();
        return NetConvert.ToMatlab(Array.CreateInstance(element, lengths), typeof(Array), line, col);
    }

    /// <summary><c>NET.createGeneric(definition, {typeArgs}, ctorArgs…)</c>: construct a closed generic type.</summary>
    private static JgsValue CreateGeneric(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("NET.createGeneric", args, 2, int.MaxValue, line, col);
        string name = TextOf(args[0]);
        JgsValue[] typeArguments = args[1].Type == JgsType.Cell ? args[1].AsCell : [args[1]];
        Type definition = interpreter.NetTypes.TypeNamed($"{name}`{typeArguments.Length}")
            ?? throw new JgsRuntimeException(line, col,
                $"The generic type '{name}' with {typeArguments.Length} type arguments was not found.");
        Type closed;
        try
        {
            closed = definition.MakeGenericType([.. typeArguments.Select(a => NetTypeArgument(interpreter, a, line, col))]);
        }
        catch (Exception fault) when (fault is not JgsException)
        {
            throw NetInvoke.Raise(fault, "", line, col);
        }

        interpreter.NoteNet();
        return NetInvoke.Construct(closed, args.Skip(2).ToArray(), line, col, interpreter.NetTypes);
    }

    /// <summary>A type named by text (<c>'System.Double'</c>) for an array or a generic argument.</summary>
    private static Type NetTypeArgument(Interpreter interpreter, JgsValue value, int line, int col)
    {
        string name = TextOf(value);
        return interpreter.NetTypes.TypeNamed(name)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:undefinedVarOrClass", $"Unable to resolve the name '{name}'.");
    }

    /// <summary>
    /// <c>dotnetenv</c>: the runtime JGraph runs on, as a <c>NETEnvironment</c>-shaped value;
    /// <c>dotnetenv("core", Version=v)</c> accepts the running runtime and refuses any other.
    /// </summary>
    private static JgsValue DotNetEnv(IReadOnlyList<JgsValue> args, int line, int col)
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
            if (!same)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:netenv:NETLoaded",
                    ".NET is loaded. To change the environment, restart MATLAB then call dotnetenv.");
            }

            return JgsValue.Null;
        }

        JgsValue env = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Version"] = JgsValue.StringScalar(Environment.Version.ToString()),
            ["RuntimeLocation"] = JgsValue.StringScalar(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\', '/')),
            ["Runtime"] = JgsValue.StringScalar("core"),
            ["Status"] = JgsValue.StringScalar("loaded"),
        });
        env.SetClassName("NETEnvironment");
        return env;
    }
}
