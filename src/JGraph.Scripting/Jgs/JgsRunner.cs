using System.IO;
using System.Linq;
using JGraph.Api;

using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The shared body of a JGS run, used by <see cref="JgsScriptEngine"/> (and, later, the debug session)
/// so plain and debugged runs cannot drift: reset the facade, parse, seed the built-ins, wire the
/// <c>run()</c> include builtin, execute, and snapshot the globals the script defined.
/// </summary>
internal static class JgsRunner
{
    /// <summary>A call frame has no pristine bindings to revert a cleared name to.</summary>
    private static readonly Dictionary<string, JgsValue> NoPristine = new();

    /// <summary>Runs <paramref name="code"/> and maps every JGS failure to a diagnostic result.</summary>
    /// <param name="code">The JGS source.</param>
    /// <param name="context">The host services for the run.</param>
    /// <param name="cancellationToken">Checked cooperatively before every statement.</param>
    /// <param name="sourceId">The identity of <paramref name="code"/> (file path or ""), stamped on its
    /// statements so a debugger can map execution to the right document.</param>
    /// <param name="hook">The debug hook, or null for a plain run.</param>
    /// <param name="dialect">The language variant to run, or null for <see cref="JgsDialect.Jgs"/>.</param>
    /// <param name="searchFolders">Folders placed on the function path before the script runs, after
    /// its own folder — what a host would otherwise have to <c>addpath</c> from inside the code. The
    /// parity harness puts its fixtures' shared helper classes there, the way the recorder does for
    /// MATLAB, so a fixture and its recording resolve the same names from the same folder.</param>
    public static ScriptRunResult Run(
        string code,
        ScriptContext context,
        CancellationToken cancellationToken,
        string sourceId = "",
        IJgsDebugHook? hook = null,
        JgsDialect? dialect = null,
        IReadOnlyList<string>? searchFolders = null)
    {
        dialect ??= JgsDialect.Jgs;

        // Whether this run owns the process's graphics state, or is nested inside something that
        // already does. A live session installs a dispatcher when it is built and keeps it for its
        // whole life, so finding one here means a workspace is alive around this run — which is the
        // case for a debug run started from the editor, and never the case under -batch.
        JgsCallbackDispatcher? displaced = JgsCallbackDispatcher.Current;
        bool nested = displaced is not null;

        // JGS scripts drive the same static JG facade; a run that owns it starts from a clean state.
        // The previous completed run's packed buffers are released deterministically here (its
        // figures and variable snapshots hold copies, never the buffers); finalizers remain the
        // backstop for everything else.
        //
        // A nested run must not do any of it. The session around it still has figures open, handles
        // its console workspace names, and variables pointing at those buffers — and JgsHandleRegistry
        // .Clear() rewinds the handle counter, so the first object this run drew was handed the number
        // the session's first object already had. That is what made setting a breakpoint change what
        // a script meant: the same statement that read a surface before the debug run read whatever
        // object the debug run minted first after it.
        if (!nested)
        {
            JG.Reset();
            JgsHandleRegistry.Clear();
            DisposePreviousRunBuffers();
        }

        var globals = new JGraphScriptGlobals(context);

        // A one-shot run knows which file it came from, and mfilename has to be able to say so.
        if (sourceId.Length > 0 && Path.IsPathRooted(sourceId))
        {
            globals.BeginRun(Path.GetDirectoryName(sourceId), sourceId);
        }
        else if (context.ScriptPath is { Length: > 0 } scriptPath && Path.IsPathRooted(scriptPath))
        {
            // The launcher's -batch file.m names the file on the context and hands its code over
            // with no source id, so its diagnostics stay bare. The file's folder still has to be
            // the running script's folder: the file index scans that folder for helpers that share
            // a built-in's name, and without it an extract.m beside the script was listed by which
            // and passed over by the call, which went to the built-in (ADR 0150).
            globals.BeginRun(Path.GetDirectoryName(scriptPath), scriptPath);
        }

        // A one-shot run gets a dispatcher too — not for interface events, which have nowhere to
        // come from, but because DeleteFcn and CreateFcn fire from the script's own doings (a clf,
        // a delete(h)) and must fire under -batch exactly as they do at the prompt.
        var dispatcher = new JgsCallbackDispatcher(globals, context)
        {
            StatementToken = cancellationToken,
            StatementThreadId = Environment.CurrentManagedThreadId,
        };
        JgsCallbackDispatcher.Install(dispatcher);

        try
        {
            IReadOnlyList<Stmt> program = Parser.Parse(code, sourceId, dialect);
            JgsEnvironment environment = JgsBuiltins.CreateGlobals(globals, cancellationToken, dialect);
            var interpreter = new Interpreter(environment, cancellationToken, hook,
                echo: line => context.Output.WriteLine(line), dialect);

            // ME.stack names the file a frame ran in (V6). A -batch run hands its code over with no
            // source id so its diagnostics stay bare; the stack still has the run's file to name.
            interpreter.MainScriptPath = sourceId.Length > 0 ? sourceId : context.ScriptPath ?? string.Empty;
            interpreter.Host = globals;
            DefineRunBuiltin(environment, interpreter, globals);
            JgsBuiltins.RegisterEvalBuiltins(environment, interpreter, globals);
            JgsBuiltins.RegisterSessionBuiltins(environment, globals);
            if (searchFolders is { Count: > 0 } && interpreter.FunctionPath is { } path)
            {
                foreach (string folder in searchFolders)
                {
                    path.Add(folder, atEnd: true);
                }
            }

            // Capture the pristine builtin bindings so the post-run snapshot lists only what the
            // script itself defined (or rebound). save/load must be declared before the capture, or
            // they would list themselves as the user's variables.
            Dictionary<string, JgsValue> pristine = null!;
            JgsWorkspaceIo.DefineSaveLoad(environment, globals, interpreter, () => interpreter.CurrentFrame.Variables
                .Where(p => !pristine.TryGetValue(p.Key, out JgsValue? original) || !ReferenceEquals(original, p.Value))
                .Select(static p => (p.Key, p.Value)), () => interpreter.CurrentFrame);
            DefineWorkspaceBuiltins(environment, interpreter, context.Output, () => pristine);
            environment.Builtins.Seal(); // the last registrar has run; nothing else may land in built-in storage
            hook?.RunStarting(interpreter, environment);

            // Every built-in lives in the layer under the workspace, so the workspace starts empty
            // and this snapshot with it; it stays so that a rebound name can still be told from a
            // fresh one by the same test as before.
            pristine = environment.Locals.ToDictionary(
                static p => p.Key, static p => p.Value, StringComparer.Ordinal);

            interpreter.Run(program);
            InvokeMainIfFunctionFile(program, interpreter);

            // The run's workspace dies with the run (V10, ADR 0171): what the last statement
            // dropped is destroyed, then the base workspace's own handles, by name - as R2025b's
            // -batch exit destroys them. A debugged run keeps its workspace for the session.
            if (hook is null)
            {
                interpreter.Lifetimes.RunEnded(environment, pristine);
            }

            globals.ShowTouchedFigures(); // MATLAB expectation: created figures appear without show()
            ScriptRunResult ok = ScriptRunResult.Ok(globals.FiguresShown, SnapshotGlobals(environment, pristine));
            RegisterCompletedRun(environment, hook);
            return ok;
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is { } exit)
        {
            // The script stopped itself. Its figures still count, and the code it asked for rides
            // out on the result for the host to act on.
            globals.ShowTouchedFigures();
            return ScriptRunResult.Exited(exit.ExitCode, globals.FiguresShown);
        }
        catch (JgsException ex)
        {
            // Whatever ran before the error keeps its effect, figures included — the same rule the
            // prompt keeps. Without this a script that drew and then failed showed its figure on an
            // ordinary run and nothing at all under the debugger, which is the second way a
            // breakpoint used to change what a script did.
            ScriptDiagnostic diagnostic = ScriptDiagnostic.For(ex, sourceId);
            context.Output.WriteError(diagnostic.ToString());
            globals.ShowTouchedFigures();
            return ScriptRunResult.Failed(ex.Message, new[] { diagnostic });
        }
        catch (OperationCanceledException)
        {
            globals.ShowTouchedFigures();
            return ScriptRunResult.Failed("Script run was cancelled.");
        }
        catch (Exception ex)
        {
            // The last resort. Everything above is a way a run is meant to end; reaching here means a
            // defect in this build rather than in the script, and before M76 it meant the process
            // simply died — an unguarded ArgumentException out of the numeric layer took `qr` of a
            // wide matrix, the whole batch, and the exit code with it. A defect is reported as one,
            // named by its type so it can be found, and the run ends as a failure like any other.
            var diagnostic = new ScriptDiagnostic(0, 0,
                $"Internal error: {ex.GetType().Name}: {ex.Message}", IsError: true);
            context.Output.WriteError(diagnostic.ToString());
            globals.ShowTouchedFigures();
            return ScriptRunResult.Failed(diagnostic.Message, new[] { diagnostic });
        }
        finally
        {
            // Restore whatever was installed before, so a one-shot run inside a live session (a
            // debug run) hands the session its dispatcher back rather than leaving none.
            JgsCallbackDispatcher.Install(displaced);
            globals.CloseAllFiles(); // whatever fopen left open dies with the run
        }
    }

