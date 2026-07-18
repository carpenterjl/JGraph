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
    public static ScriptRunResult Run(
        string code,
        ScriptContext context,
        CancellationToken cancellationToken,
        string sourceId = "",
        IJgsDebugHook? hook = null)
    {
        // JGS scripts drive the same static JG facade; start each run from a clean state.
        JG.Reset();
        var globals = new JGraphScriptGlobals(context);

        try
        {
            IReadOnlyList<Stmt> program = Parser.Parse(code, sourceId);
            JgsEnvironment environment = JgsBuiltins.CreateGlobals(globals, cancellationToken);
            var interpreter = new Interpreter(environment, cancellationToken, hook,
                echo: line => context.Output.WriteLine(line));
            DefineRunBuiltin(environment, interpreter, globals);
            hook?.RunStarting(interpreter, environment);

            // Capture the pristine builtin bindings so the post-run snapshot lists only what the
            // script itself defined (or rebound).
            Dictionary<string, JgsValue> pristine = environment.Locals.ToDictionary(
                static p => p.Key, static p => p.Value, StringComparer.Ordinal);

            interpreter.Run(program);
            globals.ShowUnshownFigures(); // MATLAB expectation: created figures appear without show()
            return ScriptRunResult.Ok(globals.FiguresShown, SnapshotGlobals(environment, pristine));
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
    }

    /// <summary>Projects a JGS value to the UI-facing <see cref="ScriptVariable"/> shape.</summary>
    public static ScriptVariable ToScriptVariable(string name, JgsValue value) =>
        new(name, value.TypeName, ScriptVariable.Truncate(value.Display()), ToRawValue(value));

    /// <summary>
    /// Defines the <c>run(path)</c> builtin: it resolves the path like the table readers do, parses the
    /// file, and executes it into the global scope (functions hoisted first) — MATLAB-style script
    /// composition. Re-entrant includes are guarded so a cycle fails with a clear error.
    /// </summary>
    private static void DefineRunBuiltin(JgsEnvironment environment, Interpreter interpreter, JGraphScriptGlobals globals)
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
                // step-in lands in the right editor tab.
                interpreter.Run(Parser.Parse(source, fullPath));
            }
            finally
            {
                including.Remove(fullPath);
            }

            return JgsValue.Null;
        })));
    }

    private static IReadOnlyList<ScriptVariable> SnapshotGlobals(
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

    private static object? ToRawValue(JgsValue value) => value.Type switch
    {
        JgsType.Number => value.AsNumber,
        JgsType.Bool => value.AsBool,
        JgsType.String => value.AsString,
        JgsType.Table => value.AsTable,
        JgsType.Array when value.AsArray.All(static e => e.Type == JgsType.Number) =>
            value.AsArray.Select(static e => e.AsNumber).ToArray(),
        _ => null,
    };
}
