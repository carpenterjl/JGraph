namespace JGraph.Scripting.Jgs;

/// <summary>
/// The JGS language choices a user is allowed to make. Only these two: the rest of the language is fixed,
/// and MATLAB (<c>.m</c>) runs ignore this type entirely so that a MATLAB script behaves the same
/// everywhere. Hosts read these from the user's settings and hand them to <see cref="JgsScriptEngine"/>.
/// </summary>
/// <param name="RequireLet">When true (the default) a first assignment must say <c>let</c>, which catches
/// typos; when false a plain <c>x = 1</c> declares <c>x</c>, as MATLAB does.</param>
/// <param name="IndexBase">The index of the first element: 0 (the default, ADR 0028) or 1.</param>
public sealed record JgsLanguageOptions(bool RequireLet = true, int IndexBase = 0)
{
    /// <summary>The shipped defaults: <c>let</c> required, 0-based indexing.</summary>
    public static JgsLanguageOptions Default { get; } = new();

    /// <summary>
    /// The same options with any out-of-range index base clamped back to 0, so a hand-edited settings
    /// file cannot put the interpreter into a state no rule covers.
    /// </summary>
    public JgsLanguageOptions Sanitized() => IndexBase is 0 or 1 ? this : this with { IndexBase = 0 };
}
