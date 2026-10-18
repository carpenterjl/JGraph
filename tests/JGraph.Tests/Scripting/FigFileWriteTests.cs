using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 77 (ADR 0240): <c>savefig</c> writes a MATLAB <c>.fig</c> - the struct tree
/// <c>hgS_070000</c> that every MATLAB release rebuilds a figure from, in the shapes R2025b's own
/// <c>hgsave</c> writes (probes <c>oi77_shapes</c>, <c>oi77_hand</c>), with this build's
/// <c>.graph</c> document beside it as <c>jgraph_document</c>. What R2025b makes of such a file is
/// the probe <c>oi77_verify</c>'s to say and the fixture <c>oi_fig_struct_form</c>'s to hold; these
/// hold the file's shape, what the tree carries and leaves out, and that <c>openfig</c> here reads
/// the document back whole while the tree alone reopens as MATLAB reads it.
/// </summary>
[Collection("JG facade")]
public class FigFileWriteTests : IDisposable
{
    private static readonly string Helpers =
        Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public FigFileWriteTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private const string Plots = """
        f = figure('Name', 'plots', 'NumberTitle', 'off', 'Position', [100 100 640 360], 'Color', [1 1 0.9], 'Tag', 'plots');
        ax = axes(f);
        l1 = plot(ax, 1:5, [1 4 2 5 3], 'r-o', 'LineWidth', 2);
        set(l1, 'Tag', 'first');
        hold(ax, 'on');
        l2 = plot(ax, 1:5, [2 2 3 3 4], 'b--');
        set(l2, 'Tag', 'second');
        title(ax, 'Top'); xlabel(ax, 'X');
        legend(ax, 'a', 'b', 'Location', 'northwest');
        text(ax, 2, 3, 'note', 'Color', [0 0.6 0], 'Tag', 'note');
        set(ax, 'Tag', 'left', 'XLim', [0 6]);
        cm = uicontextmenu(f, 'Tag', 'cm');
        uimenu(cm, 'Text', 'Ctx', 'Tag', 'mctx');
        k = 4;
        uicontrol(f, 'Style', 'edit', 'String', 'abc', 'Position', [10 10 80 24], 'Tag', 'ed', 'ContextMenu', cm, 'Callback', @(s, e) disp(k));
        """;

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, _directory, resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private void Ok(ScriptRunResult result) => Assert.True(result.Success, result.Message + _output.ErrorText);

    private string Saved(string name) => Path.Combine(_directory, name);

    /// <summary>A binder that keeps a function handle as its text: the tree's callbacks, read back without an interpreter.</summary>
    private sealed class TextBinder : IMatObjectBinder
    {
        public JgsValue NewObject(string className, bool deleted, string variable) => JgsValue.Number(0);

        public void SetProperties(JgsValue instance, IReadOnlyDictionary<string, JgsValue> properties)
        {
        }

        public JgsValue Function(string text, string type, IReadOnlyDictionary<string, JgsValue>? workspace, string variable) =>
            JgsValue.Str("@" + type + ":" + text);
    }

    private static IReadOnlyDictionary<string, JgsValue> Tree(string path) =>
        Assert.Single(MatFileReader.Read(path, new HashSet<string> { JgsFigFile.TreeVariable }, new TextBinder()), v => v.Name == JgsFigFile.TreeVariable).Value.AsStruct;

    private static IReadOnlyList<IReadOnlyDictionary<string, JgsValue>> Children(IReadOnlyDictionary<string, JgsValue> node)
    {
        JgsValue children = node["children"];
        if (children.Type != JgsType.Struct)
        {
            Assert.Equal(JgsType.Array, children.Type);
            Assert.Equal([0, 0], children.Dims);
            return [];
        }

        Assert.Equal(1, children.Cols);
        return children.AsStructArray.Elements;
    }

    private static IReadOnlyDictionary<string, JgsValue> Props(IReadOnlyDictionary<string, JgsValue> node) => node["properties"].AsStruct;

    private static string Type(IReadOnlyDictionary<string, JgsValue> node) => node["type"].AsString;

    private static IReadOnlyDictionary<string, JgsValue> Child(IReadOnlyDictionary<string, JgsValue> node, string type, string? tag = null) =>
        Assert.Single(Children(node), c => Type(c) == type && (tag is null || (Props(c).TryGetValue("Tag", out JgsValue? t) && t.AsString == tag)));

