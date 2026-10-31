using System.Text.Json;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Toolstrip;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W8b: an app's argument sets and Run's drop-down, against R2025b's own <c>RunArgumentsModel</c> and <c>RunSection</c>, run by
/// <c>ui-probes/w8/oracle/run.js</c> (DesignRunArgumentsOracle.json).
/// </summary>
public class DesignRunArgumentsOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignRunArgumentsOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static string[] Words(JsonElement array) => array.EnumerateArray().Select(static e => e.GetString()!).ToArray();

    [Fact]
    public void TheSetsGoAsRunArgumentsModelTakesThem()
    {
        foreach (JsonElement run in Oracle("runs").EnumerateArray())
        {
            var model = new DesignRunArguments(Words(run.GetProperty("start")));
            foreach (JsonElement step in run.GetProperty("ops").EnumerateArray())
            {
                string set = step.GetProperty("set").GetString()!;
                if (step.GetProperty("op").GetString() == "use")
                {
                    Assert.Equal(step.GetProperty("changed").GetBoolean(), model.Use(set));
                }
                else
                {
                    model.Remove(set);
                }

                Assert.Equal(Words(step.GetProperty("all")), model.All);
                Assert.Equal(step.GetProperty("recent").GetString(), model.MostRecent);
            }
        }
    }

    [Fact]
    public void TheDropDownIsWhatBuildPopupListMakes()
    {
        foreach (JsonElement menu in Oracle("menus").EnumerateArray())
        {
            string name = menu.GetProperty("name").GetString()!;
            string[] held = Words(menu.GetProperty("held"));
            string parameters = menu.GetProperty("parameters").GetString()!;
            string? error = menu.GetProperty("error").GetString();
            IReadOnlyList<ToolstripPopupEntry> entries = DesignToolstrip.RunEntries(name, held, parameters, error, enabled: true);
            JsonElement[] made = menu.GetProperty("created").EnumerateArray().ToArray();
            Assert.Equal(made.Length, entries.Count);
            for (int i = 0; i < made.Length; i++)
            {
                JsonElement expected = made[i];
                ToolstripPopupEntry entry = entries[i];
                switch (expected.GetProperty("type").GetString())
                {
                    case "item" when expected.TryGetProperty("action", out JsonElement action):
                        Assert.Equal(action.GetString(), entry.ActionId);
                        break;
                    case "item":
                        Assert.Equal(expected.GetProperty("text").GetString(), entry.Text);
                        Assert.Equal(expected.GetProperty("removable").GetBoolean(), entry.Removable);
                        break;
                    case "field":
                        Assert.Equal(ToolstripPopupEntryKind.FieldItem, entry.Kind);
                        Assert.Equal(expected.GetProperty("label").GetString(), entry.Text);
                        Assert.Equal(expected.GetProperty("placeholder").GetString(), entry.Placeholder);
                        Assert.Equal(expected.GetProperty("enabled").GetBoolean(), entry.IsEnabled);
                        if (error is not null)
                        {
                            Assert.Equal(error, entry.ErrorText);
                            Assert.Equal(expected.GetProperty("text").GetString(), entry.FieldText);
                        }

                        break;
                    default:
                        throw new InvalidOperationException(expected.GetProperty("type").GetString());
                }
            }
        }
    }
}
