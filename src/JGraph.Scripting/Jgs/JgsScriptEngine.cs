namespace JGraph.Scripting.Jgs;

/// <summary>
/// Runs scripts written in JGS — JGraph's small built-in scripting language. Unlike the C# and Python
/// engines (which host external runtimes), JGS is a self-contained tree-walking interpreter defined in this
/// project, so it is always available and has no dependencies. Its built-ins mirror the JGraph functional
/// API, so a JGS script builds figures the same way a C# or Python script does. Because the interpreter is
/// ours, cancellation is honoured even inside a tight loop.
/// </summary>
public sealed class JgsScriptEngine : IScriptEngine
{
    /// <inheritdoc />
    public string Language => "JGS";

    /// <summary>Always true — JGS is built in and needs no external runtime.</summary>
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<ScriptRunResult> RunAsync(string code, ScriptContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        return Task.Run(() => JgsRunner.Run(code, context, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Creates an interactive debug session (breakpoints, stepping, variable inspection) for one JGS
    /// run. Debugging is a JGS-specific capability — we own this interpreter — so it lives here rather
    /// than on the engine seam.
    /// </summary>
    public Debug.JgsDebugSession CreateDebugSession() => new();

    /// <summary>
    /// The names of every JGS builtin (including <c>run</c>), for editors and completion. Derived from
    /// the live registration, so it can never drift from the language.
    /// </summary>
    public static IReadOnlyCollection<string> BuiltinNames()
    {
        var context = new ScriptContext(NullScriptOutput.Instance, static (_, _) => { });
        JgsEnvironment globals = JgsBuiltins.CreateGlobals(new JGraphScriptGlobals(context));
        var names = new List<string>(globals.Locals.Keys) { "run" };
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private sealed class NullScriptOutput : IScriptOutput
    {
        public static readonly NullScriptOutput Instance = new();

        public void Write(string text)
        {
        }

        public void WriteLine(string text)
        {
        }

        public void WriteError(string text)
        {
        }
    }
}
