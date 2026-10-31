using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W8c: a converted app's name, the regions' shares and boxes, which region a component goes to, and a box brought inside its parent, against
/// R2025b's own <c>AppDataCopyUtil</c> and <c>PositionUtil</c>, run by <c>ui-probes/w8/oracle/convert.js</c> (DesignConversionOracle.json).
/// </summary>
public class DesignConversionOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignConversionOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static double[] Numbers(JsonElement array) => array.EnumerateArray().Select(static e => e.GetDouble()).ToArray();

    [Fact]
    public void ANameIsMadeAsCreateAppNameMakesIt()
    {
        foreach (JsonElement row in Oracle("names").EnumerateArray())
        {
            string[] open = row.GetProperty("open").EnumerateArray().Select(static e => e.GetString()!).ToArray();
            Assert.Equal(row.GetProperty("made").GetString(), DesignConversion.ConvertedName(row.GetProperty("name").GetString()!, open));
        }
    }

    [Fact]
    public void TheSharesAreTheTemplates()
    {
        JsonElement ratios = Oracle("ratios");
        Assert.Equal(Numbers(ratios.GetProperty("two")), DesignConversion.Ratios(2));
        Assert.Equal(Numbers(ratios.GetProperty("three")), DesignConversion.Ratios(3));
    }

    [Fact]
    public void TheRegionsBoxesAreResizeRegionPanels()
    {
        foreach (JsonElement row in Oracle("boxes").EnumerateArray())
        {
            int panels = row.GetProperty("kind").GetString() == "two" ? 2 : 3;
            double[] size = Numbers(row.GetProperty("size"));
            double[][] expected = row.GetProperty("boxes").EnumerateArray().Select(Numbers).ToArray();
            IReadOnlyList<(double X, double Width)> boxes = DesignConversion.RegionBoxes(DesignConversion.Ratios(panels), size[0]);
            Assert.Equal(expected.Length, boxes.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i][0], boxes[i].X);
                Assert.Equal(expected[i][1], boxes[i].Width);
            }
        }
    }

    [Fact]
    public void WhichRegionIsGetComponentsWithinBounds()
    {
        foreach (JsonElement row in Oracle("within").EnumerateArray())
        {
            double[] box = Numbers(row.GetProperty("box"));
            Assert.Equal(row.GetProperty("found").GetBoolean(), DesignConversion.InRegion(box[0], box[2], row.GetProperty("width").GetDouble(), row.GetProperty("a").GetDouble(), row.GetProperty("b").GetDouble()));
        }
    }

    [Fact]
    public void ABoxIsClampedAsClampToParentDoes()
    {
        foreach (JsonElement row in Oracle("clamps").EnumerateArray())
        {
            double[] at = Numbers(row.GetProperty("at"));
            double[] size = Numbers(row.GetProperty("size"));
            double[] parent = Numbers(row.GetProperty("parent"));
            double[] expected = Numbers(row.GetProperty("position"));
            (double x, double y, double w, double h) = DesignConversion.Clamp(at[0], at[1], size[0], size[1], parent[0], parent[1]);
            Assert.Equal(expected, new[] { x, y, w, h });
        }
    }
}
