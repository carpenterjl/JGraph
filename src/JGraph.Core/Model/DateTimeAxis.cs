namespace JGraph.Core.Model;

/// <summary>
/// The value convention for a <see cref="AxisScaleType.DateTime"/> axis: a date/time is stored on the
/// axis as an OLE automation date (a <see cref="double"/> number of days since 1899-12-30, with the
/// fractional part being the time of day). This maps date/time linearly onto the axis while staying a
/// plain <see cref="double"/>, so the whole transform/decimation pipeline is unchanged; only tick
/// generation and label formatting are date-aware. Double precision over this range resolves to well
/// under a microsecond.
/// </summary>
public static class DateTimeAxis
{
    /// <summary>The smallest axis value that converts back to a <see cref="DateTime"/> (~year 0100).</summary>
    public const double MinValue = -657434.0;

    /// <summary>The largest axis value that converts back to a <see cref="DateTime"/> (~year 9999).</summary>
    public const double MaxValue = 2958465.0;

    /// <summary>Converts a <see cref="DateTime"/> to its axis value.</summary>
    public static double ToValue(DateTime dateTime) => dateTime.ToOADate();

    /// <summary>Converts an axis value back to a <see cref="DateTime"/> (clamped to the representable range).</summary>
    public static DateTime FromValue(double value) =>
        DateTime.FromOADate(System.Math.Clamp(value, MinValue, MaxValue));

    /// <summary>Converts a sequence of <see cref="DateTime"/> values to axis values.</summary>
    public static double[] ToValues(IReadOnlyList<DateTime> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        var values = new double[times.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = times[i].ToOADate();
        }

        return values;
    }
}
