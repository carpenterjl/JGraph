using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W5e: what each class can be replaced with, where the new one goes,
/// how values and limits carry over and what it and its label are called, against R2025b's own
/// <c>CompReplacementUtil</c>, <c>ComponentDefaultsProduction</c>, family replacers and
/// <c>ReplaceComponentCommand</c>, run as App Designer's client has them by <c>ui-probes/w5/oracle/replace.js</c>
/// (DesignReplacementOracle.json).
/// </summary>
public class DesignReplacementOracleTests
{
    private static readonly JsonElement Oracle = Load();

    private static JsonElement Load()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignReplacementOracle.json")));
        return oracle.RootElement.Clone();
    }

    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.String
        ? e.GetString() switch
        {
            "Inf" => double.PositiveInfinity,
            "-Inf" => double.NegativeInfinity,
            _ => throw new InvalidDataException(e.GetString()),
        }
        : e.GetDouble();

    private static double[] Numbers(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(Number).ToArray() : [Number(e)];

    private static string? Text(JsonElement record, string name)
    {
        if (!record.TryGetProperty(name, out JsonElement e))
        {
            return null;
        }

        return e.ValueKind == JsonValueKind.Array ? string.Join(",", e.EnumerateArray().Select(static i => i.GetString())) : e.GetString();
    }

    private static DesignClassBox BoxOf(string type)
    {
        JsonElement d = Oracle.GetProperty("defaults").GetProperty(type);
        double[] location = Numbers(d.GetProperty("Location"));
        double[] outerLocation = Numbers(d.GetProperty("OuterLocation"));
        double[] size = Numbers(d.GetProperty("Size"));
        double[] outer = Numbers(d.GetProperty("OuterSize"));
        return new DesignClassBox(location[0] - outerLocation[0], location[1] - outerLocation[1], new Size2D(size[0], size[1]), new Size2D(outer[0], outer[1]));
    }

    private static DesignLabelPlace PlaceOf(string type) =>
        Oracle.GetProperty("places").TryGetProperty(type, out JsonElement p) ? Enum.Parse<DesignLabelPlace>(p.GetString()!) : DesignLabelPlace.None;

    [Fact]
    public void WhatEachClassIsOfferedIsR2025bs()
    {
        int families = 0;
        foreach (JsonElement f in Oracle.GetProperty("families").EnumerateArray())
        {
            string type = f.GetProperty("type").GetString()!;
            string family = f.GetProperty("family").GetString()!;
            string[] types = f.GetProperty("offered").EnumerateArray().Select(static e => e.GetString()!).ToArray();
            string[] names = f.GetProperty("offeredNames").EnumerateArray().Select(static e => e.GetString()!).ToArray();
            if (family == "NONE")
            {
                Assert.Null(DesignReplacement.FamilyOf(type));
                Assert.Empty(DesignReplacement.Offered(type));
                continue;
            }

            families++;
            Assert.Equal(family, DesignReplacement.FamilyOf(type));
            Assert.Equal(types, DesignReplacement.Offered(type).Select(static o => o.Type));
            Assert.Equal(names, DesignReplacement.Offered(type).Select(static o => o.Name));
        }

        Assert.Equal(25, families);
    }

    [Fact]
    public void TheMenuIsShownForWhatR2025bShowsItFor()
    {
        foreach (JsonElement m in Oracle.GetProperty("menu").EnumerateArray())
        {
            string selection = m.GetProperty("selection").GetString()!;
            if (selection.StartsWith("matlab.", StringComparison.Ordinal))
            {
                Assert.Equal(m.GetProperty("shown").GetBoolean(), DesignReplacement.FamilyOf(selection) is not null);
            }
        }
    }

    [Fact]
    public void ANewOnesBoxIsR2025bs()
    {
        foreach (JsonElement f in Oracle.GetProperty("families").EnumerateArray())
        {
            string type = f.GetProperty("type").GetString()!;
            if (f.GetProperty("family").GetString() is "NONE" or "TOOL")
            {
                continue;
            }

            Assert.Equal(BoxOf(type), DesignPalette.R2025b.BoxOf(type));
            Assert.Equal(PlaceOf(type), DesignPalette.R2025b.LabelPlaceOf(type));
        }
    }

    [Fact]
    public void WhereItGoesAndItsSizeAreR2025bs()
    {
        int checkedPlaces = 0;
        foreach (JsonElement r in Oracle.GetProperty("conversions").EnumerateArray())
        {
            string source = r.GetProperty("source").GetString()!;
            string target = r.GetProperty("target").GetString()!;
            JsonElement given = r.GetProperty("in");
            if (!given.TryGetProperty("OuterLocation", out JsonElement outerLocation))
            {
                continue; // a tool
            }

            JsonElement made = r.GetProperty("out");
            double[] location = Numbers(outerLocation);
            double[] outerSize = Numbers(given.GetProperty("OuterSize"));
            double[] size = Numbers(given.GetProperty("Size"));
            bool labelled = r.GetProperty("labelled").GetBoolean();
            DesignClassBox box = BoxOf(target);
            Size2D ours = DesignReplacement.SizeOf(source, target, new Size2D(size[0], size[1]), box);
            Point2D at = DesignReplacement.LocationOf(target, labelled ? PlaceOf(source) : DesignLabelPlace.None, new Rect2D(location[0], location[1], outerSize[0], outerSize[1]), ours, box);

            double[] wantSize = Numbers(made.GetProperty("Size"));
            Assert.Equal(wantSize[0], ours.Width, 9);
            Assert.Equal(wantSize[1], ours.Height, 9);

            // R2025b's client draws from Location. For a spinner and a numeric field that keep their own height
            // beside a label it takes a new one's height in moving it and so moves it (recorded as a divergence):
            // its Position, which the client does not read, has the place kept.
            bool keepsHeight = labelled && source is "matlab.ui.control.Spinner" or "matlab.ui.control.NumericEditField" && target is "matlab.ui.control.Spinner" or "matlab.ui.control.NumericEditField";
            double[] want = Numbers(made.GetProperty(keepsHeight ? "Position" : "Location"));
            Assert.True(want[0] == at.X && want[1] == at.Y, $"{source} to {target}{(labelled ? " labelled" : string.Empty)}: R2025b [{want[0]} {want[1]}], JGraph [{at.X} {at.Y}]");
            checkedPlaces++;
        }

        Assert.True(checkedPlaces > 500, checkedPlaces.ToString());
    }

    [Fact]
    public void LimitsAndValuesCarryOverAsR2025bs()
    {
        int numeric = 0, text = 0;
        foreach (JsonElement r in Oracle.GetProperty("conversions").EnumerateArray())
        {
            string source = r.GetProperty("source").GetString()!;
            string target = r.GetProperty("target").GetString()!;
            JsonElement given = r.GetProperty("in");
            JsonElement made = r.GetProperty("out");
            string? family = DesignReplacement.FamilyOf(target);
            if (family == "NUMERIC")
            {
                double[] value = Numbers(given.GetProperty("Value").ValueKind == JsonValueKind.String && source == "matlab.ui.control.EditField" ? JsonDocument.Parse("0").RootElement : given.GetProperty("Value"));
                (double Low, double High) targetLimits = Pair(TargetDefaults(target, "Limits"));
                double[] targetValue = TargetDefaults(target, "Value");
                if (double.IsInfinity(value[0]) && target == "matlab.ui.control.RangeSlider")
                {
                    continue; // R2025b nests the pair, and its limits come out NaN: recorded as a divergence
                }

                (double Low, double High) limits = targetLimits;
                if (given.TryGetProperty("Limits", out JsonElement l))
                {
                    limits = DesignReplacement.LimitsOf(Pair(Numbers(l)), value[0], targetLimits, targetValue[0]);
                    Assert.Equal(Pair(Numbers(made.GetProperty("Limits"))), limits);
                }

                Assert.Equal(Numbers(made.GetProperty("Value")), DesignReplacement.NumericValueOf(source, target, value, targetLimits, targetValue, limits.High));
                numeric++;
            }
            else if (family == "TEXTUAL_INPUT")
            {
                JsonElement v = given.GetProperty("Value");
                string[] lines = v.ValueKind == JsonValueKind.Array
                    ? v.EnumerateArray().Select(static e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()).ToArray()
                    : [v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()];
                JsonElement w = made.GetProperty("Value");
                string[] want = w.ValueKind == JsonValueKind.Array ? w.EnumerateArray().Select(static e => e.GetString()!).ToArray() : [w.GetString()!];

                // R2025b wraps an edit field's text in a list for a text area; a list of one line is that line here.
                if (want is [var nested] && w.ValueKind == JsonValueKind.Array && w[0].ValueKind != JsonValueKind.String)
                {
                    want = [nested];
                }

                Assert.Equal(want, DesignReplacement.TextValueOf(source, target, lines));
                text++;
            }
        }

        Assert.True(numeric > 100 && text > 20, $"{numeric} {text}");

        static (double Low, double High) Pair(double[] n) => (n[0], n[1]);
    }

    private static double[] TargetDefaults(string type, string name) => name switch
    {
        "Limits" => type is "matlab.ui.control.Spinner" or "matlab.ui.control.NumericEditField" ? [double.NegativeInfinity, double.PositiveInfinity] : [0, 100],
        _ => type == "matlab.ui.control.RangeSlider" ? [20, 60] : [0],
    };

    [Fact]
    public void WhatItAndItsLabelAreCalledIsR2025bs()
    {
        int scenes = 0, failing = 0;
        foreach (JsonElement r in Oracle.GetProperty("names").EnumerateArray())
        {
            string source = r.GetProperty("source").GetString()!;
            string target = r.GetProperty("target").GetString()!;
            bool labelled = r.GetProperty("labelled").GetBoolean();
            string codeName = r.GetProperty("codeName").GetString()!;
            string? labelCode = Text(r, "labelCode");
            string? labelText = Text(r, "labelText");
            string? ownText = Text(r, "ownText");
            List<string> codes = ["UIFigure", codeName];
            List<string> texts = ["MATLAB App"];
            if (labelCode is not null)
            {
                codes.Add(labelCode);
            }

            if (labelled)
            {
                texts.Add(labelText!);
            }

            if (ownText is not null)
            {
                texts.Add(ownText);
            }

            foreach (JsonElement other in r.GetProperty("others").EnumerateArray())
            {
                codes.Add(other[0].GetString()!);
                texts.Add(other[1].GetString()!);
            }

            string? targetText = r.TryGetProperty("out", out JsonElement made) ? Text(made, "text") : null;
            (string name, string? label, string? newText) = DesignReplacement.NamesOf(source, target, labelled, codeName, labelCode, labelled ? labelText : ownText, targetText, codes, texts);
            if (!r.TryGetProperty("out", out _))
            {
                // R2025b fails on these: here the names it would have kept are kept.
                Assert.Equal(labelCode, label);
                failing++;
                continue;
            }

            string what = $"{source} to {target}, {codeName}, label {labelCode} '{labelText}', own '{ownText}'";
            Assert.True(made.GetProperty("codeName").GetString() == name, $"{what}: R2025b {made.GetProperty("codeName").GetString()}, JGraph {name}");
            if (labelled)
            {
                Assert.True(Text(made, "labelCode") == label, $"{what}: R2025b's label {Text(made, "labelCode")}, JGraph {label}");
                Assert.True(Text(made, "labelText") == (newText ?? labelText), $"{what}: R2025b's text {Text(made, "labelText")}, JGraph {newText ?? labelText}");
            }

            scenes++;
        }

        Assert.True(scenes > 700, scenes.ToString());
        Assert.True(failing > 20, failing.ToString());
    }
}
