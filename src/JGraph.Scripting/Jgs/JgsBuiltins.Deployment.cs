using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The deployment builtins (compiler and SDK plan, stage K1): what a program asks to learn whether
/// it was compiled — <c>isdeployed</c>, <c>ismcc</c>, <c>ctfroot</c>, <c>mcrversion</c>,
/// <c>mcrcachedir</c> — and the runtime's user data, <c>getmcruserdata</c> and
/// <c>setmcruserdata</c>; and, in a deployed session, the builtins R2025b's MATLAB Runtime answers
/// differently from MATLAB, as stage K0 recorded them (<c>tools/matlab-checklist/compiler-probes</c>).
/// </summary>
/// <remarks>
/// The ordinary-session answers were measured with <c>probe_k1_session</c> in R2025b, the
/// deployed ones with K0's probe programs built by <c>mcc</c> and run under the quiet launcher.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterDeploymentBuiltins"/> declares in every session.</summary>
    internal static IReadOnlyList<string> DeploymentBuiltinNames { get; } =
    [
        "isdeployed", "ismcc", "ctfroot", "mcrversion", "mcrcachedir", "getmcruserdata", "setmcruserdata",
    ];

    /// <summary>
    /// What a deployed session leaves out that this build would otherwise answer: R2025b's runtime
    /// does not ship them, and calling one is an undefined function (K0, env_plain; the vault's
    /// writers and listSecrets, K3b's k3b_secrets_run).
    /// </summary>
    private static readonly string[] DeployedOmissions =
        ["help", "lookfor", "listSecrets", "setSecret", "removeSecret", "setSecretMetadata"];

    private const string PathAlterationId = "MATLAB:mpath:PathAlterationNotSupported";

    private const string PathAlterationMessage =
        "Modifying the search path is not supported by MATLAB Compiler. Remove functions that modify the search path "
        + "from your MATLAB code. To make files visible to your deployed application, add the parent folder to your MATLAB session.";

    private const string UserDataKeyId = "MCR:mclinstancedata:MCLMCR_WRONG_USER_DATA_KEY_TYPE";

    /// <summary>
    /// Declares the deployment builtins, and when the session is a deployed one, makes it one: the
    /// frozen path and the builtins that answer for the runtime. Called after every other
    /// registrar, since a deployed session replaces some of what they declared.
    /// </summary>
    internal static void RegisterDeploymentBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Query(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        Query("isdeployed", (args, line, col) =>
        {
            Arity("isdeployed", args, 0, line, col);
            return JgsValue.Bool(host.Deployment is not null);
        });

        // True only while mcc itself runs the code (a build's dependency pass); JGraph's builds
        // never run user code, so it is always false, as it is in R2025b's runtime and session.
        Query("ismcc", (args, line, col) =>
        {
            Arity("ismcc", args, 0, line, col);
            return JgsValue.Bool(false);
        });

        // ctfroot and mcrversion are files in R2025b, whose excess inputs are MATLAB:TooManyInputs.
        Query("ctfroot", (args, line, col) =>
        {
            TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
            return JgsValue.Str(host.Deployment?.CtfRoot ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        });

        env.Builtins.Register("mcrversion", JgsValue.Function(new BuiltinFunction("mcrversion",
            (args, line, col) => McrVersion(args, 1, line, col)[0])
        {
            AutoCallsBare = true,
            MultiOutput = McrVersion,
        }));

        Query("mcrcachedir", (args, line, col) =>
        {
            TooManyInputs(args, 0, "MATLAB:mcrcachedir:tooManyInputArgs", line, col);
            return JgsValue.Str(host.Deployment?.CacheFolder ?? ScriptDeployment.CacheDirectory());
        });

        env.Builtins.Register("getmcruserdata", JgsValue.Function(new BuiltinFunction("getmcruserdata", (args, line, col) =>
        {
            Arity("getmcruserdata", args, 1, line, col);
            string key = UserDataKey(args[0], line, col);
            if (host.RuntimeUserData.TryGetValue(key, out JgsValue? held))
            {
                return JgsValue.Share(held);
            }

            // A key nothing set answers a 1-by-0 double (R2025b, in both kinds of session).
            JgsValue none = JgsValue.Array([]);
            none.Reshape(1, 0);
            return none;
        })));

        env.Builtins.Register("setmcruserdata", JgsValue.Function(new BuiltinFunction("setmcruserdata", (args, line, col) =>
        {
            Arity("setmcruserdata", args, 2, line, col);
            host.RuntimeUserData[UserDataKey(args[0], line, col)] = JgsValue.Share(args[1]);
            return JgsValue.Null;
        })
        {
            BindsAnsAsStatement = false,
        }));

        RegisterDependencyBuiltins(env, interpreter, host); // stage K2

        // mcc and compiler.build (stage K6); a deployed session has neither, as R2025b's runtime ships neither.
        if (host.Deployment is null)
        {
            RegisterCompilerBuiltins(env, interpreter, host);
            RegisterProjectBuiltins(env, interpreter, host); // MATLAB Projects and compiler tasks (stage K20)
            RegisterLegacyProjectBuiltins(env, interpreter, host); // legacy deployment projects, createDeploymentScript (stage K21)
        }

        // MATLAB Production Server's metrics and cache helpers (stage K15), in every session: a deployed function emits metrics.
        RegisterMpsCacheBuiltins(env, interpreter, host);

        if (host.Deployment is { } deployment)
        {
            RegisterDeployedSession(env, interpreter, host, deployment);
        }
    }

    /// <summary>
    /// <c>mcrversion</c>'s outputs: the major version alone for one or none, then the minor and the
    /// update, and zeros past the third — R2025b's <c>mcrversion.m</c> (KO2: 25, 2, 0).
    /// </summary>
    private static JgsValue[] McrVersion(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
        var outputs = new JgsValue[System.Math.Max(wanted, 1)];
        for (int i = 0; i < outputs.Length; i++)
        {
            outputs[i] = JgsValue.Number(i < ScriptDeployment.RuntimeVersion.Count ? ScriptDeployment.RuntimeVersion[i] : 0);
        }

        return outputs;
    }

    /// <summary>Refuses more than <paramref name="most"/> inputs with <paramref name="identifier"/>, in R2025b's words.</summary>
    private static void TooManyInputs(IReadOnlyList<JgsValue> args, int most, string identifier, int line, int col)
    {
        if (args.Count > most)
        {
            throw new JgsRuntimeException(line, col, identifier, "Too many input arguments.");
        }
    }

    /// <summary>A user-data key: a char row or a string scalar, case-sensitive, or R2025b's refusal naming the class given.</summary>
    private static string UserDataKey(JgsValue key, int line, int col)
    {
        if (IsTextScalar(key))
        {
            return TextOf(key);
        }

        throw new JgsRuntimeException(line, col, UserDataKeyId,
            "Wrong type of data used as MATLAB Runtime user data key. Keys must be scalar strings or character vectors.\n\n"
            + $"Actual key type: '{ClassOf(key, JgsDialect.Matlab)}'.");
    }

    /// <summary>
    /// Makes the session a deployed one (K0's recordings): the frozen path on the function path,
    /// the functions the runtime does not ship withdrawn, the path verbs refusing, the console
    /// verbs that have no console quiet, the debugger refusing, and the waits that read standard
    /// input reading it.
    /// </summary>
    private static void RegisterDeployedSession(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, ScriptDeployment deployment)
    {
        JgsFunctionPath? search = interpreter.FunctionPath;
        if (search is not null)
        {
            foreach (string folder in deployment.FrozenPath)
            {
                search.Add(folder, atEnd: true);
            }
        }

        foreach (string name in DeployedOmissions)
        {
            env.Builtins.Withdraw(name);
        }

        // A replacement keeps the original's way of being called: whether its bare name calls it,
        // and whether a statement of it binds ans.
        JgsValue? Original(string name) =>
            env.Builtins.TryGet(name, out JgsValue value) && value.Type == JgsType.Function ? value : null;

        void Replace(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body, bool command)
        {
            BuiltinFunction? was = Original(name)?.AsCallable as BuiltinFunction;
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                AutoCallsBare = was?.AutoCallsBare ?? true,
                BindsAnsAsStatement = was?.BindsAnsAsStatement ?? !command,
            }));
        }

        // clc and home do nothing, and say nothing: the docs' warning stubs are not what R2025b
        // ships (K0, stubs and env_plain: no warning, evalc('clc') is '').
        foreach (string name in (string[])["clc", "home"])
        {
            Replace(name, (args, line, col) =>
            {
                Arity(name, args, 0, line, col);
                return JgsValue.Null;
            }, command: true);
        }

        foreach (string name in (string[])["keyboard", "dbstop"])
        {
            Replace(name, (_, line, col) => throw new JgsRuntimeException(line, col,
                "MATLAB:services:MissingRequiredCapability", "Debugging is not supported in this configuration."), command: true);
        }

        Replace("mex", (_, line, col) => throw new JgsRuntimeException(line, col,
            "MATLAB:mex:DEPLOYMENT_MODE_NOTSUPPORTED", "Building MEX files is not supported in deployed environment."), command: true);

        JgsRuntimeException PathAlteration(int line, int col) => new(line, col, PathAlterationId, PathAlterationMessage);
        IReadOnlyList<string> Frozen() => search?.Folders ?? deployment.FrozenPath;
        bool OnPath(string folder) => Frozen().Contains(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(host.CurrentDirectory, folder))),
            JgsFunctionPath.PathComparer);

        Replace("addpath", (_, line, col) => throw PathAlteration(line, col), command: true);
        Replace("restoredefaultpath", (_, line, col) => throw PathAlteration(line, col), command: true);

        // rmpath of a folder the path does not hold only warns, as it does in MATLAB; one it holds
        // is refused (K0, path).
        Replace("rmpath", (args, line, col) =>
        {
            for (int i = 0; i < args.Count; i++)
            {
                foreach (string folder in Split(Str("rmpath", args, i, line, col)))
                {
                    if (OnPath(folder))
                    {
                        throw PathAlteration(line, col);
                    }

                    Warn(host, "MATLAB:rmpath:DirNotFound", $"\"{folder}\" not found in path.");
                }
            }

            return JgsValue.Null;
        }, command: true);

        // path answers the frozen path, and setting it to what it already is changes nothing and
        // is allowed: K0's probe put the path back that way after each refusal.
        Replace("path", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                return JgsValue.Str(string.Join(Path.PathSeparator, Frozen()));
            }

            var asked = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                asked.AddRange(Split(Str("path", args, i, line, col)));
            }

            IReadOnlyList<string> frozen = Frozen();
            if (asked.Count != frozen.Count || asked.Where((folder, i) => !JgsFunctionPath.PathComparer.Equals(
                    Path.TrimEndingDirectorySeparator(folder), frozen[i])).Any())
            {
                throw PathAlteration(line, col);
            }

            return JgsValue.Null;
        }, command: false);

        // run finds a script on the frozen path alone: one in the current folder is data, not code
        // (K0, path: run('outside_script.m') is MATLAB:run:FileNotFound).
        if (Original("run") is { } run)
        {
            Replace("run", (args, line, col) =>
            {
                if (args.Count == 1 && IsTextScalar(args[0]))
                {
                    string asked = TextOf(args[0]);
                    string? file = Path.IsPathRooted(asked)
                        ? File.Exists(asked) && IsUnder(asked, deployment.CtfRoot) ? asked : null
                        : search?.FindFile(Path.HasExtension(asked) ? asked : asked + ".m");
                    if (file is null)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:run:FileNotFound", $"{asked} not found.");
                    }

                    return run.AsCallable.Call([JgsValue.Str(file)], line, col);
                }

                return run.AsCallable.Call(args, line, col);
            }, command: true);
        }

        // pause with nothing to wait for reads a line from standard input, after R2025b's prompt
        // and with no newline of its own; a closed input ends it at once (K0, stubs).
        if (Original("pause") is { } pause)
        {
            Replace("pause", (args, line, col) =>
            {
                if (args.Count == 0 && pause.AsCallable.Call([JgsValue.Str("query")], line, col) is { Type: JgsType.String, AsString: "on" })
                {
                    host.WriteOut("Paused: Press any key and return");
                    _ = Console.In.ReadLine();
                    return JgsValue.Null;
                }

                return pause.AsCallable.Call(args, line, col);
            }, command: true);
        }

        // input reads standard input. A closed input answers [] for a value and '' for text
        // (K0, stubs); a typed line is evaluated, as MATLAB's input does.
        Replace("input", (args, line, col) =>
        {
            ArityRange("input", args, 1, 2, line, col);
            string prompt = TextOf(args[0]);
            bool text = args.Count == 2 && IsTextScalar(args[1]) && TextOf(args[1]) == "s";
            host.WriteOut(prompt);
            string? typed = Console.In.ReadLine();
            if (text)
            {
                return JgsValue.Str(typed ?? string.Empty);
            }

            return typed is null || typed.Trim().Length == 0
                ? JgsMatrix.FromColumnMajor([], 0, 0)
                : interpreter.EvaluateSource(typed, interpreter.CurrentFrame, line, col);
        }, command: false);

        // license() in a deployed session has no license number to give (K0, env_plain).
        if (Original("license") is { } license)
        {
            Replace("license", (args, line, col) =>
                args.Count == 0 ? JgsValue.Str("unknown") : license.AsCallable.Call(args, line, col), command: false);
        }

        // waitforallfiguresclosed is the runtime's own (R2025b's mcr\toolbox\compiler\runtime): it
        // waits while any figure is visible and answers the Names of the invisible ones as a row.
        env.Builtins.Register("waitforallfiguresclosed", JgsValue.Function(new BuiltinFunction("waitforallfiguresclosed",
            (args, line, col) =>
            {
                TooManyInputs(args, 0, "MATLAB:TooManyInputs", line, col);
                BlockUntil("waitforallfiguresclosed", host, CancellationToken.None,
                    () => !OpenFigures().Any(static figure => figure.Visible), deadline: null, line, col);
                JgsValue[] names =
                [
                    .. OpenFigures().Where(static figure => !figure.Visible)
                        .Select(figure => JgsGraphicsProperties.Get(JgsHandleRegistry.EntryFor(figure), "Name", line, col)),
                ];
                JgsValue answer = JgsValue.Cell(names);
                answer.Reshape(names.Length == 0 ? 0 : 1, names.Length);
                return answer;
            })
        {
            AutoCallsBare = true,
        }));
    }

    /// <summary>The figures the session has open, newest first, as <c>allchild(groot)</c> lists them.</summary>
    private static IEnumerable<FigureModel> OpenFigures()
    {
        IReadOnlyList<int> numbers = JG.FigureNumbers;
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            if (JG.TryGetFigure(numbers[i], out FigureModel figure))
            {
                yield return figure;
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> lies inside <paramref name="folder"/>.</summary>
    private static bool IsUnder(string path, string folder)
    {
        string full = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
