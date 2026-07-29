using System.IO;
using System.Linq;
using JGraph.Api;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The shared body of a JGS run, used by <see cref="JgsScriptEngine"/> (and, later, the debug session)
/// so plain and debugged runs cannot drift: reset the facade, parse, seed the built-ins, wire the
/// <c>run()</c> include builtin, execute, and snapshot the globals the script defined.
/// </summary>
internal static class JgsRunner
{
    /// <summary>Runs <paramref name="code"/> and maps every JGS failure to a diagnostic result.</summary>
    /// <param name="code">The JGS source.</param>
    /// <param name="context">The host services for the run.</param>
    /// <param name="cancellationToken">Checked cooperatively before every statement.</param>
    /// <param name="sourceId">The identity of <paramref name="code"/> (file path or ""), stamped on its
    /// statements so a debugger can map execution to the right document.</param>
    /// <param name="hook">The debug hook, or null for a plain run.</param>
    /// <param name="dialect">The language variant to run, or null for <see cref="JgsDialect.Jgs"/>.</param>
    public static ScriptRunResult Run(
        string code,
        ScriptContext context,
        CancellationToken cancellationToken,
        string sourceId = "",
        IJgsDebugHook? hook = null,
        JgsDialect? dialect = null)
    {
        dialect ??= JgsDialect.Jgs;

        // JGS scripts drive the same static JG facade; start each run from a clean state. The
        // previous completed run's packed buffers are released deterministically here (its figures
        // and variable snapshots hold copies, never the buffers); finalizers remain the backstop
        // for everything else.
        JG.Reset();
        DisposePreviousRunBuffers();
        var globals = new JGraphScriptGlobals(context);

        // A one-shot run knows which file it came from, and mfilename has to be able to say so.
        if (sourceId.Length > 0 && Path.IsPathRooted(sourceId))
        {
            globals.BeginRun(Path.GetDirectoryName(sourceId), sourceId);
        }

        try
        {
            IReadOnlyList<Stmt> program = Parser.Parse(code, sourceId, dialect);
            JgsEnvironment environment = JgsBuiltins.CreateGlobals(globals, cancellationToken, dialect);
            var interpreter = new Interpreter(environment, cancellationToken, hook,
                echo: line => context.Output.WriteLine(line), dialect);
            DefineRunBuiltin(environment, interpreter, globals, dialect);
            JgsBuiltins.RegisterEvalBuiltins(environment, interpreter, globals, dialect);
            JgsBuiltins.RegisterSessionBuiltins(environment, globals);

            // Capture the pristine builtin bindings so the post-run snapshot lists only what the
            // script itself defined (or rebound). save/load must be declared before the capture, or
            // they would list themselves as the user's variables.
            Dictionary<string, JgsValue> pristine = null!;
            JgsWorkspaceIo.DefineSaveLoad(environment, globals, () => environment.Locals
                .Where(p => !pristine.TryGetValue(p.Key, out JgsValue? original) || !ReferenceEquals(original, p.Value))
                .Select(static p => (p.Key, p.Value)));
            DefineWorkspaceBuiltins(environment, interpreter, context.Output, () => pristine);
            hook?.RunStarting(interpreter, environment);

            pristine = environment.Locals.ToDictionary(
                static p => p.Key, static p => p.Value, StringComparer.Ordinal);

            interpreter.Run(program);
            InvokeMainIfFunctionFile(program, environment);
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
            var diagnostic = new ScriptDiagnostic(ex.Line, ex.Column, ex.Message, IsError: true);
            context.Output.WriteError(diagnostic.ToString());
            return ScriptRunResult.Failed(ex.Message, new[] { diagnostic });
        }
        catch (OperationCanceledException)
        {
            return ScriptRunResult.Failed("Script run was cancelled.");
        }
        finally
        {
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
    internal static void InvokeMainIfFunctionFile(IReadOnlyList<Stmt> program, JgsEnvironment environment)
    {
        if (!IsFunctionFile(program))
        {
            return;
        }

        var main = (FnStmt)program[0];
        if (environment.TryGet(main.Name, out JgsValue value) && value.Type == JgsType.Function)
        {
            value.AsCallable.Call(System.Array.Empty<JgsValue>(), main.Line, main.Column);
        }
    }

    /// <summary>Projects a JGS value to the UI-facing <see cref="ScriptVariable"/> shape.</summary>
    public static ScriptVariable ToScriptVariable(string name, JgsValue value) =>
        new(name, value.TypeName, ScriptVariable.Truncate(value.Display()), ToRawValue(value));

    /// <summary>
    /// Defines the <c>run(path)</c> builtin: it resolves the path like the table readers do, parses the
    /// file, and executes it into the global scope (functions hoisted first) — MATLAB-style script
    /// composition. Re-entrant includes are guarded so a cycle fails with a clear error. An included
    /// file is parsed in the caller's dialect unless it is a <c>.m</c> file, which always means MATLAB.
    /// </summary>
    internal static void DefineRunBuiltin(
        JgsEnvironment environment, Interpreter interpreter, JGraphScriptGlobals globals, JgsDialect dialect)
    {
        var including = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        environment.Declare("run", JgsValue.Function(new BuiltinFunction("run", (args, line, column) =>
        {
            if (args.Count != 1 || args[0].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, column, "run(path) expects one string argument.");
            }

            string resolved = globals.Resolve(args[0].AsString);
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
                // just parses in it — a .m reached from JGS must still index 1-based and auto-declare.
                JgsDialect included = DialectForInclude(fullPath, dialect);
                interpreter.RunInDialect(included, () => interpreter.Run(Parser.Parse(source, fullPath, included)));
            }
            finally
            {
                including.Remove(fullPath);
            }

            return JgsValue.Null;
        })));
    }

