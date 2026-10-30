using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W5c: which ways a selection moves among its siblings and
/// where each one goes - Move's First, back one, on one and Last, a tab heading's drag, and the stack
/// order's four - and where a radio or toggle button added to a group goes, against R2025b's own
/// <c>ReorderAction</c>, <c>ReorderCommand</c> and <c>MutualExclusiveButtonUtil</c>, run as App
/// Designer's client has them by <c>ui-probes/w5/oracle/structure.js</c> (DesignStructureOracle.json).
/// </summary>
public class DesignStructureOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignStructureOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    [Fact]
    public void MovesAreAllowedAndMadeAsR2025bMakesThem()
    {
        JsonElement[] scenes = Oracle("moves").EnumerateArray().ToArray();
        Assert.Equal(900, scenes.Length);
        int made = 0;
        foreach (JsonElement s in scenes)
        {
            int count = s.GetProperty("count").GetInt32();
            int[] selected = s.GetProperty("selected").EnumerateArray().Select(static e => e.GetInt32()).ToArray();
            bool stack = s.GetProperty("stack").GetBoolean();
            string how = s.GetProperty("how").GetString()!;
            bool[] can = s.GetProperty("can").EnumerateArray().Select(static e => e.GetBoolean()).ToArray();
            string scene = $"{(stack ? "stack" : "move")} {how} of [{string.Join(",", selected)}] among {count}";

            var ours = DesignStructure.Ability(count, selected, stack);
            Assert.True(can.SequenceEqual([ours.First, ours.Previous, ours.Next, ours.Last, ours.Offset]),
                $"{scene}: R2025b allows [{string.Join(",", can)}], JGraph [{ours.First},{ours.Previous},{ours.Next},{ours.Last},{ours.Offset}]");

            if (s.GetProperty("after") is { ValueKind: JsonValueKind.Array } after)
            {
                DesignMove? move = how switch
                {
                    "FIRST" => DesignMove.First,
                    "PREVIOUS" => DesignMove.Previous,
                    "NEXT" => DesignMove.Next,
                    "LAST" => DesignMove.Last,
                    _ => null,
                };
                int? drop = s.GetProperty("drop") is { ValueKind: JsonValueKind.Number } d ? d.GetInt32() : null;
                int[] expected = after.EnumerateArray().Select(static e => e.GetInt32()).ToArray();
                int[] order = DesignStructure.Order(count, selected, move, drop);
                Assert.True(expected.SequenceEqual(order), $"{scene} (drop {drop}): R2025b [{string.Join(",", expected)}], JGraph [{string.Join(",", order)}]");
                made++;
            }
        }

        // The scenes reach every way, a drag of several and the stack order of one.
        Assert.True(made > 400, $"only {made} moves made");
    }

    [Fact]
    public void ButtonsGoWhereR2025bPutsThem()
    {
        JsonElement[] scenes = Oracle("buttons").EnumerateArray().ToArray();
        Assert.Equal(300, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            bool toggle = s.GetProperty("toggle").GetBoolean();
            double height = s.GetProperty("height").GetDouble();
            double[] bottoms = s.GetProperty("bottoms").EnumerateArray().Select(static e => e.GetDouble()).ToArray();
            double[] location = s.GetProperty("location").EnumerateArray().Select(static e => e.GetDouble()).ToArray();
            double[] size = s.GetProperty("size").EnumerateArray().Select(static e => e.GetDouble()).ToArray();
            bool value = s.GetProperty("value").GetBoolean();
            string scene = $"{(toggle ? "toggle" : "radio")} in {height} over [{string.Join(",", bottoms)}]";

            (double x, double y, bool selected) = DesignStructure.ButtonPlace(toggle, height, bottoms);
            Assert.True(location[0] == x && location[1] == y, $"{scene}: R2025b at ({location[0]}, {location[1]}), JGraph ({x}, {y})");
            Assert.True(value == selected, $"{scene}: R2025b's Value {value}, JGraph's {selected}");
            (double w, double h) = toggle ? DesignStructure.ToggleButtonSize : DesignStructure.RadioButtonSize;
            Assert.Equal([w, h], size);
        }
    }

    [Fact]
    public void ATabHeadingDropsBeforeTheFirstCentrePastThePointer()
    {
        // Three headings centred at 30, 90 and 150 (TabDragController's thumb centres).
        double[] centres = [30, 90, 150];

        // The first dragged between the second and third goes second; to the end, last.
        Assert.Equal((2, false, (int?)1), DesignStructure.TabDrop(centres, 120, start: 0));
        Assert.Equal((2, true, (int?)2), DesignStructure.TabDrop(centres, 170, start: 0));

        // The last dragged before the first goes first; dropped by itself it stays.
        Assert.Equal((0, false, (int?)0), DesignStructure.TabDrop(centres, 10, start: 2));
        Assert.Equal((2, false, (int?)null), DesignStructure.TabDrop(centres, 100, start: 2));
        Assert.Equal((1, false, (int?)null), DesignStructure.TabDrop(centres, 40, start: 0));
    }

    [Fact]
    public void AButtonsTextSizesItAsTheRadioButtonGroupsAre()
    {
        // The Radio Button Group entry's Button and Button2 are made 58 and 65 wide, 22 high.
        Assert.Equal((58.0, 22.0), DesignStructure.ButtonPreferredSize(toggle: false, "Button"));
        Assert.Equal((65.0, 22.0), DesignStructure.ButtonPreferredSize(toggle: false, "Button2"));

        // A toggle button keeps the width it is made at for a short text.
        Assert.Equal((100.0, 22.0), DesignStructure.ButtonPreferredSize(toggle: true, "Button4"));
    }
}
