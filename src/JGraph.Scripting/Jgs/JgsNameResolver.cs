namespace JGraph.Scripting.Jgs;

/// <summary>
/// Which layer of MATLAB's search order answered a name (M145). The members are in the order the
/// layers are asked, which is the order the machine showed and not quite the order the documentation
/// gives: a built-in class method sits above <c>private/</c>, a user class method below it.
/// </summary>
internal enum ResolutionLayer
{
    /// <summary>Nothing holds the name.</summary>
    None,

    /// <summary>A variable — in the frame, an enclosing scope, or the global workspace.</summary>
    Bound,

    /// <summary>A nested function of the running function.</summary>
    Nested,

    /// <summary>A local function of the current file.</summary>
    Local,

    /// <summary>A built-in that the dispatch table gives priority for the arguments' classes.</summary>
    BuiltinMethod,

    /// <summary>A file in the current file's <c>private/</c> folder.</summary>
    Private,

    /// <summary>A method of a user class, dispatched on an argument.</summary>
    UserMethod,

    /// <summary>A file in the current folder, the script's folder or the workspace root — or the running file itself.</summary>
    CurrentFolder,

    /// <summary>A file in a folder <c>addpath</c> added.</summary>
    PathFolder,

    /// <summary>The built-in layer.</summary>
    Builtin,
}

/// <summary>
/// What a name resolved to: the layer that answered, the value it holds, and the file it came from
/// when there is one — so that <c>which</c>, <c>exist</c>, the hot-loop guard and a handle can read
/// the layer and never re-derive it.
/// </summary>
internal readonly record struct Resolution(ResolutionLayer Layer, JgsValue Value, string? File)
{
    /// <summary>The answer for a name nothing holds.</summary>
    public static Resolution None => new(ResolutionLayer.None, JgsValue.Null, null);

    /// <summary>Whether some layer holds the name.</summary>
    public bool Found => Layer != ResolutionLayer.None;

    /// <summary>
    /// Whether the name is a function <em>definition</em> — a nested, local, private or file
    /// function, a method, or a built-in — as opposed to a variable, one holding a handle included.
    /// A definition is what a bare mention calls; a variable is what it reads.
    /// </summary>
    public bool IsFunctionDefinition =>
        Layer is not (ResolutionLayer.None or ResolutionLayer.Bound) && Value.Type == JgsType.Function;

    /// <summary>
    /// Whether the answer is settled before any argument is evaluated: a bound value, or a nested or
    /// local function, which lexical scope gives the name whatever the arguments turn out to be.
    /// Anything else is provisional until phase two of Invoke mode has seen the arguments.
    /// </summary>
    public bool IsLexical =>
        Layer is ResolutionLayer.Bound or ResolutionLayer.Nested or ResolutionLayer.Local;
}

/// <summary>
/// The one place that decides what a name means (M145). Every site that used to look a name up for
/// itself — a callee, a bare mention, <c>@name</c>, <c>feval</c> and <c>str2func</c> by name,
/// <c>which</c>, <c>exist</c>, <c>nargin('f')</c>, the loop JIT's guard — asks here, in one of four
/// modes, and reads the answer's layer rather than working it out again.
/// </summary>
/// <remarks>
/// <para>
/// The order is MATLAB's, as R2025b showed it (step 6, the flip): a <strong>bound name</strong>
/// (the global workspace when a <c>global</c> declaration reaches the frame, else the workspace
/// walk); a <strong>nested</strong> function of the running function; a <strong>local</strong>
/// function of the running file; a <strong>built-in class method</strong> — the built-in of that
/// name when the measured dispatch table says it answers for the arguments' classes and no user
/// object is among them; the running file's <strong><c>private/</c></strong> folder; a
/// <strong>user class method</strong> on the dominant (leftmost) user object, whose class lacking
/// the method falls through; the <strong>current folder</strong> (where <c>cd</c> went, the script's
/// folder, the workspace root — and the running function file itself, whose main function is not a
/// local function of its own file); the <strong><c>addpath</c> folders</strong>; and last the
/// <strong>built-in layer</strong>, so a user file ahead of the built-ins shadows one exactly as it
/// does in MATLAB. The JGS dialect keeps the order it always had, the workspace walk alone.
/// </para>
/// <para>
/// Invoke mode is two phases. <see cref="Invoke(string, JgsEnvironment)"/> is asked <em>before</em>
/// a call's arguments are evaluated, because what the parentheses mean depends on the answer: a
/// bound array is indexed with the unevaluated arguments (<c>A(end)</c>, <c>A(:, 2)</c> need the
/// index context), a bound handle is called, and a nested or local function is settled by lexical
/// scope alone. Everything else is provisional — what phase one has in hand (the built-in the walk
/// ended on, or the running file's own main function) is passed back to
/// <see cref="Invoke(string, Resolution, IReadOnlyList{JgsValue})"/> with the evaluated arguments,
/// whose classes decide the remaining layers. On the way to an unshadowed built-in, which is nearly
/// every call, phase two costs one set lookup in the file index and a field read.
/// </para>
/// </remarks>
internal sealed class JgsNameResolver
{
    private static readonly string[] CompiledCallPatterns = ["double", "logical", "double,double"];
    private static readonly string[] CompiledBarePatterns = ["none"];

