using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>What <c>jgraph.net.compile</c> was asked to build (interop plan, stage 6, ADR 0179).</summary>
/// <param name="Sources">Each source's name in diagnostics (a file's full path, or <c>&lt;string&gt;</c>) and its text.</param>
/// <param name="AssemblyName">The assembly's name; recompiling under it replaces the earlier build.</param>
/// <param name="References">Resolved references beyond the framework and the session's added assemblies.</param>
/// <param name="Unloadable">Whether the build loads into a collectible context, unloaded when it is replaced.</param>
/// <param name="AllowUnsafe">Whether <c>unsafe</c> code compiles.</param>
/// <param name="Language">The C# language version.</param>
/// <param name="Optimize">Whether the build is optimized (a Release build).</param>
internal sealed record NetCompileRequest(
    IReadOnlyList<(string Path, string Text)> Sources,
    string AssemblyName,
    IReadOnlyList<NetCompileReference> References,
    bool Unloadable,
    bool AllowUnsafe,
    LanguageVersion Language,
    bool Optimize);

/// <summary>
/// A reference a compile names: a file on disk (loaded at run time from the shared load context), or
/// an assembly compiled earlier in the session (its image, and at run time the session's copy).
/// </summary>
internal sealed record NetCompileReference(string Name, string? Path, NetCompiledAssembly? Compiled);

/// <summary>One assembly a session compiled: the image it loaded, and the context holding it.</summary>
internal sealed class NetCompiledAssembly(string name, string hash, byte[] image, Assembly assembly, AssemblyLoadContext context)
{
    public string Name { get; } = name;

    /// <summary>The hash of everything the build depended on; the same hash is the same build.</summary>
    public string Hash { get; } = hash;

    public byte[] Image { get; } = image;

    public Assembly Assembly { get; } = assembly;

    public AssemblyLoadContext Context { get; } = context;
}

