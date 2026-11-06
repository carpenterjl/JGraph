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

    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly Interpreter _interpreter;
    private readonly JGraphScriptGlobals _host;
    private readonly List<string> _folders = [];
    private readonly JgsFileIndex _index;
    private readonly JgsCodeTree _tree;

    // Names are case-sensitive the way MATLAB's are; the *paths* they resolve to are not, on Windows.
    private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.Ordinal);

    // Private files are keyed by their path: the same name means a different file under every folder.
    private readonly Dictionary<string, Loaded> _private = new(PathComparer);

    // Package members are keyed by their dotted name (P1), apart from the plain names: a plain-name
    // miss evicts its own entry, and must not evict pkg.f's, or two @pkg.f handles never compare equal.
    private readonly Dictionary<string, Loaded> _dotted = new(StringComparer.Ordinal);

    /// <summary>Creates the path over the interpreter whose globals its functions close over.</summary>
    public JgsFunctionPath(Interpreter interpreter, JGraphScriptGlobals host)
    {
        _interpreter = interpreter;
        _host = host;
        // A +package or @Class folder made current with cd gives its files no bare names (packages
        // plan, P1; measured in R2025b), so it is no search folder; the package it is still answers
        // through the folder that holds it.
        _index = new JgsFileIndex(
            () => host.ImplicitCodeFolders.Where(static folder => !JgsFileIndex.IsMcosFolder(folder)).Concat(_folders),
            () => interpreter.StatementEpoch,
            name => interpreter.Globals.Builtins.TryGet(name, out _),
            message => host.WriteErr(message + "\n"));
        _index.ShadowingFound = WarnShadowing;
        _tree = new JgsCodeTree(_index);
        host.FileChanging += path =>
        {
            _index.Invalidate(path);

            // The search folders moved (cd, rehash): read them now rather than at the next built-in
            // call, so that a folder holding max.m is warned about as the script arrives in it, the
            // way MATLAB warns at the cd.
            if (path is null)
            {
                _ = _index.Shadowing;
            }
        };
    }

    /// <summary>
    /// MATLAB's own warning for a file that takes a built-in's name, raised through the script's
    /// <c>warning</c> so that <c>lastwarn</c> keeps it and the ordinary channel carries it. The file
    /// keeps the name — ADR 0062's reason for warning was that a stray <c>mean.m</c> must not fail in
    /// a way nobody can read, and that reason survives the flip. Answers whether it warned: the JGS
    /// dialect never does, and the index must not count the name as reported when it did not.
    /// </summary>
    private bool WarnShadowing(string name)
    {
        // The session's dialect, not the running code's (V11, ADR 0172): the warning is about the
        // folders a session scans, and a JGS session never raises it, whatever code moved the folder.
        if (!_interpreter.SessionDialect.IsMatlab)
        {
            return false;
        }

        // A name JGraph defines and MATLAB does not (e) shadows nothing of MATLAB's (open item 53).
        if (name is "e")
        {
            return true;
        }

        string message = $"Function {name} has the same name as a MATLAB built-in. "
            + "We suggest you rename the function to avoid a potential name conflict.";
        if (_interpreter.Globals.TryGet("warning", out JgsValue warning) && warning.Type == JgsType.Function)
        {
            warning.AsCallable.Call([JgsValue.Str(message)], 0, 0);
            return true;
        }

        _host.WriteErr("Warning: " + message + "\n");
        return true;
    }

    /// <summary>The folders <c>addpath</c> has added, in search order (the implicit ones are not listed).</summary>
    public IReadOnlyList<string> Folders => _folders;

    /// <summary>
    /// What <c>.m</c> files the search folders hold, kept current so that whether a built-in name is
    /// shadowed by a file can be answered without a disk probe — the one read the resolver makes on
    /// its way to an unshadowed built-in, and the loop compiler's guard.
    /// </summary>
    public JgsFileIndex Index => _index;

    /// <summary>The <c>+package</c> folders the search folders hold, by kind (packages plan, stage P1).</summary>
    public JgsCodeTree Tree => _tree;

    /// <summary>The interpreter this path loads for, whose loaded classes a package listing consults (P2).</summary>
    internal Interpreter Interpreter => _interpreter;

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
        _ = _index.Shadowing; // read now, so a shadowing file in the folder is warned about at the addpath
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
    /// Forgets every loaded file that is not running, so the next call of each name re-reads it from
    /// disk — what <c>clear all</c> and <c>clear functions</c> mean for the path. Function storage a
    /// file made stays behind for any handle still holding one of its functions; the re-read
    /// replaces it.
    /// </summary>
    /// <remarks>
    /// A file with a function on the call stack stays loaded (V5, #72). A re-read makes new
    /// declarations, and a function's persistents are keyed by its declaration: unloading a running
    /// file would hand its next call — the recursive one, or the callback that re-enters it — a
    /// fresh set, which is the reset R2025b does not make.
    /// </remarks>
    public void Unload()
    {
        foreach ((string name, Loaded loaded) in _loaded.ToList())
        {
            if (!_interpreter.IsFileActive(loaded.Path))
            {
                _loaded.Remove(name);
            }
        }

        foreach ((string path, Loaded _) in _private.ToList())
        {
            if (!_interpreter.IsFileActive(path))
            {
                _private.Remove(path);
            }
        }

        foreach ((string name, Loaded loaded) in _dotted.ToList())
        {
            if (!_interpreter.IsFileActive(loaded.Path))
            {
                _dotted.Remove(name);
            }
        }

        _index.Invalidate(null);
    }

    /// <summary>
    /// <c>clear name</c> for a function file: forgets the loaded file <paramref name="name"/> answers
    /// to and the persistents of the functions it defines, unless one of them is running. A name no
    /// loaded file answers to is left alone — a script's own functions live with the script.
    /// </summary>
    public void Unload(string name)
    {
        if (_loaded.TryGetValue(name, out Loaded? loaded) && _interpreter.ForgetPersistentsOf(loaded.Path))
        {
            _loaded.Remove(name);
        }

        // clear pkg.f: the member by its dotted name (P1).
        if (_dotted.TryGetValue(name, out Loaded? member) && _interpreter.ForgetPersistentsOf(member.Path))
        {
            _dotted.Remove(name);
        }

        // clear name for a class file forgets the class too (W11: App Designer's Run clears the
        // app's class and runs the file as saved); objects made before keep the class they were made with.
        _interpreter.ForgetClassFromFile(name);
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
        // In one folder an .mlapp is found before an .m of the same name (U7, measured in R2025b).
        // A deployed session's code is on its frozen path alone (stage K1).
        // A P-file is probed for only where the index has seen one (stage K3b).
        bool pcode = _index.HoldsPcode(name);
        if (_host.Deployment is null)
        {
            foreach (string folder in _host.ImplicitCodeFolders)
            {
                // A +package or @Class folder made current gives its files no bare names (P1, measured).
                if (JgsFileIndex.IsMcosFolder(folder))
                {
                    continue;
                }

                foreach (string extension in CodeExtensions)
                {
                    if (!pcode && extension == JgsPcode.Extension)
                    {
                        continue;
                    }

                    string beside = Path.Combine(folder, name + extension);
                    if (File.Exists(beside))
                    {
                        return Path.GetFullPath(beside);
                    }
                }
            }
        }

        foreach (string folder in _folders)
        {
            foreach (string extension in CodeExtensions)
            {
                if (!pcode && extension == JgsPcode.Extension)
                {
                    continue;
                }

                string candidate = Path.Combine(folder, name + extension);
                if (File.Exists(candidate))
                {
                    fromAddedFolder = true;
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The file a name means when it may carry a code file's extension: <c>which('app1.mlapp')</c>
    /// and <c>type helper.m</c> look where a call would. Null for any other extension.
    /// </summary>
    internal string? FindFile(string asked)
    {
        string extension = Path.GetExtension(asked);
        if (extension.Length == 0)
        {
            return Find(asked);
        }

        string stem = asked[..^extension.Length];
        if (!CodeExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || !IsPlainName(stem))
        {
            return null;
        }

        // The current folder holds data, not code, in a deployed session (stage K1).
        string beside = _host.Resolve(asked);
        if (_host.Deployment is null && File.Exists(beside))
        {
            return Path.GetFullPath(beside);
        }

        foreach (string folder in _folders)
        {
            string candidate = Path.Combine(folder, asked);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The extensions a name is looked for under, first found first: an App Designer file, a P-code
    /// file, then a code file. A P-file is found before the .m beside it (R2025b, measured in
    /// probe_k3b_pcode); an .mlapp before a P-file is MATLAB's documented order, not measured.
    /// </summary>
    internal static readonly string[] CodeExtensions = [JgsMlapp.Extension, JgsPcode.Extension, ".m"];

    // --- Packages (the packages and class folders plan, stage P1, ADR 0307) ----------------------

    /// <summary>
    /// The file a dotted name (<c>pkg.sub.h</c>) resolves to - the code file <c>h</c> in the package
    /// <c>pkg.sub</c>, in the first search folder holding one - or null when no package holds it.
    /// <paramref name="fromAddedFolder"/> says whether that folder was added with <c>addpath</c>.
    /// </summary>
    internal string? FindDotted(string dotted, out bool fromAddedFolder)
    {
        fromAddedFolder = false;
        if (JgsCodeTree.DottedParts(dotted) is not { } parts)
        {
            return null;
        }

        string member = parts[^1];
        if (_tree.MemberFolder(parts[..^1], member) is not { } hit)
        {
            return null;
        }

        fromAddedFolder = _folders.Contains(hit.SearchFolder, PathComparer);
        return MemberFile(hit.Folder, member, hit.Entry);
    }

    /// <summary>The code file <paramref name="stem"/> in <paramref name="folder"/>, an .mlapp before a P-file before the .m, as <see cref="Find(string)"/> orders them.</summary>
    private static string MemberFile(string folder, string stem, JgsFileIndex.Folder entry)
    {
        foreach (string extension in CodeExtensions)
        {
            if (extension == JgsPcode.Extension && !entry.PcodeStems.Contains(stem))
            {
                continue;
            }

            string candidate = Path.Combine(folder, stem + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, stem + ".m");
    }

    /// <summary>
    /// Resolves a dotted name to the callable its package member defines, loading and caching the
    /// file under the dotted name — so <c>clear pkg.f</c> finds it — and re-reading it when it has
    /// been written since. The member's own name is the bare stem: a script in a package is the
    /// script callable, a function file its main function, a class file its constructor.
    /// </summary>
    public bool TryResolveDotted(string dotted, out JgsValue value, out string? file, out bool fromAddedFolder)
    {
        value = JgsValue.Null;
        file = null;
        if (FindDotted(dotted, out fromAddedFolder) is not { } path)
        {
            _dotted.Remove(dotted);
            return false;
        }

        file = path;
        _dotted.TryGetValue(dotted, out Loaded? cached);
        string stem = dotted[(dotted.LastIndexOf('.') + 1)..];
        if (!TryLoadCurrent(stem, path, cached, out Loaded loaded))
        {
            return false;
        }

        _dotted[dotted] = loaded;
        value = loaded.Value;
        return true;
    }

    /// <summary>
    /// A relative path with a folder in it (<c>+pkg/f.m</c>, <c>@Thing/extent.m</c>) looked for under
    /// each search folder in order - what <c>type('+pkg/f.m')</c> reads (R2025b, measured) when the
    /// current folder has no such file. Null for a rooted path, a plain name, or a miss.
    /// </summary>
    internal string? FindRelative(string relative)
    {
        if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.IndexOfAny(['/', '\\']) < 0)
        {
            return null;
        }

        foreach (string folder in _index.SearchFolders)
        {
            string candidate = Path.Combine(folder, relative);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Every file <paramref name="name"/> names, in the order MATLAB's <c>which -all</c> lists them:
    /// folder by folder, the <c>.mlapp</c> of a folder before its <c>.m</c>.
    /// </summary>
    internal IReadOnlyList<string> FindAll(string name)
    {
        var found = new List<string>();
        if (!IsPlainName(name))
        {
            return found;
        }

        void Add(string candidate)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full) && !found.Contains(full, PathComparer))
            {
                found.Add(full);
            }
        }

        string? first = Find(name);
        string? firstFolder = first is null ? null : Path.GetDirectoryName(first);
        if (firstFolder is not null)
        {
            foreach (string extension in CodeExtensions)
            {
                Add(Path.Combine(firstFolder, name + extension));
            }
        }

        foreach (string folder in _folders)
        {
            foreach (string extension in CodeExtensions)
            {
                Add(Path.Combine(folder, name + extension));
            }
        }

        return found;
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

        // A P-file is found before the .m beside it here as anywhere (stage K3b).
        string path = Path.Combine(privateFolder, name + (_index.HoldsPcodeIn(privateFolder, name) ? JgsPcode.Extension : ".m"));
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
        IReadOnlyList<Stmt> program;
        if (JgsPcode.IsPcode(path))
        {
            try
            {
                program = JgsPcode.Load(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new JgsRuntimeException(0, 0, $"'{name}': cannot read '{path}': {ex.Message}");
            }

            WarnIfOlderThanSource(path);
            return Define(name, path, program);
        }

        string source;
        try
        {
            source = JgsMlapp.ReadSource(path);
        }
        catch (InvalidDataException)
        {
            // An .mlapp that is not a package MATLAB can read: R2025b's refusal (U7, measured).
            throw new JgsRuntimeException(0, 0, "MATLAB:fileio:cantOpenFile", $"{path}: Can't open file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(0, 0, $"'{name}': cannot read '{path}': {ex.Message}");
        }

        try
        {
            program = Parser.Parse(source, path, JgsDialect.Matlab);
        }
        catch (JgsSyntaxException error)
        {
            // A file on the path that does not parse is a load error of that file, raised where the
            // name is first used and catchable there (V6, #106: R2025b's "Error: File: … Line: …
            // Column: …" is what a try around the first construction catches), not a syntax error
            // of the running script. The location is the file's own; the message names the file
            // rather than its whole path, so a recorded message reads the same on every machine.
            var load = new JgsRuntimeException(error.Line, error.Column,
                $"Error: File: {Path.GetFileName(path)} Line: {error.Line} Column: {error.Column}\n{error.Message}");
            load.AttributeTo(path);
            throw load;
        }

        return Define(name, path, program);
    }

    /// <summary>
    /// R2025b's warning when a P-file runs with a newer source beside it: the P-file still runs
    /// (measured, probe_k3b_pcode), once warned for each time it is read.
    /// </summary>
    private void WarnIfOlderThanSource(string path)
    {
        string source = Path.ChangeExtension(path, ".m");
        try
        {
            if (!File.Exists(source) || File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(path))
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        JgsBuiltins.Warn(_host, "MATLAB:pfileOlderThanMfile",
            $"P-code file {path} is older than source code file {source}.\n{path} may be obsolete and may need to be regenerated.\nType \"help pcode\" for information about generating P-code.");
    }

    /// <summary>The callable a file's parsed program defines, whether it came from source or P-code.</summary>
    private JgsValue Define(string name, string path, IReadOnlyList<Stmt> program)
    {
        // A class file holds one classdef, followed by nothing but the file's local functions, and
        // the name it answers to is the file's. What the path hands back for it is the constructor,
        // which is what makes `Circle(2)` an ordinary call: the interpreter never learns a new kind
        // of callee (M68).
        if (program.Count > 0 && program[0] is ClassdefStmt classFile)
        {
            if (!string.Equals(classFile.Name, name, StringComparison.Ordinal))
            {
                throw new JgsRuntimeException(classFile.Line, classFile.Column, "MATLAB:m_class_filename",
                    $"'{Path.GetFileName(path)}' defines class '{classFile.Name}', so it answers to "
                    + $"'{classFile.Name}' and not to '{name}' — a class file is named after its class.");
            }

            // The class's scope sits under the built-in layer like a file's: a method or a property
            // default reads the class file's names and the built-ins, and no variable of the script.
            // A local function after the classdef is the class's own (V6): its methods see it by
            // bare name, as they see each other, and nothing outside the file does.
            JgsEnvironment scope = _interpreter.NewFileScope();
            for (int i = 1; i < program.Count; i++)
            {
                if (program[i] is not FnStmt local)
                {
                    throw new JgsRuntimeException(program[i].Line, program[i].Column,
                        $"'{Path.GetFileName(path)}': a class file holds one classdef and its local functions, nothing else.");
                }

                scope.DeclareFunction(local.Name, JgsValue.Function(new UserFunction(local, scope, _interpreter)));
            }

            return _interpreter.DefineClass(classFile, scope).ConstructorValue;
        }

        if (!JgsRunner.IsFunctionFile(program))
        {
            // A script's functions are hoisted into its storage on each run; a re-read starts it over
            // so a function the edit removed does not linger.
            _interpreter.ReplaceFile(path);
            JgsRuntimeException NotAFunction(int line, int column) =>
                // R2025b's words for a script called with arguments or asked for a value (measured,
                // packages plan P1: x = pkg.s, pkg.s(1)).
                new(line, column, "MATLAB:scriptNotAFunction", $"Execution of script {name} as a function is not supported:\n{path}");

            void RunScript()
            {
                // The script's statements carry the MATLAB dialect they were parsed in, and the run
                // enters it (V11, ADR 0172) - whoever named the script.
                _interpreter.RunScriptFile(program, _interpreter.CurrentFrame, path);
            }

            // The single-output road is a value wanted (x = s): refused, as R2025b refuses; a
            // statement's mention arrives on the multi-output road asked for nothing, and runs.
            return JgsValue.Function(new BuiltinFunction(name, (_, line, column) => throw NotAFunction(line, column))
            {
                ScriptFile = path,
                KnowsWhenDiscarded = true,
                BindsAnsAsStatement = false,
                MultiOutput = (args, wanted, line, column) =>
                {
                    if (args.Count > 0 || wanted > 0)
                    {
                        throw NotAFunction(line, column);
                    }

                    RunScript();
                    return [];
                },
            });
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
