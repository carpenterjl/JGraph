using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Scattered data (M129): <c>griddata</c> and <c>griddatan</c>, the two interpolant values, the
/// tessellation verbs in any number of directions, <c>boundary</c>, and the STL pair.
/// </summary>
/// <remarks>
/// The numbers pinned here are R2025b's, recorded by <c>m129_scattered.m</c>. What this class adds
/// beyond the fixture is the behaviour a fixture cannot see: that an interpolant is a value which
/// survives being passed to a function and written to, that replacing its values re-uses the
/// tessellation rather than rebuilding it, and that the verbs refuse what MATLAB refuses.
/// </remarks>
[Collection("JG facade")]
public class MatlabScatteredDataM129Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabScatteredDataM129Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Refuses(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal");
        return result.Message ?? string.Empty;
    }

    /// <summary>Forty points in general position: a lattice of cosines nudged by a thousandth of a sine.</summary>
    private const string Points =
        "t = (1:40)';\nx = 2*cos(2.3*t) + 0.001*sin(t);\ny = 2*sin(1.7*t) + 0.001*cos(t);\n"
        + "v = x.*exp(-x.^2 - y.^2);\n";

    [Fact]
    public void ALinearFieldIsReproducedExactlyByEveryTriangleBasedMethod() =>
        Assert.Equal(
            "nearest 0|linear 1|natural 1|cubic 1|",
            Run(Points + """
                w = 1 + 2*x - 3*y;
                names = {'nearest', 'linear', 'natural', 'cubic'};
                for i = 1:4
                    q = griddata(x, y, w, 0.3, -0.2, names{i});
                    fprintf('%s %d|', names{i}, double(abs(q - (1 + 0.6 + 0.6)) < 1e-10));
                end
                """));

    [Fact]
    public void GriddataExpandsAxisVectorsIntoAGridAndHandsItBack() =>
        Assert.Equal(
            "4 5|4 5|4 5|-1.5 -0.75|-1.5 -0.5|",
            Run(Points + """
                [XQ, YQ, VQ] = griddata(x, y, v, -1.5:0.75:1.5, (-1.5:1:1.5)');
                fprintf('%d %d|', size(XQ, 1), size(XQ, 2));
                fprintf('%d %d|', size(YQ, 1), size(YQ, 2));
                fprintf('%d %d|', size(VQ, 1), size(VQ, 2));
                fprintf('%g %g|', XQ(1, 1), XQ(1, 2));
                fprintf('%g %g|', YQ(1, 1), YQ(2, 1));
                """));

    [Fact]
    public void QueryArraysOfOneShapeAreReadPointByPointRatherThanExpanded() =>
        Assert.Equal("1 3|", Run(Points + """
            vq = griddata(x, y, v, [0.1 -0.3 0.4], [0.2 0.35 -0.15]);
            fprintf('%d %d|', size(vq, 1), size(vq, 2));
            """));

    [Fact]
    public void AScatteredInterpolantIsAValueThatIsCalledLikeAFunction() =>
        Assert.Equal(
            "scatteredInterpolant|linear|linear|40 2|40 1|1|",
            Run(Points + """
                F = scatteredInterpolant(x, y, v);
                fprintf('%s|%s|%s|', class(F), F.Method, F.ExtrapolationMethod);
                fprintf('%d %d|', size(F.Points, 1), size(F.Points, 2));
                fprintf('%d %d|', size(F.Values, 1), size(F.Values, 2));
                fprintf('%d|', double(abs(F(0.1, 0.2) - griddata(x, y, v, 0.1, 0.2)) < 1e-12));
                """));

    [Fact]
    public void WritingTheValuesMovesTheSurfaceWithoutMovingTheTessellation() =>
        Assert.Equal("1|1|", Run(Points + """
            F = scatteredInterpolant(x, y, v);
            before = F(0.1, 0.2);
            F.Values = 2*v;
            fprintf('%d|', double(abs(F(0.1, 0.2) - 2*before) < 1e-12));
            F.Values = v;
            fprintf('%d|', double(abs(F(0.1, 0.2) - before) < 1e-14));
            """));

    [Fact]
    public void WritingTheMethodChangesTheSurfaceItReads() =>
        Assert.Equal("1|1|", Run(Points + """
            F = scatteredInterpolant(x, y, v);
            F.Method = 'natural';
            fprintf('%d|', double(abs(F(0.1, 0.2) - griddata(x, y, v, 0.1, 0.2, 'natural')) < 1e-12));
            F.Method = 'nearest';
            fprintf('%d|', double(abs(F(0.1, 0.2) - griddata(x, y, v, 0.1, 0.2, 'nearest')) < 1e-12));
            """));

    [Fact]
    public void AnInterpolantSurvivesBeingPassedToAFunctionAndCalledThere() =>
        Assert.Equal("1|", Run(Points + """
            F = scatteredInterpolant(x, y, v);
            read = @(G, a, b) G(a, b);
            fprintf('%d|', double(abs(read(F, 0.1, 0.2) - F(0.1, 0.2)) < 1e-15));
            """));

    [Fact]
    public void ThreeExtrapolationModesAnswerThreeDifferentThingsOutsideTheHull() =>
        Assert.Equal("none nan|nearest finite|linear finite|", Run(Points + """
            modes = {'none', 'nearest', 'linear'};
            for i = 1:3
                F = scatteredInterpolant(x, y, v, 'linear', modes{i});
                q = F(5, 5);
                if isnan(q)
                    fprintf('%s nan|', modes{i});
                else
                    fprintf('%s finite|', modes{i});
                end
            end
            """));

    [Fact]
    public void AGriddedInterpolantCarriesItsGridVectorsAndReadsACellOfThem() =>
        Assert.Equal("griddedInterpolant|linear|linear|1|2.5|2 1|", Run("""
            G = griddedInterpolant([1 2 4 7 11], [3 1 4 1 5]);
            fprintf('%s|%s|%s|', class(G), G.Method, G.ExtrapolationMethod);
            fprintf('%d|%g|', numel(G.GridVectors), G(3));
            c = G({[1.5 3.5]});
            fprintf('%d %d|', size(c, 1), size(c, 2));
            """));

    [Fact]
    public void AGriddedInterpolantsDefaultOutsideRuleIsItsOwnMethod() =>
        Assert.Equal("nearest nearest|spline spline|makima makima|previous previous|", Run("""
            names = {'nearest', 'spline', 'makima', 'previous'};
            for i = 1:4
                G = griddedInterpolant([1 2 4 7 11], [3 1 4 1 5], names{i});
                fprintf('%s %s|', G.Method, G.ExtrapolationMethod);
            end
            """));

    [Fact]
    public void TheTessellationVerbsAgreeWithEachOtherAboutTheSameSetOfPoints() =>
        Assert.Equal("61 3|17 2|1|", Run(Points + """
            P = [x y];
            T = delaunayn(P);
            fprintf('%d %d|', size(T, 1), size(T, 2));
            [K, V] = convhulln(P);
            fprintf('%d %d|', size(K, 1), size(K, 2));
            % The tessellation covers exactly the hull, so the two answers agree about the area.
            area = 0;
            for i = 1:size(T, 1)
                a = T(i, 1); b = T(i, 2); c = T(i, 3);
                area = area + abs((x(b)-x(a))*(y(c)-y(a)) - (y(b)-y(a))*(x(c)-x(a)))/2;
            end
            fprintf('%d|', double(abs(area - V) < 1e-10));
            """));

    [Fact]
    public void BarycentricCoordinatesFromTsearchnRebuildTheLinearAnswer() =>
        Assert.Equal("1|1|", Run(Points + """
            P = [x y];
            T = delaunayn(P);
            XI = [0.1 0.2; 0.4 -0.3];
            [ts, bary] = tsearchn(P, T, XI);
            for i = 1:2
                q = bary(i, :) * v(T(ts(i), :));
                fprintf('%d|', double(abs(q - griddata(x, y, v, XI(i, 1), XI(i, 2))) < 1e-12));
            end
            """));

    [Fact]
    public void DsearchnPutsTheChosenOutValueOnEveryPointOutsideTheHull() =>
        Assert.Equal("1 1 0|1 1 1|", Run(Points + """
            P = [x y];
            T = delaunayn(P);
            XI = [0.1 0.2; 0.4 -0.3; 6 6];
            k = dsearchn(P, T, XI, Inf);
            fprintf('%d %d %d|', isfinite(k(1)), isfinite(k(2)), isfinite(k(3)));
            % Without an out-value every point gets its nearest neighbour, inside the hull or not.
            k2 = dsearchn(P, T, XI);
            fprintf('%d %d %d|', isfinite(k2(1)), isfinite(k2(2)), isfinite(k2(3)));
            """));

    [Fact]
    public void ShrinkingTheBoundaryNeverEnclosesMoreThanTheConvexHull() =>
        Assert.Equal("1|1|1|", Run(Points + """
            [~, a0] = boundary(x, y, 0);
            [~, a5] = boundary(x, y, 0.5);
            [~, a1] = boundary(x, y, 1);
            fprintf('%d|', double(a0 >= a5));
            fprintf('%d|', double(a5 >= a1));
            % A shrink of nought is the convex hull, and the hull's area is convhull's own.
            [~, hull] = convhull(x, y);
            fprintf('%d|', double(abs(a0 - hull) < 1e-10));
            """));

    [Fact]
    public void AnStlRoundTripKeepsThePointsAndTheTrianglesInBothFormats() =>
        Assert.Equal("binary 5 6 6|text 5 6 6|1|", Run("""
            TR = struct('Points', [0 0 0; 1 0 0; 1 1 0; 0 1 0; 0.5 0.5 1], ...
                        'ConnectivityList', [1 2 3; 1 3 4; 1 2 5; 2 3 5; 3 4 5; 4 1 5]);
            names = {'m129_unit_bin.stl', 'm129_unit_txt.stl'};
            formats = {'binary', 'text'};
            same = 1;
            for i = 1:2
                stlwrite(TR, names{i}, formats{i});
                [S, f] = stlread(names{i});
                fprintf('%s %d %d %d|', f, size(S.Points, 1), size(S.ConnectivityList, 1), ...
                        sum(sum(S.ConnectivityList == TR.ConnectivityList)) / 3);
                same = same && abs(sum(S.Points(:)) - sum(TR.Points(:))) < 1e-6;
                delete(names{i});
            end
            fprintf('%d|', double(same));
            """));

    [Fact]
    public void TheMethodsMatlabRefusesInSpaceAreRefusedHere()
    {
        Assert.Contains("plane", Refuses(Points + "griddata(x, y, x, v, 0.1, 0.2, 0.3, 'cubic');"));
        Assert.Contains("plane", Refuses(Points + "griddata(x, y, x, v, 0.1, 0.2, 0.3, 'v4');"));
        Assert.Contains("methods", Refuses(Points + "griddata(x, y, v, 0.1, 0.2, 'quintic');"));
    }

    [Fact]
    public void QueryArraysThatAreNeitherOneShapeNorOneGridAreRefused() =>
        Assert.Contains("same size", Refuses(Points + """
            F = scatteredInterpolant(x, y, v);
            F([0.1 -0.3], [0.2; 0.4]);
            """));
}