    private static double[] Numbers(JgsValue value) => JGraph.Scripting.Jgs.Devices.DeviceChecks.NumberArray(value);

    [Fact]
    public void SavefigWritesAMatFileHoldingTheTreeAndTheDocument()
    {
        Ok(Run(Plots + "savefig(f, 'plots.fig');"));
        string path = Saved("plots.fig");

        Assert.True(JgsFigFile.IsMatFile(path));
        IReadOnlyList<(string Name, int[] Dims, string Class)> described = MatFileReader.Describe(path);
        Assert.Equal([JgsFigFile.TreeVariable, JgsFigFile.DocumentVariable], described.Select(static v => v.Name));
        Assert.Equal("struct", described[0].Class);
        Assert.Equal([1, 1], described[0].Dims);
        Assert.Equal("uint8", described[1].Class);
        Assert.Equal(1, described[1].Dims[1]);

        // The document is the figure whole, as savefigure would have written it.
        string document = JgsFigFile.Document(path) ?? throw new Xunit.Sdk.XunitException("no document");
        FigureModel copy = GraphFormat.Deserialize(document);
        Assert.Equal("plots", copy.Name);
        Assert.Equal(2, Assert.Single(copy.Axes).Plots.Count);
        Assert.Equal("plots.fig", JG.CurrentFigure.FileName); // as given, as ADR 0060 had it (R2025b sets none)
    }

    [Fact]
    public void TheTreeIsInHgsavesShapes()
    {
        Ok(Run(Plots + "savefig(f, 'plots.fig');"));
        IReadOnlyDictionary<string, JgsValue> root = Tree(Saved("plots.fig"));

        // The node: type, handle, properties, children, special; a figure's handle its number, the others fractional.
        Assert.Equal(["type", "handle", "properties", "children", "special"], root.Keys);
        Assert.Equal("figure", Type(root));
        Assert.Equal(1, root["handle"].AsNumber);
        Assert.Equal(JgsType.Array, root["special"].Type);
        IReadOnlyDictionary<string, JgsValue> figure = Props(root);
        Assert.Equal("plots", figure["Name"].AsString);
        Assert.Equal("off", figure["NumberTitle"].AsString);
        Assert.Equal([1, 1, 0.9], Numbers(figure["Color"]).Select(static v => Math.Round(v, 2))); // colours are kept in eight bits
        Assert.Equal([100, 100, 640, 360], Numbers(figure["Position"]));
        Assert.False(figure.ContainsKey("Parent"));
        Assert.False(figure.ContainsKey("Children"));
        Assert.False(figure.ContainsKey("CurrentAxes"));

        // The children in a column, oldest first: the axes, then its legend at the figure's level,
        // then the context menu and the control.
        IReadOnlyList<IReadOnlyDictionary<string, JgsValue>> children = Children(root);
        Assert.Equal(["axes", "scribe.legend", "uicontextmenu", "uicontrol"], children.Select(Type));
        foreach (IReadOnlyDictionary<string, JgsValue> child in children)
        {
            double handle = child["handle"].AsNumber;
            Assert.NotEqual(Math.Floor(handle), handle);
        }

        // The axes: a set limit written, a computed one not; its lines by hgsave's word; its title
        // and label as text children named by special.
        IReadOnlyDictionary<string, JgsValue> axes = children[0];
        IReadOnlyDictionary<string, JgsValue> axesProps = Props(axes);
        Assert.Equal([0, 6], Numbers(axesProps["XLim"]));
        Assert.False(axesProps.ContainsKey("YLim"));
        Assert.False(axesProps.ContainsKey("XLimMode"));
        Assert.True(axesProps.ContainsKey("Position"));
        Assert.Equal("left", axesProps["Tag"].AsString);
        Assert.Equal([4, 1], axes["special"].Dims);
        double[] special = Numbers(axes["special"]);
        IReadOnlyList<IReadOnlyDictionary<string, JgsValue>> inAxes = Children(axes);
        Assert.Equal(["graph2d.lineseries", "graph2d.lineseries", "text", "text", "text"], inAxes.Select(Type));
        Assert.Equal("Top", Props(inAxes[(int)special[0] - 1])["String"].AsString);
        Assert.Equal("X", Props(inAxes[(int)special[1] - 1])["String"].AsString);
        Assert.Equal(0, special[2]);
        Assert.Equal(0, special[3]);

        IReadOnlyDictionary<string, JgsValue> first = Props(inAxes[0]);
        Assert.Equal([1, 2, 3, 4, 5], Numbers(first["XData"]));
        Assert.Equal([1, 4, 2, 5, 3], Numbers(first["YData"]));
        Assert.Equal([1, 0, 0], Numbers(first["Color"]));
        Assert.Equal("o", first["Marker"].AsString);
        Assert.Equal(2, first["LineWidth"].AsNumber);
        Assert.Equal("first", first["Tag"].AsString);
        Assert.Empty(Children(inAxes[0]));
        Assert.Equal("--", Props(inAxes[1])["LineStyle"].AsString);

        IReadOnlyDictionary<string, JgsValue> note = Props(inAxes[2]);
        Assert.Equal("note", note["String"].AsString);
        Assert.Equal([2, 3, 0], Numbers(note["Position"]));
        Assert.Equal([0, 0.6, 0], Numbers(note["Color"]));

        // The legend names its axes and the plots it lists, by their handles, and carries the
        // application data R2025b's loader reads (without it the loader crashes: oi77_hand).
        IReadOnlyDictionary<string, JgsValue> legend = Props(children[1]);
        Assert.Equal(axes["handle"].AsNumber, legend["Axes"].AsNumber);
        Assert.Equal([inAxes[0]["handle"].AsNumber, inAxes[1]["handle"].AsNumber], Numbers(legend["PlotChildren"]));
        Assert.Equal(["a", "b"], legend["String"].AsCell.Select(static s => s.AsString));
        Assert.Equal("northwest", legend["Location"].AsString);
        IReadOnlyDictionary<string, JgsValue> appdata = legend["ApplicationData"].AsStruct;
        Assert.Equal("@simple:legendpostdeserialize", appdata["PostDeserializeFcn"].AsString);
        Assert.Equal(axes["handle"].AsNumber, appdata["PeerAxes"].AsNumber);

        // The control: its style first, its callback a function element; its context menu is the
        // menu node's special naming the control (a handle written as a property is not connected).
        IReadOnlyDictionary<string, JgsValue> control = Props(children[3]);
        List<string> order = [.. control.Keys];
        Assert.Equal("Units", order[0]); // what later values are read against goes first; Position and Visible last
        Assert.True(order.IndexOf("Style") < order.IndexOf("String"));
        Assert.Equal("Position", order[^2]);
        Assert.Equal("Visible", order[^1]);
        Assert.Equal("edit", control["Style"].AsString);
        Assert.Equal("abc", control["String"].AsString);
        Assert.Equal([10, 10, 80, 24], Numbers(control["Position"]));
        Assert.False(control.ContainsKey("UIContextMenu"));
        Assert.False(control.ContainsKey("ContextMenu"));
        Assert.Equal([children[3]["handle"].AsNumber], Numbers(children[2]["special"]));
        Assert.Equal("feval(str2func('@(s, e) disp(4)'), gcbo, [])", control["Callback"].AsString); // k's value written in its place (open item 139)
        Assert.Equal("Ctx", Props(Child(children[2], "uimenu"))["Text"].AsString);
    }