    /// <summary>
    /// The dialect an included file is parsed in: MATLAB for a <c>.m</c> file, otherwise the including
    /// script's own. A <c>.m</c> file has to mean the same thing however it was reached.
    /// </summary>
    private static JgsDialect DialectForInclude(string path, JgsDialect caller) =>
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
        IEnumerable<(string Name, JgsValue Value)> UserVariables()
        {
            IReadOnlyDictionary<string, JgsValue> baseline = pristine();
            foreach ((string name, JgsValue value) in environment.Locals)
            {
                if (!baseline.TryGetValue(name, out JgsValue? original) || !ReferenceEquals(original, value))
                {
                    yield return (name, value);
                }
            }
        }

        // 'clear' and 'clearvars' behave identically here: user variables go, built-ins stay, and a
        // rebound built-in reverts (which is all clearvars' "variables only" restriction can mean in
        // a workspace where the built-ins are ordinary bindings).
        void DefineClear(string builtin)
        {
            environment.Declare(builtin, JgsValue.Function(new BuiltinFunction(builtin, (args, line, column) =>
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

                if (names.Count == 0 || names.Contains("all") || names.Contains("variables"))
                {
                    // Dropping everything at once takes every user wrapper (aliases included), so
                    // their packed buffers can be released deterministically. MATLAB's plain clear
                    // drops variables but not the functions a script defined — those need 'clear all'.
                    bool everything = names.Contains("all");
                    var dropped = new List<JgsValue>();
                    foreach ((string cleared, JgsValue value) in UserVariables().ToList())
                    {
                        if (!everything && value.Type == JgsType.Function
                            && interpreter.ScriptFunctionNames.Contains(cleared))
                        {
                            continue;
                        }

                        dropped.Add(value);
                        environment.Forget(cleared, baseline);
                    }

                    DisposeBuffers(dropped);
                    return JgsValue.Null;
                }

                foreach (string cleared in names)
                {
                    // No buffer disposal here: in JGS another name may still alias the wrapper.
                    environment.Forget(cleared, baseline);
                }

                return JgsValue.Null;
            })));
        }

        DefineClear("clear");
        DefineClear("clearvars");

        environment.Declare("whos", JgsValue.Function(new BuiltinFunction("whos", (args, line, column) =>
        {
            if (args.Count != 0)
            {
                throw new JgsRuntimeException(line, column, "whos takes no arguments.");
            }

            List<(string Name, string Size, string Kind)> rows = UserVariables()
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
        JgsType.Number or JgsType.Array => "double",
        JgsType.Complex => "complex",
        JgsType.Bool => "logical",
        JgsType.String => "char",
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Table => "table",
        JgsType.Image => "image",
        JgsType.Sparse => "double (sparse)",
        JgsType.Function => "function_handle",
        _ => value.TypeName,
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

    private static void DisposePackedIn(JgsValue value, HashSet<JgsValue> visited)
    {
        if (value.Type == JgsType.Image)
        {
            if (visited.Add(value))
            {
                value.AsImage.Dispose(); // release the image's native/mapped backing buffer
            }

            return;
        }

        // Cells and structs can hold arrays, so the walk has to go through them too.
        if (value.Type == JgsType.Cell && visited.Add(value))
        {
            foreach (JgsValue element in value.AsCell)
            {
                DisposePackedIn(element, visited);
            }

            return;
        }

        if (value.Type == JgsType.Struct && visited.Add(value))
        {
            foreach ((_, JgsValue field) in value.AsStruct)
            {
                DisposePackedIn(field, visited);
            }

            return;
        }

        if (value.Type != JgsType.Array || !visited.Add(value))
        {
            return; // scalars, and any array already seen (self-referencing arrays are legal)
        }

        if (value.IsPacked)
        {
            value.AsBuffer.Dispose();
            return;
        }

        if (value.IsPackedComplex)
        {
            value.AsPackedComplex.Dispose();
            return;
        }

        foreach (JgsValue element in value.AsArray)
        {
            DisposePackedIn(element, visited);
        }
    }
}