/// <summary>A compile that failed: every error, laid out as <c>file(line,col): error CSxxxx: …</c>.</summary>
internal sealed class NetCompileFailedException(IReadOnlyList<string> errors)
    : Exception($"C# compilation failed with {errors.Count} error{(errors.Count == 1 ? "" : "s")}:\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// The inline C# helper behind <c>jgraph.net.compile</c> (interop plan, stage 6, ADR 0179): C# source
/// compiled with Roslyn into an in-memory assembly, loaded into a load context of its own and made
/// visible to the session exactly as <c>NET.addAssembly</c> makes a file visible.
/// </summary>
/// <remarks>
/// <para>
/// <b>Images are cached per process, assemblies per session.</b> A compile costs from a few hundred
/// milliseconds (warm) to seconds (Roslyn's first), so the emitted image is kept by the hash of its
/// sources, references and options, and a second session asking for the same build only loads it. Each
/// session loads its own copy, so one session's recompilation never reaches into another's objects.
/// </para>
/// <para>
/// <b>Recompiling replaces.</b> A build under an assembly name the session already compiled, or one that
/// defines a public type an earlier build defined, retires the earlier assembly: its types leave the
/// session's catalog and the process-wide member caches, its collectible context is unloaded, and an
/// object of it the script still holds refuses every use with <c>JGraph:NET:AssemblyRecompiled</c>.
/// </para>
/// </remarks>
internal static class NetCompiler
{
    private static readonly ConcurrentDictionary<string, Image> Images = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, MetadataReference> FileReferences = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyDictionary<string, MetadataReference>> Framework = new(BuildFramework);
    private static readonly ConditionalWeakTable<Assembly, string> Retired = new();
    private static volatile bool _anyRetired;

    /// <summary>An emitted build: its image and symbols, and what its compile warned.</summary>
    private sealed record Image(byte[] Pe, byte[] Pdb, string[] Warnings);

    /// <summary>
    /// Whether <paramref name="type"/> — or a type argument or element type in it — comes from an
    /// assembly a recompilation retired; <paramref name="assemblyName"/> names that assembly.
    /// </summary>
    public static bool IsRetired(Type type, out string assemblyName)
    {
        assemblyName = "";
        if (!_anyRetired)
        {
            return false;
        }

        if (Retired.TryGetValue(type.Assembly, out string? name))
        {
            assemblyName = name;
            return true;
        }

        if (type.HasElementType && IsRetired(type.GetElementType()!, out assemblyName))
        {
            return true;
        }

        if (type.IsConstructedGenericType)
        {
            foreach (Type argument in type.GenericTypeArguments)
            {
                if (IsRetired(argument, out assemblyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses an argument that is an object of a replaced build before overloads are matched, where
    /// its old type would otherwise read as no overload at all.
    /// </summary>
    public static void RefuseRetired(IReadOnlyList<JgsValue> arguments, int line, int col)
    {
        if (!_anyRetired)
        {
            return;
        }

        foreach (JgsValue argument in arguments)
        {
            (argument.AsExternalOrNull() as NetObject)?.Live(line, col);
        }
    }

    /// <summary>Whether a framework assembly of this name can be referenced without a path.</summary>
    public static bool IsFrameworkName(string name) => Framework.Value.ContainsKey(name);

    /// <summary>
    /// Compiles (or finds the cached build of) <paramref name="request"/> for the session that owns
    /// <paramref name="catalog"/>, loads it, retires what it replaces and makes its types visible.
    /// Answers the session's compiled assembly, the compiler's warnings, and whether it is newly loaded
    /// (false when the session had this very build already: nothing to warn again).
    /// </summary>
    public static (NetCompiledAssembly Compiled, IReadOnlyList<string> Warnings, bool Fresh) Compile(NetCatalog catalog, NetCompileRequest request)
    {
        string hash = HashOf(request, catalog);
        if (catalog.Compiled.TryGetValue(request.AssemblyName, out NetCompiledAssembly? same) && same.Hash == hash)
        {
            return (same, [], false);
        }

        Image image = Images.TryGetValue(hash, out Image? cached) ? cached : Images.GetOrAdd(hash, Emit(request, catalog));

        var context = new CompiledContext(request.AssemblyName, request.Unloadable, catalog, request.References);
        Assembly assembly;
        using (var pe = new MemoryStream(image.Pe, writable: false))
        using (var pdb = new MemoryStream(image.Pdb, writable: false))
        {
            assembly = context.LoadFromStream(pe, pdb);
        }

        var compiled = new NetCompiledAssembly(request.AssemblyName, hash, image.Pe, assembly, context);

        // What this build replaces: the same name, or a public type of the same full name (an edited
        // source compiled under its default name). A build this one references stays.
        HashSet<string> defines = [.. PublicNames(assembly)];
        foreach (NetCompiledAssembly earlier in catalog.Compiled.Values.ToArray())
        {
            bool referenced = request.References.Any(r => r.Compiled == earlier);
            if (string.Equals(earlier.Name, request.AssemblyName, StringComparison.OrdinalIgnoreCase)
                || (!referenced && PublicNames(earlier.Assembly).Any(defines.Contains)))
            {
                Retire(catalog, earlier);
            }
        }

        catalog.Compiled[request.AssemblyName] = compiled;
        catalog.Add(assembly, overrides: true);
        return (compiled, image.Warnings, true);
    }

    /// <summary>Ends an earlier build: out of the catalog and the caches, its objects refused, its context unloaded.</summary>
    private static void Retire(NetCatalog catalog, NetCompiledAssembly earlier)
    {
        Retired.AddOrUpdate(earlier.Assembly, earlier.Name);
        _anyRetired = true;
        catalog.Compiled.Remove(earlier.Name);
        catalog.Remove(earlier.Assembly);

        bool Gone(Type type) => IsRetired(type, out _);
        NetSignature.Forget(Gone);
        NetDelegates.Forget(Gone);
        NetEventSubscription.Forget(Gone);

        if (earlier.Context.IsCollectible)
        {
            earlier.Context.Unload();
        }
    }

    /// <summary>The full names of an assembly's public top-level types.</summary>
    private static IEnumerable<string> PublicNames(Assembly assembly) =>
        assembly.GetExportedTypes().Where(static t => !t.IsNested && t.FullName is not null).Select(static t => t.FullName!);

    /// <summary>
    /// The hash of everything a build depends on: the sources and their names, the options, the
    /// session's added assemblies (every one is referenced) and each named reference.
    /// </summary>
    private static string HashOf(NetCompileRequest request, NetCatalog catalog)
    {
        var text = new StringBuilder();
        text.Append(request.AssemblyName).Append('\0')
            .Append(request.Unloadable).Append(request.AllowUnsafe).Append(request.Optimize).Append(request.Language).Append('\0');
        foreach ((string path, string source) in request.Sources)
        {
            text.Append(path).Append('\0').Append(source).Append('\0');
        }

        foreach (NetCompileReference reference in request.References)
        {
            text.Append(reference.Name).Append('\0').Append(reference.Compiled?.Hash ?? Stamp(reference.Path)).Append('\0');
        }

        foreach (Assembly added in SessionReferences(catalog))
        {
            text.Append(added.Location).Append('\0').Append(Stamp(added.Location)).Append('\0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

        static string Stamp(string? path) =>
            path is { Length: > 0 } && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + ":" + new FileInfo(path).Length : "";
    }

    /// <summary>The session's added assemblies a build references: those with a file, never a compiled one.</summary>
    private static IEnumerable<Assembly> SessionReferences(NetCatalog catalog) =>
        catalog.Added.Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && !Framework.Value.ContainsKey(a.GetName().Name ?? ""));

    /// <summary>Runs Roslyn over the request; a build with errors throws them all.</summary>
    private static Image Emit(NetCompileRequest request, NetCatalog catalog)
    {
        var parse = new CSharpParseOptions(request.Language);
        SyntaxTree[] trees = request.Sources
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, parse, s.Path, Encoding.UTF8))
            .ToArray();

        var references = new List<MetadataReference>(Framework.Value.Values);
        foreach (Assembly added in SessionReferences(catalog))
        {
            references.Add(FileReference(added.Location));
        }

        foreach (NetCompileReference reference in request.References)
        {
            if (reference.Compiled is { } compiled)
            {
                references.Add(MetadataReference.CreateFromImage(compiled.Image));
            }
            else if (reference.Path is { } path && !Framework.Value.ContainsKey(reference.Name))
            {
                references.Add(FileReference(path));
            }
        }

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: request.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
            allowUnsafe: request.AllowUnsafe,
            deterministic: true);
        CSharpCompilation compilation = CSharpCompilation.Create(request.AssemblyName, trees, references, options);

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        EmitResult result = compilation.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        string[] errors = result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        if (!result.Success)
        {
            throw new NetCompileFailedException(errors.Length > 0 ? errors : ["(no diagnostics)"]);
        }

        string[] warnings = result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Warning)
            .Select(static d => d.ToString())
            .ToArray();
        return new Image(pe.ToArray(), pdb.ToArray(), warnings);
    }

    private static MetadataReference FileReference(string path) =>
        FileReferences.GetOrAdd(path + "|" + File.GetLastWriteTimeUtc(path).Ticks, _ => MetadataReference.CreateFromFile(path));

    /// <summary>
    /// The framework JGraph runs on, by simple name: every managed assembly of the runtime folder, and
    /// every framework assembly already loaded (the desktop framework's, in the app).
    /// </summary>
    private static IReadOnlyDictionary<string, MetadataReference> BuildFramework()
    {
        var found = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        string shared = Path.GetDirectoryName(Path.GetDirectoryName(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\', '/'))!)!;
        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (!loaded.IsDynamic && loaded.Location is { Length: > 0 } location
                && location.StartsWith(shared, StringComparison.OrdinalIgnoreCase) && loaded.GetName().Name is { } name)
            {
                found.TryAdd(name, MetadataReference.CreateFromFile(location));
            }
        }

        foreach (string file in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (found.ContainsKey(name) || !HasMetadata(file))
            {
                continue;
            }

            found[name] = MetadataReference.CreateFromFile(file);
        }

        return found;

        static bool HasMetadata(string file)
        {
            try
            {
                using FileStream stream = File.OpenRead(file);
                using var image = new System.Reflection.PortableExecutable.PEReader(stream);
                return image.HasMetadata;
            }
            catch (Exception fault) when (fault is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A compiled assembly's load context: collectible unless asked otherwise, and resolving what the
    /// build references — the session's added assemblies, the files named, the builds named — before
    /// falling back to the default context (the framework).
    /// </summary>
    private sealed class CompiledContext(string name, bool collectible, NetCatalog catalog, IReadOnlyList<NetCompileReference> references)
        : AssemblyLoadContext("JGraph.NET.compile:" + name, collectible)
    {
        protected override Assembly? Load(AssemblyName wanted)
        {
            foreach (NetCompileReference reference in references)
            {
                if (string.Equals(reference.Name, wanted.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (reference.Compiled is { } compiled)
                    {
                        return compiled.Assembly;
                    }

                    if (reference.Path is { } path && !Framework.Value.ContainsKey(reference.Name))
                    {
                        return NetCatalog.LoadShared(path);
                    }
                }
            }

            foreach (Assembly added in catalog.Added)
            {
                if (string.Equals(added.GetName().Name, wanted.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return added;
                }
            }

            return null;
        }
    }
}