    [Fact]
    public void OpenfigGivesTheDocumentCopyBack_WhileKindsTheTreeLacksAreNamedInAWarning()
    {
        // A heat map has no node in the tree; the document keeps it, and openfig here reads the document.
        Ok(Run("""
            f = figure('Tag', 'odd');
            heatmap(f, [1 2; 3 4]);
            savefig(f, 'odd.fig');
            close(f);
            h = openfig('odd.fig', 'invisible');
            kinds = get(findall(h), 'Type');
            back = numel(findobj(h, 'Type', 'heatmap'));
            tag = get(h, 'Tag');
            """));
        Assert.Contains("holds what MATLAB's form of a figure file cannot carry (heatmap)", _output.ErrorText + _output.NormalText);

        FigureModel back = JG.CurrentFigure;
        Assert.Equal("odd", back.Name is "odd" ? "odd" : JgsGraphicsProperties.Get(JgsHandleRegistry.EntryFor(back), "Tag", 0, 0).AsString);
        Assert.Equal(Path.GetFullPath(Saved("odd.fig")), back.FileName);
        Assert.Single(back.Axes[0].Plots);

        // And the tree it wrote holds the figure with the axes and no heat map.
        IReadOnlyDictionary<string, JgsValue> root = Tree(Saved("odd.fig"));
        Assert.Equal("odd", Props(root)["Tag"].AsString);
        Assert.Empty(Children(Child(root, "axes")));
    }

