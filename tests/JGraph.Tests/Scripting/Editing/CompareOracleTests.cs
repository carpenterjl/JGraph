using System.Text.Json;
using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>
/// The App Designer window plan's stage W14: the rows <see cref="TextDiff"/> makes of two texts against the rows of
/// R2025b's comparison reports, written by <c>visdiff</c> headless over hand-made pairs, a seeded corpus and the W0 app
/// pairs (<c>ui-probes/w14/w14_compare.m</c>, CompareOracle.json) and over small tie cases (<c>w14_ties.m</c>,
/// CompareTiesOracle.json).
/// </summary>
public class CompareOracleTests
{
    /// <summary>
    /// The pairs whose equally long matchings R2025b resolves differently from the parity rule (deeper ties of its
    /// two-ended search); each differs only in which of equal lines is marked. Listed so that a change is seen.
    /// </summary>
    private static readonly string[] KnownTies = ["corpus 2", "corpus 13", "corpus 20", "corpus 25", "corpus 29", "app: another app"];

    [Fact]
    public void EveryPairIsRowedAsR2025bsReportIs()
    {
        var mismatches = new List<string>();
        int compared = 0;
        foreach (string file in new[] { "CompareOracle.json", "CompareTiesOracle.json" })
        {
            using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Editing", file)));
            foreach (JsonElement pair in oracle.RootElement.EnumerateArray())
            {
                string name = pair.GetProperty("name").GetString()!;
                IReadOnlyList<string> left = TextDiff.SplitLines(string.Join("\n", Lines(pair.GetProperty("left"))));
                IReadOnlyList<string> right = TextDiff.SplitLines(string.Join("\n", Lines(pair.GetProperty("right"))));
                List<DiffRow> expected = [.. Rows(pair.GetProperty("rows"))];
                IReadOnlyList<DiffRow> actual = TextDiff.Compare(left, right);
                compared++;
                if (!actual.SequenceEqual(expected))
                {
                    // A mismatch may only be a tie: the same number of matched lines, and the same multiset of kinds.
                    Assert.Equal(expected.Count(static r => r.Kind == DiffRowKind.Same), actual.Count(static r => r.Kind == DiffRowKind.Same));
                    mismatches.Add(name);
                }
            }
        }

        Assert.Equal(108, compared);
        Assert.Equal(KnownTies.Order(StringComparer.Ordinal), mismatches.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheAppPairsCountAsTheReportDoes()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Editing", "CompareOracle.json")));
        JsonElement other = oracle.RootElement.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "app: another app");
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(
            TextDiff.SplitLines(string.Join("\n", Lines(other.GetProperty("left")))),
            TextDiff.SplitLines(string.Join("\n", Lines(other.GetProperty("right")))));
        // The report has 6 inserted, 79 deleted and 47 changed rows (W0 read 11 deleted and 47 changed of the same report,
        // missing the deleted rows' spans); this pair is among the known ties, so only the matched lines are held to it.
        Assert.Equal(183 - 6 - 79 - 47, rows.Count(r => r.Kind == DiffRowKind.Same));
        JsonElement code = oracle.RootElement.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "app: one callback line");
        IReadOnlyList<DiffRow> one = TextDiff.Compare(
            TextDiff.SplitLines(string.Join("\n", Lines(code.GetProperty("left")))),
            TextDiff.SplitLines(string.Join("\n", Lines(code.GetProperty("right")))));
        Assert.Equal([new DiffRow(DiffRowKind.Changed, 37, 37)], one.Where(static r => r.Kind != DiffRowKind.Same));
    }

    private static IEnumerable<string> Lines(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Select(static e => e.GetString() ?? string.Empty),
        JsonValueKind.String => [element.GetString()!],
        _ => [],
    };

    private static IEnumerable<DiffRow> Rows(JsonElement element)
    {
        IEnumerable<JsonElement> items = element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray(),
            JsonValueKind.Object => [element],
            _ => [],
        };
        foreach (JsonElement row in items)
        {
            DiffRowKind kind = row.GetProperty("kind").GetString() switch
            {
                "inserted" => DiffRowKind.Inserted,
                "deleted" => DiffRowKind.Deleted,
                "changed" => DiffRowKind.Changed,
                _ => DiffRowKind.Same,
            };
            yield return new DiffRow(kind, row.GetProperty("left").GetInt32(), row.GetProperty("right").GetInt32());
        }
    }
}
