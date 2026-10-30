using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W5d: where Add Menu and Add Tool put what they add, what the
/// keys select, how Move and a drag order menus, toolbars and tools, against R2025b's own
/// <c>MenuAction</c>, <c>ToolAction</c>, <c>ToolbarAction</c>, keyboard plugins, <c>ReorderAction</c> and
/// drag models, run as App Designer's client has them by <c>ui-probes/w5/oracle/bars.js</c> (DesignBarsOracle.json).
/// </summary>
public class DesignBarsOracleTests
{
    private static JsonElement[] Scenes()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignBarsOracle.json")));
        return oracle.RootElement.GetProperty("scenes").Clone().EnumerateArray().ToArray();
    }

    private sealed record Scene(DesignComponent Figure, Dictionary<string, DesignComponent> ById, IReadOnlyList<DesignComponent> Selected);

    private static Scene Build(JsonElement s)
    {
        var byId = new Dictionary<string, DesignComponent>();
        DesignComponent Make(JsonElement e)
        {
            string id = e.GetProperty("id").GetString()!;
            var c = new DesignComponent(id, e.GetProperty("type").GetString()!, "f", string.Empty);
            byId[id] = c;
            foreach (JsonElement k in e.GetProperty("kids").EnumerateArray())
            {
                c.Add(Make(k));
            }

            return c;
        }

        DesignComponent figure = Make(s.GetProperty("figure"));
        return new Scene(figure, byId, s.GetProperty("selected").EnumerateArray().Select(e => byId[e.GetString()!]).ToArray());
    }

    private static string? Id(DesignComponent? c) => c?.CodeName;

    [Fact]
    public void WhatTheMenuActionsEnableIsR2025bs()
    {
        JsonElement[] scenes = Scenes();
        Assert.Equal(500, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            Scene scene = Build(s);
            bool[] want = s.GetProperty("ability").EnumerateArray().Select(static e => e.GetBoolean()).ToArray();
            var ours = DesignBars.MenuAbility(scene.Figure, scene.Selected);
            Assert.True(want.SequenceEqual([ours.Right, ours.Below, ours.AtEnd]), $"{string.Join(",", scene.Selected.Select(static c => c.CodeName))}: R2025b [{string.Join(",", want)}], JGraph {ours}");
        }
    }

    [Fact]
    public void WhereMenusAndToolsAreAddedIsR2025bs()
    {
        int checkedPlaces = 0;
        foreach (JsonElement s in Scenes())
        {
            Scene scene = Build(s);
            DesignComponent first = scene.Selected[0];
            if (s.TryGetProperty("addRight", out JsonElement right))
            {
                Same(right, DesignBars.AddMenuPlace(first, right: true), first);
                Same(s.GetProperty("addBelow"), DesignBars.AddMenuPlace(first, right: false), first);
                checkedPlaces += 2;
            }

            if (s.TryGetProperty("addAtEnd", out JsonElement end))
            {
                (DesignComponent parent, int? index) = DesignBars.AddMenuAtEndPlace(scene.ById[s.GetProperty("atEndTarget").GetString()!]);
                Assert.Equal(end.GetProperty("parent").GetString(), parent.CodeName);
                Assert.Equal(end.GetProperty("index").ValueKind == JsonValueKind.Null ? null : end.GetProperty("index").GetInt32(), index);
                checkedPlaces++;
            }

            if (s.TryGetProperty("addTool", out JsonElement tool))
            {
                Same(tool, DesignBars.AddToolPlace(first), first);
                Assert.Equal(s.GetProperty("addToolAtEnd").GetProperty("parent").GetString(), DesignBars.AddToolAtEndPlace(first).CodeName);
                checkedPlaces += 2;
            }
        }

        Assert.True(checkedPlaces > 300, $"only {checkedPlaces} places");

        static void Same(JsonElement want, (DesignComponent Parent, int Index) ours, DesignComponent selected)
        {
            Assert.True(want.GetProperty("parent").GetString() == ours.Parent.CodeName && want.GetProperty("index").GetInt32() == ours.Index,
                $"{selected.CodeName}: R2025b {want}, JGraph {ours.Parent.CodeName}@{ours.Index}");
        }
    }

    [Fact]
    public void WhatTheKeysSelectIsR2025bs()
    {
        int pressed = 0;
        foreach (JsonElement s in Scenes())
        {
            Scene scene = Build(s);
            if (!s.TryGetProperty("keys", out JsonElement keys))
            {
                continue;
            }

            DesignComponent first = scene.Selected[0];
            Func<IReadOnlyList<DesignComponent>, DesignKey, DesignComponent?> navigate =
                DesignBars.IsTool(first) ? DesignBars.NavigateTool : first.Type == DesignBars.ToolbarType ? DesignBars.NavigateToolbar : DesignBars.NavigateMenu;
            foreach ((string name, DesignKey key) in new[] { ("left", DesignKey.Left), ("right", DesignKey.Right), ("up", DesignKey.Up), ("down", DesignKey.Down), ("home", DesignKey.Home), ("end", DesignKey.End) })
            {
                if (!keys.TryGetProperty(name, out JsonElement k))
                {
                    continue;
                }

                // A key R2025b fails on, with its index gone out of the list, selects nothing here.
                string? want = k.TryGetProperty("threw", out _) || k.GetProperty("selected").ValueKind == JsonValueKind.Null
                    ? null
                    : k.GetProperty("selected")[0].GetString();
                Assert.True(want == Id(navigate(scene.Selected, key)), $"{name} on {string.Join(",", scene.Selected.Select(static c => c.CodeName))}: R2025b {want}, JGraph {Id(navigate(scene.Selected, key))}");
                pressed++;
            }

            if (keys.TryGetProperty("enter", out JsonElement enter))
            {
                Assert.Equal(Action(enter), Name(DesignBars.Enter(first, shift: false), first));
                Assert.Equal(Action(keys.GetProperty("shiftEnter")), Name(DesignBars.Enter(first, shift: true), first));
                pressed += 2;
            }

            if (keys.TryGetProperty("editorDown", out JsonElement down))
            {
                (DesignComponent? select, DesignBars.EnterAction add, DesignComponent? to) = DesignBars.EditorDown(first);
                string? wantSelect = down.GetProperty("selected").ValueKind == JsonValueKind.Null ? null : down.GetProperty("selected")[0].GetString();
                Assert.Equal(wantSelect, Id(select));
                string? action = down.GetProperty("action").GetString();
                Assert.Equal(action, add switch
                {
                    DesignBars.EnterAction.AddBelow => "DESIGN_MENU_HIERARCHY_ADD_BELOW",
                    DesignBars.EnterAction.AddAtEnd => "DESIGN_MENU_HIERARCHY_ADD_AT_END:" + to!.CodeName,
                    _ => null,
                });
                Assert.Equal(keys.GetProperty("editorUp").GetProperty("selected")[0].GetString(), DesignBars.EditorUp(first).CodeName);
                pressed += 2;
            }
        }

        Assert.True(pressed > 1500, $"only {pressed} keys");

        static string? Action(JsonElement k) => k.GetProperty("action").GetString();

        static string? Name(DesignBars.EnterAction action, DesignComponent c) => action switch
        {
            DesignBars.EnterAction.AddRight => "DESIGN_MENU_HIERARCHY_ADD_RIGHT",
            DesignBars.EnterAction.AddBelow => "DESIGN_MENU_HIERARCHY_ADD_BELOW",
            DesignBars.EnterAction.AddAtEnd => "DESIGN_MENU_HIERARCHY_ADD_AT_END:" + c.CodeName,
            _ => null,
        };
    }

    [Fact]
    public void MovesAndDropsAreR2025bs()
    {
        int moved = 0;
        foreach (JsonElement s in Scenes())
        {
            Scene scene = Build(s);
            if (!s.TryGetProperty("can", out JsonElement can))
            {
                continue;
            }

            DesignComponent first = scene.Selected[0];
            DesignComponent parent = first.Parent!;
            List<DesignComponent> among = first.Type switch
            {
                DesignBars.MenuType => parent.Children.Where(static c => c.Type == DesignBars.MenuType).ToList(),
                DesignBars.ToolbarType => parent.Children.Where(static c => c.Type == DesignBars.ToolbarType).ToList(),
                _ => [.. parent.Children],
            };
            int[] selected = scene.Selected.Select(c => among.IndexOf(c)).ToArray();
            var ability = DesignStructure.Ability(among.Count, selected, stack: false);
            Assert.True(can.EnumerateArray().Select(static e => e.GetBoolean()).SequenceEqual([ability.First, ability.Previous, ability.Next, ability.Last, ability.Offset]),
                $"{first.CodeName}: R2025b {can}, JGraph {ability}");
            foreach (JsonProperty move in s.GetProperty("moves").EnumerateObject())
            {
                DesignMove how = move.Name switch
                {
                    "FIRST" => DesignMove.First,
                    "PREVIOUS" => DesignMove.Previous,
                    "NEXT" => DesignMove.Next,
                    _ => DesignMove.Last,
                };
                Assert.Equal(move.Value.EnumerateArray().Select(static e => e.GetString()!), After(parent, among, DesignStructure.Order(among.Count, selected, how)));
                moved++;
            }

            if (s.TryGetProperty("drop", out JsonElement drop))
            {
                int hovered = drop.GetProperty("hovered").GetInt32();
                bool atEnd = drop.GetProperty("atEnd").GetBoolean();
                (int Drop, int Direction)? ours = first.Type == DesignBars.MenuType
                    ? DesignBars.MenuDrop(parent.Children.ToList().IndexOf(first), hovered, atEnd)
                    : DesignBars.ToolbarDrop(among.IndexOf(first), hovered, atEnd);
                JsonElement asked = drop.GetProperty("asked");
                if (asked.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(ours);
                    continue;
                }

                Assert.NotNull(ours);
                int menusBefore = first.Type == DesignBars.ToolbarType ? parent.Children.Count(static c => c.Type == DesignBars.MenuType) : 0;
                Assert.Equal(asked.GetProperty("drop").GetInt32(), ours!.Value.Drop + menusBefore);
                Assert.Equal(asked.GetProperty("direction").GetInt32(), ours.Value.Direction);
                int[] order = DesignStructure.Order(among.Count, selected, null, ours.Value.Drop, ours.Value.Direction);
                Assert.Equal(drop.GetProperty("after").EnumerateArray().Select(static e => e.GetString()!), After(parent, among, order));
                moved++;
            }

            if (s.TryGetProperty("toolDrop", out JsonElement tool))
            {
                DesignComponent[] sorted = scene.Selected.OrderByDescending(c => parent.Children.ToList().IndexOf(c)).ToArray();
                DesignComponent last = sorted[0];
                IReadOnlyList<DesignComponent> bars = DesignBars.Toolbars(parent.Parent!);
                var ours = DesignBars.ToolDrop(bars.ToList().IndexOf(parent), parent.Children.ToList().IndexOf(last), sorted.Length,
                    tool.GetProperty("bar").GetInt32(), tool.GetProperty("tool").GetInt32(), tool.GetProperty("atToolEnd").GetBoolean());
                JsonElement into = tool.GetProperty("moved");
                JsonElement asked = tool.GetProperty("asked");
                if (into.ValueKind != JsonValueKind.Null)
                {
                    Assert.Equal(into.GetProperty("index").GetInt32(), ours.To);
                    Assert.Equal(bars[tool.GetProperty("bar").GetInt32()].CodeName, into.GetProperty("to").GetString());
                }
                else if (asked.ValueKind != JsonValueKind.Null)
                {
                    Assert.Equal(asked.GetProperty("drop").GetInt32(), ours.Drop);
                    if (asked.GetProperty("direction").ValueKind == JsonValueKind.Null)
                    {
                        Assert.True(double.IsNaN(ours.Direction!.Value));
                    }
                    else
                    {
                        Assert.Equal(asked.GetProperty("direction").GetDouble(), ours.Direction);
                    }
                }
                else
                {
                    Assert.Null(ours.To);
                    Assert.Null(ours.Drop);
                }

                moved++;
            }
        }

        Assert.True(moved > 150, $"only {moved} moves");

        // The whole list of children with the ones in among in their new order.
        static IEnumerable<string> After(DesignComponent parent, List<DesignComponent> among, int[] order)
        {
            var slots = new Queue<DesignComponent>(order.Select(k => among[k]));
            return parent.Children.Select(c => among.Contains(c) ? slots.Dequeue().CodeName : c.CodeName);
        }
    }

    [Fact]
    public void AMenusLabelShowsWithoutItsAccessKey()
    {
        Assert.Equal("File", DesignBarLayout.DisplayLabel("&File"));
        Assert.Equal("Save & Exit", DesignBarLayout.DisplayLabel("Save && E&xit").Replace("Ex", " Ex", StringComparison.Ordinal).Replace("  ", " ", StringComparison.Ordinal));
        Assert.Equal("Menu", DesignBarLayout.DisplayLabel("Menu"));
    }

    [Fact]
    public void TheBarsStandAboveTheFigureAndShowAListUnderASelectedMenu()
    {
        var figure = new DesignComponent("Figure", "matlab.ui.Figure", "f", string.Empty);
        var file = new DesignComponent("File", DesignBars.MenuType, "f", string.Empty);
        var open = new DesignComponent("Open", DesignBars.MenuType, "f", string.Empty);
        var exit = new DesignComponent("Exit", DesignBars.MenuType, "f", string.Empty);
        var bar = new DesignComponent("Bar", DesignBars.ToolbarType, "f", string.Empty);
        var tool = new DesignComponent("Tool", DesignBars.PushToolType, "f", string.Empty);
        figure.Add(file);
        file.Add(open);
        file.Add(exit);
        figure.Add(bar);
        bar.Add(tool);

        DesignBarLayout Layout(params DesignComponent[] selected) => new(figure, selected, static t => t.Length * 6, static (c, _) => c.CodeName, static (_, _) => null);

        // Nothing selected: the bar and its menu, no list; the toolbar under it, with its tool and +.
        DesignBarLayout quiet = Layout();
        Assert.Equal(DesignBarLayout.MenuBarHeight + DesignBarLayout.ToolbarHeight, quiet.Height);
        Assert.Null(quiet.PlaceOf(open));
        Assert.Equal(-48, quiet.PlaceOf(file)!.Box.Y);
        Assert.Equal(-26 + DesignBarLayout.ToolMargin, quiet.PlaceOf(tool)!.Box.Y);
        Assert.Contains(quiet.Places, static p => p.Kind == DesignBarPlaceKind.AddTopLevel);
        Assert.Contains(quiet.Places, static p => p.Kind == DesignBarPlaceKind.ToolbarPlus);

        // The menu selected: its items in a column under it, and the + under them.
        DesignBarLayout open1 = Layout(file);
        Assert.NotNull(open1.PlaceOf(open));
        Assert.Equal(open1.PlaceOf(file)!.Box.Bottom - 2, open1.PlaceOf(open)!.Box.Y);
        Assert.Equal(DesignBarLayout.RowHeight, open1.PlaceOf(exit)!.Box.Y - open1.PlaceOf(open)!.Box.Y);
        Assert.Single(open1.Places, static p => p.Kind == DesignBarPlaceKind.AddChild);

        // An item selected: its menu marked, the + beside it, what is at the point its row.
        DesignBarLayout item = Layout(exit);
        Assert.True(item.PlaceOf(file)!.Holds);
        Assert.True(item.PlaceOf(exit)!.Selected);
        Assert.Single(item.Places, static p => p.Kind == DesignBarPlaceKind.AddRight);
        var row = item.PlaceOf(exit)!.Box;
        Assert.Same(exit, item.PlaceAt(new JGraph.Core.Primitives.Point2D(row.X + 2, row.Y + 2))!.Component);
    }
}
