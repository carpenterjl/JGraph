using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using JGraph.Api;
using JGraph.Application.Services;
using JGraph.Core.Model;
using JGraph.Scripting;

namespace JGraph.Application.Scripting;

/// <summary>
/// What fills the <see cref="ScriptAnimation"/> seam in the running application: it opens the figure
/// the animation belongs to, applies each step on the UI thread, and waits between them so the window
/// gets a chance to draw. Every animated verb already leaves its finished picture behind, so this adds
/// motion to a run that was correct without it.
/// </summary>
/// <remarks>
/// <para>
/// Two conditions end an animation early, and both are answers rather than failures. Closing the
/// figure window retires the figure, so its number stops resolving — a script animating into a window
/// the user has shut should stop, not keep stepping a model nothing can show. Pressing Stop cancels
/// the run, and the animation is the part of it most likely to be holding the script thread, so it
/// checks between steps rather than making the user wait out the motion.
/// </para>
/// <para>
/// A script runs on its own thread (see <c>ScriptThread</c>), which is what makes waiting here safe:
/// the UI thread is free to lay out and paint while the script stands still. Called <em>on</em> the UI
/// thread there would be nothing left to draw with, so this answers false instead — the same answer a
/// batch run gets, and the verb falls back to its finished picture.
/// </para>
/// </remarks>
internal sealed class FigureAnimationPlayer
{
    /// <summary>
    /// How long a single wait lasts before the stop conditions are looked at again. Short enough that
    /// Stop feels immediate even during a slow movie, long enough not to spin.
    /// </summary>
    private const int SliceMilliseconds = 25;

    /// <summary>The longest a single step may be asked to wait, so a mistyped frame rate cannot hang a run.</summary>
    private const int MaximumPaceMilliseconds = 10_000;

    private readonly Dispatcher _dispatcher;
    private readonly IFigureWindowService _windows;
    private readonly Func<bool> _stopRequested;

    public FigureAnimationPlayer(
        Dispatcher dispatcher, IFigureWindowService windows, Func<bool> stopRequested)
    {
        _dispatcher = dispatcher;
        _windows = windows;
        _stopRequested = stopRequested;
    }

    /// <summary>
    /// Plays the steps, answering whether every one of them was applied. The signature is the seam's:
    /// see <see cref="ScriptAnimation.SetPlayer"/>.
    /// </summary>
    public bool Play(FigureModel figure, IReadOnlyList<Action> steps, double secondsPerStep)
    {
        if (_dispatcher.CheckAccess() || steps.Count == 0)
        {
            return false;
        }

        int number = JG.GetFigureNumber(figure);
        if (number < 1)
        {
            return false;
        }

        // The figure is shown before the first step rather than at the end of the run, because an
        // animation nobody is watching is not an animation.
        _dispatcher.Invoke(() => _windows.ShowScriptFigure(number, figure));

        int pace = (int)Math.Clamp(
            Math.Round(secondsPerStep * 1000), 0, MaximumPaceMilliseconds);
        foreach (Action step in steps)
        {
            if (_stopRequested() || JG.GetFigureNumber(figure) != number)
            {
                return false;
            }

            _dispatcher.Invoke(step);

            // Waiting at Render priority returns only once the window has laid out and painted what
            // the step just changed. Without it a fast pace would queue up steps the eye never sees,
            // and the animation would arrive as a jump to its last frame.
            _dispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Rest(pace);
        }

        return true;
    }

    /// <summary>Waits <paramref name="milliseconds"/>, in slices, giving up as soon as the run is stopping.</summary>
    private void Rest(int milliseconds)
    {
        for (int waited = 0; waited < milliseconds; waited += SliceMilliseconds)
        {
            if (_stopRequested())
            {
                return;
            }

            Thread.Sleep(Math.Min(SliceMilliseconds, milliseconds - waited));
        }
    }
}
