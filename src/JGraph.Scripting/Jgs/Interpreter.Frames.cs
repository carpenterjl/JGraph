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
    /// and the line of the code that entered it.
    /// </summary>
    private readonly List<(AnonymousFunction? Anonymous, string Script, int Depth, int CallLine)> _otherFrames = [];

    /// <summary>An anonymous function's body is running, entered from <paramref name="callLine"/>; <see cref="LeaveOtherFrame"/> ends it.</summary>
    internal void EnterAnonymousFrame(AnonymousFunction anonymous, int callLine) =>
        _otherFrames.Add((anonymous, string.Empty, _activeFunctions.Count, callLine));

    /// <summary>A script named by its file <paramref name="path"/> is running, entered from <paramref name="callLine"/>.</summary>
    internal void EnterScriptFrame(string path, int callLine) =>
        _otherFrames.Add((null, path, _activeFunctions.Count, callLine));

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
        (AnonymousFunction? anonymous, string script, _, int callLine) = _otherFrames[index];
        if (anonymous is not null)
        {
            string text = anonymous.Declaration.MatlabText ?? anonymous.Text;
            return (text, FileOfFrame(anonymous.File), anonymous.Declaration.InText ? 0 : line, callLine);
        }

        return (Path.GetFileNameWithoutExtension(script), script, line, callLine);
    }

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
