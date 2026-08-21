using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M45.C: the camera and aspect verbs. The projection is orthographic with an automatic fit, so the
/// whole camera is an azimuth, an elevation, and how much of the data the limits admit — every verb
/// here maps onto one of those three, and the ones that cannot are errors rather than silent no-ops.
/// </summary>
[Collection("JG facade")]
public class JgsCameraTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsCameraTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task Succeeds(string code)
    {
        ScriptRunResult result = await Run(code);
        Assert.True(result.Success, result.Message);
    }

    // --- view -----------------------------------------------------------------------------------

    [Fact]
    public async Task View_ReadsAndSetsTheCameraAngles()
    {
        await Succeeds("""
            surf([[1, 2], [3, 4]])
            view(45, 20)
            print(view)
            """);

        Assert.Equal(45, JG.Gca().Azimuth);
        Assert.Equal(20, JG.Gca().Elevation);
        Assert.Contains("[45, 20]", _output.NormalText);
    }

    [Fact]
    public async Task View_TakesAVectorAndTheTwoShorthands()
    {
        await Succeeds("surf([[1, 2], [3, 4]])\nview([10, 15])");
        Assert.Equal(10, JG.Gca().Azimuth);
        Assert.Equal(15, JG.Gca().Elevation);

        await Succeeds("view(2)");
        Assert.Equal(0, JG.Gca().Azimuth);
        Assert.Equal(90, JG.Gca().Elevation);

        await Succeeds("view(3)");
        Assert.Equal(-37.5, JG.Gca().Azimuth);
        Assert.Equal(30, JG.Gca().Elevation);
    }

    [Fact]
    public async Task View_RejectsOtherShorthands()
    {
        ScriptRunResult result = await Run("surf([[1, 2], [3, 4]])\nview(4)");

        Assert.False(result.Success);
        Assert.Contains("view(2) and view(3)", result.Message);
    }

    // --- campos, camtarget, camup ---------------------------------------------------------------

    /// <summary>
    /// The position is read as a direction <em>from the centre of the data box</em>, not from the
    /// origin, because that is the point the camera looks at. A unit box centred on (0.5, 0.5, 0.5)
    /// with the camera at (1.5, -0.5, 1.5) is one unit out along each of +x, -y and +z: 45 degrees
    /// round and 35.26 up.
    /// </summary>
    [Fact]
    public async Task Campos_RoundTripsThroughTheViewAngles()
    {
        await Succeeds("""
            surf([0, 1], [0, 1], [[0, 0], [1, 1]])
            campos([1.5, -0.5, 1.5])
            """);

        Assert.Equal(45, JG.Gca().Azimuth, 3);
        Assert.Equal(35.264, JG.Gca().Elevation, 2);
    }

    [Fact]
    public async Task Campos_RejectsTheCentre()
    {
        ScriptRunResult result = await Run("""
            surf([0, 1], [0, 1], [[0, 0], [1, 1]])
            campos(camtarget)
            """);

        Assert.False(result.Success);
        Assert.Contains("the point it is looking at", result.Message);
    }

    /// <summary>
    /// <c>camtarget</c> and <c>camup</c> answer where the camera looks and which way is up, and since
    /// M74 they are places the script can move rather than facts about the projection.
    /// </summary>
    [Fact]
    public async Task CamtargetAndCamup_AreReadableAndSettable()
    {
        await Succeeds("""
            surf([0, 2], [0, 2], [[0, 0], [4, 4]])
            print(camtarget)
            print(camup)
            """);

        Assert.Contains("[1, 1, 2]", _output.NormalText);
        Assert.Contains("[0, 0, 1]", _output.NormalText);

        await Succeeds("""
            surf([0, 2], [0, 2], [[0, 0], [0, 0]])
            camup([0, 1, 0])
            camtarget([1, 1, 0])
            """);

        Assert.Equal(new Vector3D(0, 1, 0), JG.Gca().CameraUpVector);
        Assert.Equal(new Vector3D(1, 1, 0), JG.Gca().CameraTarget);
    }

    /// <summary>Naming angles hands the camera back to them, which is what MATLAB's view does.</summary>
    [Fact]
    public async Task View_ReleasesACameraPlacedByHand()
    {
        await Succeeds("""
            surf([[1, 2], [3, 4]])
            camup([0, 1, 0])
            camtarget([1, 1, 0])
            camva(20)
            view(45, 20)
            """);

        AxesModel axes = JG.Gca();
        Assert.Null(axes.CameraUpVector);
        Assert.Null(axes.CameraTarget);
        Assert.Null(axes.CameraViewAngle);
        Assert.Equal(45, axes.Azimuth, 6);
    }

    // --- camorbit, camzoom, camva ---------------------------------------------------------------

    [Fact]
    public async Task Camorbit_TurnsTheCameraByAnIncrement()
    {
        await Succeeds("""
            surf([[1, 2], [3, 4]])
            view(10, 20)
            camorbit(5, -5)
            """);

        Assert.Equal(15, JG.Gca().Azimuth);
        Assert.Equal(15, JG.Gca().Elevation);
    }

    /// <summary>Zooming in halves the span the limits admit, about their own centre.</summary>
    [Fact]
    public async Task Camzoom_NarrowsTheViewAngleAndLeavesTheLimitsAlone()
    {
        await Succeeds("""
            surf([0, 10], [0, 10], [[0, 0], [0, 0]])
            xlim(0, 10)
            camzoom(2)
            """);

        AxesModel axes = JG.Gca();

        // Zooming is the camera's business, so the data the axes shows is untouched: the limits are
        // still the ten the script asked for, and it is the cone that halved.
        AxisModel x = axes.XAxes[0];
        Assert.Equal(0, x.Range.Min, 6);
        Assert.Equal(10, x.Range.Max, 6);
        Assert.Equal(AxesModel.DefaultCameraViewAngle / 2, axes.EffectiveCameraViewAngle(), 6);
    }

    /// <summary>
    /// <c>camva</c> is the cone the camera sees through: it starts at MATLAB's own default, and a
    /// chosen angle is stored as the angle it is rather than converted into a zoom on the limits.
    /// </summary>
    [Fact]
    public async Task Camva_ReadsAndSetsTheViewAngle()
    {
        await Succeeds("""
            surf([0, 10], [0, 10], [[0, 0], [0, 0]])
            xlim(0, 10)
            print(round(camva * 10000) / 10000)
            camva(3.3043)
            print(round(camva * 10000) / 10000)
            """);

        Assert.Contains("6.6086", _output.NormalText);
        Assert.Contains("3.3043", _output.NormalText);

        AxesModel axes = JG.Gca();
        Assert.Equal(3.3043, axes.CameraViewAngle!.Value, 6);

        // The limits are the camera's business to look at, not to change.
        AxisModel x = axes.XAxes[0];
        Assert.Equal(10, x.Range.Max - x.Range.Min, 6);
    }

    [Fact]
    public async Task Camva_RejectsAnImpossibleAngle()
    {
        ScriptRunResult result = await Run("surf([[1, 2], [3, 4]])\ncamva(0)");

        Assert.False(result.Success);
        Assert.Contains("between 0 and 180", result.Message);
    }

    // --- pbaspect and daspect -------------------------------------------------------------------

    [Fact]
    public async Task Pbaspect_SetsAndReportsTheBoxSides()
    {
        await Succeeds("""
            surf([[1, 2], [3, 4]])
            pbaspect([2, 1, 0.5])
            print(pbaspect)
            """);

        Assert.Equal(new Vector3D(2, 1, 0.5), JG.Gca().PlotBoxAspect);
        Assert.Contains("[2, 1, 0.5]", _output.NormalText);

        await Succeeds("pbaspect('auto')");
        Assert.Equal(new Vector3D(1, 1, 1), JG.Gca().PlotBoxAspect);
    }

    /// <summary>
    /// <c>daspect([1 1 1])</c> is the 3D reading of "axis equal". Since M73 the ratio is stored on
    /// the axes and the renderer shapes the box from the spans every frame, so the equal-units
    /// promise survives a later limit change instead of freezing the box it happened to make.
    /// </summary>
    [Fact]
    public async Task Daspect_StoresTheRatioAndReadsItBack()
    {
        await Succeeds("""
            surf([0, 10], [0, 5], [[0, 0], [0, 1]])
            xlim(0, 10)
            ylim(0, 5)
            zlim(0, 1)
            daspect([1, 1, 1])
            disp(daspect())
            """);

        Assert.Equal(new Vector3D(1, 1, 1), JG.Gca().DataAspectRatio);

        // The box itself is untouched — pbaspect still answers its own cube — because the two
        // aspects clear each other and the last writer here was daspect.
        Assert.Equal(new Vector3D(1, 1, 1), JG.Gca().PlotBoxAspect);
    }

    [Fact]
    public async Task Daspect_RejectsANonPositiveRatio()
    {
        ScriptRunResult result = await Run("surf([[1, 2], [3, 4]])\ndaspect([1, 0, 1])");

        Assert.False(result.Success);
        Assert.Contains("finite and positive", result.Message);
    }

    // --- the projection honours the box ---------------------------------------------------------

    /// <summary>
    /// The aspect has to reach the projection, not just the model. A box half as deep puts the top of
    /// the data box half as far above its bottom on screen.
    /// </summary>
    [Fact]
    public void TheProjectionStretchesTheBox()
    {
        var area = new Rect2D(0, 0, 400, 400);
        var cube = new Projection3D(
            new DataRange(0, 1), new DataRange(0, 1), new DataRange(0, 1), 0, 0, area);
        var flat = new Projection3D(
            new DataRange(0, 1), new DataRange(0, 1), new DataRange(0, 1), 0, 0, area,
            new Vector3D(1, 1, 0.5));

        double cubeHeight = cube.ProjectPoint(0, 0, 0).Y - cube.ProjectPoint(0, 0, 1).Y;
        double flatHeight = flat.ProjectPoint(0, 0, 0).Y - flat.ProjectPoint(0, 0, 1).Y;

        Assert.True(cubeHeight > 0);
        Assert.Equal(cubeHeight / 2, flatHeight, 6);
    }

    /// <summary>Only the ratios matter: scaling every side changes nothing on screen.</summary>
    [Fact]
    public void TheBoxAspectIsScaleInvariant()
    {
        var area = new Rect2D(0, 0, 400, 400);
        var small = new Projection3D(
            new DataRange(0, 1), new DataRange(0, 1), new DataRange(0, 1), 30, 20, area,
            new Vector3D(2, 1, 1));
        var large = new Projection3D(
            new DataRange(0, 1), new DataRange(0, 1), new DataRange(0, 1), 30, 20, area,
            new Vector3D(200, 100, 100));

        Assert.Equal(small.ProjectPoint(1, 1, 1).X, large.ProjectPoint(1, 1, 1).X, 9);
        Assert.Equal(small.ProjectPoint(1, 1, 1).Y, large.ProjectPoint(1, 1, 1).Y, 9);
    }

    [Fact]
    public void PlotBoxAspect_RoundTrips()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Is3D = true;
        axes.PlotBoxAspect = new Vector3D(3, 2, 1);

        FigureModel loaded = JGraph.Serialization.GraphFormat.Deserialize(
            JGraph.Serialization.GraphFormat.Serialize(figure));

        Assert.Equal(new Vector3D(3, 2, 1), loaded.Axes[0].PlotBoxAspect);
    }
}
