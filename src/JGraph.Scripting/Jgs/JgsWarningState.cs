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
    /// <summary>The state R2025b's <c>warning('error', id)</c> sets, as the query spells it.</summary>
    public const string ErrorState = "error";

    // The identifiers a script has set explicitly, in the order it set them: that order is what a
    // query of 'all' lists, after the default. The value is "on", "off" or "error" (open item 189:
    // R2025b's warning('error', id) is a third state, which turns the warning into an error).
    private readonly Dictionary<string, string> _states = new(StringComparer.Ordinal);
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

    /// <summary>
    /// Whether a warning under <paramref name="identifier"/> would be shown. An identifier in the
    /// error state counts as on here: a caller that cannot raise an error (a destructor, a callback's
    /// report) still shows the warning rather than swallowing it.
    /// </summary>
    public bool IsOn(string identifier) =>
        _states.TryGetValue(identifier, out string? state) ? state != "off" : DefaultOn;

    /// <summary>
    /// Whether <c>warning('error', id)</c> has turned warnings under <paramref name="identifier"/>
    /// into errors. Never true for an empty identifier: R2025b refuses <c>'all'</c> with the error
    /// state, so a warning without an identifier always stays a warning.
    /// </summary>
    public bool IsError(string identifier) =>
        identifier.Length > 0 && _states.TryGetValue(identifier, out string? state) && state == ErrorState;

    /// <summary>The state an identifier is in, as a query answers it: "on", "off" or "error".</summary>
    public string StateOf(string identifier) =>
        _states.TryGetValue(identifier, out string? state) ? state : DefaultOn ? "on" : "off";

    /// <summary>
    /// Turns one identifier on or off; <c>'all'</c> resets the default and forgets every explicit
    /// setting, which is what MATLAB's <c>warning('off', 'all')</c> does to the table.
    /// </summary>
    public void Set(string identifier, bool on) => SetState(identifier, on ? "on" : "off");

    /// <summary>
    /// Puts one identifier in "on", "off" or "error"; <c>'all'</c> takes only on or off and resets
    /// the table (measured R2025b: warning('on', 'all') forgets an identifier's error state too).
    /// </summary>
    public void SetState(string identifier, string state)
    {
        if (identifier == "all")
        {
            DefaultOn = state != "off";
            _states.Clear();
            _order.Clear();
            return;
        }

        if (_states.TryAdd(identifier, state))
        {
            _order.Add(identifier);
        }
        else
        {
            _states[identifier] = state;
        }
    }

    /// <summary>Every explicit setting, in the order it was made, with its state's word.</summary>
    public IEnumerable<(string Identifier, string State)> Explicit()
    {
        foreach (string identifier in _order)
        {
            yield return (identifier, _states[identifier]);
        }
    }

    /// <summary>Records what the last warning was, for <c>lastwarn</c>, and tells <see cref="Raised"/>.</summary>
    public void Record(string identifier, string message)
    {
        LastIdentifier = identifier;
        LastMessage = message;
        Raised?.Invoke(identifier, message);
    }

    /// <summary>
    /// Told of every warning as it is recorded, on the thread raising it: the debugger's <c>dbstop if warning</c>
    /// (App Designer window plan W11), which may pause the raising statement inside the call.
    /// </summary>
    public Action<string, string>? Raised { get; set; }
}