    /// <summary>
    /// MATLAB's function-file rule: a file whose first non-comment token is <c>function</c> is a
    /// function file, and running it invokes its main (first) function. The parsed shape of such a
    /// file is a program of nothing but <see cref="FnStmt"/>s — comments produce no statements, so a
    /// leading comment block still counts. Prompt input must never trigger this (defining a function
    /// at the console only defines it), which is why the check lives behind file entry points only.
    /// </summary>
    internal static bool IsFunctionFile(IReadOnlyList<Stmt> program) =>
        program.Count > 0 && program.All(static s => s is FnStmt);

    /// <summary>
    /// Invokes the main function of a function file with no arguments, discarding any result —
    /// MATLAB dispatches on the file name, and the first function in the file is that function.
    /// Arity and runtime errors surface as ordinary diagnostics from the call site of the file.
    /// </summary>
    internal static void InvokeMainIfFunctionFile(IReadOnlyList<Stmt> program, Interpreter interpreter)
    {
        if (!IsFunctionFile(program))
        {
            return;
        }

        var main = (FnStmt)program[0];
        if (interpreter.TryGetHoisted(main.SourceId, main.Name, main.Dialect, out JgsValue value) && value.Type == JgsType.Function)
        {
            JgsCallbacks.Invoke(value.AsCallable, System.Array.Empty<JgsValue>(), main.Line, main.Column); // run as a statement: nargout 0 (V9.1)
        }
    }

