namespace JGraph.Scripting.Jgs;

/// <summary>
/// The frames <c>dbstack</c> lists that are no function call (open item 302, ADR 0366): an anonymous
/// function's body while it runs, and a script run by name. R2025b lists each as a frame of its own,
/// in its place among the functions: <c>callit(@() dbstack)</c> written in a script is three frames,
/// the handle (named by its text, <c>@()dbstack</c>), <c>callit</c> and the script.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>
    /// The running frames that are no function call, innermost last: the handle or the script's path,
    /// how many functions were running when it was entered (its place among <see cref="_activeFunctions"/>),
    /// the line of the code that entered it, and the line it stands at whatever runs above it (-1 for the
    /// line running in it; <c>run</c>'s own frame is at a line of R2025b's <c>run.m</c>).
    /// </summary>
    private readonly List<(AnonymousFunction? Anonymous, string Script, int Depth, int CallLine, int Line)> _otherFrames = [];

    /// <summary>An anonymous function's body is running, entered from <paramref name="callLine"/>; <see cref="LeaveOtherFrame"/> ends it.</summary>
    internal void EnterAnonymousFrame(AnonymousFunction anonymous, int callLine) =>
        _otherFrames.Add((anonymous, string.Empty, _activeFunctions.Count, callLine, -1));

    /// <summary>A script named by its file <paramref name="path"/> is running, entered from <paramref name="callLine"/>.</summary>
    internal void EnterScriptFrame(string path, int callLine) =>
        _otherFrames.Add((null, path, _activeFunctions.Count, callLine, -1));

    /// <summary>The innermost frame <see cref="EnterAnonymousFrame"/> or <see cref="EnterScriptFrame"/> entered has ended.</summary>
    internal void LeaveOtherFrame() => _otherFrames.RemoveAt(_otherFrames.Count - 1);

    /// <summary>
    /// The frame the other frame at <paramref name="index"/> is, as <see cref="CurrentStack"/> lists it, and
    /// the line it was entered from. A handle is named by R2025b's text of it and its line is the line
    /// running in its body - 0 for one made from text by <c>eval</c> or <c>str2func</c> (measured); a
    /// script by its file's name.
    /// </summary>
    private (string Name, string File, int Line, int CallLine) OtherFrame(int index, int line)
    {
        (AnonymousFunction? anonymous, string script, _, int callLine, int fixedLine) = _otherFrames[index];
        if (anonymous is not null)
        {
            string text = anonymous.Declaration.MatlabText ?? anonymous.Text;
            return (text, FileOfFrame(anonymous.File), anonymous.Declaration.InText ? 0 : line, callLine);
        }

        return (Path.GetFileNameWithoutExtension(script), script, fixedLine >= 0 ? fixedLine : line, callLine);
    }

    /// <summary>
    /// An error is leaving the innermost frame <see cref="EnterAnonymousFrame"/> or <see cref="EnterScriptFrame"/>
    /// entered: it is a frame of <c>ME.stack</c>, as a function's is, named and placed as <c>dbstack</c> lists
    /// it (open item 334, ADR 0368; measured: <c>cellfun(@(x) error('p334:a', 'boom'), {1})</c> is
    /// <c>@(x)error('p334:a','boom')</c> at its line over the script, a script named in a statement is a frame
    /// at its own line, and a handle made from text is at line 0).
    /// </summary>
    internal void UnwindOtherFrame(JgsRuntimeException error)
    {
        (string name, string file, int line, int callLine) = OtherFrame(_otherFrames.Count - 1, error.PendingLine);
        if (HiddenFrame(name))
        {
            error.SkipFrame(callLine);
            return;
        }

        error.PushFrame(name, file, callLine, line);
    }

    /// <summary>
    /// Whether R2025b leaves the frame named <paramref name="name"/> out of <c>ME.stack</c> and <c>dbstack</c>:
    /// an anonymous body whose text holds <c>&gt;%</c> (open item 336, ADR 0378, measured: <c>@(x)
    /// error('id', 'x&gt;%d', 1)</c>, <c>@(x) [x(3) '&gt;%']</c> and <c>@() dbstack(numel('&gt;%') * 0)</c> have no
    /// frame, where <c>'%&gt;'</c>, <c>'x&gt;a%d'</c>, <c>'x&gt; %d'</c> and <c>['&gt;' '%']</c> have theirs). Its
    /// caller is reached as if the body were part of the call.
    /// </summary>
    internal static bool HiddenFrame(string name) =>
        name.StartsWith('@') && name.Contains(">%", StringComparison.Ordinal);

    /// <summary>
    /// The stack's name for a function that is no class method: <c>outer/inner</c> for a MATLAB nested
    /// function (R2025b, open item 336, measured), its own name otherwise.
    /// </summary>
    private static string FrameNameOf(FnStmt function) =>
        function.Dialect.IsMatlab ? function.StackName : function.Name;

    /// <summary>Whether <paramref name="name"/> is a property's set or get method's (<c>set.V</c>, <c>get.V</c>).</summary>
    private static bool IsAccessorName(string name) =>
        name.StartsWith("set.", StringComparison.Ordinal) || name.StartsWith("get.", StringComparison.Ordinal);

    /// <summary>
    /// <c>run('s.m')</c> of a MATLAB file holds R2025b's own <c>run.m</c> as a frame between the file and the
    /// code that ran it, at the line where <c>run.m</c> evaluates the script (<c>run@112(run.m)</c>,
    /// measured; open item 334, ADR 0368): two frames, <see cref="LeaveRunFrames"/> ends them. The file's
    /// path is where it sits under a MATLAB installation, read from this one's root, which holds no such file.
    /// </summary>
    /// <remarks>
    /// A script's frame is entered only for a script (<paramref name="script"/>): a function file run so is
    /// its main function's frame over <c>run</c>'s (measured: <c>p334fn@2 ; run@112 ; p334b@3</c>).
    /// </remarks>
    internal void EnterRunFrames(string path, bool script, int callLine)
    {
        _otherFrames.Add((null, RunFramePath, _activeFunctions.Count, callLine, RunFrameLine));
        if (script)
        {
            EnterScriptFrame(path, RunFrameLine);
        }
    }

    /// <summary>The frames <see cref="EnterRunFrames"/> entered have ended.</summary>
    internal void LeaveRunFrames(bool script)
    {
        LeaveOtherFrame();
        if (script)
        {
            LeaveOtherFrame();
        }
    }

    /// <summary>An error is leaving the run of a file: the file's frame and <c>run</c>'s, as <see cref="UnwindOtherFrame"/> records one.</summary>
    internal void UnwindRunFrames(JgsRuntimeException error, bool script)
    {
        UnwindOtherFrame(error);
        if (script)
        {
            (string name, string file, int line, int callLine) = OtherFrame(_otherFrames.Count - 2, error.PendingLine);
            error.PushFrame(name, file, callLine, line);
        }
    }

    /// <summary>The line of R2025b's <c>run.m</c> that evaluates the script it runs (measured).</summary>
    private const int RunFrameLine = 112;

    /// <summary><c>run.m</c>'s place under the installation's root (R2025b: <c>toolbox\matlab\lang\run.m</c>).</summary>
    private static string RunFramePath { get; } =
        Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "toolbox", "matlab", "lang", "run.m");

    /// <summary>The depth (count of running functions) the innermost other frame was entered at, or -1 when there is none left.</summary>
    private int OtherFrameDepth(int index) => index >= 0 ? _otherFrames[index].Depth : -1;

    // --- the scripts inmem lists ------------------------------------------------------------------

    /// <summary>The count <see cref="NextLoadOrder"/> hands out, shared by the path's files and the scripts run.</summary>
    private long _loadOrder;

    /// <summary>
    /// The scripts run as programs - the run's own file and each <c>run('s.m')</c> - in the order they
    /// were first run, by name and path, each with its place in the load order. R2025b's <c>inmem</c>
    /// lists them with the functions, in the order they were loaded, until a clear lets them go (open
    /// item 302, measured); a script named on the path is the path's own loaded file already.
    /// </summary>
    private readonly List<(string Name, string Path, long Order)> _scriptsRun = [];

    /// <summary>The next place in the load order <c>inmem</c> lists files by.</summary>
    internal long NextLoadOrder() => ++_loadOrder;

    /// <summary>The scripts run as programs that are loaded now, for <c>inmem</c>.</summary>
    internal IReadOnlyList<(string Name, string Path, long Order)> ScriptsRun => _scriptsRun;

    /// <summary>
    /// Notes a program <see cref="Run"/> starts, when it is a MATLAB file: the -batch file (no source id;
    /// its path is <see cref="MainScriptPath"/>) or a file <c>run</c> read.
    /// </summary>
    private void NoteScriptRun(string sourceId)
    {
        string path = sourceId.Length > 0 ? sourceId : MainScriptPath;
        if (!Dialect.IsMatlab || path.Length == 0 || !Path.IsPathRooted(path)
            || !(Path.GetExtension(path).Equals(".m", StringComparison.OrdinalIgnoreCase) || JgsPcode.IsPcode(path))
            || _scriptsRun.Exists(s => JgsFunctionPath.PathComparer.Equals(s.Path, path)))
        {
            return;
        }

        _scriptsRun.Add((Path.GetFileNameWithoutExtension(path), path, NextLoadOrder()));
    }

    /// <summary>
    /// <c>clear functions</c> and <c>clear all</c> (<paramref name="name"/> null) or <c>clear name</c>: the
    /// scripts run as programs let go, all or the one of that name, but never one still running (R2025b,
    /// measured: the -batch file stays listed after <c>clear functions</c> in its own body).
    /// </summary>
    internal void ForgetScriptsRun(string? name) =>
        _scriptsRun.RemoveAll(s => (name is null || string.Equals(s.Name, name, StringComparison.Ordinal))
            && !_runningScripts.Exists(running => JgsFunctionPath.PathComparer.Equals(running, s.Path)
                || (running.Length == 0 && JgsFunctionPath.PathComparer.Equals(s.Path, MainScriptPath))));
}
