namespace JGraph.Scripting.Jgs;

/// <summary>
/// The private half of a listener (V6, ADR 0167, appendix A #106 and #108): what <c>addlistener</c>
/// made, kept beside the <c>event.listener</c> or <c>event.proplistener</c> struct the script holds.
/// A listener is a handle: every alias is the one object, so its storage is its identity and a weak
/// table on that storage holds this exactly as long as the value lives — the source's list holds it
/// as long as the source does.
/// </summary>
/// <remarks>
/// A listener lives with its source (R2025b: <c>clear lh</c> leaves it firing; <c>delete(lh)</c>
/// ends it; deleting the source leaves it valid but with nothing to hear). The <c>listener</c>
/// function's shorter lifetime — ending with the last handle to it — is V10's business (#107); until
/// then both verbs make this.
/// </remarks>
internal sealed class JgsListener
{
    /// <summary>The struct the script holds — <c>lh</c>.</summary>
    public required JgsValue Value { get; init; }

    /// <summary>The object the listener is on.</summary>
    public required JgsObject Source { get; init; }

    /// <summary>The source as a value, which is what every callback is handed first (a handle: the one object).</summary>
    public required JgsValue SourceValue { get; init; }

    /// <summary>The run's host, where a callback's failure is reported as a warning.</summary>
    public required JGraphScriptGlobals Host { get; init; }

    /// <summary>
    /// The event listened for: a name the class declared, <c>ObjectBeingDestroyed</c>, or
    /// <c>PreSet</c>/<c>PostSet</c> for a property listener.
    /// </summary>
    public required string EventName { get; set; }

    /// <summary>For a property listener, the properties watched; null for an event listener.</summary>
    public IReadOnlyList<string>? Properties { get; init; }

    /// <summary>The function handle to call.</summary>
    public required JgsValue Callback { get; set; }

    /// <summary>The <c>Enabled</c> property: a disabled listener hears nothing and stays.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The <c>Recursive</c> property: whether the callback may run again underneath itself.</summary>
    public bool Recursive { get; set; }

    /// <summary><c>delete(lh)</c> has run: every property read or write refuses, and the source skips it.</summary>
    public bool Deleted { get; set; }

    /// <summary>How many of this listener's callbacks are on the stack, for <see cref="Recursive"/>.</summary>
    public int Depth { get; set; }

    /// <summary>Whether this is a <c>PreSet</c>/<c>PostSet</c> listener rather than an event listener.</summary>
    public bool IsProperty => Properties is not null;

    /// <summary>Whether this listener is a property listener on <paramref name="property"/>.</summary>
    public bool Watches(string property) =>
        Properties is { } watched && watched.Contains(property, StringComparer.Ordinal);
}
