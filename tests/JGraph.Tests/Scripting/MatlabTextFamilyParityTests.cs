using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The comparing, searching, trimming, padding, splitting, joining and ordering verbs against MATLAB
/// R2025b. Every expected value here was produced by running the same expression in MATLAB, not read
/// from its documentation.
/// </summary>
/// <remarks>
/// The rules these pin that the documentation leaves to inference: a missing string is equal to
/// nothing and comes out of every text verb still missing; a one-element container expands against
/// any other in <c>strcmp</c> and <c>strcat</c>; <c>strncmp</c> compares whole strings once n
/// exceeds a length; a cell of char takes exactly one argument in <c>sort</c>; <c>unique</c> of a row
/// cell answers a row; <c>join</c> of nothing is a missing string; a trailing delimiter before a line
/// break is only a delimiter to <c>textscan</c> unless the record is short.
/// </remarks>
[Collection("JG facade")]
public class MatlabTextFamilyParityTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabTextFamilyParityTests() => JG.Reset();

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

    // --- comparing --------------------------------------------------------------------------------

    [Fact]
    public Task StrcmpPairsContainersAndAnswersLogicals() => RunAsserting("""
        assert(isequal(strcmp({'a','b'}, {'a','c'}), [true false]));
        assert(islogical(strcmp({'a'}, {'a'})));
        assert(isequal(strcmp(["a" "b"], ["a" "c"]), [true false]));
        assert(isequal(strcmp({'a','b'}, {'a'}), [true false]));
        assert(isequal(strcmp({'a','b'}, ["a" "b"]), [true true]));
        assert(isequal(strcmp({'a', 1}, {'a', 1}), [true false]));
        assert(strcmp(['ab';'cd'], ['ab';'cd']));
        assert(~strcmp(['ab';'cd'], 'ab'));
        assert(~strcmp(1, 1));
        assert(~strcmp(string(missing), string(missing)));
        assert(isequal(size(strcmp({}, {})), [0 0]) && islogical(strcmp({}, {})));
        failed = false;
        try, strcmp({'a','b'}, {'a','b','c'}); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task StrncmpComparesWholeStringsPastTheirLength() => RunAsserting("""
        assert(strncmp('abc', 'abc', 5));
        assert(~strncmp('abc', 'abcd', 5));
        assert(~strncmp('abc', 'ab', 3));
        assert(strncmp('abc', 'ab', 2));
        assert(strncmp('', '', 1));
        assert(strncmp('abc', 'xyz', 0));
        assert(strncmp('abc', 'abd', 2.5));
        assert(isequal(strncmp({'abc','abd'}, {'abx','xyz'}, 2), [true false]));
        assert(isequal(strncmpi({'ABC'}, 'ab', 2), true));
        """);

    // --- searching --------------------------------------------------------------------------------

    [Fact]
    public Task SearchVerbsTakeIgnoreCase() => RunAsserting("""
        assert(contains('ABC', 'b', 'IgnoreCase', true));
        assert(~contains('ABC', 'b', 'IgnoreCase', false));
        assert(contains('ABC', 'b', 'Ignore', true));
        assert(isequal(contains({'ABC','xyz'}, 'b', 'IgnoreCase', true), [true false]));
        assert(startsWith('ABC', 'a', 'IgnoreCase', true));
        assert(endsWith('ABC', 'c', 'IgnoreCase', true));
        assert(matches('ABC', 'abc', 'IgnoreCase', true));
        assert(~matches("abc", "ab"));
        assert(count('AbA', 'a', 'IgnoreCase', true) == 2);
        assert(count("AbA", ["a" "b"], 'IgnoreCase', true) == 3);
        assert(~contains(string(missing), "a"));
        assert(~matches(string(missing), string(missing)));
        failed = false;
        try, contains('ABC', 'b', 'IgnoreCase', 'yes'); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task StrfindForcesCellsAndSearchesNumbers() => RunAsserting("""
        assert(isequal(strfind('abcabc', 'b', 'ForceCellOutput', true), {[2 5]}));
        assert(isequal(strfind('abc', 'x', 'ForceCellOutput', true), {[]}));
        assert(isequal(strfind({'abc','bcb'}, 'b'), {2, [1 3]}));
        assert(isequal(strfind(["abc" "bcb"], "b"), {2, [1 3]}));
        assert(strfind("abc", "b") == 2);
        assert(strfind('abc', 98) == 2);
        assert(strfind('abc', {'b'}) == 2);
        assert(isequal(strfind([1 2 3 2], 2), [2 4]));
        assert(strfind([1 2 3 2], [2 3]) == 2);
        assert(isequal(size(strfind('abc', 'x')), [0 0]));
        assert(isequal(size(strfind(5, 'b')), [0 0]));
        """);

    // --- the missing string -----------------------------------------------------------------------

    [Fact]
    public Task AMissingStringIsEqualToNothingAndStaysMissing() => RunAsserting("""
        assert(isnan(strlength(string(missing))));
        assert(isequaln(strlength(["a" missing "bcd"]), [1 NaN 3]));
        assert(~(string(missing) == string(missing)));
        assert(string(missing) ~= string(missing));
        assert(~(string(missing) < "a"));
        assert(~isequal(string(missing), string(missing)));
        assert(isequaln(string(missing), string(missing)));
        assert(~isequal(["a" missing], ["a" missing]));
        assert(ismissing(upper(string(missing))));
        assert(ismissing(strtrim(string(missing))));
        assert(ismissing(reverse(string(missing))));
        assert(ismissing(pad(string(missing), 3)));
        assert(ismissing(strip(string(missing))));
        assert(ismissing(strcat(string(missing), "a")));
        assert(ismissing(string(missing) + "a"));
        assert(ismissing(join(["a" missing])));
        assert(ismissing(extractAfter(string(missing), 1)));
        assert(ismissing(string(NaN)));
        assert(isequal(cellstr(["a" missing]), {'a', ''}));
        s = sort(["b" missing "a"]);
        assert(isequal(s(1:2), ["a" "b"]) && ismissing(s(3)));
        """);

    [Fact]
    public Task IsmissingReadsCellsCharsAndIndicators() => RunAsserting("""
        assert(isequal(ismissing({'a', ''}), [false true]));
        assert(isequal(ismissing({'a', NaN}), [false false]));
        assert(isequal(ismissing('abc'), [false false false]));
        assert(isequal(ismissing('abc', 'b'), [false true false]));
        assert(isequal(ismissing(["a" missing], "a"), [true false]));
        assert(isequal(ismissing([1 2 NaN], 2), [false true false]));
        assert(isequal(size(ismissing('')), [0 0]));
        assert(anymissing(["a" missing]));
        assert(~anymissing(["a" "b"]));
        assert(anymissing([1 NaN]));
        """);

    // --- converting -------------------------------------------------------------------------------

    [Fact]
    public Task TheConvertHelpersConvert() => RunAsserting("""
        assert(isstring(convertCharsToStrings('abc')));
        assert(isequal(convertCharsToStrings({'a';'b'}), ["a";"b"]));
        assert(convertCharsToStrings(['ab';'cd']) == "acbd");
        assert(convertCharsToStrings(5) == 5);
        assert(isequal(convertStringsToChars(["a" "b"]), {'a', 'b'}));
        assert(ischar(convertStringsToChars("abc")));
        assert(isequal(size(convertStringsToChars(string(missing))), [0 0]));
        assert(isequal(convertContainedStringsToChars({'a', "b", ["c" "d"]}), {'a', 'b', {'c', 'd'}}));
        s = convertContainedStringsToChars(struct('a', "b"));
        assert(ischar(s.a));
        [x, y] = convertCharsToStrings({'a'}, 'b');
        assert(isequal(x, "a") && y == "b");
        failed = false;
        try, convertCharsToStrings({'a'}, 'b'); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task CellstrKeepsShapeAndTrimsChar() => RunAsserting("""
        assert(isequal(size(cellstr(["a";"b"])), [2 1]));
        assert(isequal(size(cellstr(["a" "b"; "c" "d"])), [2 2]));
        assert(isequal(cellstr('a '), {'a'}));
        assert(isequal(size(cellstr(strings(0,0))), [0 0]));
        failed = false;
        try, cellstr({'a', 1}); catch, failed = true; end
        assert(failed);
        """);

    // --- editing ----------------------------------------------------------------------------------

    [Fact]
    public Task StrcatPairsContainersAndTrimsOnlyChar() => RunAsserting("""
        assert(isequal(strcat({'a','b'}, 'x'), {'ax', 'bx'}));
        assert(isequal(strcat({'a','b'}, {'c'}), {'ac', 'bc'}));
        assert(isequal(strcat({'a '}, 'b'), {'a b'}));
        assert(isequal(strcat('a ', {'b'}), {'ab'}));
        assert(strcat("a ", "b") == "a b");
        assert(strcat('a ', "b") == "ab");
        assert(isstring(strcat('a', "b")));
        assert(strcmp(strcat('a', 65), 'aA'));
        assert(strcmp(strcat('a', [72 105]), 'aHi'));
        assert(strcmp(strcat('a', []), 'a'));
        assert(isequal(strcat(['a';'b'], 'x'), ['ax';'bx']));
        assert(isequal(strcat(['ab';'cd'], {'x'}), {'abx';'cdx'}));
        assert(isequal(strcat(['ab';'cd'], "x"), ["abx";"cdx"]));
        assert(isequal(strcat({'a','b'}, 'x', {'y','z'}), {'axy', 'bxz'}));
        failed = false;
        try, strcat("a", 98); catch, failed = true; end
        assert(failed);
        failed = false;
        try, strcat({'a','b'}, {'c','d','e'}); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task PadAndStripTakeTheirCharacterAndSide() => RunAsserting("""
        assert(strcmp(pad('ab', 5, '*'), 'ab***'));
        assert(strcmp(pad('ab', 5, 'left', '*'), '***ab'));
        assert(strcmp(pad('ab', 5, 'both', '*'), '*ab**'));
        assert(strcmp(pad('ab', 5, 'LEFT'), '   ab'));
        assert(strcmp(pad('ab', '*'), 'ab'));
        assert(isequal(pad({'a','bbb'}), {'a  ', 'bbb'}));
        assert(isequal(pad({'a','bbb'}, 'left'), {'  a', 'bbb'}));
        assert(isequal(pad(["a" "bbb"], 4, "both", "-"), ["-a--" "bbb-"]));
        assert(strcmp(strip('xxaxx', 'x'), 'a'));
        assert(strcmp(strip('xxaxx', 'left', 'x'), 'axx'));
        assert(strcmp(strip('  a  ', 'BOTH'), 'a'));
        assert(isequal(strip(["xax" "xxb"], "left", "x"), ["ax" "b"]));
        assert(isequal(size(strip('xxxxx', 'x')), [0 0]));
        failed = false;
        try, pad('ab', 5, 42); catch, failed = true; end
        assert(failed);
        failed = false;
        try, strip('xxaxx', 'xx'); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task DeblankAndStrtrimKeepTheirContainers() => RunAsserting("""
        assert(deblank("a  ") == "a");
        assert(isequal(deblank(["a  " "b "]), ["a" "b"]));
        assert(isequal(deblank({'a  ', 'b '}), {'a', 'b'}));
        assert(isequal(deblank({'a ', 5}), {'a', 5}));
        assert(deblank(5) == 5);
        assert(isequal(deblank(['ab  ';'c   ']), ['ab';'c ']));
        assert(isequal(strtrim(['  ab';' c  ']), [' ab';'c  ']));
        assert(strcmp(deblank(char([97 0 0])), 'a'));
        assert(isequal(size(strtrim(char([0 97 0]))), [1 3]));
        assert(isequal(size(deblank({})), [0 0]));
        failed = false;
        try, strtrim(5); catch, failed = true; end
        assert(failed);
        """);

    // --- splitting and joining --------------------------------------------------------------------

    [Fact]
    public Task SplitTakesSeveralDelimitersAndADimension() => RunAsserting("""
        assert(isequal(split("a1b2c", ["1" "2"]), ["a";"b";"c"]));
        assert(isequal(split('a1b2c', {'1','2'}), {'a';'b';'c'}));
        assert(isequal(split("a  b", " "), ["a";"";"b"]));
        assert(isequal(split("a  b"), ["a";"b"]));
        assert(isequal(split("a b", " ", 2), ["a" "b"]));
        assert(isequal(split(["a b"; "c d"], " ", 1), ["a" "c";"b" "d"]));
        assert(isequal(split(["a b"; "c d"], " ", 2), ["a" "b";"c" "d"]));
        assert(isequal(split("abc", ""), ["";"a";"b";"c";""]));
        assert(split("", "") == "");
        [pieces, cuts] = split("a,b;c", [",", ";"]);
        assert(isequal(pieces, ["a";"b";"c"]) && isequal(cuts, [",";";"]));
        [~, none] = split("abc", ",");
        assert(isequal(size(none), [0 1]));
        failed = false;
        try, split("a b", " ", 0); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task JoinOfNothingIsMissingAndDelimitersMustFit() => RunAsserting("""
        assert(ismissing(join(strings(1,0))));
        assert(isequal(join(cell(1,0)), {''}));
        assert(isequal(size(join(strings(0,0))), [0 1]));
        assert(isequal(size(join(strings(3,0))), [3 1]) && all(ismissing(join(strings(3,0)))));
        assert(join(["a" "b" "c"], ["-" "+"]) == "a-b+c");
        assert(isequal(join(["a" "b"; "c" "d"], ["-"; "+"]), ["a-b";"c+d"]));
        assert(ismissing(join(["a" "b"], string(missing))));
        assert(isequal(join(["a" "b"], 45), ["a" "b"]));
        failed = false;
        try, join(["a" "b"], ["-" "+"]); catch, failed = true; end
        assert(failed);
        failed = false;
        try, join(5); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task StrjoinAndStrsplitReadTheirOptions() => RunAsserting("""
        assert(strcmp(strjoin({'a','b','c'}, {'-'}), 'a-b-c'));
        assert(strcmp(strjoin({'a','b'}, '\x'), 'axb'));
        assert(ismissing(strjoin(["a" missing])));
        assert(isequal(strsplit('a,,b', ',', 'Collapse', false), {'a', '', 'b'}));
        assert(isequal(strsplit('a1b', '\d', 'DelimiterType', 'r'), {'a', 'b'}));
        assert(isequal(strsplit('a', ''), {'a'}));
        assert(isequal(strsplit('a,b', {'', ','}), {'a', 'b'}));
        assert(isequal(strsplit('a.b', '\.'), {'a', 'b'}));
        assert(isequal(strsplit('a--b-c', {'-','--'}, 'CollapseDelimiters', false), {'a', '', 'b', 'c'}));
        [~, cuts] = strsplit('abc', ',');
        assert(isequal(size(cuts), [0 0]));
        [pieces, cuts] = strsplit("a,b", ",");
        assert(isequal(pieces, ["a" "b"]) && cuts == ",");
        failed = false;
        try, strjoin({'a', 1}); catch, failed = true; end
        assert(failed);
        """);

    // --- ordering and membership ------------------------------------------------------------------

    [Fact]
    public Task SortOrdersTextByCodeAndTakesOneArgumentForCells() => RunAsserting("""
        assert(isequal(sort({'b','a'}), {'a', 'b'}));
        assert(isequal(sort({'B','a','b'}), {'B', 'a', 'b'}));
        assert(isequal(sort({'b','a'; 'd','c'}), {'b','a'; 'd','c'}));
        assert(isequal(sort(["b" "a"], "descend"), ["b" "a"]));
        assert(isequal(sort(["b" "a"; "d" "c"], 2), ["a" "b";"c" "d"]));
        d = sort(["b" missing "a"], "descend");
        assert(ismissing(d(1)) && isequal(d(2:3), ["b" "a"]));
        f = sort(["b" missing "a"], "MissingPlacement", "first");
        assert(ismissing(f(1)) && isequal(f(2:3), ["a" "b"]));
        [~, i] = sort({'b','a','c'});
        assert(isequal(i, [2 1 3]));
        [~, i] = sort(["b" "a" "c"], "descend");
        assert(isequal(i, [3 1 2]));
        assert(~issorted({'b','a'}) && issorted({'a','b'}));
        assert(issorted(["b" "a"], "descend") && ~issorted('cba'));
        assert(max('abc') == 99 && min('abc') == 97);
        failed = false;
        try, sort({'b','a'}, 'descend'); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task SortrowsAndUniqueReadText() => RunAsserting("""
        assert(isequal(sortrows({'b',2;'a',1}), {'a',1;'b',2}));
        assert(isequal(sortrows({'b',2;'a',1}, -1), {'b',2;'a',1}));
        assert(isequal(sortrows(["b" "x";"a" "y"]), ["a" "y";"b" "x"]));
        assert(isequal(sortrows(["b";"a"], 'descend'), ["b";"a"]));
        [~, i] = sortrows({'b';'a'});
        assert(isequal(i, [2;1]));
        assert(isequal(unique({'b','a','b'}), {'a', 'b'}));
        assert(isequal(size(unique({'b';'a';'b'})), [2 1]));
        assert(isequal(unique({'b','a','b'}, 'stable'), {'b', 'a'}));
        assert(isequal(unique(["b" "a" "b"], "stable"), ["b" "a"]));
        assert(isequal(size(unique(["b" "a";"b" "c"])), [3 1]));
        assert(isequal(size(unique(["b" missing "a" missing])), [1 4]));
        assert(isequal(unique(["b" "a";"b" "a"], "rows"), ["b" "a"]));
        assert(isequal(size(unique(cell(1,0))), [1 0]));
        [~, ia, ic] = unique({'b','a','b'});
        assert(isequal(ia, [2;1]) && isequal(ic, [2;1;2]));
        """);

    [Fact]
    public Task IsmemberReadsTextTwoWays() => RunAsserting("""
        assert(ismember('b', {'a','b'}));
        assert(isequal(ismember({'a','x'}, {'a','b'}), [true false]));
        assert(isequal(ismember({'a';'x'}, {'a','b'}), [true;false]));
        assert(ismember('b', 'abc'));
        assert(isequal(ismember('bc', 'abc'), [true true]));
        assert(isequal(ismember('abc', 'b'), [false true false]));
        assert(ismember(98, 'abc') && ismember('a', 97));
        assert(isequal(ismember({'a','x'}, 'a'), [true false]));
        assert(isequal(ismember(["a" "x"], {'a','b'}), [true false]));
        assert(~ismember(string(missing), ["a" missing]));
        assert(ismember({''}, {''}));
        assert(isequal(size(ismember({}, {'a'})), [0 0]));
        [tf, loc] = ismember({'a','x'}, {'a','b','a'});
        assert(isequal(tf, [true false]) && isequal(loc, [1 0]));
        [~, loc] = ismember('b', 'abc');
        assert(loc == 2);
        assert(isequal(ismember(['ab';'cd'], 'cd', 'rows'), [false;true]));
        failed = false;
        try, ismember(1, {'a'}); catch, failed = true; end
        assert(failed);
        """);

    // --- operators and shapes ---------------------------------------------------------------------

    [Fact]
    public Task StringsCompareAndConcatenateLikeMatlab() => RunAsserting("""
        assert("a" < "b" && "b" > "a");
        assert(("abc" + pi) == "abc3.1416");
        assert(isequal(["a" "b"] + ["1"; "2"], ["a1" "b1"; "a2" "b2"]));
        assert((1 + "abc") == "1abc");
        assert(("abc" + {'d'}) == "abcd");
        assert(isequal(size({'a','b'}'), [2 1]));
        assert(isequal("abc"', "abc"));
        assert(fliplr("abc") == "abc" && isstring(fliplr("abc")));
        assert(isequal(fliplr(["a" "b" "c"]), ["c" "b" "a"]));
        assert(isequal(size(string({})), [0 0]));
        assert(strcmp(mat2str(int8([1 2])), '[1 2]'));
        assert(strcmp(mat2str(int8([1 2]), 'class'), 'int8([1 2])'));
        assert(isequal(size(repmat('a', -1, 1)), [0 1]));
        """);

    // --- compose and textscan ---------------------------------------------------------------------

    [Fact]
    public Task ComposeAnswersOneStringPerGroupOfValues() => RunAsserting("""
        assert(isequal(compose('%d', [1 2 3]), {'1', '2', '3'}));
        assert(isequal(compose('%d,', [1 2 3]), {'1,', '2,', '3,'}));
        assert(isequal(compose('%d %d', [1 2 3]), {'1 2', '3 %d'}));
        assert(isequal(compose('%s', ["a" "b"]), {'a', 'b'}));
        assert(isequal(compose('%5.1f|', [1.25 22.5]), {'  1.2|', ' 22.5|'}));
        assert(isequal(size(compose('%d', [])), [0 0]));
        assert(isequal(size(compose('%d', magic(3))), [3 3]));
        assert(isequal(compose("%d-%d", [1 2; 3 4]), ["1-2"; "3-4"]));
        failed = false;
        try, compose('%s', {'a', 'b'}); catch, failed = true; end
        assert(failed);
        failed = false;
        try, compose('%d', "5"); catch, failed = true; end
        assert(failed);
        """);

    [Fact]
    public Task TextscanReadsEmptyFieldsAndItsOptions() => RunAsserting("""
        c = textscan('1,,3', '%f', 'Delimiter', ','); assert(isequaln(c{1}, [1;NaN;3]));
        c = textscan('1,,3', '%f', 'Delimiter', ',', 'EmptyValue', -1); assert(isequal(c{1}, [1;-1;3]));
        c = textscan('1,,3', '%d', 'Delimiter', ','); assert(isequal(c{1}, int32([1;0;3])));
        c = textscan(',1,2', '%f', 'Delimiter', ','); assert(isequaln(c{1}, [NaN;1;2]));
        c = textscan('1,2,', '%f', 'Delimiter', ','); assert(isequal(c{1}, [1;2]));
        c = textscan(sprintf('1,2\n3,\n'), '%f %f', 'Delimiter', ','); assert(isequaln(c{2}, [2;NaN]));
        c = textscan(sprintf('1,\n,2'), '%f', 'Delimiter', ','); assert(isequaln(c{1}, [1;NaN;2]));
        c = textscan('1,,3', '%f', 'Delimiter', ',', 'MultipleDelimsAsOne', true); assert(isequal(c{1}, [1;3]));
        c = textscan('Inf NaN', '%f'); assert(isequaln(c{1}, [Inf;NaN]));
        c = textscan('abc', '%c'); assert(ischar(c{1}) && isequal(size(c{1}), [3 1]));
        c = textscan('abcdef', '%2c'); assert(isequal(c{1}, ['ab';'cd';'ef']));
        c = textscan('300 2 3', '%d8'); assert(isa(c{1}, 'int8') && isequal(c{1}, int8([127;2;3])));
        c = textscan('-1 2', '%u8'); assert(isequal(c{1}, uint8([0;2])));
        c = textscan('1.5 2', '%d'); assert(isequal(c{1}, int32([2;2])));
        c = textscan('1 2 3', '%f32'); assert(isa(c{1}, 'single'));
        c = textscan(sprintf('1,2;3,4'), '%f %f', 'Delimiter', ',', 'EndOfLine', ';'); assert(isequal(c{1}, [1;3]) && isequal(c{2}, [2;4]));
        c = textscan(sprintf('1 2 %% c\n3 4'), '%f %f', 'CommentStyle', '%'); assert(isequal(c{2}, [2;4]));
        c = textscan('1 2 /* c */ 3 4', '%f %f', 'CommentStyle', {'/*', '*/'}); assert(isequal(c{1}, [1;3]));
        c = textscan('1 NA 3', '%f', 'TreatAsEmpty', 'NA'); assert(isequaln(c{1}, [1;NaN;3]));
        c = textscan('1 x 3', '%f'); assert(isequal(c{1}, 1));
        c = textscan('1 2 3', '%f', 0); assert(isequal(size(c{1}), [0 1]));
        c = textscan('1 2 3', '%f', -1); assert(isequal(c{1}, [1;2;3]));
        c = textscan('1 2 3', '%*f'); assert(isequal(size(c), [1 0]));
        c = textscan('a b,c', '%s', 'Delimiter', ','); assert(isequal(c{1}, {'a b';'c'}));
        c = textscan('1 a 2 b', '%s %s', 'CollectOutput', 1); assert(isequal(c{1}, {'1', 'a'; '2', 'b'}));
        c = textscan('1 2 3 4', '%d %f', 'CollectOutput', 1); assert(numel(c) == 2);
        c = textscan('1.234', '%.2f'); assert(isequal(c{1}, [1.23;4]));
        c = textscan('1;2,3', '%f', 'Delimiter', ',;'); assert(isequal(c{1}, [1;2;3]));
        failed = false;
        try, textscan('1 x 3', '%f', 'ReturnOnError', false); catch, failed = true; end
        assert(failed);
        """);
}
