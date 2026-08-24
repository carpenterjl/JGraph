using System.Runtime.InteropServices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The console-session commands (<c>diary</c>, <c>more</c>, <c>input</c>, <c>lookfor</c>) and the
/// questions a script asks about the installation it is running on (<c>version</c>,
/// <c>computer</c>, <c>memory</c>).
/// </summary>
/// <remarks>
/// Several of these describe a teletype MATLAB session that JGraph's console is not: paging, echoing
/// each line of a function file, rebuilding a function cache. Where the behaviour does not exist,
/// the builtin still keeps and reports the setting, so a ported script runs and reads back what it
/// set — but it says so in the documentation rather than pretending the setting did something.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Session settings that survive between statements but not between sessions.</summary>
    private sealed class SessionState
    {
        public bool Echo { get; set; }

        public bool Paging { get; set; }

        public int PageSize { get; set; } = 20;

        public bool Beep { get; set; } = true;

        public string Planner { get; set; } = "estimate";

        public int ComputationThreads { get; set; } = Environment.ProcessorCount;
    }

    /// <summary>Every name <see cref="RegisterSessionBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> SessionBuiltinNames { get; } =
    [
        "diary", "echo", "home", "more", "input", "lookfor", "what",
        "beep", "pack", "recycle", "rehash", "display",
        "version", "computer", "matlabroot", "matlabdrive", "license", "isstudent",
        "memory", "maxNumCompThreads", "fftw",
    ];

    /// <summary>Declares the session and installation builtins into <paramref name="env"/> (M39).</summary>
    internal static void RegisterSessionBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        var state = new SessionState();

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // Commands rather than questions: a bare 'diary on' should not leave ans behind or echo.
        void Command(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // A question that takes no arguments has to answer when its bare name is mentioned, or
        // disp(computer) hands disp the function instead of the platform name. Callee position is
        // exempted by the interpreter, so computer('arch') still reaches the function.
        void Query(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        RegisterConsoleSession(Define, Command, Query, host, state);
        RegisterInstallationQueries(Query, state);
    }

    // --- The console session ----------------------------------------------------------------------

    private static void RegisterConsoleSession(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Command,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Query,
        JGraphScriptGlobals host, SessionState state)
    {
        Command("diary", (args, line, col) =>
        {
            ArityRange("diary", args, 0, 1, line, col);

            // Bare 'diary' toggles; a word turns it on or off; anything else is a file name.
            string word = args.Count == 1 ? Str("diary", args, 0, line, col) : string.Empty;
            switch (word)
            {
                case "off":
                    host.StopDiary();
                    return JgsValue.Null;

                case "on":
                    host.StartDiary(host.ResolveForWrite("diary"));
                    return JgsValue.Null;

                case "":
                    if (host.DiaryPath is null)
                    {
                        host.StartDiary(host.ResolveForWrite("diary"));
                    }
                    else
                    {
                        host.StopDiary();
                    }

                    return JgsValue.Null;

                default:
                    host.StartDiary(host.ResolveForWrite(word));
                    return JgsValue.Null;
            }
        });

        Command("echo", (args, line, col) =>
        {
            ArityRange("echo", args, 0, 2, line, col);
            state.Echo = Switched("echo", args, state.Echo, line, col);
            return JgsValue.Null;
        });

        Command("home", (args, line, col) =>
        {
            Arity("home", args, 0, line, col);

            // A teletype's 'home' put the cursor at the top without erasing; JGraph's console has no
            // cursor to move, so this is clc, which is the visible effect either way.
            host.ClearOutput();
            return JgsValue.Null;
        });

        Command("more", (args, line, col) =>
        {
            ArityRange("more", args, 0, 1, line, col);
            if (args.Count == 1 && args[0].Type is JgsType.Number or JgsType.Bool)
            {
                state.PageSize = Count("more", args, 0, line, col);
                state.Paging = true;
                return JgsValue.Null;
            }

            state.Paging = Switched("more", args, state.Paging, line, col);
            return JgsValue.Null;
        });

        Define("input", (args, line, col) =>
        {
            ArityRange("input", args, 1, 2, line, col);
            host.WriteOut(Str("input", args, 0, line, col));

            string? typed = Console.In.ReadLine();
            if (typed is null)
            {
                throw new JgsRuntimeException(line, col,
                    "input needs a console to read from; there is none when a script runs inside the workspace window.");
            }

            // Without 's' the text is evaluated as an expression, which is what makes input(prompt)
            // return a number for a typed number.
            if (args.Count == 2 && Str("input", args, 1, line, col) == "s")
            {
                return JgsValue.Str(typed);
            }

            return typed.Trim().Length == 0 ? JgsValue.Null : ParseTypedValue(typed, line, col);
        });

        Define("lookfor", (args, line, col) =>
        {
            Arity("lookfor", args, 1, line, col);
            string word = Str("lookfor", args, 0, line, col);
            var found = new List<JgsValue>();

            foreach (JgsBuiltinInfo entry in JgsBuiltinCatalog.All)
            {
                if (entry.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || entry.Summary.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    host.WriteOut($"{entry.Name} - {entry.Summary}\n");
                    found.Add(JgsValue.Str(entry.Name));
                }
            }

            return JgsValue.Cell([.. found]);
        });

        Query("what", (args, line, col) =>
        {
            ArityRange("what", args, 0, 1, line, col);
            string folder = args.Count == 1 ? host.Resolve(Str("what", args, 0, line, col)) : host.CurrentDirectory;
            if (!Directory.Exists(folder))
            {
                throw new JgsRuntimeException(line, col, $"what: '{folder}' is not a folder.");
            }

            // MATLAB groups a folder's contents by kind. JGraph's kinds are its own script and
            // figure files rather than MATLAB's p-files and classes.
            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["path"] = JgsValue.Str(folder),
                ["m"] = NamesIn(folder, "*.m"),
                ["jgs"] = NamesIn(folder, "*.jgs"),
                ["mat"] = NamesIn(folder, "*.mat"),
                ["fig"] = NamesIn(folder, "*.graph"),
            });
        });

        Command("beep", (args, line, col) =>
        {
            ArityRange("beep", args, 0, 1, line, col);
            if (args.Count == 1)
            {
                state.Beep = Switched("beep", args, state.Beep, line, col);
                return JgsValue.Null;
            }

            if (state.Beep)
            {
                host.WriteOut("\a");
            }

            return JgsValue.Null;
        });

        Command("pack", (args, line, col) =>
        {
            ArityRange("pack", args, 0, 1, line, col);

            // MATLAB's pack defragments its workspace by saving and reloading it. The .NET heap
            // compacts itself, so asking the collector to do it now is the honest equivalent.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return JgsValue.Null;
        });

        Query("recycle", (args, line, col) =>
        {
            ArityRange("recycle", args, 0, 1, line, col);
            if (args.Count == 1 && Str("recycle", args, 0, line, col) == "on")
            {
                // Reporting 'on' while delete still removed the file outright would be worse than
                // refusing: a caller turns this on precisely so a mistake stays recoverable.
                throw new JgsRuntimeException(line, col,
                    "recycle: JGraph's delete always removes the file; there is no recycle bin behind it.");
            }

            return JgsValue.Str("off");
        });

        Command("rehash", (args, line, col) =>
        {
            ArityRange("rehash", args, 0, 1, line, col);

            // MATLAB caches which files hold which functions and needs telling when that changes.
            // JGraph looks on disk at the moment of the call, so there is nothing to rebuild.
            return JgsValue.Null;
        });

        Command("display", (args, line, col) =>
        {
            Arity("display", args, 1, line, col);
            host.WriteOut(args[0].Display() + "\n");
            return JgsValue.Null;
        });
    }

    /// <summary>The file names matching a pattern, as a cell of strings.</summary>
    private static JgsValue NamesIn(string folder, string pattern)
    {
        string[] files = Directory.GetFiles(folder, pattern);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return JgsValue.Cell([.. files.Select(f => JgsValue.Str(Path.GetFileName(f)))]);
    }

    /// <summary>Reads an 'on'/'off' argument, toggling when there is none.</summary>
    private static bool Switched(string name, IReadOnlyList<JgsValue> args, bool current, int line, int col)
    {
        if (args.Count == 0)
        {
            return !current;
        }

        string word = Str(name, args, args.Count - 1, line, col);
        return word switch
        {
            "on" => true,
            "off" => false,
            _ => throw new JgsRuntimeException(line, col, $"{name}: expected 'on' or 'off', not '{word}'."),
        };
    }

    /// <summary>
    /// Turns text typed at an <c>input</c> prompt into a value. A number is the overwhelmingly
    /// common case and is read directly; anything else is handed back as text, because evaluating
    /// arbitrary typed input as an expression is not something a prompt should do quietly.
    /// </summary>
    private static JgsValue ParseTypedValue(string typed, int line, int col)
    {
        _ = line;
        _ = col;
        return double.TryParse(typed.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? JgsValue.Number(number)
            : JgsValue.Str(typed);
    }

    // --- The installation -------------------------------------------------------------------------

    private static void RegisterInstallationQueries(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Query, SessionState state)
    {
        string release = typeof(JGraphScriptGlobals).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        Query("version", (args, line, col) =>
        {
            ArityRange("version", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                return JgsValue.Str(release + " (JGraph)");
            }

            string option = Str("version", args, 0, line, col);
            return option switch
            {
                "-release" => JgsValue.Str(release),
                "-description" => JgsValue.Str("JGraph"),
                "-date" => JgsValue.Str(
                    File.GetLastWriteTime(typeof(JGraphScriptGlobals).Assembly.Location).ToString("MMMM d, yyyy",
                        System.Globalization.CultureInfo.InvariantCulture)),
                "-java" => JgsValue.Str("JGraph runs on .NET; there is no Java virtual machine to report."),
                "-blas" or "-lapack" => JgsValue.Str(JGraph.Numerics.LinearAlgebra.LinalgProvider.StatusReport),
                _ => throw new JgsRuntimeException(line, col, $"version does not recognize the option '{option}'."),
            };
        });

        Query("computer", (args, line, col) =>
        {
            ArityRange("computer", args, 0, 1, line, col);
            bool architecture = args.Count == 1 && Str("computer", args, 0, line, col) == "arch";

            (string name, string arch) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ("PCWIN64", "win64")
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "MACA64" : "MACI64",
                       RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "maca64" : "maci64")
                    : ("GLNXA64", "glnxa64");

            return JgsValue.Str(architecture ? arch : name);
        });

        Query("matlabroot", (args, line, col) =>
        {
            Arity("matlabroot", args, 0, line, col);

            // The installation folder, which for a ported script is what this name means: where the
            // running program's own files live.
            return JgsValue.Str(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        });

        Query("matlabdrive", (args, line, col) =>
        {
            Arity("matlabdrive", args, 0, line, col);

            // There is no cloud drive to point at, and an invented path would be worse than none.
            return JgsValue.Str(string.Empty);
        });

        Query("license", (args, line, col) =>
        {
            ArityRange("license", args, 0, 2, line, col);

            // Every feature is present in every copy, so a test always succeeds.
            return args.Count > 0 && Str("license", args, 0, line, col) == "test"
                ? JgsValue.Number(1)
                : JgsValue.Str("JGraph");
        });

        Query("isstudent", (args, line, col) =>
        {
            Arity("isstudent", args, 0, line, col);
            return JgsValue.Bool(false);
        });

        Query("memory", (args, line, col) =>
        {
            Arity("memory", args, 0, line, col);
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            double available = info.TotalAvailableMemoryBytes;
            double used = GC.GetTotalMemory(forceFullCollection: false);

            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["MaxPossibleArrayBytes"] = JgsValue.Number(available),
                ["MemAvailableAllArrays"] = JgsValue.Number(Math.Max(available - used, 0)),
                ["MemUsedMATLAB"] = JgsValue.Number(used),
            });
        });

        Query("maxNumCompThreads", (args, line, col) =>
        {
            ArityRange("maxNumCompThreads", args, 0, 1, line, col);
            int previous = state.ComputationThreads;
            if (args.Count == 1)
            {
                state.ComputationThreads = args[0].Type == JgsType.String
                    ? Environment.ProcessorCount
                    : Count("maxNumCompThreads", args, 0, line, col);
            }

            return JgsValue.Number(previous);
        });

        Query("fftw", (args, line, col) =>
        {
            ArityRange("fftw", args, 1, 2, line, col);
            string what = Str("fftw", args, 0, line, col);
            if (what != "planner")
            {
                throw new JgsRuntimeException(line, col, $"fftw does not recognize '{what}'; it takes 'planner'.");
            }

            // JGraph's transform is a radix decomposition with a Bluestein fallback and has no plan
            // to search for, so the mode is remembered and reported but changes nothing.
            string previous = state.Planner;
            if (args.Count == 2)
            {
                state.Planner = Str("fftw", args, 1, line, col);
            }

            return JgsValue.Str(previous);
        });

    }
}
