using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave F: the legacy appearance verbs — the palette colormaps, the reflectance pair, the
/// equalizing grey map, the look-by-name commands, and the three indexed-image names that arrived
/// beside <c>imapprox</c>.
/// </summary>
[Collection("JG facade")]
public class MatlabAppearanceTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabAppearanceTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    [Fact]
    public async Task TheThreePaletteMapsCycleAndColormapTakesThemByName()
    {
        await RunAsserting("""
            f = flag(8);
            disp(size(f, 1));
            disp(isequal(f(1, :), f(5, :)));
            disp(isequal(f(1, :), [1 0 0]));

            p = prism(12);
            disp(isequal(p(1, :), p(7, :)));

            c = colorcube(64);
            disp(size(c, 1));
            disp(isequal(c(end, :), [0 0 0]));
            disp(min(min(c)) >= 0 && max(max(c)) <= 1);

            figure(1);
            surf(peaks(8));
            colormap('flag');
            m = colormap;
            disp(size(m, 2));
            """);

        Assert.Equal(new[] { "8", "true", "true", "true", "64", "true", "true", "3" }, _output.NormalLines);
    }

    [Fact]
    public async Task RgbplotDrawsOneLinePerChannel()
    {
        await RunAsserting("""
            figure(1);
            rgbplot(jet(16));
            disp(numel(findobj(gca, 'Type', 'line')));
            h = findobj(gca, 'Type', 'line');
            disp(numel(get(h(1), 'XData')));
            """);

        Assert.Equal(new[] { "3", "16" }, _output.NormalLines);
    }

    [Fact]
    public async Task ValidatecolorReadsEveryWayAColourCanBeWritten()
    {
        await RunAsserting("""
            disp(isequal(validatecolor('r'), [1 0 0]));
            disp(isequal(validatecolor('#00FF00'), [0 1 0]));
            disp(isequal(validatecolor('#0F0'), validatecolor('#00FF00')));
            disp(size(validatecolor({'r', 'g', 'b'}, 'multiple'), 1));
            disp(size(validatecolor([1 0 0; 0 1 0], 'multiple'), 1));
            """);

        Assert.Equal(new[] { "true", "true", "true", "3", "2" }, _output.NormalLines);
    }

    [Fact]
    public async Task ValidatecolorInsistsOnOneColourUnlessToldOtherwise()
    {
        string message = await RunExpectingFailure("disp(validatecolor({'r', 'g'}));");
        Assert.Contains("multiple", message, StringComparison.Ordinal);

        Assert.Contains("[0, 1]", await RunExpectingFailure("disp(validatecolor([2 0 0]));"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffuseAndSpecularAgreeWithTheGeometryTheyModel()
    {
        await RunAsserting("""
            % Light straight down the normal reflects everything.
            disp(diffuse(0, 0, 1, [0 0 1]));

            % Ninety degrees off, nothing; behind, still nothing rather than a negative.
            disp(round(diffuse(1, 0, 0, [0 0 1]), 12));
            disp(diffuse(0, 0, 1, [0 0 -1]));

            % The [azimuth elevation] form is the same direction as [x y z].
            disp(abs(diffuse(0, 0, 1, [0 90]) - diffuse(0, 0, 1, [0 0 1])) < 1e-12);

            % Elementwise, and the answer keeps the shape it was given.
            d = diffuse([0 1], [0 0], [1 0], [0 0 1]);
            disp(numel(d));

            % Viewer along the reflected ray sees the full highlight; a higher spread narrows it.
            disp(specular(0, 0, 1, [0 0 1], [0 0 1]));
            disp(specular(0, 0, 1, [0 0 1], [1 0 1], 2) > specular(0, 0, 1, [0 0 1], [1 0 1], 20));
            """);

        Assert.Equal(new[] { "1", "0", "0", "true", "2", "1", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ContrastSpreadsAPicturesOwnHistogram()
    {
        await RunAsserting("""
            X = magic(8);
            m = contrast(X);
            disp(size(m, 1));
            disp(size(m, 2));

            % Grey only, and never decreasing: it is a cumulative share.
            disp(isequal(m(:, 1), m(:, 3)));
            disp(all(diff(m(:, 1)) >= 0));
            disp(m(1, 1) >= 0 && m(end, 1) <= 1);
            disp(size(contrast(X, 16), 1));
            """);

        Assert.Equal(new[] { "64", "3", "true", "true", "true", "16" }, _output.NormalLines);
    }

    [Fact]
    public async Task HiddenPaintsAMeshOpaqueAndBackAgain()
    {
        await RunAsserting("""
            figure(1);
            h = mesh(peaks(10));
            disp(isempty(get(h, 'FaceColor')));
            hidden on;
            disp(numel(get(h, 'FaceColor')));
            hidden off;
            disp(isempty(get(h, 'FaceColor')));
            """);

        // Unset before and after; a colour while it is on.
        Assert.Equal(new[] { "true", "3", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task WhitebgAndColordefSwapTheWholeLook()
    {
        await RunAsserting("""
            figure(1);
            plot(1:5, 1:5);
            whitebg('k');
            dark = get(gcf, 'Color');
            disp(sum(dark) < 0.5);

            % No colour toggles back the other way.
            whitebg;
            light = get(gcf, 'Color');
            disp(sum(light) > 2.5);

            colordef black;
            disp(sum(get(gcf, 'Color')) < 1);
            colordef white;
            disp(sum(get(gcf, 'Color')) > 2.5);
            """);

        Assert.Equal(new[] { "true", "true", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task OrientAndOpenglAreAcceptedAndSayWhatTheyKnow()
    {
        await RunAsserting("""
            disp(orient);
            orient landscape;
            disp(orient);
            opengl;
            """);

        Assert.Equal(new[] { "portrait", "portrait" }, _output.NormalLines);
        Assert.Contains("sideways", await RunExpectingFailure("orient('sideways');"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CmpermuteKeepsThePictureWhileTheColoursMoveAround()
    {
        await RunAsserting("""
            X = [1 2 3; 3 2 1];
            map = [1 0 0; 0 1 0; 0 0 1];

            % A given order is exact: row 3 of the old map becomes row 1 of the new one.
            [Y, newmap] = cmpermute(X, map, [3 2 1]);
            disp(isequal(newmap(1, :), map(3, :)));
            disp(Y(1, 1));
            disp(Y(1, 3));

            % Whatever the shuffle, the colours each pixel names are unchanged.
            rng(7);
            [Z, shuffled] = cmpermute(X, map);
            same = true;
            for r = 1:2
              for c = 1:3
                same = same && isequal(shuffled(Z(r, c), :), map(X(r, c), :));
              end
            end
            disp(same);
            """);

        Assert.Equal(new[] { "true", "3", "1", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task CmuniqueCollapsesRepeatedColoursAndDitherTradesDepthForResolution()
    {
        await RunAsserting("""
            % Four pixels, two distinct colours.
            RGB = cat(3, [0 1; 1 0], [0 0; 0 0], [0 0; 0 0]);
            [Y, map] = cmunique(RGB);
            disp(size(map, 1));
            disp(Y(1, 1) == Y(2, 2));
            disp(Y(1, 1) ~= Y(1, 2));

            % A flat grey has no exact level, so dithering has to mix black and white.
            bw = dither(0.5 * ones(8));
            disp(class(bw));
            disp(sum(bw(:)) > 0 && sum(bw(:)) < 64);
            """);

        Assert.Equal(new[] { "2", "true", "true", "logical", "true" }, _output.NormalLines);
    }
}
