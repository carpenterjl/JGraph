using System.Globalization;
using System.Text.RegularExpressions;

namespace JGraph.Tests.MatlabParity;

/// <summary>
/// Compares a fixture's <c>CHK|name|value|rule</c> lines against the recording MATLAB made of the
/// same script. The rules are the ones <c>tools/parity/compare.py</c> carries — the two are the
/// same comparator in two hosts, and a change to one is a change to both.
/// </summary>
/// <remarks>
/// Rules: <c>exact</c> (the same number, or the same text), <c>shape</c> (the same text once
/// whitespace is normalised), <c>rel=tol</c>, <c>abs=tol</c>, and <c>div=ADRnnnn</c> — a recorded
/// divergence whose values <b>must differ</b>, so that a divergence quietly closed is noticed and
/// retired from its ADR rather than left on the books.
/// </remarks>
public static class MatlabParityComparer
{
    private static readonly Regex Line = new(@"^CHK\|([^|]+)\|([^|]*)\|([^|]*)$", RegexOptions.Compiled);

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