    private readonly Interpreter _interpreter;
    private readonly JgsEnvironment _globalWorkspace;

    /// <summary>Creates the resolver over <paramref name="interpreter"/> and its global-variable workspace.</summary>
    public JgsNameResolver(Interpreter interpreter, JgsEnvironment globalWorkspace)
    {
        _interpreter = interpreter;
        _globalWorkspace = globalWorkspace;
    }

    /// <summary>
    /// Value mode: a bare name in an expression or as a statement. The variable, or the function
    /// definition to call with no arguments — resolved under the no-argument pattern, so a bare
    /// <c>eps</c> reaches <c>eps.m</c> where <c>eps(1)</c> keeps the built-in.
    /// </summary>
    public Resolution Value(string name, JgsEnvironment env) => Invoke(name, Invoke(name, env), []);

    /// <summary>
    /// Invoke mode, phase one: <c>f(x)</c>, <c>[a, b] = f(x)</c>, <c>feval('f', x)</c>, with no
    /// argument evaluated yet. A bound value for the caller to index, apply or call, a nested or
    /// local function to call — see <see cref="Resolution.IsLexical"/> — a provisional answer to
    /// hand to phase two with the arguments, or <see cref="Resolution.None"/> when nothing but a
    /// user method could hold the name.
    /// </summary>
    public Resolution Invoke(string name, JgsEnvironment env)
    {
        if (!_interpreter.Dialect.IsMatlab)
        {
            return Lookup(name, env);
        }

        if (env.IsGlobal(name) && _globalWorkspace.TryGetScope(name, out JgsEnvironment? global, out JgsValue shared))
        {
            return Classify(name, global, shared);
        }

        if (env.TryGetScope(name, out JgsEnvironment? scope, out JgsValue value) && !scope.IsBuiltinLayer)
        {
            return Classify(name, scope, value);
        }

        if (_interpreter.TryGetFileFunction(name, out JgsValue held, out string file, out bool isMain))
        {
            return new Resolution(isMain ? ResolutionLayer.CurrentFolder : ResolutionLayer.Local, held, file);
        }

        if (scope is not null)
        {
            return new Resolution(ResolutionLayer.Builtin, value, null);
        }

        // Nothing in the walk, nothing in the file, no built-in: the disk decides now rather than in
        // phase two, so a name nothing holds errors before its arguments are evaluated, as it always
        // did, and only a name something holds has them evaluated. (A built-in name never probes
        // the disk here; the index says in phase two whether a file shadows it.)
        if (TryPrivate(name, out Resolution privately))
        {
            return privately;
        }

        return FromFile(name);
    }

