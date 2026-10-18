using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS1b of the designer plan: an app's components written as App Designer's saved tree - the
/// <c>components</c> variable of <c>appModel.mat</c> - in R2025b's MCOS form.
/// </summary>
public class AppDesignerTreeWriterTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    private static AppDesign FixtureDesign() => DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;

    /// <summary>
    /// The fixture app's tree reads back through this build's own subsystem reader: one object per
    /// component, the figure first, each class App Designer offers among them.
    /// </summary>
    [Fact]
    public void TheFixtureAppsTreeReadsBackWithEveryComponent()
    {
        AppDesign design = FixtureDesign();
        DesignTreeResult result = DesignTreeWriter.Write(design);
        foreach (string problem in result.Problems)
        {
            output.WriteLine(problem);
        }

        string path = Path.Combine(Path.GetTempPath(), $"jgraph-ds1b-{Guid.NewGuid():N}.mat");
        try
        {
            File.WriteAllBytes(path, result.Bytes);
            (IReadOnlyList<(string Name, JgsValue Value)> variables, MatMcos? store) =
                MatFileReader.ReadWithSubsystem(path, null, new NullBinder());
            Assert.NotNull(store);
            (string name, _) = Assert.Single(variables, static v => v.Name.Length > 0);
            Assert.Equal(DesignTreeWriter.Variable, name);
            Assert.Equal("matlab.ui.Figure", store.ClassOf(1));
            var classes = Enumerable.Range(1, store.Count).Select(store.ClassOf).ToList();
            Assert.Equal(design.Components.Count(), classes.Count(c => DesignCatalog.R2025b.ByType(c) is not null));
            Assert.Equal(design.Components.Select(static c => c.Type).Distinct().Order(StringComparer.Ordinal),
                classes.Where(c => DesignCatalog.R2025b.ByType(c) is not null).Distinct().Order(StringComparer.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The subsystem writer's objects read back through this build's own reader: each object's
    /// class, and its stored names in order - a word as its text, a literal as its number, a value
    /// as itself, a reference as the referred objects' ids.
    /// </summary>
    [Fact]
    public void AnObjectGraphReadsBackAsItWasWritten()
    {
        var child = new McosObject("pkg.Child");
        child.Set("Value", new McosValue.Value(MatData.Row(1, 2, 3)));
        var root = new McosObject("pkg.sub.Root");
        root.Set("Mode", new McosValue.Word("manual"));
        root.Set("Flag", new McosValue.Literal(1));
        root.Set("Text", new McosValue.Value(MatData.Text("Hello")));
        root.Set("Kids", new McosValue.Refs([child, new McosObject("pkg.Child")]));
        root.Set("None", new McosValue.Refs([], "pkg.Child"));
        root.Set("Mode", new McosValue.Word("auto")); // a name stored again keeps its place
        root.Dynamic.Add(new McosObject("meta.DynamicProperty"));

        string path = Path.Combine(Path.GetTempPath(), $"jgraph-ds1b-{Guid.NewGuid():N}.mat");
        try
        {
            File.WriteAllBytes(path, MatMcosWriter.Write(root, "v", "f"));
            (_, MatMcos? store) = MatFileReader.ReadWithSubsystem(path, null, new NullBinder());
            Assert.NotNull(store);
            Assert.Equal(["pkg.sub.Root", "pkg.Child", "pkg.Child", "meta.DynamicProperty"], Enumerable.Range(1, store.Count).Select(store.ClassOf));
            IReadOnlyList<(string Name, JgsValue Value)> stored = store.Properties(1);
            Assert.Equal(["Mode", "Flag", "Text", "Kids", "None"], stored.Select(static p => p.Name));
            Assert.Equal("auto", stored[0].Value.AsString);
            Assert.Equal(1, stored[1].Value.AsNumber);
            Assert.Equal("Hello", stored[2].Value.AsString);
            Assert.True(MatMcos.Refs(stored[3].Value, out int[] kids));
            Assert.Equal([2, 3], kids);
            Assert.True(MatMcos.Refs(stored[4].Value, out int[] none));
            Assert.Empty(none);
            Assert.Equal([1.0, 2, 3], JGraph.Scripting.Jgs.Devices.DeviceChecks.NumberArray(store.Properties(2)[0].Value));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A design made here becomes App Designer's objects: each component its class's base with the
    /// generator's properties stored on top under their public names, a set colour's mode stated, a
    /// callback as its method's name, a grid child's place as its Layout options, the children in
    /// MATLAB's order, a tree's checked nodes after its children, and each component's
    /// DesignTimeProperties with its code as App Designer stores it.
    /// </summary>
    [Fact]
    public void ADesignMadeHereBecomesAppDesignersObjects()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        var figure = new DesignComponent("UIFigure", catalog.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        var design = new AppDesign(figure);
        var grid = new DesignComponent("GridLayout", catalog.ByType("matlab.ui.container.GridLayout")!);
        figure.Add(grid);
        var go = new DesignComponent("GoButton", catalog.ByType("matlab.ui.control.Button")!);
        grid.Add(go);
        go.Set("Text", CellValue.Row(new CharValue("Go")));
        go.Set("ButtonPushedFcn", new CallbackValue("GoButtonPushed"));
        go.Set("BackgroundColor", NumericValue.Row(1, 0, 0));
        go.Set("Layout.Row", NumericValue.Scalar(2));
        go.Set("Layout.Column", NumericValue.Row(1, 2));
        var stop = new DesignComponent("StopButton", catalog.ByType("matlab.ui.control.Button")!);
        grid.Add(stop);
        var tree = new DesignComponent("Tree", catalog.ByType("matlab.ui.container.CheckBoxTree")!);
        grid.Add(tree);
        var node = new DesignComponent("Node", catalog.ByType("matlab.ui.container.TreeNode")!);
        tree.Add(node);
        tree.Set("CheckedNodes", new ComponentsValue(["Node"], bracketed: true));

        var problems = new List<string>();
        McosObject root = DesignTreeWriter.Tree(design, DesignCodeStyle.Default, appFolder: null, problems);
        Assert.Empty(problems);
        Assert.Equal("matlab.ui.Figure", root.ClassName);
        McosObject gridObject = Assert.Single(Children(root));
        McosObject[] buttons = Children(gridObject);
        Assert.Equal(["matlab.ui.container.CheckBoxTree", "matlab.ui.control.Button", "matlab.ui.control.Button"], buttons.Select(static b => b.ClassName));
        McosObject goObject = buttons[2];

        Assert.Equal("Go", Text(goObject.Get("Text")));
        Assert.Equal("GoButtonPushed", Text(goObject.Get("ButtonPushedFcn")));
        Assert.Equal(new McosValue.Word("manual"), goObject.Get("BackgroundColorMode"));
        int colour = IndexOf(goObject, "BackgroundColor");
        Assert.True(colour < IndexOf(goObject, "BackgroundColorMode"));
        McosObject layout = Assert.Single(Assert.IsType<McosValue.Refs>(goObject.Get("Layout")).Objects);
        Assert.Equal("matlab.ui.layout.GridLayoutOptions", layout.ClassName);
        Assert.Equal([2.0], Numbers(layout.Get("Row")));
        Assert.Equal([1.0, 2], Numbers(layout.Get("Column")));

        McosObject treeObject = buttons[0];
        Assert.True(IndexOf(treeObject, "matlab.ui.container.Container.SerializableChildren") < IndexOf(treeObject, "CheckedNodes"));

        McosObject dtp = goObject.Dynamic.Single(d => Text(d.Get("DynamicName_")) == "DesignTimeProperties");
        var fields = (MatData.Structs)((McosValue.Value)dtp.Get("DynamicValue_")!).Data;
        Assert.Equal(["CodeName", "GroupId", "ComponentCode", "ImageRelativePath"], fields.Fields);
        Assert.Equal("GoButton", ((MatData.Chars)fields.Elements[0][0]).Characters);
        var code = (MatData.Cells)fields.Elements[0][2];
        Assert.Equal([code.Items.Length, 1], code.Size);
        Assert.Equal(DesignCodeWriter.StoredComponentCode(go), code.Items.Select(static l => ((MatData.Chars)l).Characters));
    }

    /// <summary>
    /// The lines App Designer stores for a component are its generator's lines before the code model
    /// finishes them: the component and its parent as placeholders, a callback as its method's name
    /// in quotes, an image beside the app by its full path.
    /// </summary>
    [Fact]
    public void AComponentsStoredCodeIsTheGeneratorsUnfinishedLines()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        var figure = new DesignComponent("UIFigure", catalog.ByType("matlab.ui.Figure")!);
        _ = new AppDesign(figure);
        var button = new DesignComponent("GoButton", catalog.ByType("matlab.ui.control.Button")!);
        figure.Add(button);
        button.Set("ButtonPushedFcn", new CallbackValue("GoButtonPushed"));
        button.Set("Icon", new ImageFileValue(["img", "go.png"]));
        button.Set("Text", CellValue.Row(new CharValue("Go")));

        string folder = Path.Combine(Path.GetTempPath(), "app");
        Assert.Equal(
            [
                "app.ad_CODENAME_ad = uibutton(app.ad_PARENTCODENAME_ad, 'push');",
                "app.ad_CODENAME_ad.ButtonPushedFcn = 'GoButtonPushed';",
                $"app.ad_CODENAME_ad.Icon = '{Path.Combine(folder, "img", "go.png")}';",
                "app.ad_CODENAME_ad.Text = 'Go';",
            ],
            DesignCodeWriter.StoredComponentCode(button, appFolder: folder));
        Assert.Equal("app.ad_CODENAME_ad = uifigure('Visible', 'off');", DesignCodeWriter.StoredComponentCode(figure)[0]);
        Assert.Equal(
            [
                "app.GoButton = uibutton(app.UIFigure, 'push');",
                "app.GoButton.ButtonPushedFcn = createCallbackFcn(app, @GoButtonPushed, true);",
                "app.GoButton.Icon = fullfile(pathToMLAPP, 'img', 'go.png');",
                "app.GoButton.Text = 'Go';",
            ],
            DesignCodeWriter.ComponentCode(button));
    }

    /// <summary>A date is stored as its milliseconds since 1970 on the proleptic Gregorian calendar, any year; NaT as NaN.</summary>
    [Fact]
    public void ADateIsStoredAsMillisecondsSince1970()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        var figure = new DesignComponent("UIFigure", catalog.ByType("matlab.ui.Figure")!);
        var design = new AppDesign(figure);
        var picker = new DesignComponent("DatePicker", catalog.ByType("matlab.ui.control.DatePicker")!);
        figure.Add(picker);
        picker.Set("DisabledDates", new DatetimeValue(3, 1, [new DesignDate(2026, 10, 18), new DesignDate(0, 1, 1), null]));

        McosObject root = DesignTreeWriter.Tree(design, DesignCodeStyle.Default, appFolder: null, new List<string>());
        McosObject date = Assert.Single(Assert.IsType<McosValue.Refs>(Children(root)[0].Get("DisabledDates")).Objects);
        Assert.Equal("datetime", date.ClassName);
        var data = (MatData.Numbers)((McosValue.Value)date.Get("data")!).Data;
        Assert.Equal([3, 1], data.Size);
        Assert.Equal(1_792_281_600_000.0, data.Data[0]);
        Assert.Equal(-62_167_219_200_000.0, data.Data[1]);
        Assert.True(double.IsNaN(data.Data[2]));
    }

    /// <summary>What the tree cannot hold is listed, and the rest of the tree is still written: a component class App Designer's catalog does not know, and an axes' colormap.</summary>
    [Fact]
    public void WhatTheTreeCannotHoldIsListed()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        var figure = new DesignComponent("UIFigure", catalog.ByType("matlab.ui.Figure")!);
        var design = new AppDesign(figure);
        figure.Add(new DesignComponent("Tuner", "simulink.ui.control.VariableTuner", "uivariabletuner", string.Empty));
        var axes = new DesignComponent("Axes", catalog.ByType("matlab.ui.control.UIAxes")!);
        figure.Add(axes);
        axes.Set("AD_ColormapString", new CharValue("hot"));
        axes.Set("TitleString", new CharValue("Signal"));

        DesignTreeResult result = DesignTreeWriter.Write(design);
        Assert.False(result.IsComplete);
        Assert.Collection(result.Problems,
            p => Assert.StartsWith("Tuner:", p, StringComparison.Ordinal),
            p => Assert.StartsWith("Axes.AD_ColormapString:", p, StringComparison.Ordinal));
        Assert.NotEmpty(result.Bytes);
    }

    private static McosObject[] Children(McosObject o) =>
        o.Get("matlab.ui.container.Container.SerializableChildren") is McosValue.Refs refs ? [.. refs.Objects] : [];

    private static int IndexOf(McosObject o, string name) => o.Entries.Select(static e => e.Name).ToList().IndexOf(name);

    private static string? Text(McosValue? value) => value is McosValue.Value { Data: MatData.Chars c } ? c.Characters : null;

    private static double[] Numbers(McosValue? value) => value is McosValue.Value { Data: MatData.Numbers n } ? n.Data : [];

    /// <summary>
    /// Where the probe folder <c>ds1b/rt</c> exists (it is ignored), leaves there the trees R2025b
    /// loads in the probe <c>ds1b_verify</c>: the fixture app's, and each shipped app's from its
    /// code. Nothing is asserted here but that each was written.
    /// </summary>
    [Fact]
    public void WritesTheTreesTheProbeLoads()
    {
        string? folder = ProbeFolder();
        if (folder is null)
        {
            return;
        }

        string jgraph = Path.Combine(folder, "jgraph");
        Directory.CreateDirectory(jgraph);
        Write(jgraph, "DesignerSpecApp", FixtureDesign(), appFolder: Path.Combine(folder, "spec"));
        if (Directory.Exists(ShippedRoot))
        {
            foreach (string path in Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories))
            {
                DesignReadResult read = DesignCodeReader.Read(JgsMlapp.ReadCode(path));
                if (read.Design is { } design)
                {
                    Write(jgraph, Path.GetFileNameWithoutExtension(path), design, read.Style, Path.GetDirectoryName(path));
                }
            }
        }
    }

    private void Write(string folder, string name, AppDesign design, DesignCodeStyle? style = null, string? appFolder = null)
    {
        DesignTreeResult result = DesignTreeWriter.Write(design, style, appFolder);
        File.WriteAllBytes(Path.Combine(folder, name + "__x.mat"), result.Bytes);

        // The createComponents the tree stands for, as the designer writes it: the probe runs this
        // for its reference, so that a value written by hand at its default is not counted against it.
        File.WriteAllLines(Path.Combine(folder, name + "__code.m"), DesignCodeWriter.Method(design, style));
        foreach (string problem in result.Problems)
        {
            output.WriteLine($"{name}: {problem}");
        }
    }

    private static string? ProbeFolder()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        string? rt = folder is null ? null : Path.Combine(folder, "tools", "matlab-checklist", "ui-probes", "ds1b", "rt");
        return rt is not null && Directory.Exists(rt) ? rt : null;
    }

    private sealed class NullBinder : IMatObjectBinder
    {
        public JgsValue NewObject(string className, bool deleted, string variable) => JgsValue.Number(0);

        public void SetProperties(JgsValue instance, IReadOnlyDictionary<string, JgsValue> properties)
        {
        }

        public JgsValue Function(string text, string type, IReadOnlyDictionary<string, JgsValue>? workspace, string variable) =>
            JgsValue.Str(text);
    }
}
