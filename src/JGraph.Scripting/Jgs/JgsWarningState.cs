namespace JGraph.Scripting.Jgs;

/// <summary>
/// What MATLAB's <c>warning</c> keeps between calls: which identifiers have been turned off, the
/// default that applies to every other one, and the message and identifier of the last warning
/// raised — which <c>lastwarn</c> reports, and which is recorded whether or not the warning was
/// shown (measured R2025b: a suppressed warning still becomes <c>lastwarn</c>).
/// </summary>
/// <remarks>
/// It lives on the script host rather than in the interpreter because <c>warning</c> is declared
/// with the ordinary builtins, which have the host and not the interpreter; <c>lastwarn</c> is
/// declared beside <c>eval</c>, which has both. One object, reachable from either side, is what
/// lets the two agree without one wrapping the other.
/// </remarks>
internal sealed class JgsWarningState
{
    // The identifiers a script has set explicitly, in the order it set them: that order is what a
    // query of 'all' lists, after the default.
    private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    public JgsWarningState()
    {
        // The two settings start where a fresh R2025b session has them (measured): backtrace on,
        // which the default already says, and verbose off. Every identifier — MATLAB:divideByZero
        // included, the retired warning legacy scripts query and switch off before dividing by
        // zero on purpose — starts on.
        Set("verbose", false);
    }

    /// <summary>The state an identifier that has never been set explicitly is in.</summary>
    public bool DefaultOn { get; private set; } = true;

    /// <summary>The message of the last warning raised, shown or not.</summary>
    public string LastMessage { get; set; } = string.Empty;

    /// <summary>The identifier of the last warning raised, or empty when it had none.</summary>
    public string LastIdentifier { get; set; } = string.Empty;

    /// <summary>Whether a warning under <paramref name="identifier"/> would be shown.</summary>
    public bool IsOn(string identifier) =>
        _states.TryGetValue(identifier, out bool on) ? on : DefaultOn;

    /// <summary>
    /// Turns one identifier on or off; <c>'all'</c> resets the default and forgets every explicit
    /// setting, which is what MATLAB's <c>warning('off', 'all')</c> does to the table.
    /// </summary>
    public void Set(string identifier, bool on)
    {
        if (identifier == "all")
        {
            DefaultOn = on;
            _states.Clear();
            _order.Clear();
            return;
        }

        if (_states.TryAdd(identifier, on))
        {
            _order.Add(identifier);
        }
        else
        {
            _states[identifier] = on;
        }
    }

    /// <summary>Every explicit setting, in the order it was made.</summary>
    public IEnumerable<(string Identifier, bool On)> Explicit()
    {
        foreach (string identifier in _order)
        {
            yield return (identifier, _states[identifier]);
        }
    }

    /// <summary>Records what the last warning was, for <c>lastwarn</c>.</summary>
    public void Record(string identifier, string message)
    {
        LastIdentifier = identifier;
        LastMessage = message;
    }
}
