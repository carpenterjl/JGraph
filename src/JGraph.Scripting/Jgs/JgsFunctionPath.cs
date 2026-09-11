using System.IO;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's search path: the folders a bare name is looked for in once the workspace, the script's own
/// functions, and the built-ins have all failed to answer. It turns a folder of <c>.m</c> files into a
/// project — <c>main.m</c> calling <c>helper.m</c> — which is the shape almost every ported MATLAB
/// codebase arrives in and which JGraph had no answer for before M62.
/// </summary>
/// <remarks>
/// A file here outranks a built-in of the same name, as it does in MATLAB (M145): the resolver asks
/// the folders before the built-in layer, and the <see cref="Index"/> tells it, without a disk probe,
/// whether a built-in name has a file claiming it at all. What still lets a built-in answer ahead of
/// a same-named file is the measured dispatch table — a built-in <em>class method</em> for the
/// arguments' classes — which is the resolver's business, not this class's. Until M145 the
/// built-ins won outright, a divergence ADR 0062 recorded so that a stray <c>mean.m</c> could not
/// quietly replace the real one; the shadowing warning of step 7 is what keeps that readable now.
/// A file's <c>private/</c> folder is served by <see cref="TryResolvePrivate"/>, through the same
/// loader and cache.
/// </remarks>
internal sealed class JgsFunctionPath
{
    /// <summary>A loaded file: what it resolved to, when it was last written, and the callable built from it.</summary>
    private sealed record Loaded(string Path, DateTime Written, JgsValue Value);

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly Interpreter _interpreter;
    private readonly JGraphScriptGlobals _host;
    private readonly List<string> _folders = [];
    private readonly JgsFileIndex _index;

