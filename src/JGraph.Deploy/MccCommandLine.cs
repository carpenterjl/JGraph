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

    /// <summary>A .NET assembly for the MWArray API: <c>-W dotnet:</c> (stage K8).</summary>
    DotNet,
}

/// <summary>
/// <c>-W dotnet:assembly[,api=...],class,framework,security,local|remote[,version=v]</c> as R2025b's
/// DotNetWriter writes it and its mcc reads it (k8_build): an empty class is the assembly's last name, an
/// empty framework 4.0, a security other than <c>Private</c> a strong-name key file.
/// </summary>
/// <param name="AssemblyName">The assembly's name, which is also its namespace (<c>Org.Group.K8n</c>).</param>
/// <param name="ClassName">The class the files not named in a <c>class{}</c> go into.</param>
/// <param name="Framework">The .NET version written, <c>4.0</c> or <c>5.0</c>.</param>
/// <param name="KeyFile">The strong-name key file, or null for a private assembly.</param>
public sealed record DotNetWrapper(string AssemblyName, string ClassName, string Framework, string? KeyFile)
{
    /// <summary>The assembly's own name: the last part of a dotted one, as its dll is named.</summary>
    public string ShortName => AssemblyName[(AssemblyName.LastIndexOf('.') + 1)..];
}

/// <summary>How far a standalone build goes, as <c>-T</c> says (k7_switches).</summary>
public enum MccLink
{
    /// <summary><c>-T link:exe</c>, the default: the program with its archive.</summary>
    Program,

    /// <summary><c>-T compile:exe</c>: the program, its archive written beside it.</summary>
    ProgramBesideArchive,

    /// <summary><c>-T codegen</c>: the archive alone, no program.</summary>
    ArchiveOnly,
}

/// <summary>
/// <c>mcc</c>'s command line (compiler and SDK plan, stages K6 and K7), read as R2025b's mcc reads it
/// and refused in its words (K0's <c>build_side</c>, K6's <c>k6_build</c>, K7's <c>k7_switches</c> and
/// <c>k7_more</c>):
/// <list type="bullet">
/// <item>a switch of several letters is read as that many switches (<c>-mv</c>, and <c>-help</c> as
/// <c>-h -e -l -p</c>), each taking its value from the arguments that follow in turn;</item>
/// <item><c>-B file[:a,b]</c> is replaced by the file's switches with <c>%1%</c>, <c>%2%</c> filled in,
/// read where given or from JGraph's bundle folder (R2025b's bundle names);</item>
/// <item>the standalone switches, <c>-T</c>'s three forms, <c>-w</c> (<see cref="MccWarnings"/>),
/// <c>-K</c>, <c>-/t</c>;</item>
/// <item>the obsolete switches: noted and ignored (<c>-O -i -z -L -t</c>) or refused (<c>-V -win32
/// -win64 -y -x -F</c>), as R2025b does;</item>
/// <item>switches a standalone build ignores: with R2025b's warning (<c>-g -G -M -f -c</c>) or without
/// (<c>-b -S -u -H -P -Y -A all</c>);</item>
/// <item>every <c>-W</c> target checked as R2025b checks it, then refused as one JGraph does not build
/// yet, or does not build at all (section 10 of the plan);</item>
/// <item>the encryption switches <c>-s</c>, <c>-k</c>, <c>-J</c> refused, by the decision of 2026-10-24.</item>
/// </list>
/// </summary>
public sealed class MccCommandLine
{
    private const string Prefix = "Compiler:compiler:";

    /// <summary>The switches that take the next argument as their value, each with R2025b's example of one.</summary>
    private static readonly Dictionary<string, string> Valued = new(StringComparer.Ordinal)
    {
        ["-a"] = "data.mat", ["-A"] = "all", ["-B"] = "<option file>", ["-d"] = "<output directory>", ["-E"] = "<file name>",
        ["-f"] = "<build options file>", ["-F"] = "<file name>", ["-h"] = "<file name>", ["-I"] = "<MATLAB file search directory>",
        ["-J"] = "<file name>", ["-k"] = "key=<key file>;loader=<mex function name>", ["-L"] = "<language>", ["-M"] = "-D<macro>=<value>",
        ["-o"] = "<output file>", ["-p"] = "<path-item>", ["-r"] = "<embed icon file>", ["-R"] = "-nojvm -nojit", ["-T"] = "link:lib",
        ["-w"] = "list", ["-W"] = "cpplib:<library name>", ["-Y"] = "<license.dat file>", ["-Z"] = "autodetect",
    };

