using System.Diagnostics.CodeAnalysis;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A lexical scope: a set of named variables plus a link to its enclosing scope. Lookups and assignments
/// walk outward to the enclosing scopes; declarations always create a binding in the innermost scope.
/// </summary>
internal sealed class JgsEnvironment
{
    private readonly Dictionary<string, JgsValue> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _functionBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JgsValue> _functionDefinitions = new(StringComparer.Ordinal);
    private readonly JgsEnvironment? _parent;

    // Names a 'global' statement in this scope bound to the global workspace. Null until one does,
    // which is almost always: allocating a set per call frame for a statement most functions never
    // write would cost every call for the few that do.
    private HashSet<string>? _globalNames;

    // The built-in layer this scope sits under, inherited from the parent; null for a chain built
    // without one (the interpreter's global-variable workspace, and bare scopes in tests).
    private readonly JgsBuiltinLayer? _layer;

    /// <summary>Creates a scope nested inside <paramref name="parent"/> (null for the global scope).</summary>
    public JgsEnvironment(JgsEnvironment? parent = null)
    {
        _parent = parent;
        _layer = parent?._layer;
    }

    /// <summary>Creates the root scope of <paramref name="layer"/>, the one holding the built-ins.</summary>
    internal JgsEnvironment(JgsBuiltinLayer layer)
    {
        _layer = layer;
        IsBuiltinLayer = true;
    }

    /// <summary>The bindings declared directly in this scope (not the enclosing scopes).</summary>
    public IReadOnlyDictionary<string, JgsValue> Locals => _values;

    /// <summary>The enclosing scope, or null for the global scope.</summary>
    public JgsEnvironment? Parent => _parent;

    /// <summary>
    /// Whether this scope is the built-in layer's own — the outermost scope of a workspace chain,
    /// holding every built-in and nothing else. A boundary for assignment and for <c>global</c>, the
    /// way a call frame is: a write that walks this far has found no variable, and declares in the
    /// scope it started from rather than here.
    /// </summary>
    public bool IsBuiltinLayer { get; }

    /// <summary>
    /// The built-in layer under this scope, the one place a built-in may be registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">The chain was built without a layer.</exception>
    public JgsBuiltinLayer Builtins =>
        _layer ?? throw new InvalidOperationException("This scope has no built-in layer under it.");

    /// <summary>
    /// The base workspace this scope belongs to — the scope a script's variables go into. Read from
    /// the layer by identity, because the outermost scope of the chain is the layer, not the base.
    /// A chain built without a layer answers its outermost scope, as before.
    /// </summary>
    public JgsEnvironment Base
    {
        get
        {
            if (_layer is not null)
            {
                return _layer.Base;
            }

            JgsEnvironment scope = this;
            while (scope._parent is not null)
            {
                scope = scope._parent;
            }

            return scope;
        }
    }

    /// <summary>
    /// Whether this scope is a call's own workspace, which an assignment may not write out of.
    /// </summary>
    /// <remarks>
    /// A MATLAB function has a workspace of its own: writing <c>x</c> inside it makes the function's
    /// <c>x</c>, whatever the caller happens to be holding under that name. Without this the walk
    /// below found the caller's binding and wrote into it, so a helper with a local named <c>n</c>
    /// silently changed the script's <c>n</c> — invisible until the two happened to collide. Found in
    /// M68, where a method's locals are ordinary short words and collisions stop being rare.
    /// <para>
    /// A <em>nested</em> function is the deliberate exception: it shares its parent's variables, which
    /// is the whole point of nesting. It is told apart by where it was defined — a frame opened inside
    /// another frame is nested, and only a frame opened outside every frame is a boundary.
    /// </para>
    /// Block scopes (an <c>if</c> or <c>for</c> body) are never boundaries, so a script assigning
    /// inside a loop still writes the script's own variable. Neither is a JGS <c>fn</c>: JGS is
    /// lexically scoped and its closures write to what they captured on purpose.
    /// </remarks>
    public bool IsCallBoundary { get; init; }

    /// <summary>
    /// Whether this scope is an anonymous function's workspace: the snapshot taken when the handle
    /// was made, and the frame a call of it binds its parameters in. MATLAB calls that a static
    /// workspace — a captured name can be changed, a new one cannot be added — and
    /// <c>assignin('caller', …)</c> from a function the body called refuses with those words.
    /// </summary>
    public bool IsStaticWorkspace { get; init; }