    /// <summary>
    /// Invoke mode, phase two: the layers the arguments' classes decide, from where phase one left
    /// off. <paramref name="lexical"/> is phase one's answer — returned as it is when it is settled,
    /// or when the dialect is JGS — and <paramref name="arguments"/> are the evaluated arguments
    /// (none for Value mode).
    /// </summary>
    public Resolution Invoke(string name, Resolution lexical, IReadOnlyList<JgsValue> arguments)
    {
        if (lexical.IsLexical || !_interpreter.Dialect.IsMatlab)
        {
            return lexical;
        }

        // What phase one left in hand: the built-in the walk ended on, or a file — a private or
        // folder file from the disk, or the running function file's own main function, which then
        // hides a built-in of the same name from the walk (a rare second read).
        bool hasBuiltin = lexical.Layer == ResolutionLayer.Builtin;
        JgsValue builtin = lexical.Value;
        bool fileInHand = lexical.Layer is ResolutionLayer.Private or ResolutionLayer.CurrentFolder or ResolutionLayer.PathFolder;
        if (lexical.Layer == ResolutionLayer.CurrentFolder
            && string.Equals(lexical.File, _interpreter.CurrentFile, StringComparison.Ordinal)
            && BuiltinOf(name) is { } alsoBuiltin)
        {
            hasBuiltin = true;
            builtin = alsoBuiltin;
        }

        JgsValue? dominant = DominantObject(arguments);

        // Nearly every call: an unshadowed built-in with no user object among the arguments.
        if (hasBuiltin && dominant is null && !fileInHand && !IsShadowed(name))
        {
            return lexical;
        }

        // 4. A built-in class method, when the table says the built-in answers these classes.
        if (hasBuiltin && dominant is null && JgsDispatchTable.KeepsBuiltin(name, arguments, _interpreter.Dialect))
        {
            return new Resolution(ResolutionLayer.BuiltinMethod, builtin, null);
        }

        // 5. The running file's private/ folder.
        if (lexical.Layer == ResolutionLayer.Private)
        {
            return lexical;
        }

        if (TryPrivate(name, out Resolution privately))
        {
            return privately;
        }

        // 6. A user class method on the dominant object; a class without it falls through.
        if (dominant is { } instance && _interpreter.TryUserMethod(name, instance, out IJgsCallable? method))
        {
            return new Resolution(ResolutionLayer.UserMethod, JgsValue.Function(method), null);
        }

        // 7–8. The folders: the file phase one found, else the disk.
        if (fileInHand)
        {
            return lexical;
        }

        Resolution file = FromFile(name);
        if (file.Found)
        {
            return file;
        }

        // 9. The built-in layer.
        return hasBuiltin ? new Resolution(ResolutionLayer.Builtin, builtin, null) : Resolution.None;
    }

    /// <summary>
    /// Handle mode: <c>@f</c> and <c>str2func('f')</c>. Never a variable — <c>max = 7; @max</c> is the
    /// function, as it always was — so the walk sees function definitions only, then the file's
    /// locals, its <c>private/</c>, the folders, the built-in. What is captured stands in for its
    /// layer on every invocation; see <see cref="InvokeHandle"/>.
    /// </summary>
    public Resolution Handle(string name, JgsEnvironment env)
    {
        // JGS handles are its values: a JGS 'fn' bound to a name is what @name means there, and the
        // JGS surface is frozen.
        if (!_interpreter.Dialect.IsMatlab)
        {
            return Lookup(name, env);
        }

        if (env.TryGetFunction(name, out JgsEnvironment? scope, out JgsValue value) && !scope.IsBuiltinLayer)
        {
            return Classify(name, scope, value);
        }

        bool ownFile = _interpreter.TryGetFileFunction(name, out JgsValue held, out string file, out bool isMain);
        if (ownFile && !isMain)
        {
            return new Resolution(ResolutionLayer.Local, held, file);
        }

        if (TryPrivate(name, out Resolution privately))
        {
            return privately;
        }

        if (ownFile)
        {
            return new Resolution(ResolutionLayer.CurrentFolder, held, file);
        }

        Resolution disk = FromFile(name);
        if (disk.Found)
        {
            return disk;
        }

        return scope is not null ? new Resolution(ResolutionLayer.Builtin, value, null) : Resolution.None;
    }

