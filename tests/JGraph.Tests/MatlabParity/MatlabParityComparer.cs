using System.Globalization;
using System.Text.RegularExpressions;

namespace JGraph.Tests.MatlabParity;

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
/// A <c>bits</c> line is printed by the fixture as <c>CHK|name|file:&lt;absolute path&gt;|bits</c>,
/// naming a file its own <c>writebits</c> helper wrote: one header line <c>class rows cols …</c>,
/// then one <c>num2hex</c> row per element in column-major order, a real plane then an imaginary
/// one for complex. Whoever captures the output — the recorder, this harness, <c>compare.py</c> —
/// calls <see cref="ResolveBits"/> first: it replaces the path with the SHA-256 of the file's
/// bytes and deletes the file and its <c>tempname</c> folder, so a recording carries a digest and
/// never a path. Nothing computable in exact doubles on both engines is a digest worth trusting,
/// which is why the digest is taken on the host.
/// </remarks>
public static class MatlabParityComparer
{
    private static readonly Regex Line = new(@"^CHK\|([^|]+)\|([^|]*)\|([^|]*)$", RegexOptions.Compiled);

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

    /// <summary>The <c>name -> (value, rule)</c> pairs a log holds; a missing rule reads as <c>exact</c>.</summary>
    public static Dictionary<string, (string Value, string Rule)> Parse(string text)
    {
        var lines = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            Match m = Line.Match(raw.Trim());
            if (m.Success)
            {
                string rule = m.Groups[3].Value.Length == 0 ? "exact" : m.Groups[3].Value;
                lines[m.Groups[1].Value] = (m.Groups[2].Value, rule);
            }
        }

        return lines;
    }

    /// <summary>Every line that fails its rule, in the recording's order; empty when all agree.</summary>
    public static List<string> Compare(string expectedText, string actualText)
    {
        var expected = Parse(expectedText);
        var actual = Parse(actualText);
        var problems = new List<string>();

        foreach ((string name, (string value, string rule)) in expected)
        {
            if (!actual.TryGetValue(name, out (string Value, string Rule) got))
            {
                problems.Add($"{name}: recorded but not printed");
                continue;
            }

            if (got.Rule != rule)
            {
                problems.Add($"{name}: rule is {got.Rule} here and {rule} in the recording");
                continue;
            }

            string? problem = Check(name, value, got.Value, rule);
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
            bool differs = e is double dv && a is double da
                ? !SameNumber(dv, da)
                : expected.Trim() != actual.Trim();
            return differs
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
