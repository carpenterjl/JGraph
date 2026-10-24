using System.Text.RegularExpressions;

namespace JGraph.Deploy;

/// <summary>A refusal of <c>mcc</c>'s, with R2025b's identifier and text.</summary>
public sealed class MccException(string identifier, string message) : Exception(message)
{
    /// <summary>The MException identifier, <c>Compiler:compiler:COM_ERR_*</c> for R2025b's own.</summary>
    public string Identifier { get; } = identifier;
}

/// <summary>What an <c>mcc</c> build makes.</summary>
public enum MccTarget
{
    /// <summary>A console program: <c>-m</c>, or <c>-W main:</c>.</summary>
    Console,

    /// <summary>A windowed program: <c>-e</c>, or <c>-W WinMain:</c>.</summary>
    Windowed,
}

/// <summary>
/// <c>mcc</c>'s command line (compiler and SDK plan, stage K6): the standalone switches <c>-m</c>,
/// <c>-e</c>, <c>-W main|WinMain:name[,version=v][,runtime=bundled]</c>, <c>-T link:exe</c>,
/// <c>-o</c>, <c>-n</c>, <c>-r</c>, <c>-h</c>, <c>-C</c>, <c>-a</c>, <c>-d</c> and <c>-v</c>, the
/// dependency switches compiler.build writes (<c>-X</c>, <c>-Z</c>, <c>-I</c>, <c>-N</c>, <c>-p</c>),
/// <c>-R</c> (stage K4) and <c>-j</c> (stage K3b), read as R2025b's mcc reads them and refused in its
/// words (stage K0's <c>build_side</c>, K6's <c>k6_build</c>). Every other switch R2025b knows is
/// refused as one JGraph's mcc does not build yet.
/// </summary>
public sealed class MccCommandLine
{
    private const string Prefix = "Compiler:compiler:";

    /// <summary>The switches that take the next argument as their value.</summary>
    private static readonly HashSet<string> Valued = new(StringComparer.Ordinal)
    {
        "-a", "-B", "-d", "-f", "-F", "-h", "-I", "-J", "-k", "-L", "-M", "-o", "-p", "-r", "-R", "-T", "-u", "-U", "-w", "-W", "-Y", "-Z",
    };

    /// <summary>Switches R2025b reads and then does nothing with: <c>-L C</c> and <c>-t</c> leave a build with no wrapper (K0).</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "-L", "-t" };

    /// <summary>R2025b's switches that JGraph's mcc does not build (the plan's stage K7, and the encryption decided against).</summary>
    private static readonly HashSet<string> NotBuilt = new(StringComparer.Ordinal)
    {
        "-b", "-B", "-c", "-f", "-F", "-g", "-G", "-J", "-k", "-K", "-l", "-M", "-s", "-S", "-u", "-U", "-w", "-Y",
    };

    private MccCommandLine()
    {
    }

    /// <summary>What the build makes.</summary>
    public MccTarget Target { get; private set; } = MccTarget.Console;

    /// <summary>The component's name, <c>-W main:name</c>; the main file's when not given.</summary>
    public string? ComponentName { get; private set; }

    /// <summary>The program's version, <c>-W main:name,version=v</c>.</summary>
    public ProgramVersion Version { get; private set; } = ProgramVersion.Default;

    /// <summary>JGraph's <c>-W main:name,runtime=bundled</c>: the runtime is copied beside the program.</summary>
    public RuntimeDelivery RuntimeDelivery { get; private set; }

    /// <summary><c>-o</c>: the program's name; the main file's when not given.</summary>
    public string? OutputName { get; private set; }

    /// <summary><c>-d</c>: the output folder; the current folder when not given.</summary>
    public string? OutputFolder { get; private set; }

    /// <summary><c>-n</c>: the arguments go through R2025b's numeric rule.</summary>
    public bool NumericArguments { get; private set; }

    /// <summary><c>-C</c>: the archive is written beside the program.</summary>
    public bool SeparateArchive { get; private set; }

    /// <summary><c>-r</c>: the icon file.</summary>
    public string? IconFile { get; private set; }

    /// <summary><c>-h</c>: the help text file.</summary>
    public string? HelpFile { get; private set; }

    /// <summary><c>-v</c>: the build reports what it does.</summary>
    public bool Verbose { get; private set; }

    /// <summary><c>-j</c>: the code files go in as P-files (stage K3b).</summary>
    public bool PCode { get; private set; }

    /// <summary><c>-X</c>: no data file is taken for a literal.</summary>
    public bool NoDataFiles { get; private set; }

