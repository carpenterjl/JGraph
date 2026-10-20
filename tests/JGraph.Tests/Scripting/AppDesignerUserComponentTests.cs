using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Designer plan DS7b: custom components (classes under <c>ComponentContainer</c>) in the designer.
/// The rules are App Designer's, measured in R2025b by the probe <c>ds7_uac</c>
/// (<c>tools/matlab-checklist/ui-probes/ds7</c>): the metadata file its <c>configureMetadata</c>
/// writes, the lines its <c>UserComponentAdapter</c> generates for <c>SpinnerGauge</c>, the groups its
/// inspector shows, the callback properties, the package's <c>userComponents</c> element.
/// </summary>
[Collection("JG facade")]
public class AppDesignerUserComponentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-ds7b-" + Guid.NewGuid().ToString("N"));

    public AppDesignerUserComponentTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(Helper("SpinnerGauge.m"), Path.Combine(_folder, "SpinnerGauge.m"));
        Directory.CreateDirectory(Path.Combine(_folder, "resources"));
        File.WriteAllText(Path.Combine(_folder, "resources", "appDesigner.json"), MetadataJson);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }

        GC.SuppressFinalize(this);
    }

    private static string Helper(string name) => Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers", name);

    /// <summary>The file R2025b's configureMetadata wrote for SpinnerGauge, headless (probe ds7_uac, section 1; the icon shortened).</summary>
    private const string MetadataJson = """
        {
          "components": [
            {
              "className": "SpinnerGauge",
              "componentName": "SpinnerGauge",
              "description": "SPINNERGAUGE  A custom UI component: a spinner and a linear gauge kept in step.",
              "icon": "data:image/png;base64,iVBORw0KGgo=",
              "category": "My Components",
              "authorName": "",
              "authorEmail": "",
              "version": "1.0",
              "avatar": "",
              "avatarDark": "",
              "defaultPosition": [
                1,
                1,
                220,
                80
              ]
            }
          ],
          "schema": 3,
          "MATLABRelease": "R2025b"
        }
        """;

    /// <summary>The app the probe saved as UacLast: a gauge of the user's class and a button, as App Designer's generator wrote them.</summary>
    private const string UacLastText = """
        classdef UacLast < matlab.apps.AppBase

            % Properties that correspond to app components
            properties (Access = public)
                UIFigure  matlab.ui.Figure
                GoButton  matlab.ui.control.Button
                Gauge     SpinnerGauge
            end

            % Callbacks that handle component events
            methods (Access = private)

                % Value changed function: Gauge
                function GaugeValueChanged(app, event)
                    disp('changed');
                end

                % Button pushed function: GoButton
                function GoButtonPushed(app, event)
                    app.Gauge.Value = app.Gauge.Value + 10;
                end
            end

            % Component initialization
            methods (Access = private)

                % Create UIFigure and components
                function createComponents(app)

                    % Create UIFigure and hide until all components are created
                    app.UIFigure = uifigure('Visible', 'off');
                    app.UIFigure.Position = [100 100 400 200];
                    app.UIFigure.Name = 'UAC app';

                    % Create Gauge
                    app.Gauge = SpinnerGauge(app.UIFigure);
                    app.Gauge.Value = 40;
                    app.Gauge.ValueChangedFcn = createCallbackFcn(app, @GaugeValueChanged, true);
                    app.Gauge.Position = [20 20 220 80];

                    % Create GoButton
                    app.GoButton = uibutton(app.UIFigure, 'push');
                    app.GoButton.ButtonPushedFcn = createCallbackFcn(app, @GoButtonPushed, true);
                    app.GoButton.Position = [260 40 100 22];
                    app.GoButton.Text = 'Go';

                    % Show the figure after all components are created
                    app.UIFigure.Visible = 'on';
                end
            end

            % App creation and deletion
            methods (Access = public)

                % Construct app
                function app = UacLast

                    % Create UIFigure and components
                    createComponents(app)

                    % Register the app with App Designer
                    registerApp(app, app.UIFigure)

                    if nargout == 0
                        clear app
                    end
                end

                % Code that executes before app deletion
                function delete(app)

                    % Delete UIFigure when app is deleted
                    delete(app.UIFigure)
                end
            end
        end
        """;

    private DesignDocument OpenUacLast() => DesignDocument.FromText(UacLastText.ReplaceLineEndings("\n"), Path.Combine(_folder, "UacLast.mlapp"));

    [Fact]
    public void TheMetadataFileIsReadWithTheSchemasDefaults()
    {
        DesignUserComponent gauge = Assert.Single(DesignUserComponents.Read(MetadataJson, @"C:\comps"));
        Assert.Equal("SpinnerGauge", gauge.ClassName);
        Assert.Equal("SpinnerGauge", gauge.ComponentName);
        Assert.Equal("My Components", gauge.Category);
        Assert.Equal("My Components (Custom)", gauge.SectionTitle);
        Assert.Equal("1.0", gauge.Version);
        Assert.Equal([1, 1, 220, 80], gauge.DefaultPosition);
        Assert.Equal((220, 80), gauge.DefaultSize);
        Assert.Equal(@"C:\comps", gauge.Folder);
        Assert.StartsWith("SPINNERGAUGE  A custom", gauge.Description);

        // A hand-written file: the schema's defaults fill in what is missing, a numeric version, the
        // schema's own text for a position; a packaged class keeps its package.
        IReadOnlyList<DesignUserComponent> sparse = DesignUserComponents.Read(
            """{"components": [{"className": "pkg.Thing", "version": 2}, {"className": "Other", "category": "b tools", "defaultPosition": "[100 100 50 30]"}, {"noClass": 1}], "schema": 3}""", @"C:\x");
        Assert.Equal(2, sparse.Count);
        Assert.Equal(("pkg.Thing", "Thing", "Custom Component", "My Category", "2.0"), (sparse[0].ClassName, sparse[0].ShortName, sparse[0].ComponentName, sparse[0].Category, sparse[0].Version));
        Assert.Equal("This is a custom component created by MATLAB user", sparse[0].Description);
        Assert.Equal([0, 0, 100, 50], sparse[0].DefaultPosition); // the client's own fallback for a file without the key
        Assert.Equal((100.0, 50.0), sparse[0].DefaultSize);
        Assert.Equal([100, 100, 50, 30], sparse[1].DefaultPosition);
        Assert.Empty(DesignUserComponents.Read("""{"schema": 3}""", @"C:\x"));

        // Sections: titled with (Custom), ordered without regard to case; entries by name, upper case first.
        var sections = DesignUserComponents.Sections([
            new("Z1", "zeta", "d", "My Components", "", "", "", "1.0", [1, 1, 10, 10], "f"),
            new("A1", "alpha", "d", "My Components", "", "", "", "1.0", [1, 1, 10, 10], "f"),
            new("B1", "Beta", "d", "My Components", "", "", "", "1.0", [1, 1, 10, 10], "f"),
            new("C1", "c", "d", "b tools", "", "", "", "1.0", [1, 1, 10, 10], "f"),
            new("D1", "d", "d", "Able", "", "", "", "1.0", [1, 1, 10, 10], "f")]);
        Assert.Equal(["Able (Custom)", "b tools (Custom)", "My Components (Custom)"], sections.Select(static s => s.Title));
        Assert.Equal(["Beta", "alpha", "zeta"], sections[2].Entries.Select(static e => e.ComponentName));

        // Discovery: the folder's file, a folder without one passed over, a class registered twice the later set's.
        IReadOnlyList<DesignUserComponent> found = DesignUserComponents.Discover([Path.GetTempPath(), _folder]);
        Assert.Equal("SpinnerGauge", Assert.Single(found).ClassName);
        Assert.Equal(Path.GetFullPath(_folder), found[0].Folder);
        string again = Path.Combine(_folder, "again");
        Directory.CreateDirectory(Path.Combine(again, "resources"));
        File.WriteAllText(Path.Combine(again, "resources", "appDesigner.json"), MetadataJson.Replace("220", "300", StringComparison.Ordinal).Replace("\"My Components\"", "\"Other\"", StringComparison.Ordinal));
        IReadOnlyList<DesignUserComponent> twice = DesignUserComponents.Discover([_folder, again]);
        Assert.Equal(("Other", 300.0, Path.GetFullPath(again)), (Assert.Single(twice).Category, twice[0].DefaultSize.Width, twice[0].Folder));
    }

    /// <summary>
    /// A component made by a call of a class the catalog does not know is given its class from the
    /// running class, described as App Designer's adapter describes it: the generator's property
    /// list in properties() order with Position last, the fresh instance's values as defaults, so the
    /// generated lines are the probe's; the Callbacks tab's properties and title; the inspector's groups.
    /// </summary>
    [Fact]
    public void ARunningClassIsDescribedAsAppDesignersAdapterDescribesIt()
    {
        using DesignDocument document = OpenUacLast();
        Assert.Empty(document.ReadProblems);
        Assert.True(document.Runtime.Problems.Count == 0, string.Join(" | ", document.Runtime.Problems));
        DesignComponent gauge = document.Design.Find("Gauge")!;
        DesignComponentClass cls = Assert.IsType<DesignComponentClass>(gauge.Class);
        Assert.True(cls.IsUserComponent);
        Assert.Equal("SpinnerGauge", cls.Type);
        Assert.Equal("SpinnerGauge(app.UIFigure)", cls.CreationCall("app.UIFigure"));
        Assert.Equal(
            ["Value", "Limits", "ValueChangedFcn", "Visible", "BackgroundColor", "SizeChangedFcn", "HandleVisibility", "BusyAction", "Interruptible", "Tag", "Position"],
            cls.Properties.Select(static p => p.Name));
        Assert.Equal([0], Assert.IsType<NumericValue>(cls.Property("Value")!.DefaultFor(DesignTheme.Light)).Data);
        Assert.Equal([0, 100], Assert.IsType<NumericValue>(cls.Property("Limits")!.DefaultFor(DesignTheme.Light)).Data);
        Assert.Equal([10, 10, 220, 80], Assert.IsType<NumericValue>(cls.Property("Position")!.DefaultFor(DesignTheme.Light)).Data);
        Assert.Equal([0.129411764705882, 0.129411764705882, 0.129411764705882], Assert.IsType<NumericValue>(cls.Property("BackgroundColor")!.DefaultFor(DesignTheme.Dark)).Data);
        Assert.True(cls.Property("Visible")!.HasMode);
        Assert.False(cls.Property("Position")!.HasMode);

        // The lines App Designer's generator wrote for this gauge (probe ds7_uac, section 5).
        Assert.Equal(
        [
            "app.Gauge = SpinnerGauge(app.UIFigure);",
            "app.Gauge.Value = 40;",
            "app.Gauge.ValueChangedFcn = createCallbackFcn(app, @GaugeValueChanged, true);",
            "app.Gauge.Position = [20 20 220 80];",
        ], DesignCodeWriter.ComponentCode(gauge));
        Assert.Contains("            app.Gauge.Position = [20 20 220 80];", document.CurrentText);

        // At its default position no line is written; Limits at its default never was.
        Assert.Null(document.SetProperty(gauge, "Position", NumericValue.Row(10, 10, 220, 80)));
        Assert.Equal(3, DesignCodeWriter.ComponentCode(gauge).Count);
        Assert.Null(document.SetProperty(gauge, "Limits", NumericValue.Row(0, 50)));
        Assert.Contains("app.Gauge.Limits = [0 50];", DesignCodeWriter.ComponentCode(gauge));
        Assert.Equal([0, 50], document.Runtime.ReadNumbers(gauge, "Limits")!);

        // The Callbacks tab: the event's property, then SizeChangedFcn, under the class's full name.
        Assert.Equal(["ValueChangedFcn", "SizeChangedFcn"], document.CallbackPropertiesOf(gauge));
        DesignCallbackGroup group = Assert.Single(document.CallbackGroupsOf(gauge));
        Assert.Equal("SpinnerGauge", group.Title);
        Assert.Equal("GaugeValueChanged", document.CallbackOf(gauge, "ValueChangedFcn"));
        Assert.Equal("GaugeSizeChanged", document.DefaultCallbackName(gauge, "SizeChangedFcn"));
        Assert.Equal(["value = app.Gauge.Value;", ""], DesignCallbacks.R2025b.DefaultBody(gauge, "ValueChangedFcn"));

        // The inspector: the class's own properties under its name, the read-only counters shown and
        // not edited, then the six groups every component shows (probe section 3).
        IReadOnlyList<DesignInspectorGroup> groups = document.InspectorGroupsOf(gauge);
        Assert.Equal(["SpinnerGauge", "Color", "Interactivity", "Position", "Callback Execution Control", "Parent / Child", "Identifiers"], groups.Select(static g => g.Title));
        Assert.Equal([true, true, false, false, false, false, false], groups.Select(static g => g.Expanded));
        Assert.Equal(
            [("Value", DesignEditorKind.Value), ("Limits", DesignEditorKind.Value), ("SetupCalls", DesignEditorKind.ReadOnly), ("UpdateCalls", DesignEditorKind.ReadOnly)],
            groups[0].Rows.Select(static r => (r.Editors[0].Name, r.Editors[0].Kind)));
        Assert.Equal(["Visible", "ContextMenu"], groups[2].Rows.Select(static r => r.Editors[0].Name));
        Assert.Equal(DesignEditorKind.Color, groups[1].Rows[0].Editors[0].Kind);

        // The user classes the app uses.
        Assert.Equal(["SpinnerGauge"], DesignUserComponents.ClassesOf(document.Design));
    }

    /// <summary>A library entry from the folder's registration adds a component named after its class, sized by its default position, generated as the probe's lines.</summary>
    [Fact]
    public void AUserEntryAddsAComponentNamedAfterItsClass()
    {
        using DesignDocument document = OpenUacLast();
        DesignPaletteEntry entry = Assert.Single(document.UserPaletteEntries);
        Assert.Equal(("SpinnerGauge", "My Components (Custom)", "SpinnerGauge"), (entry.Name, entry.Category, entry.Type));
        Assert.Equal("user:SpinnerGauge", entry.Key);
        Assert.Same(entry, document.FindPaletteEntry("user:SpinnerGauge"));
        Assert.Equal("Button", document.FindPaletteEntry("Button")!.Name);

        DesignComponent added = Assert.Single(document.Add(entry, document.Design.Figure, new Point2D(30, 40)));
        Assert.Equal("SpinnerGauge", added.CodeName);
        Assert.True(added.Class!.IsUserComponent);
        NumericValue position = Assert.IsType<NumericValue>(added.Get("Position"));
        Assert.Equal([220, 80], position.Data.Skip(2));
        Assert.Equal("app.SpinnerGauge = SpinnerGauge(app.UIFigure);", DesignCodeWriter.ComponentCode(added)[0]);
        Assert.Contains(DesignCodeWriter.ComponentCode(added), static l => l.StartsWith("app.SpinnerGauge.Position = [", StringComparison.Ordinal));
        Assert.Empty(document.Runtime.Problems);
        Assert.NotNull(document.Runtime.ObjectOf(added));
        Assert.Contains("        SpinnerGauge  SpinnerGauge", document.CurrentText);

        DesignComponent second = Assert.Single(document.Add(entry, document.Design.Figure, new Point2D(30, 140), new Size2D(100, 50)));
        Assert.Equal("SpinnerGauge2", second.CodeName);
        Assert.Equal([100, 50], Assert.IsType<NumericValue>(second.Get("Position")).Data.Skip(2));

        // Undone, the component and its object go; redone, they are back.
        document.Undo();
        Assert.Null(document.Design.Find("SpinnerGauge2"));
        Assert.Equal(2, document.Design.Components.Count(static c => c.Type == "SpinnerGauge"));
        document.Redo();
        Assert.NotNull(document.Design.Find("SpinnerGauge2"));
        Assert.NotNull(document.Runtime.ObjectOf(document.Design.Find("SpinnerGauge2")!));

        // Its drop box is its registration's size, with nothing made for it; and it goes into a grid's cells.
        Rect2D box = Assert.Single(document.DropBoxes(entry, new Point2D(5, 6)));
        Assert.Equal((5.0, 6.0, 220.0, 80.0), (box.X, box.Y, box.Width, box.Height));
        DesignComponent grid = Assert.Single(document.Add(document.Palette.Find("Grid Layout")!, document.Design.Figure, new Point2D(10, 10)));
        Assert.Equal("matlab.ui.container.GridLayout", grid.Type);
        DesignComponent inCell = Assert.Single(document.AddToGrid(entry, grid, [new GridSpan(2, 2, 1, 1)]));
        Assert.Equal("SpinnerGauge3", inCell.CodeName);
        Assert.Null(inCell.Get("Position"));
        Assert.Contains("app.SpinnerGauge3.Layout.Row = 2;", DesignCodeWriter.ComponentCode(inCell));
        Assert.Contains("app.SpinnerGauge3 = SpinnerGauge(app.GridLayout);", DesignCodeWriter.ComponentCode(inCell));
        Assert.NotNull(document.Runtime.ObjectOf(inCell));
    }

    /// <summary>The classes the app uses are the package's userComponents element, kept with every save (probe section 5), and the plain-text internal data's list.</summary>
    [Fact]
    public void TheClassesAnAppUsesGoIntoItsMetadata()
    {
        string mlapp = Path.Combine(_folder, "UacLast.mlapp");
        (_, MlappWriteResult written) = AppDesignerDocument.SaveNew(mlapp, UacLastText.ReplaceLineEndings("\n"));
        Assert.True(written.HasDesign);
        Assert.Empty(written.Problems);
        Assert.Contains("<userComponents><userComponent class=\"SpinnerGauge\"/></userComponents>", PartOf(mlapp, "metadata/appMetadata.xml"));

        // The tree holds the gauge as an object of its class: the container's stored names, its own
        // properties set or at their defaults, its bound callback in the mixin's struct, and App
        // Designer's DesignTimeProperties (probe ds7_uac, section 5).
        var problems = new List<string>();
        McosObject root;
        using (DesignDocument described = OpenUacLast())
        {
            root = DesignTreeWriter.Tree(described.Design, DesignCodeStyle.Default, _folder, problems);
        }

        Assert.Empty(problems);
        McosObject gauge = Assert.Single(Reach(root), static o => o.ClassName == "SpinnerGauge");
        Assert.Equal([40], Assert.IsType<MatData.Numbers>(Assert.IsType<McosValue.Value>(gauge.Get("Value")).Data).Data);
        Assert.Null(gauge.Get("Limits")); // at its default: not stored, as R2025b stores only what differs (probe section 5)
        Assert.Equal([20, 20, 220, 80], Assert.IsType<MatData.Numbers>(Assert.IsType<McosValue.Value>(gauge.Get("Position")).Data).Data);
        Assert.Equal("manual", Assert.IsType<McosValue.Word>(gauge.Get("PositionMode")).Text);
        Assert.NotNull(gauge.Get("matlab.ui.container.internal.UIContainer.StorageUnitPos"));
        MatData.Structs callbacks = Assert.IsType<MatData.Structs>(Assert.IsType<McosValue.Value>(gauge.Get("SerializableCallbackPropertyFcns")).Data);
        Assert.Equal(["ValueChangedFcn"], callbacks.Fields);
        Assert.Equal("GaugeValueChanged", Assert.IsType<MatData.Chars>(callbacks.Elements[0][0]).Characters);
        Assert.Null(gauge.Get("ValueChangedFcn"));
        Assert.Contains(gauge.Dynamic, static d => d.Get("DynamicName_") is McosValue.Value { Data: MatData.Chars { Characters: "DesignTimeProperties" } });

        // And the file written from the text alone - with no class to give the defaults - holds that
        // object under the class's own name with what the code set, and the callback in its struct.
        McosObject saved = Assert.Single(ModelOf(mlapp).Objects, static o => o.ClassName == "SpinnerGauge");
        Assert.Equal(["Value", "Position", "SerializableCallbackPropertyFcns"], saved.Entries.Select(static e => e.Name).Where(static n => n is "Value" or "Limits" or "Position" or "SerializableCallbackPropertyFcns" or "ValueChangedFcn"));

        using (DesignDocument document = DesignDocument.Open(mlapp))
        {
            Assert.True(document.Design.Find("Gauge")!.Class!.IsUserComponent);
            Assert.StartsWith("Saved ", document.Save());
            Assert.Contains("<userComponents><userComponent class=\"SpinnerGauge\"/></userComponents>", PartOf(mlapp, "metadata/appMetadata.xml"));

            // The plain-text file lists the class among its internal data and names the component by its class.
            string plain = Path.Combine(_folder, "UacPlain.m");
            Assert.StartsWith("Saved ", document.SaveAs(plain));
            string text = File.ReadAllText(plain);
            Assert.Contains("<SpinnerGauge name='Gauge'>", text);
            Assert.Contains("<ValueChangedFcn>GaugeValueChanged</ValueChangedFcn>", text);
            Assert.Contains("<Value>40</Value>", text);
            Assert.Contains("<Position>[20 20 220 80]</Position>", text);
            Assert.Contains("<UserComponents>\n        <Product>SpinnerGauge</Product>\n    </UserComponents>", text);
            Assert.Contains("        Gauge     SpinnerGauge", text);

            // The plain-text file reopened: the gauge gets its class from the running class, its value kept.
            using (DesignDocument fromPlain = DesignDocument.Open(plain))
            {
                DesignComponent readBack = fromPlain.Design.Find("Gauge")!;
                Assert.True(readBack.Class!.IsUserComponent);
                Assert.Equal([40], Assert.IsType<NumericValue>(readBack.Get("Value")).Data);
                Assert.Equal("GaugeValueChanged", fromPlain.CallbackOf(readBack, "ValueChangedFcn"));
                Assert.Empty(fromPlain.Runtime.Problems);
            }

            // Without the component, the list goes.
            document.Delete([document.Design.Find("Gauge")!]);
            Assert.StartsWith("Saved ", document.Save());
            Assert.DoesNotContain("<UserComponents>", File.ReadAllText(plain));
        }

        using (DesignDocument reopened = DesignDocument.Open(Path.Combine(_folder, "UacPlain.m")))
        {
            Assert.Null(reopened.Design.Find("Gauge"));
        }

        // An .mlapp whose gauge went: the element is emptied.
        using (DesignDocument document = DesignDocument.Open(mlapp))
        {
            document.Delete([document.Design.Find("Gauge")!]);
            Assert.StartsWith("Saved ", document.Save());
        }

        Assert.Contains("<userComponents/>", PartOf(mlapp, "metadata/appMetadata.xml"));
    }

    /// <summary>
    /// A class the runtime cannot find stays a component read as written - no class, a runtime
    /// problem naming it, its library entry refused - and the app reports it missing, as App Designer's
    /// validator does; one it finds whose setup fails it reports errored. Open Anyway takes them out:
    /// the callback they named stays, unbound, and nothing is marked changed.
    /// </summary>
    [Fact]
    public void AClassOffThePathIsReportedMissingAndCanBeTakenOut()
    {
        File.Delete(Path.Combine(_folder, "SpinnerGauge.m"));
        using (DesignDocument document = OpenUacLast())
        {
            DesignComponent gauge = document.Design.Find("Gauge")!;
            Assert.Null(gauge.Class);
            Assert.Equal("SpinnerGauge", gauge.Type);
            Assert.Contains(document.Runtime.Problems, static p => p.StartsWith("Gauge:", StringComparison.Ordinal));
            Assert.Equal("app.Gauge = SpinnerGauge(app.UIFigure);", DesignCodeWriter.ComponentCode(gauge)[0]);
            Assert.Equal(["SpinnerGauge"], DesignUserComponents.ClassesOf(document.Design));
            Assert.Throws<InvalidOperationException>(() => document.Add(Assert.Single(document.UserPaletteEntries), document.Design.Figure, new Point2D(0, 0)));
            Assert.Equal(["SpinnerGauge"], document.MissingUserComponents);
            Assert.Empty(document.ErroredUserComponents);

            document.RemoveUnavailableUserComponents();
            Assert.Null(document.Design.Find("Gauge"));
            Assert.Empty(document.MissingUserComponents);
            Assert.Empty(document.Runtime.Problems);
            Assert.False(document.IsDirty);
            Assert.False(document.CanUndo);
            Assert.Contains(document.Callbacks, static c => c.Name == "GaugeValueChanged");
            Assert.DoesNotContain("app.Gauge", document.CurrentText.Replace("app.Gauge.Value = app.Gauge.Value + 10;", string.Empty, StringComparison.Ordinal));
            Assert.Empty(DesignUserComponents.ClassesOf(document.Design));
        }

        // A class whose setup refuses: found, so errored.
        File.WriteAllText(Path.Combine(_folder, "SpinnerGauge.m"), """
            classdef SpinnerGauge < matlab.ui.componentcontainer.ComponentContainer
                properties
                    Value (1,1) double = 0
                end
                events (HasCallbackProperty, NotifyAccess = protected)
                    ValueChanged
                end
                methods (Access = protected)
                    function setup(~)
                        error('ds7:fail', 'refused on purpose');
                    end
                    function update(~)
                    end
                end
            end
            """);
        using (DesignDocument document = OpenUacLast())
        {
            Assert.Empty(document.MissingUserComponents);
            Assert.Equal(["SpinnerGauge"], document.ErroredUserComponents);
            Assert.Contains(document.Runtime.Problems, static p => p.Contains("Caused by: refused on purpose", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A class registered in a folder on the session's path, not the app's: found and described
    /// from there, its registration in the library; a class file that does not parse is reported
    /// errored, not thrown; an app saved over its tree keeps a rebound callback and a changed value.
    /// </summary>
    [Fact]
    public void AClassOnThePathIsFoundAndASaveOverTheTreeKeepsItsCallback()
    {
        string lib = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "resources"));
        File.Move(Path.Combine(_folder, "SpinnerGauge.m"), Path.Combine(lib, "SpinnerGauge.m"));
        File.Move(Path.Combine(_folder, "resources", "appDesigner.json"), Path.Combine(lib, "resources", "appDesigner.json"));
        string mlapp = Path.Combine(_folder, "UacLast.mlapp");
        AppDesignerDocument.SaveNew(mlapp, UacLastText.ReplaceLineEndings("\n"));
        using (DesignDocument document = DesignDocument.Open(mlapp, [lib]))
        {
            Assert.Empty(document.MissingUserComponents);
            Assert.True(document.Design.Find("Gauge")!.Class!.IsUserComponent);
            Assert.Equal(["SpinnerGauge"], document.UserPaletteEntries.Select(static e => e.Name));
            Assert.Equal(Path.GetFullPath(lib), document.UserPaletteEntries[0].UserComponent!.Folder);

            // Saved over its tree with the gauge moved, its value changed and its callback rebound.
            DesignComponent gauge = document.Design.Find("Gauge")!;
            Assert.Null(document.SetProperty(gauge, "Value", NumericValue.Scalar(55)));
            Assert.Null(document.SetProperty(gauge, "Position", NumericValue.Row(30, 40, 200, 70)));
            Assert.Null(document.BindCallback(gauge, "ValueChangedFcn", "GoButtonPushed"));
            Assert.StartsWith("Saved ", document.Save());
        }

        McosObject saved = Assert.Single(ModelOf(mlapp).Objects, static o => o.ClassName == "SpinnerGauge");
        Assert.Contains("SerializableCallbackPropertyFcns", saved.Entries.Select(static e => e.Name));
        using (DesignDocument reopened = DesignDocument.Open(mlapp, [lib]))
        {
            DesignComponent gauge = reopened.Design.Find("Gauge")!;
            Assert.Equal("GoButtonPushed", reopened.CallbackOf(gauge, "ValueChangedFcn"));
            Assert.Equal([55], Assert.IsType<NumericValue>(gauge.Get("Value")).Data);
            Assert.Equal([30, 40, 200, 70], reopened.Runtime.ReadNumbers(gauge, "Position")!);
        }

        // Without the path, the class is missing; with a class file that does not parse, errored.
        using (DesignDocument document = DesignDocument.Open(mlapp))
        {
            Assert.Equal(["SpinnerGauge"], document.MissingUserComponents);
        }

        File.WriteAllText(Path.Combine(lib, "SpinnerGauge.m"), "classdef SpinnerGauge < matlab.ui.componentcontainer.ComponentContainer\n    properties\n        Value (1,1) double = 0\n    end\n    methods (Access = protected)\n        function setup(comp)\n            if\n        end\n    end\nend\n");
        using (DesignDocument document = DesignDocument.Open(mlapp, [lib]))
        {
            Assert.Equal(["SpinnerGauge"], document.ErroredUserComponents);
            Assert.Empty(document.MissingUserComponents);
        }
    }

    /// <summary>A class with typed numbers and a char: each in the inspector as a number or text, each generated as the design value's text.</summary>
    [Fact]
    public void TypedNumbersAndTextAreInspectedAndWritten()
    {
        File.WriteAllText(Path.Combine(_folder, "Ds7Int.m"), """
            classdef Ds7Int < matlab.ui.componentcontainer.ComponentContainer
                properties
                    Count int32 = 1
                    Ratio single = 1.5
                    Label char = 'lbl'
                    Flag logical = false
                    Data struct = struct('a', 1)
                end
                events (HasCallbackProperty, NotifyAccess = protected)
                    Changed
                end
                methods (Access = protected)
                    function setup(comp)
                        comp.Position = [10 10 120 40];
                    end
                    function update(~)
                    end
                end
            end
            """);
        string text = UacLastText.ReplaceLineEndings("\n").Replace("SpinnerGauge", "Ds7Int", StringComparison.Ordinal).Replace("app.Gauge.Value = 40;", "app.Gauge.Count = 5;", StringComparison.Ordinal);
        using DesignDocument document = DesignDocument.FromText(text, Path.Combine(_folder, "UacLast.mlapp"));
        DesignComponent gauge = document.Design.Find("Gauge")!;
        Assert.True(gauge.Class!.IsUserComponent);
        Assert.Equal(["Count", "Ratio", "Label", "Flag", "Data", "ChangedFcn"], gauge.Class.Properties.Take(6).Select(static p => p.Name));
        Assert.Equal([1], Assert.IsType<NumericValue>(gauge.Class.Property("Count")!.DefaultFor(DesignTheme.Light)).Data);
        Assert.Equal([1.5], Assert.IsType<NumericValue>(gauge.Class.Property("Ratio")!.DefaultFor(DesignTheme.Light)).Data);
        Assert.Null(gauge.Class.Property("Data")!.DefaultFor(DesignTheme.Light));
        IReadOnlyList<DesignInspectorGroup> groups = document.InspectorGroupsOf(gauge);
        Assert.Equal(
            [("Count", DesignEditorKind.Value), ("Ratio", DesignEditorKind.Value), ("Label", DesignEditorKind.Text), ("Flag", DesignEditorKind.Logical)],
            groups[0].Rows.Select(static r => (r.Editors[0].Name, r.Editors[0].Kind)));
        Assert.Contains("app.Gauge.Count = 5;", DesignCodeWriter.ComponentCode(gauge));
        Assert.Null(document.SetProperty(gauge, "Ratio", NumericValue.Scalar(2.5)));
        Assert.Equal([2.5], document.Runtime.ReadNumbers(gauge, "Ratio")!);
        Assert.Equal(["ChangedFcn", "SizeChangedFcn"], document.CallbackPropertiesOf(gauge));
    }

    /// <summary>
    /// A packaged class in a plain-text file: written by its last segment, as App Designer's writer
    /// names every element (R2025b's own loader then cannot find the class - its limitation, probe
    /// ds7_uac section 7), and read back by the declared class, which wins over the element's name.
    /// </summary>
    [Fact]
    public void APackagedClassIsWrittenByItsLastSegmentAndReadByItsDeclaration()
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        var design = new AppDesign(figure);
        var gauge = new DesignComponent("PG", "ds7pkg.PkgGauge", "ds7pkg.PkgGauge", string.Empty);
        gauge.Set("Value", NumericValue.Scalar(5));
        figure.Add(gauge);
        var code = new DesignCode("UacPkg");
        string text = DesignPlainText.Render(design, code, new DesignPlainTextDetails { MatlabRelease = string.Empty, HasThumbnail = false });
        Assert.Contains("            <PkgGauge name='PG'>\n                <Value>5</Value>\n            </PkgGauge>", text);
        Assert.Contains("        PG        ds7pkg.PkgGauge", text);
        Assert.Contains("    <UserComponents>\n        <Product>ds7pkg.PkgGauge</Product>\n    </UserComponents>", text);

        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        Assert.Empty(read.Read.Problems);
        DesignComponent back = read.Read.Design!.Find("PG")!;
        Assert.Equal(("ds7pkg.PkgGauge", "ds7pkg.PkgGauge"), (back.Type, back.Function));
        Assert.Equal("app.PG = ds7pkg.PkgGauge(app.UIFigure);", DesignCodeWriter.ComponentCode(back)[0]);
    }

    /// <summary>
    /// With <c>JGRAPH_DS7_OUT</c> naming a folder, the app the probe <c>ds7_uac_verify</c> reads and
    /// runs in R2025b is written there as <c>uac/jgraph/UacDesigned.mlapp</c> - UacLast saved by this
    /// build, its gauge's class beside it - with the same app as a plain-text file.
    /// </summary>
    [Fact]
    public void TheProbesAppIsWrittenWhenAsked()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS7_OUT");
        if (outFolder is null)
        {
            return;
        }

        string folder = Path.Combine(outFolder, "uac", "jgraph");
        Directory.CreateDirectory(folder);
        File.Copy(Helper("SpinnerGauge.m"), Path.Combine(folder, "SpinnerGauge.m"), overwrite: true);
        string text = UacLastText.ReplaceLineEndings("\n").Replace("UacLast", "UacDesigned", StringComparison.Ordinal);
        AppDesignerDocument.SaveNew(Path.Combine(folder, "UacDesigned.mlapp"), text);
        using (DesignDocument document = DesignDocument.Open(Path.Combine(folder, "UacDesigned.mlapp")))
        {
            Assert.StartsWith("Saved ", document.SaveAs(Path.Combine(folder, "UacDesignedPlain.m")));
        }

        // And the app edited over its saved tree (the update path): the gauge moved off the base's
        // position, its value changed, its callback bound to another function.
        using (DesignDocument document = DesignDocument.Open(Path.Combine(folder, "UacDesigned.mlapp")))
        {
            DesignComponent gauge = document.Design.Find("Gauge")!;
            Assert.Null(document.SetProperty(gauge, "Value", NumericValue.Scalar(55)));
            Assert.Null(document.SetProperty(gauge, "Position", NumericValue.Row(30, 40, 200, 70)));
            Assert.Null(document.BindCallback(gauge, "ValueChangedFcn", "GoButtonPushed"));
            Assert.StartsWith("Saved ", document.SaveAs(Path.Combine(folder, "UacEdited.mlapp")));
        }
    }

    /// <summary>Every object <paramref name="root"/> refers to, itself first.</summary>
    private static IEnumerable<McosObject> Reach(McosObject root)
    {
        var seen = new HashSet<McosObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<McosObject>([root]);
        while (stack.Count > 0)
        {
            McosObject o = stack.Pop();
            if (!seen.Add(o))
            {
                continue;
            }

            yield return o;
            foreach ((_, McosValue value) in o.Entries)
            {
                if (value is McosValue.Refs refs)
                {
                    foreach (McosObject target in refs.Objects)
                    {
                        stack.Push(target);
                    }
                }
            }
        }
    }

    private static MatMcosFile ModelOf(string mlapp)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(mlapp);
        using var stream = new MemoryStream();
        zip.GetEntry("appdesigner/appModel.mat")!.Open().CopyTo(stream);
        return MatMcosFile.Read(stream.ToArray(), out string? why) ?? throw new InvalidOperationException(why);
    }

    private static string PartOf(string mlapp, string part)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(mlapp);
        using var reader = new StreamReader(zip.GetEntry(part)!.Open());
        return reader.ReadToEnd();
    }
}
