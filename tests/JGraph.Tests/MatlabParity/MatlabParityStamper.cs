using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Tests.MatlabParity;

/// <summary>
/// Writes the ratchet's states into a recording from what JGraph printed: a failing line whose owner
/// is known becomes <c>pending Vn</c> with its exact baseline, a <c>div=</c> line becomes
/// <c>diverges</c> with its exact output, a run that fails gets its <c>RUN</c> line, and a pending
/// line that now agrees with MATLAB loses its marker. It never invents an owner: a failing line no
/// sidecar claims is reported, not stamped, and a divergence that has come to agree is reported as
/// retired. <c>MatlabParityFixtureTests</c> runs it when <c>JGRAPH_PARITY_STAMP</c> names the
/// expected folder to write; the commit that runs it is the one that records the transition.
/// </summary>
public static class MatlabParityStamper
{
    private static readonly Regex Line = new(@"^CHK\|([^|]+)\|([^|]*)\|([^|]*)(?:\|([^|]*)\|([^|]*))?$", RegexOptions.Compiled);

    /// <summary>The outcome: the recording to write, how many lines changed, and what could not be stamped.</summary>
    public sealed record Result(string Text, int Stamped, IReadOnlyList<string> Unstamped);

    /// <summary>
    /// Reads a fixture's <c>.owners</c> sidecar: one <c>name&lt;tab&gt;Vn</c> per line, <c>*</c> for
    /// every line not named, <c>RUN</c> for the run itself; <c>#</c> opens a comment. Null when there
    /// is no sidecar, which stamps nothing that needs an owner.
    /// </summary>
    public static Dictionary<string, string> ReadOwners(string? path)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path))
        {
            return owners;
        }

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !Regex.IsMatch(parts[1], @"^[A-Z]+\d+$"))
            {
                throw new FormatException($"{path}: '{raw}' is not 'name<tab>Vn'");
            }

            owners[parts[0]] = parts[1];
        }

        return owners;
    }

    /// <summary>
    /// The representation overlay a stamped recording implies: every line of <paramref name="stampedText"/>
    /// that differs from the line of the same name in <paramref name="baseText"/>, and the <c>RUN</c> line
    /// when it does; empty when nothing differs. The stamp mode run in the boxed lane stamps the merged
    /// recording (<see cref="MatlabParityComparer.ApplyOverlay"/>) and writes this to
    /// <c>expected/&lt;fixture&gt;.boxed.txt</c>, so the recording itself stays the packed representation's.
    /// A run that fails in the recording and succeeds under boxed storage has no spelling in the overlay
    /// grammar and is refused.
    /// </summary>
    public static string Overlay(string baseText, string stampedText)
    {
        var baseLines = new Dictionary<string, string>(StringComparer.Ordinal);
        string? baseRun = null;
        foreach (string raw in baseText.Split('\n'))
        {
            string text = raw.Trim();
            if (text.StartsWith("RUN|", StringComparison.Ordinal))
            {
                baseRun = text;
            }
            else if (Line.Match(text) is { Success: true } m)
            {
                baseLines[m.Groups[1].Value] = text;
            }
        }

        var output = new StringBuilder();
        bool runSeen = false;
        foreach (string raw in stampedText.Split('\n'))
        {
            string text = raw.Trim();
            if (text.StartsWith("RUN|", StringComparison.Ordinal))
            {
                runSeen = true;
                if (text != baseRun)
                {
                    output.Append(text).Append('\n');
                }

                continue;
            }

            Match m = Line.Match(text);
            if (m.Success && (!baseLines.TryGetValue(m.Groups[1].Value, out string? was) || was != text))
            {
                output.Append(text).Append('\n');
            }
        }

        if (baseRun is not null && !runSeen)
        {
            throw new NotSupportedException("the run fails in the recording and succeeds here: the overlay grammar cannot say so — re-stamp the recording in the packed lane first");
        }

        return output.ToString();
    }

    /// <summary>Stamps <paramref name="expectedText"/> from <paramref name="actualText"/> and the run's failure, if any.</summary>
    public static Result Stamp(string expectedText, string actualText, string? runFailure, IReadOnlyDictionary<string, string> owners)
    {
        Dictionary<string, ParityLine> actual = MatlabParityComparer.Parse(actualText);
        (string Stage, string Message)? run = MatlabParityComparer.ParseRun(expectedText);
        var unstamped = new List<string>();
        var output = new StringBuilder();
        int stamped = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // The RUN line first, whichever way it goes.
        if (runFailure is not null)
        {
            string? stage = owners.TryGetValue("RUN", out string? owner) ? owner : run?.Stage;
            if (stage is null)
            {
                unstamped.Add($"the run failed ({runFailure}) and no owner claims RUN");
            }
            else
            {
                string line = $"RUN|pending {stage}|{runFailure.Trim()}";
                if (run is null || run.Value.Message != runFailure.Trim() || run.Value.Stage != stage)
                {
                    stamped++;
                }

                output.Append(line).Append('\n');
            }
        }
        else if (run is not null)
        {
            stamped++; // the run succeeds now: the RUN line comes off
        }

        foreach (string raw in expectedText.Split('\n'))
        {
            string text = raw.TrimEnd('\r');
            if (text.StartsWith("RUN|", StringComparison.Ordinal))
            {
                continue;
            }

            Match m = Line.Match(text.Trim());
            if (!m.Success)
            {
                if (text.Trim().Length > 0)
                {
                    output.Append(text).Append('\n');
                }

                continue;
            }

            string name = m.Groups[1].Value;
            seen.Add(name);
            var line = new ParityLine(
                m.Groups[2].Value,
                m.Groups[3].Value.Length == 0 ? "exact" : m.Groups[3].Value,
                m.Groups[4].Success ? m.Groups[4].Value : null,
                m.Groups[4].Success ? m.Groups[5].Value : null);
            string head = $"CHK|{name}|{m.Groups[2].Value}|{m.Groups[3].Value}";

            if (!actual.TryGetValue(name, out ParityLine? got))
            {
                if (runFailure is null)
                {
                    unstamped.Add($"{name}: recorded but not printed");
                }

                output.Append(text).Append('\n'); // excused by the RUN line, or reported
                continue;
            }

            string printed = got.Value.Trim();
            bool divergent = line.Rule.StartsWith("div=", StringComparison.Ordinal);
            bool agrees = MatlabParityComparer.Compare($"{head}\n", $"CHK|{name}|{printed}|{m.Groups[3].Value}\n").Count == 0;

            if (divergent)
            {
                if (MatlabParityComparer.Compare($"{head}|diverges|{printed}\n", $"CHK|{name}|{printed}|{m.Groups[3].Value}\n").Count > 0)
                {
                    unstamped.Add($"{name}: agrees with MATLAB ({printed}) — divergence {line.Rule[4..]} is retired; delete the line and its ADR entry");
                    output.Append(text).Append('\n');
                    continue;
                }

                string stampedLine = $"{head}|diverges|{printed}";
                if (stampedLine != text.Trim())
                {
                    stamped++;
                }

                output.Append(stampedLine).Append('\n');
                continue;
            }

            if (agrees)
            {
                if (line.State is not null)
                {
                    stamped++; // the flip: the marker comes off
                }

                output.Append(head).Append('\n');
                continue;
            }

            string? stage = owners.TryGetValue(name, out string? named) ? named
                : owners.TryGetValue("*", out string? any) ? any
                : line.State is { } state && state.StartsWith("pending ", StringComparison.Ordinal) ? state[8..]
                : null;
            if (stage is null)
            {
                unstamped.Add($"{name}: printed '{printed}' against MATLAB's '{line.Value}' and no owner claims it");
                output.Append(text).Append('\n');
                continue;
            }

            string pendingLine = $"{head}|pending {stage}|{printed}";
            if (pendingLine != text.Trim())
            {
                stamped++;
            }

            output.Append(pendingLine).Append('\n');
        }

        foreach (string name in actual.Keys)
        {
            if (!seen.Contains(name))
            {
                unstamped.Add($"{name}: printed but not recorded — re-run tools/parity/record-matlab.ps1");
            }
        }

        return new Result(output.ToString(), stamped, unstamped);
    }
}
