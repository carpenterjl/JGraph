using System.Text;

namespace JGraph.Deploy;

/// <summary>What <c>-w</c> makes of one of mcc's warnings.</summary>
public enum MccWarningState
{
    /// <summary>The warning is shown.</summary>
    Enabled,

    /// <summary>The warning is not shown.</summary>
    Disabled,

    /// <summary>The warning is shown, and the build then fails (<c>COM_ERR_WARNING_TREATED_AS_ERROR</c>).</summary>
    Error,
}

/// <summary>
/// mcc's <c>-w</c> (compiler and SDK plan, stage K7), as R2025b's answers it (k7_switches, k7_more):
/// <c>list</c> prints the warning table; <c>enable</c>, <c>disable</c> and <c>error</c> set every
/// warning, and with <c>:id</c> one, named by the table's short name or by any message identifier
/// (a name with a colon, known or not); the words in any case; <c>on</c> and <c>off</c> taken and
/// doing nothing. A warning disabled by name is not shown even while every warning is an error, and the
/// build still fails (k7_more's w_err_then_dis). MATLAB's own warning state is left as it was.
/// </summary>
public sealed class MccWarnings
{
    private const string Prefix = "Compiler:compiler:";

    /// <summary>R2025b's table, in its order: each short name, its message identifier and its text.</summary>
    private static readonly (string Name, string Identifier, string Text)[] Table =
    [
        ("specified_file_mismatch", Prefix + "COM_WARN_SPECIFIED_FILE_MISMATCH",
            "MATLAB file \"%s\" was specified on the command line with full path of \"%s\", \nbut was found on the search path in directory \"%s\" first.\n"),
        ("repeated_file", Prefix + "COM_WARN_REPEATED_FILE", "The file \"%s\" was repeated on the command line.\n"),
        ("switch_ignored", Prefix + "COM_WARN_SWITCH_IGNORED", "The option -%s is ignored in standalone target mode (specify -? for help).\n"),
        ("missing_lib_sentinel", Prefix + "COM_WARN_MLIB_MUST_START_WITH_LIB",
            "Shared library name \"%s\" does not begin with \"lib\".\nOn Linux and Mac, the linker expects shared library names to begin with \"lib\"."),
        ("demo_license", Prefix + "COM_WARN_DEMO_LICENSE",
            "DEMO Compiler license. \n  The generated application will expire 30 days from today, \n  on %s.\n"),
    ];

    private readonly Dictionary<string, MccWarningState> _named = new(StringComparer.Ordinal);

    /// <summary>What <c>-w enable</c>, <c>disable</c> or <c>error</c> without a name set, for every warning.</summary>
    public MccWarningState All { get; private set; } = MccWarningState.Enabled;

    /// <summary>Whether <c>-w list</c> was given: the table is printed and nothing is built.</summary>
    public bool List { get; private set; }

    /// <summary>Reads one <c>-w</c> value.</summary>
    /// <exception cref="MccException">R2025b's <c>COM_ERR_INVALID_WARN_OPTION</c>.</exception>
    public void Apply(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int colon = value.IndexOf(':', StringComparison.Ordinal);
        string action = (colon < 0 ? value : value[..colon]).ToLowerInvariant();
        MccWarningState? state = action switch
        {
            "enable" => MccWarningState.Enabled,
            "disable" => MccWarningState.Disabled,
            "error" => MccWarningState.Error,
            _ => null,
        };
        if (colon < 0)
        {
            switch (action)
            {
                case "list":
                    List = true;
                    return;
                case "on" or "off":
                    return;
            }
        }

        if (state is not { } set)
        {
            throw Invalid(value);
        }

        if (colon < 0)
        {
            All = set;
            return;
        }

        string name = value[(colon + 1)..];
        if (name == "all_warnings")
        {
            All = set;
        }
        else if (Array.FindIndex(Table, t => t.Name == name) is int row and >= 0)
        {
            _named[Table[row].Identifier] = set;
        }
        else if (name.Contains(':', StringComparison.Ordinal))
        {
            _named[name] = set;
        }
        else
        {
            throw Invalid(value);
        }
    }

    /// <summary>Whether the warning <paramref name="identifier"/> is shown.</summary>
    public bool Shows(string identifier) =>
        _named.TryGetValue(identifier, out MccWarningState state) ? state != MccWarningState.Disabled : All != MccWarningState.Disabled;

    /// <summary>Whether the warning <paramref name="identifier"/>, once issued, fails the build.</summary>
    public bool Fails(string identifier) =>
        All == MccWarningState.Error || (_named.TryGetValue(identifier, out MccWarningState state) && state == MccWarningState.Error);

    /// <summary>The refusal a warning that <see cref="Fails"/> raises after it is shown.</summary>
    public static MccException TreatedAsError() =>
        new(Prefix + "COM_ERR_WARNING_TREATED_AS_ERROR", "Error: A warning has been issued. Warnings are being treated as errors.");

    /// <summary><c>-w list</c>'s table, as R2025b prints it.</summary>
    public string ListText()
    {
        const string rule = "--------------------------------------------------------------------------------\n";
        var text = new StringBuilder();
        text.Append("Warning table (for use with -w enable:/disable:/error:). Format is as follows: \n\n");
        text.Append("\"<WARNING IDENTIFIER>\" is <STATUS>, and corresponds to the message: <TEXT> \n\n\n");
        text.Append(rule).Append('\n');
        text.Append("Identifier \"all_warnings\" is ").Append(Status(All)).Append(".\n\n");
        text.Append(rule).Append('\n');
        foreach ((string name, string identifier, string message) in Table)
        {
            MccWarningState state = _named.TryGetValue(identifier, out MccWarningState named) ? named : MccWarningState.Enabled;
            text.Append('"').Append(name).Append("\" is ").Append(Status(state)).Append(", and corresponds to the message:\n\n");
            text.Append(message).Append('\n');
            text.Append(rule).Append('\n');
        }

        return text.ToString();
    }

    private static string Status(MccWarningState state) => state switch
    {
        MccWarningState.Disabled => "disabled",
        MccWarningState.Error => "set to throw an error",
        _ => "enabled",
    };

    private static MccException Invalid(string value) => new(Prefix + "COM_ERR_INVALID_WARN_OPTION", $"Error: '{value}' is not a valid -w option.");
}
