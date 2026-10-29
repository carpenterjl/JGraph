using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W4: <see cref="DesignArrange"/> against R2025b's own
/// <c>Alignment</c>, <c>SameSize</c> and <c>Spacing</c> modules, run unmodified over 3,600 seeded
/// scenes by <c>ui-probes/w4/oracle/arrange.js</c> (DesignArrangeOracle.json): each scene's boxes after
/// the command, to the bit, and whether it acted.
/// </summary>
public class DesignArrangeOracleTests
{
    private static JsonElement[] Cases(string op)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignArrangeOracle.json")));
        return oracle.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("op").GetString() == op).Select(static c => c.Clone()).ToArray();
    }

    private static double[][] Boxes(JsonElement c, string name) =>
        c.GetProperty(name).EnumerateArray().Select(static b => b.EnumerateArray().Select(static v => v.GetDouble()).ToArray()).ToArray();

    private static void SameBoxes(double[][] expected, IReadOnlyList<DesignArrangeItem> items, int scene)
    {
        Assert.Equal(expected.Length, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            double[] got = [items[i].X, items[i].Y, items[i].Width, items[i].Height];
            Assert.True(expected[i].SequenceEqual(got), $"scene {scene}, item {i}: R2025b [{string.Join(' ', expected[i])}], JGraph [{string.Join(' ', got)}]");
        }
    }

    [Fact]
    public void AlignIsR2025bsAlignment()
    {
        JsonElement[] cases = Cases("align");
        Assert.Equal(1200, cases.Length);
        for (int n = 0; n < cases.Length; n++)
        {
            JsonElement c = cases[n];
            DesignArrangeItem[] items = Boxes(c, "boxes").Select(static b => new DesignArrangeItem(b[0], b[1], b[2], b[3])).ToArray();
            int anchor = c.GetProperty("anchor").GetInt32();
            bool done = DesignArrange.Align((DesignAlign)c.GetProperty("kind").GetInt32(), items, anchor < 0 ? null : items[anchor]);
            Assert.Equal(c.GetProperty("done").GetBoolean(), done);
            SameBoxes(Boxes(c, "after"), items, n);
        }
    }

    [Fact]
    public void SameSizeIsR2025bsSameSize()
    {
        JsonElement[] cases = Cases("samesize");
        Assert.Equal(1200, cases.Length);
        for (int n = 0; n < cases.Length; n++)
        {
            JsonElement c = cases[n];
            bool[][] fixedSides = c.GetProperty("fixed").EnumerateArray().Select(static f => f.EnumerateArray().Select(static v => v.GetBoolean()).ToArray()).ToArray();
            bool[] ratio = c.GetProperty("ratio").EnumerateArray().Select(static v => v.GetBoolean()).ToArray();
            double[][] min = Boxes(c, "min");
            DesignArrangeItem[] items = Boxes(c, "boxes").Select((b, i) => new DesignArrangeItem(b[0], b[1], b[2], b[3])
            {
                FixedWidth = fixedSides[i][0],
                FixedHeight = fixedSides[i][1],
                KeepsRatio = ratio[i],
                Minimum = (min[i][0], min[i][1]),
            }).ToArray();
            int anchor = c.GetProperty("anchor").GetInt32();
            string kind = c.GetProperty("kind").GetString()!.ToLowerInvariant();
            bool done = kind switch
            {
                "size" => DesignArrange.SameSize(DesignSameSize.Size, items, anchor < 0 ? null : items[anchor]),
                "width" => DesignArrange.SameSize(DesignSameSize.Width, items, anchor < 0 ? null : items[anchor]),
                "height" => DesignArrange.SameSize(DesignSameSize.Height, items, anchor < 0 ? null : items[anchor]),
                _ => false, // R2025b's makeSameSize refuses a kind it does not know
            };
            Assert.Equal(c.GetProperty("done").GetBoolean() && items.Length > 1, done);
            SameBoxes(Boxes(c, "after"), items, n);
        }
    }

    [Fact]
    public void SpaceIsR2025bsSpacing()
    {
        JsonElement[] cases = Cases("space");
        Assert.Equal(1200, cases.Length);
        for (int n = 0; n < cases.Length; n++)
        {
            JsonElement c = cases[n];
            int[] z = c.GetProperty("z").EnumerateArray().Select(static v => v.GetInt32()).ToArray();
            DesignArrangeItem[] items = Boxes(c, "boxes").Select((b, i) => new DesignArrangeItem(b[0], b[1], b[2], b[3]) { Z = z[i] }).ToArray();
            double gap = c.GetProperty("gap").GetDouble();
            bool changed = c.GetProperty("kind").GetString() switch
            {
                "horiz-auto" => DesignArrange.Space(horizontal: true, null, items),
                "vert-auto" => DesignArrange.Space(horizontal: false, null, items),
                "horiz-manual" => DesignArrange.Space(horizontal: true, gap, items),
                "vert-manual" => DesignArrange.Space(horizontal: false, gap, items),
                _ => false,
            };
            Assert.Equal(c.GetProperty("changed").GetBoolean(), changed);
            SameBoxes(Boxes(c, "after"), items, n);
        }
    }
}
