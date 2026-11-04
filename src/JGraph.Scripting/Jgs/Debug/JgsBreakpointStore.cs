namespace JGraph.Scripting.Jgs.Debug;

/// <summary>What <c>dbstop if</c> pauses on (App Designer window plan W11, R2025b's four conditions).</summary>
public enum StopCondition
{
    /// <summary><c>dbstop if error</c>: a run-time error no <c>try</c> catches.</summary>
    Error,

    /// <summary><c>dbstop if caught error</c>: a run-time error a <c>try</c> catches.</summary>
    CaughtError,

    /// <summary><c>dbstop if warning</c>: a warning as it is raised.</summary>
    Warning,

    /// <summary><c>dbstop if naninf</c> (or <c>infnan</c>): a statement whose result holds NaN or Inf.</summary>
    NanInf,
}

/// <summary>One breakpoint of a session: a file, its line, the condition typed after <c>if</c> ("" for none) and whether it is enabled.</summary>
/// <param name="File">The file's full path, as the interpreter names the statements parsed from it.</param>
/// <param name="Line">The 1-based line, an executable one.</param>
/// <param name="Condition">The expression that must be true for the pause, or "".</param>
/// <param name="Enabled">Whether the breakpoint pauses; App Designer's Enable/Disable keeps a disabled one in place.</param>
public sealed record JgsBreakpoint(string File, int Line, string Condition, bool Enabled);

/// <summary>
/// The breakpoints of one session (App Designer window plan W11): what <c>dbstop</c>, <c>dbclear</c>
/// and <c>dbstatus</c> work on, what the editors' margins show and what the attached debugger
/// (<see cref="JgsDebugSession"/>) reads before each statement. Lines are kept by file; the
/// <c>dbstop if</c> conditions and App Designer's Pause on Output beside them. Every change raises
/// <see cref="Changed"/> on the caller's thread.
/// </summary>
public sealed class JgsBreakpointStore
{
    private static StringComparer FileComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _lock = new();
    private readonly Dictionary<string, SortedDictionary<int, JgsBreakpoint>> _files = new(FileComparer);

    /// <summary>The conditions that are on: null for every identifier, else the identifiers listed.</summary>
    private readonly Dictionary<StopCondition, HashSet<string>?> _conditions = new();

    private bool _pauseOnOutput;

    /// <summary>Raised after any change, on the thread that made it.</summary>
    public event EventHandler? Changed;

    /// <summary>App Designer's Pause on Output: pause at the statement that displays something (no <c>dbstop if</c> word of R2025b's; <c>dbstatus</c> does not list it).</summary>
    public bool PauseOnOutput
    {
        get => _pauseOnOutput;
        set
        {
            lock (_lock)
            {
                if (_pauseOnOutput == value)
                {
                    return;
                }

                _pauseOnOutput = value;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether nothing is set: no line, no condition, no Pause on Output. The debugger stands aside then.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _files.Count == 0 && _conditions.Count == 0 && !_pauseOnOutput;
            }
        }
    }

    /// <summary>Every breakpoint, by file (ordinal) then line.</summary>
    public IReadOnlyList<JgsBreakpoint> All
    {
        get
        {
            lock (_lock)
            {
                return _files.OrderBy(static f => f.Key, StringComparer.Ordinal).SelectMany(static f => f.Value.Values).ToList();
            }
        }
    }

    /// <summary>The breakpoints in <paramref name="file"/>, by line.</summary>
    public IReadOnlyList<JgsBreakpoint> In(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (_lock)
        {
            return _files.TryGetValue(file, out SortedDictionary<int, JgsBreakpoint>? lines) ? lines.Values.ToList() : [];
        }
    }

