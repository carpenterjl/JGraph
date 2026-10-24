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

        // A component's figures are shown in its own windows where the window host is present (stage
        // K5), and closed with them. The current folder is the process's when the component opens,
        // as R2025b's pwd is (k4_client: the client's folder).
        var context = new ScriptContext(
            output,
            (number, figure) => JgRuntime.ShowFigure(this, number, figure),
            Environment.CurrentDirectory,
            resolvePath: null,
            figureFiles: new RuntimeFigureFiles(),
            closeFigure: number => JgRuntime.LoadedDesktop?.Close(this, number))
        {
            Deployment = deployment,
        };
        _session = new JgsReplSession(context, JgsDialect.Matlab, "MATLAB");
    }

    /// <summary>When the session's earliest running timer is next due (<see cref="Environment.TickCount64"/> milliseconds), or null.</summary>
    public long? NextTimerDue => _session.NextTimerDue;

    /// <summary>
    /// Delivers the callbacks the component's windows queued and runs its due timers, as R2025b's
    /// runtime does between its client's calls (<c>k5_client</c>). Engine thread, this session active.
    /// </summary>
    public void RunIdle() => _session.RunIdle(CancellationToken.None);

    /// <summary>The names of the session's invisible figures, for the warning when the runtime ends. Engine thread, this session active.</summary>
    public IReadOnlyList<string> InvisibleFigureNames()
    {
        var names = new List<string>();
        foreach (int number in JGraph.Api.JG.FigureNumbers)
        {
            if (JGraph.Api.JG.TryGetFigure(number, out JGraph.Core.Model.FigureModel figure) && !figure.Visible)
            {
                // The Name a script reads, as waitforallfiguresclosed answers it: '' for one never named.
                JgsValue name = JgsGraphicsProperties.Get(JgsHandleRegistry.EntryFor(figure), "Name", 0, 0);
                names.Add(name.Type == JgsType.String ? name.AsString : string.Empty);
            }
        }

        return names;
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

    /// <summary>Ends the session: its windows, its open files, its native host, and the archive's cache entry.</summary>
    public void End()
    {
        JgRuntime.LoadedDesktop?.Retire(this);
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Deployment.Dispose();
    }
}