    /// <summary>Projects a JGS value to the UI-facing <see cref="ScriptVariable"/> shape.</summary>
    public static ScriptVariable ToScriptVariable(string name, JgsValue value) =>
        new(
            name,
            // The panel says what whos and class() say — 'double', 'char', 'logical' — because it
            // used to name JGS types ('number', 'string') instead, and a panel that disagrees with
            // class(x) reads as a defect in one of them.
            KindOf(value),
            ScriptVariable.Truncate(value.Display()),
            ToRawValue(value));

    /// <summary>
    /// Defines the <c>run(path)</c> builtin: it resolves the path like the table readers do, parses the
    /// file, and executes it into the global scope (functions hoisted first) — MATLAB-style script
    /// composition; a function file's main function is then called, as MATLAB's <c>run</c> calls it. Re-entrant includes are guarded so a cycle fails with a clear error. An included
    /// file is parsed in the caller's dialect unless it is a <c>.m</c> file, which always means MATLAB,
    /// and a MATLAB file runs in its own folder the way MATLAB's <c>run</c> runs it: the working
    /// directory is the script's for the duration, so a file beside it answers a name and a relative
    /// path means the copy beside it, and the caller's folder is put back afterwards unless the
    /// script moved with <c>cd</c>. A JGS include stays where it was called from. The file may be
    /// named with or without its extension, since MATLAB names a script by its stem.
    /// </summary>
    internal static void DefineRunBuiltin(
        JgsEnvironment environment, Interpreter interpreter, JGraphScriptGlobals globals)
    {
        var including = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        environment.Builtins.Register("run", JgsValue.Function(new BuiltinFunction("run", (args, line, column) =>
        {
            if (args.Count != 1 || args[0].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, column, "run(path) expects one string argument.");
            }

            string resolved = globals.Resolve(args[0].AsString);
            if (!File.Exists(resolved) && Path.GetExtension(resolved).Length == 0)
            {
                string withExtension = globals.Resolve(args[0].AsString + ".m");
                if (File.Exists(withExtension))
                {
                    resolved = withExtension;
                }
            }

            string fullPath;
            string source;
            try
            {
                fullPath = Path.GetFullPath(resolved);
                source = File.ReadAllText(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new JgsRuntimeException(line, column, $"run: cannot read '{resolved}': {ex.Message}");
            }

            if (!including.Add(fullPath))
            {
                throw new JgsRuntimeException(line, column, $"run: circular include of '{fullPath}'.");
            }

            try
            {
                // Stamp the included file's statements with its resolved path so breakpoints hit and
                // step-in lands in the right editor tab. The include runs under its own dialect, not
                // just parses in it — a .m reached from JGS must still index 1-based and auto-declare
                // - and its statements carry that dialect, so the run enters it (V11, ADR 0172). A
                // file that is not a .m is parsed in the dialect of the code that ran it.
                JgsDialect included = DialectOfFile(fullPath, interpreter.Dialect);

                // A MATLAB script runs in its own folder (R2025b: pwd inside run('other/s.m') is
                // other, and the caller is back where it was after — an error included — unless the
                // script cd'd away). The scope is what lets a max.m or a sib.m beside the script be
                // found, and a run('u.m') inside it mean the u.m beside it.
                using JGraphScriptGlobals.DirectoryScope folder =
                    included.IsMatlab && Path.GetDirectoryName(fullPath) is { Length: > 0 } scriptFolder
                        ? globals.EnterDirectory(scriptFolder)
                        : default;
                // A function file run by name runs its main function (R2025b: run('f.m') on a file
                // that opens with `function f` calls f), which is what a legacy script that wraps
                // itself in a function expects; the batch launcher and the console have always done
                // this, and run() had only hoisted the functions and returned.
                IReadOnlyList<Stmt> program = Parser.Parse(source, fullPath, included);
                interpreter.Run(program);
                InvokeMainIfFunctionFile(program, interpreter);
            }
            finally
            {
                including.Remove(fullPath);
            }

            return JgsValue.Null;
        })));
    }