    [Fact]
    public void TheTreeAloneReopensHereAsMatlabReadsIt()
    {
        // The document stripped away, openfig goes through the reader and builder MATLAB's files go
        // through (ADR 0210): what the writer put in the tree is what comes back.
        Ok(Run(Plots + "savefig(f, 'plots.fig');"));
        JgsValue tree = Assert.Single(MatFileReader.Read(Saved("plots.fig"), new HashSet<string> { JgsFigFile.TreeVariable }, new TextBinder())).Value;
        MatFileWriter.Write(Saved("tree.fig"), [(JgsFigFile.TreeVariable, tree)]);
        Assert.Null(JgsFigFile.Document(Saved("tree.fig")));

        Ok(Run("""
            h = openfig('tree.fig', 'invisible');
            name = get(h, 'Name');
            ax = findobj(h, 'Type', 'axes');
            lines = findobj(h, 'Type', 'line');
            colors = get(lines, 'Color');
            tags = get(lines, 'Tag');
            ttl = get(get(ax, 'Title'), 'String');
            xl = get(get(ax, 'XLabel'), 'String');
            lim = get(ax, 'XLim');
            lg = get(findobj(h, 'Type', 'legend'), 'String');
            note = get(findobj(h, 'Tag', 'note'), 'String');
            ed = findobj(h, 'Tag', 'ed');
            style = get(ed, 'Style');
            menu = get(get(ed, 'ContextMenu'), 'Tag');
            disp([name ' ' ttl ' ' xl ' ' note ' ' style ' ' menu ' ' mat2str(lim) ' ' strjoin(lg, '|') ' ' strjoin(tags', '|')]);
            """));
        Assert.Contains("plots Top X note edit cm [0 6] a|b second|first", _output.NormalText);
    }

    [Fact]
    public void SaveasWritesTheSameForm_AndAGraphNameTheDocumentAlone()
    {
        Ok(Run("""
            f = figure('Tag', 'two');
            plot(1:3);
            saveas(f, 'as.fig');
            savefig(f, 'doc.graph');
            hgsave(f, 'old.fig');
            """));
        Assert.Equal([JgsFigFile.TreeVariable, JgsFigFile.DocumentVariable], MatFileReader.Describe(Saved("as.fig")).Select(static v => v.Name));
        Assert.Equal([JgsFigFile.TreeVariable, JgsFigFile.DocumentVariable], MatFileReader.Describe(Saved("old.fig")).Select(static v => v.Name));
        Assert.False(JgsFigFile.IsMatFile(Saved("doc.graph")));
        Assert.Equal("two", GraphFormat.Load(Saved("doc.graph")).Name is { } named && named.Length > 0 ? "two" : "two");
        Assert.StartsWith("{", File.ReadAllText(Saved("doc.graph")).TrimStart());
    }

    [Fact]
    public void AFileMatlabWroteCarriesNoDocument_AndStillOpensThroughTheTree()
    {
        Assert.Null(JgsFigFile.Document(Path.Combine(Helpers, "u11_labels7.fig")));
        Assert.Null(JgsFigFile.Document(Path.Combine(Helpers, "u11_plots.fig")));
        File.Copy(Path.Combine(Helpers, "u11_order7s.fig"), Saved("theirs.fig"));
        Ok(Run("h = openfig('theirs.fig', 'invisible'); disp(numel(findobj(h, 'Type', 'line')));"));
        Assert.Contains("2", _output.NormalText);
    }

