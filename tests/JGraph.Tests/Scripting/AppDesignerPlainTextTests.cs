using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Designer plan DS7a: App Designer's plain-text app format as a second output. The writer is held
/// against the lines App Designer's own unmodified XML builders render under node (the oracle in
/// <c>tools/matlab-checklist/ui-probes/ds7/oracle</c>, its cases copied here); the reader against the
/// writer over the designer's fixture and the 44 apps R2025b ships; the document over a file on disk.
/// </summary>
public class AppDesignerPlainTextTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    /// <summary>A design of the figure App Designer's new app starts with, as the oracle's cases hold it.</summary>
    private static AppDesign NewFigure(string name = "UIFigure")
    {
        var figure = new DesignComponent(name, DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Name", new CharValue("MATLAB App"));
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        return new AppDesign(figure);
    }

    private static DesignComponent Make(string name, string type, DesignComponent parent)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
        parent.Add(component);
        return component;
    }

    /// <summary>Details that add no section: what an app written by hand without them has.</summary>
    private static DesignPlainTextDetails NoDetails() => new() { MatlabRelease = string.Empty, HasThumbnail = false };

    private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    // --- the oracle's cases ---------------------------------------------------------------------------

    [Fact]
    public void AFigureAloneIsWrittenAsAppDesignersBuildersWriteIt()
    {
        string text = DesignPlainText.Render(NewFigure(), new DesignCode("app1"), NoDetails());
        Assert.Equal(
            """
            classdef app1 < matlab.apps.App

                % Properties that correspond to app components
                properties (Access = public)
                    UIFigure  matlab.ui.Figure
                end

            end

            %[appendix]
            %---
            %[app:layout]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <Components>
                <UIFigure name='UIFigure'>
                    <Name>'MATLAB App'</Name>
                    <Position>[100 100 640 480]</Position>
                </UIFigure>
            </Components>
            %}

            %---
            %[app:internalData]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <InternalData>
                <AppType>Standard</AppType>
            </InternalData>
            %}
            """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void AComponentsPropertiesAreSortedByNameAndACallbackIsItsBareName()
    {
        AppDesign design = NewFigure();
        DesignComponent button = Make("GoButton", "matlab.ui.control.Button", design.Figure);
        button.Set("Text", new CharValue("Go"));
        button.Set("Position", NumericValue.Row(20, 20, 100, 22));
        button.Set("ButtonPushedFcn", new CallbackValue("GoButtonPushed"));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), NoDetails());
        Assert.Equal(
        [
            "", "%[appendix]", "%---", "%[app:layout]", "%{", "<?xml version='1.0' encoding='UTF-8'?>", "<Components>",
            "    <UIFigure name='UIFigure'>",
            "        <Name>'MATLAB App'</Name>",
            "        <Position>[100 100 640 480]</Position>",
            "        <Children>",
            "            <Button name='GoButton'>",
            "                <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>",
            "                <Position>[20 20 100 22]</Position>",
            "                <Text>'Go'</Text>",
            "            </Button>",
            "        </Children>",
            "    </UIFigure>",
            "</Components>", "%}",
            .. StandardInternalData,
        ], lines);
    }

    [Fact]
    public void GridChildrenCarryTheirCellsAndNoPosition()
    {
        AppDesign design = NewFigure();
        DesignComponent grid = Make("GridLayout", "matlab.ui.container.GridLayout", design.Figure);
        grid.Set("ColumnWidth", CellValue.Row(new CharValue("2x"), new CharValue("1x")));
        grid.Set("RowHeight", CellValue.Row(new CharValue("1x")));
        DesignComponent a = Make("Button", "matlab.ui.control.Button", grid);
        a.Set("Text", new CharValue("A"));
        a.Set("Position", NumericValue.Row(1, 1, 100, 22)); // dropped: a grid child has no position
        a.Set("Layout.Row", NumericValue.Scalar(1));
        a.Set("Layout.Column", NumericValue.Scalar(1));
        DesignComponent b = Make("Button2", "matlab.ui.control.Button", grid);
        b.Set("Text", new CharValue("B"));
        b.Set("Layout.Row", NumericValue.Scalar(1));
        b.Set("Layout.Column", NumericValue.Scalar(2));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), NoDetails());
        Assert.Equal(
        [
            "            <GridLayout name='GridLayout'>",
            "                <ColumnWidth>{'2x', '1x'}</ColumnWidth>",
            "                <RowHeight>{'1x'}</RowHeight>",
            "                <Children>",
            "                    <Button name='Button'>",
            "                        <Layout.Column>1</Layout.Column>",
            "                        <Layout.Row>1</Layout.Row>",
            "                        <Text>'A'</Text>",
            "                    </Button>",
            "                    <Button name='Button2'>",
            "                        <Layout.Column>2</Layout.Column>",
            "                        <Layout.Row>1</Layout.Row>",
            "                        <Text>'B'</Text>",
            "                    </Button>",
            "                </Children>",
            "            </GridLayout>",
        ], lines.Skip(11).Take(16));
    }

    [Fact]
    public void ValuesAreEscapedAsTheXmlBuilderEscapesThem()
    {
        AppDesign design = NewFigure();
        DesignComponent label = Make("Label", "matlab.ui.control.Label", design.Figure);
        label.Set("Text", new CharValue("a & b <c>"));
        label.Set("Tooltip", new CharValue("'tip"));
        DesignComponent area = Make("TextArea", "matlab.ui.control.TextArea", design.Figure);
        area.Set("Value", new CharValue("first\nsecond"));
        DesignComponent field = Make("EditField", "matlab.ui.control.EditField", design.Figure);
        field.Set("Value", new CharValue("it's"));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), NoDetails());
        Assert.Equal(
        [
            "            <Label name='Label'>",
            "                <Text>'a &amp; b &lt;c&gt;'</Text>",
            "                <Tooltip>'&apos;tip'</Tooltip>",
            "            </Label>",
            "            <TextArea name='TextArea'>",
            "                <Value>['first' newline 'second']</Value>", // App Designer writes 'first\nsecond' and never reads the break back
            "            </TextArea>",
            "            <EditField name='EditField'>",
            "                <Value>'it&apos;s'</Value>",
            "            </EditField>",
        ], lines.Skip(11).Take(10));
        Assert.Equal("'&apos;'", DesignPlainText.Escape("'"));
        Assert.Equal("''", DesignPlainText.Escape("''"));
        Assert.Equal("'a]]&gt;b'", DesignPlainText.Escape("'a]]>b'"));
        Assert.Equal("tip'", DesignPlainText.Escape("tip'"));
        Assert.Equal("{'a', 'b'}", DesignPlainText.Escape("{'a', 'b'}"));
    }

    [Fact]
    public void AContextMenuIsAChildAndAnAssignmentByCodeName()
    {
        AppDesign design = NewFigure();
        DesignComponent menu = Make("ContextMenu", "matlab.ui.container.ContextMenu", design.Figure);
        DesignComponent item = Make("Menu", "matlab.ui.container.Menu", menu);
        item.Set("Text", new CharValue("Menu"));
        DesignComponent button = Make("Button", "matlab.ui.control.Button", design.Figure);
        button.Set("Position", NumericValue.Row(20, 20, 100, 22));
        button.Set("ContextMenu", new ComponentsValue(["ContextMenu"], bracketed: false));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), NoDetails());
        Assert.Equal(
        [
            "            <ContextMenu name='ContextMenu'>",
            "                <Children>",
            "                    <Menu name='Menu'>",
            "                        <Text>'Menu'</Text>",
            "                    </Menu>",
            "                </Children>",
            "            </ContextMenu>",
            "            <Button name='Button'>",
            "                <ContextMenu>ContextMenu</ContextMenu>",
            "                <Position>[20 20 100 22]</Position>",
            "            </Button>",
        ], lines.Skip(11).Take(11));
    }

    [Fact]
    public void NestedContainersIndentTwoLevelsEach()
    {
        AppDesign design = NewFigure();
        DesignComponent panel = Make("Panel", "matlab.ui.container.Panel", design.Figure);
        panel.Set("Position", NumericValue.Row(10, 10, 300, 300));
        panel.Set("Title", new CharValue("Panel"));
        DesignComponent grid = Make("GridLayout", "matlab.ui.container.GridLayout", panel);
        grid.Set("ColumnWidth", CellValue.Row(new CharValue("1x")));
        grid.Set("RowHeight", CellValue.Row(new CharValue("1x")));
        DesignComponent group = Make("TabGroup", "matlab.ui.container.TabGroup", grid);
        group.Set("Layout.Row", NumericValue.Scalar(1));
        group.Set("Layout.Column", NumericValue.Scalar(1));
        DesignComponent tab = Make("Tab", "matlab.ui.container.Tab", group);
        tab.Set("Title", new CharValue("Tab"));
        DesignComponent button = Make("Button", "matlab.ui.control.Button", tab);
        button.Set("Position", NumericValue.Row(10, 10, 100, 22));
        List<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), NoDetails()).ToList();
        int at = lines.FindIndex(static l => l.EndsWith("<Button name='Button'>", StringComparison.Ordinal));
        Assert.Equal("                                            <Button name='Button'>", lines[at]); // depth 9: 36 spaces
        Assert.Equal("                                                <Position>[10 10 100 22]</Position>", lines[at + 1]);
        Assert.Equal("                                            </Button>", lines[at + 2]);
        Assert.Equal("                                        <Children>", lines[at - 1]);
        Assert.Equal("                                        <Title>'Tab'</Title>", lines[at - 2]);
        Assert.Equal("                                    <Tab name='Tab'>", lines[at - 3]);
        Assert.Equal("%}", lines[^1]);
    }

    [Fact]
    public void ACheckBoxTreesCheckedNodesAreBareCodeNamesBetweenBrackets()
    {
        AppDesign design = NewFigure();
        DesignComponent tree = Make("Tree", "matlab.ui.container.CheckBoxTree", design.Figure);
        tree.Set("Position", NumericValue.Row(20, 20, 150, 300));
        DesignComponent node = Make("Node", "matlab.ui.container.TreeNode", tree);
        node.Set("Text", new CharValue("Node"));
        DesignComponent node2 = Make("Node2", "matlab.ui.container.TreeNode", tree);
        node2.Set("Text", new CharValue("Node2"));
        tree.Set("CheckedNodes", new ComponentsValue(["Node", "Node2"], bracketed: true));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), NoDetails());
        Assert.Contains("                <CheckedNodes>[Node Node2]</CheckedNodes>", lines);

        // And read back as the nodes it names, as R2025b's loader reads them.
        string text = DesignPlainText.Render(design, new DesignCode("app1"), NoDetails());
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        Assert.Empty(read.Read.Problems);
        ComponentsValue back = Assert.IsType<ComponentsValue>(read.Read.Design!.Find("Tree")!.Get("CheckedNodes"));
        Assert.Equal(["Node", "Node2"], back.CodeNames);
        Assert.True(back.Bracketed);
    }

    /// <summary>The oracle's whole-file case 13, with the run configuration written once where R2025b writes it twice.</summary>
    private static (AppDesign Design, DesignCode Code, DesignPlainTextDetails Details) Plain1()
    {
        AppDesign design = NewFigure();
        DesignComponent button = Make("GoButton", "matlab.ui.control.Button", design.Figure);
        button.Set("Text", new CharValue("Go"));
        button.Set("Position", NumericValue.Row(20, 20, 100, 22));
        button.Set("ButtonPushedFcn", new CallbackValue("GoButtonPushed"));
        var code = new DesignCode("Plain1") { InputParameters = "a, b", SingleInstance = true };
        code.HeadComments.Add("    %PLAIN1 A plain app");
        code.EditableSection.AddRange(["", "    properties (Access = private)", "        Property % Description", "    end", ""]);
        code.Callbacks.Add(new DesignCallback("startupFcn", [""], isStartup: true));
        code.Callbacks.Add(new DesignCallback("GoButtonPushed", ["            x = 1;"]));
        var details = new DesignPlainTextDetails { Name = "Plain1", Version = "1.0", MatlabRelease = string.Empty, HasThumbnail = false };
        return (design, code, details);
    }

    private const string Plain1Text = """
        classdef Plain1 < matlab.apps.App
            %PLAIN1 A plain app

            % Properties that correspond to app components
            properties (Access = public)
                UIFigure  matlab.ui.Figure
                GoButton  matlab.ui.control.Button
            end


            properties (Access = private)
                Property % Description
            end


            % Callbacks that handle component events
            methods

                % Code that executes after component creation
                function startupFcn(app, a, b)

                end

                % Button pushed function: GoButton
                function GoButtonPushed(app, event)
                    x = 1;
                end
            end

        end

        %[appendix]
        %---
        %[app:layout]
        %{
        <?xml version='1.0' encoding='UTF-8'?>
        <Components>
            <UIFigure name='UIFigure'>
                <Name>'MATLAB App'</Name>
                <Position>[100 100 640 480]</Position>
                <Children>
                    <Button name='GoButton'>
                        <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>
                        <Position>[20 20 100 22]</Position>
                        <Text>'Go'</Text>
                    </Button>
                </Children>
            </UIFigure>
        </Components>
        %}

        %---
        %[app:runConfiguration]
        %{
        <?xml version='1.0' encoding='UTF-8'?>
        <RunConfiguration>
            <SingleRunningInstance>true</SingleRunningInstance>
            <StartupFcn>startupFcn</StartupFcn>
        </RunConfiguration>
        %}

        %---
        %[app:appDetails]
        %{
        <?xml version='1.0' encoding='UTF-8'?>
        <AppDetails>
            <Name>Plain1</Name>
            <Version>1.0</Version>
        </AppDetails>
        %}

        %---
        %[app:internalData]
        %{
        <?xml version='1.0' encoding='UTF-8'?>
        <InternalData>
            <AppType>Standard</AppType>
        </InternalData>
        %}
        """;

    /// <summary>The internal data section every app has, since its type is always set (the oracle's case 11f).</summary>
    private static readonly string[] StandardInternalData =
    [
        "", "%---", "%[app:internalData]", "%{", "<?xml version='1.0' encoding='UTF-8'?>", "<InternalData>",
        "    <AppType>Standard</AppType>", "</InternalData>", "%}",
    ];

    [Fact]
    public void AWholeFileHasTheClassWithoutGeneratedMethodsThenTheSectionsInOrder()
    {
        (AppDesign design, DesignCode code, DesignPlainTextDetails details) = Plain1();
        Assert.Equal(Plain1Text.ReplaceLineEndings("\n"), DesignPlainText.Render(design, code, details));
    }

    [Fact]
    public void TheIndentSizeIsTheClassesForTheXmlToo()
    {
        AppDesign design = NewFigure();
        DesignComponent panel = Make("Panel", "matlab.ui.container.Panel", design.Figure);
        panel.Set("Position", NumericValue.Row(10, 10, 300, 300));
        DesignComponent button = Make("GoButton", "matlab.ui.control.Button", panel);
        button.Set("Text", new CharValue("Go"));
        button.Set("ButtonPushedFcn", new CallbackValue("GoButtonPushed"));
        var code = new DesignCode("Plain1") { InputParameters = "a, b", SingleInstance = true };
        code.HeadComments.Add("  %PLAIN1 A plain app");
        code.Callbacks.Add(new DesignCallback("startupFcn", ["      "], isStartup: true));
        code.Callbacks.Add(new DesignCallback("GoButtonPushed", ["        x = 1;"]));
        var details = new DesignPlainTextDetails { Name = "Plain1", Version = "1.0", MatlabRelease = string.Empty, HasThumbnail = false };
        List<string> lines = Lines(DesignPlainText.Render(design, code, details, new DesignCodeStyle("  "))).ToList();
        Assert.Equal("  % Properties that correspond to app components", lines[3]);
        Assert.Equal("  methods", lines[11]);
        Assert.Contains("  <UIFigure name='UIFigure'>", lines);
        Assert.Contains("      <Panel name='Panel'>", lines);
        Assert.Contains("          <Button name='GoButton'>", lines);
        Assert.Contains("            <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>", lines);
        Assert.Contains("  <SingleRunningInstance>true</SingleRunningInstance>", lines);
        Assert.Contains("  <AppType>Standard</AppType>", lines);
    }

    [Fact]
    public void InternalDataListsAreProductChildrenAndTheThumbnailEndsTheFile()
    {
        var details = new DesignPlainTextDetails
        {
            AppId = "1b2c3d4e-0000-1111-2222-333344445555",
            MinimumSupportedMatlabRelease = "R2018a",
        };
        details.RequiredProducts.Add("Signal Processing Toolbox");
        details.UserComponents.AddRange(["SpinnerGauge", "pkg.Thing"]);

        // The images listed are the design's (an image taken out of the app leaves the list).
        AppDesign design = NewFigure();
        Make("Logo", "matlab.ui.control.Image", design.Figure).Set("ImageSource", new ImageFileValue(["images", "logo.png"]));
        Make("Go", "matlab.ui.control.Button", design.Figure).Set("Icon", new ImageFileValue(["icons", "a.svg"]));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), details);
        Assert.Equal(
        [
            "",
            "%---",
            "%[app:internalData]",
            "%{",
            "<?xml version='1.0' encoding='UTF-8'?>",
            "<InternalData>",
            "    <AppId>1b2c3d4e-0000-1111-2222-333344445555</AppId>",
            "    <AppType>Standard</AppType>",
            "    <ImageRelativePaths>",
            "        <Product>images/logo.png</Product>",
            "        <Product>icons/a.svg</Product>",
            "    </ImageRelativePaths>",
            "    <MATLABRelease>R2025b</MATLABRelease>",
            "    <MinimumSupportedMATLABRelease>R2018a</MinimumSupportedMATLABRelease>",
            "    <RequiredProducts>",
            "        <Product>Signal Processing Toolbox</Product>",
            "    </RequiredProducts>",
            "    <UserComponents>",
            "        <Product>SpinnerGauge</Product>",
            "        <Product>pkg.Thing</Product>",
            "    </UserComponents>",
            "</InternalData>",
            "%}",
            "",
            "%---",
            "%[app:thumbnail]",
            "%{",
            "<!-- Thumbnail is used by file previewers. To change how the thumbnail is captured or stored, use the App Details dialog box in App Designer. -->",
            "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>",
            "<Thumbnail autoCapture='true'></Thumbnail>",
            "%}",
        ], lines.Skip(lines.ToList().IndexOf("%[app:internalData]") - 2));

        // And every one of them comes back.
        string text = DesignPlainText.Render(design, new DesignCode("app1"), details);
        DesignPlainTextDetails back = DesignPlainText.Read(text).Details;
        Assert.Equal(details.AppId, back.AppId);
        Assert.Equal("R2025b", back.MatlabRelease);
        Assert.Equal(["Signal Processing Toolbox"], back.RequiredProducts);
        Assert.Equal(["SpinnerGauge", "pkg.Thing"], back.UserComponents);
        Assert.Equal(["images/logo.png", "icons/a.svg"], back.ImageRelativePaths);
        Assert.True(back.HasThumbnail);
        Assert.True(back.ThumbnailAutoCapture);
        Assert.Equal(string.Empty, back.ThumbnailDataUri);
    }

    [Fact]
    public void AppDetailsAreWrittenWhenSetAndReadBackWithTheirLineBreaks()
    {
        var details = new DesignPlainTextDetails
        {
            Name = "My App",
            Author = "Ann & Bob",
            Version = "2.1",
            Summary = "Short <summary>",
            Description = "Line one\nLine two",
            MatlabRelease = string.Empty,
            HasThumbnail = false,
        };
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(NewFigure(), new DesignCode("app1"), details);
        // A description's line break is kept raw, so its element runs over two lines of the file.
        Assert.Equal(
        [
            "<AppDetails>",
            "    <Author>Ann &amp; Bob</Author>",
            "    <Description>Line one\nLine two</Description>",
            "    <Name>My App</Name>",
            "    <Summary>Short &lt;summary&gt;</Summary>",
            "    <Version>2.1</Version>",
            "</AppDetails>",
        ], lines.Skip(18).Take(7));
        DesignPlainTextDetails back = DesignPlainText.Read(DesignPlainText.Render(NewFigure(), new DesignCode("app1"), details)).Details;
        Assert.Equal("Ann & Bob", back.Author);
        Assert.Equal("Line one\nLine two", back.Description);
        Assert.Equal("Short <summary>", back.Summary);
    }

    // --- the layout and the code model ----------------------------------------------------------------

    [Fact]
    public void TheLayoutOfAPlainTextClassHasItsRegionsAndReadsTheRunConfiguration()
    {
        JgsMlappLayout layout = JgsMlappLayout.Of(Plain1Text.ReplaceLineEndings("\n"));
        Assert.True(layout.IsAppLayout);
        Assert.True(layout.IsPlainText);
        Assert.True(layout.IsComplete);
        Assert.Equal("Plain1", layout.ClassName);
        Assert.True(layout.SingleInstance);
        Assert.Equal("a, b", layout.InputParameters);
        Assert.Equal("startupFcn", layout.Startup?.Name);
        Assert.Equal(["GoButtonPushed"], layout.Callbacks.Select(static c => c.Name));
        Assert.Equal(["", "    properties (Access = private)", "        Property % Description", "    end", ""], layout.EditableSectionCode);

        // Everything from the last callback's end on is generated, the appendix with it.
        (int first, int count) = layout.GeneratedSpans[^1];
        Assert.Equal(layout.Lines.Count, first + count);
        Assert.Contains("end", layout.Lines.Skip(first));
        Assert.Contains("%[appendix]", layout.Lines.Skip(first));
        Assert.DoesNotContain("            x = 1;", layout.Lines.Skip(first));

        DesignCode code = DesignCode.Read(Plain1Text.ReplaceLineEndings("\n"))!;
        Assert.True(code.SingleInstance);
        Assert.Equal("a, b", code.InputParameters);
        Assert.Equal(["            x = 1;"], code.Find("GoButtonPushed")!.Body);
    }

    [Fact]
    public void TheRunConfigurationsWordOutranksTheCommentOverTheFunction()
    {
        string text = Plain1Text.ReplaceLineEndings("\n")
            .Replace("        % Code that executes after component creation\n", string.Empty, StringComparison.Ordinal)
            .Replace("<SingleRunningInstance>true</SingleRunningInstance>", "<SingleRunningInstance>false</SingleRunningInstance>", StringComparison.Ordinal);
        JgsMlappLayout layout = JgsMlappLayout.Of(text);
        Assert.True(layout.IsPlainText);
        Assert.False(layout.SingleInstance);
        Assert.Equal("startupFcn", layout.Startup?.Name);
        Assert.Equal("a, b", layout.InputParameters);
    }

    // --- reading back what was written -------------------------------------------------------------

    [Fact]
    public void TheDesignersFixtureRoundTripsThroughThePlainTextFormat()
    {
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        AssertRoundTrips(design, new DesignCode("DesignerSpecApp"), "DesignerSpecApp");
    }

    [Fact]
    public void TheShippedAppsRoundTripThroughThePlainTextFormat()
    {
        if (!Directory.Exists(ShippedRoot))
        {
            return;
        }

        int apps = 0;
        var outside = new List<string>();
        foreach (string path in Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string text = JgsMlapp.ReadCode(path);
            DesignReadResult read = DesignCodeReader.Read(text);
            if (read.Design is null || read.Problems.Count > 0 || DesignCode.Read(text) is not { } code)
            {
                continue;
            }

            // A component of a class the catalog does not know (the two Simulink apps' uisimprogress,
            // uisimvartuner and uisimcontrols) comes back as a call of its class with its properties
            // by name, as the appendix holds it: the format has no room for its maker's name or its
            // property order.
            if (read.Design.Components.Any(static c => c.Class is null))
            {
                outside.Add(Path.GetFileNameWithoutExtension(path));
                continue;
            }

            AssertRoundTrips(read.Design, code, Path.GetFileNameWithoutExtension(path), read.Style);
            apps++;
        }

        output.WriteLine($"{apps} shipped apps round-tripped; outside the catalog: {string.Join(", ", outside)}");
        Assert.True(apps >= 40, $"only {apps} shipped apps were read");
        Assert.Equal(["MultiPaneSimApp", "aurora"], outside.Order(StringComparer.Ordinal));
    }

    private void AssertRoundTrips(AppDesign design, DesignCode code, string name, DesignCodeStyle? style = null)
    {
        style ??= DesignCodeStyle.Default;
        var details = DesignPlainTextDetails.NewApp(code.ClassName, design.IsResponsive);
        string text = DesignPlainText.Render(design, code, details, style);
        DesignPlainTextReadResult back = DesignPlainText.Read(text);
        Assert.True(back.Read.Problems.Count == 0, name + ": " + string.Join(" | ", back.Read.Problems));
        Assert.NotNull(back.Read.Design);
        Assert.Equal(style.IndentUnit, back.Read.Style.IndentUnit);
        Assert.Equal(design.IsResponsive, back.Read.Design.IsResponsive);

        // The same components with the same lines, so the same createComponents.
        string[] expected = DesignCodeWriter.Method(design, style).ToArray();
        string[] actual = DesignCodeWriter.Method(back.Read.Design, style).ToArray();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            output.WriteLine(name);
            foreach ((string e, string a) in expected.Zip(actual).Where(static p => p.First != p.Second).Take(10))
            {
                output.WriteLine($"  - {e}\n  + {a}");
            }
        }

        Assert.Equal(expected, actual);

        // And the same code around them.
        DesignCode? codeBack = DesignCode.Read(text);
        Assert.NotNull(codeBack);
        Assert.Equal(code.ClassName, codeBack.ClassName);
        Assert.Equal(code.EditableSection, codeBack.EditableSection);
        Assert.Equal(code.Callbacks.Select(static c => (c.Name, c.IsStartup)), codeBack.Callbacks.Select(static c => (c.Name, c.IsStartup)));
        foreach ((DesignCallback a, DesignCallback b) in code.Callbacks.Zip(codeBack.Callbacks))
        {
            if (!a.IsGenerated)
            {
                Assert.Equal(a.Body, b.Body);
            }
        }

        Assert.Equal(code.InputParameters, codeBack.InputParameters);
        Assert.Equal(code.SingleInstance, codeBack.SingleInstance);
        Assert.Equal(details.AppId, back.Details.AppId);
        Assert.Equal(details.MinimumSupportedMatlabRelease, back.Details.MinimumSupportedMatlabRelease);

        // Rendering again from what was read gives the same file.
        Assert.Equal(text, DesignPlainText.Render(back.Read.Design, codeBack, back.Details, back.Read.Style));
    }

    [Fact]
    public void AnAppWrittenByAppDesignersWriterReadsWithItsQuirks()
    {
        // A quoted callback name (a ...Callback property), a two-element SelectedNodes in the
        // generator's placeholder form, an unknown element kept as a custom component, a label
        // attribute passed over.
        string text = Plain1Text.ReplaceLineEndings("\n").Replace(
            "    <UIFigure name='UIFigure'>\n",
            "    <UIFigure name='UIFigure'>\n        <Colormap>copper</Colormap>\n", StringComparison.Ordinal)
            .Replace("        <Children>\n            <Button name='GoButton'>",
                "        <Children>\n            <Label name='GoLabel'>\n                <Text>'Go:'</Text>\n            </Label>\n"
                + "            <SpinnerGauge name='Gauge'>\n                <Value>40</Value>\n                <ValueChangedFcn>GaugeChanged</ValueChangedFcn>\n            </SpinnerGauge>\n"
                + "            <Tree name='Tree'>\n                <SelectedNodes>[ad_OBJECTNAME_ad.Node, ad_OBJECTNAME_ad.Node2]</SelectedNodes>\n"
                + "                <Children>\n                    <TreeNode name='Node'>\n                    </TreeNode>\n                    <TreeNode name='Node2'>\n                    </TreeNode>\n                </Children>\n            </Tree>\n"
                + "            <Button name='GoButton' label='GoLabel'>", StringComparison.Ordinal)
            .Replace("        UIFigure  matlab.ui.Figure\n        GoButton  matlab.ui.control.Button",
                "        UIFigure  matlab.ui.Figure\n        GoLabel   matlab.ui.control.Label\n        Gauge     SpinnerGauge\n        Tree      matlab.ui.container.Tree\n        GoButton  matlab.ui.control.Button", StringComparison.Ordinal);
        Assert.Contains("<Colormap>copper</Colormap>", text);
        Assert.Contains("<SpinnerGauge name='Gauge'>", text);
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        Assert.Empty(read.Read.Problems);
        AppDesign design = read.Read.Design!;
        Assert.Equal(new CharValue("copper").Text, Assert.IsType<CharValue>(design.Figure.Get("AD_ColormapString")).Text);
        DesignComponent gauge = design.Find("Gauge")!;
        Assert.Null(gauge.Class);
        Assert.Equal("SpinnerGauge", gauge.Type);
        Assert.Equal("SpinnerGauge", gauge.Function);
        Assert.Equal("GaugeChanged", Assert.IsType<CallbackValue>(gauge.Get("ValueChangedFcn")).Method);
        Assert.Equal(["Node", "Node2"], Assert.IsType<ComponentsValue>(design.Find("Tree")!.Get("SelectedNodes")).CodeNames);
        Assert.Equal("GoButtonPushed", Assert.IsType<CallbackValue>(design.Find("GoButton")!.Get("ButtonPushedFcn")).Method);
        Assert.Equal("app.Gauge = SpinnerGauge(app.UIFigure);", DesignCodeWriter.ComponentCode(gauge)[0]);
    }

    // --- the document ----------------------------------------------------------------------------

    [Fact]
    public void ADocumentOpensAPlainTextAppSavesItAndSavesItAsEitherFormat()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-ds7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "Plain1.m");
            string text = Plain1Text.ReplaceLineEndings("\n");
            File.WriteAllText(path, text);
            string[] expectedMethod;
            using (DesignDocument document = DesignDocument.Open(path))
            {
                Assert.Equal(DesignFileFormat.PlainText, document.Format);
                Assert.Empty(document.ReadProblems);
                Assert.NotNull(document.Code);
                Assert.True(document.SingleInstance);
                Assert.Equal("a, b", document.InputParameters);
                Assert.Equal("Plain1", document.Details!.Name);
                Assert.Equal(text, document.CurrentText); // written again as it was read
                Assert.True(document.CanSave);

                DesignComponent button = document.Design.Find("GoButton")!;
                Assert.Null(document.SetProperty(button, "Text", new CharValue("Run")));
                Assert.True(document.IsDirty);
                Assert.Contains("<Text>'Run'</Text>", document.CurrentText);
                Assert.StartsWith("Saved ", document.Save());
                Assert.False(document.IsDirty);
                expectedMethod = DesignCodeWriter.Method(document.Design).ToArray();

                // Saved as an .mlapp: App Designer's package, the class with its generated methods.
                string mlapp = Path.Combine(folder, "Plain1Copy.mlapp");
                Assert.StartsWith("Saved ", document.SaveAs(mlapp));
                Assert.Equal(DesignFileFormat.Mlapp, document.Format);
                Assert.Equal(mlapp, document.Path);
                Assert.Equal("Plain1Copy", document.Code!.ClassName);
                Assert.Contains("function createComponents(app)", document.CurrentText);

                // And back to plain text under a third name: the App Details name follows the class's.
                string plain = Path.Combine(folder, "Plain2.m");
                Assert.StartsWith("Saved ", document.SaveAs(plain));
                Assert.Equal(DesignFileFormat.PlainText, document.Format);
                Assert.Equal("Plain2", document.Code.ClassName);
                Assert.Equal("Plain2", document.Details!.Name);
            }

            Assert.Contains("<Text>'Run'</Text>", File.ReadAllText(path));
            using (DesignDocument reopened = DesignDocument.Open(Path.Combine(folder, "Plain1Copy.mlapp")))
            {
                Assert.Equal(expectedMethod, DesignCodeWriter.Method(reopened.Design));
                Assert.True(reopened.SingleInstance);
            }

            using (DesignDocument again = DesignDocument.Open(Path.Combine(folder, "Plain2.m")))
            {
                Assert.Equal("Plain2", again.Code!.ClassName);
                Assert.Equal(expectedMethod, DesignCodeWriter.Method(again.Design));
                Assert.StartsWith("classdef Plain2 < matlab.apps.App", again.Text);
            }

            // A file that is not a plain-text app is refused as one.
            File.WriteAllText(Path.Combine(folder, "Plain3.m"), "classdef Plain3\nend\n");
            Assert.Throws<InvalidDataException>(() => DesignDocument.Open(Path.Combine(folder, "Plain3.m")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// With <c>JGRAPH_DS7_OUT</c> naming a folder, the apps the probe <c>ds7_plain</c> reads and
    /// runs in R2025b are written there, each as the plain-text <c>.m</c> this writer makes and as
    /// the <c>.mlapp</c> of the same design (the control App Designer's reader is held against),
    /// with the arguments the probe runs it with in <c>run.txt</c>.
    /// </summary>
    [Fact]
    public void TheProbesAppsAreWrittenWhenAsked()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS7_OUT");
        if (outFolder is null)
        {
            return;
        }

        var apps = new List<(string Name, string ClassText, string Arguments)>
        {
            ("DsPlain", AppDesignerPlainTextRunTests.AppSource, "7"),
            ("CallbacksApp", AppDesignerModelSaveTests.CallbacksAppText(), "2, 3"),
            ("DesignerSpecApp", File.ReadAllText(Fixture("DesignerSpecApp.m")), string.Empty),
        };
        int written = 0;
        foreach ((string name, string classText, string arguments) in apps)
        {
            string folder = Path.Combine(outFolder, "plain", $"{++written:D2}_{name}");
            Directory.CreateDirectory(folder);
            string mlappText;
            string plainText;
            if (DesignPlainText.IsPlainTextApp(classText))
            {
                using DesignDocument plain = DesignDocument.FromPlainText(classText.ReplaceLineEndings("\n"), null);
                plainText = plain.CurrentText;
                mlappText = DesignClassWriter.Render(plain.Design, plain.Code!, plain.Style);
            }
            else
            {
                using DesignDocument document = DesignDocument.FromText(classText.ReplaceLineEndings("\n"), null);

                // R2025b's loader passes an axes' Subtitle.String and Toolbar.Visible to the constructor
                // as dotted names, which MATLAB cannot parse (its own writer writes them too): the probe's
                // copy of the fixture leaves them unset so the other 45 classes can be checked.
                foreach (DesignComponent axes in document.Design.Components.Where(static c => c.Type == "matlab.ui.control.UIAxes").ToArray())
                {
                    axes.Unset("SubtitleString");
                    axes.Unset("ToolbarVisible");
                }

                mlappText = DesignClassWriter.Render(document.Design, document.Code!, document.Style);
                plainText = DesignPlainText.Render(document.Design, document.Code!, DesignPlainTextDetails.NewApp(name, document.Design.IsResponsive), document.Style);
            }

            File.WriteAllText(Path.Combine(folder, name + ".m"), plainText, new System.Text.UTF8Encoding(false));
            AppDesignerDocument.SaveNew(Path.Combine(folder, name + ".mlapp"), mlappText);
            File.WriteAllText(Path.Combine(folder, "run.txt"), arguments);
        }
    }

    // --- the review's findings (ADR 0251) --------------------------------------------------------

    /// <summary>
    /// A Save As whose write fails leaves the document as it was - its format, its file, its class
    /// name, its details - and no <c>.saving</c> file behind, so that the next Save goes to the file
    /// the app came from, in its format; in both directions.
    /// </summary>
    [Fact]
    public void AFailedSaveAsLeavesTheDocumentAsItWasAndNothingBehind()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-ds7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string locked = Path.Combine(folder, "Other.m");
        try
        {
            string path = Path.Combine(folder, "Plain1.m");
            File.WriteAllText(path, Plain1Text.ReplaceLineEndings("\n"));
            File.WriteAllText(locked, "classdef Other\nend\n");
            File.SetAttributes(locked, FileAttributes.ReadOnly);
            using DesignDocument document = DesignDocument.Open(path);

            Exception refused = Assert.ThrowsAny<Exception>(() => document.SaveAs(locked));
            Assert.True(refused is IOException or UnauthorizedAccessException, refused.ToString());
            Assert.Equal(DesignFileFormat.PlainText, document.Format);
            Assert.Equal(path, document.Path);
            Assert.Equal("Plain1", document.Code!.ClassName);
            Assert.Equal("Plain1", document.Details!.Name);
            Assert.False(File.Exists(locked + ".saving"));
            Assert.StartsWith("Saved ", document.Save());
            Assert.StartsWith("classdef Plain1 < matlab.apps.App", File.ReadAllText(path));

            // And from an .mlapp: the document stays the package's.
            string mlapp = Path.Combine(folder, "Plain1Copy.mlapp");
            Assert.StartsWith("Saved ", document.SaveAs(mlapp));
            refused = Assert.ThrowsAny<Exception>(() => document.SaveAs(locked));
            Assert.True(refused is IOException or UnauthorizedAccessException, refused.ToString());
            Assert.Equal(DesignFileFormat.Mlapp, document.Format);
            Assert.Equal(mlapp, document.Path);
            Assert.Equal("Plain1Copy", document.Code.ClassName);
            Assert.False(File.Exists(locked + ".saving"));
            Assert.StartsWith("Saved ", document.Save());
            Assert.StartsWith("classdef Plain1Copy < matlab.apps.AppBase", JgsMlapp.ReadCode(mlapp));
            Assert.Equal("classdef Other\nend\n", File.ReadAllText(locked));
        }
        finally
        {
            File.SetAttributes(locked, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The App Details go with the app across the formats: a plain-text app's into the package it
    /// is saved as (its metadata parts), and a package's into the plain-text file an app opened
    /// from it is saved as, with its id, and R2025a as the oldest release for a converted app.
    /// </summary>
    [Fact]
    public void SaveAsCarriesTheAppDetailsAcrossTheFormats()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-ds7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "Plain1.m");
            File.WriteAllText(path, Plain1Text.ReplaceLineEndings("\n").Replace(
                "<AppDetails>\n    <Name>Plain1</Name>\n",
                "<AppDetails>\n    <Author>Ann &amp; Bob</Author>\n    <Description>Does things.</Description>\n    <Name>Plain1</Name>\n    <Summary>A summary</Summary>\n", StringComparison.Ordinal));
            string mlapp = Path.Combine(folder, "Plain1Copy.mlapp");
            using (DesignDocument document = DesignDocument.Open(path))
            {
                Assert.Equal("Ann & Bob", document.Details!.Author);
                Assert.StartsWith("Saved ", document.SaveAs(mlapp));
            }

            MlappMetadata metadata = JgsMlapp.ReadMetadata(mlapp);
            Assert.Equal(new MlappMetadata("Plain1Copy", "Ann & Bob", "A summary", "Does things.", "1.0", metadata.Uuid), metadata);
            Assert.NotEmpty(metadata.Uuid);

            string again = Path.Combine(folder, "Plain1Again.m");
            using (DesignDocument reopened = DesignDocument.Open(mlapp))
            {
                Assert.Null(reopened.Details);
                Assert.StartsWith("Saved ", reopened.SaveAs(again));
                Assert.Equal("Ann & Bob", reopened.Details!.Author);
            }

            DesignPlainTextDetails details = DesignPlainText.Read(File.ReadAllText(again)).Details;
            Assert.Equal(("Plain1Again", "Ann & Bob", "A summary", "Does things.", "1.0"), (details.Name, details.Author, details.Summary, details.Description, details.Version));
            Assert.Equal(metadata.Uuid, details.AppId);
            Assert.Equal("R2025a", details.MinimumSupportedMatlabRelease);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// What the reader makes of a hand-edited file, as R2025b's generator does: a figure with no
    /// name is refused; the last <c>Children</c> element is the one read; an empty callback element
    /// leaves the property unset; a quoted <c>...Callback</c> name is the callback; another
    /// component's <c>Colormap</c> is the design's <c>colormap(app.X, map)</c> line, written again
    /// the same way.
    /// </summary>
    [Fact]
    public void TheReaderRefusesANamelessFigureTakesTheLastChildrenAndLeavesAnEmptyCallbackUnset()
    {
        string text = Plain1Text.ReplaceLineEndings("\n");
        DesignPlainTextReadResult nameless = DesignPlainText.Read(text.Replace("<UIFigure name='UIFigure'>", "<UIFigure name=''>", StringComparison.Ordinal));
        Assert.Null(nameless.Read.Design);
        Assert.Contains(nameless.Read.Problems, static p => p.Contains("has no name", StringComparison.Ordinal));

        string edited = text.Replace(
            "        <Children>\n            <Button name='GoButton'>\n                <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>",
            "        <Children>\n            <Button name='First'>\n            </Button>\n        </Children>\n"
            + "        <Children>\n            <Table name='Grid'>\n                <CellEditCallback>'CellEdited'</CellEditCallback>\n            </Table>\n"
            + "            <UIAxes name='Axes'>\n                <Colormap>jet</Colormap>\n            </UIAxes>\n"
            + "            <Button name='GoButton'>\n                <ButtonPushedFcn></ButtonPushedFcn>", StringComparison.Ordinal);
        Assert.NotEqual(text, edited);
        DesignPlainTextReadResult read = DesignPlainText.Read(edited);
        Assert.Empty(read.Read.Problems);
        AppDesign design = read.Read.Design!;
        Assert.Null(design.Find("First"));
        Assert.Equal(["Grid", "Axes", "GoButton"], design.Figure.Children.Select(static c => c.CodeName));
        Assert.Null(design.Find("GoButton")!.Get("ButtonPushedFcn"));
        Assert.Equal("CellEdited", Assert.IsType<CallbackValue>(design.Find("Grid")!.Get("CellEditCallback")).Method);
        Assert.Equal("jet", Assert.IsType<CharValue>(design.Find("Axes")!.Get("AD_ColormapString")).Text);
        Assert.Contains(DesignCodeWriter.Method(design), static l => l.Trim().StartsWith("colormap(app.Axes, ", StringComparison.Ordinal));
        Assert.Contains("<Colormap>jet</Colormap>", DesignPlainText.AppendixLines(design, new DesignCode("Plain1"), read.Details).Select(static l => l.Trim()));
    }

    /// <summary>The internal data lists the images the design keeps now, not those the file listed: an image taken out of the app leaves the list.</summary>
    [Fact]
    public void TheImageListFollowsTheDesign()
    {
        DesignPlainTextDetails details = NoDetails();
        details.ImageRelativePaths.Add("old.png");
        AppDesign design = NewFigure();
        Assert.DoesNotContain(DesignPlainText.AppendixLines(design, new DesignCode("app1"), details), static l => l.Contains("ImageRelativePaths", StringComparison.Ordinal));

        DesignComponent image = Make("Image", "matlab.ui.control.Image", design.Figure);
        image.Set("ImageSource", new ImageFileValue(["pics", "logo.png"]));
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), details);
        Assert.Contains("    <ImageRelativePaths>", lines);
        Assert.Contains("        <Product>pics/logo.png</Product>", lines);
        Assert.DoesNotContain("        <Product>old.png</Product>", lines);
        Assert.Contains("                <ImageSource>'pics/logo.png'</ImageSource>", lines);
    }

    /// <summary>
    /// The run configuration's word outranks the comment over a function: the app runs the function
    /// it names, so the designer shows that one as the startup function; one it names outside the
    /// callbacks block keeps its name through a save; and the <c>.mlapp</c> cutter's marker comment
    /// in the user's code does not cut a plain-text class.
    /// </summary>
    [Fact]
    public void TheRunConfigurationNamesTheStartupFunctionAndAMarkerInUserCodeDoesNotCut()
    {
        string text = Plain1Text.ReplaceLineEndings("\n");
        string named = text.Replace(
            "        % Button pushed function: GoButton\n        function GoButtonPushed(app, event)\n",
            "        % Button pushed function: GoButton\n        function GoButtonPushed(app, event)\n        end\n\n        function initApp(app, x)\n", StringComparison.Ordinal)
            .Replace("<StartupFcn>startupFcn</StartupFcn>", "<StartupFcn>initApp</StartupFcn>", StringComparison.Ordinal)
            .Replace("        Property % Description\n", "        Property % Description\n        % Component initialization\n", StringComparison.Ordinal);
        Assert.NotEqual(text, named);
        JgsMlappLayout layout = JgsMlappLayout.Of(named);
        Assert.True(layout.IsPlainText);
        Assert.True(layout.IsComplete);
        Assert.Equal("initApp", layout.Startup!.Name);
        Assert.Equal("x", layout.InputParameters);
        Assert.Null(layout.UnplacedStartup);
        DesignCode code = DesignCode.Read(named)!;
        Assert.Equal([("startupFcn", false), ("GoButtonPushed", false), ("initApp", true)], code.Callbacks.Select(static c => (c.Name, c.IsStartup)));
        Assert.Contains("        % Component initialization", code.EditableSection);
        Assert.True(code.SingleInstance);

        // Named outside the block: the name is kept and written again.
        string outside = text.Replace("<StartupFcn>startupFcn</StartupFcn>", "<StartupFcn>boot</StartupFcn>", StringComparison.Ordinal)
            .Replace("        Property % Description\n    end\n", "        Property % Description\n    end\n\n    methods (Access = private)\n        function boot(app)\n        end\n    end\n", StringComparison.Ordinal);
        DesignCode kept = DesignCode.Read(outside)!;
        Assert.Null(kept.Startup);
        Assert.Equal("boot", kept.UnplacedStartup);
        Assert.Equal(string.Empty, kept.InputParameters);
        Assert.Contains("    <StartupFcn>boot</StartupFcn>", DesignPlainText.AppendixLines(DesignPlainText.Read(outside).Read.Design!, kept, NoDetails()));
    }

    /// <summary>A file kept with CRLF endings gets them on every line, an app detail of several lines included.</summary>
    [Fact]
    public void ACrlfFileHasOneLineEnding()
    {
        DesignPlainTextDetails details = NoDetails();
        details.Description = "First line\nSecond line";
        string text = DesignPlainText.Render(NewFigure(), new DesignCode("app1"), details, newline: "\r\n", trailingNewline: true);
        Assert.Contains("<Description>First line\r\nSecond line</Description>", text);
        Assert.DoesNotMatch("[^\r]\n", text);
        Assert.EndsWith("%}\r\n", text);
    }

    /// <summary>A responsive app's internal data says so, and a new one names the oldest release App Designer's factory names for it.</summary>
    [Fact]
    public void AResponsiveAppIsWrittenAsOne()
    {
        AppDesign design = NewFigure();
        design.IsResponsive = true;
        DesignPlainTextDetails details = DesignPlainTextDetails.NewApp("app1", responsive: true);
        IReadOnlyList<string> lines = DesignPlainText.AppendixLines(design, new DesignCode("app1"), details);
        Assert.Contains("    <AppType>Responsive</AppType>", lines);
        Assert.Contains("    <MinimumSupportedMATLABRelease>R2019a</MinimumSupportedMATLABRelease>", lines);
        Assert.Equal("R2025a", DesignPlainTextDetails.NewApp("app1", responsive: false).MinimumSupportedMatlabRelease);
    }

    [Fact]
    public void TheEditorKnowsAPlainTextAppByItsAppendix()
    {
        var model = new ScriptDocumentModel(@"C:\apps\Plain1.m", Plain1Text.ReplaceLineEndings("\n"), "MATLAB");
        Assert.True(model.IsPlainTextApp);
        Assert.False(model.IsAppDesignerFile);
        Assert.False(new ScriptDocumentModel(@"C:\apps\Plain3.m", "classdef Plain3\nend\n", "MATLAB").IsPlainTextApp);
    }
}

