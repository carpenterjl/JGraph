using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 140 (ADR 0242): this build's figure document carries the <c>Tag</c> of a text in an axes,
/// of every annotation, of a legend, a colour bar and a light, and keeps a rectangle the rectangle it
/// was, so <c>findobj</c> and <c>findall</c> find them by tag after <c>savefig</c> and <c>openfig</c> as
/// they do in R2025b (probe <c>oi140_tags</c>: every tag and type kept there). With them the legend's
/// title style (open item 141) and a label's own visibility (open item 142) travel too.
/// </summary>
[Collection("JG facade")]
public sealed class FigureDocumentTagsTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-oi140-" + Guid.NewGuid().ToString("N"));

    public FigureDocumentTagsTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task Run(string code)
    {
        ScriptRunResult result = await new MatlabScriptEngine().RunAsync(
            "set(groot, 'DefaultFigureVisible', 'off');\n" + code,
            new ScriptContext(_output, static (_, _) => { }, _folder, resolvePath: null, new TestFigureFiles()), default);
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public Task EveryTaggedObjectIsFoundByItsTagAfterTheRoundTrip() => Run("""
        f = figure('Tag', 'fig');
        ax = axes(f, 'Tag', 'ax');
        hold(ax, 'on');
        set(plot(ax, 1:3), 'Tag', 'line');
        text(ax, 1, 2, 'note', 'Tag', 'text');
        set(rectangle(ax, 'Position', [0.5 0.5 1 1], 'Curvature', [0.3 0.6]), 'Tag', 'rect');
        set(light(ax), 'Tag', 'light');
        legend(ax, 'Tag', 'legend');
        colorbar(ax, 'Tag', 'cbar');
        annotation(f, 'textbox', [0.1 0.1 0.2 0.1], 'String', 'tb', 'Tag', 'a_textbox');
        annotation(f, 'rectangle', [0.3 0.3 0.2 0.2], 'Tag', 'a_rectangle');
        annotation(f, 'ellipse', [0.5 0.5 0.2 0.2], 'Tag', 'a_ellipse');
        annotation(f, 'line', [0.1 0.4], [0.9 0.8], 'Tag', 'a_line');
        annotation(f, 'arrow', [0.1 0.4], [0.7 0.6], 'Tag', 'a_arrow');
        annotation(f, 'doublearrow', [0.1 0.4], [0.5 0.4], 'Tag', 'a_doublearrow');
        annotation(f, 'textarrow', [0.1 0.4], [0.3 0.2], 'String', 'ta', 'Tag', 'a_textarrow');
        tags = {'fig', 'ax', 'line', 'text', 'rect', 'light', 'legend', 'cbar', 'a_textbox', 'a_rectangle', 'a_ellipse', 'a_line', 'a_arrow', 'a_doublearrow', 'a_textarrow'};
        before = cellfun(@(t) strjoin(cellstr(get(findall(f, 'Tag', t), 'Type')), ' '), tags, 'UniformOutput', false);
        savefig(f, 'tags.fig');
        close(f);
        h = openfig('tags.fig', 'invisible');
        after = cellfun(@(t) strjoin(cellstr(get(findall(h, 'Tag', t), 'Type')), ' '), tags, 'UniformOutput', false);
        assert(isequal(before, after), strjoin(after, ','));
        assert(all(cellfun(@(t) numel(findall(h, 'Tag', t)) == 1, tags)));
        r = findobj(h, 'Tag', 'rect');
        assert(strcmp(r.Type, 'rectangle'));
        assert(isequal(r.Position, [0.5 0.5 1 1]));
        assert(isequal(r.Curvature, [0.3 0.6]));
        tx = findobj(h, 'Tag', 'text');
        assert(strcmp(tx.String, 'note'));
        """);

    [Fact]
    public Task ARectanglesScalarCurvatureReadsBackAsWritten() => Run("""
        f = figure;
        r = rectangle('Position', [1 2 3 4], 'Curvature', 0.5, 'FaceColor', [1 1 0]);
        savefig(f, 'rect.fig');
        close(f);
        h = openfig('rect.fig', 'invisible');
        r = findobj(h, 'Type', 'rectangle');
        assert(numel(r) == 1);
        assert(isequal(r.Curvature, 0.5));
        assert(isequal(r.FaceColor, [1 1 0]));
        r.Position = [0 0 2 2];
        assert(isequal(r.Position, [0 0 2 2]));
        """);

    [Fact]
    public Task TheLegendTitlesWordsAndStyleTravel() => Run("""
        f = figure;
        plot(1:3);
        lg = legend('up');
        title(lg, 'Saved', 'FontWeight', 'normal', 'FontSize', 15, 'Color', [1 0 0], 'Interpreter', 'none', 'FontAngle', 'italic', 'FontName', 'Courier New');
        savefig(f, 'title.fig');
        close(f);
        h = openfig('title.fig', 'invisible');
        t = findobj(h, 'Type', 'legend').Title;
        assert(strcmp(t.String, 'Saved'));
        assert(strcmp(t.FontWeight, 'normal'));
        assert(t.FontSize == 15);
        assert(isequal(t.Color, [1 0 0]));
        assert(strcmp(t.Interpreter, 'none'));
        assert(strcmp(t.FontAngle, 'italic'));
        assert(strcmp(t.FontName, 'Courier New'));
        assert(strcmp(t.FontSizeMode, 'manual'));
        assert(strcmp(t.ColorMode, 'manual'));
        """);

    [Fact]
    public Task AFollowingTitleStillFollowsAfterTheRoundTrip() => Run("""
        f = figure;
        plot(1:3);
        lg = legend('up');
        lg.Title.String = 'Follows';
        lg.Title.Visible = 'off';
        savefig(f, 'follow.fig');
        close(f);
        h = openfig('follow.fig', 'invisible');
        lg = findobj(h, 'Type', 'legend');
        assert(strcmp(lg.Title.FontSizeMode, 'auto'));
        assert(strcmp(lg.Title.FontWeight, 'bold'));
        assert(lg.Title.Visible == 'off');
        lg.FontSize = 17;
        assert(lg.Title.FontSize == 17);
        """);

    [Fact]
    public Task AnImshowAxesKeepsItsLabelsShownAfterTheRoundTrip() => Run("""
        f = figure;
        imshow(magic(4) / 16);
        xlabel('x');
        savefig(f, 'labels.fig');
        close(f);
        h = openfig('labels.fig', 'invisible');
        ax = findobj(h, 'Type', 'axes');
        assert(ax.Visible == 'off');
        assert(strcmp(get(ax.XLabel, 'Visible'), 'on'));
        assert(strcmp(ax.XLabel.String, 'x'));
        """);

    [Fact]
    public void ADocumentFromBeforeOpensWithTheTagsItNeverHad()
    {
        // The new fields are optional: a document without them opens as it always did.
        var figure = new FigureModel();
        var axes = new AxesModel();
        figure.Axes.Add(axes);
        var patch = new PatchPlot([0, 1, 1], [0, 0, 1], [0, 0, 0], [[0, 1, 2]]) { RectanglePosition = [0, 0, 1, 1], RectangleCurvature = [0, 0] };
        axes.Plots.Add(patch);
        axes.Legend.Tag = "lg";
        axes.Legend.TitleFontSize = 12;
        string json = GraphFormat.Serialize(figure);
        Assert.Contains("\"rectanglePosition\"", json, StringComparison.Ordinal);
        Assert.Contains("\"titleFontSize\"", json, StringComparison.Ordinal);

        Assert.Contains("\"tag\": \"lg\"", json, StringComparison.Ordinal);

        FigureModel round = GraphFormat.Deserialize(json);
        Assert.Equal("lg", round.Axes[0].Legend.Tag);
        Assert.Equal(12, round.Axes[0].Legend.TitleFontSize);
        Assert.Equal([0, 0, 1, 1], Assert.IsType<PatchPlot>(Assert.Single(round.Axes[0].Plots)).RectanglePosition!);

        // Unknown names are passed over, so renaming the new ones is a document from before them.
        string old = json.Replace("\"rectanglePosition\"", "\"x_rectanglePosition\"", StringComparison.Ordinal)
            .Replace("\"titleFontSize\"", "\"x_titleFontSize\"", StringComparison.Ordinal)
            .Replace("\"tag\": \"lg\"", "\"x_tag\": \"lg\"", StringComparison.Ordinal);
        FigureModel reopened = GraphFormat.Deserialize(old);
        PatchPlot plain = Assert.IsType<PatchPlot>(Assert.Single(reopened.Axes[0].Plots));
        Assert.Null(plain.RectanglePosition);
        Assert.Null(reopened.Axes[0].Legend.TitleFontSize);
        Assert.Null(reopened.Axes[0].Legend.Tag);
        Assert.True(reopened.Axes[0].Legend.TitleVisible);
    }
}