    /// <summary>
    /// The dialect a file is parsed in: MATLAB for a <c>.m</c> file, otherwise <paramref name="caller"/>
    /// - the including code's for <c>run</c>, the session's for the debugger's live edit. A <c>.m</c>
    /// file has to mean the same thing however it was reached.
    /// </summary>
    internal static JgsDialect DialectOfFile(string path, JgsDialect caller) =>
        Path.GetExtension(path).Equals(".m", StringComparison.OrdinalIgnoreCase) ? JgsDialect.Matlab : caller;

    /// <summary>
    /// Declares the workspace-management builtins — <c>clear</c>, <c>clearvars</c>, <c>whos</c> —
    /// shared by every workspace owner: one-shot runs, batch, the debugger, and the interactive
    /// session. Each reads its owner's pristine snapshot through <paramref name="pristine"/>, which
    /// the owner captures <em>after</em> all of its registrations (these included), so the closure
    /// is deliberately late-bound.
    /// </summary>
    internal static void DefineWorkspaceBuiltins(
        JgsEnvironment environment, Interpreter interpreter, IScriptOutput output,
        Func<IReadOnlyDictionary<string, JgsValue>> pristine)
    {
        foreach (string constructor in new[] { "table", "array2table" })
        {
            if (!environment.TryGet(constructor, out JgsValue existing)) continue;
            environment.Builtins.Register(constructor, JgsValue.Function(new BuiltinFunction(constructor, (args, line, col) =>
            {
                var given = args.ToList();
                if (interpreter.PendingCall is { } call && !args.Any(a => a.Type == JgsType.String && a.AsString == "VariableNames"))
                {
                    int count = constructor == "array2table" ? (args.Count > 0 ? args[0].Cols : 0) : args.Count;
                    if (constructor == "table") for (int i = 0; i + 1 < args.Count; i++) if (args[i].Type == JgsType.String && args[i].AsString == "RowNames") { count = i; break; }
                    var names = new JgsValue[count];
                    for (int i = 0; i < count; i++)
                    {
                        string inferred = constructor == "array2table"
                            ? (call.Arguments.FirstOrDefault() is VariableExpr array ? array.Name : "Var") + (i + 1)
                            : i < call.Arguments.Count && call.Arguments[i] is VariableExpr variable ? variable.Name : "Var" + (i + 1);
                        names[i] = JgsValue.Str(inferred);
                    }
                    given.Add(JgsValue.Str("VariableNames")); given.Add(JgsValue.Cell(names));
                }
                return existing.AsCallable.Call(given, line, col);
            })));
        }

        IEnumerable<(string Name, JgsValue Value)> UserVariables()
        {
            IReadOnlyDictionary<string, JgsValue> baseline = pristine();
            foreach ((string name, JgsValue value) in interpreter.CurrentFrame.Variables)
            {
                if (!baseline.TryGetValue(name, out JgsValue? original) || !ReferenceEquals(original, value)
                    || (value.Type == JgsType.Function && !environment.IsFunctionBinding(name)))
                {
                    yield return (name, value);
                }
            }
        }

        // Every form of clear resolves in the active workspace (V7, ADR 0168): the frame of the
        // function that ran it and, for a nested function, its parents' frames up to the boundary -
        // the same frames an assignment can reach. Until V7 the named and plain forms forgot in the
        // workspace the builtin was registered in, so a 'clear' inside a callback deleted the base
        // workspace's variable and left the callback's (#64, #65). Only the base workspace has
        // pristine bindings to revert to; a call frame's cleared name is simply gone.
        IEnumerable<JgsEnvironment> ActiveFrames()
        {
            for (JgsEnvironment? scope = interpreter.CurrentFrame; scope is not null && !scope.IsBuiltinLayer; scope = scope.Parent)
            {
                yield return scope;
                if (scope.IsCallBoundary)
                {
                    yield break;
                }
            }
        }

        IReadOnlyDictionary<string, JgsValue> OriginalsOf(JgsEnvironment scope, IReadOnlyDictionary<string, JgsValue> baseline) =>
            ReferenceEquals(scope, environment) ? baseline : NoPristine;

        // clearvars filters the active workspace; clear also supports the older function/all forms.
        void DefineClear(string builtin)
        {
            environment.Builtins.Register(builtin, JgsValue.Function(new BuiltinFunction(builtin, (args, line, column) =>
            {
                IReadOnlyDictionary<string, JgsValue> baseline = pristine();
                var names = new List<string>();
                foreach (JgsValue argument in args)
                {
                    if (argument.Type != JgsType.String)
                    {
                        throw new JgsRuntimeException(line, column,
                            $"{builtin} takes variable names, but got a {argument.TypeName}.");
                    }

                    names.Add(argument.AsString);
                }

                // 'clear global' / 'clear global a b' takes the globals out of the global workspace
                // (V3b, #158); a frame that declared one keeps a cleared name, which reads as
                // "Reference to a cleared variable" and is created afresh by a write.
                if (builtin == "clear" && names.Count > 0 && names[0] == "global")
                {
                    interpreter.ClearGlobals(names.GetRange(1, names.Count - 1));
                    return JgsValue.Null;
                }

                if (builtin == "clearvars")
                {
                    var remove = new List<System.Text.RegularExpressions.Regex>();
                    var keep = new List<System.Text.RegularExpressions.Regex>();
                    bool except = false;
                    bool regexp = false;
                    foreach (string name in names)
                    {
                        if (name == "-except")
                        {
                            except = true;
                            regexp = false;
                            continue;
                        }

                        if (name == "-regexp")
                        {
                            regexp = true;
                            continue;
                        }

                        if (name.StartsWith('-'))
                        {
                            throw new JgsRuntimeException(line, column, $"clearvars: unsupported option '{name}'.");
                        }

                        string pattern = regexp ? name : "^" + System.Text.RegularExpressions.Regex.Escape(name).Replace("\\*", ".*") + "$";
                        (except ? keep : remove).Add(new System.Text.RegularExpressions.Regex(
                            pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                    }

                    foreach (JgsEnvironment scope in ActiveFrames())
                    {
                        IReadOnlyDictionary<string, JgsValue> originals = OriginalsOf(scope, baseline);
                        foreach ((string name, JgsValue value) in scope.Variables.ToList())
                        {
                            if (scope.IsFunctionBinding(name)
                                || (value.Type != JgsType.Function && originals.TryGetValue(name, out JgsValue? original)
                                    && ReferenceEquals(original, value)))
                            {
                                continue;
                            }

                            if ((remove.Count == 0 || remove.Any(pattern => pattern.IsMatch(name)))
                                && !keep.Any(pattern => pattern.IsMatch(name)))
                            {
                                // Retained aliases may share a value, so do not dispose its buffer here.
                                scope.Forget(name, originals);
                            }
                        }
                    }

                    return JgsValue.Null;
                }

                // 'clear all' and 'clear functions' forget every file the path loaded, so the next
                // call re-reads it, and every persistent — MATLAB's meaning (R2025b: a counter's
                // persistent is 1 again after 'clear all') — sparing the functions that are running
                // (V5, #72). A script's own functions live with the script and stay callable, as
                // they do in MATLAB, where the file is simply read again. 'clear all' takes the
                // globals as well (R2025b, measured at V7: a base global is gone after a callback's
                // 'clear all').
                bool everything = names.Contains("all");
                if (everything || names.Contains("functions"))
                {
                    interpreter.FunctionPath?.Unload();
                    interpreter.ForgetPersistents();
                }

                if (everything)
                {
                    interpreter.ClearGlobals([]);
                }

                int regexpAt = names.IndexOf("-regexp");
                if (names.Count == 0 || everything || names.Contains("variables") || regexpAt >= 0)
                {
                    // 'clear -regexp p1 p2' keeps every name no pattern matches; the other forms
                    // take every variable of the active frames.
                    var patterns = new List<System.Text.RegularExpressions.Regex>();
                    if (regexpAt >= 0)
                    {
                        foreach (string pattern in names.Skip(regexpAt + 1))
                        {
                            patterns.Add(new System.Text.RegularExpressions.Regex(
                                pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                        }
                    }

                    // A plain 'clear' unbinds a persistent and a global in the frame and leaves what
                    // they name where it is; 'clear variables', 'clear all' and the regexp form take
                    // the persistent's value too (R2025b, measured at V7).
                    bool unlinkOnly = names.Count == 0;

                    // Dropping everything at once takes every user wrapper (aliases included), so
                    // their packed buffers can be released deterministically (M6 keeps what another
                    // holder can still see). MATLAB's plain clear drops variables but not the
                    // functions a JGS script defined — those need 'clear all'; a nested function
                    // hoisted into its parent's frame is never a variable.
                    var dropped = new List<JgsValue>();
                    foreach (JgsEnvironment scope in ActiveFrames())
                    {
                        IReadOnlyDictionary<string, JgsValue> originals = OriginalsOf(scope, baseline);
                        foreach ((string cleared, JgsValue value) in scope.Variables.ToList())
                        {
                            if (scope.DeclaresFunctionLocally(cleared)
                                && !(everything && interpreter.ScriptFunctionNames.Contains(cleared)))
                            {
                                continue;
                            }

                            if (value.Type != JgsType.Function && originals.TryGetValue(cleared, out JgsValue? original)
                                && ReferenceEquals(original, value))
                            {
                                continue;
                            }

                            if (patterns.Count > 0 && !patterns.Any(pattern => pattern.IsMatch(cleared)))
                            {
                                continue;
                            }

                            dropped.Add(value);
                            if (unlinkOnly && scope.DeclaresPersistentLocally(cleared))
                            {
                                scope.Unlink(cleared);
                            }
                            else
                            {
                                scope.Forget(cleared, originals);
                            }
                        }

                        foreach (string link in scope.GlobalLinks.ToList())
                        {
                            if (patterns.Count == 0 || patterns.Any(pattern => pattern.IsMatch(link)))
                            {
                                scope.Unlink(link);
                            }
                        }
                    }

                    if (patterns.Count == 0)
                    {
                        DisposeBuffers(dropped);
                    }

                    return JgsValue.Null;
                }

                foreach (string cleared in names)
                {
                    // The nearest active frame that binds the name - a local, a persistent it
                    // declared, or a global it linked - forgets it; a nested function's 'clear v'
                    // reaches its parent's v (R2025b). No buffer disposal here: in JGS another name
                    // may still alias the wrapper.
                    foreach (JgsEnvironment scope in ActiveFrames())
                    {
                        if (scope.BindsLocally(cleared) || ReferenceEquals(scope, environment))
                        {
                            scope.Forget(cleared, OriginalsOf(scope, baseline));
                            break;
                        }
                    }

                    // The name of a function file clears the function: its persistents start over
                    // and the file is read again, unless it is running (V5, #72).
                    interpreter.FunctionPath?.Unload(cleared);
                }

                return JgsValue.Null;
            })));
        }

        DefineClear("clear");
        DefineClear("clearvars");

        environment.Builtins.Register("whos", JgsValue.Function(new BuiltinFunction("whos", (args, line, column) =>
        {
            if (args.Count == 1 && JgsBuiltins.IsMatFile(args[0]))
            {
                return JgsBuiltins.MatFileWhos(args[0], line, column); // the file's variables, a struct array (V6, #112)
            }

            var selectors = new List<System.Text.RegularExpressions.Regex>();
            bool regexp = false;
            foreach (JgsValue arg in args)
            {
                if (arg.Type != JgsType.String) throw new JgsRuntimeException(line, column, "whos expects variable names.");
                if (arg.AsString == "-regexp") { regexp = true; continue; }
                string pattern = regexp ? arg.AsString : "^" + System.Text.RegularExpressions.Regex.Escape(arg.AsString).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                selectors.Add(new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            }

            List<(string Name, string Size, string Kind)> rows = UserVariables()
                .Where(pair => selectors.Count == 0 || selectors.Any(s => s.IsMatch(pair.Name)))
                .OrderBy(static pair => pair.Name, StringComparer.Ordinal)
                .Select(pair => (pair.Name, SizeOf(pair.Value), KindOf(pair.Value)))
                .ToList();
            if (rows.Count == 0)
            {
                return JgsValue.Null;
            }

            int nameWidth = System.Math.Max("Name".Length, rows.Max(static r => r.Name.Length));
            int sizeWidth = System.Math.Max("Size".Length, rows.Max(static r => r.Size.Length));
            output.WriteLine($"  {"Name".PadRight(nameWidth)}  {"Size".PadRight(sizeWidth)}  Class");
            foreach ((string name, string size, string kind) in rows)
            {
                output.WriteLine($"  {name.PadRight(nameWidth)}  {size.PadRight(sizeWidth)}  {kind}");
            }

            return JgsValue.Null;
        })));
    }

    /// <summary>The size column of <c>whos</c> and the Workspace pane.</summary>
    internal static string SizeOf(JgsValue value) => value.Type switch
    {
        JgsType.Array => string.Join("x", JgsMatrix.DimsOf(value)),
        JgsType.String => $"1x{value.AsString.Length}",
        JgsType.Cell => $"{value.Rows}x{value.Cols}",
        JgsType.Table => $"{value.AsTable.RowCount}x{value.AsTable.ColumnCount}",
        JgsType.Image => $"{value.AsImage.Height}x{value.AsImage.Width}x{value.AsImage.Channels}",
        JgsType.Sparse => $"{value.AsSparse.Rows}x{value.AsSparse.Cols}",
        _ => "1x1",
    };

    /// <summary>The class column of <c>whos</c> and the Workspace pane.</summary>
    internal static string KindOf(JgsValue value) => value.Type switch
    {
        // A picture read from a file reports the class it carries — uint8, logical after a threshold —
        // which is the useful answer and matches MATLAB's own whos. A computed [0, 1] image has no
        // more specific class to give, so it stays the plain label it has always had.
        JgsType.Image => value.AsImage.Class == JGraph.Imaging.ImageClass.Double
            ? "image"
            : $"{value.AsImage.Class.MatlabName()} image",
        // Sparsity is an attribute rather than a class, and the column is where it fits.
        JgsType.Sparse => "double (sparse)",
        JgsType.Null => value.TypeName,
        // Everything else answers exactly what class() answers. The two columns drifted — a string
        // scalar and a logical mask both said 'double' here while class() said 'string' and
        // 'logical' — and the guards class() carries (string arrays, times, masks) are the fix, so
        // this reads them from the same place instead of restating them.
        _ => JgsBuiltins.ClassOf(value, JgsDialect.Matlab),
    };

    /// <summary>
    /// Projects the bindings <paramref name="environment"/> holds that are not still the pristine
    /// builtin binding recorded in <paramref name="pristine"/> — i.e. exactly what the user's code
    /// defined or rebound. Reference equality is the test, so rebinding <c>pi</c> shows up but the
    /// hundreds of untouched builtins and constants do not.
    /// </summary>
    internal static IReadOnlyList<ScriptVariable> SnapshotGlobals(
        JgsEnvironment environment, Dictionary<string, JgsValue> pristine)
    {
        var variables = new List<ScriptVariable>();
        foreach ((string name, JgsValue value) in environment.Locals)
        {
            // Skip builtins the script never touched; include anything it defined or rebound.
            if (pristine.TryGetValue(name, out JgsValue? original) && ReferenceEquals(original, value))
            {
                continue;
            }

            variables.Add(ToScriptVariable(name, value));
        }

        variables.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return variables;
    }

    /// <summary>
    /// Arrays above this size have no <see cref="ScriptVariable.RawValue"/>: copying 100M doubles on
    /// every Variables refresh (and feeding them to the data-viewer grid) helps nobody.
    /// </summary>
    private const int MaxRawValueElements = 2_000_000;

    private static object? ToRawValue(JgsValue value) => value.Type switch
    {
        JgsType.Number => value.AsNumber,
        JgsType.Bool => value.AsBool,
        JgsType.String => value.AsString,
        JgsType.Table => value.AsTable,
        JgsType.Image => null, // images have no Data-Viewer raw copy; the Variables panel shows the label

        JgsType.Array when value.ArrayLength > MaxRawValueElements => null,

        // A matrix becomes formatted text the host can grid without knowing the value model. This
        // comes before the packed arms: a matrix is packed too, and a flat double[] would lose the
        // shape that makes it worth looking at.
        // A ragged array-of-rows is not a matrix but still grids usefully, so it keeps the padding
        // projection it had before shapes existed.
        JgsType.Array when JgsMatrix.IsNested(value) => CellRowsGrid(value.BoxedElements()),
        JgsType.Array when JgsMatrix.IsMatrix(value) => MatrixGrid(value),

        JgsType.Array when value.IsPackedComplex => null, // boxed complex arrays have no raw view either
        JgsType.Array when value.IsPacked =>
            value.PackedKind == JgsPackedKind.Number ? value.AsBuffer.AsSpan().ToArray() : null,
        JgsType.Array when value.AsArray.All(static e => e.Type == JgsType.Number) =>
            value.AsArray.Select(static e => e.AsNumber).ToArray(),

        JgsType.Cell => CellGrid(value.AsCell),
        JgsType.Struct => StructGrid(value.AsStruct),
        _ => null,
    };

    private static ScriptValueGrid? MatrixGrid(JgsValue matrix)
    {
        int rows = JgsMatrix.RowCount(matrix);
        int columns = JgsMatrix.ColCount(matrix);
        if (rows == 0 || columns == 0 || (long)rows * columns > ScriptValueGrid.MaxCells)
        {
            return null;
        }

        var text = new List<string[]>(rows);
        for (int r = 0; r < rows; r++)
        {
            var cells = new string[columns];
            for (int c = 0; c < columns; c++)
            {
                cells[c] = Cell(JgsMatrix.At(matrix, r, c));
            }

            text.Add(cells);
        }

        return new ScriptValueGrid("matrix", Numbered(columns), text);
    }

    /// <summary>A cell array of row cells, which has a grid shape without being a numeric matrix.</summary>
    private static ScriptValueGrid? CellRowsGrid(IReadOnlyList<JgsValue> rows)
    {
        int columns = rows.Count == 0 ? 0 : rows.Max(static r => r.ArrayLength);
        if (rows.Count == 0 || columns == 0 || (long)rows.Count * columns > ScriptValueGrid.MaxCells)
        {
            return null;
        }

        var text = new List<string[]>(rows.Count);
        foreach (JgsValue row in rows)
        {
            var cells = new string[columns];
            int length = row.ArrayLength;
            for (int c = 0; c < columns; c++)
            {
                // ElementAt, not AsArray: a numeric row is packed, and asking a packed array for
                // boxed elements throws by design. Ragged rows are legal (a cell's entries need not
                // be the same length), so a short row gets empty trailing cells rather than failing
                // the whole projection.
                cells[c] = c < length ? Cell(row.ElementAt(c)) : string.Empty;
            }

            text.Add(cells);
        }

        return new ScriptValueGrid("matrix", Numbered(columns), text);
    }

    private static ScriptValueGrid? CellGrid(IReadOnlyList<JgsValue> elements)
    {
        if (elements.Count == 0 || elements.Count > ScriptValueGrid.MaxCells)
        {
            return null;
        }

        // A cell array of rows displays as a grid; a flat one as a single row, which is what it is.
        if (elements.All(static e => e.Type == JgsType.Array))
        {
            ScriptValueGrid? matrix = CellRowsGrid(elements);
            return matrix is null ? null : matrix with { Kind = "cell" };
        }

        return new ScriptValueGrid(
            "cell", Numbered(elements.Count), new[] { elements.Select(Cell).ToArray() });
    }

    private static ScriptValueGrid? StructGrid(IReadOnlyDictionary<string, JgsValue> fields)
    {
        if (fields.Count == 0 || fields.Count > ScriptValueGrid.MaxCells)
        {
            return null;
        }

        // Field per row, MATLAB's own struct display: a wide struct is far more readable down the page.
        var rows = fields
            .OrderBy(static f => f.Key, StringComparer.Ordinal)
            .Select(static f => new[] { f.Key, f.Value.TypeName, Cell(f.Value) })
            .ToList();
        return new ScriptValueGrid("struct", new[] { "Field", "Type", "Value" }, rows);
    }

    private static string Cell(JgsValue value) => ScriptVariable.Truncate(value.Display());

    private static string[] Numbered(int count) =>
        Enumerable.Range(0, count).Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    // --- Deterministic release of the previous run's packed buffers -----------------------------

    private static JgsEnvironment? _lastCompletedRun;

    /// <summary>The base workspace of the last plain run to complete, for tests that inspect it.</summary>
    internal static JgsEnvironment? LastCompletedRun => _lastCompletedRun;

    /// <summary>Remembers a completed plain run for disposal when the next run starts. Debugged
    /// runs are excluded: a debug session's lifetime is managed by its own window, and a paused
    /// session's buffers must never be freed underneath it.</summary>
    private static void RegisterCompletedRun(JgsEnvironment environment, IJgsDebugHook? hook)
    {
        if (hook is null)
        {
            Interlocked.Exchange(ref _lastCompletedRun, environment);
        }
    }

    private static void DisposePreviousRunBuffers()
    {
        JgsEnvironment? previous = Interlocked.Exchange(ref _lastCompletedRun, null);
        if (previous is null)
        {
            return;
        }

        DisposeBuffers(previous.Locals.Values);
    }

    /// <summary>
    /// Releases the packed buffers and image handles reachable from <paramref name="values"/>. The walk
    /// is reference-deduplicated because several bindings can share one buffer and arrays may
    /// self-reference. Callers must be certain nothing else still reads these values — an interactive
    /// session only does this from <c>clear</c> and disposal.
    /// </summary>
    internal static void DisposeBuffers(IEnumerable<JgsValue> values)
    {
        var visited = new HashSet<JgsValue>(ReferenceEqualityComparer.Instance);
        foreach (JgsValue value in values)
        {
            DisposePackedIn(value, visited);
        }
    }

    /// <remarks>
    /// M6: disposal never frees what someone else can see. A payload two entries hold is left
    /// alone, and a container two entries hold stops the walk, because its children belong to the
    /// other holder too — a <c>for</c> over a cell shares its source, so a
    /// <c>clear</c> inside the loop must not free what the loop is still reading. A payload
    /// carrying M6's exposed mark is left to the finalizer whatever its count says, since the count
    /// cannot see a reader that never took a counted share.
    /// </remarks>
    private static void DisposePackedIn(JgsValue value, HashSet<JgsValue> visited)
    {
        if (value.Type == JgsType.Image)
        {
            if (visited.Add(value) && Releasable(value, value.AsImage))
            {
                value.AsImage.Dispose(); // release the image's native/mapped backing buffer
            }

            return;
        }

        // Cells and structs can hold arrays, so the walk has to go through them too.
        if (value.Type == JgsType.Cell && visited.Add(value))
        {
            if (Releasable(value, value.AsCell))
            {
                foreach (JgsValue element in value.AsCell)
                {
                    DisposePackedIn(element, visited);
                }
            }

            return;
        }

        if (value.Type == JgsType.Struct && visited.Add(value))
        {
            if (Releasable(value, value.AsStructArray))
            {
                foreach (Dictionary<string, JgsValue> element in value.AsStructArray.Elements)
                {
                    if (!Releasable(value, element))
                    {
                        continue;
                    }

                    foreach ((_, JgsValue field) in element)
                    {
                        DisposePackedIn(field, visited);
                    }
                }
            }

            return;
        }

        if (value.Type != JgsType.Array || !visited.Add(value))
        {
            return; // scalars, and any array already seen (self-referencing arrays are legal)
        }

        if (value.IsPacked)
        {
            if (Releasable(value, value.AsBuffer))
            {
                value.AsBuffer.Dispose();
            }

            return;
        }

        if (value.IsPackedComplex)
        {
            if (Releasable(value, value.AsPackedComplex))
            {
                value.AsPackedComplex.Dispose();
            }

            return;
        }

        if (!Releasable(value, value.AsArray))
        {
            return;
        }

        foreach (JgsValue element in value.AsArray)
        {
            DisposePackedIn(element, visited);
        }
    }

    /// <summary>
    /// Whether this walk may free <paramref name="payload"/> — or, for a container, descend into
    /// it: only when nobody else holds it and nothing outside the count can still read it (M6).
    /// </summary>
    private static bool Releasable(JgsValue holder, object payload)
    {
        // The count is only read here, never moved: a dead wrapper never decrements (M3), so a
        // count that has drifted high costs a delayed free, and moving it down on a walk that
        // proves nothing about who is alive would cost a freed payload someone can still read.
        return !holder.IsExposed && !JgsHolders.IsShared(payload);
    }
}
