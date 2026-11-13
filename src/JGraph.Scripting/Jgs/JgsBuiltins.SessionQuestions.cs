using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The questions a program asks about the session it runs in that JGraph had no answer for (open items
/// 169 and 245): <c>ver</c>, <c>prefdir</c>, <c>userpath</c>, <c>usejava</c>, <c>inmem</c>, and
/// <c>mlock</c>, <c>munlock</c> and <c>mislocked</c>. Each was measured in R2025b, in a session and, for
/// a deployed program, in K0's <c>k0_env</c> recordings (<c>tools/matlab-checklist/compiler-probes</c>).
/// </summary>
/// <remarks>
/// <c>feature('numcores')</c> is <c>feature</c>'s (<see cref="JgsBuiltins"/>' MATLAB layer), and
/// <c>dbstack</c> the debugger's (W11).
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The fields of <c>ver</c>'s struct, in R2025b's order.</summary>
    private static readonly string[] VerFields = ["Name", "Version", "Release", "Date"];

    /// <summary>
    /// The release JGraph's MATLAB dialect follows (decision KO2, as <c>mcrversion</c> answers it): the
    /// <c>MATLAB</c>, <c>MATLAB Compiler</c> and <c>MATLAB Compiler SDK</c> rows of <c>ver</c> carry
    /// R2025b's version, release and date (measured: 25.2, (R2025b), 28-Jul-2025).
    /// </summary>
    private const string DialectVersion = "25.2";

    private const string DialectRelease = "(R2025b)";

    private const string DialectDate = "28-Jul-2025";

    // ver's row layout (R2025b's ver.m, locConvertStructInfoToDispString): the name in 50 columns, cut
    // to 47 and '...' when longer, four blanks, 'Version x' in 16, four blanks, the release in the
    // widest release's width (seven at least), then six blanks and the licence under -support.
    private const int VerNameWidth = 50;

    private const int VerVersionWidth = 16;

    // A Contents.m file's second line: 'Version x.y (Rxxxx) dd-Mmm-yyyy' (ver.m's detailsPat).
    private static readonly Regex VerDetails = new(@"^Version\s+(\S+)\s*(.*?)\s+(\S+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>One product <c>ver</c> lists, and the folders a name may ask it by.</summary>
    private sealed record VerProduct(string Name, string Version, string Release, string Date, string[] Folders);

    /// <summary>Declares the session questions into <paramref name="env"/> (open items 169 and 245).</summary>
    private static void RegisterSessionQuestions(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Query(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        void Command(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true, BindsAnsAsStatement = false }));

        void Answers(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body, bool statementPrints) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => body(args, 1, line, col) is [JgsValue first, ..] ? first : JgsValue.Null)
            {
                AutoCallsBare = true,
                BindsAnsAsStatement = !statementPrints,
                KnowsWhenDiscarded = statementPrints,
                MultiOutput = body,
            }));

        Answers("ver", (args, wanted, line, col) => Ver(host, args, wanted, line, col), statementPrints: true);

        // prefdir: a builtin in R2025b, so its refusals are maxrhs and maxlhs. prefdir(1) makes the folder.
        Query("prefdir", (args, line, col) =>
        {
            TooManyInputs(args, 1, "MATLAB:maxrhs", line, col);
            bool create = false;
            if (args.Count == 1)
            {
                // A scalar number or logical is its own type; an array, text and the rest are not.
                if (args[0].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:prefs:BadInput", "PREFDIR input must be numeric or logical scalar.");
                }

                create = args[0].AsBool;
            }

            string folder = PreferencesFolder(host);
            if (create)
            {
                Directory.CreateDirectory(folder);
            }

            return JgsValue.Str(folder);
        });

        // userpath: R2025b's toolbox/local/userpath.m. The folder answers '' when it is not there.
        string? userFolder = DefaultUserPath();
        Query("userpath", (args, line, col) =>
        {
            TooManyInputs(args, 1, "MATLAB:TooManyInputs", line, col);
            if (args.Count == 0)
            {
                return JgsValue.Str(userFolder is { Length: > 0 } f && Directory.Exists(f) && Path.IsPathFullyQualified(f) ? f : string.Empty);
            }

            if (!IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                    $"Expected input to be one of these types:\n\nstring, char\n\nInstead its type was {ClassOf(args[0], JgsDialect.Matlab)}.");
            }

            if (host.Deployment is not null)
            {
                throw new JgsRuntimeException(line, col, PathAlterationId, PathAlterationMessage);
            }

            string asked = TextOf(args[0]);
            string? next = asked switch
            {
                "reset" => DefaultUserPath(),
                "clear" => null,
                _ => Directory.Exists(asked) && Path.IsPathFullyQualified(asked)
                    ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(asked))
                    : throw new JgsRuntimeException(line, col, "MATLAB:userpath:invalidInput", "Invalid directory or directory does not exist"),
            };

            // The old folder leaves the search path and the new one goes first, as R2025b's does.
            if (userFolder is { Length: > 0 } old)
            {
                host.FunctionPath?.Remove(old);
            }

            if (next is { Length: > 0 } && Directory.Exists(next))
            {
                host.FunctionPath?.Add(next, atEnd: false);
            }

            userFolder = next;
            return JgsValue.Null;
        });

        // usejava: JGraph runs on .NET and has no Java, so jvm, awt and swing are false - what a script
        // guarding its java.* calls must see; desktop is true under the Script Workspace's window, the one
        // host that gives a session a table viewer, and false in -batch, a console or a deployed program.
        Query("usejava", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            TooManyInputs(args, 1, "MATLAB:TooManyInputs", line, col);
            if (!IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:unassignedOutputs",
                    "One or more output arguments not assigned during call to \"_feature_useJava\".");
            }

            return JgsValue.Bool(TextOf(args[0]).Equals("desktop", StringComparison.OrdinalIgnoreCase) && host.TableViewer is not null);
        });

        Answers("inmem", (args, wanted, line, col) => Inmem(interpreter, host, args, wanted, line, col), statementPrints: false);

        // mlock, munlock, mislocked: the lock is the running file's (a local function's is its file's);
        // at a script's top level or the prompt there is no file to lock and nothing happens.
        Command("mlock", (args, line, col) =>
        {
            TooManyInputs(args, 0, "MATLAB:maxrhs", line, col);
            if (interpreter.RunningCodeFile is { } file)
            {
                interpreter.SetFileLocked(file, locked: true);
            }

            return JgsValue.Null;
        });

        Command("munlock", (args, line, col) =>
        {
            TooManyInputs(args, 1, "MATLAB:maxrhs", line, col);
            string? file = args.Count == 0 ? interpreter.RunningCodeFile : LockedFileOf(env, host, args[0], refuseUnknown: true, line, col);
            if (file is not null)
            {
                interpreter.SetFileLocked(file, locked: false);
            }

            return JgsValue.Null;
        });

        Query("mislocked", (args, line, col) =>
        {
            TooManyInputs(args, 1, "MATLAB:maxrhs", line, col);
            string? file = args.Count == 0 ? interpreter.RunningCodeFile : LockedFileOf(env, host, args[0], refuseUnknown: false, line, col);
            return JgsValue.Bool(file is not null && interpreter.IsFileLocked(file));
        });
    }

    // --- prefdir and userpath ---------------------------------------------------------------------

    /// <summary>
    /// The preferences folder: a deployed program's (<see cref="ScriptDeployment.PreferencesFolder"/>), else
    /// the folder JGraph keeps its own settings and the device preferences in - <c>%APPDATA%\JGraph</c>, or
    /// the folder <c>JGRAPH_PREFDIR</c> names, as the device preferences read it.
    /// </summary>
    private static string PreferencesFolder(JGraphScriptGlobals host)
    {
        if (host.Deployment is { } deployment)
        {
            return deployment.PreferencesFolder;
        }

        string? named = Environment.GetEnvironmentVariable("JGRAPH_PREFDIR");
        return string.IsNullOrEmpty(named)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph")
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(named));
    }

    /// <summary>R2025b's default user folder, <c>Documents\MATLAB</c> - the one <c>createProject</c>'s default root sits under.</summary>
    private static string? DefaultUserPath()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length == 0 ? null : Path.Combine(documents, "MATLAB");
    }

    // --- mlock ------------------------------------------------------------------------------------

    /// <summary>
    /// The file <c>munlock(name)</c> or <c>mislocked(name)</c> asks about: a file's own path, or the file
    /// the name resolves to on the path; null for a builtin or a name that is nothing - which
    /// <c>munlock</c> refuses as R2025b does ("The file 'x' is not locked.") and <c>mislocked</c> answers false.
    /// </summary>
    private static string? LockedFileOf(JgsEnvironment env, JGraphScriptGlobals host, JgsValue asked, bool refuseUnknown, int line, int col)
    {
        if (!IsTextScalar(asked))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
        }

        string name = TextOf(asked);
        if (name.Length > 0 && Path.IsPathFullyQualified(name) && File.Exists(name))
        {
            return Path.GetFullPath(name);
        }

        if (host.FunctionPath?.Find(name) is { } file)
        {
            return file;
        }

        if (refuseUnknown && !env.Builtins.TryGet(name, out _))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:mlockNotLocked", $"The file '{name}' is not locked.");
        }

        return null;
    }

    // --- inmem ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[M, X, C] = inmem</c>: the files loaded now, in the order they were loaded, as a column of
    /// names (full paths under <c>'-completenames'</c>, the option matched as written; any other text is
    /// no option and a further argument is passed over, R2025b, measured); no MEX files ever, a 0-by-1
    /// cell; and the classes defined from files. R2025b's own toolbox files are in its list; JGraph's
    /// builtins are no files, so its list holds the program's files alone.
    /// </summary>
    private static JgsValue[] Inmem(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (wanted > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxlhs", "Too many output arguments.");
        }

        if (args.Count > 0 && !IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
        }

        bool complete = args.Count > 0 && TextOf(args[0]) == "-completenames";
        var names = new List<string>();
        var seen = new HashSet<string>(JgsFunctionPath.PathComparer);
        if (host.FunctionPath is { } path)
        {
            foreach ((string name, string file) in path.LoadedFiles())
            {
                if (seen.Add(file))
                {
                    names.Add(complete ? file : name);
                }
            }
        }

        var classes = new List<string>();
        foreach ((string name, JgsClass definition) in interpreter.Classes)
        {
            if (definition.FileWritten is null)
            {
                continue;
            }

            classes.Add(name);
            string file = definition.Declaration.SourceId;
            if (file.Length > 0 && seen.Add(file))
            {
                names.Add(complete ? file : name);
            }
        }

        var answers = new JgsValue[Math.Max(wanted, 1)];
        answers[0] = ColumnOfText(names);
        if (answers.Length > 1)
        {
            answers[1] = ColumnOfText([]);
        }

        if (answers.Length > 2)
        {
            answers[2] = ColumnOfText(classes);
        }

        return answers;
    }

    /// <summary>The texts as an n-by-1 cell of char rows.</summary>
    private static JgsValue ColumnOfText(IReadOnlyList<string> texts)
    {
        JgsValue cell = JgsValue.Cell([.. texts.Select(JgsValue.Str)]);
        cell.Reshape(texts.Count, 1);
        return cell;
    }

    // --- ver --------------------------------------------------------------------------------------

    /// <summary>
    /// <c>ver</c>, <c>ver(product)</c>, <c>v = ver</c>, <c>v = ver(product)</c> and <c>ver -support</c>,
    /// R2025b's <c>ver.m</c> transcribed: the statement form prints the session's header and the product
    /// rows, the output form answers a struct array with Name, Version, Release and Date, sorted by name;
    /// a product is asked for by its folder's name (<c>matlab</c>, <c>compiler</c>, a toolbox's own
    /// folder), case aside, and an unknown one answers a 0-by-0 struct, the statement form warning
    /// <c>MATLAB:ver:NotFound</c>.
    /// </summary>
    private static JgsValue[] Ver(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        TooManyInputs(args, 1, "MATLAB:TooManyInputs", line, col);
        if (wanted > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        bool display = wanted == 0;
        if (display)
        {
            WriteVerHeader(host);
        }

        bool support = false;
        List<VerProduct> products;
        if (args.Count == 0)
        {
            products = AllProducts(host);
        }
        else if (!IsTextScalar(args[0]))
        {
            products = [];
            Warn(host, "MATLAB:ver:NotAString", "Argument must be a string scalar or character vector.");
        }
        else if (TextOf(args[0]).Equals("-support", StringComparison.OrdinalIgnoreCase))
        {
            support = true;
            products = AllProducts(host);
        }
        else
        {
            string asked = TextOf(args[0]);
            products = [.. ProductsAt(host).Where(p => p.Folders.Any(folder => FolderTailIs(folder, asked)))];
            if (products.Count == 0 && display)
            {
                Warn(host, "MATLAB:ver:NotFound", $"No properly formatted Contents.m file was found for toolbox '{asked}'.");
            }
        }

        string licence = host.Deployment is null ? "JGraph" : "unknown";
        if (display)
        {
            WriteVerRows(host, products, support ? licence : null);
            return [JgsValue.Null];
        }

        return [VerStruct(products, support ? licence : null)];
    }

    /// <summary>
    /// What <c>ver</c> lists, sorted by name as R2025b's <c>sort</c> orders text (by character code):
    /// JGraph itself, whose version and release are <c>version</c>'s two parts ('0.2.3 (JGraph)': '0.2.3'
    /// and '(JGraph)', as R2025b's MATLAB row splits '25.2.0.2998904 (R2025b)'); <c>MATLAB</c>, the
    /// dialect JGraph follows, as R2025b's row reads; in a session that can compile, <c>MATLAB Compiler</c>
    /// and <c>MATLAB Compiler SDK</c>, whose builds JGraph's <c>mcc</c> and <c>compiler.build</c> make; and
    /// every toolbox on the search path whose <c>Contents.m</c> names a version.
    /// </summary>
    private static List<VerProduct> AllProducts(JGraphScriptGlobals host)
    {
        List<VerProduct> all = ProductsAt(host);
        all.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return all;
    }

    /// <summary>The products in the order a single question finds them: JGraph's own, then the search path's.</summary>
    private static List<VerProduct> ProductsAt(JGraphScriptGlobals host)
    {
        string release = typeof(JGraphScriptGlobals).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string built = File.GetLastWriteTime(typeof(JGraphScriptGlobals).Assembly.Location).ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        var products = new List<VerProduct>
        {
            new("MATLAB", DialectVersion, DialectRelease, DialectDate, ["toolbox/matlab/general", "toolbox/matlab"]),
            new("JGraph", release, "(JGraph)", built, ["toolbox/jgraph"]),
        };

        if (host.Deployment is null)
        {
            products.Add(new("MATLAB Compiler", DialectVersion, DialectRelease, DialectDate, ["toolbox/compiler"]));
            products.Add(new("MATLAB Compiler SDK", DialectVersion, DialectRelease, DialectDate, ["toolbox/compiler_sdk"]));
        }

        var seen = new HashSet<string>(JgsFunctionPath.PathComparer);
        foreach (string folder in host.FunctionPath?.Folders ?? [])
        {
            // A package's or class's folder is passed over, as ver.m's locRemoveUnwantedEntries passes it.
            if (!seen.Add(folder) || folder.Contains(Path.DirectorySeparatorChar + "+", StringComparison.Ordinal)
                || folder.Contains(Path.DirectorySeparatorChar + "@", StringComparison.Ordinal))
            {
                continue;
            }

            if (ContentsProduct(folder) is { } product)
            {
                products.Add(product);
            }
        }

        return products;
    }

    /// <summary>
    /// The product a folder's <c>Contents.m</c> describes (ver.m's locParseContentsFiles): its first line,
    /// less the comment character, blanks and a final full stop, is the name; its second must read
    /// <c>Version v [release] date</c>; null when there is no such file or the second line is not that.
    /// </summary>
    private static VerProduct? ContentsProduct(string folder)
    {
        string file = Path.Combine(folder, "Contents.m");
        string[] lines;
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            lines = File.ReadLines(file).Take(2).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        static string Stripped(string[] lines, int at) => at < lines.Length && lines[at].Length > 0 ? lines[at][1..].Trim() : string.Empty;
        string name = Stripped(lines, 0);
        if (name.EndsWith('.'))
        {
            name = name[..^1];
        }

        Match details = VerDetails.Match(Stripped(lines, 1));
        return details.Success
            ? new VerProduct(name, details.Groups[1].Value, details.Groups[2].Value, CleanVerDate(details.Groups[3].Value), [folder.Replace('\\', '/')])
            : null;
    }

    /// <summary>ver.m's locCleanDate: a day of one digit padded, a month of three letters or more written Mmm, when the date has two dashes.</summary>
    private static string CleanVerDate(string date)
    {
        int first = date.IndexOf('-');
        int second = first < 0 ? -1 : date.IndexOf('-', first + 1);
        if (second < 0)
        {
            return date;
        }

        string day = date[..first];
        string month = date[(first + 1)..second];
        string year = date[(second + 1)..];
        if (day.Length == 1)
        {
            day = "0" + day;
        }

        if (month.Length > 2)
        {
            month = char.ToUpperInvariant(month[0]) + month[1..3].ToLowerInvariant();
        }

        return day + "-" + month + "-" + year;
    }

    /// <summary>
    /// Whether <paramref name="asked"/> names <paramref name="folder"/>: its last folders, separated by
    /// either slash, compared without case (R2025b's <c>what(name, '-caseinsensitive')</c>, so
    /// <c>matlab</c>, <c>general</c>, <c>Toolbox/Matlab</c> and <c>toolbox\matlab</c> all reach MATLAB's row).
    /// </summary>
    private static bool FolderTailIs(string folder, string asked)
    {
        string[] want = asked.Split('/', '\\');
        string[] have = folder.Replace('\\', '/').TrimEnd('/').Split('/');
        if (want.Length == 0 || want.Length > have.Length || want.Any(static part => part.Length == 0))
        {
            return false;
        }

        for (int i = 1; i <= want.Length; i++)
        {
            if (!want[^i].Equals(have[^i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The products as <c>ver</c>'s struct: 1-by-n, 0-by-0 for none, with Licenses after Date under -support.</summary>
    private static JgsValue VerStruct(List<VerProduct> products, string? licence)
    {
        string[] fields = licence is null ? VerFields : [.. VerFields, "Licenses"];
        var rows = new Dictionary<string, JgsValue>[products.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            VerProduct p = products[i];
            rows[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(p.Name),
                ["Version"] = JgsValue.Str(p.Version),
                ["Release"] = JgsValue.Str(p.Release),
                ["Date"] = JgsValue.Str(p.Date),
            };
            if (licence is not null)
            {
                rows[i]["Licenses"] = JgsValue.Cell([JgsValue.Str(licence)]);
            }
        }

        return rows.Length switch
        {
            0 => JgsValue.StructArray(new JgsStructArray([], fields), 0, 0),
            1 => JgsValue.Struct(rows[0]),
            _ => JgsValue.StructArray(new JgsStructArray(rows, fields), 1, rows.Length),
        };
    }

    /// <summary>
    /// The header <c>ver</c> prints first (ver.m's locDisplayMatlabInformation): the version, the licence,
    /// the operating system and Java's, between rules of dashes as long as the longest line.
    /// </summary>
    private static void WriteVerHeader(JGraphScriptGlobals host)
    {
        string release = typeof(JGraphScriptGlobals).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string[] lines =
        [
            $"JGraph Version: {release} (JGraph)",
            $"JGraph License Number: {(host.Deployment is null ? "JGraph" : "unknown")}",
            $"Operating System: {OperatingSystemLine()}",
            "Java Version: Java is not enabled",
        ];
        string rule = new('-', lines.Max(static l => l.Length));
        var text = new StringBuilder();
        text.Append(rule).Append('\n');
        foreach (string l in lines)
        {
            text.Append(l).Append('\n');
        }

        text.Append(rule).Append('\n');
        host.WriteOut(text.ToString());
    }

    /// <summary>The operating system as R2025b's header names Windows ('Microsoft Windows 11 Home Version 10.0 (Build 26200)', less the edition).</summary>
    private static string OperatingSystemLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        Version v = Environment.OSVersion.Version;
        return $"Microsoft Windows {(v.Build >= 22000 ? "11" : "10")} Version {v.Major}.{v.Minor} (Build {v.Build})";
    }

    /// <summary>
    /// The product rows as <c>ver</c> prints them: MATLAB's first, then the rest by name, each laid out
    /// as ver.m's sprintf lays it out and trailing blanks dropped, then every row padded to the longest,
    /// as <c>disp(char(rows))</c> pads them.
    /// </summary>
    private static void WriteVerRows(JGraphScriptGlobals host, List<VerProduct> products, string? licence)
    {
        if (products.Count == 0)
        {
            return;
        }

        var ordered = new List<VerProduct>(products);
        int matlab = ordered.FindIndex(static p => p.Name.Equals("MATLAB", StringComparison.OrdinalIgnoreCase));
        if (matlab > 0)
        {
            VerProduct first = ordered[matlab];
            ordered.RemoveAt(matlab);
            ordered.Insert(0, first);
        }

        int releaseWidth = Math.Max(7, products.Max(static p => p.Release.Length));
        var rows = new List<string>(ordered.Count);
        foreach (VerProduct p in ordered)
        {
            string name = p.Name.Length > VerNameWidth ? p.Name[..(VerNameWidth - 3)] + "..." : p.Name;
            string row = name.PadRight(VerNameWidth) + "    " + ("Version " + p.Version).PadRight(VerVersionWidth) + "    "
                + p.Release.PadRight(releaseWidth) + "      " + (licence is null ? string.Empty : "License " + licence);
            rows.Add(row.TrimEnd(' ', '\t', '\0'));
        }

        int width = rows.Max(static r => r.Length);
        var text = new StringBuilder();
        foreach (string row in rows)
        {
            text.Append(row.PadRight(width)).Append('\n');
        }

        host.WriteOut(text.ToString());
    }
}
