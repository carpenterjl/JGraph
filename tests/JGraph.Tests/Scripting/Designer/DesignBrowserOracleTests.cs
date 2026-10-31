using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W7a: the component browser's rows in each view, which rows can be
/// dragged and where to, the order a drop leaves, what picked rows select and how high the browser stands,
/// against R2025b's own <c>ComponentTreeViewModel</c>, its strategies, <c>ComponentTreeView</c>,
/// <c>TreeDragReorderCommand</c>, <c>ReorderCommand</c> and <c>ComponentBrowserLayoutManager</c>, run by
/// <c>ui-probes/w7/oracle/browser.js</c> (DesignBrowserOracle.json).
/// </summary>
public class DesignBrowserOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignBrowserOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static DesignComponent Make(JsonElement node, Dictionary<string, DesignComponent> all)
    {
        string name = node.GetProperty("n").GetString()!;
        var component = new DesignComponent(name, node.GetProperty("t").GetString()!, "f", string.Empty);
        all[name] = component;
        if (node.TryGetProperty("k", out JsonElement kids))
        {
            foreach (JsonElement kid in kids.EnumerateArray())
            {
                component.Add(Make(kid, all));
            }
        }

        return component;
    }

    private static (AppDesign Design, Dictionary<string, DesignComponent> All) Tree(JsonElement scene)
    {
        var all = new Dictionary<string, DesignComponent>(StringComparer.Ordinal);
        var design = new AppDesign(Make(scene.GetProperty("tree"), all));
        if (scene.TryGetProperty("groups", out JsonElement groups))
        {
            foreach (JsonElement pair in groups.EnumerateArray())
            {
                design.AddGroup([DesignGroupMember.Of(all[pair[0].GetString()!]), DesignGroupMember.Of(all[pair[1].GetString()!])]);
            }
        }

        return (design, all);
    }

    private static string[] Words(JsonElement array) => array.EnumerateArray().Select(static e => e.GetString()!).ToArray();

    [Fact]
    public void RowsAreWhatEachViewLists()
    {
        JsonElement[] scenes = Oracle("rows").EnumerateArray().ToArray();
        Assert.Equal(200, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            (AppDesign design, _) = Tree(s);
            foreach ((string which, DesignBrowserList view) in new[] { ("COMPLETE", DesignBrowserList.Complete), ("EXCLUDE", DesignBrowserList.ExcludeLabels), ("TAB", DesignBrowserList.TabOrder) })
            {
                // A row whose parent the view leaves out cannot stand in a tree: both lists drop it.
                var shown = new HashSet<string>(StringComparer.Ordinal);
                var expected = new List<string>();
                foreach (JsonElement row in s.GetProperty(which).EnumerateArray())
                {
                    string label = row.GetProperty("label").GetString()!;
                    string? parent = row.GetProperty("parent").GetString();
                    string name = label.StartsWith("app.", StringComparison.Ordinal) ? label[4..] : label;
                    if (parent is null || shown.Contains(parent))
                    {
                        shown.Add(name);
                        expected.Add(label + "<" + (parent ?? string.Empty));
                    }
                }

                string[] ours = DesignBrowser.Rows(design, "MyApp", view).Select(static r => r.Label + "<" + (r.IsApp ? string.Empty : r.Parent?.CodeName ?? "MyApp")).ToArray();
                Assert.True(expected.SequenceEqual(ours), $"{which}:\nR2025b {string.Join(" ", expected)}\nJGraph {string.Join(" ", ours)}");
            }
        }
    }

    [Fact]
    public void DragsAndDropsAreAllowedWhereR2025bAllowsThem()
    {
        JsonElement[] scenes = Oracle("drags").EnumerateArray().ToArray();
        Assert.Equal(300, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            (_, Dictionary<string, DesignComponent> all) = Tree(s);
            DesignComponent[] sources = Words(s.GetProperty("sources")).Select(n => all[n]).ToArray();
            string target = s.GetProperty("target").GetString()!;
            all.TryGetValue(target, out DesignComponent? onto);

            bool canDrag = s.GetProperty("canDrag").GetBoolean();
            bool canDrop = s.GetProperty("canDrop").GetBoolean();
            Assert.True(canDrag == DesignBrowser.CanDrag(sources), $"drag of {string.Join(",", sources.Select(static c => c.CodeName))}: R2025b {canDrag}");

            // The two refusals are JGraph's: a slider switch into what holds no component, a context menu into a menu.
            bool refusedHere = sources.Any(static c => c.Type is "matlab.ui.control.Switch" or "matlab.ui.container.ContextMenu");
            bool inside = onto is not null && Up(onto).Any(u => sources.Contains(u));
            if (!refusedHere && !inside && onto is not null)
            {
                Assert.True(canDrop == DesignBrowser.CanDrop(sources, onto),
                    $"drop of {string.Join(",", sources.Select(static c => c.CodeName))} on {target}: R2025b {canDrop}");
            }
        }

        static IEnumerable<DesignComponent> Up(DesignComponent from)
        {
            for (DesignComponent? c = from; c is not null; c = c.Parent)
            {
                yield return c;
            }
        }
    }

    [Fact]
    public void DropsLeaveTheOrderR2025bLeaves()
    {
        JsonElement[] scenes = Oracle("drops").EnumerateArray().ToArray();
        Assert.Equal(1200, scenes.Length);
        int changed = 0;
        foreach (JsonElement s in scenes)
        {
            string[] before = Words(s.GetProperty("before"));
            string[] types = Words(s.GetProperty("types"));
            string[] after = Words(s.GetProperty("after"));

            // The types are the children's as they stand after the drop.
            Dictionary<string, string> typeOf = after.Zip(types).ToDictionary(static z => z.First, static z => z.Second, StringComparer.Ordinal);
            var parent = new DesignComponent("P", "matlab.ui.container.Panel", "f", string.Empty);
            var byName = new Dictionary<string, DesignComponent>(StringComparer.Ordinal);
            foreach (string name in before)
            {
                byName[name] = new DesignComponent(name, typeOf[name], "f", string.Empty);
                parent.Add(byName[name]);
            }

            DesignBrowserList view = s.GetProperty("which").GetString() switch
            {
                "COMPLETE" => DesignBrowserList.Complete,
                "EXCLUDE" => DesignBrowserList.ExcludeLabels,
                _ => DesignBrowserList.TabOrder,
            };
            DesignComponent[] rowsAfter = Words(s.GetProperty("rowsAfter")).Select(n => byName[n]).ToArray();
            DesignComponent[] sources = Words(s.GetProperty("sources")).Select(n => byName[n]).ToArray();
            string[] ours = DesignBrowser.DropOrder(view, parent.Children.ToArray(), rowsAfter, sources).Select(static c => c.CodeName).ToArray();
            Assert.True(after.SequenceEqual(ours),
                $"{view}, [{string.Join(",", sources.Select(static c => c.CodeName))}] so that the rows read [{string.Join(",", rowsAfter.Select(static c => c.CodeName))}], from [{string.Join(",", before)}]:\nR2025b {string.Join(",", after)}\nJGraph {string.Join(",", ours)}");
            changed += before.SequenceEqual(after) ? 0 : 1;
        }

        Assert.True(changed > 300, $"only {changed} drops moved anything");
    }

    [Fact]
    public void RowsPlacedAsTheStoreLeavesThem()
    {
        DesignComponent a = new("A", "t", "f", string.Empty), b = new("B", "t", "f", string.Empty), c = new("C", "t", "f", string.Empty), d = new("D", "t", "f", string.Empty);
        DesignComponent[] rows = [a, b, c, d];

        // Before a row, in their order; after it, the last next to it; first, the last of them first.
        Assert.Equal([a, b, c, d], DesignBrowser.Moved(rows, [a, b], DesignBrowserPlace.Before, c));
        Assert.Equal([c, d, b, a], DesignBrowser.Moved(rows, [a, b], DesignBrowserPlace.After, d));
        Assert.Equal([d, c, a, b], DesignBrowser.Moved(rows, [c, d], DesignBrowserPlace.First));
    }

    [Fact]
    public void ARowDroppedOnARowGoesFirstUnderIt()
    {
        JsonElement[] scenes = Oracle("reparents").EnumerateArray().ToArray();
        Assert.Equal(200, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            Assert.Equal(JsonValueKind.Null, s.GetProperty("threw").ValueKind);
            string source = s.GetProperty("source").GetString()!;

            // MoveCommand and DeleteAndCopyCommand alike put it at the place it has among the rows: first.
            Assert.Equal([source, .. Words(s.GetProperty("toBefore"))], Words(s.GetProperty("toAfter")));
            var target = new DesignComponent("T", s.GetProperty("targetType").GetString()!, "f", string.Empty);
            var row = new DesignComponent(source, s.GetProperty("type").GetString()!, "f", string.Empty);
            Assert.Same(row, DesignBrowser.Moved([], [row], DesignBrowserPlace.First)[0]);
            if (row.Type != "matlab.ui.container.ContextMenu" && row.Type != "matlab.ui.control.Switch")
            {
                Assert.True(DesignBrowser.CanDrop([row], target), $"{source} ({row.Type}) on {target.Type}");
            }
        }
    }

    [Fact]
    public void PickedRowsSelectWhatR2025bSelects()
    {
        JsonElement[] scenes = Oracle("selections").EnumerateArray().ToArray();
        Assert.Equal(800, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            var figure = new DesignComponent("UIFigure", "matlab.ui.Figure", "f", string.Empty);
            var all = new Dictionary<string, DesignComponent>(StringComparer.Ordinal) { ["UIFigure"] = figure };
            var parents = s.GetProperty("parents").EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value.GetString()!, StringComparer.Ordinal);
            var types = s.GetProperty("types").EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value.GetString()!, StringComparer.Ordinal);

            DesignComponent Get(string name)
            {
                if (all.TryGetValue(name, out DesignComponent? got))
                {
                    return got;
                }

                var made = new DesignComponent(name, types[name], "f", string.Empty);
                all[name] = made;
                Get(parents[name]).Add(made);
                return made;
            }

            DesignComponent?[] start = Words(s.GetProperty("start")).Select(n => (DesignComponent?)Get(n)).ToArray();
            DesignComponent?[] asked = Words(s.GetProperty("asked")).Select(n => (DesignComponent?)Get(n)).ToArray();
            string[] expected = Words(s.GetProperty("after"));

            IReadOnlyList<DesignComponent?>? after = DesignBrowser.SelectionAfter(start, asked, figure);
            string[] ours = (after ?? start).Select(static c => c!.CodeName).ToArray();
            string scene = $"[{string.Join(",", start.Select(static c => c!.CodeName))}] asked [{string.Join(",", asked.Select(static c => c!.CodeName))}]";
            Assert.True(expected.SequenceEqual(ours), $"{scene}:\nR2025b {string.Join(",", expected)}\nJGraph {string.Join(",", ours)}");
        }
    }

    [Fact]
    public void TheBrowserStandsAsHighAsR2025bsDoes()
    {
        JsonElement[] scenes = Oracle("layouts").EnumerateArray().ToArray();
        Assert.Equal(400, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            double previous = s.GetProperty("previous").GetDouble();
            double? set = DesignBrowser.TopHeight(
                s.GetProperty("height").GetDouble(), s.GetProperty("newHeight").GetDouble(), s.GetProperty("manual").GetBoolean(), s.GetProperty("manualTop").GetDouble(), ref previous);
            JsonElement expected = s.GetProperty("set");
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(set);
            }
            else
            {
                Assert.Equal(expected.GetDouble(), set);
            }

            Assert.Equal(s.GetProperty("previousAfter").GetDouble(), previous);
        }
    }
}
