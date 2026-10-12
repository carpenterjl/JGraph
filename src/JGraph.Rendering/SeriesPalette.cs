using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Rendering;

/// <summary>
/// The one place a plot's series color is resolved. A plot that took a seat in its axes' cycle
/// (<see cref="PlotObject.SeriesIndex"/>) is colored by that seat, whatever has been added or
/// deleted around it since — which is what lets <c>colororder</c> retint a live figure and keeps a
/// survivor's color when a neighbor dies. A plot that never took a seat (built through the raw API)
/// is colored by its position in draw order, exactly as before seats existed.
/// </summary>
internal static class SeriesPalette
{
    /// <summary>The palette in force: the axes' own order when set, else the theme's.</summary>
    internal static IReadOnlyList<Color> Of(AxesModel axes, ITheme theme) =>
        axes.ColorOrder is { Count: > 0 } chosen ? chosen : theme.SeriesPalette;

    /// <summary>The color for one plot, given its position in the current draw-order walk.</summary>
    internal static Color Resolve(IReadOnlyList<Color> palette, PlotObject plot, int positional)
    {
        if (palette.Count == 0)
        {
            return Colors.Black;
        }

        // A seat on a yyaxis side cycles that side's own colour order (item 111), or takes the side's
        // entry of the axes' order when it has none (item 106).
        if (plot.SeriesSide >= 0 && plot.Axes?.SideColorOrder(plot.SeriesSide) is { } own)
        {
            return own[System.Math.Max(0, plot.SeriesIndex) % own.Count];
        }

        int index = plot.SeriesSide >= 0 ? plot.SeriesSide : plot.SeriesIndex >= 0 ? plot.SeriesIndex : positional;
        return palette[index % palette.Count];
    }
}
