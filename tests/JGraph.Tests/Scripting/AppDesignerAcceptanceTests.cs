using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Designer plan DS8: the acceptance apps, built through the designer's own edits as its window makes
/// them (add from the library, set in the inspector, callbacks from the Callbacks tab, bodies typed into
/// the Code View, grids applied and filled, a responsive template), saved in both formats and opened
/// again; and the component coercions DS1 left to the inspector (ADR 0243, "Found with it"), which reach
/// the written code through the runtime's own components. With <c>JGRAPH_DS8_OUT</c> the apps are left
/// where <c>ds8_accept</c> (<c>tools/matlab-checklist/ui-probes/ds8</c>) runs them in R2025b and here.
/// </summary>
[Collection("JG facade")]
public class AppDesignerAcceptanceTests(ITestOutputHelper output)
{
    private static DesignDocument Blank() => DesignDocument.FromText(AppDesignerDocument.NewAppText(), null);

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static DesignComponent Find(DesignDocument document, string codeName) =>
        document.Design.Find(codeName) ?? throw new InvalidOperationException(codeName);

    /// <summary>The generated lines of one component, unindented, its comment left out.</summary>
    private static string[] Lines(DesignDocument document, string codeName) =>
        DesignCodeWriter.ComponentCode(Find(document, codeName), document.Style).ToArray();

    // --- the coercions DS1 left to the inspector (L5) -------------------------------------------------

    /// <summary>
    /// A value set in the designer goes through the component and the design keeps what the component
    /// kept, so the coercions the writer could not make (ADR 0243) are the runtime's: a spinner rounding
    /// fractional values rounds 3.5 to 4; a date picker keeps its disabled dates as a column; a button
    /// group always has one selection, so a lone toggle button is selected; checking a node's only
    /// child checks the node.
    /// </summary>
    [Fact]
    public void TheCoercionsOnSetAreTheComponents()
    {
        using DesignDocument document = Blank();
        DesignComponent figure = document.Design.Figure;

        DesignComponent spinner = document.Add(Entry("Spinner"), figure, new Point2D(100, 20))[1];
        Assert.Null(document.SetProperty(spinner, "RoundFractionalValues", OnOffValue.On));
        Assert.Null(document.SetProperty(spinner, "Value", NumericValue.Scalar(3.5)));
        Assert.Equal("4", DesignValueText.Format(spinner.Get("Value")!));
        Assert.Contains("app.Spinner.Value = 4;", Lines(document, "Spinner"));

        DesignComponent picker = document.Add(Entry("Date Picker"), figure, new Point2D(100, 60))[1];
        Assert.Null(document.SetProperty(picker, "DisabledDates",
            new DatetimeValue(1, 2, [new DesignDate(2026, 12, 25), new DesignDate(2027, 1, 1)])));
        Assert.Contains("app.DatePicker.DisabledDates = [datetime([2026 12 25]); datetime([2027 1 1])];", Lines(document, "DatePicker"));

        DesignComponent group = document.Add(Entry("Toggle Button Group"), figure, new Point2D(20, 120)).Single();
        document.Delete([group.Children[2], group.Children[1]]);
        DesignComponent lone = Assert.Single(group.Children);
        Assert.Equal("true", DesignValueText.Format(lone.Get("Value")!));
        Assert.Contains($"app.{lone.CodeName}.Value = true;", Lines(document, lone.CodeName));
        Assert.NotNull(document.SetProperty(lone, "Value", NumericValue.Logical(false)));
        Assert.Equal("true", DesignValueText.Format(lone.Get("Value")!));

        DesignComponent tree = document.Add(Entry("Tree (Check Box)"), figure, new Point2D(300, 20)).Single();
        DesignComponent parent = tree.Children.First(static n => n.Children.Count > 0);
        DesignComponent[] extra = parent.Children.Skip(1).ToArray();
        if (extra.Length > 0)
        {
            document.Delete(extra);
        }

        DesignComponent child = Assert.Single(parent.Children);
        Assert.Null(document.SetProperty(tree, "CheckedNodes", new ComponentsValue([child.CodeName], bracketed: false)));
        ComponentsValue checkedNodes = Assert.IsType<ComponentsValue>(tree.Get("CheckedNodes"));
        Assert.Equal([parent.CodeName, child.CodeName], checkedNodes.CodeNames.OrderBy(static n => n).ToArray());
        Assert.True(checkedNodes.Bracketed); // several nodes are a list, whatever form the one node came in
        Assert.Contains($"app.{tree.CodeName}.CheckedNodes = [app.{child.CodeName}, app.{parent.CodeName}];", Lines(document, tree.CodeName));
        Assert.Empty(document.Runtime.Problems);
    }

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

