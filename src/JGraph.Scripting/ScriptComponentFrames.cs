using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>
/// The flush between a script's components and the window that shows them (app-building plan,
/// section D). A property write only marks its figure dirty; at a flush point the script thread
/// takes the figure's components as one <see cref="UiFrame"/> and hands it to the host, which applies
/// it on its own thread. There is one frame in flight per figure: a statement boundary that finds the
/// last one not yet applied leaves the figure dirty for later, so a loop of ten thousand
/// <c>set(h, 'String', ...)</c> costs ten thousand model writes and a handful of frames.
/// <para>
/// The flush points: every statement boundary (unforced), the end of a run or a pump run, and
/// <c>drawnow</c> (forced — sent whether or not one is in flight). With no host installed, as under a
/// headless batch, nothing is taken at all.
/// </para>
/// </summary>
public static class ScriptComponentFrames
{
    private static Action<UiFrame>? _sink;
    private static long _seenEpoch = -1;

    /// <summary>
    /// Installs (or, with null, removes) the host's receiver. It is called on the script thread and
    /// must only post the frame to its own thread, then call <see cref="Applied"/> once applied.
    /// </summary>
    public static void SetSink(Action<UiFrame>? sink)
    {
        _sink = sink;
        _seenEpoch = -1;
        FigureModel.FramesAreDelivered = sink is not null;
    }

    /// <summary>Whether a host is listening — what a flush point asks before doing any work.</summary>
    public static bool HasSink => _sink is not null;

    /// <summary>The host has applied <paramref name="frame"/>; its figure may be sent another.</summary>
    public static void Applied(UiFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Figure.ComponentFrameInFlight = false;
    }

    /// <summary>
    /// The cheap check a statement boundary makes: nothing to do unless some component changed since
    /// the last flush looked.
    /// </summary>
    internal static void FlushIfChanged()
    {
        if (_sink is not null && _held == 0 && FigureModel.ComponentEpoch != _seenEpoch)
        {
            Flush(force: false);
        }
    }

    [ThreadStatic]
    private static int _held;

    /// <summary>
    /// Sends nothing from this thread while the scope is held: the visual designer's interpreter
    /// builds an app's components in a figure of its own (designer plan DS2), and its statement
    /// boundaries must not take frames of the figures a script on another thread is writing.
    /// </summary>
    internal static IDisposable HoldOnThisThread()
    {
        _held++;
        return new Hold();
    }

    private sealed class Hold : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                _held--;
            }
        }
    }

    /// <summary>
    /// Readies <paramref name="figure"/>'s components for a frame and takes it, as a flush does for
    /// each figure it sends: a slider a grid has stretched works its ticks out again, and a
    /// container resized or shown for the first time is told. For a figure no flush reaches - the
    /// visual designer's (designer plan DS2). Call on the thread that writes the figure.
    /// </summary>
    internal static UiFrame TakeFrame(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        Jgs.JgsGraphicsProperties.SettleSliders(figure);
        UiFrame frame = figure.TakeComponentFrame();
        Jgs.JgsContainerResize.Settle(figure);
        return frame;
    }

    /// <summary>Sends a frame for every dirty figure. Script thread only.</summary>
    /// <param name="force">Send even when the figure's last frame is still in flight — the end of a
    /// run, and <c>drawnow</c>, whose frame must arrive whatever the timing.</param>
    internal static void Flush(bool force)
    {
        if (_sink is not { } sink || _held > 0)
        {
            return;
        }

        long epoch = FigureModel.ComponentEpoch;
        bool deferred = false;
        foreach (int number in JG.FigureNumbers)
        {
            if (!JG.TryGetFigure(number, out FigureModel figure) || !figure.ComponentsDirty)
            {
                continue;
            }

            if (!force && figure.ComponentFrameInFlight)
            {
                deferred = true;
                continue;
            }

            // A slider a grid has stretched works its ticks out again for the length it has now (U5).
            Jgs.JgsGraphicsProperties.SettleSliders(figure);
            UiFrame frame = figure.TakeComponentFrame();
            figure.ComponentFrameInFlight = true;
            sink(frame);

            // A container a script resized, or one shown for the first time, is told (U2).
            Jgs.JgsContainerResize.Settle(figure);
        }

        if (!deferred)
        {
            _seenEpoch = epoch;
        }
    }
}
