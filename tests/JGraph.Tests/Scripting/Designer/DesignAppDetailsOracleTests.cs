using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The App Designer window plan's stage W8a: App Details' checks, its OK, and an app's help text, against R2025b's own <c>AppDetailsUtil</c>, <c>CodeFormatter</c>,
/// <c>ClassBuilder</c>, <c>HelpCommentsDialogController</c> and <c>AppDetailsDialogController</c>, run by <c>ui-probes/w8/oracle/details.js</c> (DesignAppDetailsOracle.json).
/// </summary>
public class DesignAppDetailsOracleTests
{
    private static JsonElement Oracle(string part)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignAppDetailsOracle.json")));
        return oracle.RootElement.GetProperty(part).Clone();
    }

    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;

    [Fact]
    public void AVersionIsCheckedAsR2025bChecksIt()
    {
        foreach (JsonElement row in Oracle("versions").EnumerateArray())
        {
            string version = Text(row, "v");
            string why = Text(row, "bad");
            Assert.Equal(why.Length == 0 ? null : why, DesignAppDetails.WhyNotVersion(version));
            Assert.Equal(row.GetProperty("temp").GetBoolean(), DesignAppDetails.MayBecomeVersion(version));
        }
    }

    [Fact]
    public void AnImageIsCheckedByItsExtensionAsR2025bChecksIt()
    {
        foreach (JsonElement row in Oracle("images").EnumerateArray())
        {
            string why = Text(row, "bad");
            Assert.Equal(why.Length == 0 ? null : why, DesignAppDetails.WhyNotImage(Text(row, "f")));
        }
    }

    [Fact]
    public void ACommentIsWrappedAsR2025bWrapsIt()
    {
        foreach (JsonElement row in Oracle("wraps").EnumerateArray())
        {
            string[] expected = row.GetProperty("lines").EnumerateArray().Select(static l => l.GetString()!).ToArray();
            Assert.Equal(expected, DesignHelpText.Wrap(Text(row, "comment"), row.GetProperty("limit").GetInt32()));
        }
    }

    [Fact]
    public void TheHelpTextsLinesAreClassBuildersLines()
    {
        foreach (JsonElement row in Oracle("lines").EnumerateArray())
        {
            string[] expected = row.GetProperty("lines").EnumerateArray().Select(static l => l.GetString()!).ToArray();
            string indent = new(' ', row.GetProperty("indent").GetInt32());
            IReadOnlyList<string> lines = DesignHelpText.Lines(Text(row, "className"), Text(row, "summary"), Text(row, "description"), indent);
            Assert.Equal(expected.Take(lines.Count), lines);

            // The builder's list runs on to the line after the description: nothing past what the help text is.
            Assert.Equal(expected.Length, lines.Count);
        }
    }

    [Fact]
    public void TheDescriptionIsHeldAsTheDialogsOkHoldsIt()
    {
        foreach (JsonElement row in Oracle("wrapped").EnumerateArray())
        {
            Assert.Equal(Text(row, "wrapped"), DesignHelpText.WrappedDescription(Text(row, "description"), row.GetProperty("indent").GetInt32()));
        }
    }

    [Fact]
    public void TheDialogsOkSetsWhatR2025bsHands()
    {
        foreach (JsonElement row in Oracle("ok").EnumerateArray())
        {
            JsonElement initial = row.GetProperty("initial");
            JsonElement typed = row.GetProperty("typed");
            JsonElement handed = row.GetProperty("handed");
            DesignDocument document = App();
            string initialMode = Text(initial, "ScreenshotMode");
            var start = new DesignAppDetailsValues(Text(initial, "Name"), Text(initial, "Version"), Text(initial, "Author"), Text(initial, "Summary"), Text(initial, "Description"), ModeOf(initialMode), initialMode == "manual" ? Pixel : string.Empty);
            Seed(document, start);
            Assert.Null(document.SetInputParameters(Text(initial, "Arguments")));
            document.SetSingleInstance(Text(initial, "Singleton") == "FOCUS");

            // A plain-text dialog says the mode; an .mlapp's keeps what was picked or held.
            string mode = typed.GetProperty("Plain").GetBoolean() ? Text(typed, "ScreenshotMode") : typed.GetProperty("Picked").GetBoolean() ? "manual" : initialMode;
            document.SetAppDetails(
                new DesignAppDetailsValues(Text(typed, "Name"), Text(typed, "Version"), Text(typed, "Author"), Text(typed, "Summary"), Text(typed, "Description"), ModeOf(mode), Pixel),
                Text(typed, "Arguments"),
                Text(typed, "Singleton") == "FOCUS");

            DesignAppDetailsValues now = document.AppDetailsValues;
            if (handed.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal(start.Name, now.Name);
                Assert.Equal(start.Version, now.Version);
                continue;
            }

            if (handed.TryGetProperty("MetaData", out JsonElement metadata))
            {
                Assert.Equal(Text(metadata, "Name"), now.Name);
                Assert.Equal(Text(metadata, "Version"), now.Version);
                Assert.Equal(Text(metadata, "Author"), now.Author);
                Assert.Equal(Text(metadata, "Summary"), now.Summary);
                Assert.Equal(Text(metadata, "Description"), now.Description);
                Assert.Equal(ModeOf(Text(metadata, "ScreenshotMode")), now.Mode);
            }

            string arguments = handed.TryGetProperty("InputArguments", out JsonElement set) ? set.GetString()! : Text(initial, "Arguments");
            Assert.Equal(arguments, document.InputParameters);
            string singleton = handed.TryGetProperty("SingletonMode", out JsonElement single) ? single.GetString()! : Text(initial, "Singleton");
            Assert.Equal(singleton == "FOCUS", document.SingleInstance);
        }
    }

    private const string Pixel = "data:image/png;base64,iVBORw0KGgo=";

    private static DesignScreenshotMode ModeOf(string mode) => mode switch
    {
        "manual" => DesignScreenshotMode.Manual,
        "none" => DesignScreenshotMode.None,
        _ => DesignScreenshotMode.Auto,
    };

    private static void Seed(DesignDocument document, DesignAppDetailsValues values) => document.AppDetails.Values = values;

    private static DesignDocument App()
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }
}
