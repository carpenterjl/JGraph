using JGraph.Core.Model;

namespace JGraph.Controls;

/// <summary>Which series' legend row was clicked, and on which axes.</summary>
public sealed class LegendRowClickedEventArgs : EventArgs
{
    public LegendRowClickedEventArgs(AxesModel axes, PlotObject plot)
    {
        Axes = axes;
        Plot = plot;
    }

    public AxesModel Axes { get; }

    public PlotObject Plot { get; }
}
