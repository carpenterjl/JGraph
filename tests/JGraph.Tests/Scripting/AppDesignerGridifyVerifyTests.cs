using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// An independent check of DS4's <see cref="DesignGridify"/> against App Designer R2025b's own
/// <c>appdesigner/gridify</c> and <c>ComponentAutoLayoutInfo</c> modules, run unaltered under node on
/// scenes the golden set does not hold (<c>gridify-verify.jsonl</c>, <c>gridify-verify-growth.jsonl</c>):
/// exact ties, zero-sized components, components flush with the container's edges, wholly outside it
/// or larger than it, up to 30 components, arbitrary doubles, coordinates from 1e-3 to 1e6 pixels, a
/// lone component, gaps at the tolerances to a hair, custom, zero and negative padding and spacing,
/// odd zooms, empty containers of every size, negative sizes, and the scenes App Designer's own code
/// fails on. The container's page rect stands at the origin, so the rects App Designer reads are the
/// very doubles the port forms from each box. The 1,136 scenes kept here are from a run of about
/// 258,000 (and 30,800 trees) in which the port and App Designer did not differ once.
/// </summary>
public class AppDesignerGridifyVerifyTests(ITestOutputHelper output)
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    /// <summary>
    /// Every scene gives App Designer's rows and columns (weights as text, pixels to the bit), spacing,
    /// padding and every component's cells; where App Designer's code throws, the port throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void EveryVerificationSceneGivesAppDesignersOwnGrid()
    {
        (int scenes, int failing, List<string> failures) = CheckScenes(Fixture("gridify-verify.jsonl"));
        output.WriteLine($"{scenes} scenes ({failing} App Designer fails on), {failures.Count} differences");
        Assert.True(scenes >= 1000, $"only {scenes} scenes");
        Assert.True(failing > 0, "no scene App Designer fails on");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(50)));
    }

    /// <summary>
    /// A zero-height component below a 2-pixel container leaves two empty rows; the overflow drops the
    /// last and then the first, where App Designer reads <c>u[1].start</c> of a one-track list and
    /// fails. The port fails there as well, with <see cref="InvalidOperationException"/> like every
    /// other scene App Designer's code fails on, not with the list's index exception.
    /// </summary>
    [Fact]
    public void DroppingTheFirstOfTwoEmptyTracksFailsAsAppDesignerDoes()
    {
        double[] padding = [10, 10, 10, 10];
        Assert.Throws<InvalidOperationException>(() => DesignGridify.Gridify(
            new Size2D(100, 2), [new GridifyComponent("A", new Rect2D(10, 4, 50, 0), false, false)], padding, 10, 10));
        Assert.Throws<InvalidOperationException>(() => DesignGridify.Gridify(
            new Size2D(2, 100), [new GridifyComponent("A", new Rect2D(4, 10, 0, 50), false, false)], padding, 10, 10));
    }

    /// <summary>
    /// Every component tree - each class of App Designer's tables and the catalog, sliders and linear
    /// gauges either way, grids with and without weights, panels, button groups, tab groups and tabs
    /// nested with children partly or wholly outside - grows as App Designer's own
    /// <c>ComponentAutoLayoutInfo.shouldComponentGrow</c> says.
    /// </summary>
    [Fact]
    public void EveryVerificationTreeGrowsAsAppDesignerSays()
    {
        (int trees, List<string> failures) = CheckTrees(Fixture("gridify-verify-growth.jsonl"));
        output.WriteLine($"{trees} trees, {failures.Count} differences");
        Assert.True(trees >= 500, $"only {trees} trees");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(50)));
    }

    /// <summary>
    /// A weight's number is written as JavaScript writes it (<c>b + "x"</c>): for 1,700 numbers - two
    /// decimals up to 1e10, arbitrary doubles from 1e-300 to 1e300, the plain-or-exponent thresholds
    /// 1e-6 and 1e21, the smallest and largest doubles - the text is node's own. A finite number's
    /// JSON token is <c>Number::toString</c>, so the file's tokens are the expected texts.
    /// </summary>
    [Fact]
    public void EveryNumberIsWrittenAsNodeWritesIt()
    {
        using JsonDocument numbers = JsonDocument.Parse(File.ReadAllText(Fixture("gridify-verify-numbers.json")));
        var failures = new List<string>();
        int count = 0;
        foreach (JsonElement number in numbers.RootElement.EnumerateArray())
        {
            count++;
            string text = DesignGridify.NumberText(number.GetDouble());
            if (text != number.GetRawText())
            {
                failures.Add($"{number.GetRawText()} is written {text}");
            }
        }

        Assert.True(count > 1500, $"only {count} numbers");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(50)));
    }

    /// <summary>Checks every tree of a JSON-lines file written by the verifier's <c>gen_growth.js</c>.</summary>
    internal static (int Trees, List<string> Failures) CheckTrees(string path)
    {
        var failures = new List<string>();
        int trees = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument json = JsonDocument.Parse(line);
            JsonElement tree = json.RootElement;
            trees++;
            DesignComponent component = Build(tree.GetProperty("tree"), "Root");
            (bool, bool) expected = (tree.GetProperty("horizontal").GetBoolean(), tree.GetProperty("vertical").GetBoolean());
            (bool, bool) actual = DesignGridify.Growth(component);
            if (expected != actual)
            {
                failures.Add($"{tree.GetProperty("name").GetString()}: grows {actual}, App Designer's {expected}: {tree.GetProperty("tree").GetRawText()}");
            }
        }

        return (trees, failures);
    }

    private static DesignComponent Build(JsonElement node, string name)
    {
        string type = node.GetProperty("type").GetString()!;
        DesignComponentClass? known = DesignCatalog.R2025b.ByType(type);
        DesignComponent component = known is null ? new DesignComponent(name, type, "uicomponent", string.Empty) : new DesignComponent(name, known);
        if (node.TryGetProperty("position", out JsonElement position))
        {
            component.Set("Position", NumericValue.Row(position.EnumerateArray().Select(static p => p.GetDouble()).ToArray()));
        }

        if (node.TryGetProperty("orientation", out JsonElement orientation))
        {
            component.Set("Orientation", new CharValue(orientation.GetString()!));
        }

        foreach (string tracks in new[] { "ColumnWidth", "RowHeight" })
        {
            if (node.TryGetProperty(tracks, out JsonElement entries))
            {
                component.Set(tracks, CellValue.Row(entries.EnumerateArray().Select(static e => e.ValueKind == JsonValueKind.String
                    ? (DesignValue)new CharValue(e.GetString()!)
                    : NumericValue.Scalar(e.GetDouble())).ToArray()));
            }
        }

        int k = 0;
        if (node.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                component.Add(Build(child, name + "_" + k++));
            }
        }

        return component;
    }

    /// <summary>Checks every scene of a JSON-lines file written by the verifier's <c>gen_verify.js</c>.</summary>
    internal static (int Scenes, int Failing, List<string> Failures) CheckScenes(string path)
    {
        var failures = new List<string>();
        int scenes = 0;
        int failing = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument json = JsonDocument.Parse(line);
            scenes++;
            if (CheckScene(json.RootElement, failures))
            {
                failing++;
            }
        }

        return (scenes, failing, failures);
    }

    /// <summary>Checks one scene; true when App Designer's code fails on it.</summary>
    private static bool CheckScene(JsonElement scene, List<string> failures)
    {
        string name = scene.GetProperty("name").GetString()!;
        JsonElement grow = scene.GetProperty("grow");
        var components = new List<GridifyComponent>();
        foreach (JsonElement c in scene.GetProperty("components").EnumerateArray())
        {
            string id = c.GetProperty("id").GetString()!;
            JsonElement g = grow.GetProperty(id);
            var box = new Rect2D(c.GetProperty("left").GetDouble(), c.GetProperty("top").GetDouble(), c.GetProperty("width").GetDouble(), c.GetProperty("height").GetDouble());
            components.Add(new GridifyComponent(id, box, g.GetProperty("horizontal").GetBoolean(), g.GetProperty("vertical").GetBoolean()));
        }

        JsonElement container = scene.GetProperty("container");
        JsonElement defaults = scene.GetProperty("defaults");
        GridifyResult Run() => DesignGridify.Gridify(
            new Size2D(container.GetProperty("width").GetDouble(), container.GetProperty("height").GetDouble()),
            components,
            Numbers(defaults.GetProperty("Padding")),
            defaults.GetProperty("RowSpacing").GetDouble(),
            defaults.GetProperty("ColumnSpacing").GetDouble(),
            scene.GetProperty("zoom").GetDouble());

        if (scene.TryGetProperty("error", out JsonElement error))
        {
            try
            {
                GridifyResult made = Run();
                failures.Add($"{name}: App Designer fails ({error.GetString()}), the port gives rows {{{string.Join(", ", made.RowHeight)}}}");
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception e)
            {
                failures.Add($"{name}: App Designer fails ({error.GetString()}), the port throws {e.GetType().Name}: {e.Message}");
            }

            return true;
        }

        GridifyResult result;
        try
        {
            result = Run();
        }
        catch (Exception e)
        {
            failures.Add($"{name}: the port throws {e.GetType().Name}: {e.Message}");
            return false;
        }

        JsonElement expected = scene.GetProperty("expected");
        CompareTracks(name, "RowHeight", expected.GetProperty("RowHeight"), result.RowHeight, failures);
        CompareTracks(name, "ColumnWidth", expected.GetProperty("ColumnWidth"), result.ColumnWidth, failures);
        CompareNumber(name, "RowSpacing", Number(expected.GetProperty("RowSpacing")), result.RowSpacing, failures);
        CompareNumber(name, "ColumnSpacing", Number(expected.GetProperty("ColumnSpacing")), result.ColumnSpacing, failures);
        double[] padding = Numbers(expected.GetProperty("Padding"));
        if (padding.Length != result.Padding.Count || padding.Zip(result.Padding).Any(static p => !Same(p.First, p.Second)))
        {
            failures.Add($"{name}: Padding is [{string.Join(' ', result.Padding.Select(static p => p.ToString("R")))}], App Designer's {expected.GetProperty("Padding").GetRawText()}");
        }

        JsonElement layout = expected.GetProperty("Layout");
        if (layout.EnumerateObject().Count() != result.Cells.Count)
        {
            failures.Add($"{name}: {result.Cells.Count} components placed, App Designer placed {layout.EnumerateObject().Count()}");
        }

        foreach (JsonProperty placed in layout.EnumerateObject())
        {
            if (!result.Cells.TryGetValue(placed.Name, out GridifyCell? cell))
            {
                failures.Add($"{name}: '{placed.Name}' is not placed");
                continue;
            }

            double[] row = Numbers(placed.Value.GetProperty("Row"));
            double[] column = Numbers(placed.Value.GetProperty("Column"));
            if (!row.SequenceEqual(cell.ToRowValue().Data) || !column.SequenceEqual(cell.ToColumnValue().Data))
            {
                failures.Add($"{name}: '{placed.Name}' is in {cell}, App Designer's Row {placed.Value.GetProperty("Row").GetRawText()} Column {placed.Value.GetProperty("Column").GetRawText()}");
            }
        }

        return false;
    }

    /// <summary>A number as the verifier writes it: JSON's own, or <c>{"nf": "Infinity"}</c> for one JSON cannot hold.</summary>
    private static double Number(JsonElement element) => element.ValueKind == JsonValueKind.Object
        ? element.GetProperty("nf").GetString() switch
        {
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.NaN,
        }
        : element.GetDouble();

    private static double[] Numbers(JsonElement element) => element.ValueKind == JsonValueKind.Array
        ? element.EnumerateArray().Select(Number).ToArray()
        : [Number(element)];

    private static bool Same(double a, double b) => a == b || (double.IsNaN(a) && double.IsNaN(b));

    private static void CompareTracks(string name, string property, JsonElement expected, IReadOnlyList<GridifyTrack> actual, List<string> failures)
    {
        JsonElement[] wanted = expected.EnumerateArray().ToArray();
        bool same = wanted.Length == actual.Count && wanted.Zip(actual).All(static p => p.First.ValueKind == JsonValueKind.String
            ? p.Second.IsWeight && p.Second.WeightText == p.First.GetString()
            : !p.Second.IsWeight && Same(p.Second.Value, Number(p.First)));
        if (!same)
        {
            failures.Add($"{name}: {property} is {{{string.Join(", ", actual.Select(static t => t.IsWeight ? t.WeightText : t.Value.ToString("R")))}}}, App Designer's {expected.GetRawText()}");
        }
    }

    private static void CompareNumber(string name, string property, double expected, double actual, List<string> failures)
    {
        if (!Same(expected, actual))
        {
            failures.Add($"{name}: {property} is {actual:R}, App Designer's {expected:R}");
        }
    }
}
