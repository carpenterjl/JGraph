using System.Globalization;
using System.Text.RegularExpressions;
using JGraph.Core.Model;
using JGraph.Scripting.Jgs;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The automatic tick rule against R2025b's settled recordings (open items 49 and 64, ADR 0236):
/// 855 slider rows over 68 ranges and 17 track lengths (probe <c>u5_ticks2</c>), and the knob and
/// gauge sweeps (probes <c>u9_behave</c>, <c>u9_settle</c>) through each kind's effective track.
/// R2025b's view keeps some rounded-step walks of its own that no equal division reaches, so the
/// corpus never matches whole; these tests pin the match counts the rule reaches, so a change that
/// loses ground is seen.
/// </summary>
public class ScaleTickCorpusTests
{
    private readonly ITestOutputHelper _output;

    public ScaleTickCorpusTests(ITestOutputHelper output) => _output = output;

    private static string Probes
    {
        get
        {
            string? walk = AppContext.BaseDirectory;
            while (walk is not null && !Directory.Exists(Path.Combine(walk, "tools", "matlab-checklist", "ui-probes")))
            {
                walk = Path.GetDirectoryName(walk);
            }

            Assert.NotNull(walk);
            return Path.Combine(walk, "tools", "matlab-checklist", "ui-probes");
        }
    }

    private static readonly Regex SliderRow = new(
        @"^(V?R?TICKS) lim=\[([-0-9.e+ ]+)\] (?:w|h)=(\d+) : major=\[?([-0-9.e+ ]*)\]? labels=", RegexOptions.Compiled);

    private static List<(double Low, double High, double Track, double[] Ticks)> SliderCorpus()
    {
        var rows = new List<(double, double, double, double[])>();
        foreach (string line in File.ReadLines(Path.Combine(Probes, "u5", "u5_ticks2.out.txt")))
        {
            Match m = SliderRow.Match(line);
            if (!m.Success)
            {
                continue;
            }

            double[] limits = Numbers(m.Groups[2].Value);
            rows.Add((limits[0], limits[1], double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), Numbers(m.Groups[4].Value)));
        }

        return rows;
    }

    private static double[] Numbers(string text) =>
        [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(static n => double.Parse(n, CultureInfo.InvariantCulture))];

    private static bool Same(double[] got, double[] want, double span)
    {
        if (got.Length != want.Length)
        {
            return false;
        }

        for (int i = 0; i < got.Length; i++)
        {
            if (Math.Abs(got[i] - want[i]) > 1e-6 * Math.Max(1, Math.Abs(span)))
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public void TheSliderRuleHoldsItsGroundOverTheRecordedCorpus()
    {
        List<(double Low, double High, double Track, double[] Ticks)> corpus = SliderCorpus();
        Assert.True(corpus.Count > 800, $"only {corpus.Count} rows parsed");
        int hits = corpus.Count(row => Same(
            JgsGraphicsProperties.AutoMajorTicks(row.Low, row.High, Math.Max(1, row.Track), "Helvetica", 12, false, false),
            row.Ticks, row.High - row.Low));
        _output.WriteLine($"slider corpus: {hits} of {corpus.Count}");
        Assert.True(hits >= 625, $"the rule matched {hits} of {corpus.Count} recorded slider rows; it matched 631 when it was fitted");
    }

    private static readonly Regex ScaleRow = new(
        @"^(?:SIZE)?TICKS (\w+)(?: sz=(\d+))?(?: lim=)? ?\[([-0-9.e+ ]+)\] : major=\[?([-0-9.e+ ]*)\]?", RegexOptions.Compiled);

    [Fact]
    public void TheScalesFollowTheSameRuleThroughTheirEffectiveTracks()
    {
        var defaults = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["knob"] = 60, ["gauge"] = 120, ["linear"] = 120, ["ninety"] = 90, ["semi"] = 120,
        };
        var byKind = new Dictionary<string, (int Hits, int Rows)>(StringComparer.Ordinal);
        foreach (string file in new[] { Path.Combine("u9", "u9_behave.out.txt"), Path.Combine("u9", "u9_settle.out.txt") })
        {
            foreach (string line in File.ReadLines(Path.Combine(Probes, file)))
            {
                Match m = ScaleRow.Match(line);
                if (!m.Success || !defaults.TryGetValue(m.Groups[1].Value, out double fallback))
                {
                    continue;
                }

                string kind = m.Groups[1].Value;
                double size = m.Groups[2].Success ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : fallback;
                double[] limits = Numbers(m.Groups[3].Value);
                double[] want = Numbers(m.Groups[4].Value);
                UiScaleModel scale = kind switch
                {
                    "knob" => new UiKnobModel(),
                    "ninety" => new UiNinetyDegreeGaugeModel(),
                    "semi" => new UiSemicircularGaugeModel(),
                    "linear" => new UiLinearGaugeModel(),
                    _ => new UiGaugeModel(),
                };
                scale.Position = kind switch
                {
                    "semi" => new JGraph.Core.Primitives.Rect2D(0, 0, size, size * 65 / 120),
                    "linear" => new JGraph.Core.Primitives.Rect2D(0, 0, size, Math.Max(20, size * 40 / 120)),
                    _ => new JGraph.Core.Primitives.Rect2D(0, 0, size, size),
                };
                scale.Lower = limits[0];
                scale.Upper = limits[1];
                JgsGraphicsProperties.RefreshTicks(scale);
                bool hit = Same([.. scale.MajorTicks], want, limits[1] - limits[0]);
                (int hits, int rows) = byKind.GetValueOrDefault(kind);
                byKind[kind] = (hits + (hit ? 1 : 0), rows + 1);
            }
        }

        foreach ((string kind, (int hits, int rows)) in byKind.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"{kind}: {hits} of {rows}");
        }

        int total = byKind.Values.Sum(static v => v.Hits);
        int all = byKind.Values.Sum(static v => v.Rows);
        Assert.True(all > 250, $"only {all} rows parsed");
        Assert.True(total >= 228, $"the scales matched {total} of {all} recorded rows; they matched 236 when the tracks were fitted");
    }
}
