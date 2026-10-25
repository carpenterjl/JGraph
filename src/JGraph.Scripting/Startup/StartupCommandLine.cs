namespace JGraph.Scripting.Startup;

/// <summary>
/// Parses JGraph's startup options. Both executables run the same parser over the same argument list,
/// so the launcher and the application can never disagree about what a command line means.
/// </summary>
/// <remarks>
/// Flags may be written with one or two leading dashes and in any case (<c>-batch</c>, <c>--BATCH</c>).
/// Every rejection produces a specific message rather than a silent fallback — a mistyped flag that
/// quietly opened the interactive window would look like the script simply did nothing.
/// </remarks>
public static class StartupCommandLine
{
    /// <summary>Parses <paramref name="args"/> (the raw argument list, without the executable name).</summary>
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            return StartupOptions.Interactive;
        }

        // jgraph mcc … (stage K7): everything after the word is mcc's own command line.
        if (args[0] == "mcc")
        {
            return new StartupOptions(StartupMode.Mcc) { ProgramArguments = [.. args.Skip(1)] };
        }

        StartupMode? mode = null;
        string? statement = null;
        string? logFile = null;
        string? startDirectory = null;
        bool showFigures = false;
        bool help = false;
        string? deployedRoot = null;
        bool numeric = false;
        IReadOnlyList<string> programArguments = [];

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            // Everything after a lone -- is the deployed program's own command line, passed on as
            // it is, flags included (K1).
            if (arg == "--")
            {
                programArguments = [.. args.Skip(i + 1)];
                break;
            }

            if (Normalize(arg) is not { } flag)
            {
                return StartupOptions.Invalid(
                    $"Unexpected argument '{arg}'. A statement must follow -batch or -r.");
            }

            switch (flag)
            {
                case "batch":
                case "r":
                    StartupMode requested = flag == "batch" ? StartupMode.Batch : StartupMode.Run;
                    if (mode is { } existing)
                    {
                        return StartupOptions.Invalid(existing == requested
                            ? $"-{flag} was given more than once."
                            : "-batch and -r cannot be combined: one exits when the script ends, the other stays open.");
                    }

                    if (!TryTakeValue(args, ref i, out statement))
                    {
                        return StartupOptions.Invalid($"-{flag} requires a statement, e.g. -{flag} \"plot(1:10)\".");
                    }

                    mode = requested;
                    break;

                case "logfile":
                    if (logFile is not null)
                    {
                        return StartupOptions.Invalid("-logfile was given more than once.");
                    }

                    if (!TryTakeValue(args, ref i, out logFile))
                    {
                        return StartupOptions.Invalid("-logfile requires a file name, e.g. -logfile \"run.log\".");
                    }

                    break;

                case "sd":
                    if (startDirectory is not null)
                    {
                        return StartupOptions.Invalid("-sd was given more than once.");
                    }

                    if (!TryTakeValue(args, ref i, out startDirectory))
                    {
                        return StartupOptions.Invalid("-sd requires a directory, e.g. -sd \"C:\\work\".");
                    }

                    break;

                case "showfigures":
                    showFigures = true;
                    break;

                // Hidden (compiler and SDK plan, stage K1): run the -batch file as a compiled
                // program's main over a folder standing in for its extracted archive, until stage
                // K3's archive and K6's launcher make the real thing. Not in the usage text.
                case "deployed":
                    if (deployedRoot is not null)
                    {
                        return StartupOptions.Invalid("--deployed was given more than once.");
                    }

                    if (!TryTakeValue(args, ref i, out deployedRoot))
                    {
                        return StartupOptions.Invalid("--deployed requires an archive, or with -batch the folder that stands in for one.");
                    }

                    break;

                // Hidden (stage K3): R2025b's extractCTF utility; everything after it is its own.
                case "extractctf":
                    if (i != 0)
                    {
                        return StartupOptions.Invalid("-extractCTF must come first; what follows it is the archive and the folder.");
                    }

                    return new StartupOptions(StartupMode.ExtractCtf) { ProgramArguments = [.. args.Skip(1)] };

                case "deployed-numeric":
                    numeric = true;
                    break;

                case "h":
                case "help":
                case "?":
                    help = true;
                    break;

                default:
                    return StartupOptions.Invalid($"Unknown option '{arg}'. Run with -help for the list.");
            }
        }

        // Help wins over everything else: someone who asks what the options are should be told, not
        // have a half-understood command line executed on their behalf.
        if (help)
        {
            return new StartupOptions(StartupMode.Help);
        }

        if (showFigures && mode != StartupMode.Batch)
        {
            return StartupOptions.Invalid(
                "-showfigures only applies to -batch; an interactive session already shows its figures.");
        }

        // --deployed with no -batch runs an archive, whose manifest names the main and says whether
        // it was built with mcc -n (K3).
        if (deployedRoot is not null && mode is null && !showFigures)
        {
            if (numeric)
            {
                return StartupOptions.Invalid("--deployed-numeric only applies with -batch: an archive says whether it was built with mcc -n.");
            }

            return new StartupOptions(StartupMode.Batch, null, logFile, startDirectory)
            {
                DeployedRoot = deployedRoot,
                ProgramArguments = programArguments,
            };
        }

        if ((deployedRoot is not null || numeric || programArguments.Count > 0) && mode != StartupMode.Batch)
        {
            return StartupOptions.Invalid("--deployed, --deployed-numeric and -- only apply to -batch, or to --deployed with an archive.");
        }

        if ((numeric || programArguments.Count > 0) && deployedRoot is null)
        {
            return StartupOptions.Invalid("--deployed-numeric and -- need --deployed.");
        }

        if (mode is null)
        {
            return logFile is null && startDirectory is null
                ? StartupOptions.Interactive
                : new StartupOptions(StartupMode.Interactive, LogFile: logFile, StartDirectory: startDirectory);
        }

        return new StartupOptions(mode.Value, statement, logFile, startDirectory, showFigures)
        {
            DeployedRoot = deployedRoot,
            NumericArguments = numeric,
            ProgramArguments = programArguments,
        };
    }

    /// <summary>
    /// Strips a flag's leading dashes and lower-cases it, or returns null when the argument is not a
    /// flag at all. A lone "-" is not a flag.
    /// </summary>
    private static string? Normalize(string arg)
    {
        if (arg.Length < 2 || arg[0] != '-')
        {
            return null;
        }

        string name = arg[1] == '-' ? arg[2..] : arg[1..];
        return name.Length == 0 ? null : name.ToLowerInvariant();
    }

    /// <summary>Consumes the next argument as the current flag's value, advancing the index.</summary>
    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        if (index + 1 >= args.Count || args[index + 1].Length == 0)
        {
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }
}
