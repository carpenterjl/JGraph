using System.Text;
using System.Text.Json;
using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>
/// The App Designer window plan's stage W9b: Smart Indent against R2025b's own formatter (<c>matlab.codeanalyzer.internal.formatCode</c>,
/// behind <c>indentcode</c>), run headless by <c>ui-probes/w9/w9_indent.m</c> over seeded programs with their indentation spoiled (SmartIndentOracle.json).
/// </summary>
public class SmartIndentOracleTests
{
    [Fact]
    public void EveryProgramIndentsAsFormatCodeDoes()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Editing", "SmartIndentOracle.json")));
        var failures = new StringBuilder();
        int failed = 0;
        int total = 0;
        int skipped = 0;
        foreach (JsonElement c in oracle.RootElement.EnumerateArray())
        {
            if (c.TryGetProperty("error", out _))
            {
                continue;
            }

            total++;
            string[] lines = c.GetProperty("text").GetString()!.Split('\n');
            IndentMode mode = c.GetProperty("mode").GetString() switch
            {
                "AllFunctionIndent" => IndentMode.All,
                "ClassicFunctionIndent" => IndentMode.Classic,
                _ => IndentMode.Mixed,
            };
            if (mode == IndentMode.Classic && lines[0].TrimStart().StartsWith("classdef", StringComparison.Ordinal))
            {
                // formatCode puts a class's second method one indent left of its first in Classic mode (ADR 0298); JGraph follows
                // the rich text component's IndentUtilities, which keeps the methods level.
                skipped++;
                continue;
            }
            bool pad = c.GetProperty("blank").GetString() == "Indent";
            int first = c.GetProperty("startLine").GetInt32() - 1;
            int last = c.GetProperty("endLine").GetInt32() - 1;
            string[] got = [.. SmartIndent.Format(lines, first, last, mode, pad)];
            string[] expected = c.GetProperty("result").GetString()!.Split('\n');
            if (!expected.SequenceEqual(got))
            {
                failed++;
                // W9_INDENT_CASES=5,16 prints those cases whole, for a look at what the formatter did.
                if ((Environment.GetEnvironmentVariable("W9_INDENT_CASES") ?? string.Empty).Split(',').Contains(total.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    failures.Append($"=== case {total} {mode} pad={pad} lines {first + 1}-{last + 1}\n");
                    for (int i = 0; i < Math.Max(expected.Length, got.Length); i++)
                    {
                        string e = (i < expected.Length ? expected[i] : "<none>").Replace("\t", "\\t", StringComparison.Ordinal);
                        string g = (i < got.Length ? got[i] : "<none>").Replace("\t", "\\t", StringComparison.Ordinal);
                        string original = (i < lines.Length ? lines[i] : "<none>").Replace("\t", "\\t", StringComparison.Ordinal);
                        failures.Append(e == g ? $"    {e}\n" : $"  ! {e}\n  > {g}\n  < {original}\n");
                    }
                }
                else if (failed <= 40)
                {
                    // The first line that differs, with the two lines before it, so a run of failures shows its kinds.
                    int at = 0;
                    while (at < Math.Min(expected.Length, got.Length) && expected[at] == got[at])
                    {
                        at++;
                    }

                    failures.Append($"--- case {total} {mode} pad={pad} lines {first + 1}-{last + 1}, line {at + 1}\n");
                    for (int i = Math.Max(0, at - 2); i < at; i++)
                    {
                        failures.Append($"    {expected[i].Replace("\t", "\\t", StringComparison.Ordinal)}\n");
                    }

                    failures.Append($"  ! {(at < expected.Length ? expected[at] : "<none>").Replace("\t", "\\t", StringComparison.Ordinal)}\n");
                    failures.Append($"  > {(at < got.Length ? got[at] : "<none>").Replace("\t", "\\t", StringComparison.Ordinal)}\n");
                }
            }
        }

        Assert.True(failed == 0, $"{failed} of {total} differ ({skipped} Classic class cases left out)\n{failures}");
        Assert.True(total - skipped > 280);
    }
}
