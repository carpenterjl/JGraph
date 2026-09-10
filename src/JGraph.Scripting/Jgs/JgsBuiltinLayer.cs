namespace JGraph.Scripting.Jgs;

/// <summary>
/// The scope every built-in lives in, and the only place one may be declared. Sits under the base
/// workspace as its parent, so a name the workspace does not bind still resolves to the built-in
/// by the ordinary outward walk — but the workspace itself never holds a built-in, which is what
/// lets <c>clear</c> drop a user's <c>max</c> and have the built-in show through with no snapshot
/// to restore it from.
/// </summary>
/// <remarks>
/// Until M145 every registrar declared its built-ins straight into the base workspace, the same
/// dictionary the script's variables and hoisted functions went into, and <c>clear</c> worked by
/// diffing that dictionary against a "pristine" copy taken after the last registrar ran. That put
/// the built-in table and the user's workspace in one bag: a name could be looked up but never
/// asked <em>which</em> it was, and a walk to the outermost scope found the workspace, which the
/// debugger relied on. This layer separates the two. Every registrar — <c>CreateGlobals</c>, the
/// interpreter's operator functions, <c>run</c>, the eval family, the session builtins,
/// <c>save</c>/<c>load</c>, <c>clear</c>/<c>whos</c>, the REPL's copies of the same — writes here
/// through <see cref="Register"/>, and once the last of them has run the owner calls
/// <see cref="Seal"/>: from then on nothing can land in built-in storage, so a later code path that
/// walks too far (a prompt assignment, a <c>global</c> statement, a rebind) fails loudly rather than
/// silently rebinding a built-in for the rest of the session.
/// <para>
/// The layer is a registration destination, not a workspace. Registrars that also <em>read</em> the
/// environment they are handed (<c>feval</c>, <c>exist</c>, <c>which</c>) keep reading the base
/// workspace, whose outward walk reaches this layer last.
/// </para>
/// </remarks>
internal sealed class JgsBuiltinLayer
{
    private readonly JgsEnvironment _root;

    /// <summary>Creates an empty layer and the base workspace that sits directly on it.</summary>
    public JgsBuiltinLayer()
    {
        _root = new JgsEnvironment(this);
        Base = new JgsEnvironment(_root);
    }

    /// <summary>The scope holding the built-ins; the parent of <see cref="Base"/>.</summary>
    public JgsEnvironment Root => _root;

    /// <summary>
    /// The base workspace — <c>evalin('base', …)</c>'s workspace, the scope a script's variables and
    /// hoisted functions go into. Held here by identity so that nothing has to find it by walking to
    /// the outermost scope, which is this layer and not the workspace.
    /// </summary>
    public JgsEnvironment Base { get; }

    /// <summary>Whether registration has closed; see <see cref="Seal"/>.</summary>
    public bool IsSealed { get; private set; }

    /// <summary>The built-ins by name, as registered (constants included).</summary>
    public IReadOnlyDictionary<string, JgsValue> Entries => _root.Locals;

    /// <summary>Declares (or redeclares) the built-in function <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">The layer is sealed.</exception>
    public void Register(string name, JgsValue value)
    {
        ThrowIfSealed(name);
        _root.DeclareFunction(name, value);
    }

    /// <summary>
    /// Declares a built-in that is a value rather than a function — <c>pi</c>, <c>i</c>,
    /// <c>newline</c>, the <c>containers</c> namespace — so that mentioning its name yields the value
    /// itself, and <c>@pi</c> does not resolve.
    /// </summary>
    /// <exception cref="InvalidOperationException">The layer is sealed.</exception>
    public void RegisterConstant(string name, JgsValue value)
    {
        ThrowIfSealed(name);
        _root.Declare(name, value);
    }

    /// <summary>Whether a built-in named <paramref name="name"/> is registered, and which value it is.</summary>
    public bool TryGet(string name, out JgsValue value) => _root.Locals.TryGetValue(name, out value!);

    /// <summary>
    /// Closes registration. The owner calls this once, after the last registrar; every later
    /// declaration into the layer throws.
    /// </summary>
    public void Seal() => IsSealed = true;

    private void ThrowIfSealed(string name)
    {
        if (IsSealed)
        {
            throw new InvalidOperationException(
                $"The built-in layer is sealed; '{name}' cannot be registered after the workspace is built.");
        }
    }
}