    /// <summary>The breakpoint on <paramref name="line"/> of <paramref name="file"/>, or null.</summary>
    public JgsBreakpoint? Find(string file, int line)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (_lock)
        {
            return _files.TryGetValue(file, out SortedDictionary<int, JgsBreakpoint>? lines) && lines.TryGetValue(line, out JgsBreakpoint? found) ? found : null;
        }
    }

    /// <summary>
    /// Sets a breakpoint, or gives the one already on the line this <paramref name="condition"/>: R2025b's
    /// <c>dbstop in f at 4 if x > 2</c> over <c>if x > 1</c> replaces the expression, and a plain
    /// <c>dbstop in f at 4</c> after it drops the condition (probe <c>w11_debug</c>). A set breakpoint is enabled.
    /// </summary>
    public void Set(string file, int line, string condition = "")
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(condition);
        lock (_lock)
        {
            if (!_files.TryGetValue(file, out SortedDictionary<int, JgsBreakpoint>? lines))
            {
                _files[file] = lines = new SortedDictionary<int, JgsBreakpoint>();
            }

            lines[line] = new JgsBreakpoint(file, line, condition.Trim(), Enabled: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the breakpoint on a line; false when there was none.</summary>
    public bool Clear(string file, int line)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (_lock)
        {
            if (!_files.TryGetValue(file, out SortedDictionary<int, JgsBreakpoint>? lines) || !lines.Remove(line))
            {
                return false;
            }

            if (lines.Count == 0)
            {
                _files.Remove(file);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Clears every breakpoint in a file; answers how many there were.</summary>
    public int ClearFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        int count;
        lock (_lock)
        {
            if (!_files.Remove(file, out SortedDictionary<int, JgsBreakpoint>? lines))
            {
                return 0;
            }

            count = lines.Count;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return count;
    }

    /// <summary>Clears every breakpoint in every file (<c>dbclear all</c> without its conditions: see <see cref="ClearAll"/>).</summary>
    public void ClearLines()
    {
        lock (_lock)
        {
            if (_files.Count == 0)
            {
                return;
            }

            _files.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary><c>dbclear all</c>: every breakpoint and every condition (R2025b: <c>dbstatus</c> answers 0 entries after it, W0); Pause on Output too.</summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _files.Clear();
            _conditions.Clear();
            _pauseOnOutput = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets a breakpoint where there is none and clears the one there is (the margin's click, Set/Clear); answers whether one is set now.</summary>
    public bool Toggle(string file, int line)
    {
        if (Find(file, line) is not null)
        {
            Clear(file, line);
            return false;
        }

        Set(file, line);
        return true;
    }

    /// <summary>Enables or disables the breakpoint on a line, keeping its place and condition; false when there is none.</summary>
    public bool SetEnabled(string file, int line, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (_lock)
        {
            if (!_files.TryGetValue(file, out SortedDictionary<int, JgsBreakpoint>? lines) || !lines.TryGetValue(line, out JgsBreakpoint? found))
            {
                return false;
            }

            if (found.Enabled == enabled)
            {
                return true;
            }

            lines[line] = found with { Enabled = enabled };
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Replaces the breakpoints of <paramref name="file"/> with <paramref name="breakpoints"/>, keeping each one's condition and
    /// state: what App Designer's code view does when the class is written again around the user's code and its lines
    /// move (W11, <c>DesignDebug</c>). One event for the whole change.
    /// </summary>
    public void Replace(string file, IReadOnlyList<JgsBreakpoint> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(breakpoints);
        lock (_lock)
        {
            _files.Remove(file);
            if (breakpoints.Count > 0)
            {
                var lines = new SortedDictionary<int, JgsBreakpoint>();
                foreach (JgsBreakpoint breakpoint in breakpoints)
                {
                    lines[breakpoint.Line] = breakpoint with { File = file };
                }

                _files[file] = lines;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <c>dbstop if &lt;condition&gt; [identifier]</c>: without an identifier the condition is on for every identifier, and stays so
    /// whatever identifiers are named after (R2025b: <c>dbstop if error</c> then <c>dbstop if error MATLAB:badsubscript</c> still
    /// lists <c>all</c>); with one, it joins the identifiers listed. <see cref="StopCondition.NanInf"/> takes no identifier.
    /// </summary>
    public void StopIf(StopCondition condition, string? identifier = null)
    {
        lock (_lock)
        {
            if (identifier is null || condition == StopCondition.NanInf)
            {
                _conditions[condition] = null;
            }
            else if (!_conditions.TryGetValue(condition, out HashSet<string>? identifiers))
            {
                _conditions[condition] = new HashSet<string>(StringComparer.Ordinal) { identifier };
            }
            else if (identifiers is not null)
            {
                identifiers.Add(identifier);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <c>dbclear if &lt;condition&gt; [identifier]</c>: without an identifier the condition goes off; with one, that identifier is
    /// taken off the list - refused in R2025b's words when the condition is on for all
    /// (<c>MATLAB:dbclear:cannotClearWhenAllEnabled</c>). Clearing what is not set is no error.
    /// </summary>
    /// <param name="condition">The condition to clear.</param>
    /// <param name="identifier">The one identifier to take off its list, or null for the whole condition.</param>
    /// <param name="refusal">R2025b's message when the clear is refused, else null.</param>
    public bool ClearStopIf(StopCondition condition, string? identifier, out string? refusal)
    {
        refusal = null;
        bool changed;
        lock (_lock)
        {
            if (!_conditions.TryGetValue(condition, out HashSet<string>? identifiers))
            {
                return true;
            }

            if (identifier is null || condition == StopCondition.NanInf)
            {
                changed = _conditions.Remove(condition);
            }
            else if (identifiers is null)
            {
                string word = Word(condition);
                refusal = $"Cannot clear the 'dbstop if {word}' setting for '{identifier}' when the setting is enabled for all identifiers. Use 'dbclear if {word}' to clear the setting for all identifiers.";
                return false;
            }
            else
            {
                changed = identifiers.Remove(identifier);
                if (identifiers.Count == 0)
                {
                    _conditions.Remove(condition);
                }
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>The conditions on, in R2025b's <c>dbstatus</c> order, each with its identifiers: <c>["all"]</c> for every identifier, none for naninf.</summary>
    public IReadOnlyList<(StopCondition Condition, IReadOnlyList<string> Identifiers)> Conditions
    {
        get
        {
            lock (_lock)
            {
                var listed = new List<(StopCondition, IReadOnlyList<string>)>();
                foreach (StopCondition condition in (StopCondition[])[StopCondition.Error, StopCondition.CaughtError, StopCondition.Warning, StopCondition.NanInf])
                {
                    if (_conditions.TryGetValue(condition, out HashSet<string>? identifiers))
                    {
                        listed.Add((condition, condition == StopCondition.NanInf ? [] : identifiers is null ? ["all"] : identifiers.OrderBy(static i => i, StringComparer.Ordinal).ToList()));
                    }
                }

                return listed;
            }
        }
    }

    /// <summary>Whether <paramref name="condition"/> is on at all.</summary>
    public bool IsOn(StopCondition condition)
    {
        lock (_lock)
        {
            return _conditions.ContainsKey(condition);
        }
    }

    /// <summary>Whether a run should pause for <paramref name="condition"/> raised under <paramref name="identifier"/> ("" for none).</summary>
    public bool Stops(StopCondition condition, string identifier)
    {
        lock (_lock)
        {
            return _conditions.TryGetValue(condition, out HashSet<string>? identifiers)
                && (identifiers is null || identifiers.Contains(identifier));
        }
    }

    /// <summary>The enabled breakpoints the interpreter thread reads: file to line to condition, a snapshot made under the lock.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> EnabledLines()
    {
        lock (_lock)
        {
            var snapshot = new Dictionary<string, IReadOnlyDictionary<int, string>>(FileComparer);
            foreach ((string file, SortedDictionary<int, JgsBreakpoint> lines) in _files)
            {
                var enabled = new Dictionary<int, string>();
                foreach (JgsBreakpoint breakpoint in lines.Values)
                {
                    if (breakpoint.Enabled)
                    {
                        enabled[breakpoint.Line] = breakpoint.Condition;
                    }
                }

                if (enabled.Count > 0)
                {
                    snapshot[file] = enabled;
                }
            }

            return snapshot;
        }
    }

    /// <summary>R2025b's word for a condition, as <c>dbstop if</c> takes it and <c>dbstatus</c> prints it.</summary>
    public static string Word(StopCondition condition) => condition switch
    {
        StopCondition.Error => "error",
        StopCondition.CaughtError => "caught error",
        StopCondition.Warning => "warning",
        _ => "naninf",
    };
}
