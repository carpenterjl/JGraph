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
    private readonly RoutedOutput _output;

    public ComponentSession(ScriptDeployment deployment, IScriptOutput output)
    {
        Deployment = deployment;
        _output = new RoutedOutput(output);

        // A component's figures are shown in its own windows where the window host is present (stage
        // K5), and closed with them. The current folder is the process's when the component opens,
        // as R2025b's pwd is (k4_client: the client's folder).
        var context = new ScriptContext(
            _output,
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

    /// <summary>The session's interpreter, for the MATLAB Data API's work on the engine thread (stage K9).</summary>
    internal Interpreter Interpreter => _session.Interpreter;

    /// <summary>
    /// Calls <paramref name="function"/> as <see cref="Call"/> does, on engine values, with the
    /// session's output and error text sent to <paramref name="output"/> and <paramref name="error"/>
    /// for the call when they are given (the MATLAB Data API's <c>RunOptions</c>, stage K9). Engine thread.
    /// </summary>
    internal JgsValue[] CallEngine(
        string function, int nargout, IReadOnlyList<JgsValue> arguments, TextWriter? output, TextWriter? error, CancellationToken cancellationToken,
        bool rawErrors = false)
    {
        (TextWriter? Out, TextWriter? Err) previous = _output.Writers;
        bool wasRaw = _output.RawErrors;
        _output.Writers = (output, error);
        _output.RawErrors = rawErrors;
        try
        {
            return _session.CallFunction(function, arguments, nargout, cancellationToken);
        }
        catch (JgsException failure)
        {
            throw Deployed(failure);
        }
        finally
        {
            _output.Writers = previous;
            _output.RawErrors = wasRaw;
        }
    }

    /// <summary>An uncaught error as its client sees it: the MException's identifier, message, stack, causes and report.</summary>
    internal static DeployedException Deployed(JgsException error)
    {
        var runtime = error as JgsRuntimeException;
        DeployedStackFrame[] stack =
        [
            .. (runtime?.Frames ?? []).Select(static f => new DeployedStackFrame(JgsDeployedMain.Shown(f), f.File, f.Line)),
        ];
        return new DeployedException(runtime?.Identifier ?? string.Empty, error.Message, stack, JgsDeployedMain.Report(error), error)
        {
            Causes = JgsDeployedMain.Causes(runtime),
        };
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
            throw Deployed(error);
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

/// <summary>
/// A component session's output: the runtime's console, or for one call the writers a MATLAB Data API
/// client gave in its <c>RunOptions</c> (stage K9) - standard output to the first, warnings and
/// standard error to the second - or a C program's print and error handlers (stage K10).
/// </summary>
internal sealed class RoutedOutput(IScriptOutput console) : IScriptOutput
{
    /// <summary>The writers of the call running now, or nulls for the console.</summary>
    public (TextWriter? Out, TextWriter? Err) Writers { get; set; }

    /// <summary>
    /// Whether the call's error writer takes a script's own error text as it was written: a C
    /// program's error handler gets <c>fprintf(2, 'x\n')</c> as <c>x\n</c> (k10_feval print), where
    /// a .NET client's writer gets the line R2025b's .NET runtime makes of it (k9_client).
    /// </summary>
    public bool RawErrors { get; set; }

    public void WriteErrorText(string text)
    {
        if (RawErrors && Writers.Err is { } writer)
        {
            writer.Write(text);
            writer.Flush();
        }
        else
        {
            WriteError(text);
        }
    }

    public void Write(string text)
    {
        if (Writers.Out is { } writer)
        {
            writer.Write(text);
            writer.Flush();
        }
        else
        {
            console.Write(text);
        }
    }

    public void WriteLine(string text)
    {
        if (Writers.Out is { } writer)
        {
            writer.Write(text + "\n");
            writer.Flush();
        }
        else
        {
            console.WriteLine(text);
        }
    }

    public void WriteError(string text)
    {
        if (Writers.Err is { } writer)
        {
            writer.Write(text + "\n");
            writer.Flush();
        }
        else
        {
            console.WriteError(text);
        }
    }
}