    /// <summary>Switches R2025b notes as no longer supported and then ignores, with its note.</summary>
    private static readonly Dictionary<string, string> Noted = new(StringComparer.Ordinal)
    {
        ["-O"] = "The -O switch is no longer supported; the MATLAB JIT accelerator makes \nCompiler optimizations unnecessary. \n",
        ["-i"] = "The -i switch is no longer supported; the \"include exported interfaces\" behavior \nis applied to every compilation. \n",
        ["-z"] = "The -z switch is no longer supported. \n",
        ["-L"] = "The -L switch is no longer supported; the target (-T) and wrapper (-W) \nswitches determine the language of the generated code. \n",
        ["-t"] = "The -t switch is no longer supported; the Compiler no longer generates C/C++ \nsource code for MATLAB functions (it generates wrapper functions instead, see \nthe documentation for -W). \n",
    };

    /// <summary>Switches R2025b refuses as no longer supported, each with its identifier and text.</summary>
    private static readonly Dictionary<string, (string Id, string Text)> Obsolete = new(StringComparer.Ordinal)
    {
        ["-V"] = ("COM_MSG_NO_DASH_V", "The -V switch is no longer supported; the MATLAB Compiler does not allow the use of previous compiler versions."),
        ["-win32"] = ("COM_MSG_NO_DASH_WIN32", "The -win32 switch is no longer supported. \n"),
        ["-win64"] = ("COM_MSG_NO_DASH_WIN64", "The -win64 switch is no longer supported; 64-bit architecture is assumed. \n"),
        ["-y"] = ("COM_ERR_NO_DASH_y", "Error: -y is no longer supported. The MATLAB Compiler no longer generates \n  Simulink C MEX S functions. See the Simulink documentation for other options\n  for embedding M-code in block diagrams."),
        ["-x"] = ("COM_ERR_NO_MORE_MEX", "Error: -x is no longer supported. The MATLAB Compiler no longer generates \n  MEX files because there is no longer any performance advantage to doing so: the \n  MATLAB JIT accelerates MATLAB files by default. \n  To hide proprietary algorithms, use the PCODE function."),
    };

    /// <summary>The library and server targets: what each is called, and the stage that builds it (null: out of scope).</summary>
    private static readonly Dictionary<string, (string What, string? Stage)> OtherTargets = new(StringComparer.Ordinal)
    {
        ["lib"] = ("a C shared library", "K11"),
        ["cpplib"] = ("a C++ shared library", "K12"),
        ["dotnet"] = ("a .NET assembly", "K8"),
        ["python"] = ("a Python package", "K13"),
        ["CTF"] = ("a Production Server archive", "K14"),
        ["excel"] = ("an Excel add-in", "K17"),
        ["mpsxl"] = ("a Production Server archive for Excel", "K17"),
        ["java"] = ("Java packages", null),
        ["com"] = ("COM components", null),
        ["hadoop"] = ("Hadoop archives", null),
        ["spark"] = ("Spark applications", null),
        ["webapp"] = ("web app archives", null),
    };

    /// <summary>The deepest a bundle may name another, so a bundle naming itself ends.</summary>
    private const int MaxBundleDepth = 32;

    private MccCommandLine()
    {
    }

    /// <summary>What the build makes.</summary>
    public MccTarget Target { get; private set; } = MccTarget.Console;

    /// <summary>How far the build goes, <c>-T</c>.</summary>
    public MccLink Link { get; private set; }

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

    /// <summary><c>-C</c>, <c>-U</c> or <c>-T compile:exe</c>: the archive is written beside the program.</summary>
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

    /// <summary><c>-K</c>: what a failed build wrote is kept.</summary>
    public bool KeepPartialOutput { get; private set; }

    /// <summary><c>-/t</c> (also <c>-/s</c>, <c>-/a</c>): the command line is printed as mcc read it before the build.</summary>
    public bool Trace { get; private set; }

    /// <summary><c>-/?</c>: the trace switches' help is printed and nothing is built.</summary>
    public bool ShowTraceHelp { get; private set; }

    /// <summary><c>-w</c>.</summary>
    public MccWarnings Warnings { get; } = new();

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

