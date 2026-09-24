using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), seventeenth sub-stage: the three forms the generated value-isolation fixtures
/// found missing. A <c>for</c> over a char row, a char matrix, a string array or a string matrix
/// binds a column a pass - a 1-by-1 char or string over a row; a dictionary entry is removed by
/// <c>d(key) = []</c>, the bare bracket alone; and a <c>containers.Map</c>'s Count is a uint64.
/// With them, what the fixture measured on the way: a dictionary's <c>keys</c> and <c>values</c>
/// are columns of their kind, its dot names are its verbs and nothing else, and a string array of
/// values is one value a key.
/// </summary>
/// <remarks>
/// The parity fixture <c>loop_text_dict_remove</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class LoopTextDictRemoveM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public LoopTextDictRemoveM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult Run(string code) => _engine.RunAsync(
        code,
        new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
        CancellationToken.None).GetAwaiter().GetResult();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string RunRefusing(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success, "the script was expected to fail");
        return result.Message + _output.ErrorText;
    }

    // --- for over text ------------------------------------------------------------------------------

    [Theory]
    [InlineData("'abz'", "char [1 1] a|char [1 1] b|char [1 1] z")]
    [InlineData("['ab'; 'cd']", "char [2 1] a/c|char [2 1] b/d")]
    [InlineData("['a'; 'b'; 'c']", "char [3 1] a/b/c")]
    [InlineData("[\"x\" \"yy\"]", "string [1 1] x|string [1 1] yy")]
    [InlineData("[\"a\" \"b\"; \"c\" \"d\"]", "string [2 1] a,c|string [2 1] b,d")]
    [InlineData("\"abc\"", "string [1 1] abc")]
    [InlineData("int8('ab')", "int8 [1 1] 97|int8 [1 1] 98")]
    public void AForOverTextBindsAColumnAPass(string source, string expected)
    {
        string text = RunAndRead($$"""
            r = {};
            for c = {{source}}
                if ischar(c)
                    r{end + 1} = sprintf('char %s %s', mat2str(size(c)), strjoin(cellstr(c)', '/'));
                elseif isstring(c)
                    r{end + 1} = sprintf('string %s %s', mat2str(size(c)), strjoin(cellstr(c(:)'), ','));
                else
                    r{end + 1} = sprintf('%s %s %s', class(c), mat2str(size(c)), mat2str(c));
                end
            end
            fprintf('%s\n', strjoin(r, '|'));
            """);
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("''", "0")]
    [InlineData("strings(1, 0)", "0")]
    [InlineData("zeros(0, 3)", "3 double [0 1]")]
    [InlineData("strings(0, 3)", "3 string [0 1]")]
    public void AnEmptySourceRunsOnePassAColumn(string source, string expected)
    {
        string text = RunAndRead($$"""
            n = 0; last = '';
            for c = {{source}}
                n = n + 1;
                last = sprintf(' %s %s', class(c), mat2str(size(c)));
            end
            fprintf('%d%s\n', n, last);
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void TheLoopWalksTheTextItsHeadNamedWhateverTheBodyRebinds()
    {
        string text = RunAndRead("""
            t = 'abc'; r = '';
            for c = t
                t(1) = 'z';
                r = [r c];
            end
            u = ["p" "q"]; v = "";
            for e = u
                u(1) = "z";
                v = v + e;
            end
            fprintf('%s %s %s %s\n', r, t, v, strjoin(u, ''));
            """);
        Assert.Equal("abc zbc pq zq", text);
    }

    // --- d(key) = [] -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("d(2) = [];", "[1 3] [10 30] 2")]
    [InlineData("d([1 3]) = [];", "2 20 1")]
    [InlineData("d(9) = [];", "[1 2 3] [10 20 30] 3")]
    [InlineData("d(2) = []; d(2) = 99;", "[1 3 2] [10 30 99] 3")]
    public void TheBareBracketRemovesADictionarysEntries(string write, string expected)
    {
        string text = RunAndRead($$"""
            d = dictionary([1 2 3], [10 20 30]);
            {{write}}
            fprintf('%s %s %d\n', mat2str(keys(d)'), mat2str(values(d)'), numEntries(d));
            """);
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("x = []; d(2) = x;")]
    [InlineData("d(2) = zeros(1, 0);")]
    [InlineData("d(2) = [1 2];")]
    public void AnEmptyOrLongerValueIsRefusedNotStored(string write)
    {
        string message = RunRefusing($$"""
            d = dictionary([1 2 3], [10 20 30]);
            {{write}}
            """);
        Assert.Contains("Dimensions of the key and value must be the same, or the value must be scalar.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACellValuedDictionaryRefusesAnEmptyCellAndRemovesByTheBracket()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], {5, 'x'});
            d(1) = [];
            c = values(d);
            fprintf('%d %s %s\n', numEntries(d), class(c), c{1});
            """);
        Assert.Equal("1 cell x", text);

        string message = RunRefusing("""
            d = dictionary([1 2], {5, 'x'});
            d(2) = {};
            """);
        Assert.Contains("Dimensions of the key and value must be the same", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARemovalThroughAnAliasAFieldACellSlotOrAGlobalReachesThatDictionaryAlone()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], [10 20]); e = d;
            e(1) = [];
            st.d = dictionary([1 2], [10 20]);
            st.d(2) = [];
            c = {dictionary([1 2], [10 20])};
            c{1}(2) = [];
            global gd
            gd = dictionary([1 2], [10 20]);
            remove_one();
            fprintf('%d %d %d %d %d\n', numEntries(d), numEntries(e), numEntries(st.d), numEntries(c{1}), numEntries(gd));
            function remove_one()
            global gd
            gd(1) = [];
            end
            """);
        Assert.Equal("2 1 1 1 1", text);
    }

    [Fact]
    public void RemovingEveryEntryLeavesAConfiguredDictionaryWithEmptyColumnsOfItsKind()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], [10 20]);
            d(1) = []; d(2) = [];
            s = dictionary(["a"], ["x"]);
            s("a") = [];
            fprintf('%d %d %s %s %s %s %s\n', numEntries(d), isConfigured(d), ...
                mat2str(size(keys(d))), class(values(d)), mat2str(size(values(d))), ...
                class(keys(s)), class(values(s)));
            """);
        Assert.Equal("0 1 [0 1] double [0 1] string string", text);
    }

    // --- keys, values and the dot ---------------------------------------------------------------------

    [Fact]
    public void KeysAndValuesAreColumnsOfTheirKind()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], [10 20]);
            s = dictionary(["a" "b"], ["x" "y"]);
            c = dictionary([1 2], {5, 'x'});
            fprintf('%s %s|%s %s %s|%s %s\n', mat2str(size(keys(d))), mat2str(values(d)), ...
                class(keys(s)), mat2str(size(values(s))), strjoin(values(s)', ','), ...
                class(values(c)), mat2str(size(values(c))));
            """);
        Assert.Equal("[2 1] [10;20]|string [2 1] x,y|cell [2 1]", text);
    }

    [Fact]
    public void AStringArrayOfValuesIsOneValueAKey()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], ["a" "b"]);
            fprintf('%s %s %d\n', d(1), d(2), numEntries(d));
            """);
        Assert.Equal("a b 2", text);
    }

    [Fact]
    public void ADictionarysDotNamesAreItsVerbsAndNothingElse()
    {
        string text = RunAndRead("""
            d = dictionary([1 2], [10 20]);
            fprintf('%d %s %d\n', d.numEntries, mat2str(d.keys), d.isConfigured);
            """);
        Assert.Equal("2 [1;2] 1", text);

        string message = RunRefusing("""
            d = dictionary([1 2], [10 20]);
            d.Count
            """);
        Assert.Contains("Unrecognized method, property, or field 'Count' for class 'dictionary'.", message, StringComparison.Ordinal);
    }

    // --- containers.Map's Count ------------------------------------------------------------------------

    [Fact]
    public void AMapsCountIsAUint64AndItsLengthIsThatCountAsADouble()
    {
        string text = RunAndRead("""
            m = containers.Map({'a', 'b'}, {1, 2});
            e = containers.Map();
            fprintf('%s %d %s %d|%s %d|%s %d\n', class(m.Count), m.Count, class(m.Count + 1), m.Count + 1, ...
                class(e.Count), e.Count, class(length(m)), length(m));
            remove(m, 'a');
            fprintf('%s %d\n', class(m.Count), m.Count);
            """);
        Assert.Equal("uint64 2 uint64 3|uint64 0|double 2\nuint64 1", text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void AMapStoresTheEmptyItIsGivenWhereADictionaryRemoves()
    {
        string text = RunAndRead("""
            m = containers.Map('KeyType', 'char', 'ValueType', 'any');
            m('a') = 1; m('b') = 2;
            m('a') = [];
            fprintf('%d %d %s\n', m.Count, isempty(m('a')), class(m('a')));
            """);
        Assert.Equal("2 1 double", text);
    }
}
