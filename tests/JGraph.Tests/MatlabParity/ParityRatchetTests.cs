using Xunit;

namespace JGraph.Tests.MatlabParity;

/// <summary>
/// The ratchet's states, each failure mode proven on the comparator alone: a pending line that
/// prints its baseline passes and any other output fails — a different wrong answer, a silent flip,
/// a missing line; a divergence passes only on its stamped output and fails when it moves or comes
/// to agree; an unstamped divergence fails; a run recorded as failing must fail with that message and
/// no other; a printed state is refused. The stamper is proven to write exactly these states and to
/// stamp nothing it cannot own. <c>tools/parity/test_compare.py</c> proves the Python twin the same way.
/// </summary>
public class ParityRatchetTests
{
    private const string Pending = "CHK|a|[1 2 3]|exact|pending V3|[7 2 3]\n";

    [Fact]
    public void PendingLinePassesOnItsBaselineOnly()
    {
        Assert.Empty(MatlabParityComparer.Compare(Pending, "CHK|a|[7 2 3]|exact\n"));

        string different = Assert.Single(MatlabParityComparer.Compare(Pending, "CHK|a|[9 2 3]|exact\n"));
        Assert.Contains("a different wrong answer is a regression", different);
        Assert.Contains("pending V3", different);

        string flipped = Assert.Single(MatlabParityComparer.Compare(Pending, "CHK|a|[1 2 3]|exact\n"));
        Assert.Contains("now agrees with MATLAB", flipped);
        Assert.Contains("owning stage's commit", flipped);

        string missing = Assert.Single(MatlabParityComparer.Compare(Pending, ""));
        Assert.StartsWith("a: recorded but not printed", missing);
    }

    [Fact]
    public void PendingLineWhoseBaselineAgreesIsRefused()
    {
        string problem = Assert.Single(MatlabParityComparer.Compare("CHK|a|5|exact|pending V3|5\n", "CHK|a|5|exact\n"));
        Assert.Contains("baseline (5) agrees with MATLAB", problem);
    }

    [Fact]
    public void PendingLineComparesItsBaselineAsText()
    {
        // The baseline is JGraph's exact output: a numerically equal spelling is still a change.
        Assert.Empty(MatlabParityComparer.Compare("CHK|a|1|exact|pending V2|1.0000000000000002\n", "CHK|a|1.0000000000000002|exact\n"));
        Assert.Single(MatlabParityComparer.Compare("CHK|a|1|exact|pending V2|2\n", "CHK|a|2.0|exact\n"));
        Assert.Empty(MatlabParityComparer.Compare("CHK|a|1|exact|pending V2|ERR no such road\n", "CHK|a|ERR no such road|exact\n"));
    }

    [Fact]
    public void PendingBitsLineComparesDigests()
    {
        string a = new string('a', 64);
        string b = new string('b', 64);
        Assert.Empty(MatlabParityComparer.Compare($"CHK|x|{a}|bits|pending V6|{b}\n", $"CHK|x|{b.ToUpperInvariant()}|bits\n"));
        Assert.Single(MatlabParityComparer.Compare($"CHK|x|{a}|bits|pending V6|{b}\n", $"CHK|x|{a}|bits\n"));
    }

    [Fact]
    public void DivergencePassesOnItsStampedOutputOnly()
    {
        const string stamped = "CHK|d|9.99|div=ADR0123|diverges|9.79\n";
        Assert.Empty(MatlabParityComparer.Compare(stamped, "CHK|d|9.79|div=ADR0123\n"));

        string moved = Assert.Single(MatlabParityComparer.Compare(stamped, "CHK|d|9.59|div=ADR0123\n"));
        Assert.Contains("diverges — printed '9.59', recorded output '9.79'", moved);

        string unstamped = Assert.Single(MatlabParityComparer.Compare("CHK|d|9.99|div=ADR0123\n", "CHK|d|9.79|div=ADR0123\n"));
        Assert.Contains("not stamped", unstamped);
    }

