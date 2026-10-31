using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W7b: what the inspector shows for a selection, who an edit goes to and the names it
/// brings, against R2025b's own <c>PropertyViewController</c>, strategies, <c>PropertyInspectorView</c>,
/// <c>InspectorPropertySheet</c>, <c>PropertiesPanelController</c> and <c>ModifyCodeNameGenerator</c>, over the sheets its
/// <c>InspectorFactory</c> publishes, run by <c>ui-probes/w7/oracle/inspector.js</c> (DesignInspectorOracle.json).
/// </summary>
public class DesignInspectorOracleTests
{
    private const string Label = "matlab.ui.control.Label";

    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignInspectorOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static DesignComponent Make(string name, string type, DesignComponent parent)
    {
        // A custom component is made by a call of its own class's name.
        var c = new DesignComponent(name, type, type.StartsWith("matlab.", StringComparison.Ordinal) ? "f" : type, string.Empty);
        parent.Add(c);
        return c;
    }

    private static IReadOnlyList<DesignPick> Picks(JsonElement scene)
    {
        var figure = new DesignComponent("fig", "matlab.ui.Figure", "f", string.Empty);
        var design = new AppDesign(figure);
        var picks = new List<DesignPick>();
        foreach (JsonElement p in scene.GetProperty("picks").EnumerateArray())
        {
            if (p.TryGetProperty("g", out JsonElement members))
            {
                DesignComponent a = Make(members[0].GetProperty("n").GetString()!, members[0].GetProperty("t").GetString()!, figure);
                DesignComponent b = Make(members[1].GetProperty("n").GetString()!, members[1].GetProperty("t").GetString()!, figure);
                picks.Add(DesignPick.Of(design.AddGroup([DesignGroupMember.Of(a), DesignGroupMember.Of(b)])));
                continue;
            }

            string name = p.GetProperty("n").GetString()!;
            string type = p.GetProperty("t").GetString()!;
            DesignComponent parent = figure;
            if (p.TryGetProperty("parent", out JsonElement over) && over.GetString() != "matlab.ui.Figure")
            {
                parent = Make("over", over.GetString()!, figure);
            }
            else if (p.TryGetProperty("grid", out _))
            {
                parent = Make("grid", "matlab.ui.container.GridLayout", figure);
            }

            DesignComponent c = type == "matlab.ui.Figure" ? figure : Make(name, type, parent);
            if (p.TryGetProperty("kids", out JsonElement kids) && kids.GetInt32() > 0)
            {
                Make("kid", type, c);
            }

            if (p.TryGetProperty("holds", out JsonElement holds) && holds.ValueKind == JsonValueKind.String)
            {
                Make("held", holds.GetString()!, c);
            }

            picks.Add(DesignPick.Of(c));
        }

        return picks;
    }

    private static string[] Words(JsonElement array) => array.EnumerateArray().Select(static e => e.GetString()!).ToArray();

    private static string Name(DesignComponent c) => c.Type == "matlab.ui.Figure" ? "fig" : c.CodeName;

    private static IEnumerable<JsonElement> Inspected() =>
        Oracle("scenes").EnumerateArray().Where(static s => !s.TryGetProperty("cascade", out _) && !s.TryGetProperty("threw", out _));

    [Fact]
    public void NoSceneThrew()
    {
        Assert.DoesNotContain(Oracle("scenes").EnumerateArray(), static s => s.TryGetProperty("threw", out _));
        Assert.Equal(605, Oracle("scenes").GetArrayLength());
    }

    [Fact]
    public void AnyValueShownIsTheLastComponents()
    {
        foreach (JsonElement s in Inspected())
        {
            if (s.TryGetProperty("lastWins", out JsonElement last))
            {
                Assert.True(last.GetBoolean());
            }
        }
    }