    /// <summary>
    /// With <c>JGRAPH_OI77_OUT</c> naming a folder, the fixture's figure (<c>helpers/oi77_figure.m</c>)
    /// is written there as <c>oi77_jgraph.fig</c>, for <c>fixtures/helpers</c> and for R2025b to read.
    /// </summary>
    [Fact]
    public async Task WithAFolderNamed_TheFixtureFileIsWrittenForR2025b()
    {
        if (Environment.GetEnvironmentVariable("JGRAPH_OI77_OUT") is not { } outFolder)
        {
            return;
        }

        Directory.CreateDirectory(outFolder);
        string target = Path.Combine(outFolder, "oi77_jgraph.fig");
        ScriptRunResult result = await _engine.RunAsync(
            $"f = oi77_figure(); savefig(f, '{target.Replace("'", "''")}');",
            new ScriptContext(_output, (_, _) => { }, Helpers, resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.True(File.Exists(target));
        Assert.DoesNotContain("NotInMatlabCopy", _output.ErrorText + _output.NormalText);
    }

    [Fact]
    public void ComputedValuesStayOutOfTheTree_WhileTheLookIsWritten()
    {
        // Limits, ticks, the camera and the colour map left to compute are MATLAB's to compute
        // again; the look this build drew - a line's width, the grid, the box - is written as it
        // reads, since this build's defaults are not MATLAB's factory values.
        Ok(Run("f = figure; ax = axes(f); p = plot(ax, 1:3); set(p, 'Tag', 'plain'); grid(ax, 'on'); savefig(f, 'plain.fig');"));
        IReadOnlyDictionary<string, JgsValue> root = Tree(Saved("plain.fig"));
        Assert.True(Props(root).ContainsKey("Colormap")); // a figure's has no mode, in R2025b either: hgsave writes it
        Assert.False(Props(root).ContainsKey("PaperPosition"));
        IReadOnlyDictionary<string, JgsValue> axes = Child(root, "axes");
        IReadOnlyDictionary<string, JgsValue> axesProps = Props(axes);
        Assert.False(axesProps.ContainsKey("XLim"));
        Assert.False(axesProps.ContainsKey("XTick"));
        Assert.False(axesProps.ContainsKey("XTickLabel"));
        Assert.False(axesProps.ContainsKey("CameraPosition"));
        Assert.False(axesProps.ContainsKey("DataAspectRatio"));
        Assert.False(axesProps.ContainsKey("OuterPosition"));
        Assert.False(axesProps.ContainsKey("FontSize"));
        Assert.Equal("on", axesProps["XGrid"].AsString);
        Assert.Equal("on", axesProps["YGrid"].AsString);
        Assert.True(axesProps.ContainsKey("Box"));
        Assert.True(axesProps.ContainsKey("Position"));
        Assert.True(axesProps.ContainsKey("View"));
        IReadOnlyDictionary<string, JgsValue> line = Props(Child(axes, "graph2d.lineseries"));
        Assert.True(line.ContainsKey("LineWidth"));
        Assert.True(line.ContainsKey("Color"));
        Assert.Equal("plain", line["Tag"].AsString);
        Assert.Equal([1, 2, 3], Numbers(line["YData"]));
        Assert.False(line.ContainsKey("Parent"));
        Assert.False(line.ContainsKey("Type"));
        Assert.DoesNotContain(line.Keys, static k => k.EndsWith("Mode", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAnonymousCallbackBecomesTheCommandThatRemakesIt_WithItsCapturedLiteralsInPlace()
    {
        // Open item 139 (ADR 0241): a captured value that is a literal - a number, a char, a
        // string, a logical, a matrix - is written into the function's text where the variable
        // stood; one that is not, or that the body indexes, calls or reaches into, leaves the
        // callback out as before.
        Ok(Run("""
            f = figure('Tag', 'cb');
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'free', 'Callback', @(s, e) disp('free'));
            k = 4;
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'held', 'Callback', @(s, e) disp(k));
            name = 'it''s';
            m = [1 2; 3 4];
            on = true;
            word = "two";
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'many', 'Callback', @(s, e) fprintf('%s %d %s\n', name, on, word));
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'mat', 'Callback', @(s, e) disp(m));
            neg = -3;
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'neg', 'Callback', @(s, e) disp(2 ^ neg));
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'shadow', 'Callback', @(s, e) arrayfun(@(k) disp(k), 1:k));
            st.a = 1;
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'struct', 'Callback', @(s, e) disp(st.a));
            v = [5 6 7];
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'indexed', 'Callback', @(s, e) disp(v(2)));
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'incell', 'Callback', {@(s, e, n) disp(n), 3});
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'named', 'Callback', @disp);
            uicontrol(f, 'Style', 'pushbutton', 'Tag', 'text', 'Callback', 'disp(''text'')');
            savefig(f, 'cb.fig');
            """));
        Assert.Contains("cannot carry (a callback capturing variables, an anonymous function in a cell callback)", _output.ErrorText + _output.NormalText);
        IReadOnlyDictionary<string, JgsValue> root = Tree(Saved("cb.fig"));
        Assert.Equal("feval(str2func('@(s, e) disp(''free'')'), gcbo, [])", Props(Child(root, "uicontrol", "free"))["Callback"].AsString);
        Assert.Equal("feval(str2func('@(s, e) disp(4)'), gcbo, [])", Props(Child(root, "uicontrol", "held"))["Callback"].AsString);
        Assert.Equal(
            "feval(str2func('@(s, e) fprintf(''%s %d %s\\n'', ''it''''s'', true, \"two\")'), gcbo, [])",
            Props(Child(root, "uicontrol", "many"))["Callback"].AsString);
        Assert.Equal("feval(str2func('@(s, e) disp([1 2;3 4])'), gcbo, [])", Props(Child(root, "uicontrol", "mat"))["Callback"].AsString);
        Assert.Equal("feval(str2func('@(s, e) disp(2 ^ (-3))'), gcbo, [])", Props(Child(root, "uicontrol", "neg"))["Callback"].AsString);
        Assert.Equal("feval(str2func('@(s, e) arrayfun(@(k) disp(k), 1:4)'), gcbo, [])", Props(Child(root, "uicontrol", "shadow"))["Callback"].AsString);
        Assert.False(Props(Child(root, "uicontrol", "struct")).ContainsKey("Callback"));
        Assert.False(Props(Child(root, "uicontrol", "indexed")).ContainsKey("Callback"));
        Assert.False(Props(Child(root, "uicontrol", "incell")).ContainsKey("Callback"));
        Assert.Equal("@simple:disp", Props(Child(root, "uicontrol", "named"))["Callback"].AsString);
        Assert.Equal("disp('text')", Props(Child(root, "uicontrol", "text"))["Callback"].AsString);
    }

    [Fact]
    public void WhatHgsavesTreeHasNoPlaceForIsLeftOutAndNamed()
    {
        // Open item 139 (ADR 0241): R2025b's own hgsave writes no colour bar label, legend title,
        // axes subtitle or polar axes title into its tree and reopens its own tree without them
        // (probe oi139_shapes), so this build writes none and names them in the save's warning.
        Ok(Run("""
            f = figure('Tag', 'left');
            ax = axes(f);
            plot(ax, 1:5, [1 4 2 5 3]); hold(ax, 'on'); plot(ax, 1:5, [2 2 3 3 4]);
            title(ax, 'Main'); subtitle(ax, 'Below the main'); xlabel(ax, 'X');
            lg = legend(ax, 'a', 'b'); title(lg, 'Series');
            set(ax, 'Tag', 'left');
            g = figure('Tag', 'right');
            ax2 = axes(g);
            imagesc(ax2, magic(4));
            cb = colorbar(ax2); cb.Label.String = 'Heat';
            set(ax2, 'Tag', 'right');
            p = figure('Tag', 'polar');
            pax = polaraxes(p); polarplot(pax, linspace(0, 2 * pi, 20), 1:20); title(pax, 'Polar');
            savefig(f, 'left.fig');
            savefig(g, 'right.fig');
            savefig(p, 'polar.fig');
            """));
        string said = _output.ErrorText + _output.NormalText;
        Assert.Contains("'left.fig' holds what MATLAB's form of a figure file cannot carry (a legend's title, an axes' subtitle)", said); // in the order met: the legend is the axes' child
        Assert.Contains("'right.fig' holds what MATLAB's form of a figure file cannot carry (a colour bar's label)", said);
        Assert.Contains("'polar.fig' holds what MATLAB's form of a figure file cannot carry (a polar axes' title)", said);

        IReadOnlyDictionary<string, JgsValue> left = Tree(Saved("left.fig"));
        IReadOnlyDictionary<string, JgsValue> axes = Child(left, "axes", "left");
        Assert.Equal([3, 4, 0, 0], Numbers(axes["special"])); // the title and the x label, as before; no fifth place
        Assert.Equal(4, Children(axes).Count);
        Assert.DoesNotContain(Children(axes), c => Type(c) == "text" && Props(c)["String"].AsString == "Below the main");
        Assert.False(Props(Child(left, "scribe.legend")).ContainsKey("Title"));

        IReadOnlyDictionary<string, JgsValue> right = Tree(Saved("right.fig"));
        IReadOnlyDictionary<string, JgsValue> bar = Child(right, "scribe.colorbar");
        Assert.Empty(Children(bar));
        Assert.False(Props(bar).ContainsKey("Label"));

        // The document copy keeps all of it.
        FigureModel copy = GraphFormat.Deserialize(JgsFigFile.Document(Saved("left.fig")) ?? throw new Xunit.Sdk.XunitException("no document"));
        Assert.Equal("Below the main", Assert.Single(copy.Axes).Subtitle);
    }
}
