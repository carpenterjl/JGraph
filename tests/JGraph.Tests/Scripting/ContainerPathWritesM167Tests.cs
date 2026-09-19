using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), fifth sub-stage: writes through a container path (appendix A #82, #83, #85,
/// #149, #166). A write into a struct array's element reaches the element; a path whose levels do
/// not exist yet creates each one; a path through a value of the wrong kind is refused in
/// R2025b's words and changes nothing; <c>struct([])</c>; cells and struct arrays by more than one
/// subscript; a dictionary read by several keys.
/// </summary>
/// <remarks>
/// The parity fixture <c>container_path_writes</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class ContainerPathWritesM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public ContainerPathWritesM167Tests() => JG.Reset();

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
    [InlineData("b(2).v(1) = 9;", "[3 4] [9 4]")]
    [InlineData("b(2).v(end + 1) = 9;", "[3 4] [3 4 9]")]
    [InlineData("b(2).v(1) = [];", "[3 4] 4")]
    [InlineData("b(2).v(2, 2) = 7;", "[3 4] [3 4;0 7]")]
    public void AWriteIntoAStructArraysElementReachesItWithOrWithoutAnAlias(string write, string expected)
    {
        string text = RunAndRead($$"""
            a = struct('v', {[1 2], [3 4]});
            b = a;
            {{write}}
            fprintf('%s %s\n', mat2str(a(2).v), mat2str(b(2).v));
            c = struct('v', {[1 2], [3 4]});
            c(2).v(1) = 9;
            assert(isequal(c(2).v, [9 4]));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void ALevelThatDoesNotExistIsCreatedAsR2025bCreatesIt()
    {
        string text = RunAndRead("""
            x.y(3).z = 1;
            c = {}; c{3}.f = 1;
            s0.c{2}(3) = 1;
            d = {}; d{2}.a(2).b = 1;
            t(2).v(3) = 5;
            e = {}; e{2}{3} = 'x';
            fprintf('%d %d %d|%d %s %s|%s %s|%d %d %d|%d %d %s|%d %s\n', ...
                numel(x.y), isempty(x.y(1).z), x.y(3).z, ...
                numel(c), class(c{1}), class(c{3}), ...
                mat2str(s0.c{2}), mat2str(size(s0.c{1})), ...
                numel(d{2}.a), isempty(d{2}.a(1).b), d{2}.a(2).b, ...
                numel(t), isempty(t(1).v), mat2str(t(2).v), ...
                numel(e{2}), e{2}{3});
            """);
        Assert.Equal("3 1 1|3 double struct|[0 0 1] [0 0]|2 1 1|2 1 [0 0 5]|3 x", text);
    }

    [Fact]
    public void ANewLevelOnACopyLeavesTheOriginalAndKeepsWhatWasThere()
    {
        string text = RunAndRead("""
            x.k = 1; y = x;
            y.n(2).z = 5;
            y.m{2} = 'q';
            b = struct('v', {1, 2}); w = b;
            w(3).g(2) = 7;
            fprintf('%s|%s|%d %d|%s|%d %d\n', strjoin(fieldnames(x)', ','), strjoin(fieldnames(y)', ','), ...
                y.k, y.n(2).z, strjoin(fieldnames(w)', ','), numel(b), isempty(w(1).g));
            """);
        Assert.Equal("k|k,n,m|1 5|v,g|2 1", text);
    }

    [Theory]
    [InlineData("x = 5; x.f = 1;", "dot indexing is not supported")]
    [InlineData("x = {1, 2}; x.f = 1;", "dot indexing is not supported")]
    [InlineData("x.t = 'ab'; x.t.f = 1;", "dot indexing is not supported")]
    [InlineData("x = {1, 2}; x{2}.f = 9;", "dot indexing is not supported")]
    [InlineData("x.a = 1; x{2} = 5;", "brace indexing is not supported")]
    [InlineData("x = 5; x{2} = 1;", "brace indexing is not supported")]
    [InlineData("x = struct('v', {1, 2}); x.v = 9;", "Scalar structure required for this assignment.")]
    public void APathThroughTheWrongKindIsRefusedInR2025bsWordsAndChangesNothing(string script, string fragment)
    {
        string text = RunAndRead($$"""
            try
                {{script}}
                disp('no error');
            catch err
                fprintf('%d\n', contains(err.message, '{{fragment}}'));
            end
            st.t = 'ab';
            try, st.t.f = 1; catch, end
            fprintf('%s\n', st.t);
            """);
        Assert.Equal("1\nab", text.Replace("\r", ""));
    }

    [Fact]
    public void StructOfEmptyIsTheFieldlessEmptyStruct()
    {
        string text = RunAndRead("""
            st = struct([]);
            n = 0;
            for k = 1:numel(st), n = n + 1; end
            g = st; g(2).a = 5;
            j = [st, struct('a', 1)];
            try
                st(1) = struct('a', 1);
            catch err
                disp(err.message);
            end
            fprintf('%s %s %d %d %d|%s %d %d|%s\n', class(st), mat2str(size(st)), numel(fieldnames(st)), isempty(st), n, ...
                mat2str(size(g)), isempty(g(1).a), g(2).a, mat2str(size(j)));
            """);
        Assert.Equal("Subscripted assignment between dissimilar structures.\nstruct [0 0] 0 1 0|[1 2] 1 5|[1 1]",
            text.Replace("\r", ""));
    }

    [Fact]
    public void ACellIsReadAndWrittenByTwoAndThreeSubscripts()
    {
        string text = RunAndRead("""
            c = {1, 'a'; [2 3], 'bc'};
            d = c(2, :); e = c(:, 1);
            g = c; g(2, 2) = {9}; g(1, :) = {7, 8};
            h = {1, 'a'}; t = h; h(1, 2, 2) = {2};
            k = {1, 'a'}; k{1, 2, 2} = [5 6];
            fprintf('%s %s %s|%s %d %d|%s %s %d %d|%s %s\n', mat2str(size(d)), d{2}, mat2str(size(e)), ...
                c{2, 2}, g{2, 2}, g{1, 2}, mat2str(size(h)), class(h{1, 1, 2}), h{1, 2, 2}, numel(t), ...
                mat2str(size(k)), mat2str(k{1, 2, 2}));
            """);
        Assert.Equal("[1 2] bc [2 1]|bc 9 8|[1 2 2] double 2 2|[1 2 2] [5 6]", text);
    }

    [Fact]
    public void AStructArrayIsWrittenByARowAndAColumn()
    {
        string text = RunAndRead("""
            b = struct('a', {1, 2}); b(2, 3).a = 5;
            e = struct('a', {1, 2; 3, 4}); t = e; e(2, 2).a = 9;
            u(2, 2).f = 3;
            fprintf('%s %d %d %d|%d %d|%s %d\n', mat2str(size(b)), isempty(b(2, 1).a), b(2, 3).a, b(1, 2).a, ...
                t(2, 2).a, e(2, 2).a, mat2str(size(u)), isempty(u(1, 1).f));
            """);
        Assert.Equal("[2 3] 1 5 2|4 9|[2 2] 1", text);
    }

    [Fact]
    public void ADictionaryIsReadBySeveralKeys()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], [10 20]);
            a = d([1 2]); d(2) = 9; b = d([2 1]);
            s = dictionary(["a" "b" "c"], [1 2 3]);
            v = s(["c" "a"]);
            try
                d([1 5]);
            catch err
                disp(err.message);
            end
            fprintf('%s %s %s %s\n', mat2str(a), mat2str(b), mat2str(v), mat2str(size(s(["a"; "b"]))));
            """);
        Assert.Equal("Element 2 of the key array not found.\n[10 20] [9 10] [3 1] [2 1]", text.Replace("\r", ""));
    }
}
