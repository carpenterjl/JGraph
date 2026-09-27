namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// One scope's imports (interop plan, stage 3, ADR 0176): the names its <c>import</c> statements
/// gave, in the order written, and the names the function form <c>import('…')</c> added while it
/// ran.
/// </summary>
/// <remarks>
/// <para>
/// R2025b resolves a function's imports when it parses the file, so an import applies to the whole
/// body wherever it stands — before its line, and in a branch never taken — and the function form
/// changes only the list: <c>L = import('System.Math')</c> answers <c>{'System.Math'}</c> and
/// <c>Math.Max(1, 2)</c> after it is still unresolved (net_import). The base workspace resolves as
/// it goes, so there both kinds resolve (<see cref="Dynamic"/>).
/// </para>
/// </remarks>
internal sealed class NetImports(bool dynamic)
{
    private readonly List<string> _written = [];
    private readonly List<string> _added = [];

    /// <summary>Whether names added while running resolve too — the base workspace and a script.</summary>
    public bool Dynamic { get; } = dynamic;

    /// <summary>The names that resolve, in the order they were made; a later one wins over an earlier.</summary>
    public IEnumerable<string> Resolving => Dynamic ? _added.Concat(_written) : _written;

    /// <summary>
    /// What <c>import</c> lists: the names added while running, then the written ones, each once
    /// (R2025b lists <c>{'System.Math'; 'System.IO.*'}</c> for a function that wrote the second and
    /// then called <c>import('System.Math')</c>; probe3).
    /// </summary>
    public IEnumerable<string> Listed => _added.Concat(_written).Distinct(StringComparer.Ordinal);

    /// <summary>Whether this scope holds no name.</summary>
    public bool IsEmpty => _written.Count == 0 && _added.Count == 0;

    /// <summary>Adds a name an <c>import</c> statement wrote.</summary>
    public void Write(string name)
    {
        if (!_written.Contains(name, StringComparer.Ordinal))
        {
            _written.Add(name);
        }
    }

    /// <summary>Adds a name the function form gave.</summary>
    public void Add(string name)
    {
        if (!_added.Contains(name, StringComparer.Ordinal))
        {
            _added.Add(name);
        }
    }

    /// <summary>Forgets every name (<c>clear import</c> at the prompt).</summary>
    public void Clear()
    {
        _written.Clear();
        _added.Clear();
    }

    /// <summary>Whether an imported name ends in <c>.*</c>.</summary>
    public static bool IsWildcard(string name) => name.EndsWith(".*", StringComparison.Ordinal);

    /// <summary>The name an explicit import binds: its last dotted piece.</summary>
    public static string ShortName(string name) => name[(name.LastIndexOf('.') + 1)..];
}

/// <summary>What an imported name reaches: a type, a static method of one, or a namespace.</summary>
/// <param name="Explicit">Whether an explicit import gave it (above nested and local functions) or a wildcard (below them).</param>
/// <param name="Type">The type, for a type or a method.</param>
/// <param name="Method">The static method's name, for a method.</param>
/// <param name="Namespace">The full namespace, for a namespace a wildcard reached.</param>
internal readonly record struct NetImported(bool Explicit, Type? Type, string? Method, string? Namespace);
