namespace JGraph.Scripting;

/// <summary>
/// A language host that compiles and runs a JGraph script. Implementations are stateless with respect to
/// a single run: everything a script touches arrives through the <see cref="ScriptContext"/>. Engines
/// report ordinary script failures through <see cref="ScriptRunResult"/> rather than by throwing.
/// </summary>
public interface IScriptEngine
{
    /// <summary>The language name shown to users and used to select the engine (e.g. "C#", "Python").</summary>
    string Language { get; }

    /// <summary>
    /// Whether the engine can actually run on this machine. The C# engine is always available; the Python
    /// engine is available only when a CPython runtime is found, and otherwise degrades gracefully.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Compiles and runs <paramref name="code"/> against <paramref name="context"/>. Never throws for a
    /// script-level error (syntax, runtime exception, missing runtime); those come back as a failed
    /// <see cref="ScriptRunResult"/>.
    /// </summary>
    Task<ScriptRunResult> RunAsync(string code, ScriptContext context, CancellationToken cancellationToken);
}
