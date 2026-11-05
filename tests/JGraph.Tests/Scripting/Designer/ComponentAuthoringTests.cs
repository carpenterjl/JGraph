using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// App Designer window plan W15: custom UI component authoring. The class texts are those R2025b's
/// code engine writes for a component (<c>ComponentAppCodeEngineConfig</c> and its behaviors, read
/// from the client; the texts measured to compile and run in R2025b by the probe <c>w15_component</c>,
/// kept as <c>W15Templates</c>): a blank component, one with a grid, one with events, managed
/// properties, an editable section, callbacks and the startup function, and one giving test cases
/// access with its inherited properties set.
/// </summary>
[Collection("JG facade")]
public class ComponentAuthoringTests
{
    private static string Template(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "W15Templates", name + ".m")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string[] Lines(string text) => text.TrimEnd('\n').Split('\n');

    [Theory]
    [InlineData("comp1", false)]
    [InlineData("comp2", true)]
    public void ANewComponentsTextIsTheCodeEngines(string name, bool withGrid)
    {
        Assert.Equal(Lines(Template(name)), Lines(DesignComponentCode.NewText(name, withGrid)));
        Assert.Equal(Template(name), DesignStartPage.NewText(withGrid ? DesignNewKind.BlankComponentWithGrid : DesignNewKind.BlankComponent, name));
    }

    [Theory]
    [InlineData("comp1")]
    [InlineData("comp2")]
    [InlineData("comp3")]
    [InlineData("comp4")]
    public void AComponentsClassReadsAndWritesBackLineForLine(string name)
    {
        string text = Template(name);
        JgsMlappLayout layout = JgsMlappLayout.Of(text);
        Assert.True(layout.IsAppLayout && layout.IsComponentLayout && layout.IsComplete, "the layout is a component's");
        DesignReadResult read = DesignCodeReader.Read(text);
        Assert.True(read.IsComplete, string.Join("\n", read.Problems));
        Assert.True(read.Design!.IsComponent);
        Assert.Equal(DesignComponentCode.RootCodeName, read.Design.Figure.CodeName);
        DesignCode code = DesignCode.Read(text) ?? throw new Xunit.Sdk.XunitException("the text is not laid out as a component");
        Assert.True(code.IsComponent);
        Assert.Equal(Lines(text), Lines(DesignClassWriter.Render(read.Design, code, read.Style)));
    }

    [Fact]
    public void TheFullComponentsPartsAreRead()
    {
        string text = Template("comp3");
        DesignCode code = DesignCode.Read(text)!;
        Assert.Equal(["Cleared", "ValueChanged"], code.Events.Select(static e => e.Name));
        Assert.Equal("Fires when the value changes", code.Events[1].Description);
        Assert.Equal(["Scale", "Limits", "Value"], code.ManagedProperties.Select(static p => p.Name));
        DesignManagedProperty limits = code.ManagedProperties[1];
        Assert.Equal(("double", "[0 100]", "The range", "public"), (limits.DataType, limits.DefaultValue, limits.Description, limits.Access));
        Assert.Equal(["1", "2"], limits.Size);
        Assert.Equal([new DesignValidationFunction("mustBeNumeric")], limits.ValidationFunctions);
        Assert.Equal([new DesignValidationFunction("mustBeInRange", "0, 100, \"inclusive\"")], code.ManagedProperties[2].ValidationFunctions);
        Assert.Equal("private", code.ManagedProperties[0].Access);
        Assert.Equal(["    properties (Access = private)", "        Count % Description", "    end"], code.EditableSection);
        Assert.Equal(["postSetupFcn", "ButtonPushed"], code.Callbacks.Select(static c => c.Name));
        Assert.True(code.Callbacks[0].IsStartup);
        Assert.Equal(["            comp.Button.Text = num2str(comp.Value);"], code.UpdateBody);
        Assert.False(code.AllowTestCaseAccess);

        DesignReadResult read = DesignCodeReader.Read(text);
        DesignComponent button = read.Design!.Find("Button")!;
        Assert.Equal("GridLayout", button.Parent!.CodeName);
        Assert.Equal("ButtonPushed", Assert.IsType<CallbackValue>(button.Get("ButtonPushedFcn")).Method);
        Assert.Equal("matlab.apps.createCallbackFcn(comp, @ButtonPushed, true)", DesignValueText.Format(button.Get("ButtonPushedFcn")!, DesignComponentCode.ObjectName));

        // comp4: the test case access and the inherited properties the root sets; a fresh update body is kept as none.
        DesignCode four = DesignCode.Read(Template("comp4"))!;
        Assert.True(four.AllowTestCaseAccess);
        Assert.Equal(["AbortSet", "Access = public", "GetObservable", "Hidden", "SetObservable"], four.ManagedProperties[0].Attributes);
        Assert.Equal("properties (AbortSet, Access = public, GetObservable, Hidden, SetObservable)", four.ManagedProperties[0].BlockLine);
        DesignComponent root = DesignCodeReader.Read(Template("comp4")).Design!.Figure;
        Assert.Equal([1, 1, 200, 60], Assert.IsType<NumericValue>(root.Get("Position")).Data);
        Assert.Equal("off", Assert.IsType<CharValue>(root.Get("Visible")).Text);
        Assert.Empty(DesignCode.Read(Template("comp1"))!.UpdateBody);
    }

