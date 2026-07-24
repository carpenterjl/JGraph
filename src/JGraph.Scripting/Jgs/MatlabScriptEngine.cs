namespace JGraph.Scripting.Jgs;

/// <summary>
/// Runs MATLAB scripts — <c>.m</c> files — on the same interpreter JGS uses, in the MATLAB dialect:
/// 1-based indexing, <c>%</c> comments, no <c>let</c>, value semantics for arrays, <c>function</c>
/// declarations, cells and structs. A <c>.m</c> file means the same thing here as it does in MATLAB
/// however JGraph was started, so this engine never consults the user's JGS language settings.
/// </summary>
public sealed class MatlabScriptEngine : IScriptEngine, IJgsDebuggable
{
    /// <inheritdoc />
    public string Language => "MATLAB";

    /// <summary>Always true — the interpreter is built in and needs no external runtime.</summary>
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<ScriptRunResult> RunAsync(string code, ScriptContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        return Task.Run(
            () => JgsRunner.Run(code, context, cancellationToken, sourceId: "", hook: null, JgsDialect.Matlab),
            cancellationToken);
    }

    /// <inheritdoc />
    public Debug.JgsDebugSession CreateDebugSession() => new(JgsDialect.Matlab);
}
