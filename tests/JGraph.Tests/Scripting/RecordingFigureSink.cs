using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A stand-in for the host's figure windows: it records every display, tracks which numbers are
/// "open", and can close one the way the real window service does — including the notification back
/// into <see cref="JG"/> that the window's Closed handler sends.
/// </summary>
internal sealed class RecordingFigureSink
{
    /// <summary>Every display, in order, with the number the figure was shown under.</summary>
    public List<(int Number, FigureModel Figure)> Shown { get; } = new();

    /// <summary>The numbers the host asked to close (script-driven <c>close</c>).</summary>
    public List<int> Closed { get; } = new();

    /// <summary>The figure numbers with a window currently open.</summary>
    public HashSet<int> Open { get; } = new();

    /// <summary>A context wired to this sink.</summary>
    public ScriptContext Context(IScriptOutput output) => new(
        output,
        showFigure: (number, figure) =>
        {
            Shown.Add((number, figure));
            Open.Add(number);
        },
        workingDirectory: null,
        resolvePath: null,
        figureFiles: null,
        audio: null,
        closeFigure: number =>
        {
            Closed.Add(number);
            Open.Remove(number);
        });

    /// <summary>
    /// The user clicking the window's X: the window goes away and the engine is told, exactly as
    /// <c>FigureWindowService</c>'s Closed handler does.
    /// </summary>
    public void SimulateUserClose(int number)
    {
        Open.Remove(number);
        JG.CloseFigure(number);
    }
}
