using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>typecast</c>, <c>histcounts</c>'s named bin count, <c>dct</c>/<c>idct</c>, <c>smoothdata</c>'s
/// chosen window, and <c>ode45</c>'s solution structure (M123).
/// </summary>
/// <remarks>
/// Every literal here is R2024a's, taken from a run rather than from the documentation. Three of
/// these five were wrong in a way that produced a perfectly plausible answer — a window of ten
/// thousand instead of four, edges that split the range exactly instead of landing on round numbers,
/// a double where a uint32 was asked for — so a test that only checked shape or class would have
/// passed on every one of them.
/// </remarks>
[Collection("JG facade")]
public class MatlabTransformsAndBinsM123Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabTransformsAndBinsM123Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    // --- typecast --------------------------------------------------------------------------------

    /// <summary>
    /// The bits are the bits of the class the value wears. Reading a single's four bytes as a
    /// double's eight is the one thing this function must not do, and it was all it did.
    /// </summary>
    [Theory]
    [InlineData("typecast(single(1.5), 'uint32')", "uint32", "1069547520")]
    [InlineData("typecast(uint32(1069547520), 'single')", "single", "1.5")]
    [InlineData("typecast(1.5, 'uint32')", "uint32", "[0 1073217536]")]
    [InlineData("typecast(int8(-1), 'uint8')", "uint8", "255")]
    [InlineData("typecast(uint16([1 2]), 'uint32')", "uint32", "131073")]
    [InlineData("typecast(single([1.5 2.5]), 'uint8')", "uint8", "[0 0 192 63 0 0 32 64]")]
    public void TypecastReadsTheBytesOfTheClassItWasHanded(string call, string cls, string value) =>
        Assert.Equal($"{cls} {value}", Run($"v = {call};\nfprintf('%s %s', class(v), mat2str(double(v)));"));

    /// <summary>A column stays a column, which a flat rewrite of the samples would lose.</summary>
    [Fact]
    public void TypecastKeepsTheOrientationItWasGiven() =>
        Assert.Equal("[2 1] [1 2]", Run("""
            a = typecast(uint32([1069547520; 1069547520]), 'single');
            b = typecast(uint32([1069547520, 1069547520]), 'single');
            fprintf('%s %s', mat2str(size(a)), mat2str(size(b)));
            """));

    // --- histcounts ------------------------------------------------------------------------------

    /// <summary>
    /// A bin count is a count, not a set of edges, so the edges it leaves free are put on round
    /// numbers. Splitting the exact range is the obvious answer and is not MATLAB's.
    /// </summary>
    [Theory]
    [InlineData("[1.234 5.678 9.1011 2.5 7.5]", 4, "[1 3.1 5.2 7.3 9.4]")]
    [InlineData("[1.234 5.678 9.1011 2.5 7.5]", 5, "[1 2.7 4.4 6.1 7.8 9.5]")]
    [InlineData("0:9", 4, "[0 2.3 4.6 6.9 9.2]")]
    [InlineData("[1 2 2 3 3 3 4 4 4 4]", 4, "[0.7 1.6 2.5 3.4 4.3]")]
    [InlineData("[1 2 2 3 3 3]", 3, "[0.6 1.4 2.2 3]")]
    public void ANamedBinCountStillChoosesReadableEdges(string data, int bins, string expected) =>
        Assert.Equal("1", Run($"""
            [~, e] = histcounts({data}, {bins});
            fprintf('%d', max(abs(e - {expected})) < 1e-12);
            """));

    /// <summary>
    /// Named limits are exact, so the count only decides how many bins fit between them. This is the
    /// arm the round-number rule must not touch.
    /// </summary>
    [Fact]
    public void NamedLimitsAreSplitExactlyAndNotRounded() =>
        Assert.Equal("1", Run("""
            z = mod((1:1000)*0.618033988749895, 1);
            [~, e] = histcounts(z, 'NumBins', 8, 'BinLimits', [0 1]);
            fprintf('%d', max(abs(e - (0:0.125:1))) < 1e-12);
            """));

    /// <summary>The automatic rule is unchanged, and it already agreed with MATLAB.</summary>
    [Fact]
    public void TheAutomaticRuleStillPutsWholeNumbersInTheirOwnBin() =>
        Assert.Equal("1", Run("""
            [n, e] = histcounts([1 2 2 3 3 3 4 4 4 4]);
            fprintf('%d', isequal(n, [1 2 3 4]) && isequal(e, [0.5 1.5 2.5 3.5 4.5]));
            """));

    // --- dct / idct ------------------------------------------------------------------------------

    /// <summary>All four types, against R2024a's own digits for the same five samples.</summary>
    [Theory]
    [InlineData(1, "[6.62132034355964 -3 0.878679656440357 -1 0.621320343559643]")]
    [InlineData(2, "[6.70820393249937 -3.14949988895055 0 -0.283990227825647 0]")]
    [InlineData(3, "[5.64940700208514 -4.35994904637288 1.71212465956731 -1.03493354415326 0.269418906373481]")]
    [InlineData(4, "[4.73655817831764 -4.51456293056127 2.23606797749979 -2.04242697556169 1.73557777668194]")]
    public void EveryDctTypeMatchesMatlab(int type, string expected) =>
        Assert.Equal("1", Run($"""
            v = dct([1 2 3 4 5], 'Type', {type});
            fprintf('%d', max(abs(v - {expected})) < 1e-12);
            """));

    /// <summary>
    /// Inverting is asking for the other type: 1 and 4 undo themselves, and 2 and 3 undo each other.
    /// </summary>
    [Fact]
    public void EachTypeIsUndoneByItsPartner() =>
        Assert.Equal("1 1 1 1", Run("""
            x = [3 1 4 1 5 9 2 6];
            fprintf('%d %d %d %d', ...
              max(abs(idct(dct(x, 'Type', 1), 'Type', 1) - x)) < 1e-12, ...
              max(abs(idct(dct(x, 'Type', 2), 'Type', 2) - x)) < 1e-12, ...
              max(abs(idct(dct(x, 'Type', 3), 'Type', 3) - x)) < 1e-12, ...
              max(abs(idct(dct(x, 'Type', 4), 'Type', 4) - x)) < 1e-12);
            """));

    /// <summary>The length pads or crops, the dimension chooses the direction, and the shape follows.</summary>
    [Fact]
    public void TheLengthAndTheDimensionAreBothTaken() =>
        Assert.Equal("[1 3] [1 8] [3 1] [4 4] 1", Run("""
            M = magic(4);
            a = dct([1 2 3 4 5], 3);
            b = dct([1 2 3 4 5], 8);
            c = dct([1; 2; 3]);
            d = dct(M, [], 2);
            fprintf('%s %s %s %s %d', mat2str(size(a)), mat2str(size(b)), mat2str(size(c)), ...
              mat2str(size(d)), max(abs(d(1,:) - [17 1.68924639724147 12 1.46507563265748])) < 1e-12);
            """));

    /// <summary>An empty transforms to an empty and a single sample is its own transform.</summary>
    [Fact]
    public void TheDegenerateShapesAreTheOnesMatlabAnswers() =>
        Assert.Equal("[0 0] 7", Run("disp([mat2str(size(dct([]))) ' ' mat2str(dct(7))]);"));

    // --- smoothdata ------------------------------------------------------------------------------

    /// <summary>
    /// The window is chosen from what is in the readings, not from how many there are. A tenth of the
    /// sample count gave the last case a window of ten thousand where MATLAB uses four — a difference
    /// that never showed in a sum and was the whole of the picture.
    /// </summary>
    [Theory]
    [InlineData("1:10", 5)]
    [InlineData("[3 1 4 1 5 9 2 6 5 3 5 8 9 7 9]", 2)]
    [InlineData("cumsum(ones(1,50)) + 0.3*sin((1:50))", 23)]
    [InlineData("sin(20*mod((1:100)*0.618033988749895, 1))", 4)]
    [InlineData("sin(20*mod((1:10000)*0.618033988749895, 1))", 4)]
    public void TheChosenWindowIsTheOneMatlabChooses(string data, int window) =>
        Assert.Equal(window.ToString(), Run($"[~, w] = smoothdata({data});\nfprintf('%d', w);"));

    /// <summary>
    /// Six methods share the window, so all six moved with it. Each row is R2024a's answer for the
    /// same ten samples.
    /// </summary>
    [Theory]
    [InlineData("", "[2 2.5 3 4 5 6 7 8 8.5 9]")]
    [InlineData(", 'movmedian'", "[2 2.5 3 4 5 6 7 8 8.5 9]")]
    [InlineData(", 'gaussian'", "[1.50359858618 2.11525760434 3 4 5 6 7 8 8.88474239566 9.49640141382]")]
    [InlineData(", 'sgolay'", "[1 2 3 4 5 6 7 8 9 10]")]
    [InlineData(", 'lowess'", "[1 2 3 4 5 6 7 8 9 10]")]
    [InlineData(", 'loess'", "[1 2 3 4 5 6 7 8 9 10]")]
    public void EveryMethodSmoothsByTheAmountMatlabSmoothsBy(string method, string expected) =>
        Assert.Equal("1", Run($"""
            v = smoothdata([1 2 3 4 5 6 7 8 9 10]{method});
            fprintf('%d', max(abs(v - {expected})) < 1e-9);
            """));

    /// <summary>The two ends of the smoothing factor, which the periodogram cannot express.</summary>
    [Fact]
    public void TheEndsOfTheSmoothingFactorAreEverythingAndNothing() =>
        Assert.Equal("50 1", Run("""
            d = cumsum(ones(1,50));
            [~, all] = smoothdata(d, 'SmoothingFactor', 1);
            [~, none] = smoothdata(d, 'SmoothingFactor', 0);
            fprintf('%d %d', all, none);
            """));

    // --- ode45's solution structure ---------------------------------------------------------------

    /// <summary>
    /// One output is a solution and two are a table of times. The solution holds the mesh the solver
    /// actually stepped on, which is fewer points and not a coarser answer.
    /// </summary>
    [Fact]
    public void OneOutputIsASolutionAndTwoAreThePair() =>
        Assert.Equal("struct ode45 [1 16] [2 16] 15 0 91 [2 7 16] 11 41", Run("""
            sol = ode45(@(t,y) [y(2); -y(1)], [0 4], [1; 0]);
            o = odeset('RelTol', 1e-8);
            s2 = ode45(@(t,y) -y, [0 1], 1, o);
            [t3, ~] = ode45(@(t,y) -y, [0 1], 1, o);
            fprintf('%s %s %s %s %d %d %d %s %d %d', class(sol), sol.solver, mat2str(size(sol.x)), ...
              mat2str(size(sol.y)), sol.stats.nsteps, sol.stats.nfailed, sol.stats.nfevals, ...
              mat2str(size(sol.idata.f3d)), numel(s2.x), numel(t3));
            """));

    /// <summary>
    /// <c>deval</c> reads the solution where nobody asked for it, off the same polynomial the solver
    /// reports its own refined points from. The numbers are R2024a's to ten digits, rounding dust
    /// included — they are not the exact cosine, and agreeing with the exact answer would mean the
    /// interpolant was not the one MATLAB uses.
    /// </summary>
    [Fact]
    public void DevalReadsTheSolutionBetweenTheStepsTheSolverTook() =>
        Assert.Equal("1", Run("""
            sol = ode45(@(t,y) [y(2); -y(1)], [0 4], [1; 0]);
            d = deval(sol, [0 1 2 3 4]);
            want = [1 0.5402993333 -0.4161525842 -0.9899872242 -0.6536329617];
            fprintf('%d', max(abs(d(1,:) - want)) < 1e-8);
            """));

    /// <summary>The second output is the slope, and a third argument picks components.</summary>
    [Fact]
    public void DevalAnswersTheSlopeAndTheComponentAsked() =>
        Assert.Equal("1 1 [1 1]", Run("""
            sol = ode45(@(t,y) [y(2); -y(1)], [0 4], [1; 0]);
            [y, yp] = deval(sol, 2);
            one = deval(sol, 2, 1);
            fprintf('%d %d %s', max(abs(y - [-0.4161525842; -0.9092936426])) < 1e-8, ...
              max(abs(yp - [-0.9092620542; 0.4161549864])) < 1e-8, mat2str(size(one)));
            """));

    /// <summary>
    /// The steps are read back out of the structure's own fields, so a solution that has been through
    /// a file reads exactly the same.
    /// </summary>
    [Fact]
    public void ASolutionIsAnOrdinaryValueAndReadsBackFromItsOwnFields() =>
        Assert.Equal("1", Run("""
            sol = ode45(@(t,y) -y, [0 2], 1);
            copy = struct('solver', sol.solver, 'x', sol.x, 'y', sol.y, 'idata', sol.idata);
            fprintf('%d', abs(deval(copy, 1.3) - deval(sol, 1.3)) == 0);
            """));

    /// <summary>Something that is not a solution is refused by name rather than read as one.</summary>
    [Fact]
    public void SomethingThatIsNotASolutionIsRefused()
    {
        var context = new ScriptContext(new RecordingScriptOutput(), (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            "y = deval([1 2 3], 1.5);", context, default, sourceId: "", hook: null, JgsDialect.Matlab);

        Assert.False(result.Success);
        Assert.Contains("solution structure", result.Message, StringComparison.Ordinal);
    }
}
