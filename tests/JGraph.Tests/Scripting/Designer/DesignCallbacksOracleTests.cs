using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W7d: what several components have in common, the Callbacks submenu, an edit of several, a row of the Callbacks tab, what several
/// selected show and the dialog's list, against R2025b's own <c>CallbackUtil</c>, <c>_ComponentContextMenuInfo</c>, <c>MassEditCallbackCommand</c>, <c>CallbackSection</c>,
/// <c>CallbackMultiselectView</c> and <c>SharedCallbackDialog</c>, run by <c>ui-probes/w7/oracle/callbacks.js</c> (DesignCallbacksOracle.json).
/// </summary>
public class DesignCallbacksOracleTests
{
    private const string Startup = DesignCallbackRules.StartupProperty;

    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignCallbacksOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static string[] Words(JsonElement array) => array.EnumerateArray().Select(static e => e.GetString()!).ToArray();

    private static IReadOnlyList<string> Labels(string type) => DesignCallbacks.R2025b.PropertiesOf(type);

    private static IReadOnlyList<DesignCallbackGroup> GroupsOf(string type) =>
        type == "App" ? [new DesignCallbackGroup(null, [Startup])] : DesignCallbacks.R2025b.GroupsOf(type);

    private static IReadOnlyList<string> PropertiesOf(string type) => GroupsOf(type).SelectMany(static g => g.Properties).ToArray();

    private sealed record Peer(DesignComponent Component, Dictionary<string, string> Bound)
    {
        public string Name => Component.CodeName;

        public string Holds(string property) => Bound.GetValueOrDefault(property, string.Empty);
    }