    /// <summary>
    /// Query mode: <c>which</c>, <c>exist</c>, <c>nargin('f')</c>. Every layer holding the name as a
    /// function, in the order they would be asked with no argument to dispatch on — the lexical
    /// function, the private file, the folder file, the built-in; empty when none does.
    /// </summary>
    public IEnumerable<Resolution> Query(string name, JgsEnvironment env)
    {
        if (!_interpreter.Dialect.IsMatlab)
        {
            Resolution held = Lookup(name, env);
            if (held.Found && held.Value.Type == JgsType.Function)
            {
                yield return held;
            }

            yield break;
        }

        Resolution lexical = Invoke(name, env);
        if (lexical.Layer is ResolutionLayer.Nested or ResolutionLayer.Local && lexical.Value.Type == JgsType.Function)
        {
            yield return lexical;
        }

        if (TryPrivate(name, out Resolution privately))
        {
            yield return privately;
        }

        if (lexical.Layer == ResolutionLayer.CurrentFolder)
        {
            yield return lexical;
        }
        else
        {
            Resolution disk = FromFile(name);
            if (disk.Found)
            {
                yield return disk;
            }
        }

        if (BuiltinOf(name) is { Type: JgsType.Function } builtin)
        {
            yield return new Resolution(ResolutionLayer.Builtin, builtin, null);
        }
    }

    /// <summary>
    /// Everything but the disk: the workspace walk — a variable or a nested function, honouring a
    /// <c>global</c> declaration that redirects the name — then the running file's own functions,
    /// then the built-in layer. What <c>exist</c> and <c>which</c> ask (a file is their own probe),
    /// and the JGS dialect's whole order. It says nothing about shadowing: a built-in it reports may
    /// lose a call to a file, which is <see cref="CompiledBuiltin"/>'s question.
    /// </summary>
    public Resolution Lookup(string name, JgsEnvironment env)
    {
        if (env.IsGlobal(name) && _globalWorkspace.TryGetScope(name, out JgsEnvironment? global, out JgsValue shared))
        {
            return Classify(name, global, shared);
        }

        if (env.TryGetScope(name, out JgsEnvironment? scope, out JgsValue value) && !scope.IsBuiltinLayer)
        {
            return Classify(name, scope, value);
        }

        if (_interpreter.TryGetFileFunction(name, out JgsValue local, out string file, out _))
        {
            return new Resolution(ResolutionLayer.Local, local, file);
        }

        return scope is not null ? new Resolution(ResolutionLayer.Builtin, value, null) : Resolution.None;
    }

    /// <summary>
    /// The built-in the loop compiler may bind <paramref name="name"/> to, or null. The layer must
    /// hold it under that name with nothing in the workspace walk ahead of it, and either no file or
    /// private function claims the name (the index says, without a disk probe) or the dispatch table
    /// keeps the built-in for every pattern the loop can produce — the register file holds doubles
    /// and logicals, and a call has one or two of them; a bare constant is the no-argument pattern.
    /// Whatever the compiled loop does, the walk would do the same, byte for byte (ADR 0099).
    /// </summary>
    public BuiltinFunction? CompiledBuiltin(string name, JgsEnvironment env, bool bare)
    {
        if (Lookup(name, env) is not { Layer: ResolutionLayer.Builtin } held
            || held.Value.AsCallable is not BuiltinFunction builtin
            || builtin.Name != name)
        {
            return null;
        }

        if (!_interpreter.Dialect.IsMatlab || !IsShadowed(name))
        {
            return builtin;
        }

        return JgsDispatchTable.KeepsBuiltinFor(name, bare ? CompiledBarePatterns : CompiledCallPatterns)
            ? builtin
            : null;
    }

    /// <summary>The built-in of that name in the layer, if any — what a handle keeps beside its capture.</summary>
    public JgsValue? BuiltinOf(string name)
    {
        JgsEnvironment globals = _interpreter.Globals;
        return globals.HasBuiltinLayer && globals.Builtins.TryGet(name, out JgsValue value) ? value : null;
    }

    /// <summary>
    /// Calls a named handle: the same layer walk a written call makes, with what the handle captured
    /// standing in for the layers it captured. A nested or local capture answers whatever the
    /// arguments are; otherwise the built-in method table is asked first (a double sent to <c>@max</c>
    /// reaches the built-in even though the handle captured <c>max.m</c>), then a private capture, a
    /// user method on the dominant object, a folder capture, and the built-in last.
    /// </summary>
    public JgsValue[] InvokeHandle(NamedHandle handle, IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        IJgsCallable target = HandleTarget(handle, arguments);
        return target is IJgsMultiCallable several
            ? several.CallMultiple(arguments, wanted, line, column)
            : [target.Call(arguments, line, column)];
    }

