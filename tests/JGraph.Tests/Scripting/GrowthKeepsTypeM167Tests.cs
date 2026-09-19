using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), fourth sub-stage: every rebuild keeps what the value is (appendix A #52, #53,
/// #123, #157, #162). Growth in one, two and three dimensions, deletion, a write of another class
/// and a write that grows a scalar each keep the target's class, its tags and its own growth fill.
/// </summary>
/// <remarks>
/// The parity fixture <c>growth_keeps_type</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class GrowthKeepsTypeM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public GrowthKeepsTypeM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    [Theory]
    [InlineData("v(2) = 9;", "logical [true true true]")]
    [InlineData("v(2:3) = [0 7];", "logical [true false true]")]
    [InlineData("v(v) = 0;", "logical [false false false]")]
    [InlineData("v(5) = 2;", "logical [true false true false true]")]
    [InlineData("v(1, 2) = 3;", "logical [true true true]")]
    [InlineData("v(1, 2, 2) = 3;", "logical [true false true false true false]")]
    public void ANumberWrittenIntoALogicalArrayIsConvertedAndTheArrayStaysLogical(string write, string expected)
    {
        string text = RunAndRead($$"""
            v = [true false true];
            t = v;
            {{write}}
            fprintf('%s %s\n', class(v), mat2str(v(:)'));
            assert(islogical(t) && isequal(t, [true false true]));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void NaNHasNoLogicalValue()
    {
        string text = RunAndRead("""
            v = [true false];
            try
                v(1) = NaN;
            catch err
                disp(err.message);
            end
            fprintf('%s %s\n', class(v), mat2str(v));
            """);
        Assert.Equal("NaN's cannot be converted to logicals.\nlogical [true false]", text.Replace("\r", ""));
    }

    [Theory]
    [InlineData("q = 1; q(3) = 5;", "double [1 3] [1 0 5]")]
    [InlineData("q = 1; q(2, 2) = 5;", "double [2 2] [1 0;0 5]")]
    [InlineData("q = true; q(3) = true;", "logical [1 3] [true false true]")]
    [InlineData("q = int8(1); q(3) = 200;", "int8 [1 3] [1 0 127]")]
    [InlineData("q = 1; q(end + 1) = 2; q(end + 1) = 3;", "double [1 3] [1 2 3]")]
    [InlineData("q = 1; q(1) = 7;", "double [1 1] 7")]
    public void AScalarIsTheOneByOneArrayAWriteGrows(string script, string expected)
    {
        string text = RunAndRead($$"""
            {{script}}
            fprintf('%s %s %s\n', class(q), mat2str(size(q)), mat2str(q));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void AScalarHeldInAFieldACellSlotAndAStructArrayElementGrowsThereAlone()
    {
        string text = RunAndRead("""
            st.q = 1; r = st; r.q(3) = 5;
            c = {1}; d = c; d{1}(3) = 5;
            v = struct('f', num2cell(1:3)); w = v; v(2).f(1) = 9; v(3).f(2) = 4;
            fprintf('%s %s %s %s %d %s %d\n', mat2str(st.q), mat2str(r.q), mat2str(c{1}), mat2str(d{1}), ...
                v(2).f, mat2str(v(3).f), w(2).f);
            """);
        Assert.Equal("1 [1 0 5] 1 [1 0 5] 9 [3 4] 2", text);
    }

    [Fact]
    public void AStringArrayAndTimesKeepTheirKindInAThirdDimension()
    {
        string text = RunAndRead("""
            x = ["a" "b"]; x(1, 2, 2) = "c";
            d = datetime(2020, 1, [1 2]); d(1, 2, 2) = datetime(2020, 2, 1);
            s = seconds([1 2]); s(1, 2, 2) = seconds(5);
            u = uint8([1 2]); u(1, 2, 2) = 9;
            fprintf('%s %s %d %s %d %s %g %s\n', class(x), mat2str(size(x)), ismissing(x(1, 1, 2)), ...
                class(d), isnat(d(1, 1, 2)), class(s), seconds(s(1, 1, 2)), class(u));
            """);
        Assert.Equal("string [1 2 2] 1 datetime 1 duration 0 uint8", text);
    }

    [Fact]
    public void ADatetimeGrowsWithNaTInEveryDimension()
    {
        string text = RunAndRead("""
            d = datetime(2020, 1, [1 2]);
            a = d; a(4) = datetime(2020, 2, 1);
            b = d; b(2, 3) = datetime(2020, 2, 1);
            fprintf('%s %d %d %s %d %d\n', class(a), isnat(a(3)), day(a(4)), class(b), isnat(b(2, 1)), day(d(2)));
            """);
        Assert.Equal("datetime 1 1 datetime 1 2", text);
    }

    [Fact]
    public void ACharRowLeavesItsOneRowAndStaysChar()
    {
        string text = RunAndRead("""
            x = 'ab'; t = x; x(2, 1) = 'c';
            y = 'ab'; y(1, 2, 2) = 'c';
            z = ['ab' 67];
            fprintf('%s %s %s %s %s %s %s %s\n', class(x), mat2str(size(x)), mat2str(double(x)), t, ...
                class(y), mat2str(size(y)), class(z), z);
            """);
        Assert.Equal("char [2 2] [97 98;99 0] ab char [1 2 2] char abC", text);
    }

    [Fact]
    public void ADeletionKeepsTheClassAndAnEmptiedMaskIsStillAMask()
    {
        string text = RunAndRead("""
            x = uint8(ones(2, 2, 3)); x(:, :, 2) = [];
            w = ["a" "b"]; w(1, 2, 2) = "c"; w(:, :, 1) = [];
            y = [true false]; y([1 2]) = [];
            m = true(1, 0); m(end + 1) = -1; m(end + 1) = 0;
            fprintf('%s %s %s %s %s %s %s %s\n', class(x), mat2str(size(x)), class(w), mat2str(size(w)), ...
                class(y), mat2str(size(y)), class(m), mat2str(m));
            """);
        Assert.Equal("uint8 [2 2 2] string [1 2] logical [1 0] logical [true false]", text);
    }

    [Fact]
    public void ARefusedNdDeletionNamesItsRule()
    {
        string text = RunAndRead("""
            x = ones(2, 2, 2);
            try
                x(1, :, 2) = [];
            catch err
                disp(err.message);
            end
            fprintf('%s\n', mat2str(size(x)));
            """);
        Assert.Equal("A null assignment can have only one non-colon index.\n[2 2 2]", text.Replace("\r", ""));
    }
}