    /// <summary>
    /// Whether this scope is a call frame or sits inside one — where a nested function lives, as
    /// opposed to a script's, a file's or a class's own scope.
    /// </summary>
    public bool IsInsideCall
    {
        get
        {
            for (JgsEnvironment? scope = this; scope is not null && !scope.IsBuiltinLayer; scope = scope._parent)
            {
                if (scope.IsCallBoundary)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Declares (or redeclares) <paramref name="name"/> in this scope with <paramref name="value"/>.</summary>
    public void Declare(string name, JgsValue value)
    {
        ThrowIfSealedLayer(name);
        _values[name] = value;
        _functionBindings.Remove(name);
    }

    /// <summary>Registers a function definition, as distinct from a variable holding its handle.</summary>
    public void DeclareFunction(string name, JgsValue value)
    {
        ThrowIfSealedLayer(name);
        _values[name] = value;
        _functionBindings.Add(name);
        _functionDefinitions[name] = value;
    }

    // The layer takes declarations only through JgsBuiltinLayer.Register, and only until it is
    // sealed. A declaration reaching it any other way is a walk that went one scope too far.
    private void ThrowIfSealedLayer(string name)
    {
        if (IsBuiltinLayer && _layer!.IsSealed)
        {
            throw new InvalidOperationException(
                $"'{name}' cannot be declared in the built-in layer: it is sealed.");
        }
    }

    private void ThrowIfLayer(string operation)
    {
        if (IsBuiltinLayer)
        {
            throw new InvalidOperationException($"The built-in layer cannot be {operation}.");
        }
    }

    /// <summary>Resolves @name without confusing a shadowing variable with the function.</summary>
    public bool TryGetFunction(string name, out JgsValue value)
    {
        for (JgsEnvironment? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._functionDefinitions.TryGetValue(name, out JgsValue? found))
            {
                value = found;
                return true;
            }
        }

        value = JgsValue.Null;
        return false;
    }

    /// <summary>
    /// Resolves @name as <see cref="TryGetFunction(string, out JgsValue)"/> does, and says which scope
    /// holds the definition — the resolver reads the layer off the scope.
    /// </summary>
    public bool TryGetFunction(string name, [NotNullWhen(true)] out JgsEnvironment? scope, out JgsValue value)
    {
        for (JgsEnvironment? candidate = this; candidate is not null; candidate = candidate._parent)
        {
            if (candidate._functionDefinitions.TryGetValue(name, out JgsValue? found))
            {
                scope = candidate;
                value = found;
                return true;
            }
        }

        scope = null;
        value = JgsValue.Null;
        return false;
    }

    /// <summary>Whether this scope itself binds <paramref name="name"/> as a function definition.</summary>
    public bool DeclaresFunctionLocally(string name) => _functionBindings.Contains(name);

    /// <summary>
    /// Whether <paramref name="name"/> is bound somewhere in the static workspace this scope belongs
    /// to — the frame and the snapshot under it, and no further — so an <c>assignin</c> into it can
    /// tell a captured name it may change from a new one it may not add.
    /// </summary>
    public bool IsBoundWithinStaticWorkspace(string name)
    {
        for (JgsEnvironment? scope = this; scope is not null && scope.IsStaticWorkspace; scope = scope._parent)
        {
            if (scope._values.ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the nearest binding is a function definition rather than a variable.</summary>
    public bool IsFunctionBinding(string name)
    {
        for (JgsEnvironment? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._values.ContainsKey(name))
            {
                return scope._functionBindings.Contains(name);
            }
        }

        return false;
    }

    /// <summary>
    /// Records that a <c>global</c> statement in this scope binds <paramref name="name"/> to the
    /// global workspace, so reads and writes of it here reach the shared variable.
    /// </summary>
    public void DeclareGlobal(string name)
    {
        ThrowIfLayer("the scope of a global declaration");
        (_globalNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
    }

    /// <summary>
    /// Whether a <c>global</c> declaration reaching this scope binds <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// The declaration belongs to the workspace that made it, so the walk stops where an assignment's
    /// does — at a call boundary. A function that never wrote <c>global x</c> keeps its own <c>x</c>
    /// however many other functions declared one, which is what MATLAB means by the statement and the
    /// reason it can be written at all. Until M69 the interpreter held one run-wide set instead, so the
    /// first function to declare a name silently rewired it for every scope for the rest of the session.
    /// A nested function is the same exception it is for assignment: it shares its parent's workspace,
    /// declarations included.
    /// </remarks>
    public bool IsGlobal(string name)
    {
        for (JgsEnvironment? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._globalNames is not null && scope._globalNames.Contains(name))
            {
                return true;
            }

            if (scope.IsCallBoundary || scope.IsBuiltinLayer)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes every binding in this scope except those still holding the exact value recorded in
    /// <paramref name="pristine"/> — restoring it to the state <paramref name="pristine"/> was captured
    /// from. This is what <c>clear</c> means at an interactive prompt: user variables go, the built-ins
    /// stay, and a rebound built-in reverts. It mutates the scope in place rather than returning a new
    /// one because a live interpreter and every closure it has created hold a reference to this
    /// instance.
    /// </summary>
    public void RetainOnly(IReadOnlyDictionary<string, JgsValue> pristine)
    {
        ThrowIfLayer("cleared");
        foreach (string name in _values.Keys.ToList())
        {
            if (!pristine.TryGetValue(name, out JgsValue? original))
            {
                _values.Remove(name);
                _functionDefinitions.Remove(name);
                _functionBindings.Remove(name);
            }
            else if (!ReferenceEquals(original, _values[name]))
            {
                _values[name] = original;
                if (original.Type == JgsType.Function) _functionBindings.Add(name);
            }
        }

        // A built-in the user deleted outright (possible via clear in a nested call) comes back too.
        foreach ((string name, JgsValue value) in pristine)
        {
            _values.TryAdd(name, value);
            if (value.Type == JgsType.Function) _functionBindings.Add(name);
        }
    }

    /// <summary>
    /// Drops <paramref name="name"/> from this scope, reverting it to the binding recorded in
    /// <paramref name="pristine"/> when it had one — the named form of <c>clear a b</c>.
    /// </summary>
    public void Forget(string name, IReadOnlyDictionary<string, JgsValue> pristine)
    {
        ThrowIfLayer("cleared");
        if (pristine.TryGetValue(name, out JgsValue? original))
        {
            _values[name] = original;
            if (original.Type == JgsType.Function) _functionBindings.Add(name);
        }
        else
        {
            _values.Remove(name);
            if (_functionBindings.Contains(name)) _functionDefinitions.Remove(name);
            _functionBindings.Remove(name);
        }
    }

    /// <summary>Whether <paramref name="name"/> resolves in this scope or any enclosing scope.</summary>
    public bool Contains(string name) =>
        _values.ContainsKey(name) || (_parent?.Contains(name) ?? false);

    /// <summary>
    /// Whether <paramref name="name"/> was declared in this scope itself, without looking outward.
    /// </summary>
    /// <remarks>
    /// The question a call frame asks about its own parameters. Looking outward answers it wrongly:
    /// every builtin is declared in the outermost scope, so a parameter named after one — <c>factor</c>,
    /// <c>size</c>, <c>mode</c> — looked bound even when the caller left it out, and its default was
    /// skipped in favour of the builtin's function value.
    /// </remarks>
    public bool DeclaresLocally(string name) => _values.ContainsKey(name);

    /// <summary>Looks up <paramref name="name"/>, walking outward. Returns false when it is not defined.</summary>
    public bool TryGet(string name, out JgsValue value)
    {
        for (JgsEnvironment? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._values.TryGetValue(name, out JgsValue? found))
            {
                value = found;
                return true;
            }
        }

        value = JgsValue.Null;
        return false;
    }

    /// <summary>
    /// Looks <paramref name="name"/> up as <see cref="TryGet"/> does, and says which scope holds it —
    /// the built-in layer, a call frame, the base workspace — which is what tells a built-in from a
    /// local function from a variable without a second walk.
    /// </summary>
    public bool TryGetScope(string name, [NotNullWhen(true)] out JgsEnvironment? scope, out JgsValue value)
    {
        for (JgsEnvironment? candidate = this; candidate is not null; candidate = candidate._parent)
        {
            if (candidate._values.TryGetValue(name, out JgsValue? found))
            {
                scope = candidate;
                value = found;
                return true;
            }
        }

        scope = null;
        value = JgsValue.Null;
        return false;
    }

    /// <summary>
    /// Assigns to an existing variable, updating the nearest scope that declares it. Returns false when the
    /// variable is not declared anywhere (the caller reports the error with a source location).
    /// </summary>
    /// <remarks>
    /// The built-in layer is never assigned into: <c>max = 7</c> at the top level finds no variable
    /// and so declares one in the base workspace, hiding the built-in until <c>clear max</c> drops it.
    /// </remarks>
    public bool TryAssign(string name, JgsValue value)
    {
        for (JgsEnvironment? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope.IsBuiltinLayer)
            {
                return false; // a built-in is not a variable; see IsBuiltinLayer
            }

            if (scope._values.ContainsKey(name))
            {
                scope._values[name] = value;
                scope._functionBindings.Remove(name);
                return true;
            }

            if (scope.IsCallBoundary)
            {
                return false; // a call's workspace ends here; see IsCallBoundary
            }
        }

        return false;
    }
}
