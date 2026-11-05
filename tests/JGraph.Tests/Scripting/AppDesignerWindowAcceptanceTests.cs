using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// App Designer window plan W16: the acceptance apps, each begun as a Start Page tile begins it
/// (<see cref="DesignStartPage.NewText"/>) and built through the designer's own edits - a blank app, a 2-Panel
/// app with auto-reflow, a Blank Component with a grid registered for apps as Configure for Apps registers it,
/// and a blank app that uses the component from the library - saved and opened again whole. With
/// <c>JGRAPH_W16_OUT</c> they are left where the probes of <c>tools/matlab-checklist/ui-probes/w16</c> open each in
/// R2025b's App Designer headless (<c>w16_verify</c>), run them in both engines (<c>w16_accept</c>), package each as
/// a MATLAB App and install it, and build the blank app into a standalone program (<c>w16_share</c>).
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerWindowAcceptanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly RecordingScriptOutput _scriptOutput = new();
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "jgraph-w16-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_scratch);
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_scriptOutput, (_, _) => { }, _scratch, null));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static DesignComponent Find(DesignDocument document, string codeName) =>
        document.Design.Find(codeName) ?? throw new InvalidOperationException(codeName);

    /// <summary>The generated lines of one component, unindented, its comment left out.</summary>
    private static string[] Lines(DesignDocument document, string codeName) =>
        DesignCodeWriter.ComponentCode(Find(document, codeName), document.Style).ToArray();

    /// <summary>
    /// Types <paramref name="body"/> into the Code View over the body <paramref name="signature"/>'s
    /// function has (empty, or App Designer's default lines), as one edit.
    /// </summary>
    private void Type(DesignDocument document, string signature, string body)
    {
        string text = document.CurrentText;
        int start = text.IndexOf(signature + "\n", StringComparison.Ordinal);
        int end = start < 0 ? -1 : text.IndexOf("\n        end", start, StringComparison.Ordinal);
        if (end < 0)
        {
            output.WriteLine(text);
            Assert.Fail("The code has no function " + signature);
        }

        Assert.True(document.ApplyEditedText(text[..(start + signature.Length)] + "\n" + body + text[end..]));
    }

    /// <summary>
    /// The four files of the acceptance set, each begun as its Start Page tile begins it and built through the
    /// designer's edits: the component registered for apps, the blank app, the 2-Panel app and the app that uses
    /// the component; each opens again with nothing to report. With <c>JGRAPH_W16_OUT</c> they are written under it
    /// for the W16 probes.
    /// </summary>
    [Fact]
    public async Task TheAcceptanceAppsAreBuiltFromTheStartPageSavedAndOpenedWhole()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_W16_OUT");
        string folder = outFolder is null ? Path.Combine(_scratch, "accept") : Path.Combine(outFolder, "accept");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        await BuildCounter(folder);
        BuildBlank(folder);
        BuildPanels(folder);
        BuildComposite(folder);
        foreach (string name in new[] { "W16Counter.mlapp", "W16Blank.mlapp", "W16Panels.mlapp", "W16Composite.mlapp" })
        {
            using DesignDocument reopened = DesignDocument.Open(Path.Combine(folder, name));
            Assert.Empty(reopened.ReadProblems);
            Assert.True(reopened.Runtime.Problems.Count == 0, name + ": " + string.Join(" | ", reopened.Runtime.Problems));
            Assert.Empty(reopened.MissingUserComponents);
            Assert.NotNull(reopened.Code);
            output.WriteLine($"{name}: {reopened.Design.Components.Count()} components, {reopened.Callbacks.Count} callbacks");
        }

        await TheCompositeAppCountsWhenItsComponentsButtonIsPushed(folder);
    }

    /// <summary>
    /// The composite app run as the window runs it: its component's button pushed through the event queue and the
    /// drain that runs the callback - the component counts by its Step, its event reaches the app's callback, which
    /// writes the label, and the component's owed <c>update</c> writes the button in the same drain (W16: before this
    /// the button kept its old count until the next event).
    /// </summary>
    private async Task TheCompositeAppCountsWhenItsComponentsButtonIsPushed(string folder)
    {
        await Exec($"addpath('{folder.Replace("'", "''", StringComparison.Ordinal)}'); app = W16Composite;");
        FigureModel figure = JG.FigureNumbers.Select(n => JG.TryGetFigure(n, out FigureModel f) ? f : null).OfType<FigureModel>().Single(f => f.Name == "W16 Composite");
        UiComponentContainerModel area = Assert.Single(figure.Components.OfType<UiComponentContainerModel>());
        Assert.Equal("W16Counter", area.ClassName);
        UiGridLayoutModel grid = Assert.IsType<UiGridLayoutModel>(Assert.Single(area.Components));
        UiCaptionModel button = Assert.IsAssignableFrom<UiCaptionModel>(Assert.Single(grid.Components));
        UiCaptionModel label = Assert.IsAssignableFrom<UiCaptionModel>(Assert.Single(figure.Components, static c => c is not UiComponentContainerModel));
        await ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);
        Assert.Equal(("0", "0"), (button.Text.Joined, label.Text.Joined));

        ScriptGraphicsCallbacks.NotifyComponent(button, "pushed");
        await ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);
        Assert.Equal(("5", "5"), (button.Text.Joined, label.Text.Joined));
        ScriptGraphicsCallbacks.NotifyComponent(button, "pushed");
        await ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);
        Assert.Equal(("10", "10"), (button.Text.Joined, label.Text.Joined));
        await Exec("delete(app);");
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _scriptOutput.ErrorText);
    }

    /// <summary>
    /// The component: the Blank Component with a grid, a Count button in its cell, the event Counted with its public
    /// callback CountedFcn, the managed properties Count and Step (Step must be positive), the button's callback
    /// counting and firing the event, <c>update</c> writing the count on the button; then registered as Configure for
    /// Apps registers it - the component made once for its default position, the record written into
    /// <c>resources/appDesigner.json</c> beside it.
    /// </summary>
    private async Task BuildCounter(string folder)
    {
        string path = Path.Combine(folder, "W16Counter.mlapp");
        using (DesignDocument document = DesignDocument.FromText(DesignStartPage.NewText(DesignNewKind.BlankComponentWithGrid, "W16Counter"), null))
        {
            Assert.True(document.IsComponent);
            DesignComponent grid = Find(document, "GridLayout");
            DesignComponent button = document.AddToGrid(Entry("Button"), grid, [GridSpan.Cell(1, 1)]).Single();
            Assert.Null(document.Rename(button, "CountButton"));
            Assert.Null(document.SetProperty(button, "Text", new CharValue("Count")));
            Assert.Null(document.AddEvent("Counted", "Fires after each count"));
            Assert.Null(document.AddManagedProperty(new DesignManagedProperty("Count") { DataType = "double", DefaultValue = "0", Description = "The count so far" }));
            var step = new DesignManagedProperty("Step") { DataType = "double", DefaultValue = "1", Description = "Added per push" };
            step.ValidationFunctions.Add(new DesignValidationFunction("mustBePositive"));
            Assert.Null(document.AddManagedProperty(step));
            Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
            Assert.Equal("CountButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
            Type(document, "        function CountButtonPushed(comp, event)",
                "            comp.Count = comp.Count + comp.Step;\n"
                + "            notify(comp, 'Counted');");
            Type(document, "        function update(comp)", "            comp.CountButton.Text = sprintf('%g', comp.Count);");
            Assert.Equal(["Counted"], document.Events.Select(static e => e.Name));
            Assert.Equal(["Count", "Step"], document.ManagedProperties.Select(static p => p.Name));
            Assert.Contains("        Step double {mustBePositive(Step)} = 1; % Added per push", document.CurrentText, StringComparison.Ordinal);
            Assert.StartsWith("Saved ", document.SaveAs(path));
        }

        // Configure for Apps: the component made once in a hidden figure for its default position, then the registry.
        DesignComponentCheck check = await ((IScriptAppRunner)_session).CheckComponentAsync(path, CancellationToken.None);
        Assert.Null(check.Error);
        Assert.Equal([1, 1, 320, 240], check.Position);
        DesignComponentMetadata model = DesignComponentMetadata.Load(path);
        Assert.True(model.IsValid);
        Assert.False(model.IsRegistered);
        DesignComponentMetadataRecord record = model.InitialRecord(DesignComponentMetadata.HelpLineOf(path));
        record.ComponentName = "Counter";
        record.Category = "W16";
        record.AuthorName = "W16";
        record.Avatar = string.Empty;
        record.AvatarDark = string.Empty;
        record.DefaultPosition = check.Position!;
        model.Save(record);
        Assert.True(File.Exists(Path.Combine(folder, "resources", "appDesigner.json")));
    }

    /// <summary>
    /// The blank app: a numeric edit field, a slider, a Scale button, a result label and an axes the startup
    /// function plots into; the button's callback writes the product into the label; App Details filled in for
    /// the packages.
    /// </summary>
    private void BuildBlank(string folder)
    {
        string path = Path.Combine(folder, "W16Blank.mlapp");
        AppDesignerDocument.SaveNew(path, DesignStartPage.NewText(DesignNewKind.BlankApp, "W16Blank"));
        using DesignDocument document = DesignDocument.Open(path);
        DesignComponent figure = document.Design.Figure;
        Assert.Null(document.SetProperty(figure, "Name", new CharValue("W16 Blank")));
        DesignComponent field = document.Add(Entry("Edit Field (Numeric)"), figure, new Point2D(120, 380))[1];
        Assert.Equal("EditField", field.CodeName);
        Assert.Null(document.SetProperty(field, "Value", NumericValue.Scalar(2)));
        DesignComponent slider = document.Add(Entry("Slider"), figure, new Point2D(120, 320))[1];
        Assert.Equal("Slider", slider.CodeName);
        Assert.Null(document.SetProperty(slider, "Limits", NumericValue.Row(0, 10)));
        Assert.Null(document.SetProperty(slider, "Value", NumericValue.Scalar(3)));
        DesignComponent button = document.Add(Entry("Button"), figure, new Point2D(120, 240)).Single();
        Assert.Null(document.Rename(button, "ScaleButton"));
        Assert.Null(document.SetProperty(button, "Text", new CharValue("Scale")));
        DesignComponent label = document.Add(Entry("Label"), figure, new Point2D(120, 200)).Single();
        Assert.Null(document.Rename(label, "ResultLabel"));
        Assert.Null(document.SetProperty(label, "Text", new CharValue("-")));
        DesignComponent axes = document.Add(Entry("Axes"), figure, new Point2D(300, 200)).Single();
        Assert.Equal("UIAxes", axes.CodeName);

        Assert.Null(document.AddStartup());
        Type(document, "        function startupFcn(app)", "            plot(app.UIAxes, 1:5, (1:5).^2);");
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Equal("ScaleButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
        Type(document, "        function ScaleButtonPushed(app, event)",
            "            app.ResultLabel.Text = sprintf('%g', app.EditField.Value * app.Slider.Value);");
        document.SetAppDetails(document.AppDetailsValues with
        {
            Name = "W16 Blank",
            Version = "1.0",
            Author = "W16",
            Summary = "A blank app from the Start Page.",
            Description = "Scales a number by a slider.",
        }, null, null);
        Assert.Contains("app.Slider.Limits = [0 10];", Lines(document, "Slider"));
        Assert.Contains("app.EditField.Value = 2;", Lines(document, "EditField"));
        Assert.StartsWith("Saved ", document.Save());
    }

    /// <summary>
    /// The 2-Panel app with auto-reflow: a list box of three colours in the left panel, a label in the right;
    /// the list box's callback writes the choice into the label.
    /// </summary>
    private void BuildPanels(string folder)
    {
        string path = Path.Combine(folder, "W16Panels.mlapp");
        AppDesignerDocument.SaveNew(path, DesignStartPage.NewText(DesignNewKind.TwoPanelApp, "W16Panels"));
        using DesignDocument document = DesignDocument.Open(path);
        Assert.True(document.Design.IsResponsive);
        DesignComponent list = document.Add(Entry("List Box"), Find(document, "LeftPanel"), new Point2D(20, 20))[1];
        Assert.Equal("ListBox", list.CodeName);
        Assert.Null(document.SetProperty(list, "Items", CellValue.Row(new CharValue("Red"), new CharValue("Green"), new CharValue("Blue"))));
        Assert.Null(document.SetProperty(list, "Value", new CharValue("Green")));
        DesignComponent label = document.Add(Entry("Label"), Find(document, "RightPanel"), new Point2D(20, 20)).Single();
        Assert.Null(document.Rename(label, "ChoiceLabel"));
        Assert.Null(document.SetProperty(label, "Text", new CharValue("none")));
        Assert.Null(document.AddCallback(list, "ValueChangedFcn"));
        Assert.Equal("ListBoxValueChanged", document.CallbackOf(list, "ValueChangedFcn"));
        Type(document, "        function ListBoxValueChanged(app, event)", "            app.ChoiceLabel.Text = app.ListBox.Value;");
        Assert.Contains("function updateAppLayout(app, event)", document.CurrentText, StringComparison.Ordinal);
        Assert.Contains("app.ListBox.Items = {'Red', 'Green', 'Blue'};", Lines(document, "ListBox"));
        Assert.Contains("app.ListBox.Value = 'Green';", Lines(document, "ListBox"));
        Assert.StartsWith("Saved ", document.Save());
    }

    /// <summary>
    /// The app that uses the component: the registration beside it puts Counter in the library under W16 (Custom);
    /// added from there, its Step set in the inspector, its public callback CountedFcn writing the count into a label.
    /// </summary>
    private void BuildComposite(string folder)
    {
        string path = Path.Combine(folder, "W16Composite.mlapp");
        AppDesignerDocument.SaveNew(path, DesignStartPage.NewText(DesignNewKind.BlankApp, "W16Composite"));
        using DesignDocument document = DesignDocument.Open(path);
        DesignComponent figure = document.Design.Figure;
        Assert.Null(document.SetProperty(figure, "Name", new CharValue("W16 Composite")));
        DesignPaletteEntry entry = Assert.Single(document.UserPaletteEntries);
        Assert.Equal(("Counter", "W16 (Custom)", "W16Counter"), (entry.Name, entry.Category, entry.Type));
        DesignComponent counter = Assert.Single(document.Add(entry, figure, new Point2D(40, 120)));
        Assert.Equal("W16Counter", counter.CodeName);
        Assert.Equal([320, 240], Assert.IsType<NumericValue>(counter.Get("Position")).Data.Skip(2));
        Assert.Null(document.SetProperty(counter, "Step", NumericValue.Scalar(5)));
        DesignComponent label = document.Add(Entry("Label"), figure, new Point2D(40, 60)).Single();
        Assert.Null(document.Rename(label, "TotalLabel"));
        Assert.Null(document.SetProperty(label, "Text", new CharValue("0")));
        Assert.Equal(["CountedFcn", "SizeChangedFcn"], document.CallbackPropertiesOf(counter));
        Assert.Null(document.AddCallback(counter, "CountedFcn"));
        Assert.Equal("W16CounterCounted", document.CallbackOf(counter, "CountedFcn"));
        Type(document, "        function W16CounterCounted(app, event)", "            app.TotalLabel.Text = sprintf('%g', app.W16Counter.Count);");
        string[] lines = Lines(document, "W16Counter");
        Assert.Equal("W16CompositeUIFigure", figure.CodeName); // named from the figure's title, as App Designer names it
        Assert.Equal("app.W16Counter = W16Counter(app.W16CompositeUIFigure);", lines[0]);
        Assert.Contains("app.W16Counter.Step = 5;", lines);
        Assert.Contains("app.W16Counter.CountedFcn = createCallbackFcn(app, @W16CounterCounted, true);", lines);
        Assert.True(document.Runtime.Problems.Count == 0, string.Join(" | ", document.Runtime.Problems));
        Assert.StartsWith("Saved ", document.Save());
    }
}
