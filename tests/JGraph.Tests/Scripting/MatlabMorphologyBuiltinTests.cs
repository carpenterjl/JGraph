using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave F as a <c>.m</c> script sees it: <c>strel</c> as an object with fields, the
/// reconstruction family, the <c>bwmorph</c> operation set, and the three distance transforms.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabMorphologyBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabMorphologyBuiltinTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _directory));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _directory));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return result.Message + _output.ErrorText;
    }

    [Fact]
    public async Task Strel_IsAnObjectWithAClassAndANeighborhood()
    {
        await RunAsserting("""
            se = strel('disk', 2);
            assert(strcmp(class(se), 'strel'));
            assert(isequal(size(se.Neighborhood), [5 5]));
            assert(sum(se.Neighborhood(:)) == 13);
            assert(se.Dimensionality == 2);

            sq = strel('square', 3);
            assert(sum(sq.Neighborhood(:)) == 9);

            rect = strel('rectangle', [2 5]);
            assert(isequal(size(rect.Neighborhood), [2 5]));

            dia = strel('diamond', 2);
            assert(sum(dia.Neighborhood(:)) == 13);

            oct = strel('octagon', 3);
            assert(isequal(size(oct.Neighborhood), [7 7]));

            horizontal = strel('line', 5, 0);
            assert(isequal(size(horizontal.Neighborhood), [1 5]));

            % A three-dimensional shape is a three-dimensional neighbourhood.
            ball = strel('sphere', 2);
            assert(ball.Dimensionality == 3);
            assert(isequal(size(ball.Neighborhood), [5 5 5]));

            % And a bare matrix is a structuring element too.
            custom = strel([1 1 1; 0 1 0]);
            assert(isequal(size(custom.Neighborhood), [2 3]));
            """);

        string message = await RunExpectingFailure("strel('hexagon', 3);");
        Assert.Contains("octagon", message);
    }

    [Fact]
    public async Task Offsetstrel_CarriesHeightsAndDilationAddsThem()
    {
        await RunAsserting("""
            se = offsetstrel('ball', 3, 2);
            assert(strcmp(class(se), 'offsetstrel'));
            assert(abs(se.Offset(4, 4) - 2) < 1e-12);
            assert(isinf(se.Offset(1, 1)) && se.Offset(1, 1) < 0);

            % Dilating a lone pixel by a ball raises it by the ball's own peak.
            I = zeros(11, 11);
            I(6, 6) = 0.5;
            D = imdilate(I, offsetstrel('ball', 2, 0.2));
            assert(abs(D(6, 6) - 0.7) < 1e-12);
            assert(D(6, 7) > 0.5 && D(6, 7) < 0.7);
            """);
    }

    [Fact]
    public async Task Morphology_TakesEitherAStrelOrThePlainMatrixOlderScriptsPass()
    {
        await RunAsserting("""
            I = zeros(15, 15);
            I(5:11, 5:11) = 1;

            viaStrel = imerode(I, strel('square', 3));
            viaMatrix = imerode(I, ones(3));
            assert(isequal(viaStrel, viaMatrix));

            % The default element is the 3-by-3 square.
            assert(isequal(imerode(I), viaMatrix));

            % Opening and closing, and the two hats over a speck.
            J = zeros(21, 21);
            J(:) = 0.2;
            J(11, 11) = 0.9;
            top = imtophat(J, strel('square', 3));
            assert(abs(top(11, 11) - 0.7) < 1e-12);
            assert(abs(top(1, 1)) < 1e-12);

            K = zeros(21, 21);
            K(:) = 0.6;
            K(11, 11) = 0.1;
            bot = imbothat(K, strel('square', 3));
            assert(abs(bot(11, 11) - 0.5) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imreconstruct_KeepsOnlyWhatTheMarkerReaches()
    {
        await RunAsserting("""
            mask = zeros(16, 16);
            mask(3:6, 3:6) = 1;
            mask(12:14, 12:14) = 1;

            marker = zeros(16, 16);
            marker(4, 4) = 1;

            kept = imreconstruct(marker, mask);
            assert(kept(3, 3) == 1);
            assert(kept(13, 13) == 0);
            assert(mask(13, 13) == 1);

            % Reconstruction is idempotent: what it grew is already all it can grow.
            assert(isequal(imreconstruct(kept, mask), kept));

            % Connectivity is 4 or 8, or the 3-by-3 neighbourhood MATLAB also takes.
            four = imreconstruct(marker, mask, 4);
            assert(isequal(four, imreconstruct(marker, mask, conndef(2, 'minimal'))));
            """);

        string message = await RunExpectingFailure("imreconstruct(zeros(4), zeros(4), 6);");
        Assert.Contains("4 or 8", message);
    }

    [Fact]
    public async Task Imfill_FillsHolesOrTheRegionAroundASeed()
    {
        await RunAsserting("""
            ring = zeros(11, 11);
            ring(3:9, 3:9) = 1;
            ring(4:8, 4:8) = 0;

            filled = imfill(ring, 'holes');
            assert(filled(6, 6) == 1);
            assert(filled(1, 1) == 0);

            % Seeded: fill the background region holding a named pixel, and nothing else.
            walls = zeros(9, 9);
            walls(:, 5) = 1;
            left = imfill(walls, [5 2]);
            assert(left(1, 1) == 1);
            assert(left(5, 7) == 0);

            % A linear index says the same thing, counted MATLAB's way down the columns.
            same = imfill(walls, sub2ind(size(walls), 5, 2));
            assert(isequal(same, left));

            % The grayscale form raises an enclosed basin to its rim.
            bowl = 0.2 * ones(11, 11);
            bowl(4:8, 4:8) = 0.7;
            bowl(6, 6) = 0.3;
            raised = imfill(bowl, 'holes');
            assert(abs(raised(6, 6) - 0.7) < 1e-12);
            """);
    }

    [Fact]
    public async Task TheExtremaFamily_SuppressesShallowPeaksAndFindsSignificantOnes()
    {
        await RunAsserting("""
            I = 0.2 * ones(15, 15);
            I(4, 4) = 0.25;
            I(11, 11) = 0.6;

            flattened = imhmax(I, 0.1);
            assert(abs(flattened(4, 4) - 0.2) < 1e-12);
            assert(abs(flattened(11, 11) - 0.5) < 1e-12);

            peaks = imregionalmax(I);
            assert(peaks(4, 4) == 1);
            assert(peaks(11, 11) == 1);
            assert(peaks(1, 1) == 0);

            significant = imextendedmax(I, 0.2);
            assert(significant(4, 4) == 0);
            assert(significant(11, 11) == 1);

            % The minima side is the same thing read upside down.
            valleys = imregionalmin(1 - I);
            assert(isequal(valleys, peaks));
            raised = imhmin(1 - I, 0.1);
            assert(abs(raised(11, 11) - 0.5) < 1e-12);

            % Imposing minima leaves exactly one, where the marker said.
            marker = zeros(15, 15);
            marker(8, 8) = 1;
            imposed = imimposemin(I, marker);
            only = imregionalmin(imposed);
            assert(only(8, 8) == 1);
            assert(only(4, 4) == 0);
            assert(sum(only(:)) == 1);
            """);
    }

    [Fact]
    public async Task Imclearborder_DropsWhatTouchesTheEdge()
    {
        await RunAsserting("""
            I = zeros(11, 11);
            I(1, 6) = 1;
            I(2, 6) = 1;
            I(6, 6) = 1;

            cleared = imclearborder(I);
            assert(cleared(1, 6) == 0);
            assert(cleared(2, 6) == 0);
            assert(cleared(6, 6) == 1);
            """);
    }

    [Fact]
    public async Task Makelut_AndBwlookup_AgreeWithTheMorphologyTheyDescribe()
    {
        await RunAsserting("""
            lut = makelut(@(x) any(x(:)), 3);
            assert(length(lut) == 512);

            I = zeros(12, 12);
            I(4:7, 4:7) = 1;
            assert(isequal(bwlookup(I, lut), imdilate(I, ones(3))));

            % applylut is the older name for the same operation.
            assert(isequal(applylut(I, lut), bwlookup(I, lut)));

            % A 2-by-2 table has sixteen entries.
            small = makelut(@(x) sum(x(:)) >= 3, 2);
            assert(length(small) == 16);
            """);

        string message = await RunExpectingFailure("bwlookup(zeros(4), [1 2 3]);");
        Assert.Contains("512", message);
    }

    [Fact]
    public async Task Bwmorph_RunsTheNamedOperations()
    {
        await RunAsserting("""
            I = zeros(11, 11);
            I(3, 3) = 1;
            I(7, 7) = 1;
            I(7, 8) = 1;
            cleaned = bwmorph(I, 'clean');
            assert(sum(cleaned(:)) == 2);

            block = zeros(9, 9);
            block(3:7, 3:7) = 1;
            outline = bwmorph(block, 'remove');
            assert(outline(5, 5) == 0);
            assert(outline(3, 5) == 1);

            holed = block;
            holed(5, 5) = 0;
            filled = bwmorph(holed, 'fill');
            voted = bwmorph(holed, 'majority');
            assert(filled(5, 5) == 1);
            assert(voted(5, 5) == 1);

            line = zeros(9, 9);
            line(5, 3:7) = 1;
            ends = bwmorph(line, 'endpoints');
            trimmed = bwmorph(line, 'spur');
            assert(sum(ends(:)) == 2);
            assert(trimmed(5, 3) == 0);

            % Inf means "until nothing changes", and a thick bar becomes a stroke.
            bar = zeros(15, 21);
            bar(7:11, 4:18) = 1;
            skeleton = bwmorph(bar, 'skel', Inf);
            assert(sum(skeleton(:)) < sum(bar(:)) / 4);
            assert(sum(skeleton(:)) > 5);

            edge = bwperim(block);
            assert(edge(5, 5) == 0);
            assert(edge(3, 5) == 1);
            """);

        string message = await RunExpectingFailure("bwmorph(zeros(4), 'sharpen');");
        Assert.Contains("branchpoints", message);
    }

    [Fact]
    public async Task Bwskel_PrunesShortBranches()
    {
        await RunAsserting("""
            I = zeros(15, 25);
            I(8, 4:22) = 1;
            I(7, 13) = 1;
            I(6, 13) = 1;

            pruned = bwskel(I, 'MinBranchLength', 4);
            assert(pruned(6, 13) == 0);
            assert(pruned(8, 13) == 1);

            % Without pruning the spur stays.
            kept = bwskel(I);
            assert(kept(6, 13) == 1);
            """);
    }

    [Fact]
    public async Task Bwhitmiss_FindsACornerFromTwoElementsOrOneInterval()
    {
        await RunAsserting("""
            I = zeros(9, 9);
            I(4:7, 4:7) = 1;

            hits = [0 0 0; 0 1 1; 0 1 1];
            misses = [1 1 1; 1 0 0; 1 0 0];
            corners = bwhitmiss(I, hits, misses);
            assert(corners(4, 4) == 1);
            assert(corners(4, 7) == 0);
            assert(corners(6, 6) == 0);

            % The interval form says the same thing in one matrix.
            interval = [-1 -1 -1; -1 1 1; -1 1 1];
            assert(isequal(bwhitmiss(I, interval), corners));
            """);
    }

    [Fact]
    public async Task Bwdist_MeasuresDistanceAndNamesTheNearestSeed()
    {
        await RunAsserting("""
            I = zeros(21, 21);
            I(11, 11) = 1;

            D = bwdist(I);
            assert(abs(D(11, 11)) < 1e-12);
            assert(abs(D(14, 15) - 5) < 1e-10);
            assert(abs(D(1, 1) - hypot(10, 10)) < 1e-10);

            % The second output is a linear index into the picture, counted down the columns.
            [D2, idx] = bwdist(I);
            assert(isequal(D, D2));
            assert(idx(1, 1) == sub2ind(size(I), 11, 11));

            % The chamfer metrics measure their own way.
            city = bwdist(I, 'cityblock');
            chess = bwdist(I, 'chessboard');
            quasi = bwdist(I, 'quasi-euclidean');
            assert(abs(city(15, 14) - 7) < 1e-10);
            assert(abs(chess(15, 14) - 4) < 1e-10);
            assert(quasi(15, 14) > D(15, 14));

            % Nothing to measure against is infinitely far away.
            empty = bwdist(zeros(4));
            assert(all(all(isinf(empty))));
            """);
    }

    [Fact]
    public async Task Bwdistgeodesic_AndGraydist_RespectWhatIsInTheWay()
    {
        await RunAsserting("""
            corridor = ones(9, 9);
            corridor(1:8, 5) = 0;

            D = bwdistgeodesic(corridor, 1, 1, 'cityblock');
            assert(abs(D(1, 7) - 22) < 1e-10);
            assert(isinf(D(4, 5)));

            % A mask says the same thing as a column and row pair.
            seeds = false(9, 9);
            seeds(1, 1) = true;
            assert(isequal(bwdistgeodesic(corridor, seeds, 'cityblock'), D));

            % Gray-weighted: a dark valley costs nothing to walk along.
            I = ones(5, 9);
            I(4, :) = 0;
            G = graydist(I, 1, 4, 'cityblock');
            assert(abs(G(4, 9)) < 1e-12);
            assert(G(1, 9) > 0.5);
            """);
    }

    [Fact]
    public async Task Conndef_AndIptcheckconn_DescribeAndPoliceConnectivity()
    {
        await RunAsserting("""
            assert(isequal(conndef(2, 'minimal'), [0 1 0; 1 1 1; 0 1 0]));
            assert(isequal(conndef(2, 'maximal'), ones(3)));
            assert(isequal(size(conndef(3, 'maximal')), [3 3 3]));
            C = conndef(3, 'minimal');
            assert(sum(C(:)) == 7);

            iptcheckconn(4, 'myfun', 'CONN');
            iptcheckconn(conndef(2, 'minimal'), 'myfun', 'CONN');
            """);

        string message = await RunExpectingFailure("iptcheckconn(5, 'myfun', 'CONN');");
        Assert.Contains("CONN", message);
    }

    [Fact]
    public async Task Bwulterode_MarksTheLastPointToSurviveErosion()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:21, 1:21);
            disc = double((X - 11).^2 + (Y - 11).^2 <= 49);

            seeds = bwulterode(disc);
            assert(seeds(11, 11) == 1);
            assert(seeds(11, 16) == 0);
            """);
    }

    [Fact]
    public async Task TheWholeFamily_TakesAMatrixAndGivesOneBack()
    {
        await RunAsserting("""
            % Nothing here needs an image value: MATLAB draws no line between a picture and a
            % matrix, and neither does any of this.
            I = zeros(12, 12);
            I(4:8, 4:8) = 1;

            assert(isequal(size(imerode(I)), [12 12]));
            assert(isequal(size(imreconstruct(I, I)), [12 12]));
            assert(isequal(size(bwperim(I)), [12 12]));
            assert(isequal(size(bwdist(I)), [12 12]));
            assert(isequal(size(imregionalmax(I)), [12 12]));
            """);
    }
}
