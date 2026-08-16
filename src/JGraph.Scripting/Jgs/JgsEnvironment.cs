namespace JGraph.Scripting.Jgs;

/// <summary>
/// A lexical scope: a set of named variables plus a link to its enclosing scope. Lookups and assignments
/// walk outward to the enclosing scopes; declarations always create a binding in the innermost scope.
/// </summary>
internal sealed class JgsEnvironment
{
    private readonly Dictionary<string, JgsValue> _values = new(StringComparer.Ordinal);
    private readonly JgsEnvironment? _parent;

    /// <summary>Creates a scope nested inside <paramref name="parent"/> (null for the global scope).</summary>
    public JgsEnvironment(JgsEnvironment? parent = null) => _parent = parent;

    /// <summary>The bindings declared directly in this scope (not the enclosing scopes).</summary>
    public IReadOnlyDictionary<string, JgsValue> Locals => _values;

    /// <summary>The enclosing scope, or null for the global scope.</summary>
    public JgsEnvironment? Parent => _parent;

    /// <summary>Declares (or redeclares) <paramref name="name"/> in this scope with <paramref name="value"/>.</summary>
    public void Declare(string name, JgsValue value) => _values[name] = value;

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
        foreach (string name in _values.Keys.ToList())
        {
            if (!pristine.TryGetValue(name, out JgsValue? original))
            {
                _values.Remove(name);
            }
            else if (!ReferenceEquals(original, _values[name]))
            {
                _values[name] = original;
            }
        }

        // A built-in the user deleted outright (possible via clear in a nested call) comes back too.
        foreach ((string name, JgsValue value) in pristine)
        {
            _values.TryAdd(name, value);
        }
    }

    /// <summary>
    /// Drops <paramref name="name"/> from this scope, reverting it to the binding recorded in
    /// <paramref name="pristine"/> when it had one — the named form of <c>clear a b</c>.
    /// </summary>
    public void Forget(string name, IReadOnlyDictionary<string, JgsValue> pristine)
    {
        if (pristine.TryGetValue(name, out JgsValue? original))
        {
            _values[name] = original;
        }
        else
        {
            _values.Remove(name);
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
    /// Assigns to an existing variable, updating the nearest scope that declares it. Returns false when the
    /// variable is not declared anywhere (the caller reports the error with a source location).
    /// </summary>
    public bool TryAssign(string name, JgsValue value)
    {
        for (JgsEnvironment? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._values.ContainsKey(name))
            {
                scope._values[name] = value;
                return true;
            }
        }

        return false;
    }
}
