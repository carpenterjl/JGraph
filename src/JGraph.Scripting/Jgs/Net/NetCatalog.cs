using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// The .NET types a MATLAB-dialect script can name (interop plan, stage 1): the base class library
/// of the runtime JGraph runs on, and every assembly the session added with <c>NET.addAssembly</c>.
/// A dotted name whose head nothing else claims is looked up here, namespace by namespace, until a
/// type answers.
/// </summary>
/// <remarks>
/// <para>
/// The base class library is one process-wide index, built on first use: every managed assembly of
/// the shared framework folder (<c>Microsoft.NETCore.App</c>) and every assembly the process has
/// already loaded, so a WPF host sees <c>System.Windows.*</c> and the CLI does not. MATLAB says the
/// same of <c>mscorlib</c> and <c>system</c>: they need no <c>NET.addAssembly</c>.
/// </para>
/// <para>
/// An added assembly is visible to the session that added it, because assemblies cannot be unloaded
/// and a later session (a test, a fresh run in the app) must not see a type it never asked for. The
/// assembly itself is loaded once per path per process, into one non-collectible load context.
/// </para>
/// </remarks>
internal sealed class NetCatalog
{
    private static readonly object Gate = new();
    private static Index? _framework;
    private static readonly Dictionary<string, Assembly> LoadedByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AssemblyLoadContext Context = new("JGraph.NET", isCollectible: false);

    private readonly List<Assembly> _added = [];
    private readonly HashSet<Assembly> _overriding = [];
    private readonly Dictionary<Assembly, NetAssemblyValue> _handles = [];
    private Index? _session;

