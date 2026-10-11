using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Writes a linear ruler's tick labels over a power of ten, as R2025b does: limits of
/// <c>[0 50000]</c> are labelled 0 to 5 with ×10⁴ at the ruler's end (open item 97). The power is the
/// ruler's own when one was set (<c>Exponent</c>), and otherwise <see cref="Auto"/>'s.
/// </summary>
public sealed class ExponentTickGenerator : ITickGenerator
{
    private readonly ITickGenerator _inner;
    private readonly int? _exponent;

    public ExponentTickGenerator(ITickGenerator inner, int? exponent)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _exponent = exponent;
    }

    public AxisScaleType ScaleType => _inner.ScaleType;

    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
    {
        TickSet raw = _inner.Generate(range, targetCount, labelFormat);
        int exponent = _exponent ?? Auto(raw.MajorTicks.Select(static t => t.Value));
        if (exponent == 0 || raw.MajorTicks.Count == 0)
        {
            return raw;
        }

        double scale = System.Math.Pow(10, exponent);
        string[] labels = TickFormats.Labels(raw.MajorTicks.Select(t => t.Value / scale).ToArray(), labelFormat);
        var relabelled = new Tick[raw.MajorTicks.Count];
        for (int i = 0; i < relabelled.Length; i++)
        {
            relabelled[i] = new Tick(raw.MajorTicks[i].Value, labels[i]);
        }

        return new TickSet(relabelled, raw.MinorTicks, raw.Step) { Exponent = exponent };
    }

    /// <summary>
    /// The power of ten R2025b factors out of a linear ruler's labels, as a rule fitted to its answers
    /// at twenty-odd limits (probe_95c, probe_97, probe_97b) rather than its algorithm: the power of
    /// the ticks' mean size from 10⁴ up, that of the largest from 10⁻³ down, and none between.
    /// </summary>
    public static int Auto(IEnumerable<double> ticks)
    {
        double sum = 0;
        double largest = 0;
        int count = 0;
        foreach (double tick in ticks)
        {
            if (!double.IsFinite(tick))
            {
                continue;
            }

            double size = System.Math.Abs(tick);
            sum += size;
            largest = System.Math.Max(largest, size);
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        double mean = sum / count;
        if (mean > 0 && System.Math.Floor(System.Math.Log10(mean)) is var big && big >= 4)
        {
            return (int)big;
        }

        return largest > 0 && System.Math.Floor(System.Math.Log10(largest)) is var small && small <= -3 ? (int)small : 0;
    }
}
