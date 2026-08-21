using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M74: the second axes wave — the camera the picture is drawn from, the alpha mapping that turns
/// data into transparency, and clipping, face ordering and the pointer's own point. Every behavior
/// here was probed at the CLI before it was written down, and the pixel-level proofs live in
/// stess_46.m; these tests pin the property semantics.
/// </summary>
[Collection("JG facade")]
public class MatlabM74AxesPropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM74AxesPropertyTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    // --- The camera -----------------------------------------------------------------------------

    [Fact]
    public async Task AnUntouchedAxesAnswersTheCameraItsAnglesImply()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            posMode = get(gca, 'CameraPositionMode');
            targetMode = get(gca, 'CameraTargetMode');
            upMode = get(gca, 'CameraUpVectorMode');
            angleMode = get(gca, 'CameraViewAngleMode');
            angle = get(gca, 'CameraViewAngle');
            """);

        Succeeded(result);

        // Every slot is empty, which is what keeps an untouched 3D figure drawn as it always was.
        Assert.Equal("auto", Text(result, "posMode"));
        Assert.Equal("auto", Text(result, "targetMode"));
        Assert.Equal("auto", Text(result, "upMode"));
        Assert.Equal("auto", Text(result, "angleMode"));
        Assert.Equal(AxesModel.DefaultCameraViewAngle, Number(result, "angle"), 4);
    }

    [Fact]
    public async Task TheAutomaticTargetIsTheMiddleOfTheBox()
    {
        ScriptRunResult result = await RunMatlab("""
            surf([0 2], [0 2], [0 0; 4 4]);
            t = camtarget;
            x = t(1);
            y = t(2);
            """);

        Succeeded(result);
        Assert.Equal(1, Number(result, "x"), 6);
        Assert.Equal(1, Number(result, "y"), 6);
    }

    [Fact]
    public async Task PlacingTheCameraTurnsItsModeManualAndNamingAnglesReleasesIt()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            set(gca, 'CameraTarget', [1 1 1]);
            set(gca, 'CameraUpVector', [0 1 0]);
            set(gca, 'CameraViewAngle', 20);
            placed = get(gca, 'CameraTargetMode');
            view(45, 20);
            released = get(gca, 'CameraTargetMode');
            up = get(gca, 'CameraUpVectorMode');
            angle = get(gca, 'CameraViewAngleMode');
            """);

        Succeeded(result);
        Assert.Equal("manual", Text(result, "placed"));

        // MATLAB's view hands the whole camera back to the angles, not merely the position.
        Assert.Equal("auto", Text(result, "released"));
        Assert.Equal("auto", Text(result, "up"));
        Assert.Equal("auto", Text(result, "angle"));
    }

    [Fact]
    public async Task FreezingACameraModeKeepsWhatIsShowing()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            before = get(gca, 'CameraViewAngle');
            set(gca, 'CameraViewAngleMode', 'manual');
            after = get(gca, 'CameraViewAngle');
            mode = get(gca, 'CameraViewAngleMode');
            """);

        Succeeded(result);
        Assert.Equal(Number(result, "before"), Number(result, "after"), 9);
        Assert.Equal("manual", Text(result, "mode"));
    }

    [Fact]
    public async Task CamposRoundTripsAndKeepsTheAnglesInStep()
    {
        ScriptRunResult result = await RunMatlab("""
            surf([0 1], [0 1], [0 0; 1 1]);
            campos([1.5 -0.5 1.5]);
            p = campos;
            x = p(1);
            v = view;
            az = v(1);
            mode = get(gca, 'CameraPositionMode');
            """);

        Succeeded(result);

        // The position is kept exactly, and the angles still describe where it looks from.
        Assert.Equal(1.5, Number(result, "x"), 9);
        Assert.Equal(45, Number(result, "az"), 3);
        Assert.Equal("manual", Text(result, "mode"));
    }

    [Fact]
    public async Task CamzoomNarrowsTheAngleWithoutMovingTheLimits()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            xlim([0 8]);
            camzoom(2);
            angle = camva;
            l = xlim;
            span = l(2) - l(1);
            """);

        Succeeded(result);
        Assert.Equal(AxesModel.DefaultCameraViewAngle / 2, Number(result, "angle"), 6);
        Assert.Equal(8, Number(result, "span"), 6);
    }

    [Fact]
    public async Task ProjectionAndCamprojAgree()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            camproj('perspective');
            byProperty = get(gca, 'Projection');
            set(gca, 'Projection', 'orthographic');
            byVerb = camproj;
            """);

        Succeeded(result);
        Assert.Equal("perspective", Text(result, "byProperty"));
        Assert.Equal("orthographic", Text(result, "byVerb"));
    }

    [Fact]
    public async Task AnImpossibleViewAngleIsRefused()
    {
        ScriptRunResult result = await RunMatlab("surf(peaks(8));\nset(gca, 'CameraViewAngle', 0);");

        Assert.False(result.Success);
        Assert.Contains("between 0 and 180", result.Message, StringComparison.Ordinal);
    }

    // --- Face order, clipping, and the pointer --------------------------------------------------

    [Fact]
    public async Task SortMethodRoundTripsAndRefusesAnythingElse()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            byDefault = get(gca, 'SortMethod');
            set(gca, 'SortMethod', 'childorder');
            chosen = get(gca, 'SortMethod');
            """);

        Succeeded(result);
        Assert.Equal("depth", Text(result, "byDefault"));
        Assert.Equal("childorder", Text(result, "chosen"));

        ScriptRunResult refused = await RunMatlab("set(gca, 'SortMethod', 'sideways');");
        Assert.False(refused.Success);
        Assert.Contains("'depth' or 'childorder'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClippingRoundTrips()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:10);
            byDefault = get(gca, 'Clipping');
            set(gca, 'Clipping', 'off');
            chosen = get(gca, 'Clipping');
            """);

        Succeeded(result);
        Assert.Equal("on", Text(result, "byDefault"));
        Assert.Equal("off", Text(result, "chosen"));
    }

    [Fact]
    public async Task ClippingStyleAnswersRectangleAndRefusesTheBox()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:10);
            style = get(gca, 'ClippingStyle');
            set(gca, 'ClippingStyle', 'rectangle');
            """);

        Succeeded(result);
        Assert.Equal("rectangle", Text(result, "style"));

        // Refusing is the honest answer: there is no six-plane clip to do it with.
        ScriptRunResult refused = await RunMatlab("plot(1:10);\nset(gca, 'ClippingStyle', '3dbox');");
        Assert.False(refused.Success);
        Assert.Contains("not implemented", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentPointIsReadOnlyAndStartsAtZero()
    {
        ScriptRunResult result = await RunMatlab("""
            plot(1:10);
            p = get(gca, 'CurrentPoint');
            rows = size(p, 1);
            cols = size(p, 2);
            first = p(1, 1);
            """);

        Succeeded(result);

        // A figure nobody has pointed at reports the zeros MATLAB reports.
        Assert.Equal(2, Number(result, "rows"), 6);
        Assert.Equal(3, Number(result, "cols"), 6);
        Assert.Equal(0, Number(result, "first"), 9);

        ScriptRunResult refused = await RunMatlab("plot(1:10);\nset(gca, 'CurrentPoint', [0 0 0; 1 1 1]);");
        Assert.False(refused.Success);
    }

    [Fact]
    public void AHitRecordsWhereThePointerCrossedTheAxes()
    {
        var axes = new AxesModel();
        axes.SetCurrentPoint(new Vector3D(1, 2, 3), new Vector3D(4, 5, 6));

        (Vector3D front, Vector3D back) = axes.CurrentPoint;
        Assert.Equal(1, front.X, 9);
        Assert.Equal(6, back.Z, 9);
    }

    // --- Alpha mapping --------------------------------------------------------------------------

    [Fact]
    public async Task ALimFollowsTheAlphaDataUntilItIsPinned()
    {
        ScriptRunResult result = await RunMatlab("""
            Z = peaks(8);
            surf(Z);
            before = get(gca, 'ALimMode');
            alpha(abs(Z) / max(abs(Z(:))));
            auto = alim;
            autoHigh = auto(2);
            alim([0 2]);
            pinnedMode = get(gca, 'ALimMode');
            pinned = alim;
            pinnedHigh = pinned(2);
            alim('auto');
            releasedMode = get(gca, 'ALimMode');
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "before"));

        // Unpinned limits are the data's own extent, which is what spreads each plot over the map.
        Assert.Equal(1, Number(result, "autoHigh"), 6);
        Assert.Equal("manual", Text(result, "pinnedMode"));
        Assert.Equal(2, Number(result, "pinnedHigh"), 6);
        Assert.Equal("auto", Text(result, "releasedMode"));
    }

    [Fact]
    public async Task TheAlphamapReadsAsARampAndTakesAVector()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            m = alphamap;
            n = numel(m);
            low = m(1);
            high = m(end);
            alphamap('rampdown');
            d = alphamap;
            downFirst = d(1);
            set(gca, 'Alphamap', [0 0.5 1]);
            chosen = get(gca, 'Alphamap');
            chosenCount = numel(chosen);
            """);

        Succeeded(result);
        Assert.Equal(64, Number(result, "n"), 6);
        Assert.Equal(0, Number(result, "low"), 9);
        Assert.Equal(1, Number(result, "high"), 9);
        Assert.Equal(1, Number(result, "downFirst"), 9);
        Assert.Equal(3, Number(result, "chosenCount"), 6);
    }

    [Fact]
    public async Task AnAlphamapEntryOutsideTheUnitRangeIsRefused()
    {
        ScriptRunResult result = await RunMatlab("surf(peaks(8));\nset(gca, 'Alphamap', [0 2]);");

        Assert.False(result.Success);
        Assert.Contains("between 0 and 1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlphaScaleRoundTrips()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            byDefault = get(gca, 'AlphaScale');
            set(gca, 'AlphaScale', 'log');
            chosen = get(gca, 'AlphaScale');
            """);

        Succeeded(result);
        Assert.Equal("linear", Text(result, "byDefault"));
        Assert.Equal("log", Text(result, "chosen"));
    }

    [Fact]
    public async Task AlphaDataTurnsFaceAlphaFlatAndANumberTurnsItBack()
    {
        ScriptRunResult result = await RunMatlab("""
            Z = peaks(8);
            surf(Z);
            h = findobj(gca, 'Type', 'surface');
            set(h, 'AlphaData', abs(Z) / max(abs(Z(:))));
            flat = get(h, 'FaceAlpha');
            set(h, 'FaceAlpha', 0.5);
            number = get(h, 'FaceAlpha');
            """);

        Succeeded(result);

        // MATLAB spells the flat mode as the FaceAlpha property holding a word instead of a number.
        Assert.Equal("flat", Text(result, "flat"));
        Assert.Equal(0.5, Number(result, "number"), 9);
    }

    [Fact]
    public async Task FlatFaceAlphaWithoutAlphaDataIsRefused()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            h = findobj(gca, 'Type', 'surface');
            set(h, 'FaceAlpha', 'flat');
            """);

        Assert.False(result.Success);
        Assert.Contains("AlphaData", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlphaDataMustMatchTheGridItHangsOn()
    {
        ScriptRunResult result = await RunMatlab("""
            surf(peaks(8));
            h = findobj(gca, 'Type', 'surface');
            set(h, 'AlphaData', zeros(3, 3));
            """);

        Assert.False(result.Success);
        Assert.Contains("must match the surface", result.Message, StringComparison.Ordinal);
    }
}
