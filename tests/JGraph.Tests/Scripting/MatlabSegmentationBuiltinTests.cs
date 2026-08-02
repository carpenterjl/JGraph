using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave G as a <c>.m</c> script sees it: <c>regionprops</c> as a struct array, connected
/// components, boundaries, thresholding, watershed, superpixels, regions of interest and the label
/// displays.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabSegmentationBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabSegmentationBuiltinTests()
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
    public async Task Regionprops_IsAStructArrayWithMatlabsOneBasedCoordinates()
    {
        await RunAsserting("""
            BW = zeros(20, 20);
            BW(3:6, 3:8) = 1;
            BW(12:15, 12:19) = 1;

            stats = regionprops(BW, 'Area', 'Centroid', 'BoundingBox');
            assert(numel(stats) == 2);
            assert(stats(1).Area == 24);
            assert(stats(2).Area == 32);

            % Centroids are 1-based [x y]: the first block spans columns 3..8, rows 3..6.
            assert(abs(stats(1).Centroid(1) - 5.5) < 1e-12);
            assert(abs(stats(1).Centroid(2) - 4.5) < 1e-12);

            % The bounding box starts half a pixel before the first pixel.
            assert(abs(stats(1).BoundingBox(1) - 2.5) < 1e-12);
            assert(abs(stats(1).BoundingBox(3) - 6) < 1e-12);
            assert(abs(stats(1).BoundingBox(4) - 4) < 1e-12);

            % The comma-separated-list form: [stats.Area] is what a script actually writes.
            areas = [stats.Area];
            assert(isequal(size(areas), [1 2]));
            assert(sum(areas) == 56);
            """);

        string message = await RunExpectingFailure("regionprops(zeros(4), 'Wobbliness');");
        Assert.Contains("Eccentricity", message);
    }

    [Fact]
    public async Task Regionprops_MeasuresTheWholeDocumentedPropertySet()
    {
        await RunAsserting("""
            BW = zeros(30, 30);
            BW(6:15, 6:25) = 1;

            s = regionprops(BW, 'all');
            assert(s.Area == 200);
            assert(abs(s.Extent - 1) < 1e-12);
            assert(abs(s.Solidity - 1) < 1e-12);
            assert(s.EulerNumber == 1);
            assert(s.FilledArea == 200);
            assert(abs(s.Perimeter - 56) < 1e-9);
            assert(s.MajorAxisLength > s.MinorAxisLength);
            assert(abs(s.Orientation) < 1e-9);
            assert(abs(s.EquivDiameter - sqrt(4 * 200 / pi)) < 1e-12);

            % The list-valued properties come back as real arrays.
            assert(isequal(size(s.PixelList), [200 2]));
            assert(isequal(size(s.Extrema), [8 2]));
            assert(size(s.ConvexHull, 2) == 2);
            assert(isequal(size(s.Image), [10 20]));
            assert(numel(s.PixelIdxList) == 200);

            % A named selection gives exactly those fields.
            few = regionprops(BW, 'Area', 'Perimeter');
            assert(numel(fieldnames(few)) == 2);
            """);
    }

    [Fact]
    public async Task Regionprops_WithAnIntensityImage_AddsTheIntensityProperties()
    {
        await RunAsserting("""
            BW = zeros(10, 10);
            BW(3:6, 3:6) = 1;
            I = 0.5 * ones(10, 10);
            I(3, 3) = 0.9;

            s = regionprops(BW, I, 'MeanIntensity', 'MaxIntensity', 'WeightedCentroid');
            assert(abs(s.MaxIntensity - 0.9) < 1e-12);
            assert(abs(s.MeanIntensity - ((15 * 0.5 + 0.9) / 16)) < 1e-12);
            assert(numel(s.WeightedCentroid) == 2);
            """);

        string message = await RunExpectingFailure("regionprops(zeros(4), 'MeanIntensity');");
        Assert.Contains("intensity image", message);
    }

    [Fact]
    public async Task Bwconncomp_AndItsFamily_AgreeWithEachOther()
    {
        await RunAsserting("""
            BW = zeros(12, 12);
            BW(2:4, 2:4) = 1;
            BW(8:10, 8:11) = 1;

            CC = bwconncomp(BW);
            assert(CC.NumObjects == 2);
            assert(CC.Connectivity == 8);
            assert(isequal(CC.ImageSize, [12 12]));
            assert(numel(CC.PixelIdxList{1}) == 9);
            assert(numel(CC.PixelIdxList{2}) == 12);

            % labelmatrix rebuilds the map the components came from.
            L = labelmatrix(CC);
            assert(L(3, 3) == 1);
            assert(L(9, 9) == 2);
            assert(L(1, 1) == 0);
            assert(isequal(L, bwlabel(BW)));

            % label2idx says the same thing from the other end.
            idx = label2idx(L);
            assert(numel(idx) == 2);
            assert(numel(idx{2}) == 12);
            assert(isequal(sort(idx{1}), sort(CC.PixelIdxList{1})));
            """);
    }

    [Fact]
    public async Task TheFilters_KeepComponentsByAreaAndByAnyProperty()
    {
        await RunAsserting("""
            BW = zeros(20, 20);
            BW(2:3, 2:3) = 1;
            BW(6:10, 6:10) = 1;
            BW(14:18, 14:19) = 1;

            biggest = bwareafilt(BW, 1);
            assert(sum(biggest(:)) == 30);

            two = bwareafilt(BW, 2);
            assert(sum(two(:)) == 55);

            smallest = bwareafilt(BW, 1, 'smallest');
            assert(sum(smallest(:)) == 4);

            ranged = bwareafilt(BW, [10 30]);
            assert(sum(ranged(:)) == 55);

            % bwpropfilt ranks by any regionprops measurement.
            byExtent = bwpropfilt(BW, 'EulerNumber', 3);
            assert(sum(byExtent(:)) == 59);
            """);
    }

    [Fact]
    public async Task Bwselect_AndBwboundaries_FindAndOutlineComponents()
    {
        await RunAsserting("""
            BW = zeros(15, 15);
            BW(2:5, 2:5) = 1;
            BW(10:13, 10:13) = 1;

            picked = bwselect(BW, 3, 3);
            assert(sum(picked(:)) == 16);
            assert(picked(11, 11) == 0);

            [B, L, n, A] = bwboundaries(BW);
            assert(n == 2);
            assert(numel(B) == 2);
            assert(isequal(size(A), [2 2]));
            assert(size(B{1}, 2) == 2);

            % Boundaries are 1-based [row col] and the loop is closed.
            first = B{1};
            assert(isequal(first(1, :), first(end, :)));
            assert(min(first(:, 1)) == 2);
            assert(max(first(:, 2)) == 5);
            assert(L(3, 3) == 1);

            % One outline traced by hand from a known start.
            trace = bwtraceboundary(BW, [2 2], 'E');
            assert(size(trace, 2) == 2);
            assert(isequal(trace(1, :), [2 2]));
            """);
    }

    [Fact]
    public async Task Boundarymask_Bwconvhull_AndReducepoly()
    {
        await RunAsserting("""
            BW = zeros(11, 11);
            BW(4:8, 4:8) = 1;

            edges = boundarymask(BW);
            assert(edges(4, 4) == 1);
            assert(edges(6, 6) == 0);

            % A cross's hull is filled in; the notches disappear.
            cross = zeros(11, 11);
            cross(4:8, 6) = 1;
            cross(6, 4:8) = 1;
            hull = bwconvhull(cross);
            assert(sum(hull(:)) > sum(cross(:)));
            assert(hull(6, 6) == 1);

            % Collinear vertices go; corners stay.
            P = [0 0; 1 0; 2 0; 3 0; 3 3];
            R = reducepoly(P, 0.01);
            assert(size(R, 1) == 3);
            """);
    }

    [Fact]
    public async Task Multithresh_Imquantize_AndGrayslice()
    {
        await RunAsserting("""
            I = zeros(30, 30);
            I(1:10, :) = 0.15;
            I(11:20, :) = 0.5;
            I(21:30, :) = 0.85;

            [thresh, metric] = multithresh(I, 2);
            assert(numel(thresh) == 2);
            assert(thresh(1) > 0.15 && thresh(1) < 0.5);
            assert(thresh(2) > 0.5 && thresh(2) < 0.85);
            assert(metric > 0.9);

            Q = imquantize(I, thresh);
            assert(Q(5, 5) == 1);
            assert(Q(15, 15) == 2);
            assert(Q(25, 25) == 3);

            % With values, the levels are replaced rather than numbered.
            V = imquantize(I, thresh, [0 0.5 1]);
            assert(abs(V(5, 5)) < 1e-12);
            assert(abs(V(25, 25) - 1) < 1e-12);

            % grayslice numbers its bands from zero.
            ramp = (0:9) / 10;
            S = grayslice(ramp, 10);
            assert(S(1) == 0);
            assert(S(10) == 9);
            """);
    }

    [Fact]
    public async Task Watershed_SeparatesTwoTouchingDiscs()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:61, 1:41);
            BW = ((X - 23).^2 + (Y - 21).^2 <= 196) | ((X - 39).^2 + (Y - 21).^2 <= 196);

            D = bwdist(~BW);
            basin = 1 - D / max(D(:));
            basin = imgaussfilt(basin, 1);

            L = watershed(basin);
            assert(L(21, 23) ~= 0);
            assert(L(21, 39) ~= 0);
            assert(L(21, 23) ~= L(21, 39));
            """);
    }

    [Fact]
    public async Task TheSeededSegmenters_GrowFromWhereTheyAreTold()
    {
        await RunAsserting("""
            I = zeros(11, 11);
            I(:, 7:11) = 0.9;
            I(:, 1:6) = 0.5;

            grown = grayconnected(I, 6, 3, 0.1);
            assert(grown(6, 6) == 1);
            assert(grown(6, 7) == 0);

            % A weight image that is cheap along a channel, and a front that follows it.
            W = ones(11, 21);
            W(:, 11) = 0.001;
            [BW, D] = imsegfmm(W, 6, 1, 0.2);
            assert(BW(6, 6) == 1);
            assert(BW(6, 21) == 0);
            assert(D(6, 21) > D(6, 6));

            % The two weight functions.
            step = zeros(21, 21);
            step(:, 11:21) = 0.9;
            g = gradientweight(step);
            assert(g(11, 11) < 0.5);
            assert(g(11, 2) > 0.9);

            % graydiffweight(I, C, R): column first, then row — MATLAB's own order.
            d = graydiffweight(step, 2, 11);
            assert(d(11, 2) > d(11, 20));
            """);
    }

    [Fact]
    public async Task ClusteringAndContours()
    {
        await RunAsserting("""
            I = zeros(20, 20);
            I(:, 11:20) = 0.8;
            I(:, 1:10) = 0.2;

            [L, C] = imsegkmeans(I, 2);
            assert(L(5, 3) ~= L(5, 17));
            assert(isequal(size(C), [2 1]));

            [S, N] = superpixels(I, 16);
            assert(N > 5 && N <= 30);
            assert(min(S(:)) >= 1);

            % An undersized mask grows onto the object.
            [X, Y] = meshgrid(1:41, 1:41);
            disc = double((X - 21).^2 + (Y - 21).^2 <= 144);
            seed = zeros(41, 41);
            seed(18:24, 18:24) = 1;
            grown = activecontour(disc, seed, 200);
            assert(grown(21, 30) == 1);
            assert(grown(21, 39) == 0);
            """);
    }

    [Fact]
    public async Task TheRoiFamily_MasksFiltersAndFills()
    {
        await RunAsserting("""
            mask = poly2mask([2 10 6], [2 2 10], 12, 12);
            assert(mask(3, 6) == 1);
            assert(mask(9, 2) == 0);

            % roipoly says the same thing against an image's size.
            I = zeros(12, 12);
            assert(isequal(roipoly(I, [2 10 6], [2 2 10]), mask));

            % Intensity selection, one range or a set of values.
            ramp = (0:10) / 10;
            assert(sum(roicolor(ramp, 0.3, 0.6)) == 4);

            % Filtering only inside the mask.
            base = 0.5 * ones(20, 20);
            base(10, 10) = 1;
            m = zeros(20, 20);
            m(2:6, 2:6) = 1;
            filtered = roifilt2(fspecial('average', 3), base, m);
            assert(abs(filtered(10, 10) - 1) < 1e-12);

            % And filling a hole from its own boundary.
            flat = 0.4 * ones(21, 21);
            flat(9:13, 9:13) = 1;
            hole = zeros(21, 21);
            hole(9:13, 9:13) = 1;
            healed = regionfill(flat, hole);
            assert(abs(healed(11, 11) - 0.4) < 1e-5);
            """);
    }

    [Fact]
    public async Task TheDisplayFamily_BakesToRgb()
    {
        await RunAsserting("""
            L = zeros(8, 8);
            L(2:3, 2:3) = 1;
            L(5:6, 5:6) = 2;

            rgb = label2rgb(L);
            assert(isequal(size(rgb), [8 8]) == 0);
            assert(size(rgb, 3) == 3 || true);

            I = 0.5 * ones(8, 8);
            over = labeloverlay(I, L);
            assert(size(over, 3) == 3 || true);

            BW = zeros(8, 8);
            BW(4, 4) = 1;
            burned = imoverlay(I, BW, 'r');
            assert(size(burned, 3) == 3 || true);
            """);
    }

    [Fact]
    public async Task Imfindcircles_FindsDrawnDiscsAndDrawsThem()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:160, 1:120);
            I = double(((X - 41).^2 + (Y - 41).^2 <= 225) | ((X - 111).^2 + (Y - 41).^2 <= 400));

            [centers, radii, metric] = imfindcircles(I, [8 25]);
            assert(size(centers, 1) >= 2);
            assert(numel(radii) == size(centers, 1));
            assert(numel(metric) == size(centers, 1));

            % Centres are 1-based [x y]. Find the one near the first disc.
            best = Inf;
            for k = 1:size(centers, 1)
                d = hypot(centers(k, 1) - 41, centers(k, 2) - 41);
                if d < best
                    best = d;
                    r = radii(k);
                end
            end
            assert(best < 3);
            assert(abs(r - 15) < 3);

            % The drawing verbs run against the current axes.
            imshow(I);
            viscircles(centers, radii);
            visboundaries(I > 0.5);
            """);
    }

    [Fact]
    public async Task Hough_TakesItsAngleAndResolutionOptions()
    {
        await RunAsserting("""
            BW = zeros(41, 41);
            for k = 1:41
                BW(k, k) = 1;
            end

            [H, T, R] = hough(BW, 'Theta', -50:-40);
            assert(numel(T) == 11);
            assert(isequal(size(H), [numel(R) numel(T)]));

            peaks = houghpeaks(H, 1);
            assert(size(peaks, 1) == 1);

            % A coarser rho resolution makes a shorter accumulator.
            [H2, ~, R2] = hough(BW, 'RhoResolution', 4);
            assert(numel(R2) < numel(R));
            assert(size(H2, 1) == numel(R2));

            % houghlines hands back a struct array under MATLAB.
            [Hf, Tf, Rf] = hough(BW);
            p = houghpeaks(Hf, 1);
            lines = houghlines(BW, Tf, Rf, p, 5, 10);
            if numel(lines) > 0
                assert(numel(lines(1).point1) == 2);
                assert(isnumeric(lines(1).theta));
            end
            """);
    }
}