    /// <summary><c>-N</c>: the toolbox folders come off the path.</summary>
    public bool ClearToolboxPath { get; private set; }

    /// <summary><c>-a</c>, in order.</summary>
    public List<string> Additional { get; } = [];

    /// <summary><c>-I</c>, in order.</summary>
    public List<string> IncludeFolders { get; } = [];

    /// <summary><c>-p</c>, in order.</summary>
    public List<string> ToolboxFolders { get; } = [];

    /// <summary><c>-Z</c>, in order: <c>autodetect</c> or <c>none</c>, as JGraph has no support packages.</summary>
    public List<string> SupportPackages { get; } = [];

    /// <summary><c>-R</c>, each one's text.</summary>
    public List<string> RuntimeOptions { get; } = [];

    /// <summary><c>-?</c>: the help is printed and nothing is built.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>The files, in order: the main first.</summary>
    public List<string> Files { get; } = [];

    /// <summary>Reads <paramref name="args"/> as R2025b's mcc does, or refuses them as it does.</summary>
    /// <exception cref="MccException">R2025b's refusal.</exception>
    public static MccCommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var line = new MccCommandLine();
        bool targetGiven = false;
        string? wrapper = null;
        string? link = null;
        if (args.Contains("-F") && args.Count > 2)
        {
            throw Refuse("COM_ERR_OPTION_F_MUST_BE_ALONE", "Error: The -F <file name> option must occur with no other options on the command line.");
        }

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg.Length < 2 || arg[0] != '-')
            {
                line.Files.Add(arg);
                continue;
            }

            if (arg == "-?")
            {
                line.ShowHelp = true;
                return line;
            }

            if (arg == "-x")
            {
                throw Refuse("COM_ERR_NO_MORE_MEX",
                    "Error: -x is no longer supported. The MATLAB Compiler no longer generates \n  MEX files because there is no longer any performance advantage to doing so: the \n  MATLAB JIT accelerates MATLAB files by default. \n  To hide proprietary algorithms, use the PCODE function.");
            }

            string? value = null;
            if (Valued.Contains(arg))
            {
                if (i + 1 >= args.Count)
                {
                    throw new MccException("JGraph:mcc:missingValue", $"The {arg} option needs a value after it.");
                }

                value = args[++i];
            }

            if (Ignored.Contains(arg))
            {
                continue;
            }

            if (NotBuilt.Contains(arg))
            {
                throw new MccException("JGraph:mcc:optionNotBuilt", $"JGraph's mcc does not build the {arg} option.");
            }

            switch (arg)
            {
                case "-m":
                    line.Target = MccTarget.Console;
                    targetGiven = true;
                    break;
                case "-e":
                    line.Target = MccTarget.Windowed;
                    targetGiven = true;
                    break;
                case "-W":
                    wrapper = value!;
                    line.Wrapper(value!);
                    targetGiven = true;
                    break;
                case "-T":
                    link = value!;
                    if (value is not ("link:exe" or "link:lib" or "compile:exe" or "compile:lib" or "codegen"))
                    {
                        throw Refuse("COM_ERR_INVALID_OPTION", $"Error: \"{value}\" is not a valid -T option argument.");
                    }

                    break;
                case "-o":
                    line.OutputName = value;
                    break;
                case "-d":
                    line.OutputFolder = value;
                    break;
                case "-n":
                    line.NumericArguments = true;
                    break;
                case "-C":
                    line.SeparateArchive = true;
                    break;
                case "-r":
                    line.IconFile = value;
                    break;
                case "-h":
                    line.HelpFile = value;
                    break;
                case "-v":
                    line.Verbose = true;
                    break;
                case "-j":
                    line.PCode = true;
                    break;
                case "-X":
                    line.NoDataFiles = true;
                    break;
                case "-N":
                    line.ClearToolboxPath = true;
                    break;
                case "-a":
                    line.Additional.Add(value!);
                    break;
                case "-I":
                    line.IncludeFolders.Add(value!);
                    break;
                case "-p":
                    line.ToolboxFolders.Add(value!);
                    break;
                case "-Z":
                    line.SupportPackages.Add(value!);
                    break;
                case "-R":
                    line.RuntimeOptions.Add(value!);
                    break;
                default:
                    throw Refuse("COM_ERR_UNRECOGNIZED_OPTION", $"Error: Unrecognized option: {arg}.");
            }
        }

        if (!targetGiven)
        {
            throw Refuse("COM_ERR_MISSING_WRAPPER",
                "Unable to determine application type, since no wrapper function was specified. \nPlease use the -W switch or specify application type via -m or -l. \nType 'mcc -?' for further assistance.");
        }

        if (wrapper is not null && link is not null && !link.EndsWith(":exe", StringComparison.Ordinal))
        {
            throw Refuse("COM_ERR_WRAPPER_TARGET_MISMATCH",
                "Error: The wrapper type specified with -W switch doesn't match with the target type specified with -T switch.");
        }

        if (line.Files.Count == 0)
        {
            throw Refuse("COM_ERR_NO_FILES_SPECIFIED", "Error: No source files were specified (-? for help).");
        }

        if (line.OutputName is { } name && !IsOutputName(name))
        {
            throw Refuse("COM_ERR_ILLEGAL_OUTPUT_NAME",
                $"Illegal output name: '{name}' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')");
        }

        foreach (string package in line.SupportPackages)
        {
            if (package is not ("autodetect" or "none"))
            {
                throw new MccException("JGraph:mcc:supportPackage", $"JGraph has no support packages; '{package}' cannot be included.");
            }
        }

        return line;
    }

    /// <summary>Whether R2025b takes <paramref name="name"/> for a program's name: a letter or <c>_</c>, then letters, digits and <c>_</c>.</summary>
    public static bool IsOutputName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>
    /// <c>-W main:name[,version=v]</c> or <c>WinMain:</c> (any case, as compiler.build writes
    /// <c>winmain</c>), and JGraph's <c>runtime=bundled</c>; a wrapper for another target is one
    /// JGraph's mcc does not build yet.
    /// </summary>
    private void Wrapper(string value)
    {
        int colon = value.IndexOf(':', StringComparison.Ordinal);
        string kind = colon < 0 ? value : value[..colon];
        string[] parts = colon < 0 ? [] : value[(colon + 1)..].Split(',');
        if (kind.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            Target = MccTarget.Console;
        }
        else if (kind.Equals("WinMain", StringComparison.OrdinalIgnoreCase))
        {
            Target = MccTarget.Windowed;
        }
        else if (kind is "lib" or "cpplib" or "dotnet" or "java" or "python" or "CTF" or "excel" or "mpsxl" or "com" or "hadoop" or "spark" or "webapp")
        {
            throw new MccException("JGraph:mcc:targetNotBuilt", $"JGraph's mcc does not build the -W {kind} target yet.");
        }
        else
        {
            throw Refuse("COM_ERR_INVALID_WRAPPER_OPTION", $"Error: '{kind}' is not a valid -W option.");
        }

        if (parts.Length > 0 && parts[0].Length > 0)
        {
            ComponentName = parts[0];
        }

        foreach (string part in parts.Skip(1))
        {
            if (part.StartsWith("version=", StringComparison.Ordinal))
            {
                string text = part["version=".Length..];
                Version = ProgramVersion.Parse(text) ?? throw Refuse("COM_ERR_INVALID_VERSION", ProgramVersion.Refusal(text));
            }
            else if (part == "runtime=bundled")
            {
                RuntimeDelivery = RuntimeDelivery.Bundled;
            }
            else if (part == "runtime=installed")
            {
                RuntimeDelivery = RuntimeDelivery.Installed;
            }
            else
            {
                throw Refuse("COM_ERR_INVALID_WRAPPER_OPTION", $"Error: '{part}' is not a valid -W option.");
            }
        }
    }

    private static MccException Refuse(string id, string text) => new(Prefix + id, text);

    /// <summary>
    /// <c>mcc -?</c>: JGraph's own help, laid out as R2025b's (its options common to every target, then
    /// each target's command), naming only what JGraph's mcc builds.
    /// </summary>
    public const string HelpText =
        "mcc Compile MATLAB functions for deployment outside MATLAB.\n"
        + " mcc [-options] fun [fun2...]\n\n"
        + "Options applicable across all deployment targets:\n"
        + " -?  Display help for the mcc command\n"
        + " -a  Add additional files or directories to be included in the build\n"
        + " -d  Build output directory\n"
        + " -I  Add a directory to be searched for MATLAB files\n"
        + " -v  Verbose display of build\n\n"
        + "JGraph Compiler\n"
        + " Standalone Application\n"
        + " mcc -m <matlabFile.m>\n\n"
        + " Standalone Windows Application\n"
        + " mcc -e <matlabFile.m>\n\n"
        + "Replace single quotes with double when executing the mcc command from a Windows Command Prompt.\n";
}
