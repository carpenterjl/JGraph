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

    /// <summary>Whether <paramref name="name"/> resolves in this scope or any enclosing scope.</summary>
    public bool Contains(string name) =>
        _values.ContainsKey(name) || (_parent?.Contains(name) ?? false);

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
