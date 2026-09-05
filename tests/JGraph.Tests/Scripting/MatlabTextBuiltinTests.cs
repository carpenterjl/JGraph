using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// String searching, regular expressions, formatted reading, and the byte views (M38). Positions
/// are 1-based here because these run in the MATLAB dialect; a JGS script gets the same answers
/// from 0.
/// </summary>
[Collection("JG facade")]
public class MatlabTextBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabTextBuiltinTests() => JG.Reset();

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

    [Fact]
    public Task Strfind_ReportsEveryPositionIncludingOverlaps() => RunAsserting("""
        assert(isequal(strfind('abcabc', 'abc'), [1 4]));
        assert(isequal(strfind('aaa', 'aa'), [1 2]));
        assert(isempty(strfind('abc', 'z')));
        assert(isequal(findstr('abcabc', 'bc'), [2 5]));
        """);

    [Fact]
    public Task PrefixComparisons_TreatAShortStringAsAMismatch() => RunAsserting("""
        assert(strncmp('hello', 'help', 3));
        assert(~strncmp('hello', 'help', 4));
        assert(strncmpi('HELLO', 'help', 3));
        assert(~strncmp('ab', 'abc', 3));
        """);

    [Fact]
    public Task CountingAndShaping_MatchTheirDefinitions() => RunAsserting("""
        assert(count('abcabc', 'abc') == 2);
        assert(strlength('hello') == 5);
        assert(strcmp(deblank('trim me   '), 'trim me'));
        assert(strlength(blanks(4)) == 4);
        assert(strcmp(strcat('one ', 'two'), 'onetwo'));
        assert(matches('abc', 'abc'));
        assert(~matches('abc', 'ab'));
        assert(isequal(strlength({'a', 'bb', 'ccc'}), [1 2 3]));
        """);

    [Fact]
    public Task Regexp_ProducesTheOutputsItsOptionWordsName() => RunAsserting("""
        assert(isequal(regexp('one two three', '\w+'), [1 5 9]));
        assert(isequal(regexp('one two three', '\w+', 'end'), [3 7 13]));

        m = regexp('a1 b22 c333', '[a-z](\d+)', 'match');
        assert(iscell(m));
        assert(numel(m) == 3);
        assert(strcmp(m{2}, 'b22'));

        t = regexp('a1 b22', '[a-z](\d+)', 'tokens');
        assert(strcmp(t{1}{1}, '1'));
        assert(strcmp(t{2}{1}, '22'));

        assert(strcmp(regexp('a1 b22', '[a-z](\d+)', 'match', 'once'), 'a1'));

        parts = regexp('a,b,,c', ',', 'split');
        assert(numel(parts) == 4);
        assert(strcmp(parts{3}, ''));
        """);

    [Fact]
    public Task Regexp_TakesSeveralOutputsInTheOrderTheWordsWereGiven() => RunAsserting("""
        [tok, mat] = regexp('x=12', '(\w)=(\d+)', 'tokens', 'match');
        assert(strcmp(tok{1}{1}, 'x'));
        assert(strcmp(tok{1}{2}, '12'));
        assert(strcmp(mat{1}, 'x=12'));

        % With no option words the outputs are MATLAB's default order: start, then end.
        [s, e] = regexp('hello', 'l+');
        assert(s == 3);
        assert(e == 4);
        """);

    [Fact]
    public Task RegexpReplaceAndTranslate_HandleGroupsAndWildcards() => RunAsserting("""
        assert(strcmp(regexprep('hello world', 'o', '0'), 'hell0 w0rld'));
        assert(strcmp(regexprep('john smith', '(\w+) (\w+)', '$2 $1'), 'smith john'));
        assert(strcmp(regexpi('ABC', 'b', 'match', 'once'), 'B'));
        assert(strcmp(regexptranslate('escape', 'a.b'), 'a\.b'));
        assert(~isempty(regexp('report2024.txt', regexptranslate('wildcard', '*.txt'))));
        """);

    [Fact]
    public Task Isstrprop_ClassifiesEachCharacter() => RunAsserting("""
        assert(isequal(isstrprop('a1 ', 'alpha'), [true false false]));
        assert(isequal(isstrprop('a1 ', 'digit'), [false true false]));
        assert(isequal(isstrprop('a1 ', 'wspace'), [false false true]));
        assert(isequal(isstrprop('a1 ', 'alphanum'), [true true false]));
        """);

    [Fact]
    public Task Sscanf_ReadsEveryNumberInTheString() => RunAsserting("""
        assert(isequal(sscanf('1 2 3', '%f'), [1; 2; 3]));   % a column, as MATLAB answers
        assert(isequal(sscanf('1.5, 2.5', '%f, %f'), [1.5; 2.5]));
        assert(sscanf('-3e2', '%f') == -300);   % one value still comes back as a one-element array
        assert(isequal(sscanf('10 20 30', '%d', 2), [10; 20]));
        assert(strcmp(sscanf('abc', '%s'), 'abc'));
        """);

    [Fact]
    public Task ByteViews_RoundTripThroughTheirEncoding() => RunAsserting("""
        b = unicode2native('AB');
        assert(isequal(b, [65 66]));
        assert(strcmp(native2unicode([72 105]), 'Hi'));
        assert(strcmp(native2unicode(unicode2native('round trip')), 'round trip'));

        % A double's bits, little-endian: 1.0 is 0x3FF0000000000000.
        assert(isequal(typecast(1, 'uint8'), [0 0 0 0 0 0 240 63]));
        assert(numel(typecast([1 2], 'uint32')) == 4);
        assert(typecast(1, 'double') == 1);
        """);

    [Fact]
    public Task StringConversions_AreIdentityWithNoStringArrayType() => RunAsserting("""
        assert(strcmp(convertCharsToStrings('text'), 'text'));
        assert(strcmp(convertStringsToChars('text'), 'text'));
        assert(strcmp(convertContainedStringsToChars('text'), 'text'));
        assert(strcmp(setstr([72 105]), 'Hi'));
        """);
}