    /// <summary>R2025b's notes for the obsolete switches given, printed before the build.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>The switches a standalone build ignores with R2025b's warning, each as the warning names it (<c>-G</c> as <c>g</c>).</summary>
    public List<string> IgnoredSwitches { get; } = [];

    /// <summary>The command line as mcc read it: letters split, bundles in place.</summary>
    public List<string> Processed { get; } = [];

    /// <summary><c>-?</c>, or no arguments: the help is printed and nothing is built.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>The files, in order: the main first.</summary>
    public List<string> Files { get; } = [];

    /// <summary><c>-W dotnet:</c>'s parts, for a .NET assembly.</summary>
    public DotNetWrapper? DotNet { get; private set; }

    /// <summary>A library's <c>class{Name:file,file}</c> arguments, in order: each class and its files.</summary>
    public List<(string Name, List<string> Files)> Classes { get; } = [];

    /// <summary><c>-g</c> for a library: a debug build, with its symbols.</summary>
    public bool DebugBuild { get; private set; }

    /// <summary>Whether the build only prints something: help, the trace help or the warning table.</summary>
    public bool PrintsOnly => ShowHelp || ShowTraceHelp || Warnings.List;

    /// <summary>
    /// Reads <paramref name="args"/> as R2025b's mcc does, or refuses them as it does. A bundle file is
    /// read against <paramref name="currentFolder"/>, then from <paramref name="bundleFolder"/>
    /// (<see cref="BundleFolder"/> when null).
    /// </summary>
    /// <exception cref="MccException">R2025b's refusal.</exception>
    public static MccCommandLine Parse(IReadOnlyList<string> args, string? currentFolder = null, string? bundleFolder = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var line = new MccCommandLine();
        if (args.Count == 0)
        {
            line.ShowHelp = true;
            return line;
        }

        if (args.Contains("-F"))
        {
            throw args.Count == 2 && args[0] == "-F"
                ? Refuse("COM_ERR_DASH_F_DEPRECATED", "Error: The mcc -F option is no longer supported. Use deploytool /build instead. Run deploytool -? for usage.")
                : Refuse("COM_ERR_OPTION_F_MUST_BE_ALONE", "Error: The -F <file name> option must occur with no other options on the command line.");
        }

        if (args.Contains("-E"))
        {
            throw args.Count == 2 && args[0] == "-E"
                ? new MccException("JGraph:mcc:optionNotBuilt", "JGraph's mcc does not read -E files.")
                : Refuse("COM_ERR_OPTION_E_MUST_BE_ALONE", "Error: The -E <file name> option must occur with no other options on the command line.");
        }

        var words = new List<string>(args);
        var depth = new List<int>(Enumerable.Repeat(0, words.Count));
        string? wrapperKind = null;
        string[] wrapperParts = [];
        string? link = null;
        bool codegenSwitch = false;
        bool mpsSwitch = false;
        bool separateSwitch = false;

        for (int i = 0; i < words.Count; i++)
        {
            string arg = words[i];
            if (arg.Length < 2 || arg[0] != '-')
            {
                // class{Name:file,file} names a library class and its files (R2025b's ClassMapWriter).
                if (Regex.Match(arg, "^class\\{([^:}]*):(.*)\\}$") is { Success: true } classed)
                {
                    line.Classes.Add((classed.Groups[1].Value, [.. classed.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)]));
                }
                else
                {
                    line.Files.Add(arg);
                }

                line.Processed.Add(arg);
                continue;
            }

            if (arg.FirstOrDefault(char.IsWhiteSpace) is var space and not '\0')
            {
                throw Refuse("COM_ERR_ILLEGAL_WHITESPACE",
                    $"Error: Found illegal whitespace character in command line option: \"{space}\". \nThe strings on the left and right side of the space should be separate arguments \nto MCC.");
            }

            if (arg == "-?")
            {
                line.ShowHelp = true;
                return line;
            }

            if (arg.StartsWith("-/", StringComparison.Ordinal))
            {
                switch (arg)
                {
                    case "-/?":
                        line.ShowTraceHelp = true;
                        return line;
                    case "-/t" or "-/s" or "-/a":
                        line.Trace = true;
                        line.Processed.Add(arg);
                        continue;
                    default:
                        throw Refuse("COM_DBG_ERR_UNRECOGNIZED_OPTION", $"Error: Unrecognized option: -/{arg[2..]}.");
                }
            }

            // Several letters are as many switches (k7_switches: -mv, -vm, -mC; -help read as -h -e -l -p).
            if (arg.Length > 2 && !Obsolete.ContainsKey(arg))
            {
                words.RemoveAt(i);
                int level = depth[i];
                depth.RemoveAt(i);
                for (int c = arg.Length - 1; c >= 1; c--)
                {
                    words.Insert(i, "-" + arg[c]);
                    depth.Insert(i, level);
                }

                i--;
                continue;
            }

            if (Obsolete.TryGetValue(arg, out (string Id, string Text) obsolete))
            {
                throw Refuse(obsolete.Id, obsolete.Text);
            }

            string? value = null;
            if (Valued.TryGetValue(arg, out string? example))
            {
                // The last argument with nothing after it is taken and does nothing (k7_switches:
                // help_word read -help as -h -e -l -p and went on); an empty value is refused.
                if (i + 1 >= words.Count)
                {
                    line.Processed.Add(arg);
                    continue;
                }

                if (words[i + 1].Length == 0)
                {
                    throw Refuse("COM_ERR_EXPECTED_ARGUMENT", $"Error: The {arg} option requires a nonempty argument (e.g. \"{arg} {example}\").");
                }

                value = words[++i];
            }
            else if (arg == "-P" && i + 1 < words.Count && !words[i + 1].StartsWith('-'))
            {
                // R2025b's undocumented -P takes the next argument unless it is a switch (k7_more).
                value = words[++i];
            }

            if (arg == "-B")
            {
                int level = depth[i - 1] + 1;
                if (level > MaxBundleDepth)
                {
                    throw new MccException("JGraph:mcc:bundleDepth", $"The bundle '{value}' names bundles more than {MaxBundleDepth} deep.");
                }

                IReadOnlyList<string> expanded = ReadBundle(value!, currentFolder, bundleFolder ?? BundleFolder);
                words.RemoveRange(i - 1, 2);
                depth.RemoveRange(i - 1, 2);
                words.InsertRange(i - 1, expanded);
                depth.InsertRange(i - 1, Enumerable.Repeat(level, expanded.Count));
                i -= 2;
                continue;
            }

            line.Processed.Add(arg);
            if (value is not null)
            {
                line.Processed.Add(value);
            }

            if (Noted.TryGetValue(arg, out string? note))
            {
                line.Notes.Add(note);
                continue;
            }

            switch (arg)
            {
                case "-m":
                    line.Target = MccTarget.Console;
                    wrapperKind = "main";
                    wrapperParts = [];
                    link = "link:exe";
                    break;
                case "-e":
                    line.Target = MccTarget.Windowed;
                    wrapperKind = "WinMain";
                    wrapperParts = [];
                    link = "link:exe";
                    break;
                case "-l":
                    wrapperKind = "lib";
                    wrapperParts = [];
                    link = "link:lib";
                    break;
                case "-W":
                    (wrapperKind, wrapperParts) = line.Wrapper(value!);
                    break;
                case "-T":
                    link = value!;
                    if (!IsLinkValue(value!))
                    {
                        throw Refuse("COM_ERR_INVALID_OPTION", $"Error: \"{value}\" is not a valid -T option argument.");
                    }

                    break;
                case "-o":
                    if (line.OutputName is { } first)
                    {
                        throw Refuse("COM_ERR_MULTIPLE_COMPONENT_NAMES",
                            $"Error: Multiple component names. Processing \"-o\" switch. Component name \"{first}\" already specified via \"-o\".");
                    }

                    line.OutputName = value;
                    break;
                case "-d":
                    line.OutputFolder = value;
                    break;
                case "-n":
                    line.NumericArguments = true;
                    break;
                case "-C":
                    separateSwitch = true;
                    break;
                case "-U":
                    mpsSwitch = true;
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
                case "-K":
                    line.KeepPartialOutput = true;
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
                case "-w":
                    line.Warnings.Apply(value!);
                    break;
                case "-g" or "-G":
                    line.IgnoredSwitches.Add("g");
                    break;
                case "-M" or "-f":
                    line.IgnoredSwitches.Add(arg[1..]);
                    break;
                case "-c":
                    codegenSwitch = true;
                    line.IgnoredSwitches.Add("c");
                    break;
                case "-A":
                    if (value != "all")
                    {
                        throw Refuse("COM_ERR_INVALID_OPTION", $"Error: \"{value}\" is not a valid -A option argument.");
                    }

                    break;
                case "-b" or "-S" or "-u" or "-H" or "-P" or "-Y":
                    // A standalone build takes these and does nothing with them, as R2025b's does
                    // (k7_switches: -b, -S, -u, -H, -P, -Y beside -m); -Y names a licence JGraph has no use for.
                    break;
                case "-s" or "-k" or "-J":
                    throw new MccException("JGraph:mcc:optionNotBuilt",
                        $"JGraph's mcc does not build the {arg} option: JGraph builds no encrypted archives.");
                default:
                    throw Refuse("COM_ERR_UNRECOGNIZED_OPTION", $"Error: Unrecognized option: -{arg[1..]}.");
            }
        }