    [Fact]
    public void DivergenceThatAgreesIsRetiredEvenWhenStampedSo()
    {
        string problem = Assert.Single(MatlabParityComparer.Compare("CHK|d|9.99|div=ADR0123|diverges|9.99\n", "CHK|d|9.99|div=ADR0123\n"));
        Assert.Contains("retired", problem);
    }

    [Fact]
    public void StatesOnTheWrongRuleAreRefused()
    {
        Assert.Contains("only a div=ADRnnnn line diverges", Assert.Single(MatlabParityComparer.Compare("CHK|a|1|exact|diverges|2\n", "CHK|a|2|exact\n")));
        Assert.Contains("cannot be pending", Assert.Single(MatlabParityComparer.Compare("CHK|a|1|div=ADR0001|pending V3|2\n", "CHK|a|2|div=ADR0001\n")));
        Assert.Contains("unknown state", Assert.Single(MatlabParityComparer.Compare("CHK|a|1|exact|maybe V3|2\n", "CHK|a|2|exact\n")));
        Assert.Contains("printed a state", Assert.Single(MatlabParityComparer.Compare("CHK|a|1|exact\n", "CHK|a|1|exact|pending V3|1\n")));
    }

    [Fact]
    public void RunRecordedAsFailingMustFailWithThatMessage()
    {
        const string recording = "RUN|pending V6|Expected an expression, but found '.'.\nCHK|first|1|exact\nCHK|later|2|exact\n";

        // The run fails as recorded: the line it printed is checked, the one it never reached is excused.
        Assert.Empty(MatlabParityComparer.Compare(recording, "CHK|first|1|exact\n", "Expected an expression, but found '.'."));
        Assert.Contains("is not exactly", Assert.Single(MatlabParityComparer.Compare(recording, "CHK|first|9|exact\n", "Expected an expression, but found '.'.")));

        string other = Assert.Single(MatlabParityComparer.Compare(recording, "", "Something else"));
        Assert.Contains("a different failure is a regression", other);

        List<string> succeeded = MatlabParityComparer.Compare(recording, "CHK|first|1|exact\nCHK|later|2|exact\n", null);
        Assert.Contains("the run succeeded", Assert.Single(succeeded));
    }

