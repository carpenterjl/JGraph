using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;

namespace JGraph.Runtime;

/// <summary>
/// The JGraph Runtime in this process (compiler and SDK plan, stage K4): started once, with the
/// runtime options R2025b's <c>-R</c> takes, and shared by every component the process opens.
/// </summary>
/// <remarks>
/// <para>
/// One runtime per process, with one engine thread, as R2025b's (<c>record-k4.ps1</c>,
/// <c>recordings/k4_client.out.txt</c>): calls from any thread wait their turn there, across
/// instances and components alike. Each component (each archive) has its own session: its own
/// workspace, globals, persistent variables, path, current folder, random stream, warning state,
/// user data, display format and figures, as R2025b gives each component its own. Two
/// <see cref="Component"/>s opened on one archive share its session, as two instances of one R2025b
/// component class do.
/// </para>
/// <para>
/// The engine keeps the figure registry, the graphics handles and the display format in statics,
/// so before a call for a component other than the last one the runtime moves the last one's out
/// and this one's in (<see cref="JgsProcessState"/>). What the process held before the runtime
/// started is put back when it terminates.
/// </para>
/// </remarks>
public static class JgRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ComponentSession> Sessions = new(StringComparer.Ordinal);
    private static EngineThread? _engine;
    private static IScriptOutput _output = ConsoleScriptOutput.Instance;
    private static DeployedLogOutput? _log;
    private static ComponentSession? _active;
    private static JgsProcessState? _host;

    /// <summary>Whether the runtime is running in this process.</summary>
    public static bool IsInitialized
    {
        get
        {
            lock (Gate)
            {
                return _engine is not null;
            }
        }
    }

    /// <summary>The options the runtime was started with.</summary>
    public static RuntimeOptions Options { get; private set; } = RuntimeOptions.None;

    /// <summary>
    /// Starts the runtime with <paramref name="options"/>, as R2025b's runtime starts under a
    /// component's <c>-R</c> options: it prints its start lines to standard output, holds the
    /// process's arithmetic to one thread under <c>-singleCompThread</c>, and opens the
    /// <c>-logfile</c> against the current folder. Opening a component starts the runtime with the
    /// component's own options when it is not running yet.
    /// </summary>
    /// <param name="options">The options; none when not given.</param>
    /// <returns>True when this call started the runtime; false when it was already running, in which case the options it runs with stand.</returns>
    public static bool Initialize(RuntimeOptions? options = null) => Initialize(options, ConsoleScriptOutput.Instance);

    /// <summary>Starts the runtime writing to <paramref name="console"/> where the process's standard streams would be (for tests).</summary>
    internal static bool Initialize(RuntimeOptions? options, IScriptOutput console)
    {
        lock (Gate)
        {
            if (_engine is not null)
            {
                return false;
            }

            Options = options ?? RuntimeOptions.None;
            _output = BatchRunner.StartRuntime(Options, console, Environment.CurrentDirectory, out _log);
            _engine = new EngineThread();
            return true;
        }
    }

    /// <summary>
    /// Ends every component's session and stops the engine thread. The runtime can be started again
    /// afterwards, which R2025b's cannot. Calls queued before this finish first.
    /// </summary>
    public static void Terminate()
    {
        EngineThread? engine;
        lock (Gate)
        {
            engine = _engine;
            if (engine is null)
            {
                return;
            }

            _engine = null;
        }

        engine.Invoke(() =>
        {
            foreach (ComponentSession session in Sessions.Values)
            {
                Activate(session);
                session.End();
            }

            Sessions.Clear();

            // The last component's state goes with it, and the process gets back what it held.
            _ = JgsProcessState.Take();
            _host?.Put();
            _host = null;
            _active = null;
            return true;
        });
        engine.Dispose();
        _log?.Dispose();
        _log = null;
        _output = ConsoleScriptOutput.Instance;
        Options = RuntimeOptions.None;
    }

    /// <summary>The engine thread, starting the runtime under <paramref name="options"/> when it is not running.</summary>
    internal static EngineThread Engine(RuntimeOptions options)
    {
        lock (Gate)
        {
            if (_engine is null)
            {
                Initialize(options);
            }

            return _engine!;
        }
    }

    /// <summary>
    /// The session of the archive <paramref name="archivePath"/> names, made on first use: the archive
    /// is extracted into the runtime cache and a deployed session is started over it. Runs on the
    /// engine thread.
    /// </summary>
    internal static ComponentSession Open(string archivePath, string uuid)
    {
        if (Sessions.TryGetValue(uuid, out ComponentSession? open))
        {
            return open;
        }

        ScriptDeployment deployment = ScriptDeployment.FromArchive(archivePath, [], runtimeOptions: Options);
        try
        {
            // Out with the last component's state (or the host's), so the new session starts as a
            // fresh one does, with its own figures and format.
            Activate(null);
            var session = new ComponentSession(deployment, _output);
            Sessions[uuid] = session;
            _active = session;
            return session;
        }
        catch
        {
            deployment.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Makes <paramref name="session"/>'s process-wide state the process's, holding the last active
    /// one's aside; null holds it aside and leaves the process fresh. Runs on the engine thread.
    /// </summary>
    internal static void Activate(ComponentSession? session)
    {
        if (session is not null && ReferenceEquals(session, _active))
        {
            return;
        }

        JgsProcessState taken = JgsProcessState.Take();
        if (_active is null)
        {
            _host ??= taken;
        }
        else
        {
            _active.Saved = taken;
        }

        _active = session;
        if (session?.Saved is { } saved)
        {
            saved.Put();
            session.Saved = null;
        }
    }
}
