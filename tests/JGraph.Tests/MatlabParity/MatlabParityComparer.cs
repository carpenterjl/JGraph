using System.Globalization;
using System.Text.RegularExpressions;

namespace JGraph.Tests.MatlabParity;

/// <summary>One recorded or printed line: its value, its rule, and — in a recording only — its state.</summary>
/// <param name="Value">What was printed.</param>
/// <param name="Rule">How the two sides compare; <c>exact</c> when the line names none.</param>
/// <param name="State">Null for a line that must agree; <c>pending Vn</c> for a line JGraph is known
/// to fail until stage Vn; <c>diverges</c> for an accepted divergence (rule <c>div=ADRnnnn</c>).</param>
/// <param name="Baseline">JGraph's exact output on the binary the state was recorded against.</param>
public sealed record ParityLine(string Value, string Rule, string? State = null, string? Baseline = null);

/// <summary>
/// Compares a fixture's <c>CHK|name|value|rule</c> lines against the recording MATLAB made of the
/// same script. The rules are the ones <c>tools/parity/compare.py</c> carries — the two are the
/// same comparator in two hosts, and a change to one is a change to both.
/// </summary>
/// <remarks>
/// <para>
/// Rules: <c>exact</c> (the same number, or the same text), <c>shape</c> (the same text once
/// whitespace is normalised), <c>rel=tol</c>, <c>abs=tol</c>, <c>div=ADRnnnn</c> — a recorded
/// divergence whose values <b>must differ</b>, so that a divergence quietly closed is noticed and
/// retired from its ADR rather than left on the books — and <c>bits</c>, a whole array compared
/// to the bit.
/// </para>
/// <para>
/// <b>States</b> (the ratchet). A recorded line may carry two more fields:
/// <c>CHK|name|value|rule|pending Vn|baseline</c> says JGraph is known to print <c>baseline</c>
/// rather than MATLAB's value until stage Vn lands; the line passes only when JGraph prints that
/// exact baseline. A different wrong answer is a regression and fails; an answer that now agrees
/// with MATLAB fails too, until the owning stage's commit removes the marker — a flip is recorded,
/// never silent. <c>CHK|name|value|div=ADRnnnn|diverges|output</c> is an accepted divergence: it
/// passes only on that exact output, and fails if the output changes or comes to agree with MATLAB.
/// A <c>div=</c> line without its stamp fails: accepting any output on a divergent line is what
/// let regressions hide behind divergences. A recording may also open with
/// <c>RUN|pending Vn|message</c>: the whole run is known to fail with that message (a class file the
/// parser refuses, say); the run must fail with exactly it, and lines it never reached are excused.
/// Printed lines never carry a state; the stamp mode of <c>MatlabParityFixtureTests</c> writes them.
/// </para>
/// <para>
/// A <c>bits</c> line is printed by the fixture as <c>CHK|name|file:&lt;absolute path&gt;|bits</c>,
/// naming a file its own <c>writebits</c> helper wrote: one header line <c>class rows cols …</c>,
/// then one <c>num2hex</c> row per element in column-major order, a real plane then an imaginary
/// one for complex. Whoever captures the output — the recorder, this harness, <c>compare.py</c> —
/// calls <see cref="ResolveBits"/> first: it replaces the path with the SHA-256 of the file's
/// bytes and deletes the file and its <c>tempname</c> folder, so a recording carries a digest and
/// never a path. Nothing computable in exact doubles on both engines is a digest worth trusting,
/// which is why the digest is taken on the host.
/// </para>
/// </remarks>
public static class MatlabParityComparer
{
    private static readonly Regex Line = new(@"^CHK\|([^|]+)\|([^|]*)\|([^|]*)(?:\|([^|]*)\|([^|]*))?$", RegexOptions.Compiled);

    private static readonly Regex RunLine = new(@"^RUN\|pending ([A-Z]+\d+)\|(.*)$", RegexOptions.Compiled);

    private static readonly Regex Stage = new(@"^pending ([A-Z]+\d+)$", RegexOptions.Compiled);