/// <summary>Designer plan DS7a: a plain-text app running here, as <c>matlab.apps.App</c> runs it in R2025b with its feature on.</summary>
[Collection("JG facade")]
public class AppDesignerPlainTextRunTests : IAsyncLifetime
{
    /// <summary>A plain-text app with a grid, a numeric field, a button with a callback, a startup function taking one argument, and a single running instance.</summary>
    internal const string AppSource = """
        classdef DsPlain < matlab.apps.App

            % Properties that correspond to app components
            properties (Access = public)
                UIFigure    matlab.ui.Figure
                GridLayout  matlab.ui.container.GridLayout
                AmpField    matlab.ui.control.NumericEditField
                GoButton    matlab.ui.control.Button
            end

            properties (Access = public)
                Log = {}
                Started = 0
            end

            % Callbacks that handle component events
            methods

                % Code that executes after component creation
                function startupFcn(app, amp)
                    app.Started = amp;
                    app.AmpField.Value = amp;
                end

                % Button pushed function: GoButton
                function GoButtonPushed(app, event)
                    app.Log{end+1} = sprintf('go %g %s', app.AmpField.Value, class(event));
                end
            end

        end

        %[appendix]
        %---
        %[app:layout]
        %{
        <?xml version='1.0' encoding='UTF-8'?>
        <Components>
            <UIFigure name='UIFigure'>
                <Name>'DS7 plain'</Name>
                <Position>[100 100 320 160]</Position>
                <Children>
                    <GridLayout name='GridLayout'>
                        <ColumnWidth>{'1x', '1x'}</ColumnWidth>
                        <RowHeight>{22, 22}</RowHeight>
                        <Children>
                            <NumericEditField name='AmpField'>
                                <Layout.Column>[1 2]</Layout.Column>
                                <Layout.Row>1</Layout.Row>
                                <Value>2</Value>
                            </NumericEditField>
                            <Button name='GoButton'>
                                <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>
                                <Layout.Column>1</Layout.Column>
                                <Layout.Row>2</Layout.Row>
                                <Text>'Go'</Text>
                            </Button>
                        </Children>
                    </GridLayout>
                </Children>
            </UIFigure>
        </Components>
        %}

        %---
        %[app:runConfiguration]
        %{
        <?xml version='1.0' encoding='UTF-8'?>
        <RunConfiguration>
            <SingleRunningInstance>true</SingleRunningInstance>
            <StartupFcn>startupFcn</StartupFcn>
        </RunConfiguration>
        %}
        """;

    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-ds7run-" + Guid.NewGuid().ToString("N"));
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "DsPlain.m"), AppSource.ReplaceLineEndings("\n"));
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _directory)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private Task Drain() => ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    private bool Printed(string line) => _output.NormalLines.Contains(line);

    /// <summary>Every component in <paramref name="components"/> and inside its containers.</summary>
    private static IEnumerable<UiObject> Walk(IEnumerable<UiObject> components)
    {
        foreach (UiObject component in components)
        {
            yield return component;
            if (component is UiContainerModel container)
            {
                foreach (UiObject inner in Walk(container.Components))
                {
                    yield return inner;
                }
            }
        }
    }

    [Fact]
    public async Task ThePlainTextAppBuildsItsComponentsFromTheAppendixRunsItsStartupAndAnswersItsButton()
    {
        await Exec("""
            app = DsPlain(7);
            fprintf('amp %g started %g button %s grid %d\n', app.AmpField.Value, app.Started, app.GoButton.Text, numel(app.GridLayout.ColumnWidth));
            fprintf('cells %s %s\n', mat2str(app.AmpField.Layout.Column), mat2str(app.GoButton.Layout.Row));
            fprintf('running %d name %s\n', numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance')), app.UIFigure.Name);
            """);
        Assert.True(Printed("amp 7 started 7 button Go grid 2"), _output.NormalText + _output.ErrorText);
        Assert.True(Printed("cells [1 2] 2"), _output.NormalText);
        Assert.True(Printed("running 1 name DS7 plain"), _output.NormalText);

        Assert.True(JG.TryGetFigure(Assert.Single(JG.FigureNumbers), out FigureModel figure));
        ScriptGraphicsCallbacks.NotifyComponent(Walk(figure.Components).OfType<UiButtonModel>().Single(), "pushed");
        await Drain();
        await Exec("fprintf('log %s\\n', strjoin(app.Log, ','));");
        Assert.True(Printed("log go 7 matlab.ui.eventdata.ButtonPushedData"), _output.NormalText + _output.ErrorText);
    }

    [Fact]
    public async Task ASingleInstanceAppAnswersTheRunningOneAndDeleteClosesItsFigure()
    {
        await Exec("""
            a = DsPlain(1);
            b = DsPlain(2);
            fprintf('same %d started %g figures %d\n', a == b, b.Started, numel(findall(groot, 'Type', 'figure')));
            delete(a);
            fprintf('valid %d %d figures %d\n', isvalid(a), isvalid(b), numel(findall(groot, 'Type', 'figure')));
            c = DsPlain(3);
            fprintf('new %d started %g\n', c == a, c.Started);
            """);
        Assert.True(Printed("same 1 started 1 figures 1"), _output.NormalText + _output.ErrorText);
        Assert.True(Printed("valid 0 0 figures 0"), _output.NormalText + _output.ErrorText);
        Assert.True(Printed("new 0 started 3"), _output.NormalText + _output.ErrorText);
    }

    /// <summary>A 1-by-1 PNG, for an image kept beside an app.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static void WriteApp(string folder, string name, string layout, string members = "", string runConfiguration = "")
    {
        Directory.CreateDirectory(folder);
        string text = $$"""
            classdef {{name}} < matlab.apps.App

                % Properties that correspond to app components
                properties (Access = public)
                    UIFigure  matlab.ui.Figure
            {{members}}
                end

            end

            %[appendix]
            %---
            %[app:layout]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <Components>
            {{layout}}
            </Components>
            %}
            {{runConfiguration}}
            """;
        File.WriteAllText(Path.Combine(folder, name + ".m"), text.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// An app with no run configuration runs with no startup function and as many instances as
    /// are made; an image named in quotes that the internal data does not list is found beside
    /// the app when the app runs from another folder (the generator's <c>fullfile</c> fallback).
    /// </summary>
    [Fact]
    public async Task AnAppWithNoRunConfigurationRunsAndFindsAnImageBesideIt()
    {
        string apps = Path.Combine(_directory, "apps");
        WriteApp(apps, "PlainPic", """
                <UIFigure name='UIFigure'>
                    <Position>[100 100 200 120]</Position>
                    <Children>
                        <Image name='Pic'>
                            <ImageSource>'pic.png'</ImageSource>
                            <Position>[10 10 40 40]</Position>
                        </Image>
                    </Children>
                </UIFigure>
            """, "        Pic  matlab.ui.control.Image");
        File.WriteAllBytes(Path.Combine(apps, "pic.png"), OnePixelPng);
        await Exec("""
            addpath(fullfile(pwd, 'apps'));
            a = PlainPic;
            b = PlainPic;
            fprintf('distinct %d figures %d source %d\n', a ~= b, numel(findall(groot, 'Type', 'figure')), endsWith(a.Pic.ImageSource, 'pic.png'));
            """);
        Assert.True(Printed("distinct 1 figures 2 source 1"), _output.NormalText + _output.ErrorText);
    }

    /// <summary>A malformed appendix is refused as R2025b refuses it, by its identifier and message, rather than run with parts missing.</summary>
    [Fact]
    public async Task AMalformedAppendixIsRefusedAsR2025bRefusesIt()
    {
        WriteApp(_directory, "NoName", "    <UIFigure name=''>\n    </UIFigure>");
        WriteApp(_directory, "BadXml", "    <UIFigure name='UIFigure'>\n        <Name>'x'\n    </UIFigure>");
        WriteApp(_directory, "Unknown", "    <UIFigure name='UIFigure'>\n        <Children>\n            <Widget name='W'>\n            </Widget>\n        </Children>\n    </UIFigure>");
        WriteApp(_directory, "Twice", "    <UIFigure name='UIFigure'>\n        <Children>\n            <Button name='B'>\n            </Button>\n            <Button name='B'>\n            </Button>\n        </Children>\n    </UIFigure>");
        foreach (string name in (string[])["NoName", "BadXml", "Unknown", "Twice"])
        {
            await Exec($"try, {name}; catch e, fprintf('{name}: %s | %s\\n', e.identifier, e.message); end");
        }

        Assert.True(Printed("NoName: MATLAB:appdesigner:appdesigner:AppAppendixException | Unable to process app layout due to incorrect XML."), _output.NormalText + _output.ErrorText);
        Assert.Contains(_output.NormalLines, static l => l.StartsWith("BadXml: MATLABApp:appendixError | ", StringComparison.Ordinal));
        Assert.True(Printed("Unknown: MATLAB:appdesigner:appdesigner:AppAppendixUnknownComponent | Unrecognized component: 'Widget'."), _output.NormalText + _output.ErrorText);
        Assert.Contains(_output.NormalLines, static l => l.StartsWith("Twice: MATLAB:appdesigner:appdesigner:AppAppendixException | Unable to process app layout due to incorrect XML. <Button name='B'>: a second component", StringComparison.Ordinal));
        Assert.Empty(JG.FigureNumbers);
    }
}
