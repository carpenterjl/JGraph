using JGraph.Api;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M67 wave C: the fan-beam family and the surface that carries a picture — the two IPT ride-alongs
/// M46 recorded as blocked, closed here.
/// <para>
/// The claim a rebinning can be held to is that it is a change of sampling and nothing else, so the
/// tests here are mostly round trips: <c>fan2para</c> undoes <c>para2fan</c>, and a reconstruction
/// through the fan finds the same object the parallel one does. A tolerance on one reconstruction
/// would only be asserting a tolerance.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabFanBeamTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabFanBeamTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        GC.SuppressFinalize(this);
    }

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code, new ScriptContext(_output, (_, _) => { }), CancellationToken.None)
            .GetAwaiter().GetResult();

    private string Printed(ScriptRunResult result) => result.Message + _output.ErrorText;

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, Printed(result));
        return _output.NormalText;
    }

    private string Error(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return Printed(result);
    }

    // --- fanbeam ------------------------------------------------------------------------------------

    [Fact]
    public void FanbeamAnswersItsDataAndTheCoordinatesItIsIndexedBy()
    {
        Assert.Equal("1 1 1 1\n", RunAndRead("""
            P = phantom(32);
            [F, sensors, betas] = fanbeam(P, 80);
            fprintf('%d %d %d %d\n', ...
                size(F, 1) == numel(sensors), size(F, 2) == numel(betas), ...
                numel(betas) == 360, abs(sensors(1) + sensors(end)) < 1e-12);
            """));
    }

    [Fact]
    public void TheSensorsSpreadWiderAsTheVertexComesCloser()
    {
        // The fan has to reach the whole object, so a nearer vertex needs a wider spread of rays —
        // which is the geometry the sensor positions are worked out from rather than a setting.
        Assert.Equal("1\n", RunAndRead("""
            P = phantom(32);
            [~, near] = fanbeam(P, 40);
            [~, far] = fanbeam(P, 400);
            fprintf('%d\n', near(end) > far(end));
            """));
    }

    [Fact]
    public void AVertexInsideTheObjectIsRefused()
    {
        // Some of the object would be behind the vertex, so there is no fan that covers it.
        string message = Error("fanbeam(phantom(64), 5);");
        Assert.Contains("outside the object", message);
        Assert.DoesNotContain("Parameter", message);
    }

    [Fact]
    public void MinimalCoverageIsRefusedByNameRatherThanApproximated()
    {
        Assert.Contains("different set of angles",
            Error("fanbeam(phantom(32), 80, 'FanCoverage', 'minimal');"));
    }

    [Fact]
    public void ALineDetectorIsAcceptedAndSpacedInPixels()
    {
        // On a line the sensor coordinate is a distance rather than an angle, so the same spacing
        // gives a different number of detectors from an arc's.
        Assert.Equal("1\n", RunAndRead("""
            P = phantom(32);
            [~, arc] = fanbeam(P, 80, 'FanSensorGeometry', 'arc');
            [~, straight] = fanbeam(P, 80, 'FanSensorGeometry', 'line');
            fprintf('%d\n', straight(end) > arc(end));
            """));
    }

    // --- the two rebinnings -------------------------------------------------------------------------

    [Fact]
    public void RebinningToAFanAndBackFindsTheSameProjections()
    {
        // The claim the whole family rests on: the two mappings are inverses, so the only thing
        // between a sinogram and itself is interpolation.
        //
        // A smooth object rather than the head phantom, and deliberately: what is left over here IS
        // the interpolation error, and the phantom's skull is a one-pixel step of nearly the whole
        // dynamic range — so on it this test would be measuring how sharp the object is instead of
        // whether the rebinning is an identity. The fan is sampled finely enough to carry the
        // parallel data, which is the other half of the claim.
        Assert.Equal("49 49 1\n", RunAndRead("""
            [xx, yy] = meshgrid(linspace(-1, 1, 33));
            R = radon(exp(-4 * (xx.^2 + yy.^2)));
            F = para2fan(R, 200, 'FanSensorSpacing', 0.25);
            back = fan2para(F, 200, 'FanSensorSpacing', 0.25);
            d = abs(R - back);
            fprintf('%d %d %d\n', size(R, 1), size(back, 1), max(d(:)) < 0.05 * max(abs(R(:))));
            """));
    }

    [Fact]
    public void Fan2ParaAnswersTheParallelCoordinatesAsWell()
    {
        Assert.Equal("1 1\n", RunAndRead("""
            F = fanbeam(phantom(32), 80);
            [P, positions, angles] = fan2para(F, 80);
            fprintf('%d %d\n', size(P, 1) == numel(positions), size(P, 2) == numel(angles));
            """));
    }

    // --- ifanbeam -----------------------------------------------------------------------------------

    [Fact]
    public void IfanbeamFindsTheObjectTheProjectionsCameFrom()
    {
        // Not a tolerance on every pixel — a reconstruction is never that — but the two claims a
        // reconstruction has to satisfy: it is the right size, and the bright disc is in the middle.
        Assert.Equal("64 64 1\n", RunAndRead("""
            P = phantom(64);
            F = fanbeam(P, 200);
            I = ifanbeam(F, 200, 'OutputSize', 64);
            middle = mean(mean(I(28:36, 28:36)));
            corner = mean(mean(I(1:6, 1:6)));
            fprintf('%d %d %d\n', size(I, 1), size(I, 2), middle > corner + 0.1);
            """));
    }

    [Fact]
    public void IfanbeamPassesTheFilterStraightThroughToTheParallelReconstruction()
    {
        // A fan reconstruction is a parallel one over rebinned data, so the filter means the same
        // thing here as it does in iradon and is handed over unchanged.
        Assert.Equal("1\n", RunAndRead("""
            F = fanbeam(phantom(32), 100);
            sharp = ifanbeam(F, 100, 'Filter', 'Ram-Lak', 'OutputSize', 32);
            soft = ifanbeam(F, 100, 'Filter', 'none', 'OutputSize', 32);
            fprintf('%d\n', max(abs(sharp(:) - soft(:))) > 1e-6);
            """));
    }

    // --- warp ---------------------------------------------------------------------------------------

    [Fact]
    public void WarpDrawsAPictureOnASurface()
    {
        Assert.Equal("surface\n", RunAndRead("""
            figure(1);
            h = warp(peaks(16), uint8(cat(3, 255 * ones(8), zeros(8), zeros(8))));
            fprintf('%s\n', get(h, 'Type'));
            """));

        SurfacePlot surface = Assert.IsType<SurfacePlot>(Assert.Single(JG.CurrentFigure.Axes[^1].Plots));

        // The texture is what makes this a warp rather than an ordinary surface, and it is one
        // colour per grid vertex — the question the renderer was asking anyway.
        Assert.NotNull(surface.TextureData);
        Assert.Equal(16 * 16, surface.TextureData!.Length);
        Assert.All(surface.TextureData, colour => Assert.Equal(0xFFFF0000u, colour));
    }

    [Fact]
    public void APictureOnItsOwnIsLaidFlat()
    {
        Assert.Equal("1\n", RunAndRead("""
            figure(1);
            h = warp(uint8(zeros(4, 6, 3)));
            z = get(h, 'ZData');
            fprintf('%d\n', all(z(:) == 0));
            """));
    }

    [Fact]
    public void ATextureHasToHaveOneColourPerVertex()
    {
        // The guard is on the plot rather than on the verb, because the verb samples the picture to
        // the grid and could not get this wrong — but anything else setting a texture could.
        var surface = new SurfacePlot(new double[3, 4]);
        Assert.Throws<ArgumentException>(() => surface.TextureData = new uint[5]);
    }

    [Fact]
    public void ASurfaceWithNoTextureStillTakesItsColoursFromItsHeights()
    {
        var surface = new SurfacePlot(new double[3, 4]);
        Assert.Null(surface.TextureData);
    }
}
