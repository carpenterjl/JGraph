using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>
/// The seam a figure window uses to run a script's graphics callbacks. A live console session
/// registers an invoker here; a one-shot run or a batch never does, so a script that assigns a
/// callback still finishes, its callback simply never fires.
/// <para>
/// This mirrors the figure-close seam in the other direction: the app knows a click happened and
/// which series it landed on, and the scripting layer knows how to run script code.
/// </para>
/// </summary>
public static class ScriptGraphicsCallbacks
{
    private static Func<AxesModel, PlotObject, bool>? _legendItemHit;

    /// <summary>Installs the invoker for the live session; pass null when the session goes away.</summary>
    public static void SetLegendItemHitInvoker(Func<AxesModel, PlotObject, bool>? invoker) =>
        _legendItemHit = invoker;

    /// <summary>
    /// Runs the axes' legend <c>ItemHitFcn</c> for a clicked series. Returns false when there is no
    /// live session, or when that legend was never given a callback.
    /// </summary>
    public static bool InvokeLegendItemHit(AxesModel axes, PlotObject plot)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(plot);
        return _legendItemHit?.Invoke(axes, plot) ?? false;
    }
}
