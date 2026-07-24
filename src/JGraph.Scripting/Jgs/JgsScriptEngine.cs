namespace JGraph.Scripting.Jgs;

/// <summary>
/// Runs scripts written in JGS — JGraph's small built-in scripting language. Unlike the C# and Python
/// engines (which host external runtimes), JGS is a self-contained tree-walking interpreter defined in this
/// project, so it is always available and has no dependencies. Its built-ins mirror the JGraph functional
/// API, so a JGS script builds figures the same way a C# or Python script does. Because the interpreter is
/// ours, cancellation is honoured even inside a tight loop.
/// </summary>
/// <summary>
/// An engine whose language the JGS debugger can step through. Debugging is a capability of this
/// interpreter — we own it — rather than of the engine seam, and both dialects get it for free.
/// </summary>
public interface IJgsDebuggable
{
    /// <summary>Creates an interactive debug session for one run of this engine's language.</summary>
    Debug.JgsDebugSession CreateDebugSession();
}

public sealed class JgsScriptEngine : IScriptEngine, IJgsDebuggable
{
    private readonly Func<JgsLanguageOptions> _options;

    /// <summary>Creates the engine with the shipped JGS defaults (<c>let</c> required, 0-based).</summary>
    public JgsScriptEngine()
        : this(null)
    {
    }

    /// <summary>
    /// Creates the engine reading its language options from <paramref name="options"/> on each run, so a
    /// change the user makes in the Options dialog takes effect on the next run without a restart. A null
    /// provider means the shipped defaults.
    /// </summary>
    public JgsScriptEngine(Func<JgsLanguageOptions>? options) =>
        _options = options ?? (static () => JgsLanguageOptions.Default);

    /// <inheritdoc />
    public string Language => "JGS";

    /// <summary>Always true — JGS is built in and needs no external runtime.</summary>
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<ScriptRunResult> RunAsync(string code, ScriptContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        JgsDialect dialect = JgsDialect.JgsWith(_options().Sanitized());
        return Task.Run(
            () => JgsRunner.Run(code, context, cancellationToken, sourceId: "", hook: null, dialect),
            cancellationToken);
    }

    /// <summary>
    /// Creates an interactive debug session (breakpoints, stepping, variable inspection) for one JGS
    /// run, under the user's current language options.
    /// </summary>
    public Debug.JgsDebugSession CreateDebugSession() => new(JgsDialect.JgsWith(_options().Sanitized()));

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