    [Fact]
    public void WhichViewAndWhichComponentsAreR2025bs()
    {
        foreach (JsonElement s in Inspected())
        {
            DesignInspection inspection = DesignInspectorSheet.Inspect(Picks(s))!;
            Assert.Equal(s.GetProperty("kind").GetString(), inspection.Kind.ToString().ToLowerInvariant());
            Assert.Equal(s.GetProperty("anchor").GetString(), Name(inspection.Anchor));
            Assert.Equal(Words(s.GetProperty("shown")), inspection.Shown.Select(Name).ToArray());
            if (s.TryGetProperty("cache", out JsonElement cache))
            {
                Assert.Equal(cache.GetString(), DesignInspectorSheet.KeyOf(inspection).Key);
            }
        }
    }

    [Fact]
    public void TheTabSaysWhatR2025bsSays()
    {
        foreach (JsonElement s in Inspected())
        {
            Assert.Equal(s.GetProperty("tab").GetString(), DesignInspectorSheet.TabOf(Picks(s)));
        }
    }

    private static List<string> RowsWeShow(DesignInspectorGroup group)
    {
        var rows = new List<string>();
        int? sub = null;
        foreach (DesignInspectorRow row in group.Rows)
        {
            if (row.SubGroup is { } n)
            {
                if (sub != n)
                {
                    rows.Add("{");
                    sub = n;
                }
            }
            else if (sub is not null)
            {
                rows.Add("}");
                sub = null;
            }

            rows.AddRange(row.Editors.Select(static e => e.SheetName));
        }

        if (sub is not null)
        {
            rows.Add("}");
        }

        return rows;
    }