    // Names are case-sensitive the way MATLAB's are; the *paths* they resolve to are not, on Windows.
    private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.Ordinal);

    // Private files are keyed by their path: the same name means a different file under every folder.
    private readonly Dictionary<string, Loaded> _private = new(PathComparer);

    /// <summary>Creates the path over the interpreter whose globals its functions close over.</summary>
    public JgsFunctionPath(Interpreter interpreter, JGraphScriptGlobals host)
    {
        _interpreter = interpreter;
        _host = host;
        _index = new JgsFileIndex(
            () => host.ImplicitCodeFolders.Concat(_folders),
            () => interpreter.StatementEpoch,
            name => interpreter.Globals.Builtins.TryGet(name, out _),
            message => host.WriteErr(message + "\n"));
        host.FileChanging += _index.Invalidate;
    }

    /// <summary>The folders <c>addpath</c> has added, in search order (the implicit ones are not listed).</summary>
    public IReadOnlyList<string> Folders => _folders;

    /// <summary>
    /// What <c>.m</c> files the search folders hold, kept current so that whether a built-in name is
    /// shadowed by a file can be answered without a disk probe — the one read the resolver makes on
    /// its way to an unshadowed built-in, and the loop compiler's guard.
    /// </summary>
    public JgsFileIndex Index => _index;

    /// <summary>
    /// Adds <paramref name="folder"/> to the search path, at the front unless
    /// <paramref name="atEnd"/>. Adding a folder that is already there moves it, which is what
    /// MATLAB's own <c>addpath</c> does and what makes <c>addpath(d)</c> a way to give d priority.
    /// </summary>
    public void Add(string folder, bool atEnd)
    {
        string full = Path.GetFullPath(folder);
        Remove(full);
        if (atEnd)
        {
            _folders.Add(full);
        }
        else
        {
            _folders.Insert(0, full);
        }

        // A folder joining or leaving the path can change what a name means, and the cache holds the
        // old answer. It is small and rebuilding it is a file read, so clear it rather than reason
        // about which entries the change could have reached. The index re-reads for the same reason.
        _loaded.Clear();
        _index.Invalidate(null);
    }

    /// <summary>Removes <paramref name="folder"/> from the search path; false when it was not on it.</summary>
    public bool Remove(string folder)
    {
        string full = Path.GetFullPath(folder);
        int at = _folders.FindIndex(existing => PathComparer.Equals(existing, full));
        if (at < 0)
        {
            return false;
        }

        _folders.RemoveAt(at);
        _loaded.Clear();
        _index.Invalidate(null);
        return true;
    }

    /// <summary>
    /// Forgets every loaded file, so the next call of each name re-reads it from disk — what
    /// <c>clear all</c> and <c>clear functions</c> mean for the path. Function storage a file made
    /// stays behind for any handle still holding one of its functions; the re-read replaces it.
    /// </summary>
    public void Unload()
    {
        _loaded.Clear();
        _private.Clear();
        _index.Invalidate(null);
    }

    /// <summary>The file <paramref name="name"/> would resolve to, or null when no folder holds one.</summary>
    public string? Find(string name) => Find(name, out _);

    /// <summary>
    /// The same, saying whether the file came from a folder <c>addpath</c> added rather than from the
    /// current folder, the script's folder or the workspace root — the two layers a resolution names.
    /// </summary>
    internal string? Find(string name, out bool fromAddedFolder)
    {
        fromAddedFolder = false;
        if (!IsPlainName(name))
        {
            return null;
        }

        // The current folder and the running script's own folder come first, exactly as they do for
        // every other file a script names — Resolve already knows that order, so it is not repeated.
        string beside = _host.Resolve(name + ".m");
        if (File.Exists(beside))
        {
            return Path.GetFullPath(beside);
        }

        foreach (string folder in _folders)
        {
            string candidate = Path.Combine(folder, name + ".m");
            if (File.Exists(candidate))
            {
                fromAddedFolder = true;
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> to the callable its file defines, loading and caching the file
    /// the first time and re-reading it when it has been written since. Returns false when no file on
    /// the path carries the name.
    /// </summary>
    public bool TryResolve(string name, out JgsValue value) => TryResolve(name, out value, out _, out _);

    /// <summary>
    /// The same, handing back the file's path and which kind of folder held it, for the resolver
    /// to record where the answer came from.
    /// </summary>
    public bool TryResolve(string name, out JgsValue value, out string? file, out bool fromAddedFolder)
    {
        value = JgsValue.Null;
        file = null;
        if (Find(name, out fromAddedFolder) is not { } path)
        {
            _loaded.Remove(name); // the file that used to answer this name is gone
            return false;
        }

        file = path;
        _loaded.TryGetValue(name, out Loaded? cached);
        if (!TryLoadCurrent(name, path, cached, out Loaded loaded))
        {
            return false;
        }

        _loaded[name] = loaded;
        value = loaded.Value;
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> to the function <c><paramref name="privateFolder"/>/name.m</c>
    /// defines — the private layer, visible only to code in the folder above <c>private/</c>, which
    /// is why the caller names the folder rather than this class working it out. The index says
    /// whether the file is there (no disk probe on a miss); the loader and cache are the path's.
    /// </summary>
    public bool TryResolvePrivate(string privateFolder, string name, out JgsValue value, out string? file)
    {
        value = JgsValue.Null;
        file = null;
        if (!IsPlainName(name) || !_index.StemsOfFullPath(privateFolder).Contains(name))
        {
            return false;
        }

        string path = Path.Combine(privateFolder, name + ".m");
        _private.TryGetValue(path, out Loaded? cached);
        if (!TryLoadCurrent(name, path, cached, out Loaded loaded))
        {
            _private.Remove(path);
            return false;
        }

        _private[path] = loaded;
        value = loaded.Value;
        file = path;
        return true;
    }

    /// <summary>
    /// The loaded form of <paramref name="path"/> as of now: <paramref name="cached"/> when it is
    /// that path and the file has not been written since, a fresh load otherwise. False when the
    /// file is not there — the index can be a statement behind another process.
    /// </summary>
    private bool TryLoadCurrent(string name, string path, Loaded? cached, out Loaded loaded)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            written = default;
        }

        if (!File.Exists(path))
        {
            loaded = null!;
            return false;
        }

        bool loadedBefore = cached is not null && PathComparer.Equals(cached.Path, path);
        if (loadedBefore && cached!.Written == written)
        {
            loaded = cached;
            return true;
        }

        JgsValue value;
        try
        {
            value = Load(name, path);
        }
        catch (JgsRuntimeException) when (loadedBefore && !CanRead(path))
        {
            // MATLAB's words for a function it had and cannot re-read. (A file that has been deleted
            // outright is the case above — Find no longer sees it — and falls through to the next
            // candidate instead, a recorded divergence: a dead binding that errors is stale-cache
            // behaviour, not fidelity.)
            throw new JgsRuntimeException(0, 0, $"Previously accessible file \"{path}\" is now inaccessible.");
        }

        loaded = new Loaded(path, written, value);
        return true;
    }

    private static bool CanRead(string path)
    {
        try
        {
            using FileStream probe = File.OpenRead(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the callable a file defines. A function file becomes its first function, closed over an
    /// environment holding the file's other functions — which is what makes a file's local functions
    /// local to it. A script file becomes something that runs the file's statements in the caller's
    /// own workspace, because that is what running a script by name means.
    /// </summary>
    private JgsValue Load(string name, string path)
    {
        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(0, 0, $"'{name}': cannot read '{path}': {ex.Message}");
        }

        IReadOnlyList<Stmt> program;
        try
        {
            program = Parser.Parse(source, path, JgsDialect.Matlab);
        }
        catch (JgsSyntaxException error)
        {
            error.AttributeTo(path);
            throw;
        }

        // A class file holds one classdef and nothing else, and the name it answers to is the file's.
        // What the path hands back for it is the constructor, which is what makes `Circle(2)` an
        // ordinary call: the interpreter never learns a new kind of callee (M68).
        if (program is [ClassdefStmt classFile])
        {
            if (!string.Equals(classFile.Name, name, StringComparison.Ordinal))
            {
                throw new JgsRuntimeException(classFile.Line, classFile.Column,
                    $"'{Path.GetFileName(path)}' defines class '{classFile.Name}', so it answers to "
                    + $"'{classFile.Name}' and not to '{name}' — a class file is named after its class.");
            }

            // The class's scope sits under the built-in layer like a file's: a method or a property
            // default reads the class file's names and the built-ins, and no variable of the script.
            return _interpreter.DefineClass(classFile, _interpreter.NewFileScope()).ConstructorValue;
        }

        if (!JgsRunner.IsFunctionFile(program))
        {
            // A script's functions are hoisted into its storage on each run; a re-read starts it over
            // so a function the edit removed does not linger.
            _interpreter.ReplaceFile(path);
            return JgsValue.Function(new BuiltinFunction(name, (args, line, column) =>
            {
                if (args.Count > 0)
                {
                    throw new JgsRuntimeException(line, column,
                        $"'{name}' is a script file, not a function, so it takes no arguments.");
                }

                _interpreter.RunInDialect(JgsDialect.Matlab,
                    () => _interpreter.RunScriptFile(program, _interpreter.CurrentFrame, path));
                return JgsValue.Null;
            }));
        }

        // The file's functions live with the file: they see each other through the resolver, the
        // built-ins through the scope they close over, and nothing of any workspace; nothing outside
        // the file sees any but the first — MATLAB's local-function rule.
        FunctionFile file = _interpreter.ReplaceFile(path);
        foreach (Stmt statement in program)
        {
            _interpreter.Hoist((FnStmt)statement, path);
        }

        // MATLAB dispatches on the file name, not on the header: helper.m answers to 'helper' even if
        // its first function is spelt something else. The main function is the file's answer, not a
        // local function of it — see FunctionFile.MainName.
        var main = (FnStmt)program[0];
        file.MainName = main.Name;
        return file.TryGet(main.Name, out JgsValue callable) ? callable : JgsValue.Null;
    }

    /// <summary>
    /// Whether <paramref name="name"/> could name a file on the path at all. A name carrying a
    /// separator or an extension is a path, not an identifier, and asking the file system about every
    /// failed lookup of one would be a wasted probe per miss.
    /// </summary>
    private static bool IsPlainName(string name) =>
        name.Length > 0
        && name.IndexOfAny(['/', '\\', ':', '.', ' ']) < 0;
}
