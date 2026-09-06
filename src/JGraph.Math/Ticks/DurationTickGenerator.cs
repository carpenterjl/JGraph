using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
namespace JGraph.Maths.Ticks;

/// <summary>Duration labels for a ruler whose coordinates are elapsed days.</summary>
public sealed class DurationTickGenerator(string format) : ITickGenerator
{
    public AxisScaleType ScaleType => AxisScaleType.Linear;
    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
    {
        TickSet seconds = LinearTickGenerator.Instance.Generate(new DataRange(range.Min * 86400, range.Max * 86400), targetCount);
        return new TickSet(seconds.MajorTicks.Select(t => new Tick(t.Value / 86400, Format(t.Value, format))).ToArray(),
            seconds.MinorTicks.Select(t => t / 86400).ToArray(), seconds.Step / 86400);
    }
    public static string Format(double seconds, string format)
    {
        string sign = seconds < 0 ? "-" : "";
        seconds = System.Math.Abs(seconds);
        string N(double v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);
        if (format is "y" or "d" or "h" or "m" or "s")
            return sign + N(seconds / (format switch { "y" => 31557600, "d" => 86400, "h" => 3600, "m" => 60, _ => 1 }));
        int decimals = format.Contains('.') ? format.Length - format.IndexOf('.') - 1 : 0;
        seconds = System.Math.Round(seconds, decimals);
        string sec = N(seconds % 60, decimals == 0 ? "00" : "00." + new string('0', decimals));
        if (format.StartsWith("mm:ss", StringComparison.Ordinal)) return sign + N(System.Math.Floor(seconds / 60), "00") + ":" + sec;
        string hours = N(System.Math.Floor(seconds / 3600), "00");
        string minutes = N(System.Math.Floor(seconds / 60) % 60, "00");
        if (format == "hh:mm") return sign + hours + ":" + minutes;
        if (format.StartsWith("dd:hh:mm:ss", StringComparison.Ordinal))
            return sign + N(System.Math.Floor(seconds / 86400), "00") + ":" + N(System.Math.Floor(seconds / 3600) % 24, "00") + ":" + minutes + ":" + sec;
        return sign + hours + ":" + minutes + ":" + sec;
    }
}