    private IJgsCallable HandleTarget(NamedHandle handle, IReadOnlyList<JgsValue> arguments)
    {
        if (!_interpreter.Dialect.IsMatlab || handle.Layer is ResolutionLayer.Nested or ResolutionLayer.Local)
        {
            return handle.Captured;
        }

        JgsValue? dominant = DominantObject(arguments);
        if (handle.Layer == ResolutionLayer.Builtin)
        {
            // Nothing captured but the built-in: only a user method can take the call from it.
            return dominant is { } instance && _interpreter.TryUserMethod(handle.Name, instance, out IJgsCallable? method)
                ? method
                : handle.Captured;
        }

        if (dominant is null)
        {
            return handle.Builtin is { } builtin
                && JgsDispatchTable.KeepsBuiltin(handle.Name, arguments, _interpreter.Dialect)
                ? builtin
                : handle.Captured;
        }

        if (handle.Layer == ResolutionLayer.Private)
        {
            return handle.Captured;
        }

        return _interpreter.TryUserMethod(handle.Name, dominant, out IJgsCallable? dispatched)
            ? dispatched
            : handle.Captured;
    }

    /// <summary>The leftmost user object among the arguments — the one a user method dispatches on — or null.</summary>
    private JgsValue? DominantObject(IReadOnlyList<JgsValue> arguments)
    {
        if (!_interpreter.AnyClasses)
        {
            return null;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Type == JgsType.Object)
            {
                return arguments[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether some file the folders or the running file's <c>private/</c> hold claims
    /// <paramref name="name"/> — the one question a built-in call asks on its way, answered from the
    /// index without a disk probe.
    /// </summary>
    private bool IsShadowed(string name)
    {
        if (_interpreter.FunctionPath is not { } path)
        {
            return false;
        }

        // Both sets are empty in almost every session; the count check spares the name a hash.
        IReadOnlySet<string> shadowing = path.Index.Shadowing;
        if (shadowing.Count > 0 && shadowing.Contains(name))
        {
            return true;
        }

        return _interpreter.CurrentPrivateStems(path.Index) is { Count: > 0 } stems && stems.Contains(name);
    }

    /// <summary>The private layer: <c>private/name.m</c> beside the running file, loaded by the path.</summary>
    private bool TryPrivate(string name, out Resolution resolution)
    {
        if (_interpreter.CurrentPrivateFolder is { } folder && _interpreter.FunctionPath is { } path
            && path.TryResolvePrivate(folder, name, out JgsValue value, out string? file))
        {
            resolution = new Resolution(ResolutionLayer.Private, value, file);
            return true;
        }

        resolution = Resolution.None;
        return false;
    }

    /// <summary>A file on the search path: the current folder before the <c>addpath</c> folders.</summary>
    private Resolution FromFile(string name)
    {
        if (_interpreter.FunctionPath is { } path
            && path.TryResolve(name, out JgsValue value, out string? file, out bool added))
        {
            return new Resolution(added ? ResolutionLayer.PathFolder : ResolutionLayer.CurrentFolder, value, file);
        }

        return Resolution.None;
    }

    /// <summary>
    /// Which layer a binding the walk found belongs to: the built-in layer's own scope holds
    /// built-ins; a function definition anywhere else is nested when its scope sits inside a call
    /// frame and local otherwise; anything else is a variable.
    /// </summary>
    private static Resolution Classify(string name, JgsEnvironment scope, JgsValue value)
    {
        if (scope.IsBuiltinLayer)
        {
            return new Resolution(ResolutionLayer.Builtin, value, null);
        }

        if (!scope.DeclaresFunctionLocally(name))
        {
            return new Resolution(ResolutionLayer.Bound, value, null);
        }

        ResolutionLayer layer = scope.IsInsideCall ? ResolutionLayer.Nested : ResolutionLayer.Local;
        string? file = value.AsCallable is UserFunction user ? user.Declaration.SourceId : null;
        return new Resolution(layer, value, file);
    }
}