    private static readonly Regex BitsLine = new(
        @"^(CHK\|[^|\r\n]+\|)file:([^|\r\n]*)(\|bits)(?=[ \t]*\r?$)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Replaces every <c>file:&lt;path&gt;</c> value of a <c>bits</c> line with the SHA-256 of that
    /// file, deleting the file and, once empty, the folder it sits in. A path that is not absolute,
    /// not under the temp folder, or not there at all becomes <c>missing:&lt;path&gt;</c>, which no
    /// recording can match — never a fallback, never a substitute.
    /// </summary>
    public static string ResolveBits(string text) => BitsLine.Replace(text, m => m.Groups[1].Value + Digest(m.Groups[2].Value.Trim()) + m.Groups[3].Value);

    private static string Digest(string path)
    {
        if (path.Length == 0 || !Path.IsPathRooted(path) || !File.Exists(path))
        {
            return "missing:" + path;
        }

        string full = Path.GetFullPath(path);
        string temp = Path.GetFullPath(Path.GetTempPath());
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            return "missing:" + path;
        }

        string digest;
        using (FileStream stream = File.OpenRead(full))
        {
            digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        }

        File.Delete(full);
        string? folder = Path.GetDirectoryName(full);
        if (folder is not null
            && !string.Equals(Path.GetFullPath(folder + Path.DirectorySeparatorChar), temp, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(folder)
            && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }

        return digest;
    }

    /// <summary>The <c>name -> line</c> pairs a log holds; a missing rule reads as <c>exact</c>.</summary>
    public static Dictionary<string, ParityLine> Parse(string text)
    {
        var lines = new Dictionary<string, ParityLine>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            Match m = Line.Match(raw.Trim());
            if (m.Success)
            {
                string rule = m.Groups[3].Value.Length == 0 ? "exact" : m.Groups[3].Value;
                lines[m.Groups[1].Value] = m.Groups[4].Success
                    ? new ParityLine(m.Groups[2].Value, rule, m.Groups[4].Value, m.Groups[5].Value)
                    : new ParityLine(m.Groups[2].Value, rule);
            }
        }

        return lines;
    }

