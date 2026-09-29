using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using JGraph.Scripting.Jgs.Net;
using Microsoft.CodeAnalysis.CSharp;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>jgraph.net.compile</c>, the inline C# helper (interop plan, stage 6, ADR 0179): a JGraph
/// extension with no MATLAB counterpart. It compiles C# — files, text, or both — into an assembly and
/// makes its types visible as <c>NET.addAssembly</c> would, answering the <c>NET.Assembly</c>.
/// </summary>
/// <remarks>
/// <c>jgraph</c> is a JGraph-owned root, registered as <c>NET</c> is, and deliberately not a name in
/// <c>NET.*</c>, which a later MATLAB release could claim. The JGS dialect has no dotted names and
/// reaches it as <c>feval("jgraph.net.compile", …)</c>.
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string CompileName = "jgraph.net.compile";

    private static readonly string[] CompileOptions = ["AssemblyName", "References", "Unloadable", "AllowUnsafe", "LanguageVersion", "Optimize"];

    /// <summary>Declares the <c>jgraph</c> root and its <c>net.compile</c>.</summary>
    private static void RegisterNetCompile(JgsEnvironment env, Interpreter interpreter)
    {
        var net = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["compile"] = JgsValue.Function(new BuiltinFunction(CompileName, (args, line, col) => CompileCSharp(interpreter, args, line, col))
            {
                KeepsStringArguments = true,
            }),
        };
        env.Builtins.RegisterConstant("jgraph", JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["net"] = JgsValue.Struct(net),

            // Test-only and undocumented (ADR 0180): the native host's door for the JGraph-only fixtures.
            ["internal"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["nativehost"] = NativeHostFunction(interpreter),

                // Test-only (device classes plan): a simulated serial port for the device fixtures.
                ["devicesim"] = DeviceSimFunction(interpreter),

                // Test-only (device classes plan, stage D3): the simulated Bluetooth radio and peer.
                ["btsim"] = BluetoothSimFunction(interpreter),
            }),
        }));
    }

    /// <summary>
    /// <c>jgraph.net.compile(source, Name=Value, …)</c>: <paramref name="args"/>' first is a <c>.cs</c>
    /// file, C# text, or a string array of either; the rest are the options.
    /// </summary>
    private static JgsValue CompileCSharp(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args.Count % 2 == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:NET:compile:Arguments",
                $"{CompileName} takes the C# to compile (a .cs file, C# text, or a string array of them) and then name-value options.");
        }

        string[]? items = TextElementsOf(args[0]);
        if (items is null || items.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:NET:compile:Arguments",
                $"The first input of {CompileName} must be text: a .cs file, C# source, or a string array of them.");
        }

        // The sources: a one-line item ending in .cs is a file, found as a script file is found;
        // anything else is C# text, named <string> (or <string N>) in diagnostics.
        var sources = new List<(string Path, string Text)>();
        string? firstFile = null;
        int strings = items.Count(static i => !IsCSharpFileName(i));
        int stringIndex = 0;
        foreach (string item in items)
        {
            if (IsCSharpFileName(item))
            {
                string path = FindCSharpFile(interpreter, item.Trim())
                    ?? throw new JgsRuntimeException(line, col, "JGraph:NET:compile:FileNotFound", $"C# file '{item.Trim()}' not found.");
                sources.Add((path, File.ReadAllText(path)));
                firstFile ??= path;
            }
            else
            {
                stringIndex++;
                sources.Add((strings == 1 ? "<string>" : $"<string {stringIndex}>", item));
            }
        }

        string? assemblyName = null;
        var references = new List<NetCompileReference>();
        bool unloadable = true, allowUnsafe = false, optimize = true;
        LanguageVersion language = LanguageVersion.Latest;
        for (int i = 1; i < args.Count; i += 2)
        {
            string option = IsTextScalar(args[i]) ? TextOf(args[i]) : "";
            string? known = CompileOptions.FirstOrDefault(o => string.Equals(o, option, StringComparison.OrdinalIgnoreCase));
            JgsValue value = args[i + 1];
            switch (known)
            {
                case "AssemblyName":
                    assemblyName = IsTextScalar(value) ? TextOf(value) : "";
                    if (!IsAssemblyName(assemblyName))
                    {
                        throw BadOption("AssemblyName", "a name of letters, digits, '_' and '.', starting with a letter or '_'");
                    }

                    break;
                case "References":
                    foreach (string reference in TextElementsOf(value) ?? throw BadOption("References", "text or a string array"))
                    {
                        references.Add(ResolveReference(interpreter, reference.Trim(), line, col));
                    }

                    break;
                case "Unloadable":
                    unloadable = Flag("Unloadable", value);
                    break;
                case "AllowUnsafe":
                    allowUnsafe = Flag("AllowUnsafe", value);
                    break;
                case "Optimize":
                    optimize = Flag("Optimize", value);
                    break;
                case "LanguageVersion":
                    if (!IsTextScalar(value) || !LanguageVersionFacts.TryParse(TextOf(value), out language))
                    {
                        throw BadOption("LanguageVersion", "a C# version such as \"latest\", \"default\" or \"12\"");
                    }

                    break;
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:NET:compile:UnknownOption",
                        $"'{option}' is not an option of {CompileName}. The options are {string.Join(", ", CompileOptions)}.");
            }
        }

        // A file names its build; text gets a name from its hash, so the same text is the same build
        // and an edited one replaces it through the types they share.
        assemblyName ??= firstFile is not null && IsAssemblyName(Path.GetFileNameWithoutExtension(firstFile))
            ? Path.GetFileNameWithoutExtension(firstFile)
            : "Inline_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", items))))[..16];

        var request = new NetCompileRequest(sources, assemblyName, references, unloadable, allowUnsafe, language, optimize);
        (NetCompiledAssembly compiled, IReadOnlyList<string> warnings, bool fresh) result;
        try
        {
            result = NetCompiler.Compile(interpreter.NetTypes, request);
        }
        catch (NetCompileFailedException failed)
        {
            throw new JgsRuntimeException(line, col, "JGraph:NET:CompileFailed", failed.Message);
        }

        interpreter.NoteNet();
        if (result.fresh)
        {
            foreach (string warning in result.warnings)
            {
                interpreter.NetTypes.Warn?.Invoke("JGraph:NET:CompileWarning", warning);
            }

            // A public type outside any namespace compiles, but no dotted name reaches it.
            foreach (Type orphan in result.compiled.Assembly.GetExportedTypes().Where(static t => !t.IsNested && t.Namespace is null))
            {
                interpreter.NetTypes.Warn?.Invoke("JGraph:NET:CompileNoNamespace",
                    $"Type '{orphan.Name}' is in no namespace, so a script cannot name it. Put it in a namespace.");
            }
        }

        return JgsValue.External(interpreter.NetTypes.HandleOf(result.compiled.Assembly));

        JgsRuntimeException BadOption(string name, string wanted) =>
            new(line, col, "JGraph:NET:compile:InvalidOption", $"The value of '{name}' must be {wanted}.");

        bool Flag(string name, JgsValue value) =>
            value.Type is JgsType.Bool or JgsType.Number
                ? value.IsTruthy
                : throw BadOption(name, "true or false");
    }

    /// <summary>Whether an item is a file name rather than C# text: one line ending in <c>.cs</c>.</summary>
    private static bool IsCSharpFileName(string item) =>
        item.IndexOfAny(['\n', '\r', ';', '{']) < 0 && item.Trim().EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssemblyName(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(static c => char.IsLetterOrDigit(c) || c is '_' or '.') && !name.EndsWith('.');

    /// <summary>A file as a script's file is found: the folder <c>cd</c> moved to, the script's, then the path.</summary>
    private static string? FindCSharpFile(Interpreter interpreter, string name)
    {
        string resolved = interpreter.Host is { } host ? host.Resolve(name) : Path.GetFullPath(name);
        if (File.Exists(resolved))
        {
            return Path.GetFullPath(resolved);
        }

        if (!Path.IsPathRooted(name) && interpreter.FunctionPath is { } functionPath)
        {
            foreach (string folder in functionPath.Folders)
            {
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// One <c>References</c> entry: an assembly this session compiled, a framework assembly (already
    /// referenced), a <c>.dll</c> file, or an assembly the process has loaded.
    /// </summary>
    private static NetCompileReference ResolveReference(Interpreter interpreter, string reference, int line, int col)
    {
        if (interpreter.NetTypes.Compiled.TryGetValue(reference, out NetCompiledAssembly? compiled))
        {
            return new NetCompileReference(compiled.Name, null, compiled);
        }

        string bare = reference.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? reference[..^4] : reference;
        bool looksLikeFile = reference.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || reference.Contains('\\', StringComparison.Ordinal) || reference.Contains('/', StringComparison.Ordinal);
        if (!looksLikeFile && NetCompiler.IsFrameworkName(bare))
        {
            return new NetCompileReference(bare, null, null);
        }

        if (looksLikeFile)
        {
            string resolved = interpreter.Host is { } host ? host.Resolve(reference) : Path.GetFullPath(reference);
            if (File.Exists(resolved))
            {
                string full = Path.GetFullPath(resolved);
                return new NetCompileReference(Path.GetFileNameWithoutExtension(full), full, null);
            }
        }
        else if (AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                     !a.IsDynamic && a.Location.Length > 0 && string.Equals(a.GetName().Name, bare, StringComparison.OrdinalIgnoreCase)) is { } loaded)
        {
            return new NetCompileReference(loaded.GetName().Name!, loaded.Location, null);
        }

        throw new JgsRuntimeException(line, col, "JGraph:NET:compile:ReferenceNotFound",
            $"Reference '{reference}' is not a framework assembly, a .dll file, or an assembly compiled in this session.");
    }
}
