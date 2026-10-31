namespace JGraph.Scripting.Designer;

/// <summary>
/// The argument sets an app runs with (App Designer window plan W8b, <c>RunArgumentsModel</c>): at most eight, the most recent first, and the empty set
/// always among them, last when it is not the most recent. Saved in an <c>.mlapp</c>'s model as <c>runConfigurations</c> when there is more than the empty set.
/// </summary>
public sealed class DesignRunArguments
{
    /// <summary>The most sets kept (<c>MAX_ARGUMENTS</c>).</summary>
    public const int MaxArguments = 8;

    /// <summary>Running with no arguments (<c>EMPTY_ARGUMENT_SET</c>).</summary>
    public const string Empty = "";

    private readonly List<string> _sets;

    /// <summary>The sets a file holds; none makes the empty set alone, as a new app has.</summary>
    public DesignRunArguments(IEnumerable<string>? held = null)
    {
        _sets = held is null ? [Empty] : [.. held];
        if (_sets.Count == 0)
        {
            _sets.Add(Empty);
        }
    }

    /// <summary>Every set, the most recent first (<c>getAllArguments</c>).</summary>
    public IReadOnlyList<string> All => _sets;

    /// <summary>What Run runs with (<c>getMostRecentArgumentSet</c>).</summary>
    public string MostRecent => _sets[0];

    /// <summary>Whether a set would run with no arguments at all (<c>_sendRunAppEvent</c>'s check).</summary>
    public static bool IsNoArguments(string set) => string.IsNullOrWhiteSpace(set);

    /// <summary>
    /// <paramref name="set"/> made the most recent (<c>setArgumentSet</c>): moved to the front or put there, the oldest going past eight, and the empty set put back
    /// when it went, last, the one before it going if the eight are full.
    /// </summary>
    /// <returns>Whether the app's kept sets changed, which is whether <paramref name="set"/> was not already the most recent.</returns>
    public bool Use(string set)
    {
        ArgumentNullException.ThrowIfNull(set);
        bool changed = _sets[0] != set;
        int at = _sets.IndexOf(set);
        if (at >= 0)
        {
            _sets.RemoveAt(at);
        }

        _sets.Insert(0, set);
        while (_sets.Count > MaxArguments)
        {
            _sets.RemoveAt(_sets.Count - 1);
        }

        if (!_sets.Contains(Empty))
        {
            if (_sets.Count == MaxArguments)
            {
                _sets.RemoveAt(_sets.Count - 1);
            }

            _sets.Add(Empty);
        }

        return changed;
    }

    /// <summary><paramref name="set"/> taken out (<c>removeArgumentSet</c>), the empty set never.</summary>
    public void Remove(string set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set != Empty)
        {
            _sets.Remove(set);
        }
    }

    /// <summary>What the Run drop-down says for a set (<c>_buildPopupList</c>): <c>Run: app1</c> or <c>Run: app1(1, 2)</c>.</summary>
    public static string ItemText(string appName, string set) => "Run: " + appName + (set.Length > 0 ? "(" + set + ")" : string.Empty);

    /// <summary>What stands before the Run drop-down's text field (<c>_buildRunEditableArgumentItem</c>): <c>Run: app1(</c>.</summary>
    public static string FieldLabel(string appName) => "Run: " + appName + "(";
}
