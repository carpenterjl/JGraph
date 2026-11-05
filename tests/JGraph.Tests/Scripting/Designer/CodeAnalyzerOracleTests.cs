using System.Text;
using System.Text.Json;
using JGraph.Scripting.Analysis;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The Code Analyzer against R2025b (App Designer window plan W10, ADR 0304): every message <c>checkcode -id -struct</c>
/// raised over the probes' files (<c>w10_analyzer</c>, <c>w10_rules</c>; CodeAnalyzerOracle.json) whose check JGraph
/// has is raised by JGraph with the same id, line, columns and fixability, and JGraph raises nothing R2025b did not.
/// A check JGraph does not have (listed in <see cref="CodeChecks.KnownElsewhere"/>) is passed over.
/// </summary>
public class CodeAnalyzerOracleTests
{
    private static JsonElement[] Cases()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "CodeAnalyzerOracle.json")));
        return oracle.RootElement.GetProperty("cases").Clone().EnumerateArray().ToArray();
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (JsonElement c in Cases())
        {
            data.Add(c.GetProperty("name").GetString()!);
        }

        return data;
    }

    private sealed record Expected(string Id, int Line, int Start, int End, bool Fix, string Text);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Messages_MatchR2025b(string name)
    {
        JsonElement c = Cases().Single(e => e.GetProperty("name").GetString() == name);
        string text = c.GetProperty("text").GetString()!;
        List<Expected> expected = c.GetProperty("messages").EnumerateArray()
            .Select(m => new Expected(m.GetProperty("id").GetString()!, m.GetProperty("line").GetInt32(), m.GetProperty("start").GetInt32(), m.GetProperty("end").GetInt32(), m.GetProperty("fix").GetBoolean(), m.GetProperty("text").GetString()!))
            .ToList();

        CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(text, name);
        var got = analysis.Messages.Select(m => (m.Id, m.Line, m.StartColumn, m.EndColumn, m.Fixable, m.Text)).ToList();
        var want = expected.Where(e => CodeChecks.ById(e.Id) is not null).Select(e => (e.Id, e.Line, e.Start, e.End, e.Fix, e.Text)).ToList();

        var report = new StringBuilder();
        foreach ((string, int, int, int, bool, string) w in want.Where(w => !got.Contains(w)))
        {
            report.AppendLine("missing: " + w);
        }

        foreach ((string, int, int, int, bool, string) g in got.Where(g => !want.Contains(g)))
        {
            report.AppendLine("extra:   " + g);
        }

        Assert.True(report.Length == 0, name + "\n" + report);
    }

    [Fact]
    public void EveryCheckIsEitherRaisedOrKnownElsewhere()
    {
        var unknown = new List<string>();
        foreach (JsonElement c in Cases())
        {
            foreach (JsonElement m in c.GetProperty("messages").EnumerateArray())
            {
                string id = m.GetProperty("id").GetString()!;
                if (CodeChecks.ById(id) is null && !CodeChecks.KnownElsewhere.Contains(id))
                {
                    unknown.Add(id);
                }
            }
        }

        Assert.Empty(unknown.Distinct());
    }
}
