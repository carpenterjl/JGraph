using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>axis off</c> at the script's end of the wire. The word was parsed and thrown away — it sat in
/// the same arm as <c>auto</c> and <c>manual</c>, which really are no-ops — so the only way to clear
/// a frame was to paint every decoration in the background colour by hand.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabAxisOffTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabAxisOffTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), null));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public async Task AxisOff_ClearsTheFrameAndAxisOnPutsItBack()
    {
        await RunAsserting("""
            plot(1:10, (1:10).^2);
            axis off
            """);

        AxesModel axes = JG.Gca();
        Assert.False(axes.Visible);
        Assert.Single(axes.Plots);

        await RunAsserting("axis on");
        Assert.True(JG.Gca().Visible);
    }

    /// <summary>
    /// The gap the report was really about: a 3-D axes kept every decoration whatever was said to it,
    /// and the surface stays on the page once they go.
    /// </summary>
    [Fact]
    public async Task AxisOff_OnA3DAxes_LeavesTheSurfaceStanding()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(linspace(-3, 3, 12));
            surf(X, Y, sin(sqrt(X.^2 + Y.^2) + eps), 'EdgeColor', 'none');
            view(45, 30);
            axis off
            """);

        AxesModel axes = JG.Gca();
        Assert.True(axes.Is3D);
        Assert.False(axes.Visible);
        Assert.Single(axes.Plots);
        Assert.True(axes.Plots[0].Visible, "the child is not what Visible governs");
    }

    /// <summary>
    /// <c>camva</c> is a zoom, and reading it back was never the broken half — this pins that the
    /// angle is both stored and, per <see cref="Rendering.AxisOffAndViewAngleTests"/>, acted on.
    /// </summary>
    [Fact]
    public async Task Camva_StoresTheAngleAndLeavesTheCameraPlacementAutomatic()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(linspace(-3, 3, 12));
            surf(X, Y, sin(sqrt(X.^2 + Y.^2) + eps));
            view(35, 44);
            camva(4);
            assert(abs(camva - 4) < 1e-12, 'camva should read back what it was given');
            """);

        AxesModel axes = JG.Gca();
        Assert.True(axes.HasAutomaticCameraPlacement);
        Assert.True(axes.CameraZoomFactor > 1.5, "four degrees is a zoom in, not a no-op");
    }
}