        if (line.Warnings.List || line.ShowTraceHelp)
        {
            return line;
        }

        // R2025b looks for -h's file before anything else it checks (k7_switches: dash_h_end, help_word).
        if (line.HelpFile is { } help && currentFolder is not null
            && !File.Exists(Path.IsPathRooted(help) ? help : Path.Combine(currentFolder, help)))
        {
            throw Refuse("COM_ERR_HELP_FILE_NOT_EXIST", $"The help file \n  '{help}'\ndoes not exist.");
        }

        if (wrapperKind is null)
        {
            throw codegenSwitch
                ? Refuse("COM_ERR_MISSING_WRAPPER_FOR_CODEGEN_ONLY",
                    "Unable to determine application type, since no wrapper function was specified. \nPlease use the -W or -l switch. \nType 'mcc -?' for further assistance.")
                : Refuse("COM_ERR_MISSING_WRAPPER",
                    "Unable to determine application type, since no wrapper function was specified. \nPlease use the -W switch or specify application type via -m or -l. \nType 'mcc -?' for further assistance.");
        }

        bool standalone = wrapperKind is "main" or "WinMain";
        if (standalone && link is not null && link.Split(':').Contains("lib"))
        {
            throw Refuse("COM_ERR_WRAPPER_TARGET_MISMATCH",
                "Error: The wrapper type specified with -W switch doesn't match with the target type specified with -T switch.");
        }

