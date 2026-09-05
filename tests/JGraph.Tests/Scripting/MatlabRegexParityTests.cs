using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The regular-expression and search-and-edit family against MATLAB R2025b. Every expected value
/// here was produced by running the same expression in MATLAB, not read from its documentation.
/// </summary>
/// <remarks>
/// Several of these pin an answer that the documentation would lead one to expect otherwise: a
/// zero-length match is ignored even when the pattern is a bare anchor, so <c>regexprep(s, '^', '&gt;')</c>
/// changes nothing; <c>regexp('abc', 'abc', 'tokens')</c> has no whole-match token; a group on the
/// untaken branch of an alternation is no token at all; <c>strrep</c> replaces overlapping
/// occurrences where <c>replace</c> does not; and <c>'preservecase'</c> implies <c>'ignorecase'</c>.
/// </remarks>
[Collection("JG facade")]
public class MatlabRegexParityTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabRegexParityTests() => JG.Reset();

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

    // --- regexp -----------------------------------------------------------------------------------

    [Fact]
    public Task NoMatchIsTheZeroByZeroEmptyOfEachOutput() => RunAsserting("""
        assert(isequal(size(regexp('abc', 'x')), [0 0]));
        assert(isequal(size(regexp('abc', 'x', 'once')), [0 0]));
        assert(isequal(size(regexp('abc', 'x', 'match')), [0 0]));
        assert(isequal(size(regexp('abc', 'x', 'tokens')), [0 0]));
        assert(isequal(size(regexp('abc', 'x', 'match', 'once')), [0 0]));
        assert(isequal(size(regexp('abc', 'x', 'names')), [0 0]));
        assert(isequal(regexp('abc', 'x', 'split', 'once'), {'abc'}));
        """);

    [Fact]
    public Task AZeroLengthMatchIsNotAMatchEvenForAnAnchor() => RunAsserting("""
        assert(isempty(regexp('abc', '^', 'start')));
        assert(isempty(regexp('abc', '$', 'start')));
        assert(strcmp(regexprep('abc', '^', '>'), 'abc'));
        assert(strcmp(regexprep(sprintf('a\nb'), '^', '>', 'lineanchors'), sprintf('a\nb')));
        % Only where the pattern can match something does it match: b* on 'abc' is the one b.
        assert(isequal(regexp('abc', 'b*'), 2));
        assert(strcmp(regexprep('abc', 'b*', '-'), 'a-c'));
        % A lazy quantifier is made to find a longer match rather than accept an empty one.
        assert(isequal(regexp('aaa', 'a*?', 'match'), {'a', 'a', 'a'}));
        % 'emptymatch' lets the anchors through.
        assert(isequal(regexp('abc', '$', 'emptymatch'), 4));
        assert(isequal(regexp('abc', '^', 'start', 'emptymatch'), 1));
        """);

    [Fact]
    public Task TokensFollowMatlabsGroupOrderAndDropUntakenBranches() => RunAsserting("""
        assert(isequal(regexp('ab', '(a)|(b)', 'tokens'), {{'a'}, {'b'}}));
        assert(isequal(regexp('b', '(a)?(b)', 'tokens', 'once'), {'', 'b'}));
        assert(isequal(regexp('ab', '(a)(x)?', 'tokens', 'once'), {'a', ''}));
        % No capture group means no token, not the whole match.
        t = regexp('abc', 'abc', 'tokens');
        assert(numel(t) == 1 && isempty(t{1}));
        assert(isequal(size(regexp('abc', 'bc', 'tokens', 'once')), [1 0]));
        % Named and unnamed groups are numbered together, left to right.
        assert(isequal(regexp('abc', '(?<n>b)(c)', 'tokens', 'once'), {'b', 'c'}));
        assert(strcmp(regexprep('ab', '(?<f>a)(?<s>b)', '$<s>$<f>'), 'ba'));
        """);

    [Fact]
    public Task TokenExtentsAreRowsPerToken() => RunAsserting("""
        assert(isequal(regexp('abc', '(b)(c)', 'tokenExtents', 'once'), [2 2; 3 3]));
        assert(isequal(regexp('abc', '(a)(c)?', 'tokenExtents'), {[1 1; 2 1]}));
        e = regexp('abc', 'b', 'tokenExtents');
        assert(numel(e) == 1 && isequal(size(e{1}), [0 0]));
        assert(isequal(size(regexp('abc', 'b', 'tokenExtents', 'once')), [0 0]));
        """);

    [Fact]
    public Task NamesIsAStructArrayWithOneElementPerMatch() => RunAsserting("""
        s = regexp('x=1,y=2', '(?<k>\w)=(?<v>\d)', 'names');
        assert(isequal(size(s), [1 2]));
        assert(strcmp(s(2).k, 'y') && strcmp(s(2).v, '2'));
        none = regexp('x', '(?<w>\d)', 'names');
        assert(isequal(size(none), [0 0]));
        assert(isequal(fieldnames(none), {'w'}));
        % A group on the untaken branch is [] in the struct; an unmatched optional one is ''.
        alt = regexp('abc', '(?<x>a)|(?<y>c)', 'names');
        assert(isequal(size(alt), [1 2]) && isequal(alt(1).y, []) && strcmp(alt(2).y, 'c'));
        opt = regexp('ab', '(?<n>a)(?<m>x)?', 'names');
        assert(strcmp(opt.m, ''));
        % No named group at all: one struct with no fields.
        assert(isequal(size(regexp('abc', 'b', 'names')), [1 1]));
        """);

    [Fact]
    public Task AStringSubjectAnswersStrings() => RunAsserting("""
        assert(isequal(regexp("abc123", "\d+", "match"), "123"));
        assert(isequal(regexp("a1b2", "\d", "match"), ["1" "2"]));
        assert(isequal(regexp("abc123", "\d+", "match", "once"), "123"));
        assert(isequal(regexp("a,b", ",", "split"), ["a" "b"]));
        assert(isequal(regexp("foo=1", "(\w+)=(\d+)", "tokens"), {["foo" "1"]}));
        assert(isequal(regexp("foo=1", "(\w+)=(\d+)", "tokens", "once"), ["foo" "1"]));
        assert(ismissing(regexp("abc", "x", "match", "once")));
        assert(isequal(size(regexp("abc", "x", "match")), [0 0]));
        assert(strcmp(class(regexp("abc", "(?<a>b)", "names").a), 'string'));
        % A char subject stays char whatever the pattern's kind.
        assert(isequal(regexp('a1', "\d", 'match'), {'1'}));
        """);

    [Fact]
    public Task ContainersOfSubjectsAndPatternsArePairedOff() => RunAsserting("""
        assert(isequal(regexp(["a1", "b2"], "\d", "match"), {"1", "2"}));
        assert(isequal(regexp(["a1", "b2"], "\d", "match", "once"), ["1" "2"]));
        assert(isequal(regexp({'a1', 'b2'}, '\d', 'match'), {{'1'}, {'2'}}));
        assert(isequal(regexp('a1', {'\d', '[a-z]'}, 'match'), {{'1'}, {'a'}}));
        assert(isequal(regexp('a1', {'\d'; '[a-z]'}, 'match', 'once'), {'1'; 'a'}));
        assert(isequal(regexp({'a1', 'b2'}, {'\d', '[a-z]'}, 'match'), {{'1'}, {'b'}}));
        assert(isequal(regexp({'a1', 'b2'}, {'\d'}, 'match'), {{'1'}, {'2'}}));
        ok = false;
        try
            regexp({'a1', 'b2', 'c3'}, {'\d', '[a-z]'}, 'match');
        catch
            ok = true;
        end
        assert(ok);
        assert(isequal(size(regexp({}, '\d', 'match')), [0 0]));
        """);

    [Fact]
    public Task OptionWordsAreReadInAnyCaseAndCheckedAgainstEachOther() => RunAsserting("""
        assert(isequal(regexp('abc', 'b', 'Match'), {'b'}));
        assert(isequal(regexp('abc', 'B', 'match', 'IgnoreCase'), {'b'}));
        assert(isequal(regexp('abc', 'b', 'forceCellOutput'), {2}));
        assert(isequal(regexp('abc', 'b', 'match', 'forceCellOutput'), {{'b'}}));
        assert(isequal(regexp('aXbXc', 'X', 'split', 'once'), {'a', 'bXc'}));
        errors = 0;
        try
            regexp('a', 'a', 'match', 'match');
        catch
            errors = errors + 1;
        end
        try
            regexp('a', 'a', 'match', 'start');
        catch
            errors = errors + 1;
        end
        try
            regexp('abc', 'b', 'ignorecase', 'matchcase');
        catch
            errors = errors + 1;
        end
        try
            regexprep('ABC', 'abc', 'x', 'preservecase', 'ignorecase');
        catch
            errors = errors + 1;
        end
        assert(errors == 4);
        """);

    [Fact]
    public Task MatlabOnlyPatternSyntaxIsTranslated() => RunAsserting("""
        assert(isequal(regexp('hello world', '\<w\w*', 'match'), {'world'}));
        assert(isequal(regexp('hello world', '\w+\>', 'match'), {'hello', 'world'}));
        % \b is a backspace in MATLAB, not a word boundary.
        assert(isempty(regexp('hello world', '\bw\w*', 'match')));
        assert(isequal(regexp('aAb', 'a\o{101}b', 'match'), {'aAb'}));
        assert(isequal(regexp('aAb', 'a\x{41}b', 'match'), {'aAb'}));
        % $ is the very end of the text; a trailing newline is not skipped over.
        assert(isempty(regexp(sprintf('a\n'), 'a$', 'match')));
        assert(isequal(regexp(sprintf('a\n'), 'a$', 'match', 'lineanchors'), {'a'}));
        assert(isequal(regexp(sprintf('l1\nl2'), '(?m)^\w+$', 'match'), {'l1', 'l2'}));
        """);

    // --- regexprep --------------------------------------------------------------------------------

    [Fact]
    public Task TheReplacementGrammarIsMatlabs() => RunAsserting("""
        assert(strcmp(regexprep('path/to', '/', '\\'), 'path\to'));
        assert(strcmp(regexprep('a b', '\s', '\n'), sprintf('a\nb')));
        assert(strcmp(regexprep('abc', 'b', '\$'), 'a$c'));
        assert(strcmp(regexprep('abc', '(b)', '\$1'), 'a$1c'));
        assert(strcmp(regexprep('abc', 'b', '\x41'), 'aAc'));
        assert(strcmp(regexprep('abc', 'b', '\101'), 'aAc'));
        assert(strcmp(regexprep('abc', 'b', '\q'), 'aqc'));
        assert(strcmp(regexprep('x', 'x', '$$'), '$$'));
        assert(strcmp(regexprep('abc', 'b', '$5'), 'a$5c'));
        assert(strcmp(regexprep('hello', '(l+)', '<$0>'), 'he<ll>o'));
        assert(strcmp(regexprep('hello', '(l+)', '$01'), 'hell1o'));
        assert(strcmp(regexprep('abc', '(b)', '$10'), 'ab0c'));
        assert(strcmp(regexprep('abcdefghijkl', '(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k)', '$10-$11-$1'), 'j-k-al'));
        assert(strcmp(regexprep('abc', '(?<x>b)', '[$<x>]'), 'a[b]c'));
        assert(strcmp(regexprep('aaa', 'a', 'b', 2), 'aba'));
        """);

    [Fact]
    public Task DynamicExpressionsRunWithTheTokensSplicedIn() => RunAsserting("""
        assert(strcmp(regexprep('hello', '^(.)', '${upper($1)}'), 'Hello'));
        assert(strcmp(regexprep('hello world', '(^|\s)(\w)', '$1${upper($2)}'), 'Hello World'));
        assert(strcmp(regexprep('snake_case', '_(\w)', '${upper($1)}'), 'snakeCase'));
        assert(strcmp(regexprep('3 4', '(\d)', '${num2str(str2num($1)*2)}'), '6 8'));
        assert(strcmp(regexprep('abc', '.', '${upper($0)}'), 'ABC'));
        assert(strcmp(regexprep('hello', '(?<first>h)', '${upper($<first>)}'), 'Hello'));
        assert(strcmp(regexprep('a''b', '('')', '${$1}'), 'a''b'));
        assert(strcmp(regexprep('abc', '(b)', '${upper($1)'), 'a${upper(b)c'));
        % The expression has to answer text.
        ok = false;
        try
            regexprep('abc', '(b)', '${length($1)}');
        catch err
            ok = ~isempty(strfind(err.message, 'did not produce a char vector or scalar string'));
        end
        assert(ok);
        """);

    [Fact]
    public Task PreserveCaseFollowsTheMeasuredRule() => RunAsserting("""
        assert(strcmp(regexprep('abc', 'ABC', 'xyz', 'preservecase'), 'xyz'));
        assert(strcmp(regexprep('Abc', 'abc', 'xyz', 'preservecase'), 'Xyz'));
        assert(strcmp(regexprep('ABC', 'abc', 'xyz', 'preservecase'), 'XYZ'));
        assert(strcmp(regexprep('ABc', 'abc', 'xyz', 'preservecase'), 'xyz'));
        assert(strcmp(regexprep('Hello WORLD', 'world', 'earth', 'preservecase'), 'Hello EARTH'));
        """);

    // --- strrep, count, replace, erase -------------------------------------------------------------

    [Fact]
    public Task StrrepReplacesOverlappingOccurrencesAndReplaceDoesNot() => RunAsserting("""
        assert(strcmp(strrep('aaa', 'aa', 'b'), 'bb'));
        assert(strcmp(strrep('abc 2 def 22 ghi 222 jkl 2222', '22', '*'), 'abc 2 def * ghi ** jkl ***'));
        assert(strcmp(strrep('ababa', 'aba', 'X'), 'XX'));
        assert(strcmp(replace('aaa', 'aa', 'b'), 'ba'));
        assert(strcmp(strrep('abc', '', 'X'), 'abc'));
        assert(isequal(strrep('abc', "b", "X"), "aXc"));
        assert(isequal(strrep('abc', {'a'}, 'x'), {'xbc'}));
        assert(isequal(strrep({'ab', 'ba'}, {'a', 'b'}, {'x', 'y'}), {'xb', 'ya'}));
        assert(count('aaa', 'aa') == 1);
        assert(count('abab', {'ab', 'ba'}) == 2);
        assert(count('abc', '') == 4);
        assert(strcmp(replace('abc', '', 'X'), 'XaXbXcX'));
        assert(strcmp(erase('abc', ''), 'abc'));
        assert(isequal(size(strfind('abc', 'x')), [0 0]));
        """);

    // --- the position verbs -------------------------------------------------------------------------

    [Fact]
    public Task ExtractAndInsertKeepTheKindOfTextAndActEverywhere() => RunAsserting("""
        assert(strcmp(class(extractAfter('abc', 'a')), 'char') && strcmp(extractAfter('abc', 'a'), 'bc'));
        assert(isequal(size(extractAfter('abc', 'x')), [0 0]));
        assert(ismissing(extractAfter("abc", "x")));
        assert(strcmp(extractBefore('abcabc', 'b'), 'a'));
        assert(strcmp(extractAfter('abc', 0), 'abc') && strcmp(extractBefore('abc', 4), 'abc'));
        assert(isequal(extractAfter({'ab', 'cb'}, {'a', 'c'}), {'b', 'b'}));
        assert(strcmp(insertAfter('abcb', 'b', 'X'), 'abXcbX'));
        assert(strcmp(insertBefore('abcb', 'b', 'X'), 'aXbcXb'));
        assert(strcmp(insertAfter('aaa', 'aa', 'X'), 'aaXa'));
        assert(strcmp(insertAfter('abc', '', 'X'), 'XaXbXcX'));
        assert(isequal(insertAfter("abc", 'a', 'X'), "aXbc"));
        errors = 0;
        try
            extractAfter('abc', 4);
        catch
            errors = errors + 1;
        end
        try
            extractBefore('abc', 0);
        catch
            errors = errors + 1;
        end
        try
            insertAfter('abc', 'a', 65);
        catch
            errors = errors + 1;
        end
        assert(errors == 3);
        """);

    [Fact]
    public Task ExtractBetweenAnswersEveryPieceAsAColumn() => RunAsserting("""
        assert(isequal(extractBetween('abcde', 'b', 'd'), {'c'}));
        assert(isequal(extractBetween('a<b>c<d>', '<', '>'), {'b'; 'd'}));
        assert(isequal(extractBetween("a<b>c<d>", "<", ">"), ["b"; "d"]));
        assert(isequal(size(extractBetween('abcde', 'x', 'd')), [0 1]));
        assert(isequal(size(extractBetween("abcde", "x", "d")), [0 1]));
        assert(isequal(extractBetween('abcbdbe', 'b', 'b'), {'c'}));
        assert(isequal(extractBetween('abcde', 2, 4), {'bcd'}));
        assert(isequal(extractBetween('abcde', 2, 4, 'Boundaries', 'exclusive'), {'c'}));
        assert(isequal(extractBetween('abcde', 'b', 'd', 'Boundaries', 'inclusive'), {'bcd'}));
        assert(isequal(extractBetween({'abc'; 'xbcx'}, 'b', 'c'), {''; ''}));
        ok = false;
        try
            extractBetween({'abcbc'; 'xbcx'}, 'b', 'c');
        catch
            ok = true;
        end
        assert(ok);
        """);
}
