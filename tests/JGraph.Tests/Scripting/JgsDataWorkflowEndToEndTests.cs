using System.Globalization;
using System.Text;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M18 end-to-end: the milestone's target workflow — a messy engineering log (junk preamble, serial
/// numbers, measurements) parsed, masked, index-mapped, summarized per device, and plotted — as one
/// JGS script.
/// </summary>
[Collection("JG facade")]
public class JgsDataWorkflowEndToEndTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public JgsDataWorkflowEndToEndTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "test_log.csv"), BuildMessyLog());
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// 6 junk preamble lines, then a 3-column table of 90 rows: ids cycle SN-A/SN-B/SN-C, temperature
    /// is 70 + (row % 30) (so &gt; 85 first at row 16, 42 rows total), voltage ramps 3.00, 3.01, ….
    /// </summary>
    private static string BuildMessyLog()
    {
        var sb = new StringBuilder();
        sb.AppendLine("JGraph environmental test rig");
        sb.AppendLine("Operator: J. Carpenter");
        sb.AppendLine("Started, 2026-07-17 09:00");
        sb.AppendLine("Firmware v2.1 (calibrated)");
        sb.AppendLine();
        sb.AppendLine("--- results follow ---");
        sb.AppendLine("SerialNumber,Temperature,Voltage");
        for (int i = 0; i < 90; i++)
        {
            string id = "SN-" + (char)('A' + (i % 3));
            double temp = 70 + (i % 30);
            double volt = 3.00 + (i * 0.01);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{id},{temp},{volt:F2}"));
        }

        return sb.ToString();
    }

    [Fact]
    public async Task MessyLogWorkflow_ParsesMasksMapsAndPlots()
    {
        const string script = """
            let t = readcsv("test_log.csv", 6)
            print("columns:", join(colnames(t), ", "), "rows:", rowcount(t))

            let ids  = textcolumn(t, "SerialNumber")
            let temp = column(t, "Temperature")
            let volt = column(t, "Voltage")

            let devices = unique(ids)
            let i = 0
            while i < length(devices) {
                let id = devices[i]
                let mask = ids == id
                let temps = temp(mask)
                print(sprintf("%s: n=%d mean=%.2f std=%.2f p95=%.1f",
                    id, sum(mask), mean(temps), std(temps), percentile(temps, 95)))
                i = i + 1
            }

            let hot = find(temp > 85)
            let hotIds  = ids(hot)
            let hotVolt = volt(hot)
            print("over-temp readings:", length(hot), "first id:", hotIds[0])

            let lateTemp = slice(temp, 75)
            let lateVolt = slice(volt, 75)
            let drift = lateTemp - mean(temp)

            figure()
            subplot(2, 1, 1)
            scatter(hotVolt, temp(hot))
            title("Voltage vs Temperature (>85 C)")
            subplot(2, 1, 2)
            plot(lateVolt, drift)
            grid(true)
            show()
            """;

        ScriptRunResult result = await _engine.RunAsync(
            script, new ScriptContext(_output, (_, figure) => _figures.Add(figure), _directory), default);

        Assert.True(result.Success, result.Message);
        string text = _output.NormalText;

        // The table parsed past the preamble.
        Assert.Contains("columns: SerialNumber, Temperature, Voltage rows: 90", text);

        // Per-device stats via unique + string-mask + gather: temps are 70+(row%30) so device A
        // (rows 0,3,…,87) sees 70+{0,3,…,27}, mean 83.5; B and C shift by one step each.
        Assert.Contains("SN-A: n=30 mean=83.50", text);
        Assert.Contains("SN-B: n=30 mean=84.50", text);
        Assert.Contains("SN-C: n=30 mean=85.50", text);

        // Index mapping back to the original rows: temp > 85 first at row 16 (SN-B), 14 per 30 rows.
        Assert.Contains("over-temp readings: 42 first id: SN-B", text);

        // The figure: two subplots, a scatter over the hot rows and a drift line.
        FigureModel figure = Assert.Single(_figures);
        Assert.Equal(2, figure.Axes.Count);
        Assert.Single(figure.Axes[0].Plots);
        Assert.Single(figure.Axes[1].Plots);
        Assert.Equal("Voltage vs Temperature (>85 C)", figure.Axes[0].Title);
    }

    [Fact]
    public async Task AlignedSubArrays_KeepRowCorrespondence()
    {
        // Row 3 of each sliced column maps to row 78 of the originals — the user's alignment ask.
        const string script = """
            let t = readcsv("test_log.csv", 6)
            let temp = column(t, "Temperature")
            let volt = column(t, "Voltage")
            let lateTemp = slice(temp, 75)
            let lateVolt = slice(volt, 75)
            print(lateTemp[3] == temp[78], lateVolt[3] == volt[78])
            print(length(lateTemp), length(lateVolt))
            """;

        ScriptRunResult result = await _engine.RunAsync(
            script, new ScriptContext(_output, (_, figure) => _figures.Add(figure), _directory), default);

        Assert.True(result.Success, result.Message);
        Assert.Contains("true true", _output.NormalText);
        Assert.Contains("15 15", _output.NormalText);
    }
}