    private static List<string> RowsTheyShow(JsonElement rows)
    {
        var list = new List<string>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.String)
            {
                list.Add(row.GetString()!);
            }
            else
            {
                list.Add("{");
                list.AddRange(RowsTheyShow(row.GetProperty("sub")));
                list.Add("}");
            }
        }

        return list;
    }

    [Fact]
    public void TheRowsAreThoseEveryComponentShares()
    {
        int compared = 0;
        foreach (JsonElement s in Inspected())
        {
            if (!s.TryGetProperty("groups", out JsonElement groups))
            {
                continue;
            }

            DesignInspection inspection = DesignInspectorSheet.Inspect(Picks(s))!;
            DesignInspectorGroup[] ours = DesignInspectorSheet.Sheet(inspection, DesignInspector.R2025b.GroupsOf(inspection.Anchor.Type)).ToArray();
            Assert.Equal(
                groups.EnumerateArray().Select(static g => g.GetProperty("title").GetString()! + (g.GetProperty("expanded").GetBoolean() ? "+" : "-")).ToArray(),
                ours.Select(static g => g.Title + (g.Expanded ? "+" : "-")).ToArray());
            foreach ((JsonElement theirs, DesignInspectorGroup mine) in groups.EnumerateArray().Zip(ours))
            {
                Assert.Equal(RowsTheyShow(theirs.GetProperty("rows")), RowsWeShow(mine));
            }

            Assert.Equal(Words(s.GetProperty("alpha")), DesignInspectorSheet.Alphabetical(ours).Select(static e => e.SheetName).ToArray());
            compared++;
        }

        Assert.True(compared > 500, $"{compared} compared");
    }

    [Fact]
    public void ASearchFindsWhatR2025bsFinds()
    {
        int searches = 0;
        foreach (JsonElement s in Inspected())
        {
            if (!s.TryGetProperty("searches", out JsonElement all))
            {
                continue;
            }

            DesignInspection inspection = DesignInspectorSheet.Inspect(Picks(s))!;
            IReadOnlyList<DesignEditor> rows = DesignInspectorSheet.Alphabetical(DesignInspectorSheet.Sheet(inspection, DesignInspector.R2025b.GroupsOf(inspection.Anchor.Type)));
            foreach (JsonElement q in all.EnumerateArray())
            {
                DesignInspectorMatches ours = DesignInspectorSheet.Search(rows, q.GetProperty("text").GetString()!);
                Assert.Equal(Words(q.GetProperty("direct")), ours.Direct.Select(static e => e.SheetName).ToArray());
                Assert.Equal(Words(q.GetProperty("help")), ours.Help.Select(static e => e.SheetName).ToArray());
                Assert.Equal(q.GetProperty("none").GetBoolean(), ours.None);
                searches++;
            }
        }

        Assert.True(searches > 3000, $"{searches} searches");
    }

    [Fact]
    public void AnEditGoesToWhoR2025bsGoesTo()
    {
        foreach (JsonElement s in Inspected())
        {
            if (!s.TryGetProperty("edits", out JsonElement edits))
            {
                continue;
            }

            DesignInspection inspection = DesignInspectorSheet.Inspect(Picks(s))!;
            foreach (JsonElement e in edits.EnumerateArray())
            {
                Assert.Equal(Words(e.GetProperty("components")), DesignInspectorSheet.EditTargets(inspection, e.GetProperty("property").GetString()!).Select(Name).ToArray());
            }
        }
    }

    [Fact]
    public void AutoResizeChildrenReachesWhatIsInside()
    {
        int scenes = 0;
        foreach (JsonElement s in Oracle("scenes").EnumerateArray().Where(static s => s.TryGetProperty("cascade", out _)))
        {
            JsonElement nested = s.GetProperty("nested");
            var figure = new DesignComponent("fig", "matlab.ui.Figure", "f", string.Empty);
            DesignComponent panel = Make(nested.GetProperty("panel").GetString()!, "matlab.ui.container.Panel", figure);
            DesignComponent inner = Make(nested.GetProperty("inner").GetString()!, nested.GetProperty("innerType").GetString()!, panel);
            Make(nested.GetProperty("button").GetString()!, "matlab.ui.control.Button", inner);
            DesignComponent other = Make(nested.GetProperty("other").GetString()!, "matlab.ui.container.Panel", figure);
            DesignPick[] picks = s.GetProperty("picks").GetArrayLength() == 1 ? [DesignPick.Of(panel)] : [DesignPick.Of(panel), DesignPick.Of(other)];
            Assert.Equal(Words(s.GetProperty("targets")), DesignInspectorSheet.EditTargets(DesignInspectorSheet.Inspect(picks)!, "AutoResizeChildren").Select(Name).ToArray());
            scenes++;
        }

        Assert.Equal(20, scenes);
    }

    [Fact]
    public void SeveralRenamedTogetherDiffer()
    {
        JsonElement[] scenes = Oracle("renames").EnumerateArray().ToArray();
        Assert.Equal(200, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            Assert.False(s.TryGetProperty("threw", out _));
            string text = s.GetProperty("text").GetString()!;
            JsonElement[] picks = s.GetProperty("picks").EnumerateArray().ToArray();
            var inUse = new List<string> { "fig" };
            inUse.AddRange(picks.Select(static p => p.GetProperty("n").GetString()!));
            string[] ours = picks.Select(p => DesignNames.RenamedForText(
                p.GetProperty("text").GetString()!, text, DesignNames.Of(p.GetProperty("t").GetString()!)!, p.GetProperty("n").GetString()!, inUse)).ToArray();
            Assert.Equal(Words(s.GetProperty("after")), ours);
        }
    }

    [Fact]
    public void WhatItSaysIsR2025bsWords()
    {
        JsonElement words = Oracle("words");
        Assert.Equal(DesignInspectorSheet.NoMatches, words.GetProperty("none").GetString());
        Assert.Equal(DesignInspectorSheet.HelpResults, words.GetProperty("help").GetString());
        Assert.Equal(DesignInspectorSheet.MultipleComponents, words.GetProperty("multipleTab").GetString());
        Assert.Equal(DesignInspectorSheet.NoComponent, words.GetProperty("noneTab").GetString());
    }
}
