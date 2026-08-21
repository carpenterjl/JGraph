using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M72: the gaps the capability report's own gallery work turned up — transparency and <c>'none'</c>,
/// patch lighting, TeX text, the volume verbs' documented forms, image values in plain arithmetic,
/// GIF output, and the small refusals with easy spellings.
/// </summary>
[Collection("JG facade")]
public class MatlabM72ParityTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM72ParityTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    // --- A matrix echoes as the thing that was typed ----------------------------------------------

    [Fact]
    public async Task AMatrixEchoes_InRowsRatherThanColumnMajorOrder()
    {
        ScriptRunResult result = await RunMatlab("""
            a = [3 2 1 0; 4 5 6 7]
            b = [1, 2; 3, 4]
            v = [1 2 3]
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("a = [3, 2, 1, 0; 4, 5, 6, 7]", _output.NormalLines);
        Assert.Contains("b = [1, 2; 3, 4]", _output.NormalLines);
        Assert.Contains("v = [1, 2, 3]", _output.NormalLines);
    }

    // --- Transparency, and 'none' where MATLAB writes it -----------------------------------------

    [Fact]
    public async Task APatchTakesFaceAlphaEdgeAlphaAndNone()
    {
        ScriptRunResult result = await RunMatlab("""
            [X, Y, Z] = meshgrid(linspace(-2, 2, 8));
            p = patch(isosurface(X, Y, Z, X.^2 + Y.^2 + Z.^2, 2));
            set(p, 'FaceAlpha', 0.4, 'EdgeAlpha', 0.25);
            fa = get(p, 'FaceAlpha');
            ea = get(p, 'EdgeAlpha');
            set(p, 'EdgeColor', 'none');
            edge = get(p, 'EdgeColor');
            set(p, 'FaceColor', 'none');
            face = get(p, 'FaceColor');
            set(p, 'FaceColor', 'r');
            back = numel(get(p, 'FaceColor'));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0.4, Number(result, "fa"), 10);
        Assert.Equal(0.25, Number(result, "ea"), 10);
        Assert.Equal("none", Text(result, "edge"));
        Assert.Equal("none", Text(result, "face"));
        Assert.Equal(3.0, Number(result, "back"));
    }

    [Fact]
    public async Task ASurfaceTakesItsOptionsAtConstruction()
    {
        // surf and its family took no name/value pairs at all before M72, so the commonest way to
        // write a translucent surface in MATLAB was an arity error here.
        ScriptRunResult result = await RunMatlab("""
            [X, Y] = meshgrid(1:5, 1:5);
            s = surf(X, Y, X + Y, 'FaceAlpha', 0.4, 'EdgeColor', 'none');
            fa = get(s, 'FaceAlpha');
            edges = isempty(get(s, 'EdgeColor'));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0.4, Number(result, "fa"), 10);
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "edges").RawValue));
    }

    [Fact]
    public async Task NoneReachesEveryKindThatCanBeUnfilled()
    {
        ScriptRunResult result = await RunMatlab("""
            b = bar([1 2 3]);
            set(b, 'EdgeColor', 'none');
            barEdge = get(b, 'EdgeColor');
            h = histogram(randn(1, 40));
            set(h, 'EdgeColor', 'none');
            histEdge = get(h, 'EdgeColor');
            a = area([1 2 3]);
            set(a, 'EdgeColor', 'none');
            areaEdge = get(a, 'EdgeColor');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("none", Text(result, "barEdge"));
        Assert.Equal("none", Text(result, "histEdge"));
        Assert.Equal("none", Text(result, "areaEdge"));
    }

    // --- A patch is lit, and a patch-only axes is three-dimensional -------------------------------

    [Fact]
    public async Task APatchAnswersTheLightingProperties_AndLightingReachesIt()
    {
        ScriptRunResult result = await RunMatlab("""
            [X, Y, Z] = meshgrid(linspace(-2, 2, 8));
            p = patch(isosurface(X, Y, Z, X.^2 + Y.^2 + Z.^2, 2));
            before = get(p, 'FaceLighting');
            camlight;
            lighting gouraud;
            after = get(p, 'FaceLighting');
            material dull;
            diffuse = get(p, 'DiffuseStrength');
            ambient = get(p, 'AmbientStrength');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("flat", Text(result, "before"));
        Assert.Equal("gouraud", Text(result, "after"));
        Assert.Equal(0.8, Number(result, "diffuse"), 10);
        Assert.Equal(0.3, Number(result, "ambient"), 10);
    }

    [Fact]
    public async Task APatchWithHeights_PutsItsAxesInThreeDimensions()
    {
        // Until M72 only the surface verbs set the flag, so an axes whose only child was an
        // isosurface stayed flat and view(3) moved nothing.
        ScriptRunResult result = await RunMatlab("""
            [X, Y, Z] = meshgrid(linspace(-2, 2, 8));
            patch(isosurface(X, Y, Z, X.^2 + Y.^2 + Z.^2, 2));
            spatial = numel(get(gca, 'View'));
            view(2);
            flat = get(gca, 'View');
            view(3);
            back = get(gca, 'View');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2.0, Number(result, "spatial"));
        Assert.True(JG.Gca().Is3D);
        Assert.Equal([0.0, 90.0], (double[])Assert.Single(result.Variables, v => v.Name == "flat").RawValue!);
        Assert.Equal([-37.5, 30.0], (double[])Assert.Single(result.Variables, v => v.Name == "back").RawValue!);
    }

    // --- TeX --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"\sigma", "σ")]
    [InlineData(@"x\cdot y", "x·y")]
    [InlineData(@"a\pm b", "a±b")]
    [InlineData(@"e^{-r^2}", "e⁻ʳ²")]
    [InlineData(@"H_2O", "H₂O")]
    [InlineData(@"x^{10}", "x¹⁰")]
    [InlineData(@"\alpha\rightarrow\infty", "α→∞")]
    [InlineData(@"\bf{bold}", "bold")]
    [InlineData(@"100\circ", "100∘")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData("plain text", "plain text")]
    public void TexMarkup_RendersMatlabsSubset(string written, string shown) =>
        Assert.Equal(shown, TexMarkup.Render(written, TextInterpreter.Tex));

    [Fact]
    public void TexMarkup_LeavesEverythingAloneWhenTheInterpreterIsNone() =>
        Assert.Equal(@"x\cdot e^{-r^2}", TexMarkup.Render(@"x\cdot e^{-r^2}", TextInterpreter.None));

    [Fact]
    public void TexMarkup_ShowsACommandItDoesNotKnowRatherThanDroppingIt() =>
        Assert.Equal(@"\frobnicate x", TexMarkup.Render(@"\frobnicate x", TextInterpreter.Tex));

    [Fact]
    public async Task ATextObjectKeepsItsMarkupAndAnswersItsInterpreter()
    {
        ScriptRunResult result = await RunMatlab("""
            t = text(1, 1, 'x\cdot e^{-r^2}');
            raw = get(t, 'String');
            how = get(t, 'Interpreter');
            set(t, 'Interpreter', 'none');
            off = get(t, 'Interpreter');
            title('a', 'Interpreter', 'latex');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(@"x\cdot e^{-r^2}", Text(result, "raw"));
        Assert.Equal("tex", Text(result, "how"));
        Assert.Equal("none", Text(result, "off"));
    }

    [Fact]
    public async Task AnUnknownInterpreterWordRefusesByName()
    {
        ScriptRunResult result = await RunMatlab("t = text(1, 1, 'x'); set(t, 'Interpreter', 'bogus');");

        Assert.False(result.Success);
        Assert.Contains("'tex', 'latex' or 'none'", result.Message);
    }

    // --- The volume verbs' documented forms -------------------------------------------------------

    [Fact]
    public async Task SliceCutsAVolumeInBothOfItsForms()
    {
        ScriptRunResult result = await RunMatlab("""
            [X, Y, Z] = meshgrid(linspace(-2, 2, 10));
            V = X.^2 + Y.^2 + Z.^2;
            full = numel(slice(X, Y, Z, V, 0, 0, 0));
            bare = numel(slice(V, 5, [], []));
            some = numel(slice(X, Y, Z, V, [-1 1], 0, [], 'linear'));
            kind = get(gca, 'Type');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3.0, Number(result, "full"));
        Assert.Equal(1.0, Number(result, "bare"));
        Assert.Equal(3.0, Number(result, "some"));
    }

    [Fact]
    public async Task JgsKeepsItsOwnSlice()
    {
        // The name is taken twice and the JGS surface is frozen, so the array slicer has to survive
        // the volume verb being declared over it in the other dialect.
        ScriptRunResult result = await new JgsScriptEngine().RunAsync(
            "let a = [10, 20, 30, 40]\nlet piece = slice(a, 1, 3)",
            new ScriptContext(_output, static (_, _) => { }),
            default);

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            [20.0, 30.0],
            (double[])Assert.Single(result.Variables, v => v.Name == "piece").RawValue!);
    }

    [Fact]
    public async Task EveryDocumentedStreamlineFormTraces()
    {
        ScriptRunResult result = await RunMatlab("""
            [X, Y] = meshgrid(1:10, 1:10);
            U = ones(10); V = X * 0.1;
            a = numel(streamline(X, Y, U, V, 1, 1));
            b = numel(streamline(U, V, 1, 1));
            [Xv, Yv, Zv] = meshgrid(1:6, 1:6, 1:6);
            Uv = ones(6, 6, 6); Vv = zeros(6, 6, 6); Wv = ones(6, 6, 6) * 0.2;
            c = numel(streamline(Xv, Yv, Zv, Uv, Vv, Wv, 1, 1, 1));
            d = numel(streamline(Uv, Vv, Wv, 1, 1, 1));
            e = numel(streamline(stream2(X, Y, U, V, 1, 1)));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1.0, Number(result, "a"));
        Assert.Equal(1.0, Number(result, "b"));
        Assert.Equal(1.0, Number(result, "c"));
        Assert.Equal(1.0, Number(result, "d"));
        Assert.Equal(1.0, Number(result, "e"));
    }

    [Fact]
    public async Task StreamsliceColoursItsWholeFamilyAlike()
    {
        ScriptRunResult result = await RunMatlab("""
            [X, Y] = meshgrid(linspace(-2, 2, 16));
            h = streamslice(X, Y, -Y, X);
            many = numel(h) > 4;
            same = isequal(get(h(1), 'Color'), get(h(end), 'Color'));
            """);

        Assert.True(result.Success, result.Message);
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "many").RawValue));
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "same").RawValue));
    }

    [Fact]
    public async Task MeshgridAndNdgridCountTheirOutputs()
    {
        ScriptRunResult result = await RunMatlab("""
            [X, Y, Z] = meshgrid(1:3);
            m = numel(size(X)) * 100 + size(X, 3);
            [A, B, C] = ndgrid(1:2);
            n = size(A, 3);
            [P, Q] = meshgrid(1:3);
            two = numel(size(P));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(303.0, Number(result, "m"));
        Assert.Equal(2.0, Number(result, "n"));
        Assert.Equal(2.0, Number(result, "two"));
    }

    // --- A picture is a matrix of readings ---------------------------------------------------------

    [Fact]
    public async Task AnImageValueDoesPlainArithmetic()
    {
        ScriptRunResult result = await RunMatlab("""
            I = mat2gray(magic(8));
            J = 1 - I;
            kind = class(J);
            rows = size(J, 1);
            top = max(max(I .* 2));
            again = class(mat2gray(I));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("double", Text(result, "kind"));
        Assert.Equal(8.0, Number(result, "rows"));
        Assert.Equal(2.0, Number(result, "top"), 10);
    }

    // --- GIF ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AGifIsWrittenAndAppendedToFrameByFrame()
    {
        string folder = Directory.CreateTempSubdirectory("jgraph-gif").FullName;
        try
        {
            string path = Path.Combine(folder, "anim.gif").Replace("\\", "/");
            ScriptRunResult result = await RunMatlab($$"""
                for k = 1:4
                  A = ones(20, 20) * (k / 5);
                  if k == 1
                    imwrite(A, '{{path}}', 'LoopCount', Inf, 'DelayTime', 0.2);
                  else
                    imwrite(A, '{{path}}', 'WriteMode', 'append', 'DelayTime', 0.2);
                  end
                end
                first = mean(mean(im2mat(imread('{{path}}', 1))));
                last = mean(mean(im2mat(imread('{{path}}', 4))));
                """);

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(path));

            // Six bytes of signature, one graphic-control block per frame, and the loop extension.
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal("GIF89a"u8.ToArray(), bytes[..6]);
            Assert.Equal(0x3B, bytes[^1]);
            Assert.Equal(4, CountFrames(bytes));

            // The frames are the four different greys they were written from, brightest last.
            Assert.True(Number(result, "last") > Number(result, "first"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task TheIndexedImwriteFormReadsItsColourMap()
    {
        string folder = Directory.CreateTempSubdirectory("jgraph-indexed").FullName;
        try
        {
            string path = Path.Combine(folder, "indexed.png").Replace("\\", "/");
            ScriptRunResult result = await RunMatlab($"""
                imwrite(uint8(magic(8) * 4), gray(256), '{path}');
                back = size(imread('{path}'), 1);
                """);

            Assert.True(result.Success, result.Message);
            Assert.Equal(8.0, Number(result, "back"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AppendingToSomethingThatIsNotAGifRefusesByName()
    {
        ScriptRunResult result = await RunMatlab(
            "imwrite(ones(4), 'nowhere.png', 'WriteMode', 'append');");

        Assert.False(result.Success);
        Assert.Contains("only a GIF holds more than one frame", result.Message);
    }

    /// <summary>Counts a GIF's graphic-control blocks, which is one per frame as this writer emits them.</summary>
    private static int CountFrames(byte[] bytes)
    {
        int frames = 0;
        for (int i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] == 0x21 && bytes[i + 1] == 0xF9 && bytes[i + 2] == 0x04)
            {
                frames++;
            }
        }

        return frames;
    }

    // --- The small refusals with easy spellings ----------------------------------------------------

    [Fact]
    public async Task GridMinorWorksTheMinorLines()
    {
        // The word toggles, the way a bare grid toggles the majors, and it leaves those alone.
        ScriptRunResult result = await RunMatlab("""
            plot(1:5);
            grid minor;
            grid minor;
            """);

        Assert.True(result.Success, result.Message);
        Assert.False(JG.Gca().Grid.ShowMinor);

        ScriptRunResult once = await RunMatlab("plot(1:5); grid minor;");

        Assert.True(once.Success, once.Message);
        Assert.True(JG.Gca().Grid.ShowMinor);
    }

    [Fact]
    public async Task AFigureTakesItsPropertiesAtConstruction()
    {
        ScriptRunResult result = await RunMatlab("""
            f = figure('Position', [100 100 400 300], 'Name', 'made');
            named = get(gcf, 'Name');
            wide = get(gcf, 'Position');
            g = figure(7, 'Name', 'seven');
            seven = get(gcf, 'Name');
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("made", Text(result, "named"));
        Assert.Equal("seven", Text(result, "seven"));

        double[] position = (double[])Assert.Single(result.Variables, v => v.Name == "wide").RawValue!;
        Assert.Equal(400.0, position[2]);
        Assert.Equal(300.0, position[3]);
    }

    [Fact]
    public async Task AFigurePropertyWithNoValueRefusesByName()
    {
        ScriptRunResult result = await RunMatlab("figure('Position');");

        Assert.False(result.Success);
        Assert.Contains("needs a value", result.Message);
    }
}