    [Fact]
    public void AManagedDeclarationRoundTrips()
    {
        var property = new DesignManagedProperty("Value") { DataType = "double", DefaultValue = "0", Description = "The value" };
        property.Size.AddRange(["1", "1"]);
        property.ValidationFunctions.Add(new DesignValidationFunction("mustBeInRange", "0, 100"));
        Assert.Equal("Value (1,1) double {mustBeInRange(Value, 0, 100)} = 0; % The value", property.Declaration);
        DesignManagedProperty back = DesignManagedProperty.Parse(property.Declaration, property.Attributes)!;
        Assert.Equal(property.Declaration, back.Declaration);
        Assert.Equal("Access = public", property.BlockId);

        // The dialog's flags: AbortSet brings the observables, and the block is keyed by the sorted attributes.
        property.SetFlags(["AbortSet", "Hidden"]);
        Assert.Equal(["AbortSet", "Access = public", "GetObservable", "Hidden", "SetObservable"], property.Attributes);
        Assert.Equal("Name % A thing", DesignManagedProperty.Parse("Name % A thing", ["Access = private"])!.Declaration);
        Assert.Null(DesignManagedProperty.Parse("Name = 3", ["Access = private"])); // no semicolon: the user's line
        Assert.Null(DesignManagedProperty.Parse("function x", ["Access = private"]));
        Assert.Equal("{'a'; 'b'}", DesignManagedProperty.Parse("Items cell = {'a'; 'b'}; % two", ["Access = public"])!.DefaultValue);
        Assert.Equal(["mustBeMember(Items, {'a', 'b'})"], DesignManagedProperty.Parse("Items {mustBeMember(Items, {'a', 'b'})}", [])!.ValidationFunctions.Select(f => f.Signature("Items")));

        Assert.Equal("notify(comp, 'Cleared');", new DesignCodeEvent("Cleared").NotifyLine);
        Assert.Equal("ClearedFcn", new DesignCodeEvent("Cleared").CallbackProperty);
    }

