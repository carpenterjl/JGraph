using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
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
            print(X(0, :), X(1, :))
            print(Y(0, :), Y(1, :))
            print(size(X))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[1, 2, 3] [1, 2, 3]", _output.NormalText);
        Assert.Contains("[10, 10, 10] [20, 20, 20]", _output.NormalText);
        Assert.Contains("[2, 3]", _output.NormalText);
    }

    [Fact]
    public async Task MatrixArithmetic_BroadcastsElementwise()
    {
        ScriptRunResult result = await Run("""
            let [X, Y] = meshgrid([1, 2], [3, 4])
            let S = X * X + Y
            print(S(0, :), S(1, :))
            let T = S * 10
            print(T(0, :))
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

    /// <summary>
    /// M44 wave 1: <c>shading</c> stopped being a no-op. MATLAB drives it through FaceColor and
    /// EdgeColor, so the mode has to move both the interpolation and the grid lines: faceted keeps
    /// the lines, flat drops them, interp drops them and colors the corners.
    /// </summary>
    [Fact]
    public async Task Shading_SetsFaceInterpolationAndGridLines()
    {
        ScriptRunResult flat = await Run("""
            surf([[1, 2], [3, 4]])
            shading("flat")
            show()
            """);

        Assert.True(flat.Success, flat.Message);
        var flatSurface = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.Equal(SurfaceShading.Flat, flatSurface.Shading);
        Assert.Equal(SurfaceStyle.Filled, flatSurface.Style);

        JG.Reset();
        _figures.Clear();
        ScriptRunResult interp = await Run("""
            surf([[1, 2], [3, 4]])
            shading("interp")
            show()
            """);

        Assert.True(interp.Success, interp.Message);
        var interpSurface = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.Equal(SurfaceShading.Interp, interpSurface.Shading);
        Assert.Equal(SurfaceStyle.Filled, interpSurface.Style);

        // A mesh has nothing but its lines, so shading must not take them away.
        JG.Reset();
        _figures.Clear();
        ScriptRunResult wire = await Run("""
            mesh([[1, 2], [3, 4]])
            shading("interp")
            show()
            """);

        Assert.True(wire.Success, wire.Message);
        Assert.Equal(SurfaceStyle.Wireframe, Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]).Style);
    }

    /// <summary>
    /// M44 wave 4: <c>lighting</c> and <c>material</c> stopped being no-ops. Both are surface
    /// properties in MATLAB, so both apply to every surface in the axes.
    /// </summary>
    [Fact]
    public async Task LightingAndMaterial_SetSurfaceProperties()
    {
        ScriptRunResult result = await Run("""
            surf([[1, 2], [3, 4]])
            lighting("gouraud")
            material("metal")
            show()
            """);

        Assert.True(result.Success, result.Message);
        var surface = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.Equal(SurfaceLighting.Gouraud, surface.FaceLighting);
        Assert.Equal(LightingModel.Metal, surface.Material);

        // MATLAB removed Phong shading but still accepts the word.
        JG.Reset();
        _figures.Clear();
        ScriptRunResult phong = await Run("""
            surf([[1, 2], [3, 4]])
            lighting("phong")
            material([0.1, 0.2, 0.3])
            show()
            """);

        Assert.True(phong.Success, phong.Message);
        var second = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.Equal(SurfaceLighting.Gouraud, second.FaceLighting);
        Assert.Equal(new LightingModel(0.1, 0.2, 0.3, 10, 1), second.Material);
    }

    /// <summary>
    /// The three verbs that create a light. <c>camlight</c> follows the camera — the one deliberate
    /// divergence from MATLAB, which freezes a world position and so leaves its highlight behind.
    /// </summary>
    [Fact]
    public async Task LightVerbs_AddLightsToTheAxes()
    {
        ScriptRunResult result = await Run("""
            surf([[1, 2], [3, 4]])
            light("Position", [0, 0, 1], "Color", "r", "Style", "local")
            lightangle(45, 60)
            camlight("headlight")
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = _figures[0].Axes[^1];
        Assert.Equal(3, axes.Lights.Count);

        Assert.Equal(LightStyle.Local, axes.Lights[0].Style);
        Assert.Equal(new Vector3D(0, 0, 1), axes.Lights[0].Position);
        Assert.Equal(Colors.Red, axes.Lights[0].Color);
        Assert.False(axes.Lights[0].FollowsCamera);

        // lightangle uses view()'s convention: elevation 60 lifts the light most of the way up.
        Assert.Equal(System.Math.Sin(System.Math.PI / 3), axes.Lights[1].Position.Z, 12);

        // A headlight sits exactly on the camera axis, which in camera coordinates is (0, 0, 1).
        Assert.True(axes.Lights[2].FollowsCamera);
        Assert.Equal(1, axes.Lights[2].Position.Z, 12);
        Assert.Equal(0, axes.Lights[2].Position.X, 12);
    }

    [Fact]
    public async Task NewPlotsClearTheAxesLights()
    {
        ScriptRunResult first = await Run("""
            surf([[1, 2], [3, 4]])
            camlight()
            """);

        Assert.True(first.Success, first.Message);
        Assert.Single(JG.Gca().Lights);

        ScriptRunResult second = await Run("""
            surf([[5, 6], [7, 8]])
            show()
            """);

        Assert.True(second.Success, second.Message);
        Assert.Empty(_figures[^1].Axes[^1].Lights);
    }

    [Fact]
    public async Task LightVerbs_RejectBadArguments()
    {
        ScriptRunResult badMode = await Run("surf([[1, 2], [3, 4]])\nlighting(\"glow\")");
        Assert.False(badMode.Success);
        Assert.Contains("glow", badMode.Message);

        JG.Reset();
        ScriptRunResult badPreset = await Run("surf([[1, 2], [3, 4]])\nmaterial(\"plastic\")");
        Assert.False(badPreset.Success);
        Assert.Contains("plastic", badPreset.Message);

        JG.Reset();
        ScriptRunResult badStyle = await Run("light(\"Style\", \"spot\")");
        Assert.False(badStyle.Success);
        Assert.Contains("spot", badStyle.Message);

        JG.Reset();
        ScriptRunResult badOption = await Run("light(\"Intensity\", 3)");
        Assert.False(badOption.Success);
        Assert.Contains("Intensity", badOption.Message);

        JG.Reset();
        ScriptRunResult badPosition = await Run("camlight(\"behind\")");
        Assert.False(badPosition.Success);
        Assert.Contains("behind", badPosition.Message);
    }

    [Fact]
    public async Task Shading_RejectsAnUnknownMode()
    {
        ScriptRunResult result = await Run("""
            surf([[1, 2], [3, 4]])
            shading("glossy")
            """);

        Assert.False(result.Success);
        Assert.Contains("glossy", result.Message);
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

    // --- M45.A: parametric surfaces -------------------------------------------------------------

    /// <summary>
    /// A meshgrid pair carries no more information than its two generating vectors, so it still
    /// collapses to the rectilinear fast path — which is what ADR 0046 §6 recorded and what keeps
    /// the analytic sweep and the floor contours working for every ordinary surface.
    /// </summary>
    [Fact]
    public async Task MeshgridMatrices_StayRectilinear()
    {
        ScriptRunResult result = await Run("""
            let [X, Y] = meshgrid([1, 2, 3], [10, 20, 30])
            surf(X, Y, X + Y)
            show()
            """);

        Assert.True(result.Success, result.Message);
        var surface = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.False(surface.IsParametric);
        Assert.Equal([1, 2, 3], surface.X);
        Assert.Equal([10, 20, 30], surface.Y);
    }

    /// <summary>
    /// An X/Y pair that varies in both directions has no generating vectors to collapse to. Before
    /// M45.A it was silently flattened to its first row and column, which drew a square where the
    /// script asked for a circle.
    /// </summary>
    [Fact]
    public async Task AGridThatVariesInBothDirections_BecomesParametric()
    {
        ScriptRunResult result = await Run("""
            let [T, R] = meshgrid([0, 1.5708, 3.1416, 4.7124], [1, 2])
            surf(R * cos(T), R * sin(T), R)
            show()
            """);

        Assert.True(result.Success, result.Message);
        var surface = Assert.IsType<SurfacePlot>(_figures[0].Axes[^1].Plots[0]);
        Assert.True(surface.IsParametric);
        Assert.NotNull(surface.XGrid);

        // The polar sweep reaches -2 in X, which a first-row collapse would never have seen.
        Assert.InRange(surface.GetXDataBounds().Min, -2.001, -1.999);
    }

    /// <summary>
    /// The other half of M55's implicit-x fix: MATLAB counts a plot's samples from 1, and JGS
    /// numbers everything from 0 (ADR 0028). The verbs that draw heights alone now ask the dialect
    /// rather than the facade, and this is the test that says the JGS side did not move.
    /// </summary>
    [Fact]
    public async Task HeightsAloneAreStillNumberedFromZeroHere()
    {
        ScriptRunResult result = await Run("""
            figure(1)
            print(get(plot([3, 5, 2, 6]), "XData"))
            print(get(stem([3, 5, 2, 6]), "XData"))
            print(get(stairs([3, 5, 2, 6]), "XData"))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, _output.NormalLines.Count);
        Assert.All(_output.NormalLines, written => Assert.Equal("[0, 1, 2, 3]", written));
    }
}