    private static Peer[] Peers(JsonElement components) => components.EnumerateArray().Select(static c =>
    {
        var bound = c.GetProperty("b").EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value.GetString()!);
        return new Peer(new DesignComponent(c.GetProperty("n").GetString()!, c.GetProperty("t").GetString()!, "f", string.Empty), bound);
    }).ToArray();

    [Fact]
    public void TheWordsAreR2025bs()
    {
        JsonElement words = Oracle("words");
        Assert.Equal(DesignCallbackRules.NothingInCommon, words.GetProperty("multiselect").GetString());
        Assert.Equal(DesignCallbackRules.NoCallbacks, words.GetProperty("noCallbacks").GetString());
        Assert.Equal(DesignCallbackRules.Placeholder, words.GetProperty("placeholder").GetString());
        Assert.Equal(DesignCallbackRules.InvalidNameTitle, words.GetProperty("invalidTitle").GetString());
    }

    [Fact]
    public void TheTableIsR2025bsAndNamesAreEnoughToCompare()
    {
        JsonElement table = Oracle("table");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty c in table.EnumerateObject())
        {
            string[] items = c.Value.EnumerateArray().SelectMany(static g => Words(g.GetProperty("items"))).ToArray();
            string[] labels = items.Select(static i => JsonDocument.Parse(i).RootElement.GetProperty("Label").GetString()!).ToArray();
            Assert.Equal(labels, PropertiesOf(c.Name));
            for (int i = 0; i < items.Length; i++)
            {
                // R2025b compares each item's whole JSON; a property reads the same in every class but the app's row, which stands alone.
                if (c.Name != "App")
                {
                    Assert.Equal(seen.TryGetValue(labels[i], out string? other) ? other : items[i], items[i]);
                    seen[labels[i]] = items[i];
                }
            }
        }
    }

    [Fact]
    public void WhatSeveralHaveInCommonIsR2025bs()
    {
        JsonElement[] scenes = Oracle("common").EnumerateArray().ToArray();
        Assert.Equal(500, scenes.Length);
        Assert.Contains(scenes, static s => s.GetProperty("common").GetArrayLength() > 0 && s.GetProperty("types").GetArrayLength() > 1);
        foreach (JsonElement s in scenes)
        {
            IReadOnlyList<string>[] lists = Words(s.GetProperty("types")).Select(static t => (IReadOnlyList<string>)PropertiesOf(t)).ToArray();
            Assert.Equal(Words(s.GetProperty("common")), DesignCallbackRules.Common(lists));
        }
    }

    // R2025b's doubled group title ("MouseMouse Callbacks") as JGraph words it, and with several selected the properties they do not all have left out.
    private static void SameItems(JsonElement expected, IReadOnlyList<DesignCallbackItem> got, IReadOnlyList<string> common, bool several, bool checkSelect, string scene)
    {
        var want = new List<(string Text, string? Property, bool Disabled, JsonElement? Under)>();
        foreach (JsonElement i in expected.EnumerateArray())
        {
            string text = i.GetProperty("text").GetString()!;
            string? property = i.GetProperty("property").GetString();
            JsonElement? under = i.TryGetProperty("menu", out JsonElement m) ? m : null;
            if (several && under is null && text != DesignCallbackRules.SelectExistingText && !common.Contains(property!))
            {
                continue;
            }

            if (under is { } group && several && !group.EnumerateArray().Any(g => common.Contains(g.GetProperty("property").GetString()!)))
            {
                continue;
            }

            want.Add((text, property, i.GetProperty("disabled").GetBoolean(), under));
        }

        Assert.True(want.Count == got.Count, $"{scene}: {want.Count} items, got {got.Count}");
        for (int k = 0; k < want.Count; k++)
        {
            string text = want[k].Text;
            if (want[k].Under is not null && text.EndsWith(" Callbacks", StringComparison.Ordinal))
            {
                string title = text[..^" Callbacks".Length];
                if (title.Length % 2 == 0 && title[..(title.Length / 2)] == title[(title.Length / 2)..])
                {
                    text = title[..(title.Length / 2)] + " Callbacks";
                }
            }

            Assert.True(text == got[k].Text, $"{scene}: {text} / {got[k].Text}");
            if (want[k].Under is { } under)
            {
                SameItems(under, got[k].Items!, common, several, checkSelect, scene);
            }
            else
            {
                Assert.Equal(want[k].Property is "selectExisting" ? null : want[k].Property, got[k].Property);
                if (got[k].Kind != DesignCallbackItemKind.SelectExisting || checkSelect)
                {
                    Assert.True(want[k].Disabled == got[k].Disabled, $"{scene}: {text} disabled {want[k].Disabled}");
                }
            }
        }
    }

    [Fact]
    public void TheSubmenuIsR2025bs()
    {
        JsonElement[] scenes = Oracle("menus").EnumerateArray().ToArray();
        Assert.Equal(1200, scenes.Length);
        int submenus = 0, clicks = 0;
        for (int n = 0; n < scenes.Length; n++)
        {
            JsonElement s = scenes[n];
            Assert.False(s.TryGetProperty("threw", out _));
            Peer[] peers = Peers(s.GetProperty("components"));
            string startup = s.GetProperty("startup").GetString()!;
            string[] callbacks = Words(s.GetProperty("callbacks"));
            Peer first = peers[0];
            bool app = first.Component.Type == "App";
            IReadOnlyList<DesignCallbackGroup> groups = GroupsOf(first.Component.Type);
            IReadOnlyList<string> common = DesignCallbackRules.Common(peers.Select(static p => PropertiesOf(p.Component.Type)).ToArray());
            bool several = peers.Length > 1;
            string Bound(string p) => p == Startup ? startup : first.Holds(p);
            IReadOnlyList<DesignCallbackItem> got = DesignCallbackRules.SubMenu(groups, common, several, Bound, static _ => false, callbacks.Any(c => c != startup), app);

            JsonElement[] items = s.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(DesignCallbackRules.HelpText, items[^1].GetProperty("text").GetString());
            Assert.True(items.Length == (got.Count > 0 ? 2 : 1), $"scene {n}");
            if (got.Count > 0)
            {
                submenus++;
                Assert.Equal(DesignCallbackRules.MenuText, items[0].GetProperty("text").GetString());

                // Select existing asks of the first group's items, which several selected may leave out here.
                bool checkSelect = !several || groups.Count == 1;
                SameItems(items[0].GetProperty("menu"), got, common, several, checkSelect, $"scene {n}");
            }

            // Each clicked: a new name asked for everyone, named for the first; a go-to.
            string[] names = peers.Select(static p => p.Name).Concat(callbacks).ToArray();
            foreach (JsonElement click in s.GetProperty("clicks").EnumerateArray())
            {
                string text = click.GetProperty("text").GetString()!;
                if (click.GetProperty("edit").ValueKind == JsonValueKind.Object)
                {
                    clicks++;
                    JsonElement edit = click.GetProperty("edit");
                    string property = edit.GetProperty("prop").GetString()!;
                    Assert.Equal(peers.Select(static p => p.Name), Words(edit.GetProperty("peers")));
                    Assert.False(edit.GetProperty("detachPrevious").GetBoolean());
                    string name = app ? DesignCallbacks.Unique("startupFcn", names) : DesignCallbacks.Unique(DesignCallbacks.DefaultName(first.Component, property), names);
                    Assert.Equal(edit.GetProperty("name").GetString(), name);
                }
                else if (click.GetProperty("goTo").ValueKind == JsonValueKind.String)
                {
                    Assert.Equal($"Go to {click.GetProperty("goTo").GetString()} callback", text);
                }
            }
        }

        Assert.True(submenus > 500, $"{submenus} submenus");
        Assert.True(clicks > 500, $"{clicks} clicks");
    }

    [Fact]
    public void AnEditOfSeveralIsR2025bs()
    {
        JsonElement[] scenes = Oracle("edits").EnumerateArray().ToArray();
        Assert.Equal(1000, scenes.Length);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        for (int n = 0; n < scenes.Length; n++)
        {
            JsonElement s = scenes[n];
            Peer[] peers = Peers(s.GetProperty("components"));
            string property = s.GetProperty("property").GetString()!;
            string name = s.GetProperty("name").GetString()!;
            var functions = new HashSet<string>(Words(s.GetProperty("callbacks")), StringComparer.Ordinal);
            var byComponent = peers.ToDictionary(static p => p.Component, static p => p, (IEqualityComparer<DesignComponent>)ReferenceEqualityComparer.Instance);

            // The command goes through them last first, as its constructor reverses the list.
            DesignComponent[] order = peers.Select(static p => p.Component).Reverse().ToArray();
            IReadOnlyList<DesignCallbackStep> steps = DesignCallbackRules.Plan(order, name, s.GetProperty("detachPrevious").GetBoolean(), c => byComponent[c].Holds(property), functions.Contains);
            JsonElement[] want = s.GetProperty("steps").EnumerateArray().ToArray();
            Assert.True(want.Length == steps.Count, $"scene {n}: {want.Length} steps, got {steps.Count}");
            for (int k = 0; k < want.Length; k++)
            {
                string kind = want[k][0].GetString()!;
                kinds.Add(kind);
                Assert.Equal(kind, steps[k].Kind.ToString().ToLowerInvariant());
                Assert.Equal(want[k][2].GetString(), steps[k].From);
                if (kind != "rename")
                {
                    Assert.Equal(want[k][1].GetString(), steps[k].Component.CodeName);
                }

                if (kind is "add" or "attach" or "rename" or "reassign")
                {
                    Assert.Equal(want[k][3].GetString(), steps[k].To);
                }
            }
        }

        Assert.Equal(["add", "attach", "detach", "reassign", "rename"], kinds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ARowOfTheTabIsR2025bs()
    {
        JsonElement[] scenes = Oracle("rows").EnumerateArray().ToArray();
        Assert.Equal(1200, scenes.Length);
        int refusals = 0, edits = 0;
        for (int n = 0; n < scenes.Length; n++)
        {
            JsonElement s = scenes[n];
            Assert.Equal(JsonValueKind.Null, s.GetProperty("threw").ValueKind);
            Peer[] peers = Peers(s.GetProperty("components"));
            Peer anchor = peers[^1];
            string property = s.GetProperty("property").GetString()!;
            string value = s.GetProperty("value").GetString()!;
            string startup = s.GetProperty("startup").GetString()!;
            string[] callbacks = Words(s.GetProperty("callbacks"));
            string current = property == Startup ? startup : anchor.Holds(property);

            (string Name, bool IsStartup)[] functions = callbacks.Select(c => (c, c == startup)).ToArray();
            string[] choices = DesignCallbackRules.Choices(functions, current, property).Select(static c => c.Label + "|" + c.Value).ToArray();
            string[] wantChoices = s.GetProperty("choices").EnumerateArray().Select(static c => c.GetProperty("label").GetString() + "|" + c.GetProperty("value").GetString()).ToArray();
            Assert.Equal(wantChoices, choices);

            string[] inUse = peers.Select(static p => p.Name).Concat(callbacks).ToArray();
            string? why = value == current || value.Length == 0 || value == DesignCallbackRules.UseDefault ? null
                : DesignCallbackRules.WhyNot(property, value, callbacks.Contains(value), startup, v => DesignNames.WhyNotName(v, current, inUse));
            JsonElement result = s.GetProperty("result");
            if (value == current)
            {
                Assert.Equal(JsonValueKind.Null, result.ValueKind);
            }
            else if (result.TryGetProperty("refused", out JsonElement refused))
            {
                refusals++;
                Assert.Equal(refused.GetString(), why);
            }
            else
            {
                edits++;
                Assert.True(why is null, $"scene {n}: refused {value} for {why}");
                Assert.True(result.GetProperty("detachPrevious").GetBoolean());
                Assert.Equal(peers.Select(static p => p.Name), Words(result.GetProperty("peers")));
                string name = value != DesignCallbackRules.UseDefault ? value
                    : property == Startup ? DesignCallbacks.Unique("startupFcn", inUse)
                    : DesignCallbacks.Unique(DesignCallbacks.DefaultName(anchor.Component, property), inUse);
                Assert.Equal(result.GetProperty("name").GetString(), name);
            }
        }

        Assert.True(refusals > 100, $"{refusals} refusals");
        Assert.True(edits > 300, $"{edits} edits");
    }

    [Fact]
    public void WhatSeveralSelectedShowIsR2025bs()
    {
        JsonElement[] scenes = Oracle("multi").EnumerateArray().ToArray();
        Assert.Equal(400, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            Peer[] all = Peers(s.GetProperty("components"));
            var labels = s.GetProperty("labeled").EnumerateArray().Select(static g => g[0].GetString()!).ToHashSet(StringComparer.Ordinal);
            Peer[] peers = all.Where(p => !labels.Contains(p.Name)).ToArray();
            IReadOnlyList<string> common = DesignCallbackRules.Common(peers.Select(static p => PropertiesOf(p.Component.Type)).ToArray());
            Assert.Equal(s.GetProperty("message").GetBoolean(), common.Count == 0);
            if (common.Count > 0)
            {
                JsonElement section = s.GetProperty("section");
                Assert.Equal(Words(section.GetProperty("components")), peers.Select(static p => p.Name));
                Assert.Equal(Words(section.GetProperty("common")), common);
            }
        }
    }

    [Fact]
    public void TheDialogOffersWhatR2025bDoes()
    {
        JsonElement[] scenes = Oracle("dialog").EnumerateArray().ToArray();
        Assert.Equal(300, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            string type = s.GetProperty("type").GetString()!;
            JsonElement bound = s.GetProperty("bound");
            string Holds(string p) => bound.TryGetProperty(p, out JsonElement b) ? b.GetString()! : string.Empty;
            IReadOnlyList<string> properties = PropertiesOf(type);
            Assert.Equal(Words(s.GetProperty("properties")), DesignCallbackRules.Free(properties, properties, Holds));
        }
    }

    [Fact]
    public void WithSeveralTheDialogOffersOnlyWhatTheyAllHaveWhereR2025bBindsWhatTheyDoNot()
    {
        IReadOnlyList<string> dropDown = Labels("matlab.ui.control.DropDown");
        IReadOnlyList<string> slider = Labels("matlab.ui.control.Slider");
        IReadOnlyList<string> common = DesignCallbackRules.Common([dropDown, slider]);
        Assert.Equal(["ValueChangedFcn"], common);
        Assert.Equal(["ValueChangedFcn"], DesignCallbackRules.Free(dropDown, common, static _ => string.Empty));
    }
}
