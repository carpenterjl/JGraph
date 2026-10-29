using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W5: the clipboard's naming and grid placement against
/// R2025b's own <c>CopyCodeNameGenerator</c> and <c>GridUtil.findLocation</c>, run unmodified over
/// 1,600 seeded scenes by <c>ui-probes/w5/oracle/copy.js</c> (DesignClipboardOracle.json).
/// </summary>
public class DesignClipboardOracleTests
{
    private static JsonElement[] Cases(string op)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignClipboardOracle.json")));
        return oracle.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("op").GetString() == op).Select(static c => c.Clone()).ToArray();
    }

    /// <summary>A component info as the oracle wrote it, made a component whose code name and label text are its; its id noted.</summary>
    private static DesignComponent Component(JsonElement info, Dictionary<DesignComponent, string> ids)
    {
        string type = info.GetProperty("Type").GetString()!;
        JsonElement values = info.GetProperty("PropertyValues");
        var component = new DesignComponent(values.GetProperty("CodeName").GetString()!, DesignCatalog.R2025b.ByType(type)!);
        if (values.TryGetProperty("Text", out JsonElement text))
        {
            component.Set("Text", new CharValue(text[0].GetString()!));
        }

        ids[component] = info.GetProperty("SourcePeerNodeId").GetString()!;
        foreach (JsonElement child in info.GetProperty("Children").EnumerateArray())
        {
            component.Add(Component(child, ids));
        }

        return component;
    }

    [Fact]
    public void CopiesAreNamedAsR2025bNamesThem()
    {
        JsonElement[] cases = Cases("names");
        Assert.Equal(800, cases.Length);
        for (int n = 0; n < cases.Length; n++)
        {
            JsonElement c = cases[n];
            var ids = new Dictionary<DesignComponent, string>(ReferenceEqualityComparer.Instance);
            var items = new List<DesignClipItem>();
            foreach (JsonElement top in c.GetProperty("components").EnumerateArray())
            {
                if (top.GetProperty("Type").GetString() == "Group")
                {
                    var group = new DesignClipGroup();
                    foreach (JsonElement member in top.GetProperty("Members").EnumerateArray())
                    {
                        group.Members.Add((items.Count, null));
                        items.Add(new DesignClipItem(Component(member, ids), null!, null, null, false, []) { Group = group });
                    }
                }
                else
                {
                    items.Add(new DesignClipItem(Component(top, ids), null!, null, null, false, []));
                }
            }

            var clip = new DesignClip(items, [], null!, null!);
            var inUse = c.GetProperty("inUse").EnumerateArray().Select(static s => s.GetString()!).ToList();
            DesignDocument.CopyNames(clip, items.Select(static i => i.Copy).ToArray(), inUse);

            Dictionary<string, string> expected = c.GetProperty("names").EnumerateArray().ToDictionary(static p => p[0].GetString()!, static p => p[1].GetString()!);
            foreach ((DesignComponent component, string id) in ids)
            {
                if (expected.TryGetValue(id, out string? name))
                {
                    Assert.True(name == component.CodeName, $"scene {n}, {id}: R2025b {name}, JGraph {component.CodeName}");
                }
            }

            Assert.Equal(expected.Count, ids.Count(p => expected.ContainsKey(p.Value)));
        }
    }

    private static GridSpan? Span(JsonElement constraints)
    {
        if (constraints.GetProperty("Type").GetString() != "Grid")
        {
            return null;
        }

        (int a, int b) Range(JsonElement v) => v.ValueKind == JsonValueKind.Array ? (v[0].GetInt32(), v[1].GetInt32()) : (v.GetInt32(), v.GetInt32());
        (int r0, int r1) = Range(constraints.GetProperty("Row"));
        (int c0, int c1) = Range(constraints.GetProperty("Column"));
        return new GridSpan(r0, r1, c0, c1);
    }

    [Fact]
    public void CellsAreFoundAsR2025bFindsThem()
    {
        JsonElement[] cases = Cases("cells");
        Assert.Equal(800, cases.Length);
        int placed = 0;
        for (int n = 0; n < cases.Length; n++)
        {
            JsonElement c = cases[n];
            var groups = new Dictionary<string, DesignClipGroup>(StringComparer.Ordinal);
            foreach (JsonElement g in c.GetProperty("groups").EnumerateArray())
            {
                groups[g.GetProperty("Id").GetString()!] = new DesignClipGroup();
            }

            var entries = c.GetProperty("components").EnumerateArray().Select(e =>
                (Span(e.GetProperty("PropertyValues").GetProperty("LayoutConstraints")),
                 e.TryGetProperty("GroupId", out JsonElement id) ? groups[id.GetString()!] : null)).ToArray();
            GridSpan[] occupied = c.GetProperty("occupied").EnumerateArray().Select(static o => Span(o)!.Value).ToArray();
            JsonElement start = c.GetProperty("start");
            int? row = start[0].ValueKind == JsonValueKind.Number ? start[0].GetInt32() : null;
            int? column = start[1].ValueKind == JsonValueKind.Number ? start[1].GetInt32() : null;

            IReadOnlyList<(int Row, int Column)>? found = DesignDocument.FindLocation(entries, c.GetProperty("rows").GetInt32(), c.GetProperty("columns").GetInt32(), occupied, row, column);
            JsonElement expected = c.GetProperty("found");
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Assert.True(found is null, $"scene {n}: R2025b finds no place, JGraph does");
                continue;
            }

            placed++;
            Assert.True(found is not null, $"scene {n}: R2025b finds a place, JGraph none");
            Assert.Equal(expected.EnumerateArray().Select(static p => (p[0].GetInt32(), p[1].GetInt32())).ToArray(), found!.ToArray());
        }

        Assert.True(placed > 200);
    }
}
