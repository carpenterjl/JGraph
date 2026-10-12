using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Rendering;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's <c>legend</c>, for MATLAB-dialect code (open item 109, probes <c>probe_109b</c> and
/// <c>probe_109c</c>). JGS keeps its own verb, which reads every word as a name.
/// <list type="bullet">
/// <item><c>off</c> deletes the legend, <c>hide</c> and <c>show</c> keep it and set whether it is
/// drawn, <c>toggle</c> flips that, <c>boxoff</c> and <c>boxon</c> its box; each but <c>off</c> and
/// <c>hide</c> makes a legend where there is none.</item>
/// <item>Otherwise a legend is made, or made again: of the series a leading handle array names, of
/// as many as there are labels, or of all, then the labels name them in turn and any trailing
/// name-value pairs are the legend's properties. A series it leaves unnamed is <c>data1</c>,
/// <c>data2</c>, … (<see cref="LegendModel.NameUnnamedRows"/>); making it again takes the old
/// legend's names back first, as R2025b's replacement does.</item>
/// </list>
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly HashSet<string> LegendCommands =
        new(StringComparer.OrdinalIgnoreCase) { "off", "show", "hide", "toggle", "boxoff", "boxon" };

    private static JgsValue MatlabLegend(IReadOnlyList<JgsValue> args, JGraphScriptGlobals host, int line, int col)
    {
        AxesModel axes = JG.Gca();
        LegendModel legend = axes.Legend;

        if (args.Count == 1 && IsTextScalar(args[0]) && LegendCommands.Contains(StrOf("legend", args[0], line, col)))
        {
            string command = StrOf("legend", args[0], line, col).ToLowerInvariant();
            switch (command)
            {
                case "off":
                    DeleteLegend(legend);
                    return JgsValue.Null;
                case "hide" when !legend.Visible:
                    return JgsValue.Null;
                case "hide":
                    legend.Hidden = true;
                    return JgsHandleRegistry.For(legend);
            }

            if (!legend.Visible)
            {
                MakeLegend(axes, null, [], host, line, col);
            }
            else if (command == "show")
            {
                legend.Hidden = false;
            }
            else if (command == "toggle")
            {
                legend.Hidden = !legend.Hidden;
            }

            if (command is "boxoff" or "boxon")
            {
                legend.ShowBorder = command == "boxon";
            }

            return JgsHandleRegistry.For(legend);
        }

        // legend again, with nothing to say, is the legend there is, shown (probe_109c).
        if (args.Count == 0 && legend.Visible)
        {
            legend.Hidden = false;
            RefreshLegend(legend);
            return JgsHandleRegistry.For(legend);
        }

        // A leading handle, or array of them, picks the series; labels follow, then the options.
        int at = 0;
        List<PlotObject>? subset = null;
        if (args.Count > 0 && !IsTextScalar(args[0]) && TextElementsOf(args[0]) is null && args[0].Type != JgsType.Cell)
        {
            subset = PlotsOf("legend", args[0], line, col);
            at = 1;
        }

        var labels = new List<string>();
        int options = args.Count;
        for (; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]) && (args.Count - at) % 2 == 0
                && JgsGraphicsProperties.TryFind(legend, StrOf("legend", args[at], line, col), out _))
            {
                options = at;
                break;
            }

            if (IsTextScalar(args[at]))
            {
                labels.Add(StrOf("legend", args[at], line, col));
            }
            else if (TextElementsOf(args[at]) is { } many)
            {
                labels.AddRange(many);
            }
            else if (args[at].Type == JgsType.Cell && args[at].AsCell.Length == 0)
            {
                // legend({}) names nothing.
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "legend's labels are text: character vectors, a string array, or a cell array of them.");
            }
        }

        MakeLegend(axes, subset, labels, host, line, col);
        JgsHandleEntry entry = JgsHandleRegistry.Require(JgsHandleRegistry.For(legend), line, col);
        for (int i = options; i + 1 < args.Count; i += 2)
        {
            JgsGraphicsProperties.Set(entry, StrOf("legend", args[i], line, col), args[i + 1], line, col);
        }

        return JgsHandleRegistry.For(legend);
    }

    /// <summary>Makes <paramref name="axes"/>' legend, or makes it again, of the series asked for.</summary>
    private static void MakeLegend(
        AxesModel axes, List<PlotObject>? subset, List<string> labels, JGraphScriptGlobals host, int line, int col)
    {
        LegendModel legend = axes.Legend;
        if (legend.Visible)
        {
            legend.ClearUnnamedRows();
        }
        else if (legend.BeingDeleted)
        {
            // A deleted legend's handle stays dead; the new one is a new object (R2025b).
            JgsHandleRegistry.Retire(legend);
        }

        legend.NamesUnnamed = true;
        legend.SyncEntries(LegendablePlots(axes), whenFixed: true);
        List<PlotObject> rows = [.. legend.Entries.Select(static e => e.Plot).OfType<PlotObject>()];
        List<PlotObject> chosen = subset ?? (labels.Count > 0 ? [.. rows.Take(labels.Count)] : rows);
        if (labels.Count > chosen.Count)
        {
            Warn(host, "MATLAB:legend:IgnoringExtraEntries", "Ignoring extra legend entries.");
        }

        for (int k = 0; k < chosen.Count && k < labels.Count; k++)
        {
            chosen[k].DisplayName = labels[k];
        }

        JG.Legend(axes, chosen);
        legend.NameUnnamedRows();
    }

    /// <summary>The series a legend can show: what has a legend key and was not left out of legends.</summary>
    internal static IReadOnlyList<PlotObject> LegendablePlots(AxesModel axes) =>
        [.. axes.Plots.Where(static p => p is ILegendItem && p.ShowsInLegend)];

    /// <summary>
    /// Brings a MATLAB legend's rows and the names it gives up to date, as R2025b's are whenever a
    /// script asks: the renderer does it each frame, and a run that draws nothing never would.
    /// </summary>
    internal static void RefreshLegend(LegendModel legend)
    {
        if (legend.Visible && legend.Parent is AxesModel owner)
        {
            legend.SyncEntries(LegendablePlots(owner));
            legend.NameUnnamedRows();
        }
    }

    /// <summary>
    /// Deletes a legend, for <c>delete(l)</c> and <c>legend off</c>: it is its axes' part for life, so
    /// it is hidden, announced as deleted, gives back the names it gave, and its handle dies.
    /// </summary>
    internal static void DeleteLegend(LegendModel legend)
    {
        if (!legend.Visible)
        {
            return;
        }

        GraphObjectLifecycle.NotifyDeleting(legend);
        legend.ClearUnnamedRows();
        legend.Visible = false;
        legend.Hidden = false;
        legend.NamesUnnamed = false;
        JgsHandleRegistry.Retire(legend);
    }
}
