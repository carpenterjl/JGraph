using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The shape verbs over a char row, a string array and a cell (M122), and the two transposes that
/// disagreed with each other.
/// </summary>
/// <remarks>
/// Every expectation here was measured against R2024a. Four of these forms did not refuse before the
/// milestone — they returned their argument unchanged, which is why the tests check what came back
/// and not only that something did.
/// </remarks>
[Collection("JG facade")]
public class MatlabShapeOfTextM122Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabShapeOfTextM122Tests() => JG.Reset();

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

    /// <summary>The class and shape of an expression, which is most of what changed here.</summary>
    private string Shape(string setup, string expression) => Run(
        $"{setup}\nv = {expression};\nfprintf('%s %d %d', class(v), size(v, 1), size(v, 2));");

    [Theory]

    // A char row is 1-by-n characters, so every one of these reads it as characters. The first
    // four used to refuse; rot90 and transpose used to hand the row straight back.
    [InlineData("reshape(s, 2, 2)", "char 2 2")]
    [InlineData("fliplr(s)", "char 1 4")]
    [InlineData("circshift(s, 1)", "char 1 4")]
    [InlineData("triu(s)", "char 1 4")]
    [InlineData("sort(s)", "char 1 4")]
    [InlineData("unique(s)", "char 1 4")]
    [InlineData("rot90(s)", "char 4 1")]
    [InlineData("permute(s, [2 1])", "char 4 1")]
    [InlineData("transpose(s)", "char 4 1")]
    [InlineData("squeeze(s)", "char 1 4")]
    [InlineData("flipud(s)", "char 1 4")]
    [InlineData("repmat(s, 2, 1)", "char 2 4")]
    public Task ACharRowIsItsCharacters(string expression, string expected) => Task.Run(() =>
        Assert.Equal(expected, Shape("s = 'ABCD';", expression)));

    [Theory]
    [InlineData("reshape(sa, 1, 4)", "string 1 4")]
    [InlineData("permute(sa, [2 1])", "string 2 2")]
    [InlineData("fliplr(sa)", "string 2 2")]
    [InlineData("circshift(sa, 1)", "string 2 2")]
    [InlineData("transpose(sa)", "string 2 2")]
    [InlineData("rot90(sa)", "string 2 2")]
    public Task AStringArrayIsRearrangedInItsOwnContainer(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Shape("sa = [\"a\" \"b\"; \"c\" \"d\"];", expression)));

    [Theory]
    [InlineData("reshape(c, 1, 4)", "cell 1 4")]
    [InlineData("permute(c, [2 1])", "cell 2 2")]
    [InlineData("fliplr(c)", "cell 2 2")]
    [InlineData("circshift(c, 1)", "cell 2 2")]
    [InlineData("rot90(c)", "cell 2 2")]
    [InlineData("repmat(c, 1, 2)", "cell 2 4")]
    public Task ACellIsRearrangedInItsOwnContainer(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Shape("c = {1, 2; 3, 4};", expression)));

    /// <summary>
    /// The four that used to answer the right shape and the wrong contents. A no-op has the shape of
    /// its input, so only reading the values back tells a rotation from a shrug.
    /// </summary>
    [Fact]
    public Task RearrangingACellMovesItsElements() => Task.Run(() =>
    {
        string moved = Run("""
            c = {1, 2; 3, 4};
            p = permute(c, [2 1]);
            t = transpose(c);
            f = fliplr(c);
            r = reshape(c, 1, 4);
            fprintf('%d %d %d %d | %d %d %d %d | %d %d %d %d | %d %d %d %d', ...
              p{1,1}, p{1,2}, p{2,1}, p{2,2}, t{1,1}, t{1,2}, t{2,1}, t{2,2}, ...
              f{1,1}, f{1,2}, f{2,1}, f{2,2}, r{1}, r{2}, r{3}, r{4});
            """);

        Assert.Equal("1 3 2 4 | 1 3 2 4 | 2 1 4 3 | 1 3 2 4", moved);
    });

    [Fact]
    public Task RearrangingAStringArrayMovesItsElements() => Task.Run(() =>
    {
        string moved = Run("""
            sa = ["a" "b"; "c" "d"];
            fprintf('%s | %s | %s', ...
              strjoin(cellstr(reshape(sa, 1, 4)), ','), ...
              strjoin(cellstr(reshape(fliplr(sa), 1, 4)), ','), ...
              strjoin(cellstr(reshape(circshift(sa, 1), 1, 4)), ','));
            """);

        Assert.Equal("a,c,b,d | b,d,a,c | c,a,d,b", moved);
    });

    /// <summary>
    /// A char row reshapes down its columns, exactly as a number array does. Reading the code points
    /// back rather than the letters is deliberate: a wrong order is legible as letters and obvious as
    /// numbers.
    /// </summary>
    [Fact]
    public Task ACharRowReshapesColumnByColumn() => Task.Run(() =>
        Assert.Equal(
            "[65 67 69;66 68 70]",
            Run("disp(mat2str(double(reshape('ABCDEF', 2, 3))));")));

    /// <summary>
    /// The defect that the family probe found rather than the report: <c>transpose(v)</c> answered
    /// the row it was given while <c>v'</c> answered the column MATLAB answers. The two are one
    /// operation and had disagreed for long enough that the function carried a comment saying it
    /// matched the operator.
    /// </summary>
    [Fact]
    public Task TheTransposeFunctionAgreesWithTheTransposeOperator() => Task.Run(() =>
    {
        string sizes = Run("""
            v = [1 2 3 4];
            c = 'ABCD';
            fprintf('%s %s %s %s %s', mat2str(size(v')), mat2str(size(transpose(v))), ...
              mat2str(size(ctranspose(v))), mat2str(size(c')), mat2str(size(transpose(c))));
            """);

        Assert.Equal("[4 1] [4 1] [4 1] [4 1] [4 1]", sizes);
    });

    /// <summary>A transposed row still holds its values, in order.</summary>
    [Fact]
    public Task ATransposedRowKeepsItsValues() => Task.Run(() =>
        Assert.Equal("[1;2;3;4]", Run("disp(mat2str(transpose([1 2 3 4])));")));

    /// <summary>
    /// The verbs that read values rather than only moving them are deliberately not given the
    /// position gather, because a sort of positions says nothing about the text at those positions.
    /// MATLAB refuses <c>triu</c> of a string array too.
    /// </summary>
    [Fact]
    public Task AVerbThatReadsValuesStillRefusesAStringArray() => Task.Run(() =>
    {
        var context = new ScriptContext(new RecordingScriptOutput(), (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            "sa = [\"a\" \"b\"; \"c\" \"d\"];\nv = triu(sa);",
            context, default, sourceId: "", hook: null, JgsDialect.Matlab);

        Assert.False(result.Success);
    });

    /// <summary>
    /// A char matrix reaches these verbs as the numbers it already is, so nothing about it changed —
    /// this is the guard that the promotion did not start intercepting the case that already worked.
    /// </summary>
    [Fact]
    public Task ACharMatrixIsUnchangedByTheNewLane() => Task.Run(() =>
    {
        Assert.Equal("char 3 2", Shape("m = ['abc'; 'def'];", "reshape(m, 3, 2)"));
        Assert.Equal("char 2 2", Shape("m = ['ab'; 'cd'];", "triu(m)"));
        Assert.Equal(
            "[97 101;100 99;98 102]",
            Run("m = ['abc'; 'def'];\ndisp(mat2str(double(reshape(m, 3, 2))));"));
    });

    /// <summary>
    /// <c>mat2str</c>'s contract is that <c>eval</c> reads its answer back as the same value, which
    /// makes the kind of quote part of the answer rather than a detail of it.
    /// </summary>
    [Fact]
    public Task Mat2strWritesTheQuotesItsContainerTakes() => Task.Run(() =>
    {
        string written = Run("""
            fprintf('%s | %s | %s | %s | %s', mat2str('auto'), mat2str("auto"), ...
              mat2str(['ab'; 'cd']), mat2str(["a" "b"; "c" "d"]), mat2str('it''s'));
            """);

        Assert.Equal("'auto' | \"auto\" | ['ab';'cd'] | [\"a\" \"b\";\"c\" \"d\"] | 'it''s'", written);
    });
}
