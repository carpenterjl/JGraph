using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>
/// The seam an animated verb runs through. A figure window installs a player that shows the figure
/// between steps; a one-shot run or a batch never does, and every animated verb here is written so
/// that not having one is an answer rather than a failure.
/// <para>
/// This is deliberately the smallest thing that serves <c>movie</c>, <c>comet</c> and
/// <c>comet3</c>: a list of steps and a pace. The verbs decide what a step is, so the seam does not
/// have to know anything about lines, frames, or what is being animated — which is what lets
/// <c>streamparticles</c> use it without the seam learning about streamlines.
/// </para>
/// </summary>
public static class ScriptAnimation
{
    private static Func<FigureModel, IReadOnlyList<Action>, double, bool>? _player;

    /// <summary>Installs the player for a live session; pass null when the session goes away.</summary>
    public static void SetPlayer(Func<FigureModel, IReadOnlyList<Action>, double, bool>? player) =>
        _player = player;

    /// <summary>Whether anything can show the steps of an animation as they happen.</summary>
    public static bool HasPlayer => _player is not null;

    /// <summary>
    /// Runs the steps with <paramref name="secondsPerStep"/> between them, answering false when
    /// there is no player — in which case the caller does whatever standing still means for it.
    /// </summary>
    public static bool Play(FigureModel figure, IReadOnlyList<Action> steps, double secondsPerStep)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(steps);
        return _player?.Invoke(figure, steps, secondsPerStep) ?? false;
    }
}
