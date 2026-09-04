using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.MatlabParity;

/// <summary>
/// M124: one theory case per fixture under <c>MatlabParity/fixtures</c>. Each fixture is run in the
/// MATLAB dialect and its <c>CHK</c> lines are compared, by the rule each line carries, against the
/// recording MATLAB R2025b made of the same script (<c>MatlabParity/expected</c>, written by
/// <c>tools/parity/record-matlab.ps1</c>). MATLAB is never run here.
/// </summary>
/// <remarks>
/// A fixture with no recording fails rather than passing vacuously; a <c>div=</c> line whose two
/// values agree fails, because that is a divergence retired without anyone noticing. The fixture and
/// expected files are copied to the output folder by the test project file.
/// </remarks>
[Collection("JG facade")]
public class MatlabParityFixtureTests : IDisposable
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "MatlabParity");

    public MatlabParityFixtureTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    public static IEnumerable<object[]> Fixtures()
    {
        string folder = Path.Combine(Root, "fixtures");
        if (!Directory.Exists(folder))
        {
            yield return new object[] { "(no fixtures folder was copied to the test output)" };
            yield break;
        }

        foreach (string path in Directory.GetFiles(folder, "*.m").OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return new object[] { Path.GetFileNameWithoutExtension(path) };
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureAgreesWithMatlabByItsRules(string fixture)
    {
        string script = Path.Combine(Root, "fixtures", fixture + ".m");
        string recording = Path.Combine(Root, "expected", fixture + ".txt");
        Assert.True(File.Exists(script), $"{fixture}: fixture not found at {script}");
        Assert.True(
            File.Exists(recording),
            $"{fixture}: not recorded — run tools/parity/record-matlab.ps1 -Fixtures {fixture}");

        string expected = File.ReadAllText(recording);
        Assert.Contains("CHK|", expected);

        string actual = RunMatlabDialect(File.ReadAllText(script));
        List<string> problems = MatlabParityComparer.Compare(expected, actual);
        Assert.True(
            problems.Count == 0,
            $"{fixture}: {problems.Count} line(s) disagree with MATLAB\n  - " + string.Join("\n  - ", problems));
    }

    [Fact]
    public void RecordingNamesTheMatlabItCameFrom()
    {
        string path = Path.Combine(Root, "expected", "matlab_version.txt");
        Assert.True(File.Exists(path), "expected/matlab_version.txt is missing — run record-matlab.ps1");
        Assert.Contains("R2025b", File.ReadAllText(path));
    }

    // The comparator itself, so a wrong line cannot pass by accident.

    [Fact]
    public void ComparerPassesAgreeingLines()
    {
        const string expected = "CHK|a|1.5|exact\nCHK|b|[2 3]|shape\nCHK|c|100|rel=1e-3\nCHK|d|0.5|abs=1e-6\nCHK|e|9.99|div=ADR0001\n";
        const string actual = "CHK|a|1.5|exact\nCHK|b|[2  3]|shape\nCHK|c|100.05|rel=1e-3\nCHK|d|0.5000005|abs=1e-6\nCHK|e|9.79|div=ADR0001\n";
        Assert.Empty(MatlabParityComparer.Compare(expected, actual));
    }

    [Fact]
    public void ComparerFailsAWrongValue()
    {
        List<string> problems = MatlabParityComparer.Compare("CHK|a|1.5|rel=1e-12\n", "CHK|a|1.5000001|rel=1e-12\n");
        string problem = Assert.Single(problems);
        Assert.StartsWith("a: 1.5000001 is", problem);
    }

    [Fact]
    public void ComparerFailsARetiredDivergence()
    {
        List<string> problems = MatlabParityComparer.Compare("CHK|a|9.99|div=ADR0123\n", "CHK|a|9.99|div=ADR0123\n");
        string problem = Assert.Single(problems);
        Assert.Contains("ADR0123 is retired", problem);
    }

    [Fact]
    public void ComparerFailsMissingAndUnrecordedLines()
    {
        List<string> problems = MatlabParityComparer.Compare("CHK|a|1|exact\n", "CHK|b|1|exact\n");
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("a: recorded but not printed"));
        Assert.Contains(problems, p => p.StartsWith("b: printed but not recorded"));
    }

    [Fact]
    public void ComparerFailsARuleThatChanged()
    {
        List<string> problems = MatlabParityComparer.Compare("CHK|a|1|exact\n", "CHK|a|1|rel=1e-9\n");
        string problem = Assert.Single(problems);
        Assert.Contains("rule is rel=1e-9 here and exact in the recording", problem);
    }

    [Fact]
    public void ComparerReadsMatlabsInfAndNan()
    {
        Assert.Empty(MatlabParityComparer.Compare("CHK|a|Inf|exact\nCHK|b|NaN|rel=1e-9\nCHK|c|-Inf|abs=1\n",
                                                  "CHK|a|Inf|exact\nCHK|b|NaN|rel=1e-9\nCHK|c|-Inf|abs=1\n"));
        Assert.Single(MatlabParityComparer.Compare("CHK|a|Inf|exact\n", "CHK|a|1e308|exact\n"));
    }

    private static string RunMatlabDialect(string code)
    {
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
