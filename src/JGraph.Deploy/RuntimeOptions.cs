namespace JGraph.Deploy;

/// <summary>
/// The runtime options a program was built with (<c>mcc -R</c>, kept in the manifest's
/// <c>runtime-options</c>) or a host starts the runtime with (compiler and SDK plan, stage K4), as
/// <c>record-k4.ps1</c> measured R2025b's runtime honouring them (<c>recordings/k4_ropts.out.txt</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>-startmsg[,text]</c> prints the runtime's start line, then the text when there is one.</item>
/// <item><c>-completemsg,text</c> prints the text once the runtime has started, before the main runs.</item>
/// <item><c>-logfile,file</c> copies everything the program writes to standard output and standard
/// error into the file, read against the current folder and replaced on every run.</item>
/// <item><c>-singleCompThread</c> makes <c>maxNumCompThreads</c> 1 and refuses to raise it.</item>
/// <item><c>-nojvm</c> and <c>-nodisplay</c> change nothing here: JGraph has no Java, and the
/// display option is Linux's.</item>
/// <item>Anything else is warned about when the runtime starts, and the program runs.</item>
/// </list>
/// One <c>-R</c> argument can hold several options separated by commas, as
/// <c>-startmsg,Starting,-completemsg,Done</c> does: a part that starts with <c>-</c> names an
/// option and the parts after it, up to the next, are its value.
/// </remarks>
public sealed record RuntimeOptions
{
    /// <summary>The release the start line names: the MATLAB Runtime JGraph's dialect follows (decision KO2).</summary>
    public const string Release = "25.2";

    /// <summary>The options with nothing set.</summary>
    public static RuntimeOptions None { get; } = new();

    /// <summary>Whether <c>-nojvm</c> was given.</summary>
    public bool NoJvm { get; init; }

    /// <summary>Whether <c>-nodisplay</c> was given.</summary>
    public bool NoDisplay { get; init; }

    /// <summary>Whether <c>-singleCompThread</c> was given.</summary>
    public bool SingleCompThread { get; init; }

    /// <summary>The log file <c>-logfile</c> names, as given, or null.</summary>
    public string? LogFile { get; init; }

    /// <summary>The text after <c>-startmsg</c>, empty when it had none, or null when the option was not given.</summary>
    public string? StartMessage { get; init; }

    /// <summary>The text after <c>-completemsg</c>, or null when the option was not given.</summary>
    public string? CompleteMessage { get; init; }

    /// <summary>The options the runtime does not know, without their leading <c>-</c>, in order.</summary>
    public IReadOnlyList<string> Unrecognized { get; init; } = [];

    /// <inheritdoc />
    public bool Equals(RuntimeOptions? other) =>
        other is not null && NoJvm == other.NoJvm && NoDisplay == other.NoDisplay && SingleCompThread == other.SingleCompThread
        && LogFile == other.LogFile && StartMessage == other.StartMessage && CompleteMessage == other.CompleteMessage
        && Unrecognized.SequenceEqual(other.Unrecognized);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(NoJvm, NoDisplay, SingleCompThread, LogFile, StartMessage, CompleteMessage, Unrecognized.Count);

    /// <summary>Reads the options from <c>-R</c> arguments.</summary>
    /// <param name="arguments">Each <c>-R</c> argument's text, as written after <c>-R</c>.</param>
    public static RuntimeOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        RuntimeOptions options = None;
        var unrecognized = new List<string>();
        foreach (string argument in arguments)
        {
            foreach ((string name, string? value) in Split(argument))
            {
                options = name switch
                {
                    "-nojvm" => options with { NoJvm = true },
                    "-nodisplay" => options with { NoDisplay = true },
                    "-singleCompThread" => options with { SingleCompThread = true },
                    "-logfile" when value is { Length: > 0 } => options with { LogFile = value },
                    "-startmsg" => options with { StartMessage = value ?? string.Empty },
                    "-completemsg" => options with { CompleteMessage = value ?? string.Empty },
                    _ => Unknown(options, name, unrecognized),
                };
            }
        }

        return options with { Unrecognized = [.. unrecognized] };
    }

    private static RuntimeOptions Unknown(RuntimeOptions options, string name, List<string> unrecognized)
    {
        unrecognized.Add(name.TrimStart('-'));
        return options;
    }

    /// <summary>
    /// What the runtime prints to standard output as it starts, before anything runs: a warning per
    /// option it does not know, then the start line and message, then the complete message.
    /// </summary>
    /// <remarks>
    /// R2025b's start line is <c>Initializing MATLAB Runtime version 25.2</c>; JGraph's names its own
    /// runtime, with the release its dialect follows.
    /// </remarks>
    public IReadOnlyList<string> StartLines()
    {
        var lines = new List<string>();
        foreach (string name in Unrecognized)
        {
            lines.Add($"Warning: Unrecognized command line option: {name}.");
        }

        if (StartMessage is not null)
        {
            lines.Add($"Initializing JGraph Runtime version {Release}");
            if (StartMessage.Length > 0)
            {
                lines.Add(StartMessage);
            }
        }

        if (CompleteMessage is { Length: > 0 })
        {
            lines.Add(CompleteMessage);
        }

        return lines;
    }

    /// <summary>The options in one <c>-R</c> argument, each with the value that follows it.</summary>
    private static IEnumerable<(string Name, string? Value)> Split(string argument)
    {
        string[] parts = argument.Split(',');
        int i = 0;
        while (i < parts.Length)
        {
            string name = parts[i].Trim();
            i++;
            if (name.Length == 0)
            {
                continue;
            }

            int start = i;
            while (i < parts.Length && !parts[i].TrimStart().StartsWith('-'))
            {
                i++;
            }

            yield return (name, i > start ? string.Join(',', parts[start..i]) : null);
        }
    }
}
