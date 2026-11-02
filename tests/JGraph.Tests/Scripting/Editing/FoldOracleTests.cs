using System.Text.Json;
using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>
/// The App Designer window plan's stage W9c: the blocks the editor folds against the blocks R2025b's <c>mtree</c> finds in the Smart Indent
/// oracle's programs, run headless by <c>ui-probes/w9/w9_folds.m</c> (FoldOracle.json).
/// </summary>
public class FoldOracleTests
{
    private static readonly Dictionary<string, FoldKind> Kinds = new(StringComparer.Ordinal)
    {
        ["FUNCTION"] = FoldKind.Function,
        ["CLASSDEF"] = FoldKind.Classdef,
        ["PROPERTIES"] = FoldKind.Properties,
        ["METHODS"] = FoldKind.Methods,
        ["EVENTS"] = FoldKind.Events,
        ["ENUMERATION"] = FoldKind.Enumeration,
        ["FOR"] = FoldKind.For,
        ["PARFOR"] = FoldKind.Parfor,
        ["WHILE"] = FoldKind.While,
        ["TRY"] = FoldKind.Try,
        ["SPMD"] = FoldKind.Spmd,
        ["IF"] = FoldKind.If,
        ["SWITCH"] = FoldKind.Switch,
        ["CASE"] = FoldKind.Case,
        ["OTHERWISE"] = FoldKind.Case,
        ["ARGUMENTS"] = FoldKind.Arguments,
        ["BLKCOM"] = FoldKind.BlockComment,
    };

    [Fact]
    public void EveryBlockIsTheOneMtreeFinds()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Editing", "FoldOracle.json")));
        int compared = 0;
        foreach (JsonElement c in oracle.RootElement.EnumerateArray())
        {
            if (c.GetProperty("error").GetBoolean())
            {
                continue;
            }

            string[] lines = c.GetProperty("text").GetString()!.Split('\n');
            JsonElement blocks = c.GetProperty("blocks");
            var expected = new HashSet<FoldCandidate>();
            var commentLines = new SortedSet<int>();
            var inBlockComment = new HashSet<int>();
            IEnumerable<JsonElement> nodes = blocks.ValueKind == JsonValueKind.Array ? blocks.EnumerateArray() : [blocks];
            foreach (JsonElement b in nodes)
            {
                string kind = b.GetProperty("kind").GetString()!;
                int first = b.GetProperty("start").GetInt32();
                int last = b.GetProperty("end").GetInt32();
                if (kind == "COMMENT")
                {
                    commentLines.Add(first);
                }

                if (kind == "BLKCOM")
                {
                    for (int i = first; i <= last; i++)
                    {
                        inBlockComment.Add(i);
                    }
                }

                if (Kinds.TryGetValue(kind, out FoldKind fold) && last > first)
                {
                    expected.Add(new FoldCandidate(fold, first, last));
                }
            }

            // A run of two or more whole-line comments among mtree's comment nodes, outside block comments, is a comment candidate.
            int start = -1;
            for (int line = 1; line <= lines.Length + 1; line++)
            {
                bool comment = line <= lines.Length && commentLines.Contains(line) && !inBlockComment.Contains(line) && lines[line - 1].TrimStart().StartsWith('%');
                if (comment)
                {
                    start = start < 0 ? line : start;
                }
                else
                {
                    if (start > 0 && line - start >= 2)
                    {
                        expected.Add(new FoldCandidate(FoldKind.Comment, start, line - 1));
                    }

                    start = -1;
                }
            }

            var got = new HashSet<FoldCandidate>(CodeFoldRules.AllBlocks(lines).Where(static f => f.Kind != FoldKind.Section));
            Assert.True(expected.SetEquals(got), $"case {compared + 1}:\n  missing {string.Join(", ", expected.Except(got))}\n  extra {string.Join(", ", got.Except(expected))}");
            compared++;
        }

        Assert.True(compared > 350);
    }

    [Fact]
    public void TheCandidateAtALineIsTheInnermost()
    {
        string[] lines = ["function f", "if x", "  for k = 1:2", "    y = 1;", "  end", "end", "% a", "% b", "end"];
        IReadOnlyList<FoldCandidate> all = CodeFoldRules.AllBlocks(lines);
        Assert.Equal(new FoldCandidate(FoldKind.For, 3, 5), CodeFoldRules.At(all, 4));
        Assert.Equal(new FoldCandidate(FoldKind.If, 2, 6), CodeFoldRules.At(all, 2));
        Assert.Equal(new FoldCandidate(FoldKind.Comment, 7, 8), CodeFoldRules.At(all, 8));
        Assert.Equal([new FoldCandidate(FoldKind.Function, 1, 9), new FoldCandidate(FoldKind.If, 2, 6), new FoldCandidate(FoldKind.For, 3, 5)], CodeFoldRules.Lineage(all, new FoldCandidate(FoldKind.For, 3, 5)));
        IReadOnlyList<FoldCandidate> defaults = CodeFoldRules.Candidates(lines);
        Assert.DoesNotContain(defaults, static c => c.Kind == FoldKind.If);
        Assert.Contains(defaults, static c => c.Kind == FoldKind.Function);
    }
}