    /// <summary>
    /// Every line that opens as a <c>CHK|</c> line and does not parse — a value with a <c>|</c> in it,
    /// a field too many. Such a line used to be skipped by both sides, which is a line recorded and
    /// never compared; it is a problem now.
    /// </summary>
    public static List<string> Malformed(string text)
    {
        var bad = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("CHK|", StringComparison.Ordinal) && !Line.IsMatch(line))
            {
                bad.Add(line);
            }
        }

        return bad;
    }

    /// <summary>The recording's <c>RUN|pending Vn|message</c> line, or null when the run must succeed.</summary>
    public static (string Stage, string Message)? ParseRun(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            Match m = RunLine.Match(raw.TrimEnd('\r'));
            if (m.Success)
            {
                return (m.Groups[1].Value, m.Groups[2].Value.Trim());
            }
        }

        return null;
    }

    /// <summary>
    /// Every line that fails its rule and state, in the recording's order; empty when all agree.
    /// <paramref name="runFailure"/> is the message the run failed with, or null when it succeeded.
    /// </summary>
    public static List<string> Compare(string expectedText, string actualText, string? runFailure = null)
    {
        var expected = Parse(expectedText);
        var actual = Parse(actualText);
        (string Stage, string Message)? run = ParseRun(expectedText);
        var problems = new List<string>();

        foreach ((string side, string text) in new[] { ("recorded", expectedText), ("printed", actualText) })
        {
            foreach (string line in Malformed(text))
            {
                problems.Add($"malformed {side} line '{line}' — a value must not contain '|'");
            }
        }

        if (run is null && runFailure is not null)
        {
            problems.Add($"the run failed ({runFailure}) and the recording has no RUN|pending line");
        }
        else if (run is { } r && runFailure is null)
        {
            problems.Add($"RUN: recorded as failing until {r.Stage} but the run succeeded — remove the RUN line in the owning stage's commit and record the flip");
        }
        else if (run is { } rr && runFailure is not null && rr.Message != runFailure.Trim())
        {
            problems.Add($"RUN: the run failed with '{runFailure}', recorded '{rr.Message}' (pending {rr.Stage}) — a different failure is a regression");
        }

        foreach ((string name, ParityLine line) in expected)
        {
            if (!actual.TryGetValue(name, out ParityLine? got))
            {
                if (run is null)
                {
                    problems.Add($"{name}: recorded but not printed");
                }

                continue; // a run recorded as failing never reaches every line
            }

            if (got.State is not null)
            {
                problems.Add($"{name}: printed a state ({got.State}) — only a recording carries one");
                continue;
            }

            if (got.Rule != line.Rule)
            {
                problems.Add($"{name}: rule is {got.Rule} here and {line.Rule} in the recording");
                continue;
            }

            string? problem = CheckState(name, line, got.Value);
            if (problem is not null)
            {
                problems.Add(problem);
            }
        }

        foreach (string name in actual.Keys)
        {
            if (!expected.ContainsKey(name))
            {
                problems.Add($"{name}: printed but not recorded — re-run tools/parity/record-matlab.ps1");
            }
        }

        return problems;
    }

    /// <summary>A line's verdict under its state: the rule alone, a pending baseline, or an accepted divergence.</summary>
    private static string? CheckState(string name, ParityLine line, string printed)
    {
        bool divergent = line.Rule.StartsWith("div=", StringComparison.Ordinal);

        if (line.State is null)
        {
            return divergent
                ? $"{name}: divergence {line.Rule[4..]} is not stamped with JGraph's output — stamp it (JGRAPH_PARITY_STAMP) so any other output fails"
                : Check(name, line.Value, printed, line.Rule);
        }

        if (line.State == "diverges")
        {
            if (!divergent)
            {
                return $"{name}: state 'diverges' on a {line.Rule} line — only a div=ADRnnnn line diverges";
            }

            if (!SameText(printed, line.Baseline!, line.Rule))
            {
                return $"{name}: diverges — printed '{printed}', recorded output '{line.Baseline}'";
            }

            return Differs(line.Value, printed)
                ? null
                : $"{name}: agrees with MATLAB ({printed}) — divergence {line.Rule[4..]} is retired; delete the line and its ADR entry";
        }

        Match stage = Stage.Match(line.State);
        if (!stage.Success)
        {
            return $"{name}: unknown state '{line.State}'";
        }

        if (divergent)
        {
            return $"{name}: a div= line cannot be pending — it diverges or it is retired";
        }

        string owner = stage.Groups[1].Value;
        if (!SameText(printed, line.Baseline!, line.Rule))
        {
            return Check(name, line.Value, printed, line.Rule) is null
                ? $"{name}: now agrees with MATLAB ({printed}) — the pending {owner} marker comes off in the owning stage's commit, which records the flip"
                : $"{name}: pending {owner} printed '{printed}', baseline '{line.Baseline}' — a different wrong answer is a regression";
        }

        return Check(name, line.Value, line.Baseline!, line.Rule) is null
            ? $"{name}: pending {owner} but its baseline ({line.Baseline}) agrees with MATLAB — remove the marker"
            : null;
    }

    private static bool SameText(string printed, string baseline, string rule) =>
        rule == "bits"
            ? string.Equals(printed.Trim(), baseline.Trim(), StringComparison.OrdinalIgnoreCase)
            : printed.Trim() == baseline.Trim();

    private static bool Differs(string expected, string actual) =>
        Number(expected) is double dv && Number(actual) is double da
            ? !SameNumber(dv, da)
            : expected.Trim() != actual.Trim();

    private static string? Check(string name, string expected, string actual, string rule)
    {
        double? e = Number(expected);
        double? a = Number(actual);

        if (rule == "exact")
        {
            if (e is double ev && a is double av)
            {
                return SameNumber(ev, av) ? null : $"{name}: {actual} is not exactly {expected}";
            }

            return expected.Trim() == actual.Trim() ? null : $"{name}: '{actual}' is not '{expected}'";
        }

        if (rule == "shape")
        {
            static string Norm(string s) => Regex.Replace(s.Trim(), @"\s+", " ");
            return Norm(expected) == Norm(actual) ? null : $"{name}: shape {actual} is not {expected}";
        }

        if (rule == "bits")
        {
            foreach ((string side, string value) in new[] { ("recorded", expected.Trim()), ("printed", actual.Trim()) })
            {
                if (value.StartsWith("missing:", StringComparison.Ordinal))
                {
                    return $"{name}: bits file missing ({side} {value[8..]})";
                }

                if (value.StartsWith("file:", StringComparison.Ordinal))
                {
                    return $"{name}: bits file not resolved ({side} {value}) — ResolveBits must run before Compare";
                }

                if (value.Length != 64 || !value.All(Uri.IsHexDigit))
                {
                    return $"{name}: '{value}' ({side}) is not a SHA-256 digest";
                }
            }

            return string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{name}: bits {actual} are not {expected}";
        }

        if (rule.StartsWith("div=", StringComparison.Ordinal))
        {
            return Differs(expected, actual)
                ? null
                : $"{name}: agrees with MATLAB ({actual}) — divergence {rule[4..]} is retired; delete the line and its ADR entry";
        }

        if (rule.StartsWith("rel=", StringComparison.Ordinal) || rule.StartsWith("abs=", StringComparison.Ordinal))
        {
            if (e is not double ev || a is not double av)
            {
                return $"{name}: '{actual}' or '{expected}' is not a number under rule {rule}";
            }

            double tol = double.Parse(rule[4..], CultureInfo.InvariantCulture);
            if (double.IsNaN(ev) || double.IsNaN(av) || double.IsInfinity(ev) || double.IsInfinity(av))
            {
                return SameNumber(ev, av) ? null : $"{name}: {actual} is not {expected}";
            }

            bool relative = rule[0] == 'r';
            double allowed = relative ? (ev == 0 ? tol : tol * Math.Abs(ev)) : tol;
            double diff = Math.Abs(av - ev);
            return diff <= allowed
                ? null
                : $"{name}: {actual} is {diff:E3} from {expected}, more than the {allowed:E3} the rule {rule} allows";
        }

        return $"{name}: unknown rule '{rule}'";
    }

    private static bool SameNumber(double e, double a) => e == a || (double.IsNaN(e) && double.IsNaN(a));

    private static double? Number(string text)
    {
        string t = text.Trim();
        switch (t)
        {
            case "Inf":
            case "+Inf":
                return double.PositiveInfinity;
            case "-Inf":
                return double.NegativeInfinity;
            case "NaN":
                return double.NaN;
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }
}
