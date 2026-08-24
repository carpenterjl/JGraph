using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M85: the forms M72 left on the table — <c>streamslice</c> through a volume, and <c>slice</c> along
/// a surface a script drew itself.
/// <para>
/// The pair belongs in one file because they are the same shape of gap. Both verbs were implemented,
/// both were counted as implemented, and both answered the wrong thing to a documented form: the
/// slicer errored on its own arity, and <c>slice</c> read three matrices as seventy-two scalar planes
/// and drew every one of them. A coverage table counts served names; only a test counts a name served
/// rightly, which is the standing argument these cases exist to make.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM85VolumeFormTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM85VolumeFormTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    /// <summary>
    /// A one-element number arrives either packed or bare depending on how it was made, so both are
    /// unwrapped here rather than at every call.
    /// </summary>
    private static double Number(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue switch
        {
            double[] { Length: 1 } packed => packed[0],
            double one => one,
            { } other => throw new InvalidOperationException($"{name} is a {other.GetType()}."),
            null => throw new InvalidOperationException($"{name} carries no value."),
        };

    /// <summary>A field whose flow runs along x and rises with z, over a six-cube.</summary>
    private const string Volume = """
        [X, Y, Z] = meshgrid(1:6, 1:6, 1:6);
        U = ones(6, 6, 6); V = zeros(6, 6, 6); W = ones(6, 6, 6) * 0.2;
        """;

    private const string Plane = """
        [Xp, Yp] = meshgrid(1:10, 1:10);
        """;

    private static IReadOnlyList<Line3DPlot> Streamlines() =>
        [.. JG.Gca().Plots.OfType<Line3DPlot>()];

    // --- The three spatial forms -------------------------------------------------------------------

    /// <summary>
    /// Both spellings of the volume form draw, where every one of them errored on arity before —
    /// nine arguments handed to a verb that accepted eight.
    /// </summary>
    [Fact]
    public async Task TheVolumeFormDrawsWithItsGridAndWithout()
    {
        ScriptRunResult gridded = await RunMatlab(
            Volume + "\nh = streamslice(X, Y, Z, U, V, W, 3, [], []); n = numel(h);");
        Succeeded(gridded);
        Assert.True(Number(gridded, "n") >= 2);

        ScriptRunResult bare = await RunMatlab(
            Volume + "\nh = streamslice(U, V, W, [], 3, []); n = numel(h);");
        Succeeded(bare);
        Assert.True(Number(bare, "n") >= 2);
    }

    /// <summary>
    /// The trailing triple names planes, not starting points: a line drawn for the x-plane at 3 lies
    /// in that plane at every one of its points. This is the whole difference between this verb and
    /// <c>streamline</c>, and the reason the tracing happens inside the plane rather than in space.
    /// </summary>
    [Fact]
    public async Task ALineOfAnXSliceStaysInItsPlane()
    {
        Succeeded(await RunMatlab(
            Volume + "\nstreamslice(X, Y, Z, U, V, W, 3, [], [], 'noarrows');"));

        IReadOnlyList<Line3DPlot> drawn = Streamlines();
        Assert.NotEmpty(drawn);
        Assert.All(drawn, line => Assert.All(line.X, x => Assert.Equal(3, x, 6)));
    }

    /// <summary>Three plane lists at once draw three slices' worth, each in its own plane.</summary>
    [Fact]
    public async Task EveryPlaneListIsCutAndEachKeepsItsOwnDirection()
    {
        Succeeded(await RunMatlab(
            Volume + "\nstreamslice(X, Y, Z, U, V, W, 3, 3, 3, 'noarrows');"));

        IReadOnlyList<Line3DPlot> drawn = Streamlines();
        Assert.All(drawn, line => Assert.True(
            line.X.All(x => Math.Abs(x - 3) < 1e-6)
            || line.Y.All(y => Math.Abs(y - 3) < 1e-6)
            || line.Z.All(z => Math.Abs(z - 3) < 1e-6),
            "every line has to lie in one of the three planes it was asked for."));
    }

    /// <summary>A denser lattice is more lines; the density reaches the volume form as well.</summary>
    [Fact]
    public async Task TheDensityScalesTheLatticeInSpaceToo()
    {
        ScriptRunResult once = await RunMatlab(
            Volume + "\nn = numel(streamslice(X, Y, Z, U, V, W, 3, [], [], 'noarrows'));");
        ScriptRunResult twice = await RunMatlab(
            Volume + "\nn = numel(streamslice(X, Y, Z, U, V, W, 3, [], [], 2, 'noarrows'));");
        Succeeded(once);
        Succeeded(twice);
        Assert.True(Number(twice, "n") > Number(once, "n"));
    }

    /// <summary>An empty plane list everywhere is nothing to cut, and says so rather than drawing nothing.</summary>
    [Fact]
    public async Task ASliceOfNoPlanesAtAllIsRefused()
    {
        ScriptRunResult result = await RunMatlab(
            Volume + "\nstreamslice(X, Y, Z, U, V, W, [], [], []);");
        Assert.False(result.Success);
        Assert.Contains("nothing to slice", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Arrows ------------------------------------------------------------------------------------

    /// <summary>
    /// Arrows are drawn unless a script says otherwise, which is MATLAB's default and — for this one
    /// change — the JGS picture as well, by the user's authorization recorded in ADR 0085.
    /// </summary>
    [Fact]
    public async Task ArrowsAreDrawnByDefaultAndNoarrowsTakesThemAway()
    {
        ScriptRunResult with = await RunMatlab(Plane + "\nn = numel(streamslice(Xp, Yp, -Yp, Xp));");
        ScriptRunResult without = await RunMatlab(
            Plane + "\nn = numel(streamslice(Xp, Yp, -Yp, Xp, 'noarrows'));");
        Succeeded(with);
        Succeeded(without);

        // One head per line, so asking for them exactly doubles what is drawn.
        Assert.Equal(2 * Number(without, "n"), Number(with, "n"));

        ScriptRunResult asked = await RunMatlab(
            Plane + "\nn = numel(streamslice(Xp, Yp, -Yp, Xp, 'arrows'));");
        Succeeded(asked);
        Assert.Equal(Number(with, "n"), Number(asked, "n"));
    }

    /// <summary>
    /// A slice is one drawing rather than dozens of series, and the arrowheads are part of it — an
    /// arrow in the next colour of the order would say the line it sits on belongs to something else.
    /// </summary>
    [Fact]
    public async Task TheArrowheadsWearTheSliceColour()
    {
        Succeeded(await RunMatlab(Plane + "\nstreamslice(Xp, Yp, -Yp, Xp);"));

        IReadOnlyList<Line3DPlot> drawn = Streamlines();
        Assert.True(drawn.Count >= 4, $"only {drawn.Count} lines were drawn.");
        Assert.All(drawn, line => Assert.Equal(drawn[0].Color, line.Color));
    }

    /// <summary>An arrowhead lies flat against its own slice, whichever way the slice faces.</summary>
    [Fact]
    public async Task AnArrowheadLiesInThePlaneItMarks()
    {
        Succeeded(await RunMatlab(Volume + "\nstreamslice(X, Y, Z, U, V, W, [], 4, []);"));

        IReadOnlyList<Line3DPlot> drawn = Streamlines();
        Assert.NotEmpty(drawn);

        // The heads are the three-point polylines; every point of one is still on the y-plane.
        IReadOnlyList<Line3DPlot> heads = [.. drawn.Where(line => line.X.Count == 3)];
        Assert.NotEmpty(heads);
        Assert.All(heads, head => Assert.All(head.Y, y => Assert.Equal(4, y, 6)));
    }

    // --- One drawing, not a series each ------------------------------------------------------------

    /// <summary>
    /// Every line a slice traced is on the axes, not just the last one.
    /// <para>
    /// This is the defect that predates the whole family. Each line went through the facade, and a
    /// verb drawing with <c>hold</c> off clears the axes first — so a slice cleared itself once per
    /// line and kept the last. Every handle came back live, so <c>numel(h)</c> was right and the
    /// picture was not, which is exactly the kind of wrongness a count cannot see.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EveryLineOfASliceIsOnTheAxesAndNotOnlyTheLast()
    {
        ScriptRunResult result = await RunMatlab(
            Plane + "\nh = streamslice(Xp, Yp, -Yp, Xp); n = numel(h); "
            + "drawn = numel(get(gca, 'Children'));");
        Succeeded(result);
        Assert.True(Number(result, "n") >= 4);
        Assert.Equal(Number(result, "n"), Number(result, "drawn"));
    }

    /// <summary>The same for every other verb of the family that makes one picture out of many pieces.</summary>
    [Theory]
    [InlineData("streamline(Xp, Yp, -Yp, Xp, [2 3 4], [2 3 4])")]
    [InlineData("contourslice(X, Y, Z, X + Y + Z, [], [], [2 4])")]
    [InlineData("streamribbon(X, Y, Z, U, V, W, [1 1], [2 3], [1 1])")]
    [InlineData("streamtube(X, Y, Z, U, V, W, [1 1], [2 3], [1 1])")]
    public async Task EveryPieceOfEveryBundleIsOnTheAxes(string call)
    {
        ScriptRunResult result = await RunMatlab(
            Volume + "\n" + Plane + $"\nh = {call}; n = numel(h); "
            + "drawn = numel(get(gca, 'Children'));");
        Succeeded(result);
        Assert.True(Number(result, "n") >= 2, $"{call} drew fewer than two pieces.");
        Assert.Equal(Number(result, "n"), Number(result, "drawn"));
    }

    /// <summary>
    /// And <c>hold</c> still decides what happens to whatever was there first — which is the half of
    /// this that could have been broken by fixing the other half, since the fix is precisely about
    /// not asking the facade twice.
    /// </summary>
    [Fact]
    public async Task ASliceReplacesWhatCameBeforeItUnlessTheAxesIsHeld()
    {
        ScriptRunResult held = await RunMatlab(
            Plane + "\ncontour(peaks(20)); hold on; "
            + "n = numel(streamslice(Xp, Yp, -Yp, Xp, 'noarrows')); "
            + "drawn = numel(get(gca, 'Children'));");
        Succeeded(held);
        Assert.Equal(Number(held, "n") + 1, Number(held, "drawn"));

        ScriptRunResult replaced = await RunMatlab(
            Plane + "\ncontour(peaks(20)); "
            + "n = numel(streamslice(Xp, Yp, -Yp, Xp, 'noarrows')); "
            + "drawn = numel(get(gca, 'Children'));");
        Succeeded(replaced);
        Assert.Equal(Number(replaced, "n"), Number(replaced, "drawn"));
    }

    // --- The vertex outputs ------------------------------------------------------------------------

    /// <summary>
    /// Asked for its vertices the verb hands them over and draws nothing, which is the arrangement
    /// <c>stream2</c> and <c>stream3</c> already have beside <c>streamline</c>. The width of a vertex
    /// table says which world it came from: two columns over a plane, three through a volume.
    /// </summary>
    [Fact]
    public async Task TheVertexFormAnswersTwoCellsAndDrawsNothing()
    {
        ScriptRunResult flat = await RunMatlab(
            Plane + "\n[v, a] = streamslice(Xp, Yp, -Yp, Xp); "
            + "nv = numel(v); na = numel(a); w = size(v{1}, 2); drawn = numel(get(gca, 'Children'));");
        Succeeded(flat);
        Assert.True(Number(flat, "nv") >= 2);
        Assert.Equal(Number(flat, "nv"), Number(flat, "na"));
        Assert.Equal(2, Number(flat, "w"));
        Assert.Equal(0, Number(flat, "drawn"));

        ScriptRunResult space = await RunMatlab(
            Volume + "\n[v, a] = streamslice(X, Y, Z, U, V, W, 3, [], []); w = size(v{1}, 2);");
        Succeeded(space);
        Assert.Equal(3, Number(space, "w"));
    }

    // --- The words ---------------------------------------------------------------------------------

    /// <summary>
    /// The interpolation word is checked and then does nothing, which is the stance <c>slice</c>
    /// takes beside it: a script asking for 'cubic' should learn it did not get it.
    /// </summary>
    [Fact]
    public async Task AnUnknownTrailingWordIsRefusedAndTheKnownOnesAreNot()
    {
        ScriptRunResult refused = await RunMatlab(
            Plane + "\nstreamslice(Xp, Yp, -Yp, Xp, 'sideways');");
        Assert.False(refused.Success);
        Assert.Contains("'sideways'", refused.Message, StringComparison.Ordinal);

        Succeeded(await RunMatlab(
            Plane + "\nstreamslice(Xp, Yp, -Yp, Xp, 2, 'cubic', 'noarrows');"));
    }

    /// <summary>A leading axes handle is peeled at both of the verb's doors, not only the one.</summary>
    [Fact]
    public async Task TheAxesFormWorksForTheVolumeAndForTheVertices()
    {
        Succeeded(await RunMatlab(
            Volume + "\nax = axes; h = streamslice(ax, X, Y, Z, U, V, W, 3, [], []);"));
        Succeeded(await RunMatlab(
            Volume + "\nax = axes; [v, a] = streamslice(ax, X, Y, Z, U, V, W, 3, [], []);"));
    }

    // --- slice over a surface ----------------------------------------------------------------------

    /// <summary>
    /// Three same-sized matrices are one surface, not seventy-two planes. Before M85 this drew 108
    /// patches — an answer, and a wrong one, from a form the probe records as accepted.
    /// </summary>
    [Fact]
    public async Task ASlicingSurfaceIsOnePatchRatherThanAPileOfPlanes()
    {
        ScriptRunResult result = await RunMatlab(
            Volume + """

            Vol = X + Y + Z;
            [XI, YI] = meshgrid(1:6, 1:6);
            ZI = XI * 0 + 3;
            h = slice(X, Y, Z, Vol, XI, YI, ZI);
            n = numel(h);
            """);
        Succeeded(result);
        Assert.Equal(1, Number(result, "n"));
        Assert.Single(JG.Gca().Plots.OfType<PatchPlot>());
    }

    /// <summary>
    /// A surface that happens to lie in a plane draws what the plane form draws — which is the check
    /// that the two readings are one reading, rather than two that happen to both produce a picture.
    /// </summary>
    [Fact]
    public async Task AFlatSurfaceAgreesWithThePlaneItLiesIn()
    {
        Succeeded(await RunMatlab(
            Volume + """

            Vol = X + Y + Z;
            [XI, YI] = meshgrid(1:6, 1:6);
            slice(X, Y, Z, Vol, XI, YI, XI * 0 + 3);
            """));
        IReadOnlyList<double> overSurface =
            Assert.Single(JG.Gca().Plots.OfType<PatchPlot>()).ColorData!;

        JG.Reset();
        Succeeded(await RunMatlab(Volume + "\nVol = X + Y + Z;\nslice(X, Y, Z, Vol, [], [], 3);"));
        IReadOnlyList<double> overPlane =
            Assert.Single(JG.Gca().Plots.OfType<PatchPlot>()).ColorData!;

        Assert.Equal(overPlane.Count, overSurface.Count);
        Assert.All(
            overPlane.Zip(overSurface),
            both => Assert.Equal(both.First, both.Second, 9));
    }

    /// <summary>
    /// A ragged surface is refused by name. The three matrices are three coordinates of one lattice,
    /// so mismatched sizes are not a surface that needs interpreting — they are a mistake.
    /// </summary>
    [Fact]
    public async Task ARaggedSurfaceIsRefused()
    {
        ScriptRunResult result = await RunMatlab(
            Volume + """

            Vol = X + Y + Z;
            [XI, YI] = meshgrid(1:6, 1:6);
            slice(X, Y, Z, Vol, XI, YI, XI(1:3, 1:3));
            """);
        Assert.False(result.Success);
        Assert.Contains("one size", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The plane forms are untouched, including the empty lists and the refusals M72 wrote. The new
    /// reading is told apart by shape, and a shape no plane list ever has.
    /// </summary>
    [Fact]
    public async Task ThePlaneFormsAreUnchanged()
    {
        ScriptRunResult result = await RunMatlab(
            Volume + """

            Vol = X + Y + Z;
            a = numel(slice(Vol, 4, [], []));
            b = numel(slice(X, Y, Z, Vol, [-1 1], 0, [], 'linear'));
            """);
        Succeeded(result);
        Assert.Equal(1, Number(result, "a"));
        Assert.Equal(3, Number(result, "b"));

        ScriptRunResult refused = await RunMatlab(
            Volume + "\nVol = X + Y + Z;\nslice(X, Y, Z, Vol, 0, 0, 0, 'sideways');");
        Assert.False(refused.Success);
        Assert.Contains("interpolation", refused.Message, StringComparison.OrdinalIgnoreCase);
    }
}
