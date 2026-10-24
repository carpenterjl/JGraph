using JGraph.Scripting;
using JGraph.Scripting.Jgs;

namespace JGraph.Runtime;

/// <summary>
/// One component's deployed session: the archive's extracted copy, a MATLAB session over it with
/// the archive's frozen path, and the process-wide state held aside while another component runs.
/// Everything here runs on the engine thread.
/// </summary>
internal sealed class ComponentSession
{
    private readonly JgsReplSession _session;

    public ComponentSession(ScriptDeployment deployment, IScriptOutput output)
    {
        Deployment = deployment;

        // Figures are not shown: a component's windows are stage K5's. The current folder is the
        // process's when the component opens, as R2025b's pwd is (k4_client: the client's folder).
        var context = new ScriptContext(output, static (_, _) => { }, Environment.CurrentDirectory)
        {
            Deployment = deployment,
        };
        _session = new JgsReplSession(context, JgsDialect.Matlab, "MATLAB");
    }

    /// <summary>The archive's deployment: its extracted copy, manifest and frozen path.</summary>
    public ScriptDeployment Deployment { get; }

    /// <summary>The process-wide state held aside while another component runs, or null while this one is active.</summary>
    public JgsProcessState? Saved { get; set; }

    /// <summary>
    /// Calls <paramref name="function"/> with <paramref name="nargout"/> outputs. The arguments are
    /// minted fresh into the session and the outputs copied out; an error the function did not catch
    /// is a <see cref="DeployedException"/>.
    /// </summary>
    public RuntimeValue[] Call(string function, int nargout, IReadOnlyList<RuntimeValue> arguments, CancellationToken cancellationToken)
    {
        JgsValue[] inputs = [.. arguments.Select(a => RuntimeValueConversion.ToEngine(a, this))];
        JgsValue[] outputs;
        try
        {
            outputs = _session.CallFunction(function, inputs, nargout, cancellationToken);
        }
        catch (JgsException error)
        {
            var runtime = error as JgsRuntimeException;
            DeployedStackFrame[] stack =
            [
                .. (runtime?.Frames ?? []).Select(static f => new DeployedStackFrame(JgsDeployedMain.Shown(f), f.File, f.Line)),
            ];
            throw new DeployedException(runtime?.Identifier ?? string.Empty, error.Message, stack, JgsDeployedMain.Report(error), error);
        }

        // A call asked for no outputs answers none, whatever ans the function left.
        return [.. outputs.Take(nargout).Select(o => RuntimeValueConversion.FromEngine(o, this))];
    }

    /// <summary>Ends the session: its open files, its native host, and the archive's cache entry.</summary>
    public void End()
    {
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Deployment.Dispose();
    }
}