    [Fact]
    public void TheDocumentEditsEventsPropertiesAndTheUpdateBody()
    {
        using var document = DesignDocument.FromText(DesignComponentCode.NewText("comp5", withGrid: true), null);
        Assert.True(document.IsComponent);
        document.SelectApp();
        Assert.Equal(DesignComponentCode.DefaultText, document.InspectorTab);
        Assert.Equal([DesignComponentCode.StartupProperty], document.CallbackSheet!.Groups[0].Properties);
        Assert.Equal(DesignComponentCode.StartupGroupTitle, document.CallbackSheet!.Groups[0].Title);
        Assert.Equal(["comp5", "comp.GridLayout"], document.BrowserRows(DesignBrowserList.Complete).Select(static r => r.Label));

        // An event, then its rename follows the user's notify call; undo takes both back.
        Assert.Null(document.AddEvent("ValueChanged", "Fires"));
        Assert.Contains("    events (HasCallbackProperty, NotifyAccess = private)\n        ValueChanged % Fires\n    end\n", document.CurrentText, StringComparison.Ordinal);
        Assert.Equal(DesignComponentCode.FcnSuffixWarning, document.AddEvent("ChangedFcn"));
        Assert.Equal("Name is already defined as event.", document.AddEvent("ValueChanged"));
        Assert.Null(document.AddStartup());
        Assert.Equal("postSetupFcn", document.Callbacks[0].Name);
        Assert.Contains("            % Execute the postSetup function\n            postSetupFcn(comp)\n", document.CurrentText, StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(document.CurrentText.Replace("        function postSetupFcn(comp)\n            \n", "        function postSetupFcn(comp)\n            notify(comp, 'ValueChanged');\n", StringComparison.Ordinal)));
        Assert.Null(document.UpdateEvent(document.Events[0], "Changed", "Fires"));
        Assert.Contains("notify(comp, 'Changed');", document.CurrentText, StringComparison.Ordinal);
        document.Undo();
        Assert.Contains("notify(comp, 'ValueChanged');", document.CurrentText, StringComparison.Ordinal);
        Assert.Equal("ValueChanged", document.Events[0].Name);

        // A managed property with a checked default; a bad default is refused in MATLAB's words.
        var value = new DesignManagedProperty("Value") { DataType = "double", DefaultValue = "5" };
        value.ValidationFunctions.Add(new DesignValidationFunction("mustBePositive"));
        Assert.Null(document.AddManagedProperty(value));
        Assert.Contains("    properties (Access = public)\n        Value double {mustBePositive(Value)} = 5;\n    end\n    \n", document.CurrentText, StringComparison.Ordinal);
        var bad = new DesignManagedProperty("Other") { DataType = "double", DefaultValue = "-1" };
        bad.ValidationFunctions.Add(new DesignValidationFunction("mustBePositive"));
        Assert.Contains("positive", document.AddManagedProperty(bad), StringComparison.Ordinal);
        var sized = new DesignManagedProperty("Pair") { DataType = "double", DefaultValue = "[1 2 3]" };
        sized.Size.AddRange(["1", "2"]);
        Assert.Equal("Value must be a vector with 2 elements.", document.AddManagedProperty(sized));
        Assert.Contains("Unrecognized function or variable 'abc'", document.AddManagedProperty(new DesignManagedProperty("Bad") { DataType = "double", DefaultValue = "abc" }), StringComparison.Ordinal);

        // The inspector's edit of the default takes the canonical text; the rename follows comp.Value in the user's code.
        Assert.True(document.ApplyEditedText(document.CurrentText.Replace("            % Use this function to update the underlying components\n            \n", "            comp.Value = comp.Value + 1;\n", StringComparison.Ordinal)));
        Assert.Equal(["            comp.Value = comp.Value + 1;"], document.Code!.UpdateBody);
        Assert.Null(document.SetManagedDefault(document.ManagedProperties[0], "7"));
        Assert.Equal("7", document.ManagedProperties[0].DefaultValue);
        DesignManagedProperty renamed = document.ManagedProperties[0].Clone();
        renamed.Name = "Count";
        Assert.Null(document.ModifyManagedProperty(document.ManagedProperties[0], renamed));
        Assert.Contains("            comp.Count = comp.Count + 1;", document.CurrentText, StringComparison.Ordinal);
        Assert.Contains("Count double {mustBePositive(Count)} = 7;", document.CurrentText, StringComparison.Ordinal);

        // Test case access changes the block line; a removal takes the block away.
        Assert.Null(document.SetAllowTestCaseAccess(true));
        Assert.Contains("    properties (GetAccess = ?matlab.unittest.TestCase, SetAccess = private, Transient, NonCopyable)\n        GridLayout  matlab.ui.container.GridLayout\n    end\n", document.CurrentText, StringComparison.Ordinal);
        Assert.Null(document.RemoveManagedProperty(document.ManagedProperties[0]));
        Assert.DoesNotContain("properties (Access = public)", document.CurrentText, StringComparison.Ordinal);
        Assert.Null(document.RemoveEvent(document.Events[0]));
        Assert.DoesNotContain("events (", document.CurrentText, StringComparison.Ordinal);

        // What stands reads back as itself.
        string text = document.CurrentText;
        Assert.Equal(text, DesignDocument.FromText(text, null).CurrentText);
    }

    [Fact]
    public void AComponentSavesAsAnMlappAndOpensAgain()
    {
        // The .mlapp an R2025b App Designer reads: the class text with the CustomUIComponent app type and the
        // AppTypeData written from the text (events, managed properties, update); a copy goes beside the probe's
        // control when JGRAPH_W15_OUT names a folder, for w15_verify.m to load in R2025b.
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-w15-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "comp3.mlapp");
            using (DesignDocument document = DesignDocument.FromText(Template("comp3"), null))
            {
                document.SaveAs(path);
            }

            Assert.Equal(Template("comp3"), JgsMlapp.ReadCode(path).Replace("\r\n", "\n", StringComparison.Ordinal));
            using DesignDocument opened = DesignDocument.Open(path);
            Assert.True(opened.IsComponent);
            Assert.Equal(["Cleared", "ValueChanged"], opened.Events.Select(static e => e.Name));
            Assert.Equal(["Scale", "Limits", "Value"], opened.ManagedProperties.Select(static p => p.Name));
            Assert.Equal(Template("comp3"), opened.CurrentText);
            if (Environment.GetEnvironmentVariable("JGRAPH_W15_OUT") is { Length: > 0 } outFolder)
            {
                Directory.CreateDirectory(outFolder);
                File.Copy(path, Path.Combine(outFolder, "comp3.mlapp"), overwrite: true);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
