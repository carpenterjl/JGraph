using System.IO;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's search path: the folders a bare name is looked for in once the workspace, the script's own
/// functions, and the built-ins have all failed to answer. It turns a folder of <c>.m</c> files into a
/// project — <c>main.m</c> calling <c>helper.m</c> — which is the shape almost every ported MATLAB
/// codebase arrives in and which JGraph had no answer for before M62.
/// </summary>
/// <remarks>
/// Resolution deliberately runs <em>last</em>. In MATLAB a path file shadows a built-in of the same
/// name; here the ~2,500 built-ins win, because a script that quietly gets a user's half-finished
/// <c>mean.m</c> instead of the real one fails in a way nobody can read. ADR 0062 records the
/// divergence.
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

    // Names are case-sensitive the way MATLAB's are; the *paths* they resolve to are not, on Windows.
    private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.Ordinal);

    /// <summary>Creates the path over the interpreter whose globals its functions close over.</summary>
    public JgsFunctionPath(Interpreter interpreter, JGraphScriptGlobals host)
    {
        _interpreter = interpreter;
        _host = host;
    }

    /// <summary>The folders <c>addpath</c> has added, in search order (the implicit ones are not listed).</summary>
    public IReadOnlyList<string> Folders => _folders;

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
        // about which entries the change could have reached.
        _loaded.Clear();
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
        return true;
    }

    /// <summary>The file <paramref name="name"/> would resolve to, or null when no folder holds one.</summary>
    public string? Find(string name)
    {
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
    public bool TryResolve(string name, out JgsValue value)
    {
        value = JgsValue.Null;
        if (Find(name) is not { } path)
        {
            _loaded.Remove(name); // the file that used to answer this name is gone
            return false;
        }

        DateTime written = File.GetLastWriteTimeUtc(path);
        if (_loaded.TryGetValue(name, out Loaded? cached)
            && PathComparer.Equals(cached.Path, path) && cached.Written == written)
        {
            value = cached.Value;
            return true;
        }

        value = Load(name, path);
        _loaded[name] = new Loaded(path, written, value);
        return true;
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

        IReadOnlyList<Stmt> program = Parser.Parse(source, path, JgsDialect.Matlab);

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

            return _interpreter.DefineClass(classFile, new JgsEnvironment(_interpreter.Globals)).ConstructorValue;
        }

        if (!JgsRunner.IsFunctionFile(program))
        {
            return JgsValue.Function(new BuiltinFunction(name, (args, line, column) =>
            {
                if (args.Count > 0)
                {
                    throw new JgsRuntimeException(line, column,
                        $"'{name}' is a script file, not a function, so it takes no arguments.");
                }

                _interpreter.RunInDialect(JgsDialect.Matlab,
                    () => _interpreter.RunScriptFile(program, _interpreter.CurrentFrame));
                return JgsValue.Null;
            }));
        }

        // The file's functions see each other and the globals behind them, and nothing outside the
        // file sees any but the first — MATLAB's local-function rule, expressed as a scope.
        var fileScope = new JgsEnvironment(_interpreter.Globals);
        foreach (Stmt statement in program)
        {
            var declaration = (FnStmt)statement;
            fileScope.Declare(declaration.Name, JgsValue.Function(
                new UserFunction(declaration, fileScope, _interpreter)));
        }

        // MATLAB dispatches on the file name, not on the header: helper.m answers to 'helper' even if
        // its first function is spelt something else.
        var main = (FnStmt)program[0];
        return fileScope.TryGet(main.Name, out JgsValue callable) ? callable : JgsValue.Null;
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
