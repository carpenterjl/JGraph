namespace JGraph.Scripting.Jgs;

/// <summary>
/// Which layer of MATLAB's search order answered a name (M145). The order of the members is the
/// order the layers are asked in once the flip lands; until then the resolver walks them in the
/// order the interpreter has always used, and the value here only says where the answer came from.
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

    /// <summary>A file in the current folder, the script's folder or the workspace root.</summary>
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
    /// Whether the name is a function <em>definition</em> — a nested, local or file function or a
    /// built-in — as opposed to a variable, one holding a handle included. A definition is what a
    /// bare mention calls; a variable is what it reads.
    /// </summary>
    public bool IsFunctionDefinition =>
        Layer is not (ResolutionLayer.None or ResolutionLayer.Bound) && Value.Type == JgsType.Function;
}

/// <summary>
/// The one place that decides what a name means (M145, step 4). Every site that used to look a name
/// up for itself — a callee, a bare mention, <c>@name</c>, <c>feval</c> and <c>str2func</c> by name,
/// <c>which</c>, <c>exist</c>, <c>nargin('f')</c>, the loop JIT's guard — asks here, in one of four
/// modes, and reads the answer's layer rather than working it out again.
/// </summary>
/// <remarks>
/// <para>
/// The order implemented is the one the interpreter has always had: the nearest binding in the
/// workspace walk, variable or nested function, then the running file's own functions (read from
/// the file's <see cref="FunctionFile"/>, where step 5 moved them out of the workspace), then a
/// built-in, then a file on the search path. Files above built-ins — the order MATLAB has and the
/// point of the milestone — is a later step's flip; it changes only this class and the layers a
/// handle walks, which is why the sites were routed through here first.
/// </para>
/// <para>
/// <see cref="Invoke"/> is asked <em>before</em> a call's arguments are evaluated, because what the
/// parentheses mean depends on the answer: a bound array is indexed with the unevaluated arguments
/// (<c>A(end)</c>, <c>A(:, 2)</c> need the index context), a bound handle is called, and only a
/// function definition has its arguments evaluated and handed over. The caller keeps that
/// classification; this class only says what the name is.
/// </para>
/// </remarks>
internal sealed class JgsNameResolver
{
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
    /// definition to call with no arguments.
    /// </summary>
    public Resolution Value(string name, JgsEnvironment env) => Resolve(name, env);

    /// <summary>
    /// Invoke mode, phase one: <c>f(x)</c>, <c>[a, b] = f(x)</c>, <c>feval('f', x)</c>, with no
    /// argument evaluated yet. A bound value for the caller to index, apply or call; else the
    /// function definition to call with the evaluated arguments.
    /// </summary>
    public Resolution Invoke(string name, JgsEnvironment env) => Resolve(name, env);

    /// <summary>
    /// Handle mode: <c>@f</c> and <c>str2func('f')</c>. Never a variable — <c>max = 7; @max</c> is the
    /// function, as it always was — so the walk sees function definitions only, then the files.
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

        if (_interpreter.TryGetFileFunction(name, out JgsValue local, out string file))
        {
            return new Resolution(ResolutionLayer.Local, local, file);
        }

        return scope is not null ? new Resolution(ResolutionLayer.Builtin, value, null) : FromFile(name);
    }

    /// <summary>
    /// Query mode: <c>which</c>, <c>exist</c>, <c>nargin('f')</c>. Every layer holding the name as a
    /// function, in the order they would be asked; empty when none does.
    /// </summary>
    public IEnumerable<Resolution> Query(string name, JgsEnvironment env)
    {
        Resolution held = Lookup(name, env);
        if (held.Found && held.Value.Type == JgsType.Function)
        {
            yield return held;
        }

        Resolution file = FromFile(name);
        if (file.Found)
        {
            yield return file;
        }
    }

    /// <summary>
    /// Everything but the disk: the workspace walk — a variable or a nested function, honouring a
    /// <c>global</c> declaration that redirects the name — then the running file's own functions,
    /// then the built-in layer. This is what a statement asks about its callee before deciding how
    /// to run it, and what the loop JIT asks about every name it wants to compile: neither may
    /// touch the disk.
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

        // No workspace on the walk holds the name: the file's own functions come before the built-in
        // the walk ended on, exactly as a script's local functions beat built-ins when they lived in
        // the base workspace.
        if (_interpreter.TryGetFileFunction(name, out JgsValue local, out string file))
        {
            return new Resolution(ResolutionLayer.Local, local, file);
        }

        return scope is not null ? new Resolution(ResolutionLayer.Builtin, value, null) : Resolution.None;
    }

    /// <summary>
    /// Calls a named handle: the same walk a written call makes, with what the handle captured
    /// standing in for the layers it captured. Under the order in force that is the captured target
    /// itself; the built-in method and user method layers a handle walks between its captures are
    /// what the flip adds here.
    /// </summary>
    public JgsValue[] InvokeHandle(NamedHandle handle, IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        IJgsCallable target = handle.Captured;
        return target is IJgsMultiCallable several
            ? several.CallMultiple(arguments, wanted, line, column)
            : [target.Call(arguments, line, column)];
    }

    private Resolution Resolve(string name, JgsEnvironment env)
    {
        Resolution held = Lookup(name, env);
        return held.Found ? held : FromFile(name);
    }

    /// <summary>A file on the search path — the last thing tried, in the MATLAB dialect only.</summary>
    private Resolution FromFile(string name)
    {
        if (_interpreter.Dialect.IsMatlab && _interpreter.FunctionPath is { } path
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
