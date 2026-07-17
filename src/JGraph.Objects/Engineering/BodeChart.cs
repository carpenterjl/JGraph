using JGraph.Core.Model;

namespace JGraph.Objects.Engineering;

/// <summary>
/// The two stacked axes produced by a Bode plot: a magnitude (dB) panel over a phase (degrees) panel,
/// both against a shared logarithmic frequency axis. Returned so callers can restyle either panel.
/// </summary>
public readonly struct BodeChart
{
    public BodeChart(AxesModel magnitude, AxesModel phase)
    {
        Magnitude = magnitude;
        Phase = phase;
    }

    /// <summary>The upper panel: magnitude in decibels versus frequency (log X).</summary>
    public AxesModel Magnitude { get; }

    /// <summary>The lower panel: phase in degrees versus frequency (log X).</summary>
    public AxesModel Phase { get; }
}
