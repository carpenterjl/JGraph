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
/// Open item 142 (ADR 0242): MATLAB-dialect <c>imshow</c> as R2025b's base-MATLAB <c>imshow.m</c> does
/// it (probes <c>oi142_imshow</c>, 50 forms, and <c>oi142_errors</c>, 30 refusals and two pictures):
/// the high-level image call, then reversed y, ticks out, no grid, a 1:1:1 data aspect, the axes
/// invisible with its title and labels drawn, the class's display range on CLim and 256 greys on
/// the colormap of a grey picture, the map of an indexed one, and a lone picture making the figure's
/// next plot start afresh. With it: a figure's 'replace' and 'replacechildren' are obeyed once, and
/// a replacing plot gives back the visibility, aspect, layer and directions imshow set.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabImshowTests : IDisposable
{
    public MatlabImshowTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("h = imshow(magic(4), []); ax = gca; assert(strcmp(h.Type, 'image')); assert(isequal(ax.XLim, [0.5 4.5])); assert(strcmp(ax.XLimMode, 'manual')); assert(isequal(ax.YLim, [0.5 4.5])); assert(strcmp(ax.YDir, 'reverse')); assert(strcmp(ax.TickDir, 'out')); assert(strcmp(ax.TickDirMode, 'manual'));")]
    [InlineData("imshow(magic(4), []); ax = gca; assert(isequal(ax.DataAspectRatio, [1 1 1])); assert(strcmp(ax.DataAspectRatioMode, 'manual')); assert(strcmp(ax.PlotBoxAspectRatioMode, 'auto')); assert(ax.Visible == 'off'); assert(ax.XGrid == 'off'); assert(strcmp(ax.Layer, 'top'));")]
    [InlineData("imshow(magic(4), []); ax = gca; assert(isequal(ax.CLim, [1 16])); assert(strcmp(ax.CLimMode, 'manual')); assert(isequal(size(ax.Colormap), [256 3])); assert(isequal(ax.Colormap(1, :), [0 0 0])); assert(isequal(ax.Colormap(end, :), [1 1 1]));")]
    [InlineData("h = imshow(reshape(1:20, 4, 5) / 20); assert(isequal(get(gca, 'CLim'), [0 1])); assert(isequal(xlim, [0.5 5.5])); assert(strcmp(h.CDataMapping, 'scaled')); assert(isequal(h.XData, [1 5]));")]
    [InlineData("h = imshow(uint8(magic(4) * 10)); assert(isequal(get(gca, 'CLim'), [0 255])); assert(isa(h.CData, 'uint8'));")]
    [InlineData("h = imshow(uint16(magic(4) * 1000)); assert(isequal(get(gca, 'CLim'), [0 65535])); assert(isa(h.CData, 'uint16'));")]
    [InlineData("h = imshow(int16(magic(4) * 100 - 800)); assert(isequal(get(gca, 'CLim'), [-32768 32767])); assert(isa(h.CData, 'int16'));")]
    [InlineData("h = imshow(int8(magic(4) - 8)); assert(isequal(get(gca, 'CLim'), [-128 127]));")]
    [InlineData("h = imshow(logical(eye(4))); assert(isequal(get(gca, 'CLim'), [0 1])); assert(strcmp(h.CDataMapping, 'scaled')); assert(islogical(h.CData));")]
    [InlineData("h = imshow(int16(magic(4) * 100 - 800), []); assert(isequal(get(gca, 'CLim'), [-700 800]));")]
    [InlineData("imshow(ones(3) * 5, []); assert(isequal(get(gca, 'CLim'), [0 1]));")] // a constant picture takes its class's range
    [InlineData("imshow(magic(4) / 16, [0.2 0.8]); assert(isequal(get(gca, 'CLim'), [0.2 0.8]));")]
    [InlineData("imshow(magic(4) / 16, 'DisplayRange', [0.2 0.8]); assert(isequal(get(gca, 'CLim'), [0.2 0.8]));")]
    [InlineData("imshow(magic(4) / 16, 'disp', [0 2]); assert(isequal(get(gca, 'CLim'), [0 2]));")] // a start of the name
    [InlineData("imshow(magic(4) / 16, \"DisplayRange\", [0 2]); assert(isequal(get(gca, 'CLim'), [0 2]));")]
    [InlineData("imshow(magic(4) * 4 / 16, 'DisplayRange', []); assert(isequal(get(gca, 'CLim'), [0.25 4]));")]
    public void AGreyPictureIsShownThroughTheAxesGreysAndRange(string code) => Run(code);

    [Theory]
    [InlineData("h = imshow(reshape(1:48, 4, 4, 3) / 48); ax = gca; assert(strcmp(h.CDataMapping, 'direct')); assert(strcmp(ax.CLimMode, 'auto')); assert(isequal(ax.CLim, [0 1])); assert(ax.Visible == 'off');")]
    [InlineData("h = imshow(uint8(reshape(1:48, 4, 4, 3) * 5)); assert(isa(h.CData, 'uint8')); assert(strcmp(h.CDataMapping, 'direct'));")]
    [InlineData("imshow(reshape(1:48, 4, 4, 3) / 48, []); assert(isequal(get(gca, 'CLim'), [0 1])); assert(strcmp(get(gca, 'CLimMode'), 'manual'));")]
    [InlineData("h = imshow(reshape(-3:44, 4, 4, 3) / 40); assert(min(h.CData(:)) == 0); assert(max(h.CData(:)) == 1);")] // clipped to [0 1]
    [InlineData("h = imshow(magic(4) / 2, jet(8)); ax = gca; assert(strcmp(h.CDataMapping, 'direct')); assert(isequal(size(ax.Colormap), [8 3])); assert(strcmp(ax.CLimMode, 'auto'));")]
    [InlineData("imshow(magic(4) / 16, 'Colormap', jet(16)); assert(isequal(size(get(gca, 'Colormap')), [16 3])); assert(strcmp(get(gca, 'CLimMode'), 'manual'));")]
    [InlineData("imshow(reshape(1:48, 4, 4, 3) / 48, jet(8)); assert(isequal(size(get(gca, 'Colormap')), [8 3]));")] // a map given positionally makes an indexed picture
    [InlineData("imshow(reshape(1:48, 4, 4, 3) / 48, 'Colormap', jet(8)); [msg, id] = lastwarn; assert(strcmp(id, 'images:imageDisplayValidateParams:colormapWithTruecolor')); assert(strcmp(msg, 'Ignoring ''Colormap'' parameter provided with a truecolor image.'));")]
    [InlineData("imshow(magic(4) / 16 + 1i); [msg, id] = lastwarn; assert(strcmp(id, 'images:imageDisplayValidateParams:displayingRealPart')); assert(isreal(get(findobj(gca, 'Type', 'image'), 'CData')));")]
    public void ColourAndIndexedPicturesKeepTheirOwnColours(string code) => Run(code);

    [Theory]
    [InlineData("h = imshow(magic(4) / 16, 'XData', [10 20], 'YData', [5 8]); assert(isequal(h.XData, [10 20])); assert(isequal(h.YData, [5 8])); assert(max(abs(xlim - [8.33333333333333 21.6666666666667])) < 1e-9); assert(isequal(ylim, [4.5 8.5]));")]
    [InlineData("h = imshow(magic(4) / 16, 'XData', [10 20]); assert(isequal(h.YData, [1 4]));")]
    [InlineData("h = imshow(magic(4) / 16, 'Interpolation', 'bilinear'); assert(strcmp(h.Interpolation, 'bilinear'));")]
    [InlineData("h = imshow(magic(4) / 16); assert(strcmp(h.Interpolation, 'nearest')); assert(strcmp(h.BusyAction, 'cancel')); assert(h.Interruptible == 'off');")]
    [InlineData("a1 = subplot(1, 2, 1); a2 = subplot(1, 2, 2); plot(1:3); imshow(magic(4) / 16, 'Parent', a1); assert(isequal(gca, a2)); assert(isequal(a1.XLim, [0.5 4.5])); assert(a1.Visible == 'off'); assert(a2.Visible == 'on');")]
    [InlineData("plot(1:10); hold on; imshow(magic(4) / 16); ax = gca; assert(numel(ax.Children) == 2); assert(strcmp(ax.XLimMode, 'auto')); assert(strcmp(ax.YDir, 'reverse')); assert(ax.Visible == 'off'); assert(strcmp(ax.Layer, 'bottom'));")]
    [InlineData("plot(1:10); grid on; ax0 = gca; imshow(magic(4) / 16); assert(isequal(gca, ax0)); assert(numel(ax0.Children) == 1); assert(ax0.XGrid == 'off');")]
    [InlineData("imshow(magic(4) / 16); imshow(magic(4) / 32); assert(numel(findobj(gcf, 'Type', 'image')) == 1);")]
    [InlineData("imshow(zeros(0, 3)); ax = gca; assert(strcmp(ax.XLimMode, 'auto')); assert(isequal(ax.CLim, [0 1])); assert(ax.Visible == 'off');")]
    [InlineData("imshow(magic(4) / 16); title('x'); assert(strcmp(get(gca, 'Title').String, 'x'));")]
    public void TheOptionsTheParentAndHold(string code) => Run(code);

    [Theory]
    [InlineData("imshow(magic(4) / 16); assert(strcmp(get(gcf, 'NextPlot'), 'replacechildren'));")]
    [InlineData("imshow(magic(4) / 16); ax0 = gca; plot(1:3); ax = gca; assert(~isequal(ax, ax0)); assert(ax.Visible == 'on'); assert(strcmp(ax.YDir, 'normal')); assert(strcmp(get(gcf, 'NextPlot'), 'add')); assert(numel(findobj(gcf, 'Type', 'axes')) == 1);")]
    [InlineData("subplot(1, 2, 1); imshow(magic(4) / 16); assert(strcmp(get(gcf, 'NextPlot'), 'add'));")] // not at the default place
    [InlineData("imshow(magic(4) / 16, 'InitialMagnification', 'fit'); assert(strcmp(get(gcf, 'NextPlot'), 'add'));")]
    [InlineData("imshow(magic(4) / 16, 'Border', 'tight', 'InitialMagnification', 'fit'); assert(isequal(get(gca, 'Position'), [0 0 1 1])); assert(strcmp(get(gcf, 'NextPlot'), 'replacechildren'));")]
    [InlineData("f = gcf; f.NextPlot = 'new'; imshow(magic(4) / 16); assert(~isequal(gcf, f));")]
    public void ALonePictureMakesTheNextPlotStartTheFigureAfresh(string code) => Run(code);

    [Theory]
    [InlineData("imshow()", "images:imshow:tooFewArgs", "IMSHOW expected at least 1 input argument but was called instead with 0 input arguments.")]
    [InlineData("imshow(magic(4), 3)", "images:imshow:invalidSyntax", "Invalid IMSHOW syntax.")]
    [InlineData("imshow(1:4, 1:4, magic(4))", "images:imshow:invalidSyntax", "Invalid IMSHOW syntax.")]
    [InlineData("imshow(magic(4), [5 1])", "images:checkDisplayRange:badDisplayRangeValues", "HIGH must be greater than LOW.")]
    [InlineData("imshow(magic(4), 'Bogus', 1)", "images:imageDisplayParsePVPairs:unrecognizedParameter", "The parameter, Bogus, is not recognized by imageDisplayParsePVPairs")]
    [InlineData("imshow(magic(4), [0 1], 3)", "images:validate:tooManyInputs", "Too many input arguments were passed to imageDisplayParsePVPairs.")]
    [InlineData("imshow(magic(4), ones(2, 2))", "images:imageDisplayParsePVPairs:invalidInputs", "Invalid input arguments.")]
    [InlineData("imshow(magic(4), 'XData')", "images:imageDisplayParsePVPairs:oddNumberArgs", "Function IMAGEDISPLAYPARSEPVPAIRS expected an even number of parameter/value arguments.")]
    [InlineData("imshow(magic(4), 'In', 1)", "MATLAB:imageDisplayParsePVPairs:ambiguousStringChoice", "Expected input number 2, PARAM, to match one of these values:\n\n'Colormap', 'DisplayRange', 'InitialMagnification', 'XData', 'YData', 'Interpolation', 'Parent', 'Border', 'Reduce'\n\nThe input, 'In', matched more than one valid value.")]
    [InlineData("imshow(magic(4), 'XData', [1 2], 'Bogus', 1)", "MATLAB:imageDisplayParsePVPairs:unrecognizedStringChoice", "Expected input number 4, PARAM, to match one of these values:\n\n'Colormap', 'DisplayRange', 'InitialMagnification', 'XData', 'YData', 'Interpolation', 'Parent', 'Border', 'Reduce'\n\nThe input, 'Bogus', did not match any of the valid values.")]
    [InlineData("imshow(magic(4), 'Interpolation', 'cubic')", "MATLAB:imageDisplayValidateParams:unrecognizedStringChoice", "Expected Interpolation to match one of these values:\n\n'nearest', 'bilinear'\n\nThe input, 'cubic', did not match any of the valid values.")]
    [InlineData("imshow(magic(4), 'Border', 'wide')", "MATLAB:imshow:unrecognizedStringChoice", "Expected BORDER to match one of these values:\n\n'loose', 'tight'\n\nThe input, 'wide', did not match any of the valid values.")]
    [InlineData("imshow(magic(4), 'Colormap', [1 2])", "images:imageDisplayValidateParams:invalidColormap", "Colormap must be an m-by-3 numeric matrix.")]
    [InlineData("imshow(magic(4), 'XData', 5)", "images:imageDisplayValidateParams:need2Coords", "XDATA must be a 2-element vector.")]
    [InlineData("imshow(magic(4), 'InitialMagnification', 'big')", "MATLAB:imshow:unrecognizedStringChoice", "Expected INITIAL_MAG to match one of these values:\n\n'fit'\n\nThe input, 'big', did not match any of the valid values.")]
    [InlineData("imshow({1})", "MATLAB:imageDisplayValidateParams:invalidType", "Expected input number 1, I, to be one of these types:\n\ndouble, single, uint8, uint16, uint32, uint64, int8, int16, int32, int64, logical\n\nInstead its type was cell.")]
    [InlineData("imshow(int16(magic(4)), jet(8))", "images:imageDisplayValidateParams:invalidIndexedImage", "An indexed image can be uint8, uint16, double, single, or logical.")]
    [InlineData("imshow(int16(ones(4, 4, 3)))", "images:imageDisplayValidateParams:invalidRGBClass", "RGB images must be uint8, uint16, single, or double.")]
    [InlineData("imshow(true(2, 2, 3))", "images:imageDisplayValidateParams:expected2D", "If input is logical (binary), it must be two-dimensional.")]
    [InlineData("imshow(rand(4, 4, 2))", "images:imageDisplayValidateParams:unsupportedDimension", "Multi-plane image inputs must be RGB images of size MxNx3.")]
    [InlineData("imshow('nofile.png')", "images:getImageFromFile:fileDoesNotExist", "Cannot find the specified file: \"nofile.png\".")]
    [InlineData("imshow(magic(4), [NaN 1])", "MATLAB:imageDisplayValidateParams:expectedNonNaN", "Expected input number 2, [LOW HIGH], to be non-NaN.")]
    [InlineData("imshow(magic(4), 'Parent', 5)", "images:imshow:invalidAxes", "HAX must be a valid axes handle.")]
    [InlineData("imshow('peppers.png', [0 1])", "images:imageDisplayParsePVPairs:unrecognizedParameter", "The parameter, peppers.png, is not recognized by imageDisplayParsePVPairs")]
    [InlineData("imshow(magic(4), 'Reduce', true)", "images:imshow:badReduceSyntax", "The Reduce parameter requires a filename syntax")]
    [InlineData("imshow(magic(4), 'DisplayRange', 5)", "images:checkDisplayRange:not2ElementVector", "[LOW HIGH] must be a 2-element vector.")]
    [InlineData("imshow(magic(4), [0 1 2])", "MATLAB:hg:shaped_arrays:ColorMapType", "Error setting property 'Colormap' of class 'Axes':\nValue must be either:\nan Mx3 array of type single or double in the range [0 1]\nan Mx3 array of type uint8.")]
    public void ItRefusesInR2025bsWords(string statement, string identifier, string message) =>
        Run($"try, {statement}; error('no:refusal', 'nothing refused'); catch e, assert(strcmp(e.identifier, '{identifier}'), e.identifier); assert(strcmp(e.message, sprintf('{message.Replace("\n", "\\n").Replace("'", "''")}')), e.message); end");

    [Theory]
    [InlineData("f = figure; set(f, 'NextPlot', 'replacechildren'); plot(1:3); assert(strcmp(get(f, 'NextPlot'), 'add')); subplot(2, 1, 1); plot(1:3); subplot(2, 1, 2); plot(1:3); assert(numel(findobj(f, 'Type', 'axes')) == 2);")]
    [InlineData("f = figure; set(f, 'NextPlot', 'replace'); plot(1:3); assert(strcmp(get(f, 'NextPlot'), 'add'));")]
    [InlineData("subplot(1, 2, 1); imshow(magic(4) / 16); ax = gca; plot(ax, 1:3); assert(ax.Visible == 'on'); assert(strcmp(ax.YDir, 'normal')); assert(strcmp(ax.TickDirMode, 'auto')); assert(strcmp(ax.DataAspectRatioMode, 'auto')); assert(strcmp(ax.Layer, 'bottom'));")]
    [InlineData("imagesc(magic(4)); ax = gca; plot(ax, 1:3); assert(isequal(gca, ax)); assert(strcmp(ax.YDir, 'normal')); assert(strcmp(ax.Layer, 'bottom'));")] // R2025b's replacing newplot resets YDir
    public void AFiguresReplaceIsObeyedOnceAndAReplacingPlotResetsWhatImshowSet(string code) => Run(code);

    [Fact]
    public void TheLabelsOfAnImshowAxesAreDrawnThoughTheAxesIsNot()
    {
        // R2025b sets imshow's title and labels visible, and draws them on the invisible axes; a plain
        // axes made invisible draws neither label (oi142_errors P1, P2).
        Run("imshow(kron(magic(4) / 16, ones(10))); xlabel('XLABELTEXT'); ylabel('YLABELTEXT'); title('TITLETEXT');");
        RecordingRenderContext shown = Render(JG.Gcf());
        Assert.Contains("XLABELTEXT", shown.Texts);
        Assert.Contains("YLABELTEXT", shown.Texts);
        Assert.Contains("TITLETEXT", shown.Texts);

        JG.Reset();
        Run("plot(1:10); xlabel('XLABELTEXT'); set(gca, 'Visible', 'off');");
        Assert.DoesNotContain("XLABELTEXT", Render(JG.Gcf()).Texts);

        JG.Reset();
        Run("plot(1:10); xlabel('XLABELTEXT'); set(gca, 'Visible', 'off'); set(get(gca, 'XLabel'), 'Visible', 'on'); assert(strcmp(get(get(gca, 'XLabel'), 'Visible'), 'on'));");
        Assert.Contains("XLABELTEXT", Render(JG.Gcf()).Texts);
    }

    [Fact]
    public void AnImageValueGoesToImageAsItsNumbersInTheMatlabDialect()
    {
        // image(imread(...)) is the image call R2025b makes of the array, not imshow's set-up.
        string file = Path.Combine(Path.GetTempPath(), "jgraph-oi142-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Run($"imwrite(uint8(reshape(1:48, 4, 4, 3) * 5), '{file}'); img = imread('{file}'); h = image(img); ax = gca; assert(ax.Visible == 'on'); assert(isa(h.CData, 'uint8')); assert(isequal(size(h.CData), [4 4 3]));");
        }
        finally
        {
            File.Delete(file);
        }
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
