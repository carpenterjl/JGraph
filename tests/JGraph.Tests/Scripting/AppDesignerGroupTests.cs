using System.IO.Compression;
using JGraph.Core.Model;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 150 (the designer leftovers plan, stage G1): App Designer's component groups - read
/// from and written into the <c>.mlapp</c> tree (<c>components.Groups</c> and each member's
/// <c>GroupId</c>, as the probe <c>oi150_groups</c> recorded them over the shipped apps) and the
/// plain-text format (the <c>label</c> attribute and the <c>app:componentGroups</c> section, as
/// DS7a's oracle rendered them), and the design's model of them.
/// </summary>
public class AppDesignerGroupTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";
    private const string Model = "appdesigner/appModel.mat";

    private static IEnumerable<string> ShippedApps() =>
        Directory.Exists(ShippedRoot) ? Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories) : [];

    private static string? Shipped(string name) => ShippedApps().FirstOrDefault(a => Path.GetFileName(a) == name);

    private static byte[] ModelOf(string mlapp)
    {
        using ZipArchive package = ZipFile.OpenRead(mlapp);
        using Stream stream = package.GetEntry(Model)!.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static DesignComponent Make(string name, string type, DesignComponent? parent = null, params double[] position)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
        if (position.Length == 4)
        {
            component.Set("Position", NumericValue.Row(position));
        }

        parent?.Add(component);
        return component;
    }

    private static AppDesign NewFigure()
    {
        DesignComponent figure = Make("UIFigure", "matlab.ui.Figure", null, 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        return new AppDesign(figure);
    }

    /// <summary>Records as lines, <c>id&lt;parent:members</c>, the members sorted: a file lists a group's members in its objects' order, the design in its own.</summary>
    private static string[] Show(IEnumerable<DesignGroupRecord> records) =>
        records.Select(static r => $"{r.Id}<{r.ParentId}:{string.Join(",", r.Members.Order(StringComparer.Ordinal))}").ToArray();

    private static string Copy(string app, string folder)
    {
        Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, Path.GetFileName(app));
        File.Copy(app, copy, overwrite: true);
        File.SetAttributes(copy, FileAttributes.Normal);
        return copy;
    }

    // --- the model -------------------------------------------------------------------------------------

    /// <summary>
    /// A file's groups are made over the components as App Designer's loader makes them: members in
    /// the design's order, a labelled component's label first whatever the order, a member group
    /// where its first component is met, a parent the file lacks making a top group, a name the
    /// design lacks left out, and a group left empty dropped.
    /// </summary>
    [Fact]
    public void AttachGroupsBuildsThemAsAppDesignersLoaderDoes()
    {
        AppDesign design = NewFigure();
        DesignComponent field = Make("EditField", "matlab.ui.control.EditField", design.Figure, 90, 400, 100, 22);
        DesignComponent label = Make("EditFieldLabel", "matlab.ui.control.Label", design.Figure, 20, 400, 55, 22);
        DesignComponent b1 = Make("Button", "matlab.ui.control.Button", design.Figure, 10, 10, 100, 22);
        DesignComponent b2 = Make("Button2", "matlab.ui.control.Button", design.Figure, 10, 40, 100, 22);
        DesignComponent b3 = Make("Button3", "matlab.ui.control.Button", design.Figure, 200, 10, 100, 22);
        design.AttachGroups(
        [
            new DesignGroupRecord("pair", "", ["EditField", "EditFieldLabel"]),
            new DesignGroupRecord("inner", "outer", ["Button2", "Button3"]),
            new DesignGroupRecord("outer", "", ["Button"]),
            new DesignGroupRecord("orphan", "nowhere", ["Button"]),
            new DesignGroupRecord("empty", "", ["Nobody"]),
        ]);

        Assert.Equal(["pair", "inner", "outer"], design.Groups.Select(static g => g.Id));
        DesignGroup pair = design.Groups[0];
        Assert.True(pair.IsLabeled);
        Assert.Same(label, pair.Label);
        Assert.Same(field, pair.Field);
        Assert.Equal(["EditFieldLabel", "EditField"], pair.Members.Select(static m => m.ToString()));

        DesignGroup inner = design.Groups[1];
        DesignGroup outer = design.Groups[2];
        Assert.Same(outer, inner.Parent);
        Assert.Null(outer.Parent);
        Assert.Equal(["Button", "inner"], outer.Members.Select(static m => m.ToString()));
        Assert.Equal(["Button2", "Button3"], inner.Members.Select(static m => m.ToString()));
        Assert.Same(outer, design.GroupOf(b1));
        Assert.Same(inner, design.GroupOf(b2));
        Assert.Same(outer, design.RootGroupOf(b3));
        Assert.Equal(["Button", "Button2", "Button3"], outer.AllComponents().Select(static c => c.CodeName));
        Assert.Equal(["pair<:EditField,EditFieldLabel", "inner<outer:Button2,Button3", "outer<:Button"], Show(design.GroupRecords()));
        Assert.Equal(["EditFieldLabel", "EditField"], design.GroupRecords()[0].Members);
    }

    /// <summary>Grouping, adding, removing and ungrouping keep the design's bookkeeping, and a member leaving the design leaves its group.</summary>
    [Fact]
    public void TheDesignKeepsGroupsAsTheyChange()
    {
        AppDesign design = NewFigure();
        DesignComponent a = Make("A", "matlab.ui.control.Button", design.Figure);
        DesignComponent b = Make("B", "matlab.ui.control.Button", design.Figure);
        DesignComponent c = Make("C", "matlab.ui.control.Button", design.Figure);
        DesignGroup g = design.AddGroup([DesignGroupMember.Of(a), DesignGroupMember.Of(b)], "g");
        Assert.Equal(36, new DesignGroup().Id.Length); // a GUID, as App Designer's peer node ids
        Assert.Throws<ArgumentException>(() => design.AddGroup([DesignGroupMember.Of(a)]));
        design.AddToGroup(g, [DesignGroupMember.Of(c)]);
        Assert.Equal(["A", "B", "C"], g.Members.Select(static m => m.ToString()));
        design.RemoveFromGroup(g, [DesignGroupMember.Of(b)]);
        Assert.Null(design.GroupOf(b));
        DesignGroup outer = design.AddGroup([DesignGroupMember.Of(g), DesignGroupMember.Of(b)], "outer");
        Assert.Same(outer, g.Parent);
        Assert.Throws<ArgumentException>(() => design.AddToGroup(g, [DesignGroupMember.Of(outer)]));
        design.LeaveGroup(c);
        Assert.Equal(["A"], g.Members.Select(static m => m.ToString()));
        Assert.True(design.RemoveGroup(g));
        Assert.Null(design.GroupOf(a));
        Assert.Equal(["B"], outer.Members.Select(static m => m.ToString()));
        Assert.False(design.RemoveGroup(g));
        Assert.Equal(["outer"], design.Groups.Select(static g => g.Id));
    }

    // --- the .mlapp tree ---------------------------------------------------------------------------------

    /// <summary>
    /// Every shipped app's groups are read as the probe counted them: 26 apps with groups in a
    /// level-5 model (SignalIntegrityLink's 7 are in a version 7.3 model), 136 groups, 279 members,
    /// 133 labelled pairs, 3 groups inside another, bertool's two as the probe listed them.
    /// </summary>
    [Fact]
    public void TheShippedAppsGroupsAreReadAsTheProbeCountedThem()
    {
        int apps = 0, groups = 0, members = 0, labeled = 0, nested = 0;
        foreach (string app in ShippedApps())
        {
            IReadOnlyList<DesignGroupRecord> records = JgsMlapp.ReadGroups(app);
            if (records.Count == 0)
            {
                continue;
            }

            AppDesign design = DesignCodeReader.Read(JgsMlapp.ReadCode(app)).Design!;
            design.AttachGroups(records);
            apps++;
            groups += records.Count;
            members += records.Sum(static r => r.Members.Count);
            labeled += design.Groups.Count(static g => g.IsLabeled);
            nested += design.Groups.Count(static g => g.Parent is not null);
            Assert.Equal(records.Count, design.Groups.Count);
            if (Path.GetFileName(app) == "bertool.mlapp")
            {
                DesignGroup three = design.Groups.Single(static g => g.Id.StartsWith("406437af", StringComparison.Ordinal));
                // In the design's order (createComponents), which findall lists the other way round in the probe.
                Assert.Equal(["SynchronizationButtonGroup", "Normalizedtimingerror", "RMSphasenoiserad"], three.Members.Select(static m => m.ToString()));
                Assert.False(three.IsLabeled);
                DesignGroup six = design.Groups.Single(static g => g.Id.StartsWith("77e33420", StringComparison.Ordinal));
                Assert.Equal(6, six.Members.Count);
                DesignGroup child = design.Groups.Single(static g => g.Id.StartsWith("05e893d8", StringComparison.Ordinal));
                Assert.Equal("cc10bd45-6143-4dbf-85e3-1dca3d25d374", child.Parent?.Id);
                Assert.True(child.IsLabeled);
                Assert.Equal("ModulationOrderLabel", child.Label!.CodeName);
            }
        }

        output.WriteLine($"{apps} apps, {groups} groups, {members} members, {labeled} labelled, {nested} nested");
        Assert.Equal((26, 136, 279, 126, 3), (apps, groups, members, labeled, nested)); // the probe's 133 labelled pairs less SignalIntegrityLink's 7
    }

    /// <summary>
    /// A shipped app saved with its own text and its own groups is not touched: the tree is left
    /// and the model is the same bytes - with the groups given, or with none given (the file's kept).
    /// </summary>
    [Fact]
    public void AShippedAppSavedUnchangedKeepsItsModelToTheByte()
    {
        if (Shipped("bertool.mlapp") is not { } app)
        {
            return;
        }

        string temp = Path.Combine(Path.GetTempPath(), $"jgraph-oi150-{Guid.NewGuid():N}");
        try
        {
            string copy = Copy(app, temp);
            byte[] before = ModelOf(copy);
            string text = JgsMlapp.ReadCode(copy);
            MlappSaveResult same = JgsMlapp.WriteCode(copy, text, JgsMlapp.ReadGroups(copy));
            Assert.Null(same.Tree);
            Assert.Equal(before, ModelOf(copy));
            MlappSaveResult kept = JgsMlapp.WriteCode(copy, text);
            Assert.Null(kept.Tree);
            Assert.Equal(before, ModelOf(copy));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// A group made and one taken apart in the designer are written into the tree on save, the
    /// generated code unchanged: <c>components.Groups</c> holds the design's groups in order and
    /// each member's <c>GroupId</c> names its group, each other component's empty; the document
    /// opened again has them. The file is kept for R2025b's loader when asked (<c>oi150_verify</c>).
    /// </summary>
    [Fact]
    public void GroupingInTheDesignerWritesTheGroupsIntoTheTree()
    {
        if (Shipped("DataExport.mlapp") is not { } app)
        {
            return;
        }

        string? keep = Environment.GetEnvironmentVariable("JGRAPH_OI150_OUT");
        string temp = keep ?? Path.Combine(Path.GetTempPath(), $"jgraph-oi150-{Guid.NewGuid():N}");
        try
        {
            string copy = Copy(app, Path.Combine(temp, "grouped"));
            using (DesignDocument document = DesignDocument.Open(copy))
            {
                AppDesign design = document.Design;
                Assert.Equal(2, design.Groups.Count);
                DesignGroup five = design.Groups.Single(static g => g.Members.Count == 5);
                DesignGroup pair = design.Groups.Single(static g => g.Members.Count == 2);
                Assert.True(pair.IsLabeled);

                // The labelled pair taken apart; two buttons grouped; the five-member group kept.
                design.RemoveGroup(pair);
                DesignComponent[] buttons = design.Components.Where(static c => c.Type == "matlab.ui.control.Button").Take(2).ToArray();
                Assert.Equal(2, buttons.Length);
                DesignGroup made = design.AddGroup([DesignGroupMember.Of(buttons[0]), DesignGroupMember.Of(buttons[1])], "11111111-2222-4333-8444-555555555555");
                Assert.Same(made, design.GroupOf(buttons[1]));
                string note = document.Save();
                output.WriteLine(note);
            }

            IReadOnlyList<DesignGroupRecord> saved = JgsMlapp.ReadGroups(copy);
            Assert.Equal(2, saved.Count);
            Assert.Equal(5, saved[0].Members.Count);
            Assert.Equal("11111111-2222-4333-8444-555555555555", saved[1].Id);
            Assert.Equal(string.Empty, saved[1].ParentId);
            Assert.Equal(2, saved[1].Members.Count);
            Assert.All(saved[1].Members, static m => Assert.Contains("Button", m));

            // The tree's objects: the pair's two members have no group now.
            MatMcosFile file = MatMcosFile.Read(ModelOf(copy))!;
            using (DesignDocument again = DesignDocument.Open(copy))
            {
                Assert.Equal(Show(saved), Show(again.Design.GroupRecords()));
                Assert.DoesNotContain(again.Design.Groups, static g => g.IsLabeled);
            }
        }
        finally
        {
            if (keep is null)
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>A new <c>.mlapp</c> written whole holds its groups, and a design with none writes no <c>Groups</c> field.</summary>
    [Fact]
    public void ANewFileHoldsItsGroups()
    {
        string? keep = Environment.GetEnvironmentVariable("JGRAPH_OI150_OUT");
        string temp = keep is null ? Path.Combine(Path.GetTempPath(), $"jgraph-oi150-{Guid.NewGuid():N}") : Path.Combine(keep, "new");
        Directory.CreateDirectory(temp);
        try
        {
            string text = DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), Design())!;
            string path = Path.Combine(temp, "Grouped.mlapp");
            (string saved, MlappWriteResult result) = AppDesignerDocument.SaveNew(path, text, null, Design().GroupRecords());
            Assert.True(result.HasDesign, string.Join(" ", result.Problems));
            Assert.Equal(["g1<:EditField,EditFieldLabel", "g2<:Button,Button2"], Show(JgsMlapp.ReadGroups(path)));
            using DesignDocument document = DesignDocument.Open(path);
            Assert.Equal(2, document.Design.Groups.Count);
            Assert.True(document.Design.Groups[0].IsLabeled);

            string plain = Path.Combine(temp, "Plain.mlapp");
            AppDesignerDocument.SaveNew(plain, saved.Replace("Grouped", "Plain", StringComparison.Ordinal), null, []);
            Assert.Empty(JgsMlapp.ReadGroups(plain));
            Assert.True(MatMcosFile.Read(ModelOf(plain)) is { } file && file.Variable(DesignTreeWriter.Variable) is { } components
                && MatRaw.StructFields(components.Element)!.Select(static f => f.Name).SequenceEqual([DesignTreeWriter.FigureField]));
        }
        finally
        {
            if (keep is null)
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        static AppDesign Design()
        {
            AppDesign design = NewFigure();
            DesignComponent label = Make("EditFieldLabel", "matlab.ui.control.Label", design.Figure, 20, 400, 55, 22);
            label.Set("Text", new CharValue("Edit Field"));
            DesignComponent field = Make("EditField", "matlab.ui.control.EditField", design.Figure, 90, 400, 100, 22);
            DesignComponent b1 = Make("Button", "matlab.ui.control.Button", design.Figure, 10, 10, 100, 22);
            DesignComponent b2 = Make("Button2", "matlab.ui.control.Button", design.Figure, 10, 40, 100, 22);
            design.AddGroup([DesignGroupMember.Of(field), DesignGroupMember.Of(label)], "g1");
            design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(b2)], "g2");
            return design;
        }
    }

    // --- the plain-text format ---------------------------------------------------------------------------

    private static string Appendix(AppDesign design) =>
        string.Join('\n', DesignPlainText.AppendixLines(design, new DesignCode("app1"), new DesignPlainTextDetails { MatlabRelease = string.Empty, HasThumbnail = false }));

    /// <summary>The appendix up to the end of its layout section (the internal data always follows here, which the oracle's cases had not set).</summary>
    private static string Layout(string appendix) => appendix[..(appendix.IndexOf("\n%}", StringComparison.Ordinal) + 3)];

    /// <summary>The appendix's component groups section, from its delimiter to its end.</summary>
    private static string GroupsSection(string appendix)
    {
        int at = appendix.IndexOf("%---\n%[app:componentGroups]", StringComparison.Ordinal);
        return at < 0 ? string.Empty : appendix[at..(appendix.IndexOf("\n%}", at, StringComparison.Ordinal) + 3)];
    }

    /// <summary>The oracle's case 4: a labelled component's field names its label; the label gets no attribute (and 4b: whichever comes first).</summary>
    [Fact]
    public void ALabelledComponentsFieldNamesItsLabel()
    {
        AppDesign design = NewFigure();
        DesignComponent label = Make("EditFieldLabel", "matlab.ui.control.Label", design.Figure, 20, 400, 55, 22);
        label.Set("HorizontalAlignment", new CharValue("right"));
        label.Set("Text", new CharValue("Edit Field"));
        DesignComponent field = Make("EditField", "matlab.ui.control.EditField", design.Figure, 90, 400, 100, 22);
        design.AddGroup([DesignGroupMember.Of(label), DesignGroupMember.Of(field)]);
        Assert.Equal(
            """

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
                        <Label name='EditFieldLabel'>
                            <HorizontalAlignment>'right'</HorizontalAlignment>
                            <Position>[20 400 55 22]</Position>
                            <Text>'Edit Field'</Text>
                        </Label>
                        <EditField name='EditField' label='EditFieldLabel'>
                            <Position>[90 400 100 22]</Position>
                        </EditField>
                    </Children>
                </UIFigure>
            </Components>
            %}
            """.Replace("\r\n", "\n", StringComparison.Ordinal), Layout(Appendix(design)));

        // 4b: the field listed before its label in the tree.
        AppDesign other = NewFigure();
        DesignComponent f2 = Make("EditField", "matlab.ui.control.EditField", other.Figure, 90, 400, 100, 22);
        DesignComponent l2 = Make("EditFieldLabel", "matlab.ui.control.Label", other.Figure);
        l2.Set("Text", new CharValue("Edit Field"));
        other.AddGroup([DesignGroupMember.Of(f2), DesignGroupMember.Of(l2)]);
        Assert.Contains("<EditField name='EditField' label='EditFieldLabel'>", Appendix(other), StringComparison.Ordinal);
        Assert.Contains("<Label name='EditFieldLabel'>\n", Appendix(other), StringComparison.Ordinal);
    }

    /// <summary>The oracle's case 12: the groups that are not labelled pairs in their section, a labelled pair inside one as its field's name, a group inside one nested.</summary>
    [Fact]
    public void TheGroupsSectionIsWrittenAsTheOracleRendersIt_AndReadsBack()
    {
        AppDesign design = Case12();
        string appendix = Appendix(design);
        Assert.True(appendix.IndexOf("%[app:componentGroups]", StringComparison.Ordinal) is var at && at > appendix.IndexOf("</Components>", StringComparison.Ordinal)
            && at < appendix.IndexOf("%[app:internalData]", StringComparison.Ordinal));
        Assert.Equal(
            """
            %---
            %[app:componentGroups]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <ComponentGroups>
                <Group>
                    <Member>Button</Member>
                    <Member>Button2</Member>
                </Group>
                <Group>
                    <Group>
                        <Member>Button3</Member>
                        <Member>Button4</Member>
                    </Group>
                    <Member>Button5</Member>
                </Group>
                <Group>
                    <Member>EditField</Member>
                    <Member>Button6</Member>
                </Group>
            </ComponentGroups>
            %}
            """.Replace("\r\n", "\n", StringComparison.Ordinal), GroupsSection(appendix));
        Assert.Contains("<EditField name='EditField' label='EditFieldLabel'>", appendix, StringComparison.Ordinal);

        // Read back: the same groups, the labelled pair found by its label attribute.
        string text = DesignPlainText.Render(design, new DesignCode("app1"), new DesignPlainTextDetails { MatlabRelease = string.Empty, HasThumbnail = false });
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        Assert.Empty(read.Read.Problems);
        AppDesign back = read.Read.Design!;
        Assert.Equal(5, back.Groups.Count);
        DesignGroup pair = back.Groups.Single(static g => g.IsLabeled);
        Assert.Equal("EditFieldLabel", pair.Label!.CodeName);
        DesignGroup[] top = back.Groups.Where(static g => g.Parent is null && !g.IsLabeled).ToArray();
        Assert.Equal(3, top.Length);
        Assert.Equal(["Button", "Button2"], top[0].Members.Select(static m => m.ToString()));
        Assert.Equal(2, top[1].Members.Count);
        Assert.Equal(["Button3", "Button4"], top[1].Members[0].Group!.Members.Select(static m => m.ToString()));
        Assert.Equal("Button5", top[1].Members[1].ToString());
        Assert.Same(pair, top[2].Members[0].Group);
        Assert.Equal("Button6", top[2].Members[1].ToString());
        Assert.Equal(appendix, Appendix(back));

        static AppDesign Case12()
        {
            AppDesign design = NewFigure();
            DesignComponent b1 = Make("Button", "matlab.ui.control.Button", design.Figure, 10, 10, 100, 22);
            DesignComponent b2 = Make("Button2", "matlab.ui.control.Button", design.Figure, 10, 40, 100, 22);
            DesignComponent b3 = Make("Button3", "matlab.ui.control.Button", design.Figure, 200, 10, 100, 22);
            DesignComponent b4 = Make("Button4", "matlab.ui.control.Button", design.Figure, 200, 40, 100, 22);
            DesignComponent b5 = Make("Button5", "matlab.ui.control.Button", design.Figure, 200, 70, 100, 22);
            DesignComponent label = Make("EditFieldLabel", "matlab.ui.control.Label", design.Figure);
            label.Set("Text", new CharValue("Edit Field"));
            DesignComponent field = Make("EditField", "matlab.ui.control.EditField", design.Figure, 90, 400, 100, 22);
            DesignComponent b6 = Make("Button6", "matlab.ui.control.Button", design.Figure, 300, 70, 100, 22);
            design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(b2)]);
            DesignGroup inner = design.AddGroup([DesignGroupMember.Of(b3), DesignGroupMember.Of(b4)]);
            design.AddGroup([DesignGroupMember.Of(inner), DesignGroupMember.Of(b5)]);
            DesignGroup pair = design.AddGroup([DesignGroupMember.Of(label), DesignGroupMember.Of(field)]);
            design.AddGroup([DesignGroupMember.Of(pair), DesignGroupMember.Of(b6)]);
            return design;
        }
    }

    /// <summary>What the reader leaves out, each with a problem: a label the layout lacks, a member in a group already, a group of one.</summary>
    [Fact]
    public void TheReaderRecordsWhatItCannotGroup()
    {
        string text = """
            classdef app1 < matlab.apps.App

                % Properties that correspond to app components
                properties (Access = public)
                    UIFigure  matlab.ui.Figure
                    EditField  matlab.ui.control.EditField
                    Button  matlab.ui.control.Button
                    Button2  matlab.ui.control.Button
                end

            end

            %[appendix]
            %---
            %[app:layout]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <Components>
                <UIFigure name='UIFigure'>
                    <Children>
                        <EditField name='EditField' label='Nobody'>
                        </EditField>
                        <Button name='Button'>
                        </Button>
                        <Button name='Button2'>
                        </Button>
                    </Children>
                </UIFigure>
            </Components>
            %}

            %---
            %[app:componentGroups]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <ComponentGroups>
                <Group>
                    <Member>Button</Member>
                    <Member>Button2</Member>
                </Group>
                <Group>
                    <Member>Button</Member>
                    <Member>EditField</Member>
                </Group>
                <Group>
                    <Member>Ghost</Member>
                </Group>
            </ComponentGroups>
            %}
            """;
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        AppDesign design = read.Read.Design!;
        Assert.Single(design.Groups);
        Assert.Equal(["Button", "Button2"], design.Groups[0].Members.Select(static m => m.ToString()));
        Assert.Equal(
        [
            "<EditField name='EditField' label='Nobody'>: the layout has no such label.",
            "<Member>Button</Member>: the component is in a group already.",
            "<Group>: a group of fewer than two members.",
            "<Member>Ghost</Member>: the layout has no such component.",
            "<Group>: a group of fewer than two members.",
        ], read.Read.Problems);
    }

    // --- the review's cases (ADR 0254) --------------------------------------------------------------

    /// <summary>Records compare without regard to member order; a loop of parents makes top groups; a record one component carries is not a group.</summary>
    [Fact]
    public void RecordsCompareByMembersInAnyOrder_AndAttachGroupsIsSafeOnBadFiles()
    {
        Assert.True(DesignGroupRecord.Same([new DesignGroupRecord("g", "", ["A", "B"])], [new DesignGroupRecord("g", "", ["B", "A"])]));
        Assert.False(DesignGroupRecord.Same([new DesignGroupRecord("g", "", ["A", "B"])], [new DesignGroupRecord("g", "", ["A", "C"])]));

        AppDesign design = NewFigure();
        Make("A", "matlab.ui.control.Button", design.Figure);
        Make("B", "matlab.ui.control.Button", design.Figure);
        Make("C", "matlab.ui.control.Button", design.Figure);
        Make("D", "matlab.ui.control.Button", design.Figure);
        design.AttachGroups(
        [
            new DesignGroupRecord("x", "y", ["A", "B"]),
            new DesignGroupRecord("y", "x", ["C", "D"]),
            new DesignGroupRecord("lone", "", ["A"]),
            new DesignGroupRecord("one", "", ["D"]),
        ]);
        // The loop is cut where it would close: y holds x, and stands at the top.
        Assert.Equal(["x", "y"], design.Groups.Select(static g => g.Id).Order());
        DesignGroup y = design.Groups.Single(static g => g.Id == "y");
        Assert.Null(y.Parent);
        Assert.Equal(["A", "B", "C", "D"], y.AllComponents().Select(static c => c.CodeName).Order());
        Assert.Equal(y, design.Groups.Single(static g => g.Id == "x").Root);
    }

    /// <summary>A plain-text Group of one member that is a labelled component keeps the labelled component the layout declared.</summary>
    [Fact]
    public void AGroupOfOneLabelledComponentLeavesTheLabelledComponent()
    {
        string text = """
            classdef app1 < matlab.apps.App

                % Properties that correspond to app components
                properties (Access = public)
                    UIFigure  matlab.ui.Figure
                    EditFieldLabel  matlab.ui.control.Label
                    EditField  matlab.ui.control.EditField
                end

            end

            %[appendix]
            %---
            %[app:layout]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <Components>
                <UIFigure name='UIFigure'>
                    <Children>
                        <Label name='EditFieldLabel'>
                        </Label>
                        <EditField name='EditField' label='EditFieldLabel'>
                        </EditField>
                    </Children>
                </UIFigure>
            </Components>
            %}

            %---
            %[app:componentGroups]
            %{
            <?xml version='1.0' encoding='UTF-8'?>
            <ComponentGroups>
                <Group>
                    <Member>EditField</Member>
                </Group>
            </ComponentGroups>
            %}
            """;
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        AppDesign design = read.Read.Design!;
        Assert.Single(design.Groups);
        Assert.True(design.Groups[0].IsLabeled);
        Assert.Equal(["<Group>: a group of fewer than two members."], read.Read.Problems);
    }
}