    // --- the acceptance apps ------------------------------------------------------------------------

    /// <summary>
    /// Three apps built with the designer's edits, as its window makes them, saved and opened again
    /// whole: a form in absolute layout with a startup function taking an argument and a callback
    /// over its components (and its plain-text copy); a grid app from Apply Grid Layout with a slider,
    /// a gauge and an axes in its cells; a two-panel responsive app from the template. With
    /// <c>JGRAPH_DS8_OUT</c> they are written under it for the probe <c>ds8_accept</c>, which runs
    /// them in R2025b and in this build's batch runner and holds the two outputs to each other.
    /// </summary>
    [Fact]
    public void TheAcceptanceAppsAreBuiltSavedAndOpenedWhole()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS8_OUT");
        string temp = Path.Combine(Path.GetTempPath(), "jgraph-ds8-" + Guid.NewGuid().ToString("N"));
        string folder = outFolder is null ? temp : Path.Combine(outFolder, "accept");
        Directory.CreateDirectory(folder);
        try
        {
            BuildForm(folder);
            BuildGrid(folder);
            BuildPanels(folder);
            foreach (string name in new[] { "AcceptForm.mlapp", "AcceptGrid.mlapp", "AcceptPanels.mlapp", "AcceptFormPlain.m" })
            {
                using DesignDocument reopened = DesignDocument.Open(Path.Combine(folder, name));
                Assert.Empty(reopened.ReadProblems);
                Assert.Empty(reopened.Runtime.Problems);
                Assert.NotNull(reopened.Code);
                output.WriteLine($"{name}: {reopened.Design.Components.Count()} components, {reopened.Callbacks.Count} callbacks");
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// The form: a numeric edit field, a spinner that rounds, a drop-down of two operations, a check
    /// box, a date picker with two disabled dates, a check-box tree, a Compute button and a result
    /// label; <c>startupFcn(app, n)</c> puts the argument in the field; the button's callback computes
    /// and writes the label.
    /// </summary>
    private void BuildForm(string folder)
    {
        string path = Path.Combine(folder, "AcceptForm.mlapp");
        AppDesignerDocument.SaveNew(path, AppDesignerDocument.NewAppText().Replace("app1", "AcceptForm", StringComparison.Ordinal));
        using DesignDocument document = DesignDocument.Open(path);
        DesignComponent figure = document.Design.Figure;
        Assert.Null(document.SetProperty(figure, "Name", new CharValue("Accept Form")));

        DesignComponent field = document.Add(Entry("Edit Field (Numeric)"), figure, new Point2D(120, 30))[1];
        Assert.Equal("EditField", field.CodeName);
        DesignComponent spinner = document.Add(Entry("Spinner"), figure, new Point2D(120, 70))[1];
        Assert.Null(document.SetProperty(spinner, "RoundFractionalValues", OnOffValue.On));
        Assert.Null(document.SetProperty(spinner, "Limits", NumericValue.Row(0, 10)));
        Assert.Null(document.SetProperty(spinner, "Value", NumericValue.Scalar(3.5)));
        DesignComponent drop = document.Add(Entry("Drop Down"), figure, new Point2D(120, 110))[1];
        Assert.Null(document.SetProperty(drop, "Items", CellValue.Row(new CharValue("Add"), new CharValue("Multiply"))));
        DesignComponent check = document.Add(Entry("Check Box"), figure, new Point2D(120, 150)).Single();
        Assert.Null(document.Rename(check, "DoubleCheckBox"));
        Assert.Null(document.SetProperty(check, "Text", new CharValue("Double it")));
        Assert.Equal("DoubleCheckBox", check.CodeName); // named by hand, so its text does not rename it
        DesignComponent picker = document.Add(Entry("Date Picker"), figure, new Point2D(120, 190))[1];
        Assert.Null(document.SetProperty(picker, "DisabledDates",
            new DatetimeValue(1, 2, [new DesignDate(2026, 12, 25), new DesignDate(2027, 1, 1)])));
        DesignComponent tree = document.Add(Entry("Tree (Check Box)"), figure, new Point2D(380, 30)).Single();
        DesignComponent parent = tree.Children.First(static n => n.Children.Count > 0);
        Assert.Null(document.SetProperty(tree, "CheckedNodes", new ComponentsValue([parent.Children[0].CodeName], bracketed: false)));

        DesignComponent button = document.Add(Entry("Button"), figure, new Point2D(120, 240)).Single();
        Assert.Null(document.Rename(button, "ComputeButton"));
        Assert.Null(document.SetProperty(button, "Text", new CharValue("Compute")));
        DesignComponent label = document.Add(Entry("Label"), figure, new Point2D(120, 280)).Single();
        Assert.Null(document.Rename(label, "ResultLabel"));
        Assert.Null(document.SetProperty(label, "Text", new CharValue("-")));

        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Equal("ComputeButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.Null(document.AddStartup());
        Assert.Null(document.SetInputParameters("n"));
        Type(document, "        function startupFcn(app, n)", "            app.EditField.Value = n;");
        Type(document, "        function ComputeButtonPushed(app, event)",
            "            a = app.EditField.Value;\n"
            + "            b = app.Spinner.Value;\n"
            + "            if strcmp(app.DropDown.Value, 'Add')\n"
            + "                r = a + b;\n"
            + "            else\n"
            + "                r = a * b;\n"
            + "            end\n"
            + "            if app.DoubleCheckBox.Value\n"
            + "                r = 2 * r;\n"
            + "            end\n"
            + "            app.ResultLabel.Text = sprintf('%g', r);");
        Assert.Contains("app.Spinner.Value = 4;", Lines(document, "Spinner"));
        Assert.Contains("app.DatePicker.DisabledDates = [datetime([2026 12 25]); datetime([2027 1 1])];", Lines(document, "DatePicker"));
        Assert.StartsWith("Saved ", document.Save());
        Assert.StartsWith("Saved ", document.SaveAs(Path.Combine(folder, "AcceptFormPlain.m")));
    }

    /// <summary>
    /// The grid app: Apply Grid Layout on the blank figure (8 rows of 3), a slider across the top, a
    /// gauge on the right and an axes filling the rest; the slider's callback moves the gauge and plots.
    /// </summary>
    private void BuildGrid(string folder)
    {
        string path = Path.Combine(folder, "AcceptGrid.mlapp");
        AppDesignerDocument.SaveNew(path, AppDesignerDocument.NewAppText().Replace("app1", "AcceptGrid", StringComparison.Ordinal));
        using DesignDocument document = DesignDocument.Open(path);
        DesignComponent grid = document.Gridify(document.Design.Figure);
        DesignComponent slider = document.AddToGrid(Entry("Slider"), grid, [GridSpan.Cell(1, 1), new GridSpan(1, 1, 2, 3)])[1];
        Assert.Equal("Slider", slider.CodeName);
        DesignComponent gauge = document.AddToGrid(Entry("Gauge"), grid, [GridSpan.Cell(5, 3), new GridSpan(2, 4, 3, 3)])[1];
        Assert.Equal("Gauge", gauge.CodeName);
        DesignComponent axes = document.AddToGrid(Entry("Axes"), grid, [new GridSpan(2, 8, 1, 2)]).Single();
        Assert.Equal("UIAxes", axes.CodeName);
        Assert.Null(document.AddCallback(slider, "ValueChangedFcn"));
        Type(document, "        function SliderValueChanged(app, event)",
            "            app.Gauge.Value = event.Value;\n"
            + "            plot(app.UIAxes, 1:10, (1:10) * event.Value);");
        Assert.Contains("app.Slider.Layout.Column = [2 3];", Lines(document, "Slider"));
        Assert.Contains("app.UIAxes.Layout.Row = [2 8];", Lines(document, "UIAxes"));
        Assert.StartsWith("Saved ", document.Save());
    }

    /// <summary>
    /// The two-panel responsive app from File > New: a button in the left panel, a label in the
    /// right; the button's callback writes the grid's column count into the label.
    /// </summary>
    private void BuildPanels(string folder)
    {
        string path = Path.Combine(folder, "AcceptPanels.mlapp");
        AppDesignerDocument.SaveNew(path, DesignResponsive.NewAppText(2).Replace("app1", "AcceptPanels", StringComparison.Ordinal));
        using DesignDocument document = DesignDocument.Open(path);
        Assert.True(document.Design.IsResponsive);
        DesignComponent button = document.Add(Entry("Button"), Find(document, "LeftPanel"), new Point2D(20, 20)).Single();
        DesignComponent label = document.Add(Entry("Label"), Find(document, "RightPanel"), new Point2D(20, 20)).Single();
        Assert.Null(document.Rename(label, "StatusLabel"));
        Assert.Null(document.SetProperty(label, "Text", new CharValue("waiting")));
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Type(document, "        function ButtonPushed(app, event)",
            "            app.StatusLabel.Text = sprintf('%d columns', numel(app.GridLayout.ColumnWidth));");
        Assert.Contains("function updateAppLayout(app, event)", document.CurrentText, StringComparison.Ordinal);
        Assert.StartsWith("Saved ", document.Save());
    }
}