        if (line.Files.Count == 0 && line.Classes.Count == 0)
        {
            throw Refuse("COM_ERR_NO_FILES_SPECIFIED", "Error: No source files were specified (-? for help).");
        }

        if (wrapperKind == "dotnet")
        {
            line.DotNetTarget(wrapperParts, currentFolder);

            // -g is a .NET build's debug switch, not one a standalone build ignores (k8_build: debug).
            line.DebugBuild = line.IgnoredSwitches.Remove("g");
            line.IgnoredSwitches.RemoveAll(static s => s == "g");
        }
        else if (!standalone)
        {
            OtherTarget(wrapperKind, wrapperParts, mpsSwitch, separateSwitch);
        }

        line.Link = link is null ? MccLink.Program
            : link.Split(':').Contains("codegen") ? MccLink.ArchiveOnly
            : link.Split(':').Contains("compile") ? MccLink.ProgramBesideArchive
            : MccLink.Program;
        line.SeparateArchive = separateSwitch || mpsSwitch || line.Link == MccLink.ProgramBesideArchive;

        if (line.OutputName is { } name && !IsOutputName(name))
        {
            throw Refuse("COM_ERR_ILLEGAL_OUTPUT_NAME",
                $"Illegal output name: '{name}' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')");
        }

        foreach (string package in line.SupportPackages)
        {
            if (package is not ("autodetect" or "none"))
            {
                // R2025b answers this in its analysis (k7_switches: Z_bogus); JGraph has no support packages.
                throw Refuse("REQ_UNEXPECTED_ERROR",
                    "Error while determining required deployable files. Compilation terminated. Details:\n"
                    + "Error using matlab.depfun.internal.SPMode.validateSupportPackageInputs (line 69)\nUnknown support package inputs:\n\n"
                    + package + "\n\nThe input must be 'autodetect', 'none', or a list of installed support packages. Use the function "
                    + "\"compiler.codetools.deployableSupportPackages\" to find all installed support\npackages.\n");
            }
        }

