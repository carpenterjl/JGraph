namespace JGraph.Scripting.Jgs;

/// <summary>
/// One scope's imports (interop plan, stage 3, ADR 0176; packages plan, stage P3, ADR 0309): the
/// names its <c>import</c> statements gave, in the order written, and the names the function form
/// <c>import('…')</c> added while it ran. An entry names a .NET type, static method or namespace,
/// or a package's member, class, class static or sub-package - which world is decided when the
/// name is looked up, not here.
/// </summary>
/// <remarks>
/// <para>
/// R2025b resolves a function's imports when it parses the file, so an import applies to the whole
/// body wherever it stands — before its line, and in a branch never taken — and the function form
/// changes only the list: <c>L = import('System.Math')</c> answers <c>{'System.Math'}</c> and
/// <c>Math.Max(1, 2)</c> after it is still unresolved (net_import). The base workspace resolves as
/// it goes, so there both kinds resolve (<see cref="Dynamic"/>). A script's imports are a list of
/// its own over its caller's (<see cref="Outer"/>), made for its run and dropped after it: the
/// script's statements see them and the caller's list never shows them (p3_imports, measured).
/// </para>
/// </remarks>
internal sealed class JgsImports(bool dynamic)
{
    private readonly List<string> _written = [];
    private readonly List<string> _added = [];

    /// <summary>Whether names added while running resolve too — the base workspace and a script.</summary>
    public bool Dynamic { get; } = dynamic;

    /// <summary>The list this one sits over - a script's over the workspace it runs in - or null.</summary>
    public JgsImports? Outer { get; init; }

    /// <summary>The names that resolve, in the order they were made.</summary>
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

/// <summary>
/// What an imported name reaches: a .NET type, a static method of one or a namespace (ADR 0176),
/// or a package's member or class, a class's static method or a sub-package (P3, ADR 0309).
/// </summary>
/// <param name="Explicit">Whether an explicit import gave it (above nested and local functions) or a wildcard (below them).</param>
/// <param name="Type">The .NET type, for a type or a method.</param>
/// <param name="Method">The .NET static method's name, for a method.</param>
/// <param name="Namespace">The full .NET namespace, for a namespace a wildcard reached.</param>
/// <param name="Member">A package member's callable - a function file, a script or a class's static method.</param>
/// <param name="Class">A class in a package.</param>
/// <param name="Package">The qualified name of a sub-package a wildcard reached in front of a dot.</param>
/// <param name="QualifiedName">The member's or class's dotted name, what a handle to it is called.</param>
/// <param name="File">The file the member or class was read from, what <c>which</c> answers.</param>
internal readonly record struct JgsImported(
    bool Explicit,
    Type? Type = null,
    string? Method = null,
    string? Namespace = null,
    JgsValue? Member = null,
    JgsClass? Class = null,
    string? Package = null,
    string? QualifiedName = null,
    string? File = null)
{
    /// <summary>Whether the import reached a package's world rather than .NET's.</summary>
    public bool IsPackage => Member is not null || Class is not null || Package is not null;
}
