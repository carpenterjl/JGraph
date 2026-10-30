using System.Text.Json;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W5b: which place of a component with several a key, Tab and
/// Shift+Tab open, what it edits and how its text aligns, against R2025b's own controllers'
/// <c>_getEditableHotspotIndex</c> and <c>getEditorOptions</c>, run as App Designer's client has them
/// by <c>ui-probes/w5/oracle/inplace.js</c> over every orientation and scale direction
/// (DesignInPlaceOracle.json).
/// </summary>
public class DesignInPlaceOracleTests
{
    [Fact]
    public void HotspotsAreChosenAndAlignedAsR2025bChoosesThem()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignInPlaceOracle.json")));
        JsonElement[] scenes = oracle.RootElement.EnumerateArray().ToArray();
        Assert.Equal(176, scenes.Length);
        foreach (JsonElement s in scenes)
        {
            string type = s.GetProperty("type").GetString()!;
            int count = s.GetProperty("hotspots").GetInt32();
            string orientation = s.GetProperty("orientation").GetString()!;
            string direction = s.GetProperty("scaleDirection").GetString()!;
            string way = s.GetProperty("way").GetString()!;
            int from = s.GetProperty("from").GetInt32();
            int expected = s.GetProperty("hotspot").GetInt32();
            string scene = $"{type} {orientation} {direction} of {count}: {way} from {from}";

            int hotspot = way == "key"
                ? DesignInPlace.DefaultHotspot(type, orientation, direction)
                : DesignInPlace.NextHotspot(from, way == "previous", 0, count - 1);
            Assert.True(expected == hotspot, $"{scene}: R2025b opens {expected}, JGraph {hotspot}");

            string? property = s.GetProperty("property").GetString();
            string ours = DesignInPlace.EditsLimits(type) ? (hotspot == 0 ? "Minimum" : "Maximum")
                : type == "matlab.ui.control.Table" ? "ColumnName" : "Items";
            Assert.True(property == ours, $"{scene}: R2025b edits {property}, JGraph {ours}");

            string? alignment = s.GetProperty("alignment").GetString();
            string aligned = DesignInPlace.HotspotAlignment(type, hotspot, count, orientation, direction);
            Assert.True(alignment == aligned, $"{scene}: R2025b aligns {alignment}, JGraph {aligned}");
        }
    }
}