    [Fact]
    public void AMalformedLineIsAProblemNotASkip()
    {
        // A value with a '|' in it was silently skipped by both sides before — recorded, never compared.
        const string recording = "CHK|a|x|y|z|w|exact\nCHK|b|1|exact\n";
        List<string> problems = MatlabParityComparer.Compare(recording, recording);
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Contains("malformed", p));
        Assert.Contains(problems, p => p.Contains("recorded line"));
        Assert.Contains(problems, p => p.Contains("printed line"));
        Assert.Empty(MatlabParityComparer.Malformed("CHK|a|1|exact\nCHK|b|1|exact|pending V3|2\nnot a chk line\n"));
    }

    [Fact]
    public void AnUnrecordedRunFailureFails()
    {
        List<string> problems = MatlabParityComparer.Compare("CHK|a|1|exact\n", "", "boom");
        Assert.Contains(problems, p => p.Contains("no RUN|pending line"));
        Assert.Contains(problems, p => p.StartsWith("a: recorded but not printed"));
    }

    [Fact]
    public void StamperWritesPendingWithAnOwnerAndReportsWithout()
    {
        var owners = new Dictionary<string, string> { ["a"] = "V3" };
        MatlabParityStamper.Result r = MatlabParityStamper.Stamp("CHK|a|1|exact\nCHK|b|2|exact\nCHK|c|3|exact\n", "CHK|a|7|exact\nCHK|b|8|exact\nCHK|c|3|exact\n", null, owners);
        Assert.Equal("CHK|a|1|exact|pending V3|7\nCHK|b|2|exact\nCHK|c|3|exact\n", r.Text);
        Assert.Equal(1, r.Stamped);
        string unowned = Assert.Single(r.Unstamped);
        Assert.StartsWith("b: printed '8'", unowned);
        Assert.Contains("no owner claims it", unowned);

        MatlabParityStamper.Result any = MatlabParityStamper.Stamp("CHK|b|2|exact\n", "CHK|b|8|exact\n", null, new Dictionary<string, string> { ["*"] = "V6" });
        Assert.Equal("CHK|b|2|exact|pending V6|8\n", any.Text);
    }

    [Fact]
    public void StamperStripsAFlipAndKeepsAnUnchangedBaseline()
    {
        var none = new Dictionary<string, string>();
        MatlabParityStamper.Result kept = MatlabParityStamper.Stamp(Pending, "CHK|a|[7 2 3]|exact\n", null, none);
        Assert.Equal(Pending, kept.Text);
        Assert.Equal(0, kept.Stamped);
        Assert.Empty(kept.Unstamped);

        MatlabParityStamper.Result flipped = MatlabParityStamper.Stamp(Pending, "CHK|a|[1 2 3]|exact\n", null, none);
        Assert.Equal("CHK|a|[1 2 3]|exact\n", flipped.Text);
        Assert.Equal(1, flipped.Stamped);

        // A changed baseline keeps its owner when no sidecar says otherwise; the commit names it.
        MatlabParityStamper.Result moved = MatlabParityStamper.Stamp(Pending, "CHK|a|[9 2 3]|exact\n", null, none);
        Assert.Equal("CHK|a|[1 2 3]|exact|pending V3|[9 2 3]\n", moved.Text);
        Assert.Equal(1, moved.Stamped);
    }

    [Fact]
    public void StamperStampsDivergencesAndReportsRetiredOnes()
    {
        var none = new Dictionary<string, string>();
        MatlabParityStamper.Result r = MatlabParityStamper.Stamp("CHK|d|9.99|div=ADR0123\nCHK|e|1|div=ADR0123\n", "CHK|d|9.79|div=ADR0123\nCHK|e|1|div=ADR0123\n", null, none);
        Assert.Equal("CHK|d|9.99|div=ADR0123|diverges|9.79\nCHK|e|1|div=ADR0123\n", r.Text);
        Assert.Equal(1, r.Stamped);
        Assert.Contains("retired", Assert.Single(r.Unstamped));
    }

    [Fact]
    public void StamperWritesAndRemovesTheRunLine()
    {
        var owners = new Dictionary<string, string> { ["RUN"] = "V6" };
        MatlabParityStamper.Result failing = MatlabParityStamper.Stamp("CHK|a|1|exact\nCHK|b|2|exact\n", "CHK|a|1|exact\n", "Expected an expression, but found '.'.", owners);
        Assert.Equal("RUN|pending V6|Expected an expression, but found '.'.\nCHK|a|1|exact\nCHK|b|2|exact\n", failing.Text);
        Assert.Equal(1, failing.Stamped);
        Assert.Empty(failing.Unstamped);

        MatlabParityStamper.Result unowned = MatlabParityStamper.Stamp("CHK|a|1|exact\n", "", "boom", new Dictionary<string, string>());
        Assert.Contains("no owner claims RUN", Assert.Single(unowned.Unstamped));

        MatlabParityStamper.Result recovered = MatlabParityStamper.Stamp(failing.Text, "CHK|a|1|exact\nCHK|b|2|exact\n", null, owners);
        Assert.Equal("CHK|a|1|exact\nCHK|b|2|exact\n", recovered.Text);
        Assert.Equal(1, recovered.Stamped);
    }

    [Fact]
    public void OwnersFileIsNameTabStage()
    {
        string path = Path.Combine(Path.GetTempPath(), "jg_owners_" + Guid.NewGuid().ToString("N") + ".owners");
        File.WriteAllText(path, "# comment\na\tV3\nRUN\tV6 # trailing\n*\tV10\n");
        try
        {
            Dictionary<string, string> owners = MatlabParityStamper.ReadOwners(path);
            Assert.Equal("V3", owners["a"]);
            Assert.Equal("V6", owners["RUN"]);
            Assert.Equal("V10", owners["*"]);
            File.WriteAllText(path, "a V3\n");
            Assert.Throws<FormatException>(() => MatlabParityStamper.ReadOwners(path));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Empty(MatlabParityStamper.ReadOwners(null));
    }
}
