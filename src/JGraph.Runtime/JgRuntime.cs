using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using JGraph.Core.Model;
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
/// <para>
/// Between calls the engine thread delivers what each component's windows queued and runs its
/// timers when they are due, as R2025b's runtime does (<c>record-k5.ps1</c>,
/// <c>recordings/k5_client.out.txt</c>: a timer one call started ticked on while the client slept).
/// A component's figures are shown in windows by <c>JGraph.Runtime.Desktop</c>, found the first
/// time one is shown (stage K5); a window's report about a component that is not the live one is
/// run on the engine thread with that component's state in place.
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
    private static int _ownSessions;

    /// <summary>Held while the process's state is swapped, and by a window's report that runs in the live component's.</summary>
    private static readonly object ActivationGate = new();

    /// <summary>The component each shown figure belongs to, which is where a report about its window runs.</summary>
    private static readonly ConditionalWeakTable<FigureModel, ComponentSession> FigureOwners = new();

    private static IRuntimeDesktop? _found;
    private static bool _desktopSought;
    private static bool _routing;
    private static bool _exitHooked;

    /// <summary>The window host, once a component has shown a figure and it was found; null before then and where there is none.</summary>
    internal static IRuntimeDesktop? LoadedDesktop => DesktopOverride ?? (Volatile.Read(ref _desktopSought) ? _found : null);

    /// <summary>A window host to use in place of <c>JGraph.Runtime.Desktop</c>'s (tests).</summary>
    internal static IRuntimeDesktop? DesktopOverride { get; set; }

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
            _engine = new EngineThread { Idle = RunIdle };

            // A client that never terminates the runtime still gets what terminating does when its
            // process ends: the warning about invisible figures, and the log file closed (stage K5).
            if (!_exitHooked)
            {
                _exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += static (_, _) => TerminateWithin(TimeSpan.FromSeconds(5));
            }

            return true;
        }
    }

    /// <summary>
    /// Ends every component's session and stops the engine thread. The runtime can be started again
    /// afterwards, which R2025b's cannot. Calls queued before this finish first.
    /// </summary>
    public static void Terminate() => TerminateWithin(Timeout.InfiniteTimeSpan);

    /// <summary>Terminates, waiting at most <paramref name="wait"/> for the calls queued before (the process's end).</summary>
    private static void TerminateWithin(TimeSpan wait)
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

        Task<bool> ended = engine.Run(() =>
        {
            WarnInvisibleFigures();
            foreach (ComponentSession session in Sessions.Values)
            {
                Activate(session);
                session.End();
            }

            Sessions.Clear();
            if (_routing)
            {
                _routing = false;
                ScriptGraphicsCallbacks.Router = null;
                ScriptEventQueue.InstallPump(null);
            }

            // The last component's state goes with it, and the process gets back what it held.
            _ = JgsProcessState.Take();
            _host?.Put();
            _host = null;
            _active = null;
            return true;
        });
        if (wait == Timeout.InfiniteTimeSpan)
        {
            ended.GetAwaiter().GetResult();
        }
        else
        {
            try
            {
                if (!ended.Wait(wait))
                {
                    // The process is ending under a call that has not finished; nothing more can be done.
                    return;
                }
            }
            catch (AggregateException)
            {
                // The process is ending and its windows' thread with it; what could be said was said.
                return;
            }
        }

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
    /// is extracted into the runtime cache and a deployed session is started over it. With
    /// <paramref name="own"/>, a new session every time, which <see cref="Close"/> ends. Runs on the
    /// engine thread.
    /// </summary>
    internal static ComponentSession Open(string archivePath, string uuid, bool own = false)
    {
        if (!own && Sessions.TryGetValue(uuid, out ComponentSession? open))
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
            Sessions[own ? uuid + "#" + (++_ownSessions).ToString(System.Globalization.CultureInfo.InvariantCulture) : uuid] = session;
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
    /// Ends a session a component had of its own (<see cref="Component.Dispose"/>): its figures and
    /// state go, and a value it made can no longer be used. Engine thread.
    /// </summary>
    internal static void Close(ComponentSession session)
    {
        string? key = Sessions.FirstOrDefault(pair => ReferenceEquals(pair.Value, session)).Key;
        if (key is null)
        {
            return;
        }

        Activate(session);
        session.End();
        Sessions.Remove(key);
        Activate(null);
    }

    /// <summary>
    /// Makes <paramref name="session"/>'s process-wide state the process's, holding the last active
    /// one's aside; null holds it aside and leaves the process fresh. Runs on the engine thread.
    /// </summary>
    internal static void Activate(ComponentSession? session)
    {
        lock (ActivationGate)
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

    /// <summary>
    /// Runs <paramref name="work"/> on the engine thread with <paramref name="session"/> made live,
    /// after the calls queued before it: the native bridge's reads and writes of an object a call
    /// returned to C (stage K10), which knows the session that made it and not a component handle.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runtime, and the session with it, has ended.</exception>
    internal static T RunIn<T>(ComponentSession session, Func<T> work)
    {
        EngineThread? engine;
        lock (Gate)
        {
            engine = _engine;
        }

        if (engine is null)
        {
            throw new InvalidOperationException("The JGraph Runtime is not running.");
        }

        return engine.Run(() =>
        {
            if (!Sessions.ContainsValue(session))
            {
                throw new InvalidOperationException("The component that made the value has ended.");
            }

            Activate(session);
            return work();
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Shows <paramref name="owner"/>'s figure in its window, finding the window host the first time
    /// (stage K5). Engine thread, from the session's show. Nothing is shown where there is no host.
    /// </summary>
    internal static void ShowFigure(ComponentSession owner, int number, FigureModel figure)
    {
        if (Desktop() is not { } desktop)
        {
            return;
        }

        FigureOwners.AddOrUpdate(figure, owner);
        if (!_routing)
        {
            // From the first window on, reports about a window run in its component's state, and a
            // queued callback wakes the engine thread to deliver it.
            _routing = true;
            ScriptGraphicsCallbacks.Router = new GraphicsRouter();
            ScriptEventQueue.InstallPump(Poke);
        }

        // Components made before the window host was there to take their frames (the first show is
        // what starts it) get their first frame now, so the window opens on them.
        if (figure.Components.Count > 0 && figure.LastComponentFrame is null)
        {
            _ = ScriptComponentFrames.TakeFrame(figure);
        }

        desktop.Show(owner, number, figure);
    }

    private static IRuntimeDesktop? Desktop()
    {
        if (DesktopOverride is { } given)
        {
            return given;
        }

        if (!_desktopSought)
        {
            _found = FindDesktop();
            Volatile.Write(ref _desktopSought, true);
        }

        return _found;
    }

    /// <summary>
    /// <c>JGraph.Runtime.Desktop</c>'s host, from the process's own assemblies or from beside this
    /// one (the runtime folder's <c>bin</c>), started on demand. Null where it is missing or the
    /// process cannot load WPF: a client whose runtimeconfig does not name the Windows desktop framework.
    /// </summary>
    private static IRuntimeDesktop? FindDesktop()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string folder = Path.GetDirectoryName(typeof(JgRuntime).Assembly.Location) ?? AppContext.BaseDirectory;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string beside = Path.Combine(folder, name.Name + ".dll");
            return File.Exists(beside) ? context.LoadFromAssemblyPath(beside) : null;
        };

        try
        {
            Assembly desktop = Assembly.Load("JGraph.Runtime.Desktop");
            object? host = desktop.GetType("JGraph.Runtime.Desktop.DesktopHost", throwOnError: true)!
                .GetMethod("Start", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, null);
            return host as IRuntimeDesktop;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException
            or TargetInvocationException or TypeLoadException or MissingMethodException)
        {
            return null;
        }
    }

    /// <summary>Wakes the engine thread to deliver a callback a window queued (the event queue's pump).</summary>
    private static void Poke()
    {
        EngineThread? engine;
        lock (Gate)
        {
            engine = _engine;
        }

        engine?.Post(static () => { });
    }

    /// <summary>
    /// The engine's work between calls: each component's queued callbacks and due timers, run with
    /// its state in place. Answers how long until the next timer is due, or null.
    /// </summary>
    private static TimeSpan? RunIdle()
    {
        long? next = null;
        foreach (ComponentSession session in Sessions.Values.ToArray())
        {
            bool waiting = ReferenceEquals(session, _active) ? ScriptEventQueue.Count > 0 : session.Saved is { HasEvents: true };
            long? due = session.NextTimerDue;
            if (waiting || due <= Environment.TickCount64)
            {
                Activate(session);
                session.RunIdle();
                due = session.NextTimerDue;
            }

            if (due is { } at && (next is null || at < next))
            {
                next = at;
            }
        }

        return next is { } soonest ? TimeSpan.FromMilliseconds(Math.Max(1, soonest - Environment.TickCount64)) : null;
    }

    /// <summary>
    /// R2025b's warning when its runtime ends with invisible figures open (<c>k5_client</c>), with
    /// JGraph's runtime named as in the start line. Engine thread.
    /// </summary>
    private static void WarnInvisibleFigures()
    {
        var names = new List<string>();
        foreach (ComponentSession session in Sessions.Values)
        {
            Activate(session);
            names.AddRange(session.InvisibleFigureNames());
        }

        if (names.Count == 0)
        {
            return;
        }

        var lines = new List<string>
        {
            $"Warning: {names.Count} invisible figure(s) at JGraph Runtime termination.",
            " Figure ID(s):",
            string.Empty,
        };
        lines.AddRange(names.Select(static name => "Name: " + name));
        lines.Add(string.Empty);
        lines.Add("If your application has terminated unexpectedly, please note that ");
        lines.Add(" applications generated by the MATLAB Compiler terminate when there are no ");
        lines.Add(" visible figure windows.");
        foreach (string line in lines)
        {
            _output.WriteError(line);
        }
    }

    /// <summary>
    /// Runs a window's report in its figure's component: now when that component is live, else on
    /// the engine thread once it has been made so (stage K5).
    /// </summary>
    private sealed class GraphicsRouter : IScriptGraphicsRouter
    {
        public bool Run(FigureModel? figure, Action body)
        {
            ComponentSession? owner = figure is not null && FigureOwners.TryGetValue(figure, out ComponentSession? found) ? found : null;
            lock (ActivationGate)
            {
                if (owner is null || ReferenceEquals(owner, _active))
                {
                    body();
                    return true;
                }
            }

            EngineThread? engine;
            lock (Gate)
            {
                engine = _engine;
            }

            engine?.Post(() =>
            {
                if (Sessions.ContainsValue(owner))
                {
                    Activate(owner);
                    body();
                }
            });
            return false;
        }
    }
}
