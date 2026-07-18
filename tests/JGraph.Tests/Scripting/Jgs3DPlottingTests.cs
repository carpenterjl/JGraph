using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M20b: JGS matrix support (meshgrid, matrix arithmetic, matrix-aware math builtins) and the 3D /
/// colormap plotting verbs (surf, mesh, meshc, contour, imagesc, view, colormap, colorbar, ...).
/// </summary>
[Collection("JG facade")]
public class Jgs3DPlottingTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public Jgs3DPlottingTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    // --- Matrices in the language ---------------------------------------------------------------

    [Fact]
    public async Task Meshgrid_ProducesCoordinateMatrices()
    {
        ScriptRunResult result = await Run("""
            let [X, Y] = meshgrid([1, 2, 3], [10, 20])
            print(X[0], X[1])
            print(Y[0], Y[1])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[1, 2, 3] [1, 2, 3]", _output.NormalText);
        Assert.Contains("[10, 10, 10] [20, 20, 20]", _output.NormalText);
    }

    [Fact]
    public async Task MatrixArithmetic_BroadcastsElementwise()
    {
        ScriptRunResult result = await Run("""
            let [X, Y] = meshgrid([1, 2], [3, 4])
            let S = X * X + Y
            print(S[0], S[1])
            let T = S * 10
            print(T[0])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[4, 7] [5, 8]", _output.NormalText);
        Assert.Contains("[40, 70]", _output.NormalText);
    }

    [Fact]
    public async Task MathBuiltins_MapOverMatrices()
    {
        ScriptRunResult result = await Run("""
            let M = [[1, 9], [16, 25]]
            print(sqrt(M))
            print(-M)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[[1, 3], [4, 5]]", _output.NormalText);
        Assert.Contains("[[-1, -9], [-16, -25]]", _output.NormalText);
    }

    [Fact]
    public async Task ZerosOnes_TwoArgs_BuildMatrices()
    {
        ScriptRunResult result = await Run("""
            let Z = zeros(2, 3)
            let O = ones(2, 2)
            print(length(Z), length(Z[0]))
            print(O[1])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("2 3", _output.NormalText);
        Assert.Contains("[1, 1]", _output.NormalText);
    }

    [Fact]
    public async Task RaggedMatrix_IsAClearError()
    {
        ScriptRunResult result = await Run("surf([[1, 2], [3]])");

        Assert.False(result.Success);
        Assert.Contains("same length", result.Message);
    }

    // --- 3D plotting verbs ----------------------------------------------------------------------

    [Fact]
    public async Task Surf_BuildsA3DAxes_WithASurfacePlot()
    {
        ScriptRunResult result = await Run("""
            let x = linspace(-2, 2, 9)
            let y = linspace(-2, 2, 9)
            let [X, Y] = meshgrid(x, y)
            surf(x, y, X * X + Y * Y)
            view(45, 60)
            zlabel("height")
            zlim(0, 10)
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures)!.Axes[^1];
        Assert.True(axes.Is3D);
        var surface = Assert.IsType<SurfacePlot>(axes.Plots[0]);
        Assert.Equal(SurfaceStyle.FilledWithWireframe, surface.Style);
        Assert.Equal(9, surface.X.Length);
        Assert.Equal(4, surface.Z[0, 4], 6); // row 0 is y = -2, col 4 is x = 0 -> 0 + 4
        Assert.Equal(45, axes.Azimuth);
        Assert.Equal(60, axes.Elevation);
        Assert.Equal("height", axes.ZAxis.Label);
        Assert.False(axes.ZAxis.AutoScale);
        Assert.Equal(10, axes.ZAxis.Range.Max);
    }

    [Fact]
    public async Task Mesh_And_Meshc_SelectWireframeStyles()
    {
        ScriptRunResult result = await Run("""
            mesh([[1, 2], [3, 4]])
            show()
            """);

        Assert.True(result.Success, result.Message);
        var surface = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.Equal(SurfaceStyle.Wireframe, surface.Style);
        Assert.False(surface.ShowContourBelow);

        JG.Reset();
        _figures.Clear();
        ScriptRunResult meshc = await Run("""
            meshc([0, 1], [0, 1], [[1, 2], [3, 4]])
            show()
            """);

        Assert.True(meshc.Success, meshc.Message);
        var contoured = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.True(contoured.ShowContourBelow);
    }

    [Fact]
    public async Task Contour_And_Contourf_BuildContourPlots()
    {
        ScriptRunResult result = await Run("""
            let x = linspace(0, 1, 5)
            contour(x, x, ones(5, 5))
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = _figures[0].Axes[^1];
        Assert.False(axes.Is3D);
        var contour = Assert.IsType<ContourPlot>(axes.Plots[0]);
        Assert.False(contour.Filled);

        JG.Reset();
        _figures.Clear();
        ScriptRunResult filled = await Run("""
            let x = linspace(0, 1, 5)
            contourf(x, x, ones(5, 5), [0.5, 1.5])
            show()
            """);

        Assert.True(filled.Success, filled.Message);
        var band = Assert.IsType<ContourPlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.True(band.Filled);
        Assert.Equal([0.5, 1.5], band.Levels!);
    }

    [Fact]
    public async Task Imagesc_And_Pcolor_BuildImagePlots()
    {
        ScriptRunResult result = await Run("""
            imagesc([[1, 2], [3, 4]])
            show()
            """);

        Assert.True(result.Success, result.Message);
        Assert.IsType<ImagePlot>(_figures[0].Axes[^1].Plots[0]);

        JG.Reset();
        _figures.Clear();
        ScriptRunResult pc = await Run("""
            pcolor([0, 5], [0, 10], [[1, 2], [3, 4]])
            show()
            """);

        Assert.True(pc.Success, pc.Message);
        var image = Assert.IsType<ImagePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.Equal(5, image.XExtent.Max);
        Assert.Equal(10, image.YExtent.Max);
    }

    [Fact]
    public async Task Colormap_And_Colorbar_ApplyToTheCurrentAxes()
    {
        ScriptRunResult result = await Run("""
            surf([[1, 2], [3, 4]])
            colormap("jet")
            colorbar()
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = _figures[0].Axes[^1];
        var surface = (SurfacePlot)axes.Plots[0];
        Assert.Equal("Jet", surface.Colormap.Name);
        Assert.True(axes.Colorbar.Visible);
    }

    [Fact]
    public async Task Colormap_UnknownName_IsAScriptError()
    {
        ScriptRunResult result = await Run("""
            surf([[1, 2], [3, 4]])
            colormap("plasma")
            """);

        Assert.False(result.Success);
        Assert.Contains("Unknown colormap", result.Message);
        Assert.Contains("viridis", result.Message);
    }

    [Fact]
    public async Task EndToEnd_SincSurface_WithCamera_Colormap_AndColorbar()
    {
        ScriptRunResult result = await Run("""
            let x = linspace(-8, 8, 30)
            let [X, Y] = meshgrid(x, x)
            let R = sqrt(X * X + Y * Y) + 0.01
            surf(x, x, sin(R) / R)
            title("Sinc surface")
            view(30, 45)
            colormap("hot")
            colorbar()
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures)!.Axes[^1];
        Assert.True(axes.Is3D);
        Assert.Equal(30, axes.Azimuth);
        Assert.Equal(45, axes.Elevation);
        Assert.Equal("Sinc surface", axes.Title);
        var surface = (SurfacePlot)axes.Plots[0];
        Assert.Equal("Hot", surface.Colormap.Name);
        Assert.Equal(30, surface.Z.GetLength(0));
        Assert.True(axes.Colorbar.Visible);

        // The peak of sinc(r) is near the center of the grid.
        double center = surface.Z[15, 15];
        Assert.InRange(center, 0.8, 1.0);
    }
}
