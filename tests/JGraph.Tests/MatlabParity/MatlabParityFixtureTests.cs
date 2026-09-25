using System.Text;
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
/// <para>
/// A fixture with no recording fails rather than passing vacuously; a <c>div=</c> line whose two
/// values agree fails, because that is a divergence retired without anyone noticing. The fixture and
/// expected files are copied to the output folder by the test project file.
/// </para>
/// <para>
/// <b>The ratchet.</b> A recording's lines carry states (<see cref="MatlabParityComparer"/>): a line
/// JGraph is known to fail is <c>pending Vn</c> with its exact baseline, an accepted divergence is
/// <c>diverges</c> with its exact output, a run known to fail has a <c>RUN</c> line. States are
/// written by the stamp mode: with <c>JGRAPH_PARITY_STAMP</c> set to the expected folder to write,
/// each fixture's recording is re-stamped from what JGraph printed, owners taken from the fixture's
/// <c>.owners</c> sidecar, and the theory fails with a summary so a stamping run is never mistaken
/// for a green one. <c>tools/parity/check-ratchet.py</c> then holds that no line stays pending on a
/// stage whose ADR has landed.
/// </para>
/// <para>
/// <b>The boxed overlay.</b> A recording's states are the packed representation's answers. In the
/// boxed lane (<c>JGRAPH_JGS_PACKED=0</c>, <see cref="JgsPacking.Enabled"/> off) the same fixture is
/// held to the recording merged with <c>expected/&lt;fixture&gt;.boxed.txt</c>, which carries only the
/// lines whose state differs under boxed storage (<see cref="MatlabParityComparer.ApplyOverlay"/>).
/// The stamp mode run in that lane writes the overlay and leaves the recording alone; run in the
/// packed lane it writes the recording, as before. <c>check-ratchet.py</c> reads both.
/// </para>
/// <para>
/// A fixture runs the way the recorder runs it in MATLAB (<c>cd(fixtures); addpath(helpers); name</c>):
/// from its real path, so <c>mfilename</c> and the implicit folder are the fixture's own, with the
/// fixtures folder current and <c>fixtures\helpers\</c> on the function path. Only the top-level
/// <c>.m</c> files are fixtures; <c>helpers\</c> holds the class and function files fixtures share,
/// which have no recording of their own and are never run by name. Whether a fixture is a script or a
/// function file is the file's own first token, and both engines run it by that form — a fixture that
/// needs base-workspace semantics is written as a script.
/// </para>
/// <para>
/// <b>JGraph-only fixtures.</b> A <c>.jgs</c> fixture runs in the JGS dialect (V11, ADR 0172: the
/// dialect crossing, which R2025b cannot run). Its recording is written by hand from the rule and
/// carries no state; the MATLAB half of each of its cases is a <c>.m</c> fixture recorded from
/// R2025b over the same helpers, so what a helper means in MATLAB is measured, never assumed.
/// </para>
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

        foreach (string path in Directory.GetFiles(folder)
                     .Where(static p => Path.GetExtension(p) is ".m" or ".jgs")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return new object[] { Path.GetFileNameWithoutExtension(path) };
        }
    }

    /// <summary>The fixture's script: the <c>.m</c> file of that name, or the <c>.jgs</c> one of a JGraph-only fixture.</summary>
    private static string ScriptOf(string fixture)
    {
        string matlab = Path.Combine(Root, "fixtures", fixture + ".m");
        return File.Exists(matlab) ? matlab : Path.Combine(Root, "fixtures", fixture + ".jgs");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureAgreesWithMatlabByItsRules(string fixture)
    {
        string script = ScriptOf(fixture);
        string recording = Path.Combine(Root, "expected", fixture + ".txt");
        Assert.True(File.Exists(script), $"{fixture}: fixture not found at {script}");
        Assert.True(
            File.Exists(recording),
            $"{fixture}: not recorded — run tools/parity/record-matlab.ps1 -Fixtures {fixture}");

        string baseline = File.ReadAllText(recording);
        Assert.Contains("CHK|", baseline);

        // The boxed lane reads the recording through its overlay; the packed lane reads it as it is.
        string expected = baseline;
        string? overlay = JgsPacking.Enabled ? null : Path.Combine(Root, "expected", fixture + ".boxed.txt");
        if (overlay is not null && File.Exists(overlay))
        {
            (expected, List<string> overlayProblems) = MatlabParityComparer.ApplyOverlay(baseline, File.ReadAllText(overlay));
            Assert.True(
                overlayProblems.Count == 0,
                $"{fixture}: the boxed overlay does not fit its recording\n  - " + string.Join("\n  - ", overlayProblems));
        }

        (string printed, string? runFailure) = RunFixture(script);
        string actual = MatlabParityComparer.ResolveBits(printed);

        if (Environment.GetEnvironmentVariable("JGRAPH_PARITY_STAMP") is { Length: > 0 } stampFolder)
        {
            Stamp(fixture, baseline, expected, actual, runFailure, stampFolder, boxed: overlay is not null);
            return;
        }

        List<string> problems = MatlabParityComparer.Compare(expected, actual, runFailure);
        Assert.True(
            problems.Count == 0,
            $"{fixture}: {problems.Count} line(s) disagree with MATLAB\n  - " + string.Join("\n  - ", problems));
    }

    /// <summary>
    /// The stamp mode: rewrites the recording's states from this run and fails with what it did, so
    /// the run reads as a stamping run and never as a gate. A fixture with nothing to stamp and no
    /// problem passes; one with a line no owner claims fails naming it. In the boxed lane the merged
    /// recording is stamped and only the overlay is written — the lines whose stamped state differs
    /// from the recording's — or deleted when nothing differs any more.
    /// </summary>
    private static void Stamp(string fixture, string baseline, string expected, string actual, string? runFailure, string stampFolder, bool boxed)
    {
        string ownersPath = Path.Combine(Root, "fixtures", fixture + ".owners");
        MatlabParityStamper.Result result = MatlabParityStamper.Stamp(
            expected, actual, runFailure, MatlabParityStamper.ReadOwners(ownersPath));
        string target = Path.Combine(stampFolder, fixture + (boxed ? ".boxed.txt" : ".txt"));
        if (result.Stamped > 0)
        {
            if (!boxed)
            {
                File.WriteAllText(target, result.Text, new UTF8Encoding(false));
            }
            else if (MatlabParityStamper.Overlay(baseline, result.Text) is { Length: > 0 } overlayText)
            {
                File.WriteAllText(target, overlayText, new UTF8Encoding(false));
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        string summary = $"{fixture}: stamped {result.Stamped} line(s) into {target}";
        if (result.Unstamped.Count > 0)
        {
            summary += $"; {result.Unstamped.Count} could not be stamped\n  - " + string.Join("\n  - ", result.Unstamped);
        }

        Assert.True(result.Stamped == 0 && result.Unstamped.Count == 0, summary);
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
        const string expected = "CHK|a|1.5|exact\nCHK|b|[2 3]|shape\nCHK|c|100|rel=1e-3\nCHK|d|0.5|abs=1e-6\nCHK|e|9.99|div=ADR0001|diverges|9.79\n";
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
        List<string> problems = MatlabParityComparer.Compare("CHK|a|9.99|div=ADR0123|diverges|9.99\n", "CHK|a|9.99|div=ADR0123\n");
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

    [Fact]
    public void HelpersAreNotFixtures()
    {
        // The helpers folder exists and holds files, and none of them is enumerated as a fixture — a
        // helper enumerated as a fixture would fail for want of a recording it must never have.
        string helpers = Path.Combine(Root, "fixtures", "helpers");
        Assert.True(Directory.Exists(helpers), helpers);
        Assert.NotEmpty(Directory.GetFiles(helpers, "*.m"));
        IEnumerable<string> names = Fixtures().Select(row => (string)row[0]);
        Assert.DoesNotContain("HelperBox", names);
        Assert.DoesNotContain("helper_twice", names);
        Assert.Contains("p1_helpers", names);
    }

    /// <summary>
    /// Runs a fixture as the recorder runs it in MATLAB: by its real path (so <c>mfilename</c> and
    /// the implicit folder are its own), with the fixtures folder current and <c>helpers\</c> on the
    /// function path. A <c>.jgs</c> fixture runs the same way in the JGS dialect. The comparer's
    /// inline lines are the comparer's business; this is the fixture's.
    /// </summary>
    private static (string Printed, string? RunFailure) RunFixture(string script)
    {
        string fixtures = Path.GetDirectoryName(script)!;
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, fixtures) { ScriptPath = script };
        JgsDialect dialect = Path.GetExtension(script) == ".jgs" ? JgsDialect.Jgs : JgsDialect.Matlab;
        ScriptRunResult result = JgsRunner.Run(
            File.ReadAllText(script), context, default, sourceId: script, hook: null, dialect,
            searchFolders: [Path.Combine(fixtures, "helpers")]);

        // A run that fails is not an assertion failure here: the recording may say it fails
        // (RUN|pending), and the comparer holds it to exactly that.
        return (output.NormalText, result.Success ? null : result.Message ?? "the run failed with no message");
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
