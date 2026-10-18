using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 141 (ADR 0242): a legend's <c>Title</c> is a text object of the legend's own,
/// <c>matlab.graphics.illustration.legend.Text</c>, with R2025b's 21 properties and no Type, Tag or
/// Position (probes <c>oi141_title</c>, <c>oi141_font</c>, <c>oi141_follow</c>,
/// <c>oi141_classes</c>). Its size, angle, font, interpreter and colour follow the legend's until
/// written, its weight is bold until written; <c>title(lg, …)</c> answers it; deleting it empties
/// it; a legend made again has a title of its own; and the legend draws it over its rows.
/// </summary>
[Collection("JG facade")]
public sealed class LegendTitleObjectTests : IDisposable
{
    private const string Legend = "plot(1:3); hold on; plot(3:-1:1); lg = legend('up', 'down'); ";

    public LegendTitleObjectTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("t = lg.Title; assert(isa(t, 'matlab.graphics.illustration.legend.Text')); assert(isa(t, 'matlab.graphics.Graphics')); assert(~isa(t, 'matlab.graphics.primitive.Text')); assert(isgraphics(t)); assert(isvalid(t));")]
    [InlineData("t = lg.Title; assert(isequal(t, lg.Title)); assert(isequal(get(lg, 'Title'), t)); assert(isequal(t.Parent, lg)); assert(isempty(t.Children));")]
    [InlineData("t = lg.Title; assert(isequal(properties(t)', {'String', 'Color', 'FontAngle', 'FontName', 'FontSize', 'FontWeight', 'Interpreter', 'ColorMode', 'FontAngleMode', 'FontNameMode', 'FontSizeMode', 'FontWeightMode', 'InterpreterMode', 'Children', 'Parent', 'Visible', 'HandleVisibility', 'Selected', 'SelectionHighlight', 'HitTest', 'PickableParts'}));")]
    [InlineData("t = lg.Title; assert(isequal(fieldnames(get(t))', properties(t)'));")]
    [InlineData("t = lg.Title; assert(isempty(t.String)); assert(strcmp(t.FontWeight, 'bold')); assert(t.FontSize == lg.FontSize); assert(isequal(t.Color, lg.TextColor)); assert(strcmp(t.FontName, lg.FontName)); assert(strcmp(t.Interpreter, lg.Interpreter)); assert(strcmp(t.FontAngle, 'normal'));")]
    [InlineData("t = lg.Title; assert(strcmp(t.ColorMode, 'auto')); assert(strcmp(t.FontSizeMode, 'auto')); assert(strcmp(t.FontWeightMode, 'auto')); assert(t.Visible == 'on'); assert(t.HitTest == 'off'); assert(strcmp(t.PickableParts, 'visible')); assert(strcmp(t.HandleVisibility, 'on'));")]
    public void ALegendsTitleIsATextObjectOfItsOwn(string code) => Run(Legend + code);

    [Theory]
    [InlineData("h = title(lg, 'Series'); assert(isequal(h, lg.Title)); assert(strcmp(lg.Title.String, 'Series'));")]
    [InlineData("title(lg, 'S', 'FontWeight', 'normal', 'Color', 'r'); assert(strcmp(lg.Title.FontWeight, 'normal')); assert(strcmp(lg.Title.FontWeightMode, 'manual')); assert(isequal(lg.Title.Color, [1 0 0]));")]
    [InlineData("title(lg, {'a', 'b'}); s = lg.Title.String; assert(iscell(s)); assert(isequal(size(s), [2 1])); assert(isequal(s, {'a'; 'b'}));")]
    [InlineData("title(lg, [\"a\" \"b\"]); assert(isequal(lg.Title.String, {'a'; 'b'}));")]
    [InlineData("title(lg, 5); assert(strcmp(lg.Title.String, '5'));")]
    [InlineData("lg.Title.String = \"s\"; assert(ischar(lg.Title.String)); assert(strcmp(lg.Title.String, 's'));")]
    [InlineData("lg.Title.String = {'p'; 'q'}; assert(isequal(lg.Title.String, {'p'; 'q'}));")]
    public void TitleOfTheLegendWritesItsTitleAndAnswersIt(string code) => Run(Legend + code);

    [Theory]
    [InlineData("lg.Title.String = 'T'; lg.FontSize = 14; assert(lg.Title.FontSize == 14); lg.FontSize = 6; assert(lg.Title.FontSize == 6);")]
    [InlineData("lg.Title.String = 'T'; lg.Title.FontSize = 20; lg.FontSize = 8; assert(lg.Title.FontSize == 20); assert(strcmp(lg.Title.FontSizeMode, 'manual'));")]
    [InlineData("lg.FontName = 'Arial'; assert(strcmp(lg.Title.FontName, 'Arial')); lg.Interpreter = 'latex'; assert(strcmp(lg.Title.Interpreter, 'latex')); lg.FontAngle = 'italic'; assert(strcmp(lg.Title.FontAngle, 'italic'));")]
    [InlineData("lg.TextColor = [1 0 0]; assert(isequal(lg.Title.Color, [1 0 0])); lg.Title.Color = [0 0 1]; lg.TextColor = [0 1 0]; assert(isequal(lg.Title.Color, [0 0 1]));")]
    [InlineData("lg.FontWeight = 'bold'; assert(strcmp(lg.Title.FontWeight, 'bold')); lg.FontWeight = 'normal'; assert(strcmp(lg.Title.FontWeight, 'bold'));")] // bold while it follows, whatever the legend's
    [InlineData("lg.Title.Color = 'none'; assert(strcmp(lg.Title.Color, 'none')); assert(strcmp(lg.Title.ColorMode, 'manual')); lg.Title.ColorMode = 'auto'; assert(isequal(lg.Title.Color, lg.TextColor));")]
    [InlineData("lg.Title.FontSizeMode = 'manual'; s = lg.Title.FontSize; lg.FontSize = 30; assert(lg.Title.FontSize == s); lg.Title.FontSizeMode = 'auto'; assert(lg.Title.FontSize == 30);")]
    [InlineData("set(lg.Title, 'FontS', 12); assert(lg.Title.FontSize == 12); assert(strcmp(get(lg.Title, 'string'), ''));")]
    public void ItsFontAndColourFollowTheLegendUntilWritten(string code) => Run(Legend + code);

    [Theory]
    [InlineData("get(lg.Title, 'Type')", "MATLAB:class:setgetPropertyNotFound", "Property Type not found in class matlab.graphics.illustration.legend.Text, or is not present in all elements of the array of class matlab.graphics.illustration.legend.Text.")]
    [InlineData("get(lg.Title, 'Position')", "MATLAB:class:GetProhibited", "No public property 'Position' for class ''Text''.")]
    [InlineData("set(lg.Title, 'Bogus', 1)", "MATLAB:class:InvalidProperty", "The name 'Bogus' is not an accessible property for an instance of class 'matlab.graphics.illustration.legend.Text'.")]
    [InlineData("lg.Title.fonts = 12", "MATLAB:noPublicFieldForClass", "Unrecognized property 'fonts' for class 'matlab.graphics.illustration.legend.Text'.")]
    [InlineData("lg.Title.FontSize = 0", "MATLAB:datatypes:OutsideRangeFiniteGreater0", "Error setting property 'FontSize' of class 'Text':\nValue should be a finite number greater than 0")]
    [InlineData("lg.Title.FontWeight = 'heavy'", "MATLAB:datatypes:InvalidEnumValue", "Error setting property 'FontWeight' of class 'Text':\n'heavy' is not a valid value. Use one of these values: 'normal' | 'bold'.")]
    [InlineData("lg.Title.Parent = gca", "MATLAB:hg:TryingToReparentConnectedObject", "Trying to reparent an already connected internal component object.")]
    [InlineData("title(lg)", "MATLAB:title:InvalidNumberOfInputs", "Not enough input arguments.")]
    public void ItRefusesInR2025bsWords(string statement, string identifier, string message) =>
        Run(Legend + $"try, {statement}; error('no:refusal', 'nothing refused'); catch e, assert(strcmp(e.identifier, '{identifier}'), e.identifier); assert(strcmp(e.message, sprintf('{message.Replace("\n", "\\n").Replace("'", "''")}')), e.message); end");

    [Theory]
    [InlineData("lg.Title.String = 'Kept'; delete(lg.Title); assert(isvalid(lg.Title)); assert(isempty(lg.Title.String));")]
    [InlineData("lg.Title.String = 'Kept'; lg2 = legend('again'); assert(isequal(lg, lg2)); assert(strcmp(lg2.Title.String, 'Kept'));")]
    [InlineData("lg.Title.String = 'Kept'; t = lg.Title; legend('off'); lg3 = legend('third'); assert(~isvalid(t)); assert(isempty(lg3.Title.String));")]
    [InlineData("lg.Title.String = 'Kept'; t = lg.Title; hold off; plot(5:7); lg4 = legend('new'); assert(~isvalid(t)); assert(isempty(lg4.Title.String));")] // a replacing plot deletes the legend
    [InlineData("lg.Title = 'assigned'; assert(strcmp(lg.Title.String, 'assigned'));")] // this build's extra: R2025b refuses the write
    public void DeletingItEmptiesItAndALegendMadeAgainHasItsOwn(string code) => Run(Legend + code);

    [Fact]
    public void TheLegendDrawsItsTitleBoldAndCentredOverItsRows()
    {
        Run(Legend + "f = gcf;");
        FigureModel figure = JG.Gcf();
        LegendModel legend = figure.Axes[0].Legend;

        RecordingRenderContext before = Render(figure);
        Rect2D bare = Assert.IsType<Rect2D>(legend.LastBox);
        Assert.DoesNotContain("Title", before.Texts);

        legend.Title = "Title";
        RecordingRenderContext after = Render(figure);
        Rect2D titled = Assert.IsType<Rect2D>(legend.LastBox);
        int at = after.Texts.IndexOf("Title");
        Assert.True(at >= 0);
        Assert.True(after.TextStyles[at].Bold);
        Assert.Equal(legend.TextStyle.FontSize, after.TextStyles[at].FontSize);
        Assert.Equal(titled.CenterX, after.TextPositions[at].X, 6);
        Assert.True(titled.Height > bare.Height);
        Assert.True(after.TextPositions[at].Y < after.TextPositions[after.Texts.IndexOf("up")].Y);

        // A hidden title keeps its room, as R2025b's does (oi141_font F24).
        legend.TitleVisible = false;
        RecordingRenderContext hidden = Render(figure);
        Assert.DoesNotContain("Title", hidden.Texts);
        Assert.Equal(titled.Height, Assert.IsType<Rect2D>(legend.LastBox).Height, 6);

        // A long title widens the box.
        legend.TitleVisible = true;
        legend.Title = "A much longer legend title than any entry";
        Render(figure);
        Assert.True(Assert.IsType<Rect2D>(legend.LastBox).Width > titled.Width);
    }

    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    private static void Run(string code)
    {
        ScriptRunResult result = JgsRunner.Run(
            "set(groot, 'DefaultFigureVisible', 'off'); " + code,
            new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