    /// <summary>
    /// The assemblies this session compiled with <c>jgraph.net.compile</c>, by assembly name (ADR 0179);
    /// a build under a name here replaces the one it names.
    /// </summary>
    public Dictionary<string, NetCompiledAssembly> Compiled { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The one <c>NET.Assembly</c> this session answers for <paramref name="assembly"/>: adding an
    /// assembly again, by path or by name, answers the handle the first add made (R2025b:
    /// <c>NET.addAssembly(p) == a</c> is true; net_assembly, ADR 0176).
    /// </summary>
    public NetAssemblyValue HandleOf(Assembly assembly)
    {
        if (!_handles.TryGetValue(assembly, out NetAssemblyValue? handle))
        {
            handle = new NetAssemblyValue(assembly);
            _handles[assembly] = handle;
        }

        return handle;
    }

    /// <summary>The folder the process was last moved to by any session, so an unmoved call costs a compare.</summary>
    private static string? _processFolder;

    /// <summary>
    /// The session's own working folder — where <c>cd</c> went, the script's folder, the configured
    /// one — or null when it has none and <c>pwd</c> is the process's folder already.
    /// </summary>
    public Func<string?>? Folder { get; set; }

    /// <summary>
    /// Moves the process's working folder to the session's before a .NET member runs: R2025b's
    /// <c>cd</c> is the process's, so <c>System.Environment.CurrentDirectory</c> and a relative path
    /// given to .NET follow it (net_assembly <c>cwd_follows_cd</c>, ADR 0176). The folder is left
    /// there, as MATLAB leaves it; a folder that cannot be entered leaves the process where it was.
    /// </summary>
    public void SyncFolder()
    {
        // The member about to run may change the environment, which the native host must then be
        // sent before its next call (ADR 0180); a counter makes that a compare when nothing ran.
        Native.EnvironmentBlock.NoteChange();

        if (Folder?.Invoke() is not { } folder || string.Equals(folder, _processFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Environment.CurrentDirectory = folder;
            _processFolder = folder;
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The session's folder is gone or unreachable; .NET keeps the process's.
        }
    }

    /// <summary>Where this session's .NET warnings go: its <c>warning</c> state and its console.</summary>
    public Action<string, string>? Warn { get; set; }

    /// <summary>The assemblies this session added, in the order it added them.</summary>
    public IReadOnlyList<Assembly> Added => _added;

    /// <summary>Whether a dotted name is a namespace: <c>System</c>, <c>System.IO</c>, <c>JGTest</c>.</summary>
    public bool IsNamespace(string name) =>
        Framework.Namespaces.Contains(name) || (_session?.Namespaces.Contains(name) ?? false);

    /// <summary>The public type a full name names, or null. Generic definitions answer to <c>List`1</c>.</summary>
    public Type? TypeNamed(string fullName) =>
        _session is { } session && session.Types.TryGetValue(fullName, out Type? own) ? own
        : Framework.Types.TryGetValue(fullName, out Type? found) ? found
        : null;

    /// <summary>
    /// The namespaces and types one level under <paramref name="qualifier"/> (<c>System</c> gives
    /// <c>IO</c>, <c>Math</c>, …), from the framework and this session's assemblies, for the editor's
    /// completion (ADR 0183). A generic type is named without its arity.
    /// </summary>
    public IEnumerable<(string Name, bool IsNamespace)> Children(string qualifier) =>
        _session is { } session
            ? Framework.ChildrenOf(qualifier).Concat(session.ChildrenOf(qualifier)).DistinctBy(static c => c.Name)
            : Framework.ChildrenOf(qualifier);

    /// <summary>Whether the framework's index is built, so asking it costs a lookup rather than a scan of every framework assembly.</summary>
    public static bool FrameworkReady => _framework is not null;

    /// <summary>Starts building the framework's index on a pool thread, for an editor that must not wait for it.</summary>
    public static void WarmFramework()
    {
        if (_framework is null)
        {
            _ = Task.Run(static () => Framework);
        }
    }

    /// <summary><see cref="Children"/> of the framework alone, for an editor with no session to ask.</summary>
    public static IReadOnlyList<(string Name, bool IsNamespace)> FrameworkChildren(string qualifier) => Framework.ChildrenOf(qualifier);

    /// <summary>The framework's public type of that full name, or null; <see cref="TypeNamed"/> without a session.</summary>
    public static Type? FrameworkType(string fullName) => Framework.Types.GetValueOrDefault(fullName);

    /// <summary>The generic definitions a name without its arity names: <c>System.Collections.Generic.List</c>.</summary>
    public IEnumerable<Type> GenericDefinitions(string fullName)
    {
        for (int arity = 1; arity <= 8; arity++)
        {
            if (TypeNamed($"{fullName}`{arity}") is { } definition)
            {
                yield return definition;
            }
        }
    }

    /// <summary>
    /// Loads an assembly by path (once per process) and makes its types visible to this session.
    /// Answers the assembly; adding it again answers the same one.
    /// </summary>
    public Assembly AddFromPath(string path)
    {
        Assembly assembly = LoadShared(path);
        Add(assembly);
        return assembly;
    }

    /// <summary>
    /// Loads an assembly by path into the process's shared load context, once per path, without making
    /// it visible to any session: <c>NET.addAssembly</c>'s load, and a compiled build's file reference.
    /// </summary>
    public static Assembly LoadShared(string path)
    {
        string full = Path.GetFullPath(path);
        Assembly assembly;
        lock (Gate)
        {
            if (!LoadedByPath.TryGetValue(full, out assembly!))
            {
                // The file is read as a PE image first, which is where R2025b's refusals of a native
                // DLL and of a file that is not an image come from ("PE image does not have
                // metadata.", "Image is too small.", source System.Reflection.Metadata: net_assembly).
                using (FileStream stream = File.OpenRead(full))
                using (var image = new System.Reflection.PortableExecutable.PEReader(stream))
                {
                    _ = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(image);
                }

                assembly = Context.LoadFromAssemblyPath(full);
                LoadedByPath[full] = assembly;
            }
        }

        return assembly;
    }

    /// <summary>
    /// Makes an already-loaded assembly's types visible to this session. A compiled build
    /// (<paramref name="overrides"/>) wins a full name an earlier added assembly also defines, so an
    /// edited helper answers to its own name; any other assembly leaves the first definition in place.
    /// </summary>
    public void Add(Assembly assembly, bool overrides = false)
    {
        if (_added.Contains(assembly))
        {
            return;
        }

        _added.Add(assembly);
        if (overrides)
        {
            _overriding.Add(assembly);
        }

        _session ??= new Index();
        _session.Take(assembly, overrides);
    }

    /// <summary>
    /// Takes a retired compiled build out of this session (ADR 0179): its types, its handle, and the
    /// index rebuilt from what is left, so a name it defined answers to whatever else defines it.
    /// </summary>
    public void Remove(Assembly assembly)
    {
        if (!_added.Remove(assembly))
        {
            return;
        }

        _overriding.Remove(assembly);
        _handles.Remove(assembly);
        _session = new Index();
        foreach (Assembly kept in _added)
        {
            _session.Take(kept, _overriding.Contains(kept));
        }
    }

    /// <summary>The process-wide index of the framework's types, built on first use.</summary>
    private static Index Framework
    {
        get
        {
            if (_framework is { } built)
            {
                return built;
            }

            lock (Gate)
            {
                return _framework ??= BuildFramework();
            }
        }
    }

    private static Index BuildFramework()
    {
        var index = new Index();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (IsFrameworkAssembly(loaded))
            {
                seen.Add(loaded.GetName().Name ?? "");
                index.Take(loaded);
            }
        }

        string folder = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (string file in Directory.EnumerateFiles(folder, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (seen.Contains(name) || !(name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.", StringComparison.Ordinal) || name is "mscorlib" or "netstandard"))
            {
                continue;
            }

            try
            {
                index.Take(AssemblyLoadContext.Default.LoadFromAssemblyName(AssemblyName.GetAssemblyName(file)));
            }
            catch (Exception fault) when (fault is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                // A native image in the folder (clrjit, the host) has no metadata to index.
            }
        }

        return index;
    }

    /// <summary>
    /// Whether a loaded assembly belongs to a framework (its file sits under the shared framework
    /// root), rather than to JGraph or to a package it brought.
    /// </summary>
    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
        {
            return false;
        }

        string shared = Path.GetDirectoryName(Path.GetDirectoryName(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\', '/'))!)!;
        return assembly.Location.StartsWith(shared, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Namespaces and public top-level types, by full name.</summary>
    private sealed class Index
    {
        public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Type> Types { get; } = new(StringComparer.Ordinal);

        /// <summary>Children by parent name, built on first ask and dropped when an assembly is taken.</summary>
        private Dictionary<string, List<(string Name, bool IsNamespace)>>? _children;

        private readonly object _childrenGate = new();

        public IReadOnlyList<(string Name, bool IsNamespace)> ChildrenOf(string qualifier)
        {
            lock (_childrenGate)
            {
                _children ??= BuildChildren();
                return _children.TryGetValue(qualifier, out List<(string, bool)>? found) ? found : [];
            }
        }

        private Dictionary<string, List<(string Name, bool IsNamespace)>> BuildChildren()
        {
            var children = new Dictionary<string, List<(string, bool)>>(StringComparer.Ordinal);
            var seen = new HashSet<(string, string)>();
            void Add(string parent, string name, bool isNamespace)
            {
                if (seen.Add((parent, name)))
                {
                    if (!children.TryGetValue(parent, out List<(string, bool)>? list))
                    {
                        children[parent] = list = [];
                    }

                    list.Add((name, isNamespace));
                }
            }

            foreach (string space in Namespaces)
            {
                int dot = space.LastIndexOf('.');
                Add(dot < 0 ? "" : space[..dot], space[(dot + 1)..], true);
            }

            foreach (Type type in Types.Values)
            {
                string name = type.Name;
                int tick = name.IndexOf('`');
                Add(type.Namespace ?? "", tick < 0 ? name : name[..tick], false);
            }

            return children;
        }

        public void Take(Assembly assembly, bool overrides = false)
        {
            lock (_childrenGate)
            {
                _children = null;
            }

            Type[] exported;
            try
            {
                exported = assembly.GetExportedTypes();
            }
            catch (Exception fault) when (fault is ReflectionTypeLoadException or NotSupportedException or FileNotFoundException)
            {
                return;
            }

            foreach (Type type in exported)
            {
                if (type.IsNested || type.FullName is not { } fullName || type.Namespace is not { } space)
                {
                    continue;
                }

                if (overrides)
                {
                    Types[fullName] = type;
                }
                else
                {
                    Types.TryAdd(fullName, type);
                }

                for (int dot = space.Length; dot > 0; dot = space.LastIndexOf('.', dot - 1))
                {
                    if (!Namespaces.Add(space[..dot]))
                    {
                        break;
                    }
                }
            }
        }
    }
}
