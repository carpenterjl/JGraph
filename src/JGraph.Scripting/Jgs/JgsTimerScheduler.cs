namespace JGraph.Scripting.Jgs;

/// <summary>
/// The private half of a <c>timer</c> (V6, ADR 0167, appendix A #105): what the script cannot read
/// as a property, or reads only through one. A timer is a struct wearing the class name, like a
/// <c>VideoWriter</c>, and a handle: every alias is the one object, so its storage is its identity
/// and a weak table on that storage holds this beside it exactly as long as the value lives.
/// </summary>
internal sealed class JgsTimerState
{
    /// <summary>The struct the script holds — the value every callback is handed as its first argument.</summary>
    public required JgsValue Value { get; init; }

    /// <summary>The scheduler that fires this timer, which is the run's.</summary>
    public required JgsTimerScheduler Scheduler { get; init; }

    /// <summary>The visible properties, written in place: a handle's fields are its own.</summary>
    public Dictionary<string, JgsValue> Fields => Value.AsStruct;

    /// <summary>The <c>Name</c> property as it stands, for the messages that name the timer.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>delete(t)</c> has run: every property read or write, and every verb, refuses.</summary>
    public bool Deleted { get; set; }

    /// <summary>Between <c>start</c> and the stop that ends it — the <c>Running</c> property.</summary>
    public bool Running { get; set; }

    /// <summary>When the next <c>TimerFcn</c> is due, in <see cref="Environment.TickCount64"/> milliseconds.</summary>
    public long Due { get; set; }

    /// <summary>The order this timer was armed in, which breaks a tie between two timers due at once.</summary>
    public long Sequence { get; set; }

    /// <summary>How many <c>TimerFcn</c>s have run since <c>start</c> — the <c>TasksExecuted</c> property.</summary>
    public int Executed { get; set; }

    /// <summary>How many of this timer's callbacks are on the stack. A timer whose callback is
    /// running is never fired again underneath it.</summary>
    public int CallbackDepth { get; set; }

    /// <summary>Whether one of this timer's callbacks is running now.</summary>
    public bool InCallback => CallbackDepth > 0;

    /// <summary>When the first and the latest <c>TimerFcn</c> of this start ran, for the two period readings.</summary>
    public long? FirstFire { get; set; }

    /// <summary>See <see cref="FirstFire"/>.</summary>
    public long? LastFire { get; set; }

    /// <summary>The <c>Period</c> property, in seconds; validated when it is set.</summary>
    public double Period { get; set; } = 1;

    /// <summary>The <c>StartDelay</c> property, in seconds.</summary>
    public double StartDelay { get; set; }

    /// <summary>The <c>TasksToExecute</c> property; infinite by default.</summary>
    public double TasksToExecute { get; set; } = double.PositiveInfinity;

    /// <summary>The <c>ExecutionMode</c> property, in its canonical spelling.</summary>
    public string Mode { get; set; } = "singleShot";
}

/// <summary>
/// The run's timers and when they fire (V6, ADR 0167, appendix A #105).
/// </summary>
/// <remarks>
/// <para>
/// There is no timer thread. A callback runs on the script thread, at a drain point, and nowhere
/// else: <c>pause</c>, <c>drawnow</c>, <c>getframe</c>, <c>wait</c>, <c>start</c> itself, and the
/// boundary between two statements — never between two C# steps of one statement. That is the rule
/// that keeps M5's scopes honest (a statement's held operands are read before anything can write
/// them) and it is what R2025b does as well: a <c>TimerFcn</c> due during a busy <c>for</c> loop
/// runs between two of its iterations, and one due during <c>pause</c> runs inside the pause.
/// </para>
/// <para>
/// The scheduler belongs to the run's <see cref="JGraphScriptGlobals"/>, and the interpreter
/// reaches it through its own <see cref="Interpreter.Timers"/>, so two runs on two threads (the
/// test lanes) never see each other's timers; a static would have let one run's callback fire on
/// another's thread. The firing itself is <see cref="JgsBuiltins"/>' business (it runs script
/// code, which the ownership audit tracks by name); this class keeps the list and the clock.
/// </para>
/// </remarks>
internal sealed class JgsTimerScheduler
{
    /// <summary>Nested drains stop at this depth — a timer whose callback pauses is not a stack overflow.</summary>
    private const int MaxDrainDepth = 16;

    private readonly List<JgsTimerState> _armed = new();
    private int _drainDepth;
    private int _named;
    private long _sequence;

    public JgsTimerScheduler(Interpreter interpreter, JGraphScriptGlobals host)
    {
        Interpreter = interpreter;
        Host = host;
    }

    /// <summary>The interpreter a text callback is evaluated in, and whose base workspace it sees.</summary>
    public Interpreter Interpreter { get; }

    /// <summary>The run's host, where a callback's failure is reported.</summary>
    public JGraphScriptGlobals Host { get; }

    /// <summary>Whether any timer is running, so a statement boundary can skip the clock entirely.</summary>
    public bool Armed => _armed.Count > 0;

    /// <summary>The next default <c>Name</c>: MATLAB's <c>timer-1</c>, <c>timer-2</c>, … for this run.</summary>
    public string NextName() => $"timer-{++_named}";

    /// <summary>Puts a timer on the clock, due at <paramref name="due"/>.</summary>
    public void Arm(JgsTimerState state, long due)
    {
        state.Due = due;
        state.Sequence = ++_sequence;
        if (!_armed.Contains(state))
        {
            _armed.Add(state);
        }
    }

    /// <summary>Takes a timer off the clock; nothing when it was not on it.</summary>
    public void Disarm(JgsTimerState state) => _armed.Remove(state);

    /// <summary>
    /// Fires every timer that is due, oldest due first, each through
    /// <see cref="JgsBuiltins.FireTimer"/>. Takes only the timers due when it starts, so a callback
    /// that re-arms its timer yields back to the statement that reached this point; the next drain
    /// point picks it up. A timer whose callback is already on the stack is left alone.
    /// </summary>
    public void Drain()
    {
        if (_drainDepth >= MaxDrainDepth || _armed.Count == 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        List<JgsTimerState>? due = null;
        foreach (JgsTimerState state in _armed)
        {
            if (state.Due <= now && !state.InCallback)
            {
                (due ??= new List<JgsTimerState>()).Add(state);
            }
        }

        if (due is null)
        {
            return;
        }

        due.Sort(static (a, b) => a.Due != b.Due ? a.Due.CompareTo(b.Due) : a.Sequence.CompareTo(b.Sequence));
        _drainDepth++;
        try
        {
            foreach (JgsTimerState state in due)
            {
                // An earlier callback in this pass may have stopped or deleted a later timer.
                if (state.Running && !state.Deleted && !state.InCallback)
                {
                    JgsBuiltins.FireTimer(state);
                }
            }
        }
        finally
        {
            _drainDepth--;
        }
    }
}
