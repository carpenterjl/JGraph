using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.DataImport;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: table access builtins (colnames, rowcount, textcolumn) and skiprows on the readers.</summary>
[Collection("JG facade")]
public class JgsTableBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public JgsTableBuiltinTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
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
            // Best-effort cleanup; the OS temp folder is purged eventually anyway.
        }
    }

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), _directory), default);

    private string WriteCsv(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string MessyLog = """
        JGraph test rig 7
        Operator: J. Carpenter
        Date, 2026-07-17
        (calibration OK)

        SerialNumber,Temperature,Voltage
        SN-1,81.5,3.30
        SN-2,90.0,3.28
        SN-1,79.0,3.31
        """;

    [Fact]
    public async Task Readcsv_WithSkiprows_ParsesPastTheJunkPreamble()
    {
        WriteCsv("log.csv", MessyLog);

        ScriptRunResult result = await Run("""
            let t = readcsv("log.csv", 5)
            print(rowcount(t))
            print(join(colnames(t), "|"))
            print(column(t, "Temperature"))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("3", _output.NormalText);
        Assert.Contains("SerialNumber|Temperature|Voltage", _output.NormalText);
        Assert.Contains("[81.5, 90, 79]", _output.NormalText);
    }

    [Fact]
    public async Task Textcolumn_ReturnsStringValues_ForTheIdWorkflow()
    {
        WriteCsv("log.csv", MessyLog);

        ScriptRunResult result = await Run("""
            let t = readcsv("log.csv", 5)
            let ids = textcolumn(t, "SerialNumber")
            print(ids)
            print(unique(ids))
            print(sum(ids == "SN-1"))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[SN-1, SN-2, SN-1]", _output.NormalText);
        Assert.Contains("[SN-1, SN-2]", _output.NormalText);
        Assert.Contains("2", _output.NormalText);
    }

    [Fact]
    public async Task Textcolumn_UnknownColumn_ListsAvailableNames()
    {
        WriteCsv("simple.csv", "A,B\n1,2\n");

        ScriptRunResult result = await Run("let t = readcsv(\"simple.csv\")\nprint(textcolumn(t, \"Nope\"))");

        Assert.False(result.Success);
        Assert.Contains("Available: A, B", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Readxlsx_WithSkiprows_SkipsLeadingRows()
    {
        string path = Path.Combine(_directory, "book.xlsx");
        using (MemoryStream stream = new XlsxFixture()
            .Sheet("Sheet1",
                new[] { XCell.Text("junk header") },
                new[] { XCell.Text("more junk") },
                new[] { XCell.Text("Value"), XCell.Text("Name") },
                new[] { XCell.Number(1), XCell.Text("one") },
                new[] { XCell.Number(2), XCell.Text("two") })
            .BuildStream())
        using (FileStream file = File.Create(path))
        {
            stream.CopyTo(file);
        }

        ScriptRunResult result = await Run("""
            let t = readxlsx("book.xlsx", 2)
            print(rowcount(t))
            print(column(t, "Value"))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("2", _output.NormalText);
        Assert.Contains("[1, 2]", _output.NormalText);
    }

    [Fact]
    public async Task Readtable_WithSkiprows_RoutesByExtension()
    {
        WriteCsv("log.csv", MessyLog);

        ScriptRunResult result = await Run("let t = readtable(\"log.csv\", 5)\nprint(rowcount(t))");

        Assert.True(result.Success, result.Message);
        Assert.Contains("3", _output.NormalText);
    }

    [Fact]
    public async Task Readcsv_WithoutSkiprows_IsUnchanged()
    {
        WriteCsv("plain.csv", "X,Y\n1,10\n2,20\n");

        ScriptRunResult result = await Run("let t = readcsv(\"plain.csv\")\nprint(column(t, \"Y\"))");

        Assert.True(result.Success, result.Message);
        Assert.Contains("[10, 20]", _output.NormalText);
    }
}
