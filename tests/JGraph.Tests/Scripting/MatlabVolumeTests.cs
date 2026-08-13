using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M59: volume visualization. What these pin is what each verb answers and what it drew with — the
/// arithmetic underneath has its own suite in <c>VolumeFieldTests</c>, so nothing here re-checks a
/// gradient or an interpolation for its own sake.
/// </summary>
[Collection("JG facade")]
public class MatlabVolumeTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabVolumeTests() => JG.Reset();

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

    /// <summary>The sphere field every test below reads, on a grid fine enough to measure against.</summary>
    private const string Field = """
        [X, Y, Z] = meshgrid(-2:0.2:2, -2:0.2:2, -2:0.2:2);
        V = X.^2 + Y.^2 + Z.^2;
        """;

    // --- the grids the family needs ---------------------------------------------------------------

    [Fact]
    public Task MeshgridBuildsAVolumeGrid() => RunAsserting("""
        [X, Y, Z] = meshgrid(1:4, 1:3, 1:2);
        assert(isequal(size(X), [3 4 2]));
        assert(isequal(size(Y), size(X)));
        assert(isequal(size(Z), size(X)));

        % x runs across the columns, y down the rows, z through the pages.
        assert(X(2, 3, 1) == 3);
        assert(Y(2, 3, 1) == 2);
        assert(Z(1, 1, 2) == 2);
        """);

    [Fact]
    public Task MeshgridKeepsItsTwoDimensionalAnswer() => RunAsserting("""
        [X, Y] = meshgrid(1:4, 1:3);
        assert(isequal(size(X), [3 4]));
        assert(X(1, 3) == 3);
        assert(Y(2, 1) == 2);
        """);

    /// <summary>
    /// The one difference between the two names: ndgrid runs the first vector down the first
    /// dimension, meshgrid swaps the first two.
    /// </summary>
    [Fact]
    public Task NdgridRunsItsFirstVectorDownTheFirstDimension() => RunAsserting("""
        [P, Q, R] = ndgrid(1:4, 1:3, 1:2);
        assert(isequal(size(P), [4 3 2]));
        assert(P(2, 3, 1) == 2);
        assert(Q(2, 3, 1) == 3);
        assert(R(1, 1, 2) == 2);
        """);

    [Fact]
    public Task GradientAnswersOnePerDimensionOfAVolume() => RunAsserting("""
        [X, Y, Z] = meshgrid(1:5, 1:5, 1:5);
        V = X + 10 * Y + 100 * Z;
        [gx, gy, gz] = gradient(V);
        assert(abs(gx(2, 2, 2) - 1) < 1e-12);
        assert(abs(gy(2, 2, 2) - 10) < 1e-12);
        assert(abs(gz(2, 2, 2) - 100) < 1e-12);

        % The faces use a one-sided difference and get the same answer on a straight slope.
        assert(abs(gz(1, 1, 1) - 100) < 1e-12);
        assert(abs(gx(5, 5, 5) - 1) < 1e-12);
        """);

    // --- the verbs that only answer with numbers --------------------------------------------------

    [Fact]
    public Task VolumeboundsAnswersTheBoxAndTheRangeOfTheReadings() => RunAsserting(Field + """
        b = volumebounds(X, Y, Z, V);
        assert(numel(b) == 8);
        assert(abs(b(1) + 2) < 1e-12 && abs(b(2) - 2) < 1e-12);
        assert(abs(b(5) + 2) < 1e-12 && abs(b(6) - 2) < 1e-12);
        assert(abs(b(7)) < 1e-12);
        assert(abs(b(8) - 12) < 1e-9);
        """);

    [Fact]
    public Task VolumeboundsWithNoGridCountsFromOne() => RunAsserting(Field + """
        b = volumebounds(V);
        assert(b(1) == 1 && b(2) == 21);
        """);

    [Fact]
    public Task SubvolumeKeepsOnlyWhatIsInsideItsBox() => RunAsserting(Field + """
        [nx, ny, nz, nv] = subvolume(X, Y, Z, V, [0 2 nan nan nan nan]);

        % NaN leaves a side alone, so only x was cut.
        assert(min(nx(:)) >= 0);
        assert(max(nx(:)) <= 2);
        assert(abs(min(ny(:)) + 2) < 1e-12);
        assert(size(nv, 1) == size(V, 1));
        assert(size(nv, 2) < size(V, 2));
        assert(size(nv, 3) == size(V, 3));
        """);

    [Fact]
    public Task ReducevolumeKeepsEveryNthReadingAndBothEnds() => RunAsserting(Field + """
        [rx, ry, rz, rv] = reducevolume(X, Y, Z, V, [2 2 2]);
        assert(numel(rv) < numel(V));

        % It still spans what it spanned, so a drawing of it fills the same box.
        assert(abs(min(rx(:)) - min(X(:))) < 1e-12);
        assert(abs(max(rx(:)) - max(X(:))) < 1e-12);
        assert(abs(max(rz(:)) - max(Z(:))) < 1e-12);
        """);

    [Fact]
    public Task Smooth3PullsASpikeDownWithoutMovingItsTotal() => RunAsserting("""
        V = zeros(7, 7, 7);
        V(4, 4, 4) = 27;
        s = smooth3(V);
        assert(isequal(size(s), size(V)));
        assert(s(4, 4, 4) < 27);
        assert(s(3, 4, 4) > 0);
        assert(abs(sum(s(:)) - 27) < 1e-9);
        """);

    [Fact]
    public Task Smooth3TakesItsFilterAndItsBlockSize() => RunAsserting("""
        V = zeros(9, 9, 9);
        V(5, 5, 5) = 1;
        box3 = smooth3(V, 'box', 3);
        gauss = smooth3(V, 'gaussian', 5, 1);
        assert(box3(5, 5, 5) > 0);
        assert(gauss(5, 5, 5) > 0);

        % A wider block spreads the spike further out.
        wide = smooth3(V, 'box', 5);
        assert(wide(3, 5, 5) > box3(3, 5, 5));
        """);

    [Fact]
    public Task DivergenceOfTheOutwardFieldIsThree() => RunAsserting(Field + """
        d = divergence(X, Y, Z, X, Y, Z);
        assert(abs(d(5, 5, 5) - 3) < 1e-9);
        assert(isequal(size(d), size(V)));
        """);

    [Fact]
    public Task CurlOfAFieldTurningAboutZPointsAlongZ() => RunAsserting(Field + """
        U = -Y; W = 0 * Z;
        [cx, cy, cz, cav] = curl(X, Y, Z, U, X, W);
        assert(abs(cx(5, 5, 5)) < 1e-9);
        assert(abs(cy(5, 5, 5)) < 1e-9);
        assert(abs(cz(5, 5, 5) - 2) < 1e-9);

        % The fourth answer is the angular velocity — half the length of the curl.
        assert(abs(cav(5, 5, 5) - 1) < 1e-9);
        """);

    [Fact]
    public Task CurlAndDivergenceReadAPlaneToo() => RunAsserting("""
        [X, Y] = meshgrid(-2:0.25:2, -2:0.25:2);
        d = divergence(X, Y, X, Y);
        assert(abs(d(5, 5) - 2) < 1e-9);

        cav = curl(X, Y, -Y, X);
        assert(abs(cav(5, 5) - 1) < 1e-9);
        """);

    /// <summary>
    /// The reading is straight-line interpolation along each direction, so it is exact on a field
    /// that is itself straight in each direction and only close on one that curves. A grid point is
    /// exact either way, which is the other half of what this pins.
    /// </summary>
    [Fact]
    public Task Interp3ReadsBetweenTheGridLines() => RunAsserting(Field + """
        L = 3 * X - 2 * Y + 0.5 * Z;
        q = interp3(X, Y, Z, L, 0.137, -0.44, 0.29);
        assert(abs(q - (3 * 0.137 - 2 * -0.44 + 0.5 * 0.29)) < 1e-9);

        % On the curving field a grid point is still exactly its own reading.
        onGrid = interp3(X, Y, Z, V, 0.4, 0.4, 0.4);
        assert(abs(onGrid - 3 * 0.16) < 1e-9);

        % Several points at once answer in the shape they were asked in.
        many = interp3(X, Y, Z, V, [0 1], [0 0], [0 0]);
        assert(numel(many) == 2);
        assert(abs(many(1)) < 1e-12);
        assert(abs(many(2) - 1) < 1e-9);
        """);

    // --- isosurface and the shapes ----------------------------------------------------------------

    /// <summary>
    /// The milestone's central claim about this verb: the surface it finds is where the readings
    /// actually reach the level, which for a sphere field means a sphere of the right radius.
    /// </summary>
    [Fact]
    public Task IsosurfaceFindsTheSurfaceWhereTheReadingsReachTheLevel() => RunAsserting(Field + """
        fv = isosurface(X, Y, Z, V, 1);
        assert(isstruct(fv));
        assert(size(fv.vertices, 2) == 3);
        assert(size(fv.faces, 2) == 3);

        r = sqrt(sum(fv.vertices.^2, 2));
        assert(max(abs(r - 1)) < 0.02);
        """);

    [Fact]
    public Task IsosurfaceAnswersFacesAndVerticesWhenAskedForTwo() => RunAsserting(Field + """
        [f, v] = isosurface(X, Y, Z, V, 1);
        fv = isosurface(X, Y, Z, V, 1);
        assert(isequal(f, fv.faces));
        assert(isequal(v, fv.vertices));

        % Faces count their vertices from one, the way a script writes them.
        assert(min(f(:)) >= 1);
        assert(max(f(:)) <= size(v, 1));
        """);

    [Fact]
    public Task IsosurfaceDrawsAPatchWhenNobodyWantedTheShape() => RunAsserting(Field + """
        figure(1);
        isosurface(X, Y, Z, V, 1);
        h = findobj(gcf, 'Type', 'patch');
        assert(numel(h) == 1);
        assert(size(get(h, 'Vertices'), 2) == 3);
        """);

    /// <summary>
    /// The loop the whole family exists for, and the one the roadmap named explicitly: the struct one
    /// verb hands back is the struct <c>patch</c> reads.
    /// </summary>
    [Fact]
    public Task TheStructIsosurfaceAnswersWithFeedsPatch() => RunAsserting(Field + """
        figure(1);
        fv = isosurface(X, Y, Z, V, 1);
        h = patch(fv);
        assert(strcmp(get(h, 'Type'), 'patch'));
        assert(isequal(size(get(h, 'Faces')), size(fv.faces)));
        assert(isequal(size(get(h, 'Vertices')), size(fv.vertices)));

        % And the drawn patch holds the same numbers it was handed.
        assert(max(max(abs(get(h, 'Vertices') - fv.vertices))) < 1e-12);
        """);

    [Fact]
    public Task IsosurfaceReadsAVolumeWithNoGridAtAll() => RunAsserting(Field + """
        withGrid = isosurface(X, Y, Z, V, 1);
        without = isosurface(V, 1);
        assert(size(without.vertices, 1) == size(withGrid.vertices, 1));

        % Without a grid the coordinates count from one instead.
        assert(min(without.vertices(:)) >= 1);
        """);

    [Fact]
    public Task IsosurfaceTakesAColourVolume() => RunAsserting(Field + """
        fv = isosurface(X, Y, Z, V, 1, X);
        assert(isfield(fv, 'facevertexcdata'));
        assert(size(fv.facevertexcdata, 1) == size(fv.vertices, 1));

        % The colour at each vertex is that other field read there, which for X is the x coordinate.
        assert(max(abs(fv.facevertexcdata - fv.vertices(:, 1))) < 1e-9);
        """);

    [Fact]
    public Task IsocapsClosesTheSurfaceAtTheWalls() => RunAsserting(Field + """
        cap = isocaps(X, Y, Z, V, 3);
        assert(~isempty(cap.faces));

        % Every cap vertex sits on a wall of the box.
        onWall = abs(abs(cap.vertices(:, 1)) - 2) < 1e-9 ...
               | abs(abs(cap.vertices(:, 2)) - 2) < 1e-9 ...
               | abs(abs(cap.vertices(:, 3)) - 2) < 1e-9;
        assert(all(onWall));
        """);

    [Fact]
    public Task IsocapsTakesTheSideItShouldCover() => RunAsserting(Field + """
        above = isocaps(X, Y, Z, V, 5, 'above');
        below = isocaps(X, Y, Z, V, 5, 'below');
        assert(~isempty(above.faces));
        assert(~isempty(below.faces));
        """);

    [Fact]
    public Task IsonormalsAnswersOneDirectionPerVertex() => RunAsserting(Field + """
        fv = isosurface(X, Y, Z, V, 1);
        n = isonormals(X, Y, Z, V, fv.vertices);
        assert(isequal(size(n), size(fv.vertices)));

        % Every normal has unit length and lies along the radius, because this field grows outward.
        len = sqrt(sum(n.^2, 2));
        assert(max(abs(len - 1)) < 1e-9);
        along = sum(n .* fv.vertices, 2) ./ sqrt(sum(fv.vertices.^2, 2));
        assert(max(along) < -0.99);
        """);

    [Fact]
    public Task IsocolorsReadsAnotherFieldAtTheVertices() => RunAsserting(Field + """
        fv = isosurface(X, Y, Z, V, 1);
        c = isocolors(X, Y, Z, Y, fv.vertices);
        assert(numel(c) == size(fv.vertices, 1));
        assert(max(abs(c - fv.vertices(:, 2))) < 1e-9);
        """);

    [Fact]
    public Task IsocolorsPaintsThePatchItIsHanded() => RunAsserting(Field + """
        figure(1);
        fv = isosurface(X, Y, Z, V, 1);
        h = patch(fv);
        c = isocolors(X, Y, Z, X, h);
        assert(numel(c) == size(fv.vertices, 1));
        """);

    [Fact]
    public Task Surf2PatchTurnsAGridIntoQuadrilaterals() => RunAsserting("""
        Z = peaks(10);
        fv = surf2patch(Z);
        assert(size(fv.vertices, 1) == 100);
        assert(size(fv.faces, 1) == 81);
        assert(size(fv.faces, 2) == 4);

        % 'triangles' cuts each quadrilateral in two.
        tri = surf2patch(Z, 'triangles');
        assert(size(tri.faces, 1) == 162);
        assert(size(tri.faces, 2) == 3);
        """);

    [Fact]
    public Task Surf2PatchTakesItsOwnGrid() => RunAsserting("""
        [X, Y] = meshgrid(1:4, 1:3);
        fv = surf2patch(X, Y, X + Y);
        assert(size(fv.vertices, 1) == 12);
        assert(size(fv.faces, 1) == 6);
        """);

    [Fact]
    public Task ReducepatchLeavesFewerFacesThanItWasAskedToKeep() => RunAsserting(Field + """
        fv = isosurface(X, Y, Z, V, 1);
        [f, v] = reducepatch(fv, 0.2);
        assert(size(f, 1) < size(fv.faces, 1));
        assert(size(v, 1) < size(fv.vertices, 1));

        % At least the share asked for, and every face still names three different vertices.
        assert(size(f, 1) >= 0.2 * size(fv.faces, 1));
        assert(all(f(:, 1) ~= f(:, 2)) && all(f(:, 2) ~= f(:, 3)));
        """);

    [Fact]
    public Task ReducepatchChangesAPatchInPlaceWhenHandedOne() => RunAsserting(Field + """
        figure(1);
        h = patch(isosurface(X, Y, Z, V, 1));
        before = size(get(h, 'Faces'), 1);
        reducepatch(h, 0.3);
        assert(size(get(h, 'Faces'), 1) < before);
        """);

    [Fact]
    public Task ShrinkfacesPullsEveryFaceInTowardsItsOwnCentre() => RunAsserting("""
        fv.vertices = [0 0 0; 1 0 0; 0 1 0];
        fv.faces = [1 2 3];
        [f, v] = shrinkfaces(fv, 0.5);
        assert(size(f, 1) == 1);
        assert(size(v, 1) == 3);

        % The centre stays put and every corner has moved half way in.
        assert(max(abs(mean(v) - mean(fv.vertices))) < 1e-12);
        assert(abs(v(1, 1) - 1/6) < 1e-12);
        """);

    /// <summary>
    /// Shrinking necessarily breaks the sharing of vertices: two faces that met at a corner now have
    /// a corner each. That is what the operation means, not a loss.
    /// </summary>
    [Fact]
    public Task ShrinkfacesGivesEveryFaceItsOwnCorners() => RunAsserting(Field + """
        fv = isosurface(X, Y, Z, V, 1);
        [f, v] = shrinkfaces(fv, 0.5);
        assert(size(f, 1) == size(fv.faces, 1));
        assert(size(v, 1) == 3 * size(fv.faces, 1));
        """);

    // --- the stream family ------------------------------------------------------------------------

    [Fact]
    public Task Stream3FollowsAFieldThatPointsOneWay() => RunAsserting("""
        [X, Y, Z] = meshgrid(-2:0.25:2, -2:0.25:2, -2:0.25:2);
        U = ones(size(X));
        v = stream3(X, Y, Z, U, 0 * X, 0 * X, -2, 0.5, -0.5);
        assert(iscell(v));
        assert(numel(v) == 1);

        p = v{1};
        assert(size(p, 2) == 3);
        assert(abs(p(1, 1) + 2) < 1e-9);
        assert(p(end, 1) > 1.9);

        % It only ever moved along x.
        assert(max(abs(p(:, 2) - 0.5)) < 1e-9);
        assert(max(abs(p(:, 3) + 0.5)) < 1e-9);
        """);

    [Fact]
    public Task Stream2StaysOnItsCircle() => RunAsserting("""
        [X, Y] = meshgrid(-2:0.1:2, -2:0.1:2);
        v = stream2(X, Y, -Y, X, 1, 0, [0.1 400]);
        p = v{1};
        assert(size(p, 2) == 2);
        assert(size(p, 1) == 400);

        r = sqrt(p(:, 1).^2 + p(:, 2).^2);
        assert(max(abs(r - 1)) < 0.02);
        """);

    [Fact]
    public Task StreamlineDrawsThePointsItIsHanded() => RunAsserting("""
        figure(1);
        [X, Y, Z] = meshgrid(-2:0.25:2, -2:0.25:2, -2:0.25:2);
        v = stream3(X, Y, Z, ones(size(X)), 0 * X, 0 * X, [-2 -2], [0 1], [0 0]);
        h = streamline(v);
        assert(numel(h) == 2);
        assert(strcmp(get(h(1), 'Type'), 'line'));
        """);

    [Fact]
    public Task StreamlineTracesForItselfWhenHandedAField() => RunAsserting("""
        figure(1);
        [X, Y, Z] = meshgrid(-2:0.25:2, -2:0.25:2, -2:0.25:2);
        h = streamline(X, Y, Z, ones(size(X)), 0 * X, 0 * X, [-2 -2], [0 1], [0 0]);
        assert(numel(h) == 2);

        x = get(h(1), 'XData');
        assert(abs(x(1) + 2) < 1e-9);
        assert(x(end) > 1.9);
        """);

    [Fact]
    public Task StreamsliceStartsItsOwnLinesOverThePlane() => RunAsserting("""
        figure(1);
        [X, Y] = meshgrid(-2:0.2:2, -2:0.2:2);
        h = streamslice(X, Y, -Y, X);
        assert(numel(h) > 4);
        assert(strcmp(get(h(1), 'Type'), 'line'));
        """);

    [Fact]
    public Task StreamribbonAndStreamtubeDrawSurfaces() => RunAsserting("""
        figure(1);
        [X, Y, Z] = meshgrid(-2:0.25:2, -2:0.25:2, -2:0.25:2);
        U = ones(size(X));
        hr = streamribbon(X, Y, Z, U, 0 * X, 0 * X, -2, 0, 0);
        assert(numel(hr) == 1);
        assert(strcmp(get(hr(1), 'Type'), 'surface'));

        clf;
        ht = streamtube(X, Y, Z, U, 0 * X, 0 * X, -2, 0, 0);
        assert(numel(ht) == 1);
        assert(strcmp(get(ht(1), 'Type'), 'surface'));
        """);

    [Fact]
    public Task ConeplotPutsOneArrowheadAtEachPlace() => RunAsserting("""
        figure(1);
        [X, Y, Z] = meshgrid(-2:0.25:2, -2:0.25:2, -2:0.25:2);
        h = coneplot(X, Y, Z, ones(size(X)), 0 * X, 0 * X, [-1 0 1], [0 0 0], [0 0 0]);
        assert(strcmp(get(h, 'Type'), 'patch'));

        % Three cones of eight sides each, and every cone has a wall and a base per side.
        assert(size(get(h, 'Faces'), 1) == 48);
        """);

    [Fact]
    public Task ConeplotDrawsArrowsWhenAskedForQuiver() => RunAsserting("""
        figure(1);
        [X, Y, Z] = meshgrid(-2:0.25:2, -2:0.25:2, -2:0.25:2);
        h = coneplot(X, Y, Z, ones(size(X)), 0 * X, 0 * X, [-1 0 1], [0 0 0], [0 0 0], 'quiver');
        assert(strcmp(get(h, 'Type'), 'quiver'));
        """);

    [Fact]
    public Task ContoursliceDrawsOneLinePerLevelPerPlane() => RunAsserting(Field + """
        figure(1);
        h = contourslice(X, Y, Z, V, [], [], [-1 0 1], 4);
        assert(numel(h) <= 12);
        assert(numel(h) > 0);
        assert(strcmp(get(h(1), 'Type'), 'line'));

        % Every point of a z-plane's contour sits on that plane.
        z = get(h(1), 'ZData');
        z = z(~isnan(z));
        assert(max(z) - min(z) < 1e-9);
        """);

    // --- the refusals -----------------------------------------------------------------------------

    [Fact]
    public async Task ASubvolumeBoxIsSixNumbers()
    {
        string message = await RunExpectingFailure(Field + "subvolume(X, Y, Z, V, [0 1]);");
        Assert.Contains("six numbers", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownSmooth3FilterIsRefusedByName()
    {
        string message = await RunExpectingFailure("smooth3(zeros(5,5,5), 'wibble');");
        Assert.Contains("wibble", message, StringComparison.Ordinal);
        Assert.Contains("gaussian", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownIsocapsSideIsRefusedByName()
    {
        string message = await RunExpectingFailure(Field + "isocaps(X, Y, Z, V, 1, 'sideways');");
        Assert.Contains("sideways", message, StringComparison.Ordinal);
        Assert.Contains("above", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownConeplotWordIsRefusedByName()
    {
        string message = await RunExpectingFailure("""
            [X, Y, Z] = meshgrid(-2:0.5:2, -2:0.5:2, -2:0.5:2);
            coneplot(X, Y, Z, ones(size(X)), 0*X, 0*X, 0, 0, 0, 'arrows');
            """);
        Assert.Contains("arrows", message, StringComparison.Ordinal);
        Assert.Contains("quiver", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStructWithoutBothFieldsIsRefusedByPatch()
    {
        string message = await RunExpectingFailure("""
            figure(1);
            s.faces = [1 2 3];
            patch(s);
            """);
        Assert.Contains("vertices", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVolumeOfFourDimensionsIsRefused()
    {
        string message = await RunExpectingFailure("smooth3(zeros(2, 2, 2, 2));");
        Assert.Contains("dimensions", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInterp3MethodOtherThanLinearIsRefusedWithAReason()
    {
        string message = await RunExpectingFailure(Field + "interp3(X, Y, Z, V, 0, 0, 0, 'spline');");
        Assert.Contains("spline", message, StringComparison.Ordinal);
        Assert.Contains("linear", message, StringComparison.Ordinal);
    }
}