        return line;
    }

    /// <summary>Whether R2025b takes <paramref name="name"/> for a program's name: a letter or <c>_</c>, then letters, digits and <c>_</c>.</summary>
    public static bool IsOutputName(string name) => Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>Where JGraph's bundle files are: <c>toolbox\compiler\bundles</c> beside JGraph, R2025b's names.</summary>
    public static string BundleFolder => Path.Combine(AppContext.BaseDirectory, "toolbox", "compiler", "bundles");

    /// <summary>
    /// <c>-T</c>'s value: words from link, compile, codegen, exe and lib joined by colons, any of them
    /// empty (k7_switches took <c>exe</c> and <c>link:</c> and refused <c>link:nothing</c>).
    /// </summary>
    private static bool IsLinkValue(string value) =>
        value.Split(':').All(static part => part is "" or "link" or "compile" or "codegen" or "exe" or "lib");

    /// <summary>
    /// <c>-W main:name[,version=v][,runtime=bundled|installed]</c> or <c>WinMain:</c> (any case);
    /// anything else after the name is taken and ignored, as R2025b's does (k7_switches). Any other
    /// target is checked where it is given and refused once the command line is read.
    /// </summary>
    private (string Kind, string[] Parts) Wrapper(string value)
    {
        int colon = value.IndexOf(':', StringComparison.Ordinal);
        string kind = colon < 0 ? value : value[..colon];
        string[] parts = colon < 0 ? [] : value[(colon + 1)..].Split(',');
        if (kind.Equals("main", StringComparison.OrdinalIgnoreCase) || kind.Equals("WinMain", StringComparison.OrdinalIgnoreCase))
        {
            Target = kind.Equals("main", StringComparison.OrdinalIgnoreCase) ? MccTarget.Console : MccTarget.Windowed;
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
                else if (part.StartsWith("runtime=", StringComparison.Ordinal))
                {
                    RuntimeDelivery = part["runtime=".Length..] switch
                    {
                        "bundled" => RuntimeDelivery.Bundled,
                        "installed" => RuntimeDelivery.Installed,
                        string other => throw new MccException("JGraph:mcc:runtimeDelivery",
                            $"'{other}' is not a way JGraph delivers its runtime with a program: use runtime=installed or runtime=bundled."),
                    };
                }
            }

            return (Target == MccTarget.Console ? "main" : "WinMain", parts);
        }

        if (!OtherTargets.ContainsKey(kind))
        {
            throw Refuse("COM_ERR_INVALID_WRAPPER_OPTION", $"Error: '{kind}' is not a valid -W option.");
        }

        string name = parts.Length > 0 ? parts[0] : "";
        switch (kind)
        {
            case "lib":
                if (name.Length == 0)
                {
                    throw Refuse("COM_ERR_ILLEGAL_OUTPUT_NAME",
                        "Illegal output name: '' \n (standalone and shared library target output names must begin with a letter or '_' and contain only alpha-numeric characters or '_')");
                }

                break;
            case "cpplib":
                if (name.Length == 0)
                {
                    throw Refuse("COM_ERR_COMPONENT_NAME_NEEDED_FOR_CPPLIB", "Error: A component name must be specified following \"cpplib:\".\n");
                }

                foreach (string part in parts.Skip(1))
                {
                    if (part == "named")
                    {
                        throw Refuse("COM_ERR_NAMED_CPPLIB_NOT_SUPPORTED_YET", "Error: The \"named\" interface for the C++ Shared Library target is not yet supported.\n");
                    }

                    if (part is not ("all" or "generic" or "legacy"))
                    {
                        throw Refuse("COM_ERR_BAD_ARG_FOR_CPPLIB", $"Error: Option \"{part}\" is not valid following \"cpplib:component_name,\".\n");
                    }
                }

                break;
            case "dotnet":
                RequireName(name);
                for (int p = 1; p < parts.Length; p++)
                {
                    if (!parts[p].StartsWith("api=", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (p != 1)
                    {
                        throw Refuse("COM_ERR_INVALID_API_PLACEMENT",
                            "Invalid placement of \"api=\" in -W or -B option. \"api=\" must be specified immediately after component name.");
                    }

                    if (parts[p]["api=".Length..] is not ("matlab-data" or "mwarray" or ""))
                    {
                        throw Refuse("COM_ERR_INVALID_API_VALUE",
                            $"Invalid value for \"api=\" specified in -W or -B option: {parts[p]["api=".Length..]}. Value must be a member of this set:\n\t'matlab-data'\n\t'mwarray'\n\t''");
                    }
                }

                // name[,api=...],class,version,...: the version after the class.
                int classAt = parts.Length > 1 && parts[1].StartsWith("api=", StringComparison.Ordinal) ? 2 : 1;
                if (parts.Length > classAt + 1 && parts[classAt + 1] is not ("" or "4.0" or "5.0"))
                {
                    throw Refuse("COM_ERR_NET_VERSION_INVALID",
                        "The .NET version is set to an unsupported value. Set the .NET version to \"4.0\" if targeting the .NET 4.x Framework. For .NET 5.0 and later, use \"5.0\".");
                }

                break;
            case "python" or "com" or "CTF" or "excel" or "mpsxl":
                RequireName(name);
                break;
        }

        return (kind, parts);
    }

    private static void RequireName(string name)
    {
        if (name.Length == 0)
        {
            throw Refuse("COM_ERR_ILLEGAL_COMPONENT_NAME", "Error: Component name invalid or unspecified (specify with -n or -W).");
        }
    }

    /// <summary>
    /// <c>-W dotnet:</c>'s parts once the command line is read (k8_build): the MWArray API built, the
    /// MATLAB Data API stage K9's, remoting refused, a key file that is not there R2025b's refusal.
    /// </summary>
    private void DotNetTarget(string[] parts, string? currentFolder)
    {
        string name = parts[0];
        int at = 1;
        string api = "";
        if (parts.Length > 1 && parts[1].StartsWith("api=", StringComparison.Ordinal))
        {
            api = parts[1]["api=".Length..];
            at = 2;
        }

        string Part(int offset) => parts.Length > at + offset ? parts[at + offset] : "";
        string className = Part(0);
        string framework = Part(1);
        string security = Part(2);
        string locality = Part(3);
        foreach (string part in parts.Skip(at + 4))
        {
            if (part.StartsWith("version=", StringComparison.Ordinal))
            {
                string text = part["version=".Length..];
                Version = ProgramVersion.Parse(text) ?? throw Refuse("COM_ERR_INVALID_VERSION", ProgramVersion.Refusal(text));
            }
        }

        if (api == "matlab-data")
        {
            throw new MccException("JGraph:mcc:targetNotBuilt",
                "JGraph's mcc does not build a .NET assembly for the MATLAB Data API (-W dotnet:...,api=matlab-data) yet.");
        }

        if (locality == "remote")
        {
            throw new MccException("JGraph:mcc:targetOutOfScope",
                "JGraph does not build remotable .NET assemblies (-W dotnet:...,remote): .NET 8 has no remoting.");
        }

        string? keyFile = null;
        if (security.Length > 0 && security != "Private")
        {
            keyFile = currentFolder is null || Path.IsPathRooted(security) ? security : Path.Combine(currentFolder, security);
            if (!File.Exists(keyFile))
            {
                throw Refuse("COM_ERR_SNK_FILE_NOT_EXIST", $"Strong name key file \"{security}\" does not exist.");
            }
        }

        var wrapper = new DotNetWrapper(name, "", framework.Length == 0 ? "4.0" : framework, keyFile);
        DotNet = wrapper with { ClassName = className.Length == 0 ? wrapper.ShortName : className };
        Target = MccTarget.DotNet;
    }

    /// <summary>A library or server target, once the command line is read: R2025b's last checks, then JGraph's refusal.</summary>
    private static void OtherTarget(string kind, string[] parts, bool mpsSwitch, bool separateSwitch)
    {
        if (kind == "CTF" && !mpsSwitch)
        {
            throw Refuse("COM_ERROR_MISSING_U_WITH_W_CTF", "The \"-U\" option must be used when \"-W CTF:\" is specified.");
        }

        if (kind == "spark" && !separateSwitch)
        {
            throw Refuse("COM_ERR_SPARK_MISSING_OPTION", "Error: Missing \"-C\" option to compile Spark component \n");
        }

        (string what, string? stage) = OtherTargets[kind];
        throw stage is null
            ? new MccException("JGraph:mcc:targetOutOfScope", $"JGraph does not build {what} (-W {kind}).")
            : new MccException("JGraph:mcc:targetNotBuilt", $"JGraph's mcc does not build {what} (-W {kind}) yet.");
    }

    /// <summary>
    /// A bundle's switches: the file read where given (against <paramref name="currentFolder"/>) or
    /// from <paramref name="bundleFolder"/>, split at white space, each <c>%n%</c> replaced by the n-th
    /// of the arguments after the name's colon (k7_switches, k7_more: a missing one refused, extra ones
    /// left unused, an empty list one empty argument).
    /// </summary>
    private static List<string> ReadBundle(string value, string? currentFolder, string bundleFolder)
    {
        int colon = BundleColon(value);
        string file = colon < 0 ? value : value[..colon];
        string[]? arguments = colon < 0 ? null : value[(colon + 1)..].Split(',');
        string given = Path.IsPathRooted(file) || currentFolder is null ? file : Path.Combine(currentFolder, file);
        string? path = File.Exists(given) ? given
            : !Path.IsPathRooted(file) && File.Exists(Path.Combine(bundleFolder, file)) ? Path.Combine(bundleFolder, file)
            : null;
        if (path is null)
        {
            throw Refuse("COM_ERR_UNABLE_TO_OPEN_FILE", $"Error: Unable to open file \"{file}\": No such file or directory.");
        }

        var words = new List<string>();
        foreach (string word in File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add(Regex.Replace(word, "%([0-9]+)%", match =>
            {
                int n = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return arguments is not null && n >= 1 && n <= arguments.Length
                    ? arguments[n - 1]
                    : throw Refuse("COM_ERR_ILLEGAL_BUNDLE_REPLACEMENT", "Error: Illegal bundle file replacement argument.");
            }));
        }

        return words;
    }

    /// <summary>The colon that starts a bundle's arguments: not a drive's, nor one a folder separator follows.</summary>
    private static int BundleColon(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == ':' && !(i == 1 && char.IsAsciiLetter(value[0]))
                && !(i + 1 < value.Length && value[i + 1] is '\\' or '/'))
            {
                return i;
            }
        }

        return -1;
    }

    private static MccException Refuse(string id, string text) => new(Prefix + id, text);

    /// <summary>
    /// <c>mcc -?</c> and <c>mcc</c> alone: JGraph's own help, in R2025b's order (the switches every target
    /// reads, then each target's command), naming what JGraph's mcc builds and what it does not yet.
    /// </summary>
    public const string HelpText =
        "mcc  Build MATLAB code into a program that runs on the JGraph Runtime.\n"
        + "  mcc [-options] mainFile [moreFiles...]\n\n"
        + "Switches every target reads:\n"
        + "  -?            Print this help\n"
        + "  -a <path>     Put a file or folder in the archive as well\n"
        + "  -d <folder>   Write the build into this folder, making it if need be\n"
        + "  -I <folder>   Look for code in this folder too\n"
        + "  -v            Report each step of the build\n"
        + "  -w <what>     Show, hide or fail on mcc's own warnings: list, enable, disable or error,\n"
        + "                alone or followed by :<identifier>\n"
        + "  -B <file>     Read more switches from a bundle file, filling %1%, %2%... from <file>:<a>,<b>...\n\n"
        + "JGraph builds\n"
        + "  A console program\n"
        + "  mcc -m <mainFile.m>\n\n"
        + "  A windowed program, with no console\n"
        + "  mcc -e <mainFile.m>\n\n"
        + "  For either: -o <name> names the program, -n passes numbers as numbers, -C writes the archive\n"
        + "  beside the program, -r <icon> and -h <helpFile> set its icon and its -? text, -j stores the code\n"
        + "  as P-files, -R <option> sets a runtime option, -K keeps what a failed build wrote, -T codegen\n"
        + "  writes the archive alone, and -W main:<name>,runtime=bundled copies the runtime beside the program.\n\n"
        + "JGraph does not build these yet\n"
        + "  C or C++ shared library      mcc -W lib:<name> <file.m>      mcc -W cpplib:<name> <file.m>\n"
        + "  .NET assembly                mcc -W 'dotnet:<assembly>,<class>' <file.m>\n"
        + "  Python package               mcc -W python:<package> <file.m>\n"
        + "  Production Server archive    mcc -W CTF:<archive> -U <file.m>\n"
        + "  Excel add-in                 mcc -W 'excel:<addin>,<class>' -b <file.m>\n\n"
        + "From a Windows command prompt, run jgmcc (or jgraph mcc) and use double quotes where these use single.\n";

    /// <summary><c>mcc -/?</c>: the trace switches, in JGraph's words.</summary>
    public const string TraceHelpText =
        "mcc's trace switches:\n"
        + "  -/t   Print the command line as mcc read it (letters apart, bundles in place), then build.\n"
        + "  -/s   Taken, as -/t: JGraph's mcc has no signature listing.\n"
        + "  -/a   Both of the above.\n";
}
