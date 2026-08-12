using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Where a polar axes' spokes stand and what each one reads. The renderer draws through this and the
/// θ tick verbs answer through it, so <c>thetaticks</c> cannot report spokes the chart is not drawing —
/// the same one-place rule the Cartesian tick verbs get from <see cref="ManualTickGenerator"/>. The
/// θ ruler itself always holds degrees; a caller who speaks radians converts at its own boundary.
/// </summary>
public static class PolarSpokes
{
    /// <summary>
    /// The angles, in degrees, the spokes stand at. A θ ruler told where its ticks go says so, kept to
    /// the visible turn; left to itself it divides the turn every 30°, because a circle wants a factor
    /// of the turn and not the round decimal number a linear tick generator would reach for.
    /// </summary>
    public static IReadOnlyList<double> Degrees(AxisModel thetaAxis)
    {
        ArgumentNullException.ThrowIfNull(thetaAxis);
        DataRange turn = thetaAxis.Range;

        if (thetaAxis.TickPositions is { Count: > 0 } given)
        {
            var chosen = new List<double>(given.Count);
            foreach (double degrees in given)
            {
                if (degrees >= turn.Min - 1e-9 && degrees <= turn.Max + 1e-9)
                {
                    chosen.Add(degrees);
                }
            }

            return chosen;
        }

        var spokes = new List<double>(12);
        bool fullTurn = System.Math.Abs(turn.Max - turn.Min) >= 359.999;
        for (double degrees = System.Math.Ceiling(turn.Min / 30) * 30; degrees <= turn.Max + 1e-9; degrees += 30)
        {
            // On a full turn the last spoke lands on the first one, so it is dropped rather than drawn
            // twice — otherwise its label would be overprinted by the other end of the same circle.
            if (fullTurn && degrees >= turn.Min + 360 - 1e-9)
            {
                break;
            }

            spokes.Add(degrees);
        }

        return spokes;
    }

    /// <summary>
    /// What one spoke reads: an override if the ruler was given a list, a number written with the
    /// ruler's format if it was given one — the format owns the whole text, so nothing is added around
    /// it — and otherwise the angle in the given units, with degrees wearing their sign.
    /// </summary>
    public static string Label(AxisModel thetaAxis, AngleUnits units, double degrees, int index)
    {
        ArgumentNullException.ThrowIfNull(thetaAxis);

        IReadOnlyList<string>? overrides = thetaAxis.TickLabelOverrides;
        if (overrides is not null)
        {
            return overrides.Count == 0 ? string.Empty : overrides[index % overrides.Count];
        }

        double shown = units == AngleUnits.Radians ? degrees * System.Math.PI / 180 : degrees;
        if (thetaAxis.TickLabelFormat is { } format)
        {
            return shown.ToString(format, CultureInfo.CurrentCulture);
        }

        return units == AngleUnits.Radians
            ? shown.ToString("0.###", CultureInfo.CurrentCulture)
            : shown.ToString("0.###", CultureInfo.CurrentCulture) + "°";
    }
}
